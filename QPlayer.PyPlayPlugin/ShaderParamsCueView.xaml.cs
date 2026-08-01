using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace QPlayer.PyPlayPlugin
{
    /// <summary>
    /// Interaction logic for ShaderParamsCueView.xaml
    /// </summary>
    public partial class ShaderParamsCueView : UserControl
    {
        public ShaderParamsCueView()
        {
            InitializeComponent();
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb)
                return;

            TargetQIDField.IsEnabled = !(cb.IsChecked ?? false);
        }
    }
}
