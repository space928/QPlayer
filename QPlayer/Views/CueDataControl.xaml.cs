using QPlayer.SourceGenerator;
using QPlayer.Utilities;
using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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

namespace QPlayer.Views;

/// <summary>
/// Interaction logic for CueDataControl.xaml
/// </summary>
public partial class CueDataControl : UserControl, INotifyPropertyChanged, INotifyPropertyChanging
{
    [Reactive("CueIcon")]
    private DrawingImage? CueIcon_Template
    {
        get
        {
            if (DataContext is not CueViewModel vm)
                return DefaultCueIcon;

            if (cueIcons.TryGetValue(vm.TypeName, out var icon))
                return icon;

            return DefaultCueIcon;
        }
    }

    const int DragDeadzone = 10;

    private Point startPos;

    internal static readonly StringDict<DrawingImage> cueIcons = [];
    internal static DrawingImage? DefaultCueIcon
    {
        get
        {
            if (App.Current.Resources.Contains("IconPlay"))
                return (DrawingImage)App.Current.Resources["IconPlay"];
            return null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event PropertyChangingEventHandler? PropertyChanging;

    public CueDataControl()
    {
        InitializeComponent();
        //this.DataContext = this;
    }

    private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        startPos = e.GetPosition(this);

        if (DataContext is not CueViewModel vm)
            return;
        if (vm.MainViewModel != null)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => vm.SelectCommand.Execute(null));
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CueViewModel vm)
            return;
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
        if (DataContext is not CueViewModel vm)
            return;
        var mainVm = vm.MainViewModel;
        if (mainVm == null)
            return;
        if (e.LeftButton == MouseButtonState.Pressed && delta.Length > DragDeadzone
            && mainVm.DraggingCues.Count == 0
            && !e.OriginalSource.GetType().IsAssignableTo(typeof(TextBox)))
        {
            DataObject data = new();
            if (mainVm.MultiSelection.Count > 1)
            {
                // Add the entire multi-selection in the correct order
                foreach (var cue in mainVm.Cues)
                    if (mainVm.MultiSelection.Contains(cue))
                        mainVm.DraggingCues.Add(cue);
            } 
            else
            {
                // Just add this cue
                mainVm.DraggingCues.Add(vm);
            }
            data.SetData("Cues", mainVm.DraggingCues.ToArray());

            DragDrop.DoDragDrop(this, data, DragDropEffects.Move | DragDropEffects.Scroll);

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

        InsertMarker.Visibility = Visibility.Collapsed;

        if (DataContext is not CueViewModel targetVm)
            return;
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
        InsertMarker.Visibility = Visibility.Collapsed;
    }
}
