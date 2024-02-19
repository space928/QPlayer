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
            if (vm.MainViewModel != null)
                vm.SelectCommand.Execute(null);
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            var vm = (CueViewModel)DataContext;
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
                                BringIntoView(new Rect(new Size(10, 200))); // Leave some padding bellow us
                        }
                        break;
                }
            };
        }

        private void Grid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
        }
    }
}
