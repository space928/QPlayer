using QPlayer.Models;
using QPlayer.ViewModels;
using QPlayer.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

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

    public void AddMenuItem(string menu, string? subMenu, MenuItem menuItem)
    {
        // TOOD: Check that x.Header is actually a string and not a label.
        if (MainMenu.Items.OfType<MenuItem>().FirstOrDefault(x => x.Header is string text && text == menu) is MenuItem parentMenu)
            parentMenu.Items.Add(menuItem);
        else
            MainMenu.Items.Add(menuItem);
    }

    public void RegisterCueType(CueFactory.RegisteredCueType cue)
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

    public void Window_Loaded(object sender, RoutedEventArgs e)
    {
        keyBindings.Clear();
        foreach (object binding in InputBindings)
            if (binding is KeyBinding keyBinding)
                keyBindings.Add((keyBinding.Key, keyBinding.Modifiers), keyBinding);
    }

    public void Window_Closing(object sender, CancelEventArgs e)
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
                e.Handled = true;
                var mods = Keyboard.Modifiers;
                if (keyBindings.TryGetValue((e.Key, mods), out var binding))
                    binding.Command.Execute(null);
                break;
        }
    }

    private void CueList_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        base.OnGiveFeedback(e);

        var vm = (MainViewModel)DataContext;
        if (e.Effects.HasFlag(DragDropEffects.Copy))
            Mouse.SetCursor(Cursors.Cross);
        else if (e.Effects.HasFlag(DragDropEffects.Move))
            Mouse.SetCursor(Cursors.Hand);
        else
            Mouse.SetCursor(Cursors.No);

        /*if (vm.DraggingCues.Count > 0)
        {
            if (DraggingItemsPanel.Visibility != Visibility.Visible)
                DraggingItemsPanel.Visibility = Visibility.Visible;

            //var mousePos = Mouse.GetPosition((Panel)DraggingItemsPanel.Parent);
            var mousePos = Mouse.PrimaryDevice.GetPosition((Panel)DraggingItemsPanel.Parent);
            Debug.WriteLine(mousePos);
            DraggingItemsPanel.Margin = new(mousePos.X+2, mousePos.Y+2, 0, 0);
        }*/

        e.Handled = true;
    }

    private void CueList_Drop(object sender, DragEventArgs e)
    {
        base.OnDrop(e);

        var vm = (MainViewModel)DataContext;
        HandleCueListDrop(e, vm, null);
    }

    internal static void HandleCueListDrop(DragEventArgs e, MainViewModel vm, CueViewModel? dropTargetVm)
    {
        int dstIndex;
        if (dropTargetVm == null)
            dstIndex = vm.Cues.Count;
        else
            dstIndex = vm.Cues.IndexOf(dropTargetVm);

        if (e.Data.GetDataPresent("Cues"))
        {
            CueViewModel[] dataCues = (CueViewModel[])e.Data.GetData("Cues"); // The items being drag/dropped

            foreach (var dataCue in dataCues.Reverse())
            {
                if (e.KeyStates.HasFlag(DragDropKeyStates.ControlKey))
                {
                    e.Effects = DragDropEffects.Copy;

                    var copy = vm.DuplicateCueExecute(dataCue);
                    if (copy != null)
                        vm.MoveCue(copy, dstIndex);
                }
                else
                {
                    e.Effects = DragDropEffects.Move;

                    vm.MoveCue(dataCue, dstIndex);
                }
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
                                if (vm.CreateCue(nameof(SoundCue), afterLast: true) is not SoundCueViewModel cue)
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
                        default:
                            break;
                    }
                }
            }
        }
        e.Handled = true;
    }

    private void QPlayerMainWindow_DragOver(object sender, DragEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        if (vm.DraggingCues.Count > 0)
        {
            if (DraggingItemsPanel.Visibility != Visibility.Visible)
                DraggingItemsPanel.Visibility = Visibility.Visible;

            //var mousePos = Mouse.GetPosition((Panel)DraggingItemsPanel.Parent);
            var mousePos = e.GetPosition((Panel)DraggingItemsPanel.Parent);
            //Debug.WriteLine(mousePos);
            DraggingItemsPanel.Margin = new(mousePos.X + 2, mousePos.Y + 2, 0, 0);
        }
    }

    private void QPlayerMainWindow_MouseMove(object sender, MouseEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        if (vm.DraggingCues.Count == 0 && DraggingItemsPanel.Visibility == Visibility.Visible)
            DraggingItemsPanel.Visibility = Visibility.Hidden;
    }

    private void StatusBarText_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        vm.OpenWindow<LogWindow>();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        if (vm.UnsavedChangedCheck())
            Close();
    }

    private void OverlayConsume_KeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
    }
}
