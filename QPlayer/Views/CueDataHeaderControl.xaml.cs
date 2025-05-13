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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace QPlayer.Views;

/// <summary>
/// Interaction logic for CueDataHeaderControl.xaml
/// </summary>
public partial class CueDataHeaderControl : UserControl
{
    public CueDataHeaderControl()
    {
        InitializeComponent();
    }

    private void GridSplitter_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var splitter = (GridSplitter)sender;
        int col = Grid.GetColumn(splitter);


    }
}
