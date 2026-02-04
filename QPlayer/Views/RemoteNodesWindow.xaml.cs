using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
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

namespace QPlayer.Views;

/// <summary>
/// Interaction logic for RemoteNodesWindow.xaml
/// </summary>
public partial class RemoteNodesWindow : Window
{
    public RemoteNodesWindow(MainViewModel viewModel)
    {
        this.DataContext = new RemoteNodesWindowViewModel(viewModel);
        InitializeComponent();
    }
}
