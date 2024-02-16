using QPlayer.ViewModels;
using ReactiveUI.Fody.Helpers;
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

namespace QPlayer.Views
{
    /// <summary>
    /// Interaction logic for CueDataControl.xaml
    /// </summary>
    public partial class CueDataControl : UserControl
    {
        public CueDataControl()
        {
            InitializeComponent();
            //this.DataContext = this;
        }

        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var vm = (CueViewModel)DataContext;
            if(vm.MainViewModel != null )
                vm.MainViewModel.SelectedCue = vm;
        }
    }
}
