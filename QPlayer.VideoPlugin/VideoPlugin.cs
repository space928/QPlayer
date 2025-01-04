using QPlayer.Models;
using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;

namespace QPlayer.VideoPlugin;

[PluginName("Video Plugin")]
[PluginAuthor("Thomas Mathieson")]
[PluginDescription("This plugin adds video cues and general support for video playback, compositing, and effects.")]
public class VideoPlugin : QPlayerPlugin
{
    private MainViewModel? mainViewModel;
    private readonly List<VideoWindow> windows = [];
    private readonly ReadOnlyCollection<VideoWindow> windowsRO;
    private readonly SortedList<float, VideoObject> videoObjects;
    private readonly Lock lockObj;
    private readonly Thread videoThread;

    public VideoPlugin()
    {
        windowsRO = new(windows);
        lockObj = new();
        videoObjects = [];
        videoThread = new(() => RunUIThread(title));
        videoThread.Priority = ThreadPriority.AboveNormal;
        videoThread.Name = "QPlayer Video Window";

        videoThread.Start();
    }

    public override void OnLoad(MainViewModel mainViewModel)
    {
        this.mainViewModel = mainViewModel;
        // Register main menu item


        // The plugin is an instance class, but only one will ever be created in the
        // lifetime of the application, so we can use it as a singleton.
        pluginInst = this;
    }

    private void RegisterVideoObjectImpl(VideoObject obj)
    {
        lock (lockObj)
        {
            videoObjects.Add(obj.ZIndex, obj);
        }
    }

    private void RunVideoThread()
    {
        foreach (var wnd in windows)
        {
            wnd.
        }
    }

    // Since only one VideoPlugin instance will ever exist, we provide a static interface to some methods for convenience
    #region Static Interface
    private static VideoPlugin? pluginInst;

    public static VideoPlugin? VideoPluginInst => pluginInst;
    public static ReadOnlyCollection<VideoWindow> Windows => pluginInst?.windowsRO ?? [];

    public static bool RegisterVideoObject(VideoObject obj)
    {
        if (pluginInst is VideoPlugin plugin)
        {
            plugin.RegisterVideoObjectImpl(obj);
            return true;
        }
        return false;
    }
    #endregion
}
