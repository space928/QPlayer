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
using QPlayer.Audio;
using CommunityToolkit.Mvvm.Input;
using Mathieson.Dev;
using PropertyChanged;

namespace QPlayer.ViewModels;

public class ProjectSettingsViewModel : ObservableObject, IConvertibleModel<ShowSettings, ProjectSettingsViewModel>
{
    #region Bindable Properties
    [Reactive] public string Title { get; set; } = "Untitled";
    [Reactive] public string Description { get; set; } = string.Empty;
    [Reactive] public string Author { get; set; } = string.Empty;
    [Reactive] public DateTime Date { get; set; }

    [Reactive] public int AudioLatency { get; set; }
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

    [Reactive] public bool EnableRemoteControl { get; set; }
    [Reactive] public bool IsRemoteHost { get; set; }
    [Reactive] public bool SyncShowFileOnSave { get; set; }
    [Reactive] public string NodeName { get; set; } = string.Empty;
    [Reactive] public ReadOnlyObservableCollection<RemoteNodeViewModel> RemoteNodes => remoteNodesRO;

    [Reactive] public RelayCommand<RemoteNodeViewModel> RemoveRemoteNodeCommand {  get; private set; }
    [Reactive] public MainViewModel MainViewModel => mainViewModel;
    #endregion

    public IPAddress OSCNic => (SelectedNIC >= 0 && SelectedNIC < nicAddresses.Count) ? nicAddresses[SelectedNIC].addr : (nicAddresses.FirstOrDefault().addr ?? IPAddress.Any);
    public IPAddress OSCSubnet => nicAddresses.ElementAtOrDefault(SelectedNIC).subnet ?? IPAddress.Broadcast;
    public event Action<RemoteNodeViewModel>? RemoteNodeStatusChanged;

    internal object? SelectedAudioOutputDeviceKey
    {
        get
        {
            var i = Math.Max(0, SelectedAudioOutputDeviceIndex);
            if (i >= audioOutputDevices.Length)
                return null;
            return audioOutputDevices[i].key;
        }
    }

    private (object key, string identifier)[] audioOutputDevices = [];
    private readonly MainViewModel mainViewModel;
    private ShowSettings? projectSettings;
    private readonly ObservableCollection<string> nics = [];
    private readonly List<(IPAddress addr, IPAddress subnet)> nicAddresses = [];

    private readonly StringDict<RemoteNodeViewModel> remoteNodesDict = [];
    private readonly ReadOnlyObservableCollection<RemoteNodeViewModel> remoteNodesRO;
    private readonly ObservableCollection<RemoteNodeViewModel> remoteNodes = [];

    public ProjectSettingsViewModel(MainViewModel mainViewModel)
    {
        remoteNodesRO = new(remoteNodes);
        this.mainViewModel = mainViewModel;
        AudioOutputDriverValues ??= new(Enum.GetValues<AudioOutputDriver>());
        PropertyChanged += (o, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(AudioLatency):
                case nameof(SelectedAudioOutputDeviceIndex):
                    if (SelectedAudioOutputDeviceIndex >= 0 && SelectedAudioOutputDeviceIndex < audioOutputDevices.Length)
                        mainViewModel.OpenAudioDevice();
                    break;
                case nameof(SelectedNIC):
                case nameof(OSCRXPort):
                case nameof(OSCTXPort):
                case nameof(IsRemoteHost):
                case nameof(EnableRemoteControl):
                    mainViewModel.OSCManager.ConnectOSC();
                    break;
                case nameof(MonitorOSCMessages):
                    mainViewModel.OSCManager.MonitorOSC(MonitorOSCMessages);
                    break;
                //case nameof(EnableRemoteControl):
                //    break;
            }
        };

        _ = AudioOutputDevices; // Update the list of audio devices...
        //if (SelectedAudioOutputDeviceIndex < audioOutputDevices.Length)
        //    mainViewModel.OpenAudioDevice();

        RemoveRemoteNodeCommand = new(item => { 
            if (item != null)
                RemoveRemoteNode(item.Name);
        });

        QueryNICs();
    }

    public static ProjectSettingsViewModel FromModel(ShowSettings model, MainViewModel mainViewModel)
    {
        ProjectSettingsViewModel ret = new(mainViewModel);
        ret.Title = model.title;
        ret.Description = model.description;
        ret.Author = model.author;
        ret.Date = model.date;

        ret.AudioLatency = model.audioLatency;
        ret.AudioOutputDriver = model.audioOutputDriver;
        ret.SelectedAudioOutputDeviceIndex = ret.AudioOutputDevices.IndexOf(model.audioOutputDevice);

        ret.OSCRXPort = model.oscRXPort;
        ret.OSCTXPort = model.oscTXPort;
        if (IPAddress.TryParse(model.oscNIC, out var oscIP))
            ret.SelectedNIC = ret.nicAddresses.FindIndex(x=> x.addr.Equals(oscIP));//.IndexOf(oscIP);

        ret.EnableRemoteControl = model.enableRemoteControl;
        ret.SyncShowFileOnSave = model.syncShowFileOnSave;
        ret.IsRemoteHost = model.isRemoteHost;
        ret.NodeName = model.nodeName;
        foreach (var node in model.remoteNodes)
        {
            RemoteNodeViewModel remote = new(node, ret);
            if (ret.remoteNodesDict.TryAdd(remote.Name, remote))
                ret.remoteNodes.Add(remote);
        }

        return ret;
    }

    public void Bind(ShowSettings model)
    {
        projectSettings = model;
        PropertyChanged += (o, e) =>
        {
            ProjectSettingsViewModel vm = (ProjectSettingsViewModel)(o ?? throw new NullReferenceException(nameof(ProjectSettingsViewModel)));
            if (e.PropertyName != null)
                vm.ToModel(e.PropertyName);
        };
    }

    public void ToModel(ShowSettings model)
    {
        model.title = Title;
        model.description = Description;
        model.author = Author;
        model.date = Date;

        model.audioLatency = AudioLatency;
        model.audioOutputDriver = AudioOutputDriver;
        var devices = AudioOutputDevices;
        model.audioOutputDevice = devices[Math.Clamp(SelectedAudioOutputDeviceIndex, 0, devices.Count)];

        model.oscRXPort = OSCRXPort;
        model.oscTXPort = OSCTXPort;
        model.oscNIC = OSCNic.ToString();

        model.enableRemoteControl = EnableRemoteControl;
        model.isRemoteHost = IsRemoteHost;
        model.syncShowFileOnSave = SyncShowFileOnSave;
        model.nodeName = NodeName;
        model.remoteNodes = remoteNodes.Select(x => new RemoteNode(x.Name, x.Address)).ToList();
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
            case nameof(AudioLatency):
                projectSettings.audioLatency = AudioLatency;
                break;
            case nameof(AudioOutputDriver):
                projectSettings.audioOutputDriver = AudioOutputDriver;
                break;
            case nameof(SelectedAudioOutputDeviceIndex):
                var outputDevices = AudioOutputDevices;
                projectSettings.audioOutputDevice = outputDevices[Math.Clamp(SelectedAudioOutputDeviceIndex, 0, outputDevices.Count - 1)];
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
            case nameof(EnableRemoteControl):
                projectSettings.enableRemoteControl = EnableRemoteControl;
                break;
            case nameof(IsRemoteHost):
                projectSettings.isRemoteHost = IsRemoteHost;
                break;
            case nameof(SyncShowFileOnSave):
                projectSettings.syncShowFileOnSave = SyncShowFileOnSave;
                break;
            case nameof(NodeName):
                projectSettings.nodeName = NodeName;
                break;
            case nameof(RemoteNodes):
                projectSettings.remoteNodes = remoteNodes.Select(x => new RemoteNode(x.Name, x.Address)).Distinct().ToList();
                break;

            case nameof(NICs):
            case nameof(MonitorOSCMessages):
            case nameof(OSCNic):
            case nameof(SelectedAudioOutputDeviceKey):
            case nameof(OSCSubnet):
                break;
            default:
                throw new ArgumentException($"Couldn't convert property {propertyName} to model!", nameof(propertyName));
        }
    }

    /// <summary>
    /// Removes a remote node from the collection of remote nodes.
    /// </summary>
    /// <param name="name">The name of the name to remove</param>
    /// <returns><see langword="true"/> if the node was removed successfully.</returns>
    public bool RemoveRemoteNode(string name)
    {
        var ret = false;
        lock (remoteNodesDict)
        {
            if (remoteNodesDict.Remove(name, out var node))
                ret = remoteNodes.Remove(node);
        }
        return ret;
    }

    /// <summary>
    /// Gets a remote node by name, returning a new node and adding it, if it doesn't already 
    /// exist in the collection.
    /// </summary>
    /// <param name="name"></param>
    /// <returns>true if the returned node was newly added.</returns>
    public bool GetOrAddRemoteNode(ReadOnlySpan<char> name, out RemoteNodeViewModel node)
    {
        lock (remoteNodesDict)
        {
            if (remoteNodesDict.TryGetValue(name, out node!))
            return false;

            var nameStr = new string(name);
            node = new RemoteNodeViewModel(nameStr, this);
            remoteNodesDict.Add(nameStr, node);
            remoteNodes.Add(node);
            return true;
        }
    }

    /// <inheritdoc cref="GetOrAddRemoteNode(ReadOnlySpan{char}, out RemoteNodeViewModel)"/>
    public bool GetOrAddRemoteNode(string name, out RemoteNodeViewModel node)
    {
        lock (remoteNodesDict)
        {
            if (remoteNodesDict.TryGetValue(name, out node!))
                return false;

            node = new RemoteNodeViewModel(name, this);
            remoteNodesDict.Add(name, node);
            remoteNodes.Add(node);
            return true;
        }
    }

    /// <summary>
    /// Gets whether a given remote node is currently active.
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public bool IsRemoteNodeActive(string name)
    {
        lock (remoteNodesDict)
        {
            if (remoteNodesDict.TryGetValue(name, out var node))
                return node.IsActive;
        }
        return false;
    }

    [SuppressPropertyChangedWarnings]
    internal void OnRemoteNodeStatusChanged(RemoteNodeViewModel remoteNode)
    {
        RemoteNodeStatusChanged?.Invoke(remoteNode);
    }

    private void QueryNICs()
    {
        nics.Clear();
        nicAddresses.Clear();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            var ipProps = nic.GetIPProperties();
            var nicAddr = ipProps.UnicastAddresses
                .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(x => (x.Address, x.IPv4Mask));
            if (nicAddr.FirstOrDefault() is (IPAddress naddr, IPAddress mask)
                && ipProps.GatewayAddresses.Count >= 0)
            {
                nicAddresses.AddRange(nicAddr);
                foreach (var addr in nicAddr)
                    nics.Add($"{nic.Name}: {addr}");
                //Log($"\t{nic.Name}: {string.Join(", ", nicAddr)}");
            }
        }
    }
}
