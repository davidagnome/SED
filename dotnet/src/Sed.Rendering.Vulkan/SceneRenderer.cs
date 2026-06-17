using System.Reflection;
using System.Runtime.InteropServices;
using Sed.Core.Math;
using Sed.Rendering;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace Sed.Rendering.Vulkan;

/// <summary>
/// Renders a textured, depth-tested <see cref="RenderScene"/> to an offscreen
/// image and reads it back as RGBA8. Each submesh's material is uploaded as a
/// sampled texture and bound via a descriptor set; the fragment shader modulates
/// it by the per-vertex light intensity. Falls back to a white texture for
/// missing materials. Targets are recreated on size change.
/// </summary>
public sealed unsafe class SceneRenderer : IDisposable
{
    public const Format ColorFormat = Format.R8G8B8A8Unorm;
    public const Format DepthFormat = Format.D32Sfloat;

    private readonly VulkanDevice _dev;
    private readonly Vk _vk;

    private RenderPass _renderPass;
    private DescriptorSetLayout _setLayout;
    private Sampler _sampler;
    private PipelineLayout _layout;
    private Pipeline _pipeline;
    private Pipeline _selectionPipeline;   // depth-test off: highlight always visible

    // Selection overlay geometry
    private VkBuffer _selVertexBuffer;
    private DeviceMemory _selVertexMemory;
    private VkBuffer _selIndexBuffer;
    private DeviceMemory _selIndexMemory;
    private uint _selIndexCount;

    // Thing markers (depth-tested, white texture, vertex-colored)
    private VkBuffer _markerVertexBuffer;
    private DeviceMemory _markerVertexMemory;
    private VkBuffer _markerIndexBuffer;
    private DeviceMemory _markerIndexMemory;
    private uint _markerIndexCount;

    // Persistent 1x1 white fallback texture.
    private GpuTexture _white;

    // Geometry
    private VkBuffer _vertexBuffer;
    private DeviceMemory _vertexMemory;
    private VkBuffer _indexBuffer;
    private DeviceMemory _indexMemory;

    // Per-scene resources
    private DescriptorPool _descPool;
    private readonly List<GpuTexture> _sceneTextures = new();
    private readonly List<DrawItem> _draws = new();

    // Size-dependent targets
    private uint _width, _height;
    private Image _color, _depth;
    private DeviceMemory _colorMem, _depthMem;
    private ImageView _colorView, _depthView;
    private Framebuffer _framebuffer;
    private VkBuffer _readback;
    private DeviceMemory _readbackMem;

    private readonly record struct DrawItem(DescriptorSet Set, uint IndexOffset, uint IndexCount, float InvW, float InvH);
    private readonly record struct MatTex(DescriptorSet Set, float InvW, float InvH);

    private struct GpuTexture
    {
        public Image Image;
        public DeviceMemory Memory;
        public ImageView View;
        public DescriptorSet Set;
    }

    public SceneRenderer(VulkanDevice device)
    {
        _dev = device;
        _vk = device.Vk;
        CreateRenderPass();
        CreateSampler();
        CreateDescriptorSetLayout();
        CreatePipeline();
        _white = CreateTextureImage(new byte[] { 255, 255, 255, 255 }, 1, 1);
    }

    /// <summary>Convenience for untextured geometry: one white-textured submesh.</summary>
    public void SetMesh(Mesh mesh)
    {
        var scene = new RenderScene();
        scene.Mesh.Vertices.AddRange(mesh.Vertices);
        scene.Mesh.Indices.AddRange(mesh.Indices);
        scene.Submeshes.Add(new Submesh { Material = "", IndexOffset = 0, IndexCount = mesh.Indices.Count });
        SetScene(scene, _ => null);
    }

    /// <summary>Uploads scene geometry and per-material textures.</summary>
    public void SetScene(RenderScene scene, TextureLookup lookup)
    {
        DestroyScene();
        if (scene.Mesh.IsEmpty) return;

        var verts = scene.Mesh.Vertices.ToArray();
        var indices = scene.Mesh.Indices.ToArray();
        (_vertexBuffer, _vertexMemory) = CreateHostBuffer(
            (ulong)(verts.Length * (int)MeshVertex.Stride), BufferUsageFlags.VertexBufferBit);
        Upload(_vertexMemory, MemoryMarshal.AsBytes<MeshVertex>(verts));
        (_indexBuffer, _indexMemory) = CreateHostBuffer(
            (ulong)(indices.Length * sizeof(uint)), BufferUsageFlags.IndexBufferBit);
        Upload(_indexMemory, MemoryMarshal.AsBytes<uint>(indices));

        CreateDescriptorPool((uint)scene.Submeshes.Count + 1);
        _white.Set = AllocateTextureDescriptor(_white.View);

        var materials = new Dictionary<string, MatTex>(StringComparer.OrdinalIgnoreCase);
        foreach (var sub in scene.Submeshes)
        {
            MatTex mt;
            if (string.IsNullOrEmpty(sub.Material))
                mt = new MatTex(_white.Set, 1f, 1f);
            else if (!materials.TryGetValue(sub.Material, out mt))
            {
                var tex = lookup(sub.Material);
                if (tex is { } t && t.Rgba.Length == t.Width * t.Height * 4 && t.Width > 0 && t.Height > 0)
                {
                    var gpu = CreateTextureImage(t.Rgba, (uint)t.Width, (uint)t.Height);
                    gpu.Set = AllocateTextureDescriptor(gpu.View);
                    _sceneTextures.Add(gpu);
                    mt = new MatTex(gpu.Set, 1f / t.Width, 1f / t.Height);
                }
                else mt = new MatTex(_white.Set, 1f, 1f);
                materials[sub.Material] = mt;
            }
            _draws.Add(new DrawItem(mt.Set, (uint)sub.IndexOffset, (uint)sub.IndexCount, mt.InvW, mt.InvH));
        }
    }

    /// <summary>Sets (or clears) the highlighted overlay geometry; its vertex colors are drawn as-is.</summary>
    public void SetSelection(Mesh? mesh)
    {
        DestroySelection();
        if (mesh is null || mesh.IsEmpty) { _selIndexCount = 0; return; }

        var verts = mesh.Vertices.ToArray();
        var indices = mesh.Indices.ToArray();
        _selIndexCount = (uint)indices.Length;
        (_selVertexBuffer, _selVertexMemory) = CreateHostBuffer(
            (ulong)(verts.Length * (int)MeshVertex.Stride), BufferUsageFlags.VertexBufferBit);
        Upload(_selVertexMemory, MemoryMarshal.AsBytes<MeshVertex>(verts));
        (_selIndexBuffer, _selIndexMemory) = CreateHostBuffer(
            (ulong)(indices.Length * sizeof(uint)), BufferUsageFlags.IndexBufferBit);
        Upload(_selIndexMemory, MemoryMarshal.AsBytes<uint>(indices));
    }

    /// <summary>Sets (or clears) depth-tested marker geometry (e.g. thing markers).</summary>
    public void SetMarkers(Mesh? mesh)
    {
        DestroyMarkers();
        if (mesh is null || mesh.IsEmpty) { _markerIndexCount = 0; return; }

        var verts = mesh.Vertices.ToArray();
        var indices = mesh.Indices.ToArray();
        _markerIndexCount = (uint)indices.Length;
        (_markerVertexBuffer, _markerVertexMemory) = CreateHostBuffer(
            (ulong)(verts.Length * (int)MeshVertex.Stride), BufferUsageFlags.VertexBufferBit);
        Upload(_markerVertexMemory, MemoryMarshal.AsBytes<MeshVertex>(verts));
        (_markerIndexBuffer, _markerIndexMemory) = CreateHostBuffer(
            (ulong)(indices.Length * sizeof(uint)), BufferUsageFlags.IndexBufferBit);
        Upload(_markerIndexMemory, MemoryMarshal.AsBytes<uint>(indices));
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

        fixed (float* mvp = viewProjection.M)
            _vk.CmdPushConstants(cmd, _layout, ShaderStageFlags.VertexBit, 0, 64, mvp);

        if (_draws.Count > 0)
        {
            _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline);
            var vb = _vertexBuffer;
            ulong offset = 0;
            _vk.CmdBindVertexBuffers(cmd, 0, 1, in vb, in offset);
            _vk.CmdBindIndexBuffer(cmd, _indexBuffer, 0, IndexType.Uint32);

            foreach (var draw in _draws)
            {
                var set = draw.Set;
                _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _layout, 0, 1, in set, 0, null);
                PushInvTexSize(cmd, draw.InvW, draw.InvH);
                _vk.CmdDrawIndexed(cmd, draw.IndexCount, 1, draw.IndexOffset, 0, 0);
            }
        }

        // Thing markers (depth-tested, white texture, vertex-colored).
        if (_markerIndexCount > 0 && _white.Set.Handle != 0)
        {
            _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline);
            var set = _white.Set;
            _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _layout, 0, 1, in set, 0, null);
            PushInvTexSize(cmd, 1f, 1f);
            var vb = _markerVertexBuffer;
            ulong offset = 0;
            _vk.CmdBindVertexBuffers(cmd, 0, 1, in vb, in offset);
            _vk.CmdBindIndexBuffer(cmd, _markerIndexBuffer, 0, IndexType.Uint32);
            _vk.CmdDrawIndexed(cmd, _markerIndexCount, 1, 0, 0, 0);
        }

        // Selection overlay (depth-test off, white texture, bright per-vertex color).
        if (_selIndexCount > 0 && _white.Set.Handle != 0)
        {
            _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _selectionPipeline);
            var set = _white.Set;
            _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _layout, 0, 1, in set, 0, null);
            PushInvTexSize(cmd, 1f, 1f);
            var vb = _selVertexBuffer;
            ulong offset = 0;
            _vk.CmdBindVertexBuffers(cmd, 0, 1, in vb, in offset);
            _vk.CmdBindIndexBuffer(cmd, _selIndexBuffer, 0, IndexType.Uint32);
            _vk.CmdDrawIndexed(cmd, _selIndexCount, 1, 0, 0, 0);
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

    /// <summary>Pushes the per-material inverse texture size (push-constant offset 64).</summary>
    private void PushInvTexSize(CommandBuffer cmd, float invW, float invH)
    {
        var inv = stackalloc float[2] { invW, invH };
        _vk.CmdPushConstants(cmd, _layout, ShaderStageFlags.VertexBit, 64, 8, inv);
    }

    // ---- pipeline / descriptors ----

    private void CreateRenderPass()
    {
        var attachments = stackalloc AttachmentDescription[2];
        attachments[0] = new AttachmentDescription
        {
            Format = ColorFormat, Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare, StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined, FinalLayout = ImageLayout.TransferSrcOptimal,
        };
        attachments[1] = new AttachmentDescription
        {
            Format = DepthFormat, Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare, StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined, FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
        };
        var colorRef = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
        var depthRef = new AttachmentReference(1, ImageLayout.DepthStencilAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1, PColorAttachments = &colorRef, PDepthStencilAttachment = &depthRef,
        };
        var dependency = new SubpassDependency
        {
            SrcSubpass = 0, DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.TransferBit, DstAccessMask = AccessFlags.TransferReadBit,
        };
        var rpInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2, PAttachments = attachments,
            SubpassCount = 1, PSubpasses = &subpass, DependencyCount = 1, PDependencies = &dependency,
        };
        if (_vk.CreateRenderPass(_dev.Device, in rpInfo, null, out _renderPass) != Result.Success)
            throw new VulkanException("vkCreateRenderPass failed");
    }

    private void CreateSampler()
    {
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Nearest, MinFilter = Filter.Nearest,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
        };
        if (_vk.CreateSampler(_dev.Device, in info, null, out _sampler) != Result.Success)
            throw new VulkanException("vkCreateSampler failed");
    }

    private void CreateDescriptorSetLayout()
    {
        var binding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };
        var info = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1, PBindings = &binding,
        };
        if (_vk.CreateDescriptorSetLayout(_dev.Device, in info, null, out _setLayout) != Result.Success)
            throw new VulkanException("vkCreateDescriptorSetLayout failed");
    }

    private void CreateDescriptorPool(uint maxSets)
    {
        DestroyDescriptorPool();
        var size = new DescriptorPoolSize
        {
            Type = DescriptorType.CombinedImageSampler, DescriptorCount = maxSets,
        };
        var info = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = maxSets, PoolSizeCount = 1, PPoolSizes = &size,
        };
        if (_vk.CreateDescriptorPool(_dev.Device, in info, null, out _descPool) != Result.Success)
            throw new VulkanException("vkCreateDescriptorPool failed");
    }

    private DescriptorSet AllocateTextureDescriptor(ImageView view)
    {
        var layout = _setLayout;
        var alloc = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descPool, DescriptorSetCount = 1, PSetLayouts = &layout,
        };
        if (_vk.AllocateDescriptorSets(_dev.Device, in alloc, out var set) != Result.Success)
            throw new VulkanException("vkAllocateDescriptorSets failed");

        var imageInfo = new DescriptorImageInfo
        {
            Sampler = _sampler, ImageView = view, ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set, DstBinding = 0, DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler, PImageInfo = &imageInfo,
        };
        _vk.UpdateDescriptorSets(_dev.Device, 1, in write, 0, null);
        return set;
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
            var attrs = stackalloc VertexInputAttributeDescription[4];
            attrs[0] = new VertexInputAttributeDescription(0, 0, Format.R32G32B32Sfloat, 0);
            attrs[1] = new VertexInputAttributeDescription(1, 0, Format.R32G32B32Sfloat, 12);
            attrs[2] = new VertexInputAttributeDescription(2, 0, Format.R32G32B32Sfloat, 24);
            attrs[3] = new VertexInputAttributeDescription(3, 0, Format.R32G32Sfloat, 36);
            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1, PVertexBindingDescriptions = &binding,
                VertexAttributeDescriptionCount = 4, PVertexAttributeDescriptions = attrs,
            };
            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList,
            };
            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1,
            };
            var raster = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill, CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise, LineWidth = 1f,
            };
            var multisample = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit,
            };
            var depthOn = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = true, DepthWriteEnable = true, DepthCompareOp = CompareOp.Less,
            };
            var depthOff = new PipelineDepthStencilStateCreateInfo
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = false, DepthWriteEnable = false, DepthCompareOp = CompareOp.Always,
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

            var pushRange = new PushConstantRange(ShaderStageFlags.VertexBit, 0, 72); // mat4 + vec2
            var setLayout = _setLayout;
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1, PSetLayouts = &setLayout,
                PushConstantRangeCount = 1, PPushConstantRanges = &pushRange,
            };
            if (_vk.CreatePipelineLayout(_dev.Device, in layoutInfo, null, out _layout) != Result.Success)
                throw new VulkanException("vkCreatePipelineLayout failed");

            var info = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2, PStages = stages,
                PVertexInputState = &vertexInput, PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState, PRasterizationState = &raster,
                PMultisampleState = &multisample, PDepthStencilState = &depthOn,
                PColorBlendState = &blend, PDynamicState = &dynamic,
                Layout = _layout, RenderPass = _renderPass, Subpass = 0,
            };
            if (_vk.CreateGraphicsPipelines(_dev.Device, default, 1, in info, null, out _pipeline) != Result.Success)
                throw new VulkanException("vkCreateGraphicsPipelines failed");

            info.PDepthStencilState = &depthOff;
            if (_vk.CreateGraphicsPipelines(_dev.Device, default, 1, in info, null, out _selectionPipeline) != Result.Success)
                throw new VulkanException("vkCreateGraphicsPipelines (selection) failed");
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
            _vk.DestroyShaderModule(_dev.Device, vert, null);
            _vk.DestroyShaderModule(_dev.Device, frag, null);
        }
    }

    // ---- textures ----

    private GpuTexture CreateTextureImage(byte[] rgba, uint w, uint h)
    {
        var imgInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo, ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm, Extent = new Extent3D(w, h, 1),
            MipLevels = 1, ArrayLayers = 1, Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive, InitialLayout = ImageLayout.Undefined,
        };
        if (_vk.CreateImage(_dev.Device, in imgInfo, null, out var image) != Result.Success)
            throw new VulkanException("vkCreateImage (texture) failed");
        _vk.GetImageMemoryRequirements(_dev.Device, image, out var reqs);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo, AllocationSize = reqs.Size,
            MemoryTypeIndex = _dev.FindMemoryType(reqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };
        if (_vk.AllocateMemory(_dev.Device, in alloc, null, out var memory) != Result.Success)
            throw new VulkanException("vkAllocateMemory (texture) failed");
        _vk.BindImageMemory(_dev.Device, image, memory, 0);

        var (staging, stagingMem) = CreateHostBuffer((ulong)rgba.Length, BufferUsageFlags.TransferSrcBit);
        Upload(stagingMem, rgba);

        var cmd = BeginOneTimeCommands();
        TransitionLayout(cmd, image, ImageLayout.Undefined, ImageLayout.TransferDstOptimal,
            PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit,
            0, AccessFlags.TransferWriteBit);
        var copy = new BufferImageCopy
        {
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit, MipLevel = 0, BaseArrayLayer = 0, LayerCount = 1,
            },
            ImageExtent = new Extent3D(w, h, 1),
        };
        _vk.CmdCopyBufferToImage(cmd, staging, image, ImageLayout.TransferDstOptimal, 1, &copy);
        TransitionLayout(cmd, image, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
            PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit,
            AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit);
        EndSubmitAndWait(cmd);

        _vk.DestroyBuffer(_dev.Device, staging, null);
        _vk.FreeMemory(_dev.Device, stagingMem, null);

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo, Image = image,
            ViewType = ImageViewType.Type2D, Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        if (_vk.CreateImageView(_dev.Device, in viewInfo, null, out var view) != Result.Success)
            throw new VulkanException("vkCreateImageView (texture) failed");

        return new GpuTexture { Image = image, Memory = memory, View = view };
    }

    private void TransitionLayout(CommandBuffer cmd, Image image, ImageLayout oldL, ImageLayout newL,
        PipelineStageFlags srcStage, PipelineStageFlags dstStage, AccessFlags srcAccess, AccessFlags dstAccess)
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldL, NewLayout = newL,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored, DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image, SrcAccessMask = srcAccess, DstAccessMask = dstAccess,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        _vk.CmdPipelineBarrier(cmd, srcStage, dstStage, 0, 0, null, 0, null, 1, in barrier);
    }

    // ---- targets ----

    private void EnsureTargets(uint width, uint height)
    {
        if (width == _width && height == _height && _framebuffer.Handle != 0) return;
        DestroyTargets();
        _width = width; _height = height;

        (_color, _colorMem, _colorView) = CreateAttachment(ColorFormat,
            ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit, ImageAspectFlags.ColorBit);
        (_depth, _depthMem, _depthView) = CreateAttachment(DepthFormat,
            ImageUsageFlags.DepthStencilAttachmentBit, ImageAspectFlags.DepthBit);

        var views = stackalloc ImageView[2] { _colorView, _depthView };
        var fbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo, RenderPass = _renderPass,
            AttachmentCount = 2, PAttachments = views, Width = _width, Height = _height, Layers = 1,
        };
        if (_vk.CreateFramebuffer(_dev.Device, in fbInfo, null, out _framebuffer) != Result.Success)
            throw new VulkanException("vkCreateFramebuffer failed");
        (_readback, _readbackMem) = CreateHostBuffer((ulong)(_width * _height * 4), BufferUsageFlags.TransferDstBit);
    }

    private (Image, DeviceMemory, ImageView) CreateAttachment(Format format, ImageUsageFlags usage, ImageAspectFlags aspect)
    {
        var info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo, ImageType = ImageType.Type2D, Format = format,
            Extent = new Extent3D(_width, _height, 1), MipLevels = 1, ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit, Tiling = ImageTiling.Optimal, Usage = usage,
            SharingMode = SharingMode.Exclusive, InitialLayout = ImageLayout.Undefined,
        };
        if (_vk.CreateImage(_dev.Device, in info, null, out var image) != Result.Success)
            throw new VulkanException("vkCreateImage failed");
        _vk.GetImageMemoryRequirements(_dev.Device, image, out var reqs);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo, AllocationSize = reqs.Size,
            MemoryTypeIndex = _dev.FindMemoryType(reqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
        };
        if (_vk.AllocateMemory(_dev.Device, in alloc, null, out var memory) != Result.Success)
            throw new VulkanException("vkAllocateMemory failed");
        _vk.BindImageMemory(_dev.Device, image, memory, 0);
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo, Image = image, ViewType = ImageViewType.Type2D,
            Format = format, SubresourceRange = new ImageSubresourceRange(aspect, 0, 1, 0, 1),
        };
        if (_vk.CreateImageView(_dev.Device, in viewInfo, null, out var view) != Result.Success)
            throw new VulkanException("vkCreateImageView failed");
        return (image, memory, view);
    }

    // ---- helpers ----

    private (VkBuffer, DeviceMemory) CreateHostBuffer(ulong size, BufferUsageFlags usage)
    {
        var info = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo, Size = size, Usage = usage, SharingMode = SharingMode.Exclusive,
        };
        if (_vk.CreateBuffer(_dev.Device, in info, null, out var buffer) != Result.Success)
            throw new VulkanException("vkCreateBuffer failed");
        _vk.GetBufferMemoryRequirements(_dev.Device, buffer, out var reqs);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo, AllocationSize = reqs.Size,
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
                SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)code.Length, PCode = (uint*)pCode,
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

    // ---- teardown ----

    private void DestroyTexture(GpuTexture t)
    {
        var d = _dev.Device;
        if (t.View.Handle != 0) _vk.DestroyImageView(d, t.View, null);
        if (t.Image.Handle != 0) _vk.DestroyImage(d, t.Image, null);
        if (t.Memory.Handle != 0) _vk.FreeMemory(d, t.Memory, null);
    }

    private void DestroyDescriptorPool()
    {
        if (_descPool.Handle != 0) { _vk.DestroyDescriptorPool(_dev.Device, _descPool, null); _descPool = default; }
    }

    private void DestroySelection()
    {
        var d = _dev.Device;
        if (_selVertexBuffer.Handle != 0) { _vk.DestroyBuffer(d, _selVertexBuffer, null); _selVertexBuffer = default; }
        if (_selVertexMemory.Handle != 0) { _vk.FreeMemory(d, _selVertexMemory, null); _selVertexMemory = default; }
        if (_selIndexBuffer.Handle != 0) { _vk.DestroyBuffer(d, _selIndexBuffer, null); _selIndexBuffer = default; }
        if (_selIndexMemory.Handle != 0) { _vk.FreeMemory(d, _selIndexMemory, null); _selIndexMemory = default; }
        _selIndexCount = 0;
    }

    private void DestroyMarkers()
    {
        var d = _dev.Device;
        if (_markerVertexBuffer.Handle != 0) { _vk.DestroyBuffer(d, _markerVertexBuffer, null); _markerVertexBuffer = default; }
        if (_markerVertexMemory.Handle != 0) { _vk.FreeMemory(d, _markerVertexMemory, null); _markerVertexMemory = default; }
        if (_markerIndexBuffer.Handle != 0) { _vk.DestroyBuffer(d, _markerIndexBuffer, null); _markerIndexBuffer = default; }
        if (_markerIndexMemory.Handle != 0) { _vk.FreeMemory(d, _markerIndexMemory, null); _markerIndexMemory = default; }
        _markerIndexCount = 0;
    }

    private void DestroyScene()
    {
        var d = _dev.Device;
        foreach (var t in _sceneTextures) DestroyTexture(t);
        _sceneTextures.Clear();
        _draws.Clear();
        DestroyDescriptorPool(); // frees all descriptor sets (incl. white's)
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
        DestroyScene();
        DestroySelection();
        DestroyMarkers();
        DestroyTargets();
        DestroyTexture(_white);
        var d = _dev.Device;
        if (_selectionPipeline.Handle != 0) _vk.DestroyPipeline(d, _selectionPipeline, null);
        if (_pipeline.Handle != 0) _vk.DestroyPipeline(d, _pipeline, null);
        if (_layout.Handle != 0) _vk.DestroyPipelineLayout(d, _layout, null);
        if (_setLayout.Handle != 0) _vk.DestroyDescriptorSetLayout(d, _setLayout, null);
        if (_sampler.Handle != 0) _vk.DestroySampler(d, _sampler, null);
        if (_renderPass.Handle != 0) _vk.DestroyRenderPass(d, _renderPass, null);
    }
}
