using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using QPlayer.ViewModels;

namespace QPlayer.Views;

/// <summary>
/// Interaction logic for LogWindow.xaml
/// </summary>
public partial class LogWindow : Window
{
    public MainViewModel ViewModel { get; init; }
    private bool autoScrollToBottom;
    private readonly SolidColorBrush errorBrush = new(Color.FromArgb(255, 220, 60, 40));
    private readonly SolidColorBrush warningBrush = new(Color.FromArgb(255, 200, 220, 50));

    public LogWindow(MainViewModel viewModel)
    {
        this.ViewModel = viewModel;
        this.DataContext = viewModel;
        InitializeComponent();
        LogUndoCheckbox.IsChecked = UndoManager.LogUndoActions;
    }

    //https://stackoverflow.com/a/46548292
    private void ScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (autoScrollToBottom && e.OriginalSource is ScrollViewer scrollViewer &&
            Math.Abs(e.ExtentHeightChange) > 0.0)
        {
            scrollViewer.ScrollToBottom();
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        MainViewModel.LogList.Clear();
    }

    private void SaveToDiskButton_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog saveFileDialog = new()
        {
            AddExtension = true,
            DereferenceLinks = true,
            Filter = "Text Files (*.txt)|*.txt|All files (*.*)|*.*",
            OverwritePrompt = true,
            Title = "Save Log File"
        };
        if (saveFileDialog.ShowDialog() ?? false)
        {
            try
            {
                File.WriteAllLinesAsync(saveFileDialog.FileName, MainViewModel.LogList).ContinueWith(_ =>
                {
                    MainViewModel.Log($"Log file exported to: {saveFileDialog.FileName}");
                });
            }
            catch { }
        }
    }

    private void CheckBox_Checked(object sender, RoutedEventArgs e)
    {
        autoScrollToBottom = ScrollToBottomCheckbox.IsChecked ?? true;
        if (autoScrollToBottom && LogListBox != null && LogListBox.Items.Count > 0)
            LogListBox.ScrollIntoView(LogListBox.Items[^1]);
    }

    private void AudioBufferDbgCheckbox_Checked(object sender, RoutedEventArgs e)
    {
        bool active = (AudioBufferDbgCheckbox.IsChecked ?? false);
        ViewModel.AudioBufferDispatcherDebug.ShouldUpdate = active;
        AudioBufferDbgControl.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        LogListBox.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
    }

    private void LogUndoCheckbox_Checked(object sender, RoutedEventArgs e)
    {
        bool active = (LogUndoCheckbox.IsChecked ?? false);
        UndoManager.LogUndoActions = active;
    }

    private void LogItemText_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock text)
            return;

        if (text.Text.Contains("[Error]"))
            text.Foreground = errorBrush;
        else if (text.Text.Contains("[Warning]"))
            text.Foreground = warningBrush;
    }

    private void SaveUndoButton_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog saveFileDialog = new()
        {
            AddExtension = true,
            DereferenceLinks = true,
            Filter = "Text Files (*.txt)|*.txt|All files (*.*)|*.*",
            OverwritePrompt = true,
            Title = "Save Undo Log File"
        };
        if (saveFileDialog.ShowDialog() ?? false)
        {
            try
            {
                StringBuilder sb = new();
                sb.AppendLine("# Undo Stack");
                sb.AppendLine("* Most recent first *");
                foreach (var item in UndoManager.GetUndoLog())
                    sb.AppendLine($" - {item}");
                sb.AppendLine();
                sb.AppendLine("# Redo Stack");
                sb.AppendLine("* Most recent first *");
                foreach (var item in UndoManager.GetRedoLog())
                    sb.AppendLine($" - {item}");
                sb.AppendLine();

                File.WriteAllTextAsync(saveFileDialog.FileName, sb.ToString()).ContinueWith(_ =>
                {
                    MainViewModel.Log($"Undo log file exported to: {saveFileDialog.FileName}");
                });
            }
            catch { }
        }
    }
}
