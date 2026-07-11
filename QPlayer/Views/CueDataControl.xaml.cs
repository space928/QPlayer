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
using System.Windows.Markup;
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
    [Reactive("ExpanderVisibility")]
    private Visibility ExpanderVisibility_Template => (DataContext is GroupCueViewModel) ? Visibility.Visible : Visibility.Collapsed;
    [Reactive("IsCollapsed")]
    private bool IsCollapsed_Template
    {
        get
        {
            if (DataContext is not GroupCueViewModel gc)
                return false;
            return gc.IsCollapsed;
        }
        set
        {
            if (DataContext is GroupCueViewModel gc)
                gc.IsCollapsed = value;
        }
    }

    const int DragDeadzone = 10;

    private Point startPos;
    private CueViewModel? vm;
    private GroupCueViewModel? group;
    private static CornerRadius defaultCornerRadius;
    private static DrawingImage? defaultCueIcon;

    internal static readonly StringDict<DrawingImage> cueIcons = [];
    internal static DrawingImage? DefaultCueIcon
    {
        get
        {
            if (defaultCueIcon != null)
                return defaultCueIcon;
            if (App.Current.Resources.Contains("IconPlay"))
                return defaultCueIcon = (DrawingImage)App.Current.Resources["IconPlay"];
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

    internal void NotifyGroupMarkerChange(int visualIndex)
    {
        if (vm == null)
            return;

        var isLast = vm.MainViewModel.Cues.IsLastInGroup(visualIndex);

        int parents = 0;
        var parent = vm;
        while ((parent = parent.Parent) != null)
            parents++;
        int groupDepth = parents;
        if (group != null)
            groupDepth++;

        if (groupDepth > 0)
        {
            GroupMarker.Visibility = Visibility.Visible;
            GroupMarker.Opacity = groupDepth * 0.25;
            GroupMarker.BorderThickness = new(groupDepth * 4, 0, 0, isLast ? 1 : 0);
            if (isLast && groupDepth == 1)
                GroupMarker.CornerRadius = new(0, 0, 0, defaultCornerRadius.BottomLeft);
            else
                GroupMarker.CornerRadius = default;
            NameTextBox.Margin = new(parents * 12, 0, 0, 0);
            ColourSwatch.Margin = new(groupDepth * 4, 0, 0, 0);
        }
        else
        {
            GroupMarker.Visibility = Visibility.Collapsed;
            NameTextBox.Margin = default;
            ColourSwatch.Margin = default;
        }
    }

    private void SetGroupMarkerBGBinding()
    {
        GroupMarker.SetBinding(Border.BackgroundProperty, group != null ? nameof(CueViewModel.ColourBrush) : nameof(CueViewModel.Parent) + "." + nameof(CueViewModel.ColourBrush));
    }

    private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        startPos = e.GetPosition(this);

        if (vm == null)
            return;
        if (vm.MainViewModel != null)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => vm.SelectCommand.Execute(null));
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (defaultCueIcon == default && Resources["CornerRadiusDummy"] is Border border)
            defaultCornerRadius = border.CornerRadius;

        Init();
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        vm?.PropertyChanged -= OnCuePropertyChanged;
    }

    private void UserControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Init();
    }

    private void Init()
    {
        OnPropertyChanged(nameof(CueIcon));
        OnPropertyChanged(nameof(ExpanderVisibility));
        OnPropertyChanged(nameof(IsCollapsed));

        this.vm?.PropertyChanged -= OnCuePropertyChanged;

        if (DataContext is not CueViewModel vm)
            return;

        vm.PropertyChanged += OnCuePropertyChanged;

        this.vm = vm;
        this.group = vm as GroupCueViewModel;

        SetGroupMarkerBGBinding();
        if ((vm.Parent != null || group != null)
            && vm.MainViewModel.Cues.FindVisualIndex(vm, out var ind))
            NotifyGroupMarkerChange(ind);
    }

    private void OnCuePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (vm == null)
            return;

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
                ComputeSelOutline();
                break;
            case nameof(CueViewModel.IsMultiSelected):
                ComputeSelOutline();
                break;
        }
    }

    private void ComputeSelOutline()
    {
        if (vm == null)
            return;

        // Being multi-selected implies being selected. If not selected just hide the outline and return.
        if (!vm.IsMultiSelected)
        {
            SelOutline.Visibility = Visibility.Collapsed;
            return;
        }
        SelOutline.Visibility = Visibility.Visible;

        if (vm.IsSelected)
        {
            // Debug.WriteLine($"Selected: {vm.FullQID}");
            // Primary selection just gets a simple full outline
            SelOutline.BorderThickness = new(1);
            SelOutline.CornerRadius = defaultCornerRadius;
        }
        else
        {
            // Multi-selected cues try to join up their outlines into a single contiguous one.
            var cues = vm.MainViewModel.Cues;
            var multi = vm.MainViewModel.MultiSelection;
            if (!cues.FindVisualIndex(vm, out var ind)) // Kinda expensive...
                return;

            bool top = ind == 0 || !multi.Contains(cues[ind - 1]);
            bool bot = (ind + 1) >= cues.Count || !multi.Contains(cues[ind + 1]);

            SelOutline.BorderThickness = new(1, top ? 1 : 0, 1, bot ? 1 : 0);
            SelOutline.CornerRadius = new(
                top ? defaultCornerRadius.TopLeft : 0,
                top ? defaultCornerRadius.TopRight : 0,
                bot ? defaultCornerRadius.BottomLeft : 0,
                bot ? defaultCornerRadius.BottomRight : 0);
        }
    }

    private void Grid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
    }

    private void Grid_MouseMove(object sender, MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var delta = e.GetPosition(this) - startPos;

        if (vm == null)
            return;
        var mainVm = vm.MainViewModel;

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

            // Debug.WriteLine($"Cue DragStart!");
            DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Link | DragDropEffects.Move | DragDropEffects.Scroll);

            mainVm.DraggingCues.Clear();
        }
    }

    private void Grid_DragLeave(object sender, DragEventArgs e)
    {
        InsertMarker.Visibility = Visibility.Collapsed;
        GroupInsertMarker.Visibility = Visibility.Collapsed;
    }

    private void Grid_Drop(object sender, DragEventArgs e)
    {
        ComputeDragEffects(sender, e);
        Grid_DragLeave(sender, e);
        if (vm != null)
            MainWindow.HandleCueListDrop(e, vm.MainViewModel, vm);
        e.Handled = true;
    }

    private void ComputeDragEffects(object sender, DragEventArgs e)
    {
        if (vm == null)
            return;

        if (!IsDropAllowed())
        {
            InsertMarker.Visibility = Visibility.Collapsed;
            GroupInsertMarker.Visibility = Visibility.Collapsed;
            e.Effects = DragDropEffects.None;
        }
        else if (IsGroupDragEffect(e))
        {
            InsertMarker.Visibility = Visibility.Collapsed;
            GroupInsertMarker.Visibility = Visibility.Visible;
            e.Effects = DragDropEffects.Link;
        }
        else
        {
            InsertMarker.Visibility = Visibility.Visible;
            GroupInsertMarker.Visibility = Visibility.Collapsed;
            if (e.KeyStates.HasFlag(DragDropKeyStates.ControlKey))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.Move;
        }
    }

    private void Grid_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        // This event is handled by the MainWindow
        base.OnGiveFeedback(e);
    }

    private bool IsDropAllowed()
    {
        if (vm == null)
            return false;
        var dragging = vm.MainViewModel.DraggingCues;
        foreach (var cand in dragging)
            if (vm.HasParent(cand))
                return false;
        if (dragging.Count == 1 && dragging[0] == vm)
            return false;
        return true;
    }

    private bool IsGroupDragEffect(DragEventArgs e)
    {
        var pos = e.GetPosition(this);
        var height = ActualHeight;
        return pos.Y > height * 0.5;
    }
}
