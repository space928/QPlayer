﻿using CommunityToolkit.Mvvm.ComponentModel;
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
using System.Timers;
using System.Windows;
using System.Windows.Data;

namespace QPlayer.ViewModels
{
    public class ViewModel : ObservableObject
    {
        #region Bindable Properties
        [Reactive, ReactiveDependency(nameof(SelectedCue))] public int SelectedCueInd { get; set; } = 0;
        [Reactive] public CueViewModel? SelectedCue {
            get => SelectedCueInd >= 0 && SelectedCueInd < Cues.Count ? Cues[SelectedCueInd] : null;
            set { 
                if (value != null) 
                { 
                    SelectedCueInd = Cues.IndexOf(value); 
                    OnPropertyChanged(nameof(SelectedCueInd)); 
                } 
            }
        }
        [Reactive] public ObservableCollection<CueViewModel> Cues { get; set; }
        [Reactive] public ObservableCollection<CueViewModel> ActiveCues { get; set; }
        [Reactive] public ObservableCollection<ObservableStruct<float>> ColumnWidths { get; set; }

        [Reactive] public RelayCommand NewProjectCommand { get; private set; }
        [Reactive] public RelayCommand OpenProjectCommand { get; private set; }
        [Reactive] public RelayCommand SaveProjectCommand { get; private set; }
        [Reactive] public RelayCommand PackProjectCommand { get; private set; }
        [Reactive] public RelayCommand OpenLogCommand { get; private set; }
        [Reactive] public RelayCommand OpenSetttingsCommand { get; private set; }
        [Reactive] public RelayCommand OpenAboutCommand { get; private set; }

        [Reactive] public RelayCommand CreateGroupCueCommand { get; private set; }
        [Reactive] public RelayCommand CreateDummyCueCommand { get; private set; }
        [Reactive] public RelayCommand CreateSoundCueCommand { get; private set; }
        [Reactive] public RelayCommand CreateTimeCodeCueCommand { get; private set; }
        [Reactive] public RelayCommand CreateStopCueCommand { get; private set; }

        [Reactive] public RelayCommand GoCommand { get; private set; }
        [Reactive] public RelayCommand PauseCommand { get; private set; }
        [Reactive] public RelayCommand StopCommand { get; private set; }

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
        [Reactive]
        public string VersionString
        {
            get
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
                return $"Copyright {versionInfo.CompanyName} 2024";
            }
        }
        [Reactive] public string Clock => $"{DateTime.Now:HH:mm:ss}";
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
        private Timer slowUpdateTimer;

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
            PackProjectCommand = new(SaveProjectExecute);
            OpenAboutCommand = new(OpenAboutExecute);
            OpenSetttingsCommand = new(OpenSettingsExecute);

            CreateGroupCueCommand = new(() => CreateCue(CueType.GroupCue));
            CreateDummyCueCommand = new(() => CreateCue(CueType.DummyCue));
            CreateSoundCueCommand = new(() => CreateCue(CueType.SoundCue));
            CreateTimeCodeCueCommand = new(() => CreateCue(CueType.TimeCodeCue));
            CreateStopCueCommand = new(() => CreateCue(CueType.GroupCue));

            GoCommand = new(GoExecute);
            PauseCommand = new(PauseExecute);
            StopCommand = new(StopExecute);

            slowUpdateTimer = new(TimeSpan.FromMilliseconds(250));
            slowUpdateTimer.AutoReset = true;
            slowUpdateTimer.Elapsed += SlowUpdate;
            slowUpdateTimer.Start();

            openShowFilePath = string.Empty;
            ColumnWidths = new(Enumerable.Repeat<float>(60, 32).Select(x =>
            {
                var observable = new ObservableStruct<float>(x);
                // Bubble the change up
                observable.PropertyChanged += (o, e) => { 
                    OnPropertyChanged(nameof(ColumnWidths)); 
                } ;
                return observable;
            }));
            ActiveCues = new();
            Cues = new();
            showFile = new();
            LoadShowfileModel(showFile);
            CreateCue(CueType.GroupCue, afterLast:true);
            CreateCue(CueType.SoundCue, afterLast: true);
            CreateCue(CueType.TimeCodeCue, afterLast: true);
        }

        public void OnExit()
        {
            Log("Shutting down...");
            SaveProject(AUTOBACK_PATH);
            CloseAboutExecute();
            CloseSettingsExecute();
            CloseLogExecute();
            Log("Goodbye!");
        }

        private void SlowUpdate(object? sender, ElapsedEventArgs e)
        {
            OnPropertyChanged(nameof(Clock));
        }

        #region Commands
        public void NewProjectExecute()
        {
            Log("Creating new project...");
            SaveProject(AUTOBACK_PATH);
            if (!string.IsNullOrEmpty(openShowFilePath))
            {
                var curr = File.ReadAllText(AUTOBACK_PATH);
                var prev = File.ReadAllText(openShowFilePath);
                if (curr != prev)
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

            LoadShowfileModel(new());
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

        public void CloseLogExecute()
        {
            logWindow?.Close();
            logWindow = null;
        }

        public void OpenSettingsExecute()
        {
            settingsWindow = new(this);
            settingsWindow.Closed += (e, x) => { settingsWindow = null; };
            settingsWindow.Show();
        }

        public void CloseSettingsExecute()
        {
            settingsWindow?.Close();
            settingsWindow = null;
        }

        public void OpenAboutExecute()
        {
            aboutWindow = new(this);
            aboutWindow.Closed += (e, x) => { aboutWindow = null; };
            aboutWindow.Show();
        }

        public void CloseAboutExecute()
        {
            aboutWindow?.Close();
            aboutWindow = null;
        }

        public void GoExecute()
        {
            if(SelectedCue != null)
            {
                SelectedCue.Go();
            }
            SelectedCueInd += 1;
        }

        public void PauseExecute()
        {
            if (SelectedCue != null)
            {
                SelectedCue.Pause();
            }
        }

        public void StopExecute()
        {
            for(int i = ActiveCues.Count-1; i >= 0; i--)
                ActiveCues[i].Stop();
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

        private void LoadShowfileModel(ShowFile show)
        {
            showFile = show;
            Cues.Clear();
            foreach(Cue c in showFile.cues)
            {
                var vm = CueViewModel.FromModel(c, this);
                vm.Bind(c);
                Cues.Add(vm);
            }
        }

        /// <summary>
        /// This effectively makes sure that the data in the view model is copied to the model, just in case a change was missed.
        /// </summary>
        private void EnsureShowfileModelSync()
        {
            Dictionary<decimal, Cue> cueModels = new(showFile.cues.Select((x) => new KeyValuePair<decimal, Cue>(x.qid, x)));
            foreach (CueViewModel vm in Cues)
            {
                if (!cueModels.ContainsKey(vm.QID))
                {
                    Log($"Cue with id {vm.QID} exists in the editor but not in the internal model! Potential corruption detected!", LogLevel.Warning);
                    // TODO: If we want to be nice we could just create the model here...
                }
                else
                {
                    var q = cueModels[vm.QID];
                    vm.ToModel(q);
                }
            }
        }

        public void OpenProject(string path)
        {
            try
            {
                var s = JsonSerializer.Deserialize<ShowFile>(File.ReadAllText(path), jsonSerializerOptions);
                if (s == null)
                    throw new FileFormatException("Show file deserialized as null!");
                if (s.fileFormatVersion != ShowFile.FILE_FORMAT_VERSION)
                    Log($"Project file version '{s.fileFormatVersion}' does not match QPlayer version '{ShowFile.FILE_FORMAT_VERSION}'!", LogLevel.Warning);
                LoadShowfileModel(s);

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
                EnsureShowfileModelSync();
                File.WriteAllText(path, JsonSerializer.Serialize(showFile, jsonSerializerOptions));
                Log($"Saved profile to {path}!");
            }
            catch (Exception e)
            {
                Log($"Couldn't save profile to disk. Trying to save {path} \n  failed with: {e}", LogLevel.Warning);
            }
        }

        public CueViewModel CreateCue(CueType type, bool beforeCurrent = false, bool afterLast = false)
        {
            int insertAfterInd = SelectedCueInd;
            if (beforeCurrent)
                insertAfterInd--;
            if (afterLast)
                insertAfterInd = Cues.Count - 1;
            Cue model;
            switch (type)
            {
                case CueType.GroupCue: model = new GroupCue(); break;
                case CueType.DummyCue: model = new DummyCue(); ; break;
                case CueType.SoundCue: model = new SoundCue(); ; break;
                case CueType.TimeCodeCue: model = new TimeCodeCue(); ; break;
                case CueType.StopCue: model = new StopCue(); ; break;
                default: throw new NotImplementedException();
            }
            decimal newId;
            decimal prevId = 0;
            if (insertAfterInd >= 0 && insertAfterInd < Cues.Count) 
                prevId = Cues[insertAfterInd].QID;
            if (insertAfterInd + 1 >= 0 && insertAfterInd + 1 < Cues.Count) {
                decimal nextId = Cues[insertAfterInd + 1].QID;
                if (nextId - prevId <= 1)
                    newId = (prevId + nextId) / 2;
                else
                    newId = prevId + 1;
            } else
            {
                newId = prevId + 1;
            }
            model.qid = newId;
            var ret = CueViewModel.FromModel(model, this);
            ret.Bind(model);
            showFile.cues.Insert(insertAfterInd + 1, model);
            Cues.Insert(insertAfterInd + 1, ret);

            return ret;
        }
    }

    public class ObservableStruct<T> : ObservableObject where T: struct
    {
        [Reactive] public T Value { get; set; }

        public ObservableStruct() { }

        public ObservableStruct(T value)
        {
            Value = value;
        }   
    }
}
