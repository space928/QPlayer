using CommunityToolkit.Mvvm.ComponentModel;
using QPlayer.Models;
using QPlayer.SourceGenerator;
using System;
using System.Net;

namespace QPlayer.ViewModels;

public partial class RemoteNodeViewModel : ObservableObject
{
    [Reactive] private string name = string.Empty;
    public string Address => IPAddress?.ToString() ?? string.Empty;
    public bool IsActive => DateTime.UtcNow - LastDiscoveryTime < DiscoveryTimeout;

    public IPAddress? IPAddress
    {
        get => ipAddress;
        set
        {
            ipAddress = value;
            //OscSender = new(projectSettings.OSCNic, 0, ipAddress, projectSettings.OSCTXPort, 8, 600, 2048);
            OnPropertyChanged(nameof(Address));
        }
    }
    public DateTime LastDiscoveryTime { get; set; }
    //public OscSender OscSender { get; private set; }

    private bool wasActive;
    private IPAddress? ipAddress;

    private readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(5);
    private readonly ProjectSettingsViewModel projectSettings;

    public RemoteNodeViewModel(string name, ProjectSettingsViewModel projectSettings)
    {
        Name = name;
        this.projectSettings = projectSettings;
    }

    public RemoteNodeViewModel(RemoteNode remoteNode, ProjectSettingsViewModel projectSettings)
    {
        Name = remoteNode.name;
        this.projectSettings = projectSettings;
        if (IPAddress.TryParse(remoteNode.address, out var ipAddress))
            IPAddress = ipAddress;

        LastDiscoveryTime = DateTime.MinValue;
    }

    /// <summary>
    /// Checks if this node is still active, and notifies the UI if it's state has changed.
    /// </summary>
    /// <returns><see langword="true"/> if this node has just become inactive.</returns>
    public bool CheckIsActive()
    {
        bool nowActive = IsActive;
        if (nowActive != wasActive)
        {
            wasActive = nowActive;
            OnPropertyChanged(nameof(IsActive));
            projectSettings.OnRemoteNodeStatusChanged(this);

            return !nowActive;
        }

        return false;
    }
}
