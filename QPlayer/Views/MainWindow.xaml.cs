using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using QPlayer.ViewModels;

namespace QPlayer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly Dictionary<(Key key, ModifierKeys modifiers), KeyBinding> keyBindings = [];

    public MainWindow()
    {
        InitializeComponent();

        // Data bindings suck, just subscribe to the event we care about...
        CueListScrollViewer.ScrollChanged += (o, e) =>
        {
            CueListHeader.Margin = new Thickness(-e.HorizontalOffset, 0, 0, 0);
        };
    }

    public void Window_Loaded(object sender, RoutedEventArgs e)
    {
        keyBindings.Clear();
        foreach (object binding in InputBindings)
            if (binding is KeyBinding keyBinding)
                keyBindings.Add((keyBinding.Key, keyBinding.Modifiers), keyBinding);
    }

    public void Window_Closed(object sender, EventArgs e)
    {
        ((MainViewModel)DataContext).OnExit();
    }

    private void Consume_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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
                            var cue = (SoundCueViewModel)vm.CreateCue(Models.CueType.SoundCue, afterLast: true);
                            cue.Path = file;
                            cue.Name = System.IO.Path.GetFileNameWithoutExtension(file);
                            vm.MoveCue(cue, dstIndex++);
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
}
