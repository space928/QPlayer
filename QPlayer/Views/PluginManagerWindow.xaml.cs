using NAudio.Wave;
using QPlayer.Models;
using QPlayer.SourceGenerator;
using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace QPlayer.Views;

/// <summary>
/// Interaction logic for PluginManagerWindow.xaml
/// </summary>
public partial class PluginManagerWindow : Window, INotifyPropertyChanged, INotifyPropertyChanging
{
    [Reactive, Readonly] public ObservableCollection<PluginManagerPluginViewModel> plugins = [];
    public PluginManagerPluginViewModel? SelectedPlugin => PluginListBox.SelectedItem as PluginManagerPluginViewModel;

    private readonly MainViewModel mainVM;

    public PluginManagerWindow(MainViewModel vm)
    {
        this.mainVM = vm;
        InitializeComponent();

        DataContext = this;
        CreatePlugins();

        PluginListBox.SelectionChanged += (o, e) => OnPropertyChanged(nameof(SelectedPlugin));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event PropertyChangingEventHandler? PropertyChanging;

    private void CreatePlugins()
    {
        Plugins.Clear();

        string qplayerAssembly = Assembly.GetEntryAssembly()?.FullName ?? string.Empty;
        string qplayerVersion = mainVM.VersionString;
        if (qplayerVersion.StartsWith("Version "))
            qplayerVersion = qplayerVersion[8..];
        Plugins.Add(new("QPlayer Core", mainVM.CopyrightString, qplayerVersion, "The core QPlayer component, which defines all the built in cue types.", qplayerAssembly,
                new(CueFactory.RegisteredCueTypes.Where(x => x.assembly == qplayerAssembly)
                .Select(x => new PluginManagerRegisteredCueViewModel(x.displayName, (DrawingImage?)App.Current.TryFindResource(x.iconName))))));

        foreach (var plugin in PluginLoader.LoadedPlugins.Values)
        {
            Plugins.Add(new(plugin.Name, plugin.Author, plugin.Version, plugin.Description, plugin.assembly.FullName ?? string.Empty,
                new(plugin.registeredCueTypes
                .Select(x => new PluginManagerRegisteredCueViewModel(x.displayName, (DrawingImage?)App.Current.TryFindResource(x.iconName))))));
        }
    }
}

public record PluginManagerPluginViewModel(string Name, string Author, string Version, string Description,
    string Assembly, ObservableCollection<PluginManagerRegisteredCueViewModel> CueTypes);
public record PluginManagerRegisteredCueViewModel(string Name, DrawingImage? Icon);
