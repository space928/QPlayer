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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace QPlayer.Views;

/// <summary>
/// Interaction logic for ProjectSettingsEditor.xaml
/// </summary>
public partial class ProjectSettingsEditor : UserControl
{
    public ProjectSettingsEditor()
    {
        InitializeComponent();
    }

    private void ShowAudioDriverControls()
    {
        var vm = (ProjectSettingsViewModel?)DataContext;
        if (vm == null)
            return;
        switch (vm.AudioOutputDriver)
        {
            case Audio.AudioOutputDriver.WASAPI:
                AudioLatencyControl.Visibility = Visibility.Visible;
                ExclusiveModeControl.Visibility = Visibility.Visible;
                ChannelOffsetControl.Visibility = Visibility.Collapsed;
                ShowControlPanelControl.Visibility = Visibility.Collapsed;
                break;
            case Audio.AudioOutputDriver.Wave:
            case Audio.AudioOutputDriver.DirectSound:
                AudioLatencyControl.Visibility = Visibility.Visible;
                ExclusiveModeControl.Visibility = Visibility.Collapsed;
                ChannelOffsetControl.Visibility = Visibility.Collapsed;
                ShowControlPanelControl.Visibility = Visibility.Collapsed;
                break;
            case Audio.AudioOutputDriver.ASIO:
                AudioLatencyControl.Visibility = Visibility.Collapsed;
                ExclusiveModeControl.Visibility = Visibility.Collapsed;
                ChannelOffsetControl.Visibility = Visibility.Visible;
                ShowControlPanelControl.Visibility = Visibility.Visible;
                break;
        }
    }

    private void AudioDriver_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowAudioDriverControls();
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        ShowAudioDriverControls();
    }
}
