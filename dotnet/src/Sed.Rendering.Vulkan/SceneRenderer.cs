using System.Reflection;
using System.Runtime.InteropServices;
using Sed.Core.Math;
using Sed.Rendering;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace Sed.Rendering.Vulkan;

/// <summary>
/// Renders an indexed <see cref="Mesh"/> with a perspective MVP and depth test
/// to an offscreen image, reading the result back as RGBA8. Targets are
/// (re)created on size change; the pipeline uses dynamic viewport/scissor so a
/// resize does not rebuild it. Pixels feed the Avalonia viewport bitmap.
/// </summary>
public sealed unsafe class SceneRenderer : IDisposable
{
    public const Format ColorFormat = Format.R8G8B8A8Unorm;
    public const Format DepthFormat = Format.D32Sfloat;

    private readonly VulkanDevice _dev;
    private readonly Vk _vk;

    private RenderPass _renderPass;
    private PipelineLayout _layout;
    private Pipeline _pipeline;

    // Geometry
    private VkBuffer _vertexBuffer;
    private DeviceMemory _vertexMemory;
    private VkBuffer _indexBuffer;
    private DeviceMemory _indexMemory;
    private uint _indexCount;

    // Size-dependent targets
    private uint _width, _height;
    private Image _color, _depth;
    private DeviceMemory _colorMem, _depthMem;
    private ImageView _colorView, _depthView;
    private Framebuffer _framebuffer;
    private VkBuffer _readback;
    private DeviceMemory _readbackMem;

    public SceneRenderer(VulkanDevice device)
    {
        _dev = device;
        _vk = device.Vk;
        CreateRenderPass();
        CreatePipeline();
    }

    public void SetMesh(Mesh mesh)
    {
        DestroyGeometry();
        if (mesh.IsEmpty) { _indexCount = 0; return; }

        var verts = mesh.Vertices.ToArray();
        var indices = mesh.Indices.ToArray();
        _indexCount = (uint)indices.Length;

        (_vertexBuffer, _vertexMemory) = CreateHostBuffer(
            (ulong)(verts.Length * (int)MeshVertex.Stride), BufferUsageFlags.VertexBufferBit);
        Upload(_vertexMemory, MemoryMarshal.AsBytes<MeshVertex>(verts));

        (_indexBuffer, _indexMemory) = CreateHostBuffer(
            (ulong)(indices.Length * sizeof(uint)), BufferUsageFlags.IndexBufferBit);
        Upload(_indexMemory, MemoryMarshal.AsBytes<uint>(indices));
    }

    /// <summary>Renders one frame and returns tightly-packed RGBA8 pixels.</summary>
    public byte[] Render(Mat4 viewProjection, uint width, uint height,
        float clearR = 0.05f, float clearG = 0.05f, float clearB = 0.08f)
    {
        EnsureTargets(width, height);

        var cmd = BeginOneTimeCommands();

        var clears = stackalloc ClearValue[2];
        clears[0] = new ClearValue { Color = new ClearColorValue(clearR, clearG, clearB, 1f) };
        clears[1] = new ClearValue { DepthStencil = new ClearDepthStencilValue(1f, 0) };

        var rpBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(_width, _height)),
            ClearValueCount = 2,
            PClearValues = clears,
        };
        _vk.CmdBeginRenderPass(cmd, in rpBegin, SubpassContents.Inline);

        var viewport = new Viewport(0, 0, _width, _height, 0, 1);
        var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(_width, _height));
        _vk.CmdSetViewport(cmd, 0, 1, in viewport);
        _vk.CmdSetScissor(cmd, 0, 1, in scissor);

        if (_indexCount > 0)
        {
            _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline);

            fixed (float* mvp = viewProjection.M)
                _vk.CmdPushConstants(cmd, _layout, ShaderStageFlags.VertexBit, 0, 64, mvp);

            var vb = _vertexBuffer;
            ulong offset = 0;
            _vk.CmdBindVertexBuffers(cmd, 0, 1, in vb, in offset);
            _vk.CmdBindIndexBuffer(cmd, _indexBuffer, 0, IndexType.Uint32);
            _vk.CmdDrawIndexed(cmd, _indexCount, 1, 0, 0, 0);
        }

        _vk.CmdEndRenderPass(cmd);

        var region = new BufferImageCopy
        {
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit, MipLevel = 0, BaseArrayLayer = 0, LayerCount = 1,
            },
            ImageExtent = new Extent3D(_width, _height, 1),
        };
        _vk.CmdCopyImageToBuffer(cmd, _color, ImageLayout.TransferSrcOptimal, _readback, 1, &region);

        EndSubmitAndWait(cmd);

        int size = (int)(_width * _height * 4);
        var pixels = new byte[size];
        void* mapped;
        _vk.MapMemory(_dev.Device, _readbackMem, 0, (ulong)size, 0, &mapped);
        new Span<byte>(mapped, size).CopyTo(pixels);
        _vk.UnmapMemory(_dev.Device, _readbackMem);
        return pixels;
    }

    // ---- setup ----

    private void CreateRenderPass()
    {
        var attachments = stackalloc AttachmentDescription[2];
        attachments[0] = new AttachmentDescription
        {
            Format = ColorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.TransferSrcOptimal,
        };
        attachments[1] = new AttachmentDescription
        {
            Format = DepthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
        };

        var colorRef = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
        var depthRef = new AttachmentReference(1, ImageLayout.DepthStencilAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
            PDepthStencilAttachment = &depthRef,
        };
        var dependency = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.TransferBit,
            DstAccessMask = AccessFlags.TransferReadBit,
        };
        var rpInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency,
        };
        if (_vk.CreateRenderPass(_dev.Device, in rpInfo, null, out _renderPass) != Result.Success)
            throw new VulkanException("vkCreateRenderPass failed");
    }

    private void CreatePipeline()
    {
        var vert = LoadShaderModule("mesh.vert.spv");
        var frag = LoadShaderModule("mesh.frag.spv");
        var entry = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit, Module = vert, PName = entry,
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit, Module = frag, PName = entry,
            };

            var binding = new VertexInputBindingDescription
            {
                Binding = 0, Stride = MeshVertex.Stride, InputRate = VertexInputRate.Vertex,
            };
            var attrs = stackalloc VertexInputAttributeDescription[3];
            attrs[0] = new VertexInputAttributeDescription(0, 0, Format.R32G32B32Sfloat, 0);
            attrs[1] = new VertexInputAttributeDescription(1, 0, Format.R32G32B32Sfloat, 12);
            attrs[2] = new VertexInputAttributeDescription(2, 0, Format.R32G32B32Sfloat, 24);
            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &binding,
                VertexAttributeDescriptionCount = 3,
                PVertexAttributeDescriptions = attrs,
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList,
            };
            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1, ScissorCount = 1,
            };
            var raster = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise,
                LineWidth = 1f,
            };
            var multisample = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit,
            };
            var depthStencil = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true,
                DepthWriteEnable = true,
                DepthCompareOp = CompareOp.Less,
            };
            var blendAttachment = new PipelineColorBlendAttachmentState
            {
                BlendEnable = false,
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                 ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            };
            var blend = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1, PAttachments = &blendAttachment,
            };

            var dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
            var dynamic = new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2, PDynamicStates = dynamicStates,
            };

            var pushRange = new PushConstantRange(ShaderStageFlags.VertexBit, 0, 64);
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                PushConstantRangeCount = 1, PPushConstantRanges = &pushRange,
            };
            if (_vk.CreatePipelineLayout(_dev.Device, in layoutInfo, null, out _layout) != Result.Success)
                throw new VulkanException("vkCreatePipelineLayout failed");

            var info = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2, PStages = stages,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &raster,
                PMultisampleState = &multisample,
                PDepthStencilState = &depthStencil,
                PColorBlendState = &blend,
                PDynamicState = &dynamic,
                Layout = _layout,
                RenderPass = _renderPass,
                Subpass = 0,
            };
            if (_vk.CreateGraphicsPipelines(_dev.Device, default, 1, in info, null, out _pipeline) != Result.Success)
                throw new VulkanException("vkCreateGraphicsPipelines failed");
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
            _vk.DestroyShaderModule(_dev.Device, vert, null);
            _vk.DestroyShaderModule(_dev.Device, frag, null);
        }
    }

    private void EnsureTargets(uint width, uint height)
    {
        if (width == _width && height == _height && _framebuffer.Handle != 0)
            return;

        DestroyTargets();
        _width = width;
        _height = height;

        (_color, _colorMem, _colorView) = CreateImage(ColorFormat,
            ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit, ImageAspectFlags.ColorBit);
        (_depth, _depthMem, _depthView) = CreateImage(DepthFormat,
            ImageUsageFlags.DepthStencilAttachmentBit, ImageAspectFlags.DepthBit);

        var views = stackalloc ImageView[2] { _colorView, _depthView };
        var fbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _renderPass,
            AttachmentCount = 2, PAttachments = views,
            Width = _width, Height = _height, Layers = 1,
        };
        if (_vk.CreateFramebuffer(_dev.Device, in fbInfo, null, out _framebuffer) != Result.Success)
            throw new VulkanException("vkCreateFramebuffer failed");

        (_readback, _readbackMem) = CreateHostBuffer((ulong)(_width * _height * 4), BufferUsageFlags.TransferDstBit);
    }

    private (Image, DeviceMemory, ImageView) CreateImage(Format format, ImageUsageFlags usage, ImageAspectFlags aspect)
    {
        var info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new Extent3D(_width, _height, 1),
            MipLevels = 1, ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        if (_vk.CreateImage(_dev.Device, in info, null, out var image) != Result.Success)
            throw new VulkanException("vkCreateImage failed");

        _vk.GetImageMemoryRequirements(_dev.Device, image, out var reqs);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = reqs.Size,
            MemoryTypeIndex = _dev.FindMemoryType(reqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };
        if (_vk.AllocateMemory(_dev.Device, in alloc, null, out var memory) != Result.Success)
            throw new VulkanException("vkAllocateMemory (image) failed");
        _vk.BindImageMemory(_dev.Device, image, memory, 0);

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image, ViewType = ImageViewType.Type2D, Format = format,
            SubresourceRange = new ImageSubresourceRange(aspect, 0, 1, 0, 1),
        };
        if (_vk.CreateImageView(_dev.Device, in viewInfo, null, out var view) != Result.Success)
            throw new VulkanException("vkCreateImageView failed");

        return (image, memory, view);
    }

    private (VkBuffer, DeviceMemory) CreateHostBuffer(ulong size, BufferUsageFlags usage)
    {
        var info = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size, Usage = usage, SharingMode = SharingMode.Exclusive,
        };
        if (_vk.CreateBuffer(_dev.Device, in info, null, out var buffer) != Result.Success)
            throw new VulkanException("vkCreateBuffer failed");

        _vk.GetBufferMemoryRequirements(_dev.Device, buffer, out var reqs);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = reqs.Size,
            MemoryTypeIndex = _dev.FindMemoryType(reqs.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit),
        };
        if (_vk.AllocateMemory(_dev.Device, in alloc, null, out var memory) != Result.Success)
            throw new VulkanException("vkAllocateMemory (buffer) failed");
        _vk.BindBufferMemory(_dev.Device, buffer, memory, 0);
        return (buffer, memory);
    }

    private void Upload(DeviceMemory memory, ReadOnlySpan<byte> data)
    {
        void* mapped;
        _vk.MapMemory(_dev.Device, memory, 0, (ulong)data.Length, 0, &mapped);
        data.CopyTo(new Span<byte>(mapped, data.Length));
        _vk.UnmapMemory(_dev.Device, memory);
    }

    private ShaderModule LoadShaderModule(string nameSuffix)
    {
        var asm = typeof(SceneRenderer).GetTypeInfo().Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(nameSuffix, StringComparison.Ordinal))
            ?? throw new VulkanException($"Embedded shader '{nameSuffix}' not found");
        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var code = ms.ToArray();
        fixed (byte* pCode = code)
        {
            var info = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length, PCode = (uint*)pCode,
            };
            if (_vk.CreateShaderModule(_dev.Device, in info, null, out var module) != Result.Success)
                throw new VulkanException($"vkCreateShaderModule failed for {nameSuffix}");
            return module;
        }
    }

    private CommandBuffer BeginOneTimeCommands()
    {
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _dev.CommandPool, Level = CommandBufferLevel.Primary, CommandBufferCount = 1,
        };
        _vk.AllocateCommandBuffers(_dev.Device, in allocInfo, out var cmd);
        var begin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        _vk.BeginCommandBuffer(cmd, in begin);
        return cmd;
    }

    private void EndSubmitAndWait(CommandBuffer cmd)
    {
        _vk.EndCommandBuffer(cmd);
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        _vk.CreateFence(_dev.Device, in fenceInfo, null, out var fence);
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &cmd,
        };
        if (_vk.QueueSubmit(_dev.GraphicsQueue, 1, in submit, fence) != Result.Success)
            throw new VulkanException("vkQueueSubmit failed");
        _vk.WaitForFences(_dev.Device, 1, in fence, true, ulong.MaxValue);
        _vk.DestroyFence(_dev.Device, fence, null);
        _vk.FreeCommandBuffers(_dev.Device, _dev.CommandPool, 1, in cmd);
    }

    private void DestroyGeometry()
    {
        var d = _dev.Device;
        if (_vertexBuffer.Handle != 0) { _vk.DestroyBuffer(d, _vertexBuffer, null); _vertexBuffer = default; }
        if (_vertexMemory.Handle != 0) { _vk.FreeMemory(d, _vertexMemory, null); _vertexMemory = default; }
        if (_indexBuffer.Handle != 0) { _vk.DestroyBuffer(d, _indexBuffer, null); _indexBuffer = default; }
        if (_indexMemory.Handle != 0) { _vk.FreeMemory(d, _indexMemory, null); _indexMemory = default; }
    }

    private void DestroyTargets()
    {
        var d = _dev.Device;
        if (_readback.Handle != 0) { _vk.DestroyBuffer(d, _readback, null); _readback = default; }
        if (_readbackMem.Handle != 0) { _vk.FreeMemory(d, _readbackMem, null); _readbackMem = default; }
        if (_framebuffer.Handle != 0) { _vk.DestroyFramebuffer(d, _framebuffer, null); _framebuffer = default; }
        if (_colorView.Handle != 0) { _vk.DestroyImageView(d, _colorView, null); _colorView = default; }
        if (_depthView.Handle != 0) { _vk.DestroyImageView(d, _depthView, null); _depthView = default; }
        if (_color.Handle != 0) { _vk.DestroyImage(d, _color, null); _color = default; }
        if (_depth.Handle != 0) { _vk.DestroyImage(d, _depth, null); _depth = default; }
        if (_colorMem.Handle != 0) { _vk.FreeMemory(d, _colorMem, null); _colorMem = default; }
        if (_depthMem.Handle != 0) { _vk.FreeMemory(d, _depthMem, null); _depthMem = default; }
    }

    public void Dispose()
    {
        DestroyGeometry();
        DestroyTargets();
        var d = _dev.Device;
        if (_pipeline.Handle != 0) _vk.DestroyPipeline(d, _pipeline, null);
        if (_layout.Handle != 0) _vk.DestroyPipelineLayout(d, _layout, null);
        if (_renderPass.Handle != 0) _vk.DestroyRenderPass(d, _renderPass, null);
    }
}
