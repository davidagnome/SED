using System.Reflection;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Sed.Rendering.Vulkan;

/// <summary>
/// Renders to an offscreen RGBA8 image and reads the pixels back to the CPU.
/// This is both the renderer bring-up milestone (proves device → render pass →
/// pipeline → draw → present-to-memory works on MoltenVK) and the mechanism the
/// editor will use to display the Vulkan viewport inside an Avalonia bitmap.
/// </summary>
public sealed unsafe class OffscreenRenderer : IDisposable
{
    public const Format ColorFormat = Format.R8G8B8A8Unorm;

    private readonly VulkanDevice _dev;
    private readonly Vk _vk;
    private readonly uint _width;
    private readonly uint _height;

    private Image _image;
    private DeviceMemory _imageMemory;
    private ImageView _imageView;
    private RenderPass _renderPass;
    private Framebuffer _framebuffer;
    private PipelineLayout _pipelineLayout;
    private Pipeline _pipeline;
    private Silk.NET.Vulkan.Buffer _readback;
    private DeviceMemory _readbackMemory;

    public OffscreenRenderer(VulkanDevice device, uint width, uint height)
    {
        _dev = device;
        _vk = device.Vk;
        _width = width;
        _height = height;

        CreateColorImage();
        CreateRenderPass();
        CreateFramebuffer();
        CreatePipeline();
        CreateReadbackBuffer();
    }

    /// <summary>Renders one frame and returns tightly-packed RGBA8 pixels (row-major, top-down).</summary>
    public byte[] RenderToPixels(float clearR = 0.05f, float clearG = 0.05f, float clearB = 0.08f)
    {
        var cmd = BeginOneTimeCommands();

        var clear = new ClearValue { Color = new ClearColorValue(clearR, clearG, clearB, 1f) };
        var renderArea = new Rect2D { Offset = new Offset2D(0, 0), Extent = new Extent2D(_width, _height) };
        var rpBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _framebuffer,
            RenderArea = renderArea,
            ClearValueCount = 1,
            PClearValues = &clear,
        };

        _vk.CmdBeginRenderPass(cmd, in rpBegin, SubpassContents.Inline);
        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline);
        _vk.CmdDraw(cmd, 3, 1, 0, 0);
        _vk.CmdEndRenderPass(cmd);

        // Image is now TransferSrcOptimal (render-pass final layout); copy to buffer.
        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(_width, _height, 1),
        };
        _vk.CmdCopyImageToBuffer(cmd, _image, ImageLayout.TransferSrcOptimal, _readback, 1, &region);

        EndSubmitAndWait(cmd);

        var size = (int)(_width * _height * 4);
        var pixels = new byte[size];
        void* mapped;
        _vk.MapMemory(_dev.Device, _readbackMemory, 0, (ulong)size, 0, &mapped);
        new Span<byte>(mapped, size).CopyTo(pixels);
        _vk.UnmapMemory(_dev.Device, _readbackMemory);
        return pixels;
    }

    private void CreateColorImage()
    {
        var info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = ColorFormat,
            Extent = new Extent3D(_width, _height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        if (_vk.CreateImage(_dev.Device, in info, null, out _image) != Result.Success)
            throw new VulkanException("vkCreateImage failed");

        _vk.GetImageMemoryRequirements(_dev.Device, _image, out var reqs);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = reqs.Size,
            MemoryTypeIndex = _dev.FindMemoryType(reqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };
        if (_vk.AllocateMemory(_dev.Device, in alloc, null, out _imageMemory) != Result.Success)
            throw new VulkanException("vkAllocateMemory (image) failed");
        _vk.BindImageMemory(_dev.Device, _image, _imageMemory, 0);

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _image,
            ViewType = ImageViewType.Type2D,
            Format = ColorFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        if (_vk.CreateImageView(_dev.Device, in viewInfo, null, out _imageView) != Result.Success)
            throw new VulkanException("vkCreateImageView failed");
    }

    private void CreateRenderPass()
    {
        var colorAttachment = new AttachmentDescription
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
        var colorRef = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
        };
        // Ensure the color writes finish (and layout transition completes) before the copy.
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
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency,
        };
        if (_vk.CreateRenderPass(_dev.Device, in rpInfo, null, out _renderPass) != Result.Success)
            throw new VulkanException("vkCreateRenderPass failed");
    }

    private void CreateFramebuffer()
    {
        var view = _imageView;
        var info = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _renderPass,
            AttachmentCount = 1,
            PAttachments = &view,
            Width = _width,
            Height = _height,
            Layers = 1,
        };
        if (_vk.CreateFramebuffer(_dev.Device, in info, null, out _framebuffer) != Result.Success)
            throw new VulkanException("vkCreateFramebuffer failed");
    }

    private void CreatePipeline()
    {
        var vert = LoadShaderModule("triangle.vert.spv");
        var frag = LoadShaderModule("triangle.frag.spv");
        var entryPoint = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stages = stackalloc PipelineShaderStageCreateInfo[2];
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = vert,
                PName = entryPoint,
            };
            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = frag,
                PName = entryPoint,
            };

            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
            };
            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList,
            };

            var viewport = new Viewport(0, 0, _width, _height, 0, 1);
            var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(_width, _height));
            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                PViewports = &viewport,
                ScissorCount = 1,
                PScissors = &scissor,
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
            var blendAttachment = new PipelineColorBlendAttachmentState
            {
                BlendEnable = false,
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                 ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            };
            var blend = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &blendAttachment,
            };

            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
            };
            if (_vk.CreatePipelineLayout(_dev.Device, in layoutInfo, null, out _pipelineLayout) != Result.Success)
                throw new VulkanException("vkCreatePipelineLayout failed");

            var pipelineInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = stages,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &raster,
                PMultisampleState = &multisample,
                PColorBlendState = &blend,
                Layout = _pipelineLayout,
                RenderPass = _renderPass,
                Subpass = 0,
            };
            if (_vk.CreateGraphicsPipelines(_dev.Device, default, 1, in pipelineInfo, null, out _pipeline) != Result.Success)
                throw new VulkanException("vkCreateGraphicsPipelines failed");
        }
        finally
        {
            SilkMarshal.Free((nint)entryPoint);
            _vk.DestroyShaderModule(_dev.Device, vert, null);
            _vk.DestroyShaderModule(_dev.Device, frag, null);
        }
    }

    private void CreateReadbackBuffer()
    {
        ulong size = (ulong)(_width * _height * 4);
        var info = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
        };
        if (_vk.CreateBuffer(_dev.Device, in info, null, out _readback) != Result.Success)
            throw new VulkanException("vkCreateBuffer failed");

        _vk.GetBufferMemoryRequirements(_dev.Device, _readback, out var reqs);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = reqs.Size,
            MemoryTypeIndex = _dev.FindMemoryType(reqs.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit),
        };
        if (_vk.AllocateMemory(_dev.Device, in alloc, null, out _readbackMemory) != Result.Success)
            throw new VulkanException("vkAllocateMemory (readback) failed");
        _vk.BindBufferMemory(_dev.Device, _readback, _readbackMemory, 0);
    }

    private ShaderModule LoadShaderModule(string nameSuffix)
    {
        var asm = typeof(OffscreenRenderer).GetTypeInfo().Assembly;
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
                CodeSize = (nuint)code.Length,
                PCode = (uint*)pCode,
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
            CommandPool = _dev.CommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        _vk.AllocateCommandBuffers(_dev.Device, in allocInfo, out var cmd);

        var begin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
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
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd,
        };
        if (_vk.QueueSubmit(_dev.GraphicsQueue, 1, in submit, fence) != Result.Success)
            throw new VulkanException("vkQueueSubmit failed");
        _vk.WaitForFences(_dev.Device, 1, in fence, true, ulong.MaxValue);

        _vk.DestroyFence(_dev.Device, fence, null);
        _vk.FreeCommandBuffers(_dev.Device, _dev.CommandPool, 1, in cmd);
    }

    public void Dispose()
    {
        var d = _dev.Device;
        if (_readback.Handle != 0) _vk.DestroyBuffer(d, _readback, null);
        if (_readbackMemory.Handle != 0) _vk.FreeMemory(d, _readbackMemory, null);
        if (_pipeline.Handle != 0) _vk.DestroyPipeline(d, _pipeline, null);
        if (_pipelineLayout.Handle != 0) _vk.DestroyPipelineLayout(d, _pipelineLayout, null);
        if (_framebuffer.Handle != 0) _vk.DestroyFramebuffer(d, _framebuffer, null);
        if (_renderPass.Handle != 0) _vk.DestroyRenderPass(d, _renderPass, null);
        if (_imageView.Handle != 0) _vk.DestroyImageView(d, _imageView, null);
        if (_image.Handle != 0) _vk.DestroyImage(d, _image, null);
        if (_imageMemory.Handle != 0) _vk.FreeMemory(d, _imageMemory, null);
    }
}
