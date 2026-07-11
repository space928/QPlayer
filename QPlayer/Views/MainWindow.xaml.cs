using QPlayer.Models;
using QPlayer.ViewModels;
using QPlayer.Views;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace QPlayer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly Dictionary<(Key key, ModifierKeys modifiers), KeyBinding> keyBindings = [];
    private readonly object[] builtInCueTypes;

    public MainWindow()
    {
        InitializeComponent();

        // Data bindings suck, just subscribe to the event we care about...
        CueListScrollViewer.ScrollChanged += (o, e) =>
        {
            CueListHeader.Margin = new Thickness(-e.HorizontalOffset, 0, 0, 0);
        };

        builtInCueTypes = new object[CueEditorInst.CueEditorTemplates.Count];
        CueEditorInst.CueEditorTemplates.Keys.CopyTo(builtInCueTypes, 0);
        RegisterCueTypes();
        // var vm = (MainViewModel)DataContext;
        // vm.OnRegisterCueTypes += RegisterCueTypes;
    }

    private void RegisterCueTypes()
    {
        // Ugly way to clear existing cue types except for the built in ones, replace this with a clear once all cues use a separate cue type.
        foreach (var template in new List<object>(CueEditorInst.CueEditorTemplates.Keys.Cast<object>()))
            if (!builtInCueTypes.Contains(template))
                CueEditorInst.CueEditorTemplates.Remove(template);

        foreach (var t in CueFactory.RegisteredCueTypes)
            RegisterCueType(t);
    }

    internal void AddMenuItem(string path, ICommand command, string text)
    {
        MenuItem item = new();
        item.Header = text;
        item.Command = command;
        AddMenuItem(path, item);
    }

    internal void AddMenuItem(string path, MenuItem menuItem)
    {
        var parts = path.Split('/');
        if (path.Length == 0)
        {
            MainMenu.Items.Add(menuItem);
            return;
        }

        ItemsControl menu = MainMenu;
        foreach (var part in parts) 
        {
            var item = menu.Items
                .OfType<MenuItem>()
                .FirstOrDefault(x => x.Header switch
                    {
                        string text => text == part,
                        Label lab => lab.Content is string text && text == part,
                        TextBlock block => block.Text == part,
                        _ => false
                    });
            if (item is MenuItem parentMenu)
            {
                menu = parentMenu;
            }
            else
            {
                var newItem = new MenuItem();
                newItem.Header = part;
                menu.Items.Add(newItem);
                menu = newItem;
            }
        }
   
        menu.Items.Add(menuItem);
    }

    internal void RegisterCueType(CueFactory.RegisteredCueType cue)
    {
        // Create the cue icon
        if (cue.iconName != null && cue.iconResourceDict != null)
        {
            try
            {
                if (!App.Current.Resources.MergedDictionaries.Any(x => cue.iconResourceDict.IsAssignableFrom(x.GetType())))
                    App.Current.Resources.MergedDictionaries.Add((ResourceDictionary)Activator.CreateInstance(cue.iconResourceDict)!);

                if (App.Current.TryFindResource(cue.iconName) is not DrawingImage img)
                    throw new KeyNotFoundException($"Couldn't find cue icon with key '{cue.iconName}' in {cue.iconResourceDict.Name}");
                CueDataControl.cueIcons.TryAdd(cue.name, img);
            }
            catch (Exception ex)
            {
                MainViewModel.Log($"Failed to create icon for cue type '{cue.name}': {ex}", MainViewModel.LogLevel.Warning);
            }
        }

        // Skip built in cue types // At some point in the future we might remove this and unify cue registration
        if (cue.viewType.Name == nameof(QPlayer.Views.CueEditor))
            return;

        DataTemplate dataTemplate;
        if (cue.viewType.GetMethod(nameof(ICueView.CreateDataTemplate), BindingFlags.Public | BindingFlags.Static) is MethodInfo genView)
            dataTemplate = (DataTemplate)genView.Invoke(null, null)!;
        else
            dataTemplate = (DataTemplate)Activator.CreateInstance(cue.viewType)!;

        CueEditorInst.CueEditorTemplates.Add(new DataTemplateKey(cue.viewModelType), dataTemplate);

        // Add the cue to the various context menus
        // TODO: Tidy this up, it's a little ugly.
        var menuItem = new MenuItem();
        menuItem.Header = $"Add {cue.displayName}";
        menuItem.SetBinding(MenuItem.CommandProperty, "CreateCueCommand");
        menuItem.CommandParameter = cue.name;
        var menuItem1 = new MenuItem();
        menuItem1.Header = $"Add {cue.displayName}";
        menuItem1.SetBinding(MenuItem.CommandProperty, "CreateCueCommand");
        menuItem1.CommandParameter = cue.name;

        EditMenuCreateCueMenu.Items.Add(menuItem);
        int insertInd = 0;
        for (; insertInd < CueListContextMenu.Items.Count; insertInd++)
            if (CueListContextMenu.Items[insertInd] is Separator)
                break;
        CueListContextMenu.Items.Insert(insertInd, menuItem1);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        keyBindings.Clear();
        foreach (object binding in InputBindings)
            if (binding is KeyBinding keyBinding)
                keyBindings.Add((keyBinding.Key, keyBinding.Modifiers), keyBinding);

        var vm = (MainViewModel)DataContext;
        vm.OnScrollCueList += delta =>
        {
            CueListScrollViewer.ScrollToVerticalOffset(CueListScrollViewer.ContentVerticalOffset + delta.Y);
            CueListScrollViewer.ScrollToHorizontalOffset(CueListScrollViewer.ContentHorizontalOffset + delta.X);
        };

        if (CueListControl.ItemsSource is INotifyCollectionChanged notifyCollection)
            notifyCollection.CollectionChanged += VisualCuesCollectionChanged;
    }

    private void VisualCuesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        int count = CueListControl.Items.Count;
        var generator = CueListControl.ItemContainerGenerator;
        if (e.OldStartingIndex == -1 && e.NewStartingIndex == -1)
        {
            for (int i = 0; i < count; i++)
            {
                if (!GetCueDataControl(i, out var item))
                    continue;

                item.NotifyGroupMarkerChange(i);
            }
        }
        else
        {
            if (e.NewStartingIndex != -1)
            {
                for (int i = Math.Max(0, e.NewStartingIndex - 1); i < Math.Min(e.NewStartingIndex + 2, count); i++)
                {
                    if (!GetCueDataControl(i, out var item))
                        continue;

                    item.NotifyGroupMarkerChange(i);
                }
            }
            if (e.OldStartingIndex != -1)
            {
                for (int i = Math.Max(0, e.OldStartingIndex - 1); i < Math.Min(e.OldStartingIndex + 2, count); i++)
                {
                    if (!GetCueDataControl(i, out var item))
                        continue;

                    item.NotifyGroupMarkerChange(i);
                }
            }
        }

        bool GetCueDataControl(int ind, [NotNullWhen(true)] out CueDataControl? control)
        {
            var container = generator.ContainerFromIndex(ind);
            if (container != null
                && VisualTreeHelper.GetChildrenCount(container) > 0
                && VisualTreeHelper.GetChild(container, 0) is CueDataControl cdc)
            {
                control = cdc;
                return true;
            }

            control = null;
            return false;
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        e.Cancel = !((MainViewModel)DataContext).OnExit();
    }

    private void Consume_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Don't consume keys for text fields inside the cue list
        if (e.OriginalSource is TextBox element)
        {
            FrameworkElement? fwElement = element;
            do
            {
                fwElement = fwElement.Parent as FrameworkElement;
                if (fwElement is CueDataControl)
                    return;
            } while (fwElement != null);
        }

        // Override list viewer key bindings
        switch (e.Key)
        {
            case Key.Space:
            case Key.Up:
            case Key.Down:
            case Key.Left:
            case Key.Right:
                e.Handled = true;
                var mods = Keyboard.Modifiers;
                if (keyBindings.TryGetValue((e.Key, mods), out var binding))
                    binding.Command.Execute(null);
                break;
        }
    }

    internal static void HandleCueListDrop(DragEventArgs e, MainViewModel vm, CueViewModel? dropTargetVm)
    {
        int dstIndex;
        if (dropTargetVm == null)
            dstIndex = vm.Cues.Count;
        else
            dstIndex = vm.FindCueIndex(dropTargetVm);

        if (e.Data.GetDataPresent("Cues"))
        {
            CueViewModel[] dataCues = (CueViewModel[])e.Data.GetData("Cues"); // The items being drag/dropped

            if (e.Effects.HasFlag(DragDropEffects.Copy))
            {
                vm.DuplicateCues(dataCues, dstIndex);
            }
            else if (e.Effects.HasFlag(DragDropEffects.Move))
            {
                vm.MoveCues(dataCues, dstIndex);
            }
            else if (e.Effects.HasFlag(DragDropEffects.Link))
            {
                vm.GroupCues(dataCues, vm.Cues[dstIndex]);
            }

            vm.DraggingCues.Clear();
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop, true) is string[] files)
            {
                foreach (var file in files)
                {
                    switch (System.IO.Path.GetExtension(file).ToLowerInvariant())
                    {
                        case ".mp3":
                        case ".wav":
                        case ".aif":
                        case ".aiff":
                        case ".flac":
                        case ".ogg":
                        case ".wma":
                            {
                                if (vm.CreateCue(nameof(SoundCue)) is not SoundCueViewModel cue)
                                    break;
                                cue.Path = file;
                                cue.Name = System.IO.Path.GetFileNameWithoutExtension(file);
                                vm.MoveCue(cue, dstIndex++);
                            }
                            break;
                        //*.mp4;*.mkv;*.avi;*.webm;*.flv;*.wmv;*.mov
                        case ".mp4":
                        case ".mkv":
                        case ".avi":
                        case ".webm":
                        case ".flv":
                        case ".wmv":
                        case ".mov":
                            /*{
                                var cue = (VideoCueViewModel)vm.CreateCue(Models.CueType.VideoCue, afterLast: true);
                                cue.Path = file;
                                cue.Name = System.IO.Path.GetFileNameWithoutExtension(file);
                                vm.MoveCue(cue, dstIndex++);
                            }*/
                            break;
                        case ".qproj":
                            {
                                vm.OpenSpecificProjectExecute(file);
                                break;
                            }
                        default:
                            break;
                    }
                }
            }
        }
        e.Handled = true;
    }

    private void CueList_Drop(object sender, DragEventArgs e)
    {
        ComputeDragEffects(e);
        DraggingItemsPanel.Visibility = Visibility.Collapsed;
        if (DataContext is MainViewModel vm)
            HandleCueListDrop(e, vm, null);
        e.Handled = true;
    }

    private void ComputeDragEffects(DragEventArgs e)
    {
        if (!e.Effects.HasFlag(DragDropEffects.Scroll))
            e.Effects |= DragDropEffects.Scroll;
        else
        {
            // Set the drag effects if the targeted CueDataControl? hasn't already
            if (e.KeyStates.HasFlag(DragDropKeyStates.ControlKey))
                e.Effects = DragDropEffects.Copy | DragDropEffects.Scroll;
            else
                e.Effects = DragDropEffects.Move | DragDropEffects.Scroll;
        }
    }

    private void QPlayerMainWindow_DragOver(object sender, DragEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        if (vm.DraggingCues.Count <= 0)
            return;

        if (DraggingItemsPanel.Visibility != Visibility.Visible)
            DraggingItemsPanel.Visibility = Visibility.Visible;

        //var oldFX = e.Effects;
        ComputeDragEffects(e);

        //Debug.WriteLine($"MainWindow DragOver handled={e.Handled} o={oldFX} e={e.Effects}");

        var mousePos = e.GetPosition((Panel)DraggingItemsPanel.Parent);
        DraggingItemsPanel.Margin = new(mousePos.X + 2, mousePos.Y + 2, 0, 0);

        // Scrolling
        mousePos = e.GetPosition(CueListScrollViewer);
        if (mousePos.Y < 50)
            CueListScrollViewer.ScrollToVerticalOffset(CueListScrollViewer.VerticalOffset - 15);
        else if (mousePos.Y > CueListScrollViewer.ActualHeight - 50)
            CueListScrollViewer.ScrollToVerticalOffset(CueListScrollViewer.VerticalOffset + 15);

        e.Handled = true;
    }

    private void CueList_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        //Debug.WriteLine($"MainWindow GiveFeedback handled={e.Handled} e={e.Effects}");

        base.OnGiveFeedback(e);

        if ((e.Effects & (DragDropEffects.Link | DragDropEffects.Move | DragDropEffects.Copy)) != 0)
            Mouse.SetCursor(Cursors.Hand);
        else
            Mouse.SetCursor(Cursors.No);

        e.Handled = true;
    }

    private void QPlayerMainWindow_MouseMove(object sender, MouseEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        if (vm.DraggingCues.Count == 0 && DraggingItemsPanel.Visibility == Visibility.Visible)
            DraggingItemsPanel.Visibility = Visibility.Collapsed;
    }

    private void StatusBarText_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        vm.OpenWindow<LogWindow>();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        Dispatcher.Invoke(() =>
        {
            if (vm.UnsavedChangedCheck())
                Close();
        });
    }

    private void OverlayConsume_KeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
    }

    private void AudioActiveText_MouseDown(object sender, MouseButtonEventArgs e)
    {
        CueEditor.SelectedItem = ProjectSetupTabItem;
        Dispatcher.Invoke(ProjectSettingsEditorInst.AudioSetupHeader.BringIntoView);
        var vm = (MainViewModel)DataContext;
        if (!vm.IsAudioActive)
            vm.OpenAudioDevice();
    }

    private void ShowMode_Checked(object sender, RoutedEventArgs e)
    {
        CueListControl.Focus();
    }
}
