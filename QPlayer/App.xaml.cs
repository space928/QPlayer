using QPlayer.Audio;
using QPlayer.ViewModels;
using QPlayer.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace QPlayer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        QPlayerSplashScreen splashScreen = new("QPlayer.Resources.SplashV2.png");
        splashScreen.Show(true);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Try and get the main view model
        var vm = (MainWindow as MainWindow)?.DataContext as MainViewModel;
        bool activeCues = false;
        ManualResetEventSlim re = new(false);
        if (vm != null)
        {
            // Check if any cues are still playing
            vm.ActiveCues.CollectionChanged += (o, e) =>
            {
                if (vm.ActiveCues.Count == 0)
                {
                    activeCues = false;
                    re.Set();
                }
            };
            activeCues = vm.ActiveCues.Count > 0;

            try
            {
                ((MixerSampleProvider)vm.AudioPlaybackManager.MixerSampleProvider).MixerInputEnded += (o, e) =>
                {
                    if (((MixerSampleProvider)o!).SourceCount == 0)
                    {
                        activeCues = false;
                        re.Set();
                    }
                };
            }
            catch (Exception) { }

            // Try to save the log file
            try
            {
                MainViewModel.Log($"[CRASH] {e.Exception}", MainViewModel.LogLevel.Error);
                MainViewModel.Log($"Please submit a bug report at https://github.com/space928/QPlayer/issues/new make sure to " +
                    $"attach this file, the qplayer project file in question (crashed.qproj) and the steps that led up to " +
                    $"this crash.", MainViewModel.LogLevel.Error);
                File.WriteAllLines(Path.Combine(vm.PersistantDataManager.AutoBackDir, "crashlog.txt"), MainViewModel.LogList);
            }
            catch (Exception) { }
        }

        // Appologise to the user
        bool wait = false;
        if (activeCues)
        {
            wait = MessageBox.Show("QPlayer has encountered an unrecoverable error and must close. \n" +
            "We have detected that cues might still be playing, do you want to wait for these " +
            "cues to finish playing before exiting?", "QPlayer has crashed", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        }
        var save = MessageBox.Show("QPlayer has encountered an unrecoverable error and must close. \n" +
            "You can attempt to save your project file (this will be saved in the autobackup " +
            "directory under the name 'crashed.qproj'). \nDo you want to save your project file.",
            "QPlayer has crashed", MessageBoxButton.YesNo);
        if (save == MessageBoxResult.Yes && vm != null)
        {
            try
            {
                string path = Path.Combine(vm.PersistantDataManager.AutoBackDir, "crashed.qproj");
                vm.SaveProject(path);
                MessageBox.Show($"Project file saved successfully to: {path}\n\nPlease submit a bug report on the QPlayer GitHub page.", 
                    "QPlayer has crashed", MessageBoxButton.OK);
            }
            catch (Exception)
            {
                MessageBox.Show($"Project file could not be saved.", "QPlayer has crashed", MessageBoxButton.OK);
            }
        }
        if (wait)
        {
            re.Wait();
        }
    }
}
