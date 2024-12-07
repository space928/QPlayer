using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
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

namespace QPlayer.Views;

/// <summary>
/// Interaction logic for CueDataControl.xaml
/// </summary>
public partial class CueDataControl : UserControl
{
    private Point startPos;

    public CueDataControl()
    {
        InitializeComponent();
        //this.DataContext = this;
    }

    private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        startPos = e.GetPosition(this);

        var vm = (CueViewModel)DataContext;
        if (vm.MainViewModel != null)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => vm.SelectCommand.Execute(null));
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        var vm = (CueViewModel)DataContext;
        vm.PropertyChanged += (o, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(CueViewModel.IsSelected):
                    if (vm.IsSelected)
                    {
                        // This is a lazy way to check if the last action that selected us was a click or some other kind of Go()
                        // If the user clicks on the element we shouldn't risk it moving too much
                        if (IsMouseOver)
                            BringIntoView();
                        else
                            BringIntoView(new Rect(new Size(10, 200))); // Leave some padding below us
                    }
                    break;
            }
        };
    }

    private void Grid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
    }

    private void Grid_MouseMove(object sender, MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var delta = e.GetPosition(this) - startPos;
        if (e.LeftButton == MouseButtonState.Pressed && delta.Length > 10
            && !e.OriginalSource.GetType().IsAssignableTo(typeof(TextBox)))
        {
            DataObject data = new();
            data.SetData("Cue", (CueViewModel)DataContext);

            DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
        }
    }

    private void Grid_GiveFeedback(object sender, GiveFeedbackEventArgs e)
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

    private void Grid_Drop(object sender, DragEventArgs e)
    {
        base.OnDrop(e);

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
        e.Handled = true;
    }
}
