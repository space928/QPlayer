using CommunityToolkit.Mvvm.Input;
using DynamicData;
using PropertyChanged;
using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.Utilities;
using QPlayer.Views;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace QPlayer.ViewModels;

[Model(typeof(ShowSettings))]
[View(typeof(ProjectSettingsEditor))]
public class ProjectSettingsViewModel : BindableViewModel<ShowSettings>
{
    #region Bindable Properties
    [Reactive] public string Title { get; set; } = "Untitled";
    [Reactive] public string Description { get; set; } = string.Empty;
    [Reactive] public string Author { get; set; } = string.Empty;
    [Reactive] public DateTime Date { get; set; }

    [Reactive] public int AudioLatency { get; set; }
    [Reactive] public AudioOutputDriver AudioOutputDriver { get; set; }
    [Reactive, ModelCustomBinding(nameof(VM2M_AudioOutputDevice), nameof(M2VM_AudioOutputDevice))] 
    public int SelectedAudioOutputDeviceIndex { get; set; }

    [Reactive] public static ObservableCollection<AudioOutputDriver>? AudioOutputDriverValues { get; private set; }
    [Reactive]
    public ObservableCollection<string> AudioOutputDevices
    {
        get
        {
            var sc = SynchronizationContext.Current;
            mainViewModel.AudioPlaybackManager.GetOutputDevices(AudioOutputDriver).ContinueWith(x =>
            {
                if (x.Result.Zip(audioOutputDevices).Any(x => x.First.identifier != x.Second.identifier))
                {
                    audioOutputDevices = x.Result;
                    sc?.Post(x => OnPropertyChanged(nameof(AudioOutputDevices)), null);
                }
            });
            return new(audioOutputDevices.Select(x => x.identifier));
        }
    }

    [Reactive] public ObservableCollection<string> NICs => nics;
    [Reactive, ModelCustomBinding(nameof(VM2M_OSCNic), nameof(M2VM_OSCNic))] 
    public int SelectedNIC { get; set; }
    [Reactive] public int OSCRXPort { get; set; } = 9000;
    [Reactive] public int OSCTXPort { get; set; } = 8000;
    [Reactive, ModelSkip] public bool MonitorOSCMessages { get; set; } = false;

    [Reactive] public bool EnableRemoteControl { get; set; }
    [Reactive] public bool IsRemoteHost { get; set; }
    [Reactive] public bool SyncShowFileOnSave { get; set; }
    [Reactive] public string NodeName { get; set; } = string.Empty;
    [Reactive] public ReadOnlyObservableCollection<RemoteNodeViewModel> RemoteNodes => remoteNodesRO;

    [Reactive, ModelBindsTo(nameof(ShowSettings.mscRXPort))] public int MAMSCRXPort { get; set; } = 6004;
    [Reactive, ModelBindsTo(nameof(ShowSettings.mscTXPort))] public int MAMSCTXPort { get; set; } = 6004;
    [Reactive, ModelBindsTo(nameof(ShowSettings.mscRXDevice))] public int MAMSCRXDevice { get; set; } = 0x70;
    [Reactive, ModelBindsTo(nameof(ShowSettings.mscTXDevice))] public int MAMSCTXDevice { get; set; } = 0x71;
    [Reactive, ModelBindsTo(nameof(ShowSettings.mscPage))] public int MAMSCPage { get; set; } = -1;
    [Reactive, ModelBindsTo(nameof(ShowSettings.mscExecutor))] public int MAMSCExecutor { get; set; } = -1;
    [Reactive, ModelSkip] public bool MonitorMSCMessages { get; set; } = false;

    [Reactive] public RelayCommand<RemoteNodeViewModel> RemoveRemoteNodeCommand { get; private set; }
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

    private (object? key, string identifier)[] audioOutputDevices = [];
    private volatile bool suppressAudioDeviceQuery = false;
    private readonly MainViewModel mainViewModel;
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
                case nameof(AudioOutputDriver):
                    // Try to be friendly and set the latency to a value that's likely to work.
                    switch (AudioOutputDriver)
                    {
                        case AudioOutputDriver.Wave:
                            if (AudioLatency < 50)
                                AudioLatency = 100;
                            break;
                        case AudioOutputDriver.DirectSound:
                            if (AudioLatency < 20)
                                AudioLatency = 50;
                            break;
                    }

                    if (suppressAudioDeviceQuery)
                        break;

                    // Refresh the audio output devices asynchronously
                    audioOutputDevices = [(null, string.Empty)];
                    OnPropertyChanged(nameof(AudioOutputDevices));
                    var sc = SynchronizationContext.Current;
                    mainViewModel.AudioPlaybackManager.GetOutputDevices(AudioOutputDriver).ContinueWith(x =>
                    {
                        audioOutputDevices = x.Result;
                        sc?.Post(x => OnPropertyChanged(nameof(AudioOutputDevices)), null);
                    });
                    break;
                case nameof(AudioLatency):
                case nameof(SelectedAudioOutputDeviceIndex):
                    if (suppressAudioDeviceQuery)
                        break;
                    int ind = Math.Max(0, SelectedAudioOutputDeviceIndex);
                    if (ind < audioOutputDevices.Length)
                        mainViewModel.OpenAudioDevice();
                    break;
                case nameof(SelectedNIC):
                case nameof(OSCRXPort):
                case nameof(OSCTXPort):
                case nameof(IsRemoteHost):
                case nameof(EnableRemoteControl):
                case nameof(MAMSCRXPort):
                case nameof(MAMSCTXPort):
                    mainViewModel.OSCManager.ConnectOSC();
                    mainViewModel.MSCManager.ConnectMSC();
                    break;
                case nameof(MonitorOSCMessages):
                    mainViewModel.OSCManager.MonitorOSC(MonitorOSCMessages);
                    break;
                case nameof(MonitorMSCMessages):
                    mainViewModel.MSCManager.MonitorMSC(MonitorMSCMessages);
                    break;
                    //case nameof(EnableRemoteControl):
                    //    break;
            }
        };

        _ = AudioOutputDevices; // Update the list of audio devices...
        //if (SelectedAudioOutputDeviceIndex < audioOutputDevices.Length)
        //    mainViewModel.OpenAudioDevice();

        RemoveRemoteNodeCommand = new(item =>
        {
            if (item != null)
                RemoveRemoteNode(item.Name);
        });

        QueryNICs();
    }

    public override void SyncFromModel()
    {
        suppressAudioDeviceQuery = true;
        base.SyncFromModel();
        suppressAudioDeviceQuery = false;

        if (boundModel == null)
            return;

        // The audio device list gets populated asynchronously, defer selecting the device until the list is populated.
        var sc = SynchronizationContext.Current;
        mainViewModel.AudioPlaybackManager.GetOutputDevices(AudioOutputDriver).ContinueWith(x =>
        {
            audioOutputDevices = x.Result;
            int ind = audioOutputDevices.Select(x => x.identifier).IndexOf(boundModel.audioOutputDevice);
            sc?.Post(x => {
                OnPropertyChanged(nameof(AudioOutputDevices));
                SelectedAudioOutputDeviceIndex = ind;
            }, null);
        });

        foreach (var node in boundModel.remoteNodes)
        {
            RemoteNodeViewModel remote = new(node, this);
            if (remoteNodesDict.TryAdd(remote.Name, remote))
                remoteNodes.Add(remote);
        }
    }

    public override void SyncToModel()
    {
        base.SyncToModel();

        if (boundModel == null)
            return;
        boundModel.remoteNodes = remoteNodes.Select(x => new RemoteNode(x.Name, x.Address)).Distinct().ToList();
    }

    private static void M2VM_AudioOutputDevice(ProjectSettingsViewModel vm, ShowSettings m) { }
    private static void VM2M_AudioOutputDevice(ProjectSettingsViewModel vm, ShowSettings m)
    { 
        var devices = vm.AudioOutputDevices;
        m.audioOutputDevice = devices.Count == 0 ? string.Empty : devices[Math.Clamp(vm.SelectedAudioOutputDeviceIndex, 0, devices.Count)];
    }

    private static void M2VM_OSCNic(ProjectSettingsViewModel vm, ShowSettings m)
    {
        if (IPAddress.TryParse(m.oscNIC, out var ip))
            vm.SelectedNIC = vm.nicAddresses.FindIndex(x => x.addr.Equals(ip));
    }
    private static void VM2M_OSCNic(ProjectSettingsViewModel vm, ShowSettings m) => m.oscNIC = vm.OSCNic.ToString();

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
