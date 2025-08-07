using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.ViewModels;

public class RemoteNodesWindowViewModel : ObservableObject
{
    #region Bindable Properties
    [Reactive] public ReadOnlyObservableCollection<RemoteNodeGroupViewModel> CueRemoteNodes { get; private set; }

    [Reactive] public ReadOnlyObservableCollection<RemoteNodeViewModel> RemoteNodes { get; private set; }
    [Reactive] public ProjectSettingsViewModel ProjectSettings { get; private set; }
    //[Reactive] public string RemoteNode { get; set; }

    [Reactive] public RelayCommand RefreshCueListCommand { get; private set; }

    public MainViewModel MainViewModel => mainViewModel;
    #endregion

    private readonly MainViewModel mainViewModel;
    private readonly ObservableCollection<RemoteNodeGroupViewModel> cueRemoteNodes = [];

    public RemoteNodesWindowViewModel(MainViewModel mainViewModel)
    {
        this.mainViewModel = mainViewModel;
        CueRemoteNodes = new(cueRemoteNodes);

        mainViewModel.PropertyChanged += (o, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ProjectSettings))
            {
                BindProjectSettings(mainViewModel.ProjectSettings);
            }
        };
        BindProjectSettings(mainViewModel.ProjectSettings);

        RefreshCueListCommand = new(RefreshCueListExecute);
        RefreshCueListExecute();
    }

    [MemberNotNull(nameof(ProjectSettings), nameof(RemoteNodes))]
    private void BindProjectSettings(ProjectSettingsViewModel projectSettings)
    {
        UnbindProjectSettings();
        ProjectSettings = projectSettings;
        RemoteNodes = projectSettings.RemoteNodes;
        projectSettings.PropertyChanged += HandleProjectSettingPropertyChanged;
        ProjectSettings.RemoteNodeStatusChanged += HandleRemoteNodeStatusChanged;
    }

    private void UnbindProjectSettings()
    {
        if (ProjectSettings == null)
            return;

        ProjectSettings.PropertyChanged -= HandleProjectSettingPropertyChanged;
        ProjectSettings.RemoteNodeStatusChanged -= HandleRemoteNodeStatusChanged;
    }

    private void HandleProjectSettingPropertyChanged(object? o, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ProjectSettingsViewModel.NodeName):
                foreach (var cue in cueRemoteNodes)
                    cue.NotifyLocalNodeChanged();
                break;
        }
    }

    private void HandleRemoteNodeStatusChanged(RemoteNodeViewModel remoteNode)
    {
        foreach (var vm in cueRemoteNodes)
            if (vm.RemoteNode == remoteNode.Name)
                vm.NotifyActiveNodeChanged();
    }

    private void RefreshCueListExecute()
    {
        foreach (var cue in cueRemoteNodes) 
            cue.Dispose();
        cueRemoteNodes.Clear();

        var vms = mainViewModel.Cues
            .GroupBy(x => x.RemoteNode)
            .Select(x => new RemoteNodeGroupViewModel(mainViewModel, x.Key, x));

        foreach (var vm in vms)
            cueRemoteNodes.Add(vm);
    }
}

public class RemoteNodeGroupViewModel : ObservableObject, IDisposable
{
    [Reactive] public string RemoteNode { get; set; }
    [Reactive] public bool IsLocalNode => RemoteNode == mainViewModel.ProjectSettings.NodeName;
    [Reactive] public bool IsActiveNode => mainViewModel.ProjectSettings.IsRemoteNodeActive(RemoteNode);
    [Reactive] public ReadOnlyCollection<CueViewModel> Cues { get; private set; }

    private readonly MainViewModel mainViewModel;

    public RemoteNodeGroupViewModel(MainViewModel mainViewModel, string remoteNode, IEnumerable<CueViewModel> cues)
    {
        this.mainViewModel = mainViewModel;
        RemoteNode = remoteNode;
        Cues = new(cues.ToList());
        Bind();
    }

    private void Bind()
    {
        PropertyChanged += (o, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(RemoteNode):
                    foreach (var cue in Cues)
                        cue.RemoteNode = RemoteNode;

                    OnPropertyChanged(nameof(IsLocalNode));
                    OnPropertyChanged(nameof(IsActiveNode));
                    break;
            }
        };
    }

    public void NotifyLocalNodeChanged()
    {
        OnPropertyChanged(nameof(IsLocalNode));
    }

    internal void NotifyActiveNodeChanged()
    {
        OnPropertyChanged(nameof(IsActiveNode));
    }

    public void Dispose()
    {
        //throw new NotImplementedException();
    }
}
