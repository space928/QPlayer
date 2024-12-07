using CommunityToolkit.Mvvm.ComponentModel;
using QPlayer.Models;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.ViewModels;

public class ProjectSettingsViewModel : ObservableObject, IConvertibleModel<ShowMetadata, ProjectSettingsViewModel>
{
    #region Bindable Properties
    [Reactive] public string Title { get; set; } = "Untitled";
    [Reactive] public string Description { get; set; } = string.Empty;
    [Reactive] public string Author { get; set; } = string.Empty;
    [Reactive] public DateTime Date { get; set; }

    [Reactive] public AudioOutputDriver AudioOutputDriver { get; set; }
    [Reactive] public int SelectedAudioOutputDeviceIndex { get; set; }

    [Reactive] public static ObservableCollection<AudioOutputDriver>? AudioOutputDriverValues { get; private set; }
    [Reactive, ReactiveDependency(nameof(AudioOutputDriver))]
    public ObservableCollection<string> AudioOutputDevices
    {
        get
        {
            audioOutputDevices = mainViewModel.AudioPlaybackManager.GetOutputDevices(AudioOutputDriver);
            return new(audioOutputDevices.Select(x => x.identifier));
        }
    }

    [Reactive] public ObservableCollection<string> NICs => nics;
    [Reactive] public int SelectedNIC { get; set; }
    [Reactive] public int OSCRXPort { get; set; } = 9000;
    [Reactive] public int OSCTXPort { get; set; } = 8000;
    [Reactive] public bool MonitorOSCMessages { get; set; } = false;
    #endregion

    public IPAddress OSCNic => (SelectedNIC >= 0 && SelectedNIC < nicAddresses.Count) ? nicAddresses[SelectedNIC] : (nicAddresses.FirstOrDefault() ?? IPAddress.Any);

    private (object key, string identifier)[] audioOutputDevices = [];
    private readonly MainViewModel mainViewModel;
    private ShowMetadata? projectSettings;
    private readonly ObservableCollection<string> nics = [];
    private readonly List<IPAddress> nicAddresses = [];

    public ProjectSettingsViewModel(MainViewModel mainViewModel)
    {
        this.mainViewModel = mainViewModel;
        AudioOutputDriverValues ??= new(Enum.GetValues<AudioOutputDriver>());
        PropertyChanged += (o, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(SelectedAudioOutputDeviceIndex):
                    if (SelectedAudioOutputDeviceIndex >= 0 && SelectedAudioOutputDeviceIndex < audioOutputDevices.Length)
                        mainViewModel.AudioPlaybackManager.OpenOutputDevice(
                            AudioOutputDriver,
                            audioOutputDevices[SelectedAudioOutputDeviceIndex].key);
                    break;
                case nameof(SelectedNIC):
                case nameof(OSCRXPort):
                case nameof(OSCTXPort):
                    mainViewModel.ConnectOSC();
                    break;
                case nameof(MonitorOSCMessages):
                    mainViewModel.MonitorOSC(MonitorOSCMessages);
                    break;
            }
        };

        _ = AudioOutputDevices; // Update the list of audio devices...
        if (SelectedAudioOutputDeviceIndex < audioOutputDevices.Length)
            mainViewModel.AudioPlaybackManager.OpenOutputDevice(
                AudioOutputDriver, audioOutputDevices[SelectedAudioOutputDeviceIndex].key);

        QueryNICs();
    }

    public static ProjectSettingsViewModel FromModel(ShowMetadata model, MainViewModel mainViewModel)
    {
        ProjectSettingsViewModel ret = new(mainViewModel);
        ret.Title = model.title;
        ret.Description = model.description;
        ret.Author = model.author;
        ret.Date = model.date;

        ret.AudioOutputDriver = model.audioOutputDriver;
        ret.SelectedAudioOutputDeviceIndex = ret.AudioOutputDevices.IndexOf(model.audioOutputDevice);

        ret.OSCRXPort = model.oscRXPort;
        ret.OSCTXPort = model.oscTXPort;
        if (IPAddress.TryParse(model.oscNIC, out var oscIP))
            ret.SelectedNIC = ret.nicAddresses.IndexOf(oscIP);

        return ret;
    }

    public void Bind(ShowMetadata model)
    {
        projectSettings = model;
        PropertyChanged += (o, e) =>
        {
            ProjectSettingsViewModel vm = (ProjectSettingsViewModel)(o ?? throw new NullReferenceException(nameof(ProjectSettingsViewModel)));
            if (e.PropertyName != null)
                vm.ToModel(e.PropertyName);
        };
    }

    public void ToModel(ShowMetadata model)
    {
        model.title = Title;
        model.description = Description;
        model.author = Author;
        model.date = Date;

        model.audioOutputDriver = AudioOutputDriver;
        var devices = AudioOutputDevices;
        model.audioOutputDevice = devices[Math.Clamp(SelectedAudioOutputDeviceIndex, 0, devices.Count)];

        model.oscRXPort = OSCRXPort;
        model.oscTXPort = OSCTXPort;
        model.oscNIC = OSCNic.ToString();
    }

    public void ToModel(string propertyName)
    {
        if (projectSettings == null)
            return;
        switch (propertyName)
        {
            case nameof(Title):
                projectSettings.title = Title;
                break;
            case nameof(Description):
                projectSettings.description = Description;
                break;
            case nameof(Author):
                projectSettings.author = Author;
                break;
            case nameof(Date):
                projectSettings.date = Date;
                break;
            case nameof(AudioOutputDriver):
                projectSettings.audioOutputDriver = AudioOutputDriver;
                break;
            case nameof(SelectedAudioOutputDeviceIndex):
                projectSettings.audioOutputDevice = AudioOutputDevices[SelectedAudioOutputDeviceIndex];
                break;
            case nameof(AudioOutputDevices):
            case nameof(AudioOutputDriverValues):
                break;
            case nameof(OSCRXPort):
                projectSettings.oscRXPort = OSCRXPort;
                break;
            case nameof(OSCTXPort):
                projectSettings.oscTXPort = OSCTXPort;
                break;
            case nameof(SelectedNIC):
                projectSettings.oscNIC = OSCNic.ToString();
                break;
            case nameof(NICs):
            case nameof(MonitorOSCMessages):
            case nameof(OSCNic):
                break;
            default:
                throw new ArgumentException($"Couldn't convert property {propertyName} to model!", nameof(propertyName));
        }
    }

    private void QueryNICs()
    {
        nics.Clear();
        nicAddresses.Clear();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            var ipProps = nic.GetIPProperties();
            var nicAddr = ipProps.UnicastAddresses.Select(x => x.Address);
            if (nicAddr.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork) is IPAddress naddr
                && ipProps.GatewayAddresses.Count > 0)
            {
                nicAddresses.AddRange(nicAddr);
                foreach (var addr in nicAddr)
                    nics.Add($"{nic.Name}: {addr}");
                //Log($"\t{nic.Name}: {string.Join(", ", nicAddr)}");
            }
        }
    }
}
