using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData.Kernel;
using Microsoft.Win32;
using ReactiveUI.Fody.Helpers;
using Splat;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Data;

namespace QPlayer
{
    public class ViewModel : ObservableObject
    {
        #region Bindable Properties
        [Reactive] public RelayCommand OpenProfileCommand { get; private set; }
        [Reactive] public RelayCommand SaveProfileCommand { get; private set; }
        [Reactive] public RelayCommand OpenLogCommand { get; private set; }
        [Reactive]
        public static ObservableCollection<string> LogList
        {
            get { return logList; }
            private set
            {
                logList = value;
                BindingOperations.EnableCollectionSynchronization(logList, logListLock);
            }
        }
        #endregion

        public static readonly string AUTOBACK_PATH = "autoback.qproj";

        private static ObservableCollection<string> logList;
        private static LogWindow? logWindow;
        private static bool started = false;
        private static readonly object logListLock = new();

        public ViewModel() 
        {
            // Only run initialisation code once.
            // Otherwise the log window resets everything when it's opened
            //if (started)
            //    return;
            //started = true;

            LogList = new();
            Log("Starting QPlayer...");
            Log("  Copyright Thomas Mathieson 2024");

            // Bind commands
            OpenLogCommand = new(OpenLogExecute, () => logWindow == null);
            OpenProfileCommand = new(OpenProfileExecute);
            SaveProfileCommand = new(SaveProfileExecute);
        }

        public void OnExit()
        {
            Log("Shutting down...");
            SaveProfile(AUTOBACK_PATH);
            Log("Goodbye!");
        }

        #region Commands
        public void SaveProfileExecute()
        {
            SaveFileDialog saveFileDialog = new()
            {
                AddExtension = true,
                DereferenceLinks = true,
                Filter = "MagicQCTRL Profiles (*.json)|*.json|All files (*.*)|*.*",
                OverwritePrompt = true,
                Title = "Save MagicQCTRL Profile"
            };
            if (saveFileDialog.ShowDialog() ?? false)
            {
                SaveProfile(saveFileDialog.FileName);
            }
        }

        public void OpenProfileExecute()
        {
            OpenFileDialog openFileDialog = new()
            {
                Multiselect = false,
                Title = "Open MagicQCTRL Profile",
                CheckFileExists = true,
                Filter = "MagicQCTRL Profiles (*.json)|*.json|All files (*.*)|*.*"
            };
            if (openFileDialog.ShowDialog() ?? false)
            {
                OpenProfile(openFileDialog.FileName);
            }
        }

        public void OpenLogExecute()
        {
            logWindow = new(this);
            //logWindow.Owner = ((Window)e.Source);
            Log("Opening log...");
            logWindow.Closed += (e, x) => { logWindow = null; };
            logWindow.Show();
        }

        public void CloseLogExecute()
        {
            logWindow?.Close();
            logWindow = null;
        }

        #endregion

        public static void Log(object message, LogLevel level = LogLevel.Info, [CallerMemberName] string caller = "")
        {
            lock (logListLock)
            {
                string msg = $"[{level}] [{DateTime.Now}] [{caller}] {message}";
                LogList.Add(msg);
                Console.WriteLine(msg);
                Debug.WriteLine(msg);
            }
        }

        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error
        }

        public void OpenProfile(string path)
        {
            try
            {
                // magicQCTRLProfile = JsonSerializer.Deserialize<MagicQCTRLProfile>(File.ReadAllText(path), jsonSerializerOptions);
                // ButtonEditors.FromMagicQProfile(magicQCTRLProfile);
                // BaseBrightness = magicQCTRLProfile.baseBrightness;
                // PressedBrightness = magicQCTRLProfile.pressedBrightness;

                Log($"Loaded profile from disk! {path}");
            }
            catch (Exception e)
            {
                Log($"Couldn't load profile from disk. Trying to load {path} \n  failed with: {e}", LogLevel.Warning);
                // Save the default profile instead
                //ButtonEditors.ToMagicQProfile(ref magicQCTRLProfile);
            }
        }

        public void SaveProfile(string path)
        {
            try
            {
                //File.WriteAllText(path, JsonSerializer.Serialize(magicQCTRLProfile, jsonSerializerOptions));
                Log($"Saved profile to {path}!");
            }
            catch (Exception e)
            {
                Log($"Couldn't save profile to disk. Trying to save {path} \n  failed with: {e}", LogLevel.Warning);
            }
        }
    }
}
