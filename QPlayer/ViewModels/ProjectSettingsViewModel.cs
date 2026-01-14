using CommunityToolkit.Mvvm.Input;
using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.Utilities;
using QPlayer.Views;
using QPlayer.SourceGenerator;
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
public partial class ProjectSettingsViewModel : BindableViewModel<ShowSettings>
{
    #region Bindable Properties
    [Reactive] private string title = "Untitled";
    [Reactive] private string description = string.Empty;
    [Reactive] private string author = string.Empty;
    [Reactive] private DateTime date;

    [Reactive] private int audioLatency;
    [Reactive] private AudioOutputDriver audioOutputDriver;
    [Reactive, ModelCustomBinding(nameof(VM2M_AudioOutputDevice), nameof(M2VM_AudioOutputDevice))]
    public int selectedAudioOutputDeviceIndex;

    [Reactive, PrivateSetter, ModelSkip] private static ObservableCollection<AudioOutputDriver>? audioOutputDriverValues;

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

    [Reactive("NICs")] private readonly ObservableCollection<string> nics = [];
    [Reactive, ModelCustomBinding(nameof(VM2M_OSCNic), nameof(M2VM_OSCNic))]
    private int selectedNIC;
    [Reactive("OSCRXPort")] private int oscRXPort = 9000;
    [Reactive("OSCTXPort")] private int oscTXPort = 8000;
    [Reactive, ModelSkip] private bool monitorOSCMessages = false;

    [Reactive] private bool enableRemoteControl;
    [Reactive] private bool isRemoteHost;
    [Reactive] private bool syncShowFileOnSave;
    [Reactive] private string nodeName = string.Empty;
    [Reactive] private readonly ReadOnlyObservableCollection<RemoteNodeViewModel> remoteNodes;

    [Reactive, ModelBindsTo(nameof(ShowSettings.mscRXPort))] private int mAMSCRXPort = 6004;
    [Reactive, ModelBindsTo(nameof(ShowSettings.mscTXPort))] private int mAMSCTXPort = 6004;
    [Reactive, ModelBindsTo(nameof(ShowSettings.mscRXDevice))] private int mAMSCRXDevice = 0x70;
    [Reactive, ModelBindsTo(nameof(ShowSettings.mscTXDevice))] private int mAMSCTXDevice = 0x71;
    [Reactive, ModelBindsTo(nameof(ShowSettings.mscPage))] private int mAMSCPage = -1;
    [Reactive, ModelBindsTo(nameof(ShowSettings.mscExecutor))] private int mAMSCExecutor = -1;
    [Reactive, ModelSkip] private bool monitorMSCMessages = false;

    [Reactive] private readonly RelayCommand<RemoteNodeViewModel> removeRemoteNodeCommand;
    [Reactive] private readonly MainViewModel mainViewModel;
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
    private readonly List<(IPAddress addr, IPAddress subnet)> nicAddresses = [];

    private readonly StringDict<RemoteNodeViewModel> remoteNodesDict = [];
    private readonly ObservableCollection<RemoteNodeViewModel> remoteNodesOb = [];

    public ProjectSettingsViewModel(MainViewModel mainViewModel)
    {
        remoteNodes = new(remoteNodesOb);
        this.mainViewModel = mainViewModel;
        audioOutputDriverValues ??= new(Enum.GetValues<AudioOutputDriver>());
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

        removeRemoteNodeCommand = new(item =>
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
            int ind = audioOutputDevices.IndexOf(x => x.identifier, boundModel.audioOutputDevice);
            sc?.Post(x => {
                OnPropertyChanged(nameof(AudioOutputDevices));
                SelectedAudioOutputDeviceIndex = ind;
            }, null);
        });

        foreach (var node in boundModel.remoteNodes)
        {
            RemoteNodeViewModel remote = new(node, this);
            if (remoteNodesDict.TryAdd(remote.Name, remote))
                remoteNodesOb.Add(remote);
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
                ret = remoteNodesOb.Remove(node);
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
            remoteNodesOb.Add(node);
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
            remoteNodesOb.Add(node);
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
