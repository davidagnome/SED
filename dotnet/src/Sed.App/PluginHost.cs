using System.Reflection;
using System.Runtime.Loader;
using Sed.Plugins;

namespace Sed.App;

/// <summary>A plugin that was found, and the commands it offers.</summary>
public sealed record LoadedPlugin(string Name, string? Description, string Source,
    IReadOnlyList<PluginCommand> Commands);

/// <summary>
/// Discovers and loads managed plugins, replacing the original's Windows COM +
/// native-DLL host (`SED_COM`, `sed_plugins`) with something portable.
///
/// Each plugin assembly gets its own collectible <see cref="AssemblyLoadContext"/>
/// so plugins cannot clash over dependency versions. The contract assemblies —
/// <c>Sed.Plugins</c> and <c>Sed.Core</c> — are deliberately **not** loaded into
/// that context: if they were, the plugin's <c>Level</c> type would be a different
/// type from the host's and every call would fail with a confusing cast error.
/// They resolve to the already-loaded host copies instead.
/// </summary>
public sealed class PluginHost
{
    /// <summary>Assemblies shared with the host rather than loaded per-plugin.</summary>
    private static readonly string[] SharedAssemblies = { "Sed.Plugins", "Sed.Core" };

    private readonly List<LoadedPlugin> _plugins = new();
    private readonly List<string> _problems = new();

    public IReadOnlyList<LoadedPlugin> Plugins => _plugins;

    /// <summary>Assemblies that failed to load, and why — surfaced rather than swallowed.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Where plugins live: a <c>plugins</c> folder beside the executable.</summary>
    public static string DefaultDirectory =>
        Path.Combine(AppContext.BaseDirectory, "plugins");

    /// <summary>
    /// Loads every plugin assembly in a directory. A missing directory is normal
    /// (no plugins installed) rather than an error.
    /// </summary>
    public void LoadFrom(string directory)
    {
        _plugins.Clear();
        _problems.Clear();

        if (!Directory.Exists(directory)) return;

        foreach (var path in Directory.EnumerateFiles(directory, "*.dll").OrderBy(p => p))
        {
            try
            {
                LoadAssembly(path);
            }
            catch (Exception ex)
            {
                // One bad plugin must not stop the others, or the editor.
                _problems.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }
    }

    /// <summary>Loads plugin types from an assembly already in memory (used by tests).</summary>
    public void AddFromAssembly(Assembly assembly)
    {
        foreach (var plugin in Instantiate(assembly, assembly.GetName().Name ?? "in-memory"))
            _plugins.Add(plugin);
    }

    private void LoadAssembly(string path)
    {
        var context = new PluginLoadContext(path);
        var assembly = context.LoadFromAssemblyPath(path);

        int before = _plugins.Count;
        foreach (var plugin in Instantiate(assembly, Path.GetFileName(path)))
            _plugins.Add(plugin);

        if (_plugins.Count == before)
            _problems.Add($"{Path.GetFileName(path)}: no ISedPlugin implementations found.");
    }

    private IEnumerable<LoadedPlugin> Instantiate(Assembly assembly, string source)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Partially loadable assemblies still yield the types that resolved.
            types = ex.Types.Where(t => t is not null).ToArray()!;
        }

        foreach (var type in types)
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(ISedPlugin).IsAssignableFrom(type)) continue;

            LoadedPlugin? loaded = null;
            try
            {
                if (Activator.CreateInstance(type) is not ISedPlugin instance) continue;

                var commands = instance.GetCommands()?.Where(c => c is not null).ToList()
                               ?? new List<PluginCommand>();
                loaded = new LoadedPlugin(instance.Name, instance.Description, source, commands);
            }
            catch (Exception ex)
            {
                _problems.Add($"{source}: {type.Name} failed to initialise — {ex.Message}");
            }

            if (loaded is not null) yield return loaded;
        }
    }

    /// <summary>
    /// Runs a plugin command, containing any exception. A plugin throwing must
    /// report a problem, not take down the editor mid-edit.
    /// </summary>
    public static string Invoke(PluginCommand command, PluginContext context)
    {
        try
        {
            command.Execute(context);
            return $"{command.Label}: done.";
        }
        catch (Exception ex)
        {
            return $"{command.Label} failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Isolates a plugin's private dependencies while sharing the contract types
    /// with the host.
    /// </summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath) : base(isCollectible: true) =>
            _resolver = new AssemblyDependencyResolver(pluginPath);

        protected override Assembly? Load(AssemblyName name)
        {
            // Contract assemblies must be the host's instances, or the plugin's
            // Level/Sector types would not match the ones it is handed.
            if (name.Name is { } n && SharedAssemblies.Contains(n)) return null;

            var path = _resolver.ResolveAssemblyToPath(name);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override nint LoadUnmanagedDll(string name)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(name);
            return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
