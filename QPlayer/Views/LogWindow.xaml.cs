using System;
using System.Windows;
using System.Windows.Controls;
using QPlayer.ViewModels;

namespace QPlayer.Views
{
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
    }
}
