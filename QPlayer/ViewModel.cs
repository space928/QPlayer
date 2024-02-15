using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData.Kernel;
using Microsoft.Win32;
using QPlayer.Models;
using QPlayer.Views;
using ReactiveUI.Fody.Helpers;
using Splat;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace QPlayer
{
    public class ViewModel : ObservableObject
    {
        #region Bindable Properties
        [Reactive] public RelayCommand NewProjectCommand { get; private set; }
        [Reactive] public RelayCommand OpenProjectCommand { get; private set; }
        [Reactive] public RelayCommand SaveProjectCommand { get; private set; }
        [Reactive] public RelayCommand PackProjectCommand { get; private set; }
        [Reactive] public RelayCommand OpenLogCommand { get; private set; }
        [Reactive] public RelayCommand OpenSetttingsCommand { get; private set; }
        [Reactive] public RelayCommand OpenAboutCommand { get; private set; }
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
        [Reactive] public string VersionString { get
            {
                var assembly = Assembly.GetAssembly(typeof(ViewModel));
                if (assembly == null)
                    return string.Empty;
                var versionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
                return $"Version {versionInfo.ProductVersion}";
            } 
        }
        [Reactive]
        public string CopyrightString
        {
            get
            {
                var assembly = Assembly.GetAssembly(typeof(ViewModel));
                if (assembly == null)
                    return string.Empty;
                var versionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
                return $"Copyright {versionInfo.LegalCopyright}";
            }
        }
        #endregion

        public static readonly string AUTOBACK_PATH = "autoback.qproj";

        private ShowFile showFile;
        private string openShowFilePath;
        private static ObservableCollection<string> logList;
        private static LogWindow? logWindow;
        private static SettingsWindow? settingsWindow;
        private static AboutWindow? aboutWindow;
        private static bool started = false;
        private static readonly object logListLock = new();
        private JsonSerializerOptions jsonSerializerOptions = new()
        {
            IncludeFields = true,
            AllowTrailingCommas = true,
            WriteIndented = true,
        };

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
            NewProjectCommand = new(NewProjectExecute);
            OpenProjectCommand = new(OpenProjectExecute);
            SaveProjectCommand = new(SaveProjectExecute);
            OpenAboutCommand = new(OpenAboutExecute);
            OpenSetttingsCommand = new(OpenSettingsExecute);

            openShowFilePath = string.Empty;
            showFile = new();
        }

        public void OnExit()
        {
            Log("Shutting down...");
            SaveProject(AUTOBACK_PATH);
            Log("Goodbye!");
        }

        #region Commands
        public void NewProjectExecute()
        {
            Log("Creating new project...");
            SaveProject(AUTOBACK_PATH);
            if(!string.IsNullOrEmpty(openShowFilePath))
            {
                var curr = File.ReadAllText(AUTOBACK_PATH);
                var prev = File.ReadAllText(openShowFilePath);
                if(curr != prev)
                {
                    // There are unsaved changes!
                    var mbRes = MessageBox.Show("Current project has unsaved changes! Do you wish to save them now?", 
                        "Unsaved Changes", 
                        MessageBoxButton.YesNoCancel);
                    switch (mbRes)
                    {
                        case MessageBoxResult.Yes:
                            SaveProject(openShowFilePath);
                            break;
                        case MessageBoxResult.No:
                            break;
                        case MessageBoxResult.Cancel:
                            Log("   aborted!");
                            return;
                    }
                }
            }

            showFile = new();
        }

        public void SaveProjectExecute()
        {
            SaveFileDialog saveFileDialog = new()
            {
                AddExtension = true,
                DereferenceLinks = true,
                Filter = "QPlayer Projects (*.qproj)|*.qproj|All files (*.*)|*.*",
                OverwritePrompt = true,
                Title = "Save QPlayer Project"
            };
            if (saveFileDialog.ShowDialog() ?? false)
            {
                SaveProject(saveFileDialog.FileName);
            }
        }

        public void OpenProjectExecute()
        {
            OpenFileDialog openFileDialog = new()
            {
                Multiselect = false,
                Title = "Open QPlayer Project",
                CheckFileExists = true,
                Filter = "QPlayer Projects (*.qproj)|*.qproj|All files (*.*)|*.*"
            };
            if (openFileDialog.ShowDialog() ?? false)
            {
                OpenProject(openFileDialog.FileName);
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

        public void OpenSettingsExecute()
        {
            settingsWindow = new(this);
            settingsWindow.Closed += (e, x) => { settingsWindow = null; };
            settingsWindow.Show();
        }

        public void OpenAboutExecute()
        {
            aboutWindow = new(this);
            aboutWindow.Closed += (e, x) => { aboutWindow = null; };
            aboutWindow.Show();
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

        public void OpenProject(string path)
        {
            try
            {
                var s = JsonSerializer.Deserialize<ShowFile>(File.ReadAllText(path), jsonSerializerOptions);
                if (s == null)
                    throw new FileFormatException("Show file deserialized as null!");
                showFile = s;

                Log($"Loaded profile from disk! {path}");
            }
            catch (Exception e)
            {
                Log($"Couldn't load profile from disk. Trying to load {path} \n  failed with: {e}", LogLevel.Warning);
                // Save the default profile instead
                //ButtonEditors.ToMagicQProfile(ref magicQCTRLProfile);
            }
        }

        public void SaveProject(string path)
        {
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(showFile, jsonSerializerOptions));
                Log($"Saved profile to {path}!");
            }
            catch (Exception e)
            {
                Log($"Couldn't save profile to disk. Trying to save {path} \n  failed with: {e}", LogLevel.Warning);
            }
        }
    }
}
