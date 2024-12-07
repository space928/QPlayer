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

        if (e.Effects.HasFlag(DragDropEffects.Copy))
            Mouse.SetCursor(Cursors.Cross);
        else if (e.Effects.HasFlag(DragDropEffects.Move))
            Mouse.SetCursor(Cursors.Hand);
        else
            Mouse.SetCursor(Cursors.No);

        e.Handled = true;
    }

    private void CueList_Drop(object sender, DragEventArgs e)
    {
        base.OnDrop(e);

        var vm = (MainViewModel)DataContext;

        // If the DataObject contains string data, extract it.
        if (e.Data.GetDataPresent("Cue"))
        {
            CueViewModel dataCue = (CueViewModel)e.Data.GetData("Cue");

            if (e.KeyStates.HasFlag(DragDropKeyStates.ControlKey))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.Move;
            }

            // TODO: Move/insert the new cue
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
                            break;
                        default:
                            break;
                    }
                }
            }
        }
        e.Handled = true;
    }
}
