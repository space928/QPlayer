using QPlayer.Utilities;
using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using static QPlayer.ViewModels.CueFactory;

namespace QPlayer.Models;

/*
 * QPlayer plugins should be placed in the '/plugins/' subdirectory to be automatically loaded at startup.
 * 
 * A plugin itself can hook into a few qplayer events to perform work. Additionally, a plugin can define 
 * new cue types and UI windows. 
 * 
 */

public static class PluginLoader
{
    private readonly static Dictionary<Assembly, LoadedPlugin> loadedPlugins = [];
    private readonly static ReadOnlyDictionary<Assembly, LoadedPlugin> loadedPluginsRO = new(loadedPlugins);

    public static ReadOnlyDictionary<Assembly, LoadedPlugin> LoadedPlugins => loadedPluginsRO;

    public static void LoadPlugins(MainViewModel mainViewModel)
    {
        foreach (var plugin in loadedPlugins)
            plugin.Value.pluginInst?.OnUnload();

        loadedPlugins.Clear();

        try
        {
            var baseAssembly = Assembly.GetEntryAssembly();
            var pluginsPath = Path.Combine(Path.GetDirectoryName(baseAssembly?.Location) ?? string.Empty, "plugins");
            if (baseAssembly == null || !Directory.Exists(pluginsPath))
                return;

            foreach (var fname in Directory.EnumerateFiles(pluginsPath))
            {
                if (!fname.EndsWith(".dll"))
                    continue;

                try
                {
                    var loadContext = new PluginLoadContext(fname);
                    var pluginAssembly = loadContext.LoadFromAssemblyName(new(Path.GetFileNameWithoutExtension(fname))); //Assembly.LoadFile(fname);
                    if (pluginAssembly == null)
                        continue;

                    QPlayerPlugin? pluginInst = null;
                    if (pluginAssembly.GetTypes().FirstOrDefault(typeof(QPlayerPlugin).IsAssignableFrom) is Type pluginType)
                        pluginInst = Activator.CreateInstance(pluginType) as QPlayerPlugin;
                    else
                        continue; // Fail silently here, as we may be accidentally loading a plugin dependency DLL which should not be loaded as a plugin.
                        // throw new Exception("Couldn't load plugin as no class was found implementing QPlayerPlugin!");

                    var cueTypes = CueFactory.RegisterAssembly(pluginAssembly);

                    var assName = pluginAssembly.GetName();
                    string version = assName.Version?.ToString() ?? "0.0";
                    string author = pluginType.GetCustomAttribute<PluginAuthorAttribute>()?.Name ?? string.Empty;
                    string name = pluginType.GetCustomAttribute<PluginNameAttribute>()?.Name ?? pluginAssembly.FullName ?? fname;
                    string description = pluginType.GetCustomAttribute<PluginDescriptionAttribute>()?.Description ?? "No description provided.";

                    loadedPlugins.Add(pluginAssembly, new(name, author, version, description, pluginAssembly, pluginInst, cueTypes));

                    pluginInst?.OnLoad(mainViewModel);
                }
                catch (Exception ex)
                {
                    MainViewModel.Log($"Error occurred while loading plugin '{Path.GetFileName(fname)}'\n{ex}", MainViewModel.LogLevel.Error);
                }
            }
        }
        catch (Exception ex)
        {
            MainViewModel.Log($"Error occurred while loading plugins: {ex}", MainViewModel.LogLevel.Error);
        }
    }

    internal static void OnUnload()
    {
        foreach (var plugin in loadedPlugins)
            plugin.Value.pluginInst?.OnUnload();
    }

    internal static void OnSave(string path)
    {
        foreach (var plugin in loadedPlugins)
            plugin.Value.pluginInst?.OnSave(path);
    }

    internal static void OnGo(CueViewModel cue)
    {
        foreach (var plugin in loadedPlugins)
            plugin.Value.pluginInst?.OnGo(cue);
    }

    internal static void OnSlowUpdate()
    {
        foreach (var plugin in loadedPlugins)
            plugin.Value.pluginInst?.OnSlowUpdate();
    }

    public readonly struct LoadedPlugin(string name, string author, string version, string description, 
        Assembly assembly, QPlayerPlugin? pluginInst, RegisteredCueType[] registeredCueTypes)
    {
        public readonly string Name = name;
        public readonly string Author = author;
        public readonly string Version = version;
        public readonly string Description = description;
        public readonly Assembly assembly = assembly;
        public readonly QPlayerPlugin? pluginInst = pluginInst;
        public readonly RegisteredCueType[] registeredCueTypes = registeredCueTypes;
    }
}

/// <summary>
/// Loads a plugin and it's dependencies in a load context to avoid depedency conflicts.
/// </summary>
/// <remarks>
/// Taken from: https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support
/// </remarks>
/// <param name="pluginPath"></param>
class PluginLoadContext(string pluginPath) : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver resolver = new(pluginPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string? assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
            return LoadFromAssemblyPath(assemblyPath);

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
            return LoadUnmanagedDllFromPath(libraryPath);

        return IntPtr.Zero;
    }
}
