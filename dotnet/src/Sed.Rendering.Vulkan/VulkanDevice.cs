using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Sed.Rendering.Vulkan;

/// <summary>
/// A logical Vulkan device with a graphics-capable queue and a command pool.
/// On macOS the required <c>VK_KHR_portability_subset</c> device extension is
/// enabled automatically (mandated by the spec when the device advertises it).
/// </summary>
public sealed unsafe class VulkanDevice : IDisposable
{
    private const string PortabilitySubsetExtension = "VK_KHR_portability_subset";

    public Vk Vk { get; }
    public PhysicalDevice PhysicalDevice { get; }
    public Device Device { get; }
    public uint GraphicsQueueFamily { get; }
    public Queue GraphicsQueue { get; }
    public CommandPool CommandPool { get; }
    public string DeviceName { get; }

    private VulkanDevice(Vk vk, PhysicalDevice physical, Device device, uint queueFamily,
        Queue queue, CommandPool pool, string name)
    {
        Vk = vk;
        PhysicalDevice = physical;
        Device = device;
        GraphicsQueueFamily = queueFamily;
        GraphicsQueue = queue;
        CommandPool = pool;
        DeviceName = name;
    }

    /// <summary>Picks a physical device (discrete preferred) and creates the logical device.</summary>
    public static VulkanDevice Create(VulkanContext context)
    {
        var vk = context.Vk;
        var physical = SelectPhysicalDevice(vk, context.Instance, out var name);

        uint queueFamily = FindGraphicsQueueFamily(vk, physical);

        float priority = 1.0f;
        var queueInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = queueFamily,
            QueueCount = 1,
            PQueuePriorities = &priority,
        };

        var extensions = new List<string>();
        if (HasDeviceExtension(vk, physical, PortabilitySubsetExtension))
            extensions.Add(PortabilitySubsetExtension);

        var ppExtensions = (byte**)SilkMarshal.StringArrayToPtr(extensions);
        try
        {
            var features = new PhysicalDeviceFeatures();
            var createInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueInfo,
                EnabledExtensionCount = (uint)extensions.Count,
                PpEnabledExtensionNames = ppExtensions,
                PEnabledFeatures = &features,
            };

            if (vk.CreateDevice(physical, in createInfo, null, out var device) != Result.Success)
                throw new VulkanException("vkCreateDevice failed");

            vk.GetDeviceQueue(device, queueFamily, 0, out var queue);

            var poolInfo = new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = queueFamily,
                Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            };
            if (vk.CreateCommandPool(device, in poolInfo, null, out var pool) != Result.Success)
                throw new VulkanException("vkCreateCommandPool failed");

            return new VulkanDevice(vk, physical, device, queueFamily, queue, pool, name);
        }
        finally
        {
            SilkMarshal.Free((nint)ppExtensions);
        }
    }

    private static PhysicalDevice SelectPhysicalDevice(Vk vk, Instance instance, out string name)
    {
        uint count = 0;
        vk.EnumeratePhysicalDevices(instance, ref count, null);
        if (count == 0)
            throw new VulkanException("No Vulkan physical devices available");

        var devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* p = devices)
            vk.EnumeratePhysicalDevices(instance, ref count, p);

        PhysicalDevice chosen = devices[0];
        foreach (var d in devices)
        {
            vk.GetPhysicalDeviceProperties(d, out var props);
            if (props.DeviceType == PhysicalDeviceType.DiscreteGpu)
            {
                chosen = d;
                break;
            }
        }

        vk.GetPhysicalDeviceProperties(chosen, out var chosenProps);
        name = SilkMarshal.PtrToString((nint)chosenProps.DeviceName) ?? "<unknown>";
        return chosen;
    }

    private static uint FindGraphicsQueueFamily(Vk vk, PhysicalDevice physical)
    {
        uint count = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(physical, ref count, null);
        var families = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* p = families)
            vk.GetPhysicalDeviceQueueFamilyProperties(physical, ref count, p);

        for (uint i = 0; i < count; i++)
            if (families[i].QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                return i;

        throw new VulkanException("No graphics-capable queue family found");
    }

    private static bool HasDeviceExtension(Vk vk, PhysicalDevice physical, string name)
    {
        uint count = 0;
        vk.EnumerateDeviceExtensionProperties(physical, (byte*)null, ref count, null);
        var props = new ExtensionProperties[count];
        fixed (ExtensionProperties* p = props)
            vk.EnumerateDeviceExtensionProperties(physical, (byte*)null, ref count, p);

        for (uint i = 0; i < count; i++)
        {
            fixed (byte* pName = props[i].ExtensionName)
            {
                if (SilkMarshal.PtrToString((nint)pName) == name)
                    return true;
            }
        }
        return false;
    }

    /// <summary>Finds a memory type index satisfying <paramref name="typeBits"/> and <paramref name="properties"/>.</summary>
    public uint FindMemoryType(uint typeBits, MemoryPropertyFlags properties)
    {
        Vk.GetPhysicalDeviceMemoryProperties(PhysicalDevice, out var memProps);
        for (uint i = 0; i < memProps.MemoryTypeCount; i++)
        {
            bool typeOk = (typeBits & (1u << (int)i)) != 0;
            bool propsOk = (memProps.MemoryTypes[(int)i].PropertyFlags & properties) == properties;
            if (typeOk && propsOk)
                return i;
        }
        throw new VulkanException("No suitable memory type found");
    }

    public void Dispose()
    {
        if (CommandPool.Handle != 0)
            Vk.DestroyCommandPool(Device, CommandPool, null);
        if (Device.Handle != 0)
            Vk.DestroyDevice(Device, null);
    }
}
