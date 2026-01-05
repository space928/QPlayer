using QPlayer.Utilities;
using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

    public static int LoadedPlugins => loadedPlugins.Count;

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
                    var pluginAssembly = Assembly.LoadFile(fname);
                    if (pluginAssembly == null) 
                        continue;

                    var cueTypes = CueFactory.RegisterAssembly(pluginAssembly);
                    QPlayerPlugin? pluginInst = null;
                    if (pluginAssembly.GetTypes().FirstOrDefault(typeof(QPlayerPlugin).IsAssignableFrom) is Type pluginType)
                        pluginInst = Activator.CreateInstance(pluginType) as QPlayerPlugin;

                    loadedPlugins.Add(pluginAssembly, new(pluginAssembly.FullName ?? fname, pluginAssembly, pluginInst, cueTypes));

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

    private readonly struct LoadedPlugin(string name, Assembly assembly, QPlayerPlugin? pluginInst, CueFactory.RegisteredCueType[] registeredCueTypes)
    {
        public readonly string Name = name;
        public readonly Assembly assembly = assembly;
        public readonly QPlayerPlugin? pluginInst = pluginInst;
        public readonly RegisteredCueType[] registeredCueTypes = registeredCueTypes;
    }
}

public abstract class QPlayerPlugin
{
    /// <summary>
    /// Called at startup when the plugin is loaded by QPlayer.
    /// </summary>
    /// <param name="mainViewModel"></param>
    public virtual void OnLoad(MainViewModel mainViewModel) { }
    /// <summary>
    /// Called just before QPlayer exits.
    /// </summary>
    public virtual void OnUnload() { }
    /// <summary>
    /// Called just before QPlayer saves a show file.
    /// </summary>
    /// <param name="path"></param>
    public virtual void OnSave(string path) { }
    /// <summary>
    /// Called every time QPlayer starts a cue.
    /// </summary>
    /// <param name="cue"></param>
    public virtual void OnGo(CueViewModel cue) { }
    /// <summary>
    /// Called every 250 ms on the UI thread.
    /// </summary>
    public virtual void OnSlowUpdate() { }
    /// <summary>
    /// Called every 40 ms on the UI thread.
    /// </summary>
    //public void OnFastUpdate();
}
