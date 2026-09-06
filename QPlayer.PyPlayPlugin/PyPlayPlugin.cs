using QPlayer.Models;
using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace QPlayer.PyPlayPlugin;

[PluginName("PyPlay Plugin")]
[PluginAuthor("Thomas Mathieson")]
[PluginDescription("This plugin adds support for PyPlay video and shader cues. PyPlay is a flexible cross-platform video playback engine. https://github.com/dmathies/pyPlay")]
public class PyPlayPlugin : QPlayerPlugin
{
    public override void OnLoad(MainViewModel mainViewModel)
    {
        MainViewModel.Log("Loaded PyPlay plugin!");
    }
}
