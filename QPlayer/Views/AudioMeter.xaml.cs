using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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

namespace QPlayer.Views;

/// <summary>
/// Interaction logic for AudioMeter.xaml
/// </summary>
public partial class AudioMeter : UserControl
{
    public AudioMeter()
    {
        InitializeComponent();
    }

    private void UserControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        
    }
}
