using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using QPlayer.ViewModels;

namespace QPlayer.Views;

/// <summary>
/// Interaction logic for LogWindow.xaml
/// </summary>
public partial class LogWindow : Window
{
    public MainViewModel ViewModel { get; init; }

    public LogWindow(MainViewModel viewModel)
    {
        this.ViewModel = viewModel;
        this.DataContext = viewModel;
        InitializeComponent();
    }

    //https://stackoverflow.com/a/46548292
    private void ScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is ScrollViewer scrollViewer &&
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
}
