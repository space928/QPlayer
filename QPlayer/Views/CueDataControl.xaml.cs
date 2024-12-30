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
    const int DragDeadzone = 10;

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
        var vm = (CueViewModel)DataContext;
        if (e.LeftButton == MouseButtonState.Pressed && delta.Length > DragDeadzone
            && (vm.MainViewModel?.DraggingCues?.Count ?? -1) == 0
            && !e.OriginalSource.GetType().IsAssignableTo(typeof(TextBox)))
        {
            DataObject data = new();
            vm.MainViewModel?.DraggingCues?.Add(vm);
            data.SetData("Cues", new CueViewModel[] { vm });

            DragDrop.DoDragDrop(this, data, DragDropEffects.Move);

            vm.MainViewModel?.DraggingCues?.Clear();
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

        InsertMarker.Visibility = Visibility.Hidden;

        var targetVm = (CueViewModel)DataContext;
        var mainVm = targetVm.MainViewModel;
        if (mainVm != null)
            MainWindow.HandleCueListDrop(e, mainVm, targetVm);

        e.Handled = true;
    }

    private void Grid_DragEnter(object sender, DragEventArgs e)
    {
        InsertMarker.Visibility = Visibility.Visible;
    }

    private void Grid_DragLeave(object sender, DragEventArgs e)
    {
        InsertMarker.Visibility = Visibility.Hidden;
    }
}
