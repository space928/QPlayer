using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;
using QPlayer.ViewModels;

namespace QPlayer.Views
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow : Window
    {
        public AboutWindow(MainViewModel viewModel)
        {
            this.DataContext = viewModel;
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute=true });
            e.Handled = true;
        }

        private void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/space928/QPlayer/") { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
