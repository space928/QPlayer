using QPlayer.SourceGenerator;
using QPlayer.ViewModels;
using System;

namespace QPlayer.OSCCuePlugin;


[Model(typeof(OSCCueModel))]
[GenerateView]
[View(typeof(OSCCueViewModelView))]
[DisplayName("OSC Cue")]
[Icon("IconOSCCue", typeof(Icons))]
public partial class OSCCueViewModel : CueViewModel
{
    [Reactive/*, ChangesProp(nameof(OSCMessageValid))*/]
    [Tooltip("The address and parameters of the OSC command to send when this cue is triggered. " +
        "Addresses must start with a slash (/), OSC parameters are specified directly after the address, " +
        "separated by commas. (Eg: '/qplayer/go,5' sends a message to '/qplayer/go' with the integer " +
        "parameter '5')")]
    private string command = "/";


    [Reactive/*, ChangesProp(nameof(OSCMessageValid))*/]
    [Tooltip("(OPTIONAL) The IP Address of the target in the form '192.168.0.123'")]
    private string ip = "";

    [Reactive/*, ChangesProp(nameof(OSCMessageValid))*/]
    [Tooltip("(OPTIONAL) The IP Port of the target in the form '10023'")]
    private string port = "";

    /*[Reactive("OSCMessageValid"), ModelSkip]
    private bool OSCMessageValid_Template
    {
        get
        {
            try
            {
                var (addr, args) = OSCMessageParser.ParseOSCMessage(command);
                if (addr.Length <= 1)
                    return false;
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
    }*/

    public OSCCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    { }

    public override void Go()
    {
        base.Go();
        try
        {
            var ipAndPort = ip;
            if (!string.IsNullOrEmpty(port) && int.TryParse(port, out var portnumber)) ipAndPort = $"{ip}:{portnumber}";
            mainViewModel?.OSCManager.SendMessage(command, ipAndPort);
        }
        catch (Exception ex)
        {
            MainViewModel.Log($"Failed to send OSC message: {ex.Message}", MainViewModel.LogLevel.Error);
        }
    }
}
