using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QPlayer.SourceGenerator;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.ViewModels;

public partial class RemoteNodesWindowViewModel : ObservableObject
{
    #region Bindable Properties
    [Reactive, Readonly] private readonly ReadOnlyObservableCollection<RemoteNodeGroupViewModel> cueRemoteNodes;

    [Reactive, Readonly] private ReadOnlyObservableCollection<RemoteNodeViewModel> remoteNodes;
    [Reactive, Readonly] private ProjectSettingsViewModel projectSettings;
    //[Reactive] public string RemoteNode { get; set; }

    [Reactive, Readonly] private readonly RelayCommand refreshCueListCommand;

    public MainViewModel MainViewModel => mainViewModel;
    #endregion

    private readonly MainViewModel mainViewModel;
    private readonly ObservableCollection<RemoteNodeGroupViewModel> cueRemoteNodesCollection = [];

    public RemoteNodesWindowViewModel(MainViewModel mainViewModel)
    {
        this.mainViewModel = mainViewModel;
        cueRemoteNodes = new(cueRemoteNodesCollection);

        mainViewModel.PropertyChanged += (o, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ProjectSettings))
            {
                BindProjectSettings(mainViewModel.ProjectSettings);
            }
        };
        BindProjectSettings(mainViewModel.ProjectSettings);

        refreshCueListCommand = new(RefreshCueListExecute);
        RefreshCueListExecute();
    }

    [MemberNotNull(nameof(projectSettings), nameof(remoteNodes))]
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
        foreach (var vm in cueRemoteNodesCollection)
            if (vm.RemoteNode == remoteNode.Name)
                vm.NotifyActiveNodeChanged();
    }

    private void RefreshCueListExecute()
    {
        foreach (var cue in cueRemoteNodesCollection) 
            cue.Dispose();
        cueRemoteNodesCollection.Clear();

        var vms = mainViewModel.Cues
            .GroupBy(x => x.RemoteNode)
            .Select(x => new RemoteNodeGroupViewModel(mainViewModel, x.Key, x));

        foreach (var vm in vms)
            cueRemoteNodesCollection.Add(vm);
    }
}

public partial class RemoteNodeGroupViewModel : ObservableObject, IDisposable
{
    [Reactive] private string remoteNode;
    public bool IsLocalNode => RemoteNode == mainViewModel.ProjectSettings.NodeName;
    public bool IsActiveNode => mainViewModel.ProjectSettings.IsRemoteNodeActive(RemoteNode);
    [Reactive, Readonly] private ReadOnlyCollection<CueViewModel> cues;

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
