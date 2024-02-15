using System;
using System.Windows;
using System.Windows.Controls;

namespace QPlayer
{
    /// <summary>
    /// Interaction logic for LogWindow.xaml
    /// </summary>
    public partial class LogWindow : Window
    {
        public ViewModel ViewModel { get; init; }

        public LogWindow(ViewModel viewModel)
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
