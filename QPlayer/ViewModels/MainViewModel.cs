using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetBrains.Profiler.Api;
using Microsoft.Win32;
using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.Views;
using QPlayer.Utilities;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Timer = System.Timers.Timer;

namespace QPlayer.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        #region Bindable Properties
        [Reactive]
        public int SelectedCueInd
        {
            get => selectedCueInd;
            set
            {
                var prev = selectedCueInd;
                selectedCueInd = Math.Clamp(value, 0, Cues.Count);
                SelectedCue?.OnFocussed();
                if (prev != selectedCueInd)
                    OnPropertyChanged(nameof(SelectedCue));
            }
        }
        [Reactive]
        public CueViewModel? SelectedCue
        {
            get => SelectedCueInd >= 0 && SelectedCueInd < Cues.Count ? Cues[SelectedCueInd] : null;
            set
            {
                if (value != null)
                {
                    SelectedCueInd = Cues.IndexOf(value);
                    OnPropertyChanged(nameof(SelectedCueInd));
                    value.OnFocussed();
                }
            }
        }
        [Reactive] public ObservableCollection<CueViewModel> Cues { get; set; }
        [Reactive] public ObservableCollection<CueViewModel> ActiveCues { get; set; }
        [Reactive] public ObservableCollection<ObservableStruct<float>> ColumnWidths { get; set; }
        [Reactive] public ObservableCollection<CueViewModel> DraggingCues { get; set; }

        [Reactive] public ProjectSettingsViewModel ProjectSettings { get; private set; }

        [Reactive] public TimeSpan PreloadTime { get; set; }

        [Reactive] public RelayCommand NewProjectCommand { get; private set; }
        [Reactive] public RelayCommand OpenProjectCommand { get; private set; }
        [Reactive] public RelayCommand SaveProjectCommand { get; private set; }
        [Reactive] public RelayCommand SaveProjectAsCommand { get; private set; }
        [Reactive] public RelayCommand PackProjectCommand { get; private set; }
        [Reactive] public RelayCommand OpenLogCommand { get; private set; }
        [Reactive] public RelayCommand OpenSetttingsCommand { get; private set; }
        [Reactive] public RelayCommand OpenOnlineManualCommand { get; private set; }
        [Reactive] public RelayCommand OpenAboutCommand { get; private set; }

        [Reactive] public RelayCommand CreateCueMenuCommand { get; private set; }

        [Reactive] public RelayCommand<string> CreateCueCommand { get; private set; }

        [Reactive] public RelayCommand MoveCueUpCommand { get; private set; }
        [Reactive] public RelayCommand MoveCueDownCommand { get; private set; }
        [Reactive] public RelayCommand DeleteCueCommand { get; private set; }
        [Reactive] public RelayCommand DuplicateCueCommand { get; private set; }

        [Reactive] public RelayCommand GoCommand { get; private set; }
        [Reactive] public RelayCommand PauseCommand { get; private set; }
        [Reactive] public RelayCommand UnpauseCommand { get; private set; }
        [Reactive] public RelayCommand StopCommand { get; private set; }
        [Reactive] public RelayCommand UpCommand { get; private set; }
        [Reactive] public RelayCommand DownCommand { get; private set; }
        [Reactive] public RelayCommand PreloadCommand { get; private set; }

        [Reactive] public string? ProjectFilePath { get; private set; }

        [Reactive] public string StatusText { get; private set; } = "Ready";
        [Reactive] public SolidColorBrush StatusTextColour { get; private set; } = StatusInfoBrush;

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
        [Reactive] public string WindowTitle => $"QPlayer - {ProjectSettings.Title}";
        [Reactive]
        public string VersionString
        {
            get
            {
                var assembly = Assembly.GetAssembly(typeof(MainViewModel));
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
                var assembly = Assembly.GetAssembly(typeof(MainViewModel));
                if (assembly == null)
                    return string.Empty;
                var versionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
                return $"Copyright {versionInfo.LegalCopyright}";
            }
        }
        [Reactive] public string Clock => $"{DateTime.Now:HH:mm:ss}";
        #endregion

        /// <summary>
        /// The global instance of the audio playback manager.
        /// </summary>
        public AudioPlaybackManager AudioPlaybackManager => audioPlaybackManager;
        /// <summary>
        /// An event which is fired every 250ms on the main thread.
        /// </summary>
        public event Action? OnSlowUpdate;
        public OSCManager OSCManager => oscManager;

        public static readonly string AUTOBACK_PATH = "autoback.qproj";

        private int selectedCueInd = 0;
        private ShowFile showFile;
        private static ObservableCollection<string> logList = [];
        private static LogWindow? logWindow;
        private static SettingsWindow? settingsWindow;
        private static AboutWindow? aboutWindow;
        private static readonly object logListLock = new();
        private readonly JsonSerializerOptions jsonSerializerOptions = new()
        {
            IncludeFields = true,
            AllowTrailingCommas = true,
            WriteIndented = true,
        };
        private readonly Timer slowUpdateTimer;
        private readonly AudioPlaybackManager audioPlaybackManager;
        private readonly SynchronizationContext syncContext;
        private readonly Dictionary<decimal, CueViewModel> cuesDict;
        private readonly OSCManager oscManager;
        private static readonly SolidColorBrush StatusInfoBrush = new(Color.FromArgb(255, 220, 220, 220));
        private static readonly SolidColorBrush StatusWarningBrush = new(Color.FromArgb(255, 200, 220, 50));
        private static readonly SolidColorBrush StatusErrorBrush = new(Color.FromArgb(255, 220, 60, 40));
        private static string lastLogStatusMessage = "";
        private static DateTime lastLogStatusMessageTime = DateTime.MinValue;
        private static LogLevel lastLogStatusLevel = LogLevel.Info;

        //public static DateTime dbg_cueStartTime;

        public MainViewModel()
        {
            // Only run initialisation code once.
            // Otherwise the log window resets everything when it's opened
            //if (started)
            //    return;
            //started = true;

            LogList = logList;
            Log("Starting QPlayer...");
            Log("  Copyright Thomas Mathieson 2025");

            syncContext = SynchronizationContext.Current ?? new();
            audioPlaybackManager = new(this);
            oscManager = new(this);

            // Bind commands
            OpenLogCommand = new(OpenLogExecute);
            NewProjectCommand = new(NewProjectExecute);
            OpenProjectCommand = new(OpenProjectExecute);
            SaveProjectCommand = new(() => SaveProjectExecute());
            SaveProjectAsCommand = new(() => SaveProjectAsExecute());
            PackProjectCommand = new(() => SaveProjectAsExecute());
            OpenOnlineManualCommand = new(OpenOnlineManualExecute);
            OpenAboutCommand = new(OpenAboutExecute);
            OpenSetttingsCommand = new(OpenSettingsExecute);

            CreateCueMenuCommand = new(ShowCreateCueMenuExecute);
            CreateCueCommand = new(type => CreateCue(type));

            MoveCueUpCommand = new(MoveCueUpExecute);
            MoveCueDownCommand = new(MoveCueDownExecute);
            DeleteCueCommand = new(DeleteCueExecute);
            DuplicateCueCommand = new(() => DuplicateCueExecute());

            GoCommand = new(GoExecute);
            PauseCommand = new(PauseExecute);
            UnpauseCommand = new(UnpauseExecute);
            StopCommand = new(StopExecute);
            UpCommand = new(() => SelectedCueInd--);
            DownCommand = new(() => SelectedCueInd++);
            PreloadCommand = new(PreloadExecute);

            slowUpdateTimer = new(TimeSpan.FromMilliseconds(250));
            slowUpdateTimer.AutoReset = true;
            slowUpdateTimer.Elapsed += SlowUpdate;
            slowUpdateTimer.Start();

            ColumnWidths = new(Enumerable.Repeat<float>(60, 32).Select(x =>
            {
                var observable = new ObservableStruct<float>(x);
                // Bubble the change up
                observable.PropertyChanged += (o, e) =>
                {
                    OnPropertyChanged(nameof(ColumnWidths));
                };
                return observable;
            }));
            ProjectFilePath = null;
            ActiveCues = [];
            Cues = [];
            DraggingCues = [];
            cuesDict = [];
            Cues.CollectionChanged += (o, e) =>
            {
                switch (e.Action)
                {
                    case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                        {
                            CueViewModel item = (CueViewModel)e.NewItems![0]!;
                            cuesDict.TryAdd(item.QID, item);
                            break;
                        }
                    case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                        {
                            CueViewModel item = (CueViewModel)e.OldItems![0]!;
                            cuesDict.Remove(item.QID);
                            break;
                        }
                    case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                        {
                            CueViewModel oldItm = (CueViewModel)e.OldItems![0]!;
                            CueViewModel newItm = (CueViewModel)e.NewItems![0]!;
                            cuesDict.Remove(oldItm.QID);
                            cuesDict.TryAdd(newItm.QID, newItm);
                            break;
                        }
                    case System.Collections.Specialized.NotifyCollectionChangedAction.Move:
                        break;
                    case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                        {
                            cuesDict.Clear();
                            if (e.NewItems != null)
                            {
                                foreach (var item in e.NewItems)
                                {
                                    CueViewModel x = (CueViewModel)item!;
                                    cuesDict.TryAdd(x.QID, x);
                                }
                            }
                            break;
                        }
                }
            };
            ProjectSettings = new(this);
            ProjectSettings.PropertyChanged += (o, e) =>
            {
                if (e.PropertyName == nameof(ProjectSettingsViewModel.Title))
                    OnPropertyChanged(nameof(WindowTitle));
            };

            showFile = new();
            LoadShowfileModel(showFile);
            CreateCue(CueType.GroupCue, afterLast: true);
            CreateCue(CueType.SoundCue, afterLast: true);
            CreateCue(CueType.TimeCodeCue, afterLast: true);

            // These are redundant, they are done when we load the showfile
            //ConnectOSC();
            //OpenAudioDevice();
            oscManager.SubscribeOSC();
        }

        public void OnExit()
        {
            Log("Shutting down...");
            UnsavedChangedCheck(false);
            CloseAboutExecute();
            CloseSettingsExecute();
            CloseLogExecute();
            audioPlaybackManager.Stop();
            audioPlaybackManager.Dispose();
            Log("Goodbye!");
        }

        private void SlowUpdate(object? sender, ElapsedEventArgs e)
        {
            OnPropertyChanged(nameof(Clock));

            syncContext.Post(_ =>
            {
                OnSlowUpdate?.Invoke();

                if (DateTime.Now - lastLogStatusMessageTime > TimeSpan.FromSeconds(5))
                {
                    StatusText = $"Ready - {Cues.Count} cues in project";
                    StatusTextColour = StatusInfoBrush;
                }
                else
                {
                    StatusText = lastLogStatusMessage;
                    StatusTextColour = lastLogStatusLevel switch
                    {
                        LogLevel.Info => StatusInfoBrush,
                        LogLevel.Debug => StatusInfoBrush,
                        LogLevel.Warning => StatusWarningBrush,
                        LogLevel.Error => StatusErrorBrush,
                        _ => StatusInfoBrush
                    };
                }
            }, null);
        }

        #region Commands
        public void NewProjectExecute()
        {
            Log("Creating new project...");
            if (!UnsavedChangedCheck())
                return;

            ProjectFilePath = null;
            LoadShowfileModel(new());
        }

        public void SaveProjectExecute(bool async = true)
        {
            if (!string.IsNullOrEmpty(ProjectFilePath))
            {
                if (async)
                    Task.Run(() => SaveProjectAsync(ProjectFilePath));
                else
                    SaveProject(ProjectFilePath);
            }
            else
                SaveProjectAsExecute();
        }

        public void SaveProjectAsExecute(bool async = true)
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
                ProjectFilePath = saveFileDialog.FileName;
                SaveProjectExecute(async);
            }
        }

        public void OpenProjectExecute()
        {
            Log("Opening project...");
            if (!UnsavedChangedCheck())
                return;
            OpenFileDialog openFileDialog = new()
            {
                Multiselect = false,
                Title = "Open QPlayer Project",
                CheckFileExists = true,
                Filter = "QPlayer Projects (*.qproj)|*.qproj|All files (*.*)|*.*"
            };
            if (openFileDialog.ShowDialog() ?? false)
            {
                Task.Run(() => OpenProject(openFileDialog.FileName));
            }
        }

        public void OpenLogExecute()
        {
            if (logWindow != null)
            {
                logWindow.Activate();
                return;
            }

            logWindow = new(this);
            //logWindow.Owner = ((Window)e.Source);
            //Log("Opening log...");
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

        public void OpenOnlineManualExecute()
        {
            Process.Start(new ProcessStartInfo("https://space928.github.io/QPlayer/reference/") { UseShellExecute = true });
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
            //dbg_cueStartTime = DateTime.Now;
            //Log($"[Playback Debugging] Go command started! {dbg_cueStartTime:HH:mm:ss.ffff}");
            MeasureProfiler.StartCollectingData("Go Execute");

            do
            {
                if (SelectedCue?.Enabled ?? false)
                    SelectedCue.DelayedGo();
                SelectedCueInd += 1;
            } while ((!SelectedCue?.Halt ?? false) || (!SelectedCue?.Enabled ?? false));
        }

        public void PauseExecute()
        {
            for (int i = ActiveCues.Count - 1; i >= 0; i--)
                ActiveCues[i].Pause();
        }

        public void UnpauseExecute()
        {
            for (int i = ActiveCues.Count - 1; i >= 0; i--)
                if (ActiveCues[i].State == CueState.Paused)
                    ActiveCues[i].Go();
        }

        public void StopExecute()
        {
            for (int i = ActiveCues.Count - 1; i >= 0; i--)
                ActiveCues[i].Stop();

            AudioPlaybackManager.StopAllSounds();
        }

        public void PreloadExecute()
        {
            SelectedCue?.Preload(PreloadTime);
        }

        public void MoveCueUpExecute()
        {
            if (SelectedCue == null || SelectedCueInd <= 0)
                return;

            int ind = SelectedCueInd;
            var prevCue = Cues[ind - 1];
            // Swap the cue IDs and then swap the cues
            //(prevCue.QID, SelectedCue.QID) = (SelectedCue.QID, prevCue.QID);
            (Cues[ind], Cues[ind - 1]) = (Cues[ind - 1], Cues[ind]);
            (showFile.cues[ind], showFile.cues[ind - 1]) = (showFile.cues[ind - 1], showFile.cues[ind]);
            SelectedCueInd--;
            SelectedCue.QID = ChooseQID(SelectedCueInd, true);
        }

        public void MoveCueDownExecute()
        {
            if (SelectedCue == null || SelectedCueInd >= Cues.Count - 1)
                return;

            int ind = SelectedCueInd;
            var nextCue = Cues[ind + 1];
            // Swap the cue IDs and then swap the cues
            //(nextCue.QID, SelectedCue.QID) = (SelectedCue.QID, nextCue.QID);
            (Cues[ind], Cues[ind + 1]) = (Cues[ind + 1], Cues[ind]);
            (showFile.cues[ind], showFile.cues[ind + 1]) = (showFile.cues[ind + 1], showFile.cues[ind]);
            SelectedCueInd++;
            SelectedCue.QID = ChooseQID(SelectedCueInd, true);
        }

        public void DeleteCueExecute()
        {
            if (SelectedCue == null)
                return;

            int selectedInd = SelectedCueInd;
            Cues.RemoveAt(selectedInd);
            showFile.cues.RemoveAt(selectedInd);
            SelectedCueInd = selectedInd;
            OnPropertyChanged(nameof(SelectedCue));
        }

        public CueViewModel? DuplicateCueExecute(CueViewModel? src = null)
        {
            src ??= SelectedCue;
            if (src == null)
                return null;

            return CreateCue(src.Type, false, false, src);
        }

        public void ShowCreateCueMenuExecute()
        {
            ContextMenu menu = new();
            menu.IsOpen = true;
            foreach (var qtypeObj in typeof(CueType).GetEnumValues())
            {
                var qtype = (CueType)qtypeObj;
                if (qtype == CueType.None)
                    continue;
                menu.Items.Add(new MenuItem()
                {
                    Header = $"Create {qtype.ToString()[..^3]} Cue",
                    Command = new RelayCommand(() => CreateCue(qtype))
                });
            }
        }
        #endregion

        public static void Log(object message, LogLevel level = LogLevel.Info, [CallerMemberName] string caller = "")
        {
#if !DEBUG
            if (level <= LogLevel.Debug)
                return;
#endif

            lock (logListLock)
            {
                var time = DateTime.Now;
                string messageString = message?.ToString() ?? "null";
                string msg = $"[{level}] [{time}] [{caller}] {messageString}";
                LogList.Add(msg);
                Console.WriteLine(msg);
                Debug.WriteLine(msg);

                if (level != LogLevel.Debug)
                {
                    lastLogStatusLevel = level;
                    lastLogStatusMessage = messageString;
                    lastLogStatusMessageTime = time;
                }
            }
        }

        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error
        }

        public void NotifyQIDChanged(decimal oldVal, decimal newVal, CueViewModel src)
        {
            if (cuesDict.TryGetValue(oldVal, out var cue))
            {
                if (cue == src)
                    cuesDict.Remove(oldVal);

                cuesDict.TryAdd(newVal, src);
            }
            else
            {
                cuesDict.TryAdd(newVal, src);
            }
        }

        public bool FindCue(object id, [NotNullWhen(true)] out CueViewModel? cue)
        {
            switch (id)
            {
                case int idInt:
                    return cuesDict.TryGetValue(idInt, out cue);
                case float idFloat:
                    return cuesDict.TryGetValue(decimal.CreateTruncating(idFloat), out cue);
                case decimal idDec:
                    return cuesDict.TryGetValue(idDec, out cue);
                case string idString:
                    return cuesDict.TryGetValue(decimal.Parse(idString), out cue);
                default:
                    cue = null;
                    Log($"Couldn't find cue with ID: {id}!", LogLevel.Warning);
                    return false;
            }
        }

        public bool FindCue(decimal id, [NotNullWhen(true)] out CueViewModel? cue)
        {
            return cuesDict.TryGetValue(id, out cue);
        }

        /// <summary>
        /// Converts a path to/from a project relative path. Only paths which are in subdirectories of the project path are made relative.
        /// </summary>
        /// <param name="path">the path to convert</param>
        /// <param name="expand">whether the path should be expanded to an absolute path or made relative to the project</param>
        /// <returns></returns>
        public string ResolvePath(string path, bool expand = true)
        {
            string? projPath = ProjectFilePath;
            if (string.IsNullOrEmpty(projPath))
                return path;

            string? projDir = Path.GetDirectoryName(projPath);
            if (string.IsNullOrEmpty(projDir))
                return path;

            if (expand)
            {
                if (File.Exists(path))
                    return path;

                string ret = Path.Combine(projDir, path);
                if (File.Exists(ret))
                    return ret;

                // The file wasn't found, try searching for it in the project directory
                string fileName = Path.GetFileName(path);
                if (string.IsNullOrEmpty(fileName) || fileName == ".")
                    return path;
                foreach (string file in Directory.EnumerateFiles(projDir, fileName, SearchOption.AllDirectories))
                    if (Path.GetFileName(file) == fileName)
                        return file;

                // The file couldn't be found, let it fail normally.
                return path;
            }
            else
            {
                if (string.IsNullOrEmpty(path))
                    return path;
                string absPath = Path.GetFullPath(path);
                if (absPath.Contains(projDir) && absPath != projDir)
                    return Path.GetRelativePath(projDir, absPath);
                return path;
            }
        }

        /// <summary>
        /// Moves the given cue in the cue stack to the given cue ID, or directly after it if the 
        /// specified cue ID is already in use.
        /// </summary>
        /// <param name="cue">The cue to move in the cue stack.</param>
        /// <param name="newId">The cue ID to insert the cue at.</param>
        /// <param name="select">Whether the moved cue should be reselected.</param>
        public void MoveCue(CueViewModel cue, decimal newId, bool select = true)
        {
            int ind;
            for (ind = 0; ind < Cues.Count; ind++)
            {
                var qid = Cues[ind].QID;
                if (newId == qid)
                    newId = ChooseQID(ind);
                if (newId >= qid)
                    break;
            }

            MoveCue(cue, ind, select);
            cue.QID = newId;
        }

        /// <summary>
        /// Moves the given cue in the cue stack to the specified index.
        /// </summary>
        /// <param name="cue">The cue to move in the cue stack.</param>
        /// <param name="index">The index within the cue stack to move the cue to.</param>
        /// <param name="select">Whether the moved cue should be reselected.</param>
        public void MoveCue(CueViewModel cue, int index, bool select = true)
        {
            // Find the src and dst indices
            index = Math.Clamp(index, 0, Cues.Count);
            int origIndex = Cues.IndexOf(cue);
            if (origIndex == -1)
            {
                Log($"Failed to move cue {cue.QID} '{cue.Name}'! Could not be found in cue stack.", LogLevel.Error);
                return;
            }

            // The cue doesn't need to be moved, don't do anything.
            if (origIndex == index)
                return;

            // Remove the cue
            Cues.RemoveAt(origIndex);
            var cueModel = showFile.cues[origIndex];
            showFile.cues.RemoveAt(origIndex);

            if (index > origIndex)
                index--;

            // Update it's qid
            cue.QID = ChooseQID(index - 1);

            // Reinsert it
            Cues.Insert(index, cue);
            showFile.cues.Insert(index, cueModel);

            if (select)
                SelectedCueInd = index;
        }

        /// <summary>
        /// Checks if the project file has unsaved changes and prompts the user to save if needed.
        /// </summary>
        /// <returns>false if the user decided to cancel the current operation.</returns>
        private bool UnsavedChangedCheck(bool canCancel = true)
        {
            SaveProject(AUTOBACK_PATH);
            string? curr = null;
            string? prev = null;
            try
            {
                curr = File.ReadAllText(AUTOBACK_PATH);
                prev = string.IsNullOrEmpty(ProjectFilePath) ? "#" : File.ReadAllText(ProjectFilePath);
            }
            catch { }
            if (curr == null || curr != prev)
            {
                // There are unsaved changes!
                var mbRes = MessageBox.Show("Current project has unsaved changes! Do you wish to save them now?",
                    "Unsaved Changes",
                    canCancel ? MessageBoxButton.YesNoCancel : MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.Yes);
                switch (mbRes)
                {
                    case MessageBoxResult.Yes:
                        SaveProjectExecute(false);
                        break;
                    case MessageBoxResult.No:
                        return true;
                    case MessageBoxResult.Cancel:
                        Log("   aborted!");
                        return false;
                }
            }
            return true;
        }

        private void LoadShowfileModel(ShowFile show)
        {
            showFile = show;
            ProjectSettings = ProjectSettingsViewModel.FromModel(show.showSettings, this);
            ProjectSettings.Bind(show.showSettings);
            ProjectSettings.PropertyChanged += (o, e) =>
            {
                if (e.PropertyName == nameof(ProjectSettingsViewModel.Title))
                    OnPropertyChanged(nameof(WindowTitle));
            };
            for (int i = 0; i < Math.Min(showFile.columnWidths.Count, ColumnWidths.Count); i++)
                ColumnWidths[i].Value = showFile.columnWidths[i];
            Cues.Clear();
            foreach (Cue c in showFile.cues)
            {
                var vm = CueViewModel.FromModel(c, this);
                vm.Bind(c);
                Cues.Add(vm);
            }
            OnPropertyChanged(nameof(SelectedCue));
            oscManager.ConnectOSC();
            OpenAudioDevice();
        }

        /// <summary>
        /// This effectively makes sure that the data in the view model is copied to the model, just in case a change was missed.
        /// </summary>
        private void EnsureShowfileModelSync()
        {
            Dictionary<decimal, Cue> cueModels = new(showFile.cues.Select((x) => new KeyValuePair<decimal, Cue>(x.qid, x)));
            foreach (CueViewModel vm in Cues)
            {
                if (!cueModels.TryGetValue(vm.QID, out Cue? value))
                {
                    Log($"Cue with id {vm.QID} exists in the editor but not in the internal model! Potential corruption detected!", LogLevel.Warning);
                    // TODO: If we want to be nice we could just create the model here...
                }
                else
                {
                    var q = value;
                    vm.ToModel(q);
                }
            }
            ProjectSettings.ToModel(showFile.showSettings);
            showFile.columnWidths = ColumnWidths.Select(x => x.Value).ToList();
            showFile.fileFormatVersion = ShowFile.FILE_FORMAT_VERSION;
        }

        public async Task OpenProject(string path)
        {
            Log($"Loading project from: {path}");
            try
            {
                using var f = File.OpenRead(path);
                ShowFile showFile;
                try
                {
                    showFile = await JsonSerializer.DeserializeAsync<ShowFile>(f, jsonSerializerOptions)
                        ?? throw new FileFormatException("Show file deserialized as null!");
                }
                catch
                {
                    Log($"Show file is corrupt or out of date, attempting to repair...", LogLevel.Warning);
                    showFile = await ShowFileConverter.LoadShowFileSafeAsync(f);
                }

                if (showFile.fileFormatVersion != ShowFile.FILE_FORMAT_VERSION)
                {
                    //Log($"Project file version '{showFile.fileFormatVersion}' does not match QPlayer version '{ShowFile.FILE_FORMAT_VERSION}'!", LogLevel.Warning);
                    f.Position = 0;
                    await ShowFileConverter.UpgradeShowFileAsync(showFile, f);
                }

                syncContext.Send(_ =>
                {
                    ProjectFilePath = path;
                    LoadShowfileModel(showFile);
                }, null);

                Log($"Loaded project from disk! {path}");
            }
            catch (Exception e)
            {
                Log($"Couldn't load project from disk. Trying to load {path} \n  failed with: {e}", LogLevel.Warning);
            }
        }

        public async Task SaveProjectAsync(string path)
        {
            try
            {
                Log("Saving project...");
                // For now, this method can't be trusted on other threads, let run on the main thread.
                // Chances are this method is being called from the main thread anyway, so it shouldn't
                // make a difference.
                syncContext.Send(_ =>
                {
                    EnsureShowfileModelSync();
                }, null);

                using var f = File.OpenWrite(path);
                using var ms = new MemoryStream();
                await JsonSerializer.SerializeAsync(ms, showFile, jsonSerializerOptions);
                ms.Position = 0;
                ms.CopyTo(f);

                if (ProjectSettings.EnableRemoteControl && ProjectSettings.SyncShowFileOnSave)
                {
                    ms.Position = 0;
                    try
                    {
                        await oscManager.SendRemoteUpdateShowFileAsync(ProjectSettings.RemoteNodes.Select(x => x.Name), ms.ToArray());
                    }
                    catch (Exception ex)
                    {
                        Log($"Error while sending show file to remote client: '{ex.Message}'\n{ex}", LogLevel.Error);
                    }
                }

                Log($"Saved project to {path}!");
            }
            catch (Exception e)
            {
                Log($"Couldn't save project to disk. Trying to save {path} \n  failed with: {e}", LogLevel.Warning);
            }
        }

        public void SaveProject(string path)
        {
            try
            {
                Log("Saving project...");
                EnsureShowfileModelSync();

                using var f = File.OpenWrite(path);
                using var ms = new MemoryStream();
                JsonSerializer.Serialize(ms, showFile, jsonSerializerOptions);
                ms.Position = 0;
                ms.CopyTo(f);

                if (ProjectSettings.EnableRemoteControl && ProjectSettings.SyncShowFileOnSave)
                {
                    ms.Position = 0;
                    oscManager.SendRemoteUpdateShowFile(ProjectSettings.RemoteNodes.Select(x => x.Name), ms.ToArray());
                }

                Log($"Saved project to {path}!");
            }
            catch (Exception e)
            {
                Log($"Couldn't save project to disk. Trying to save {path} \n  failed with: {e}", LogLevel.Warning);
            }
        }

        public CueViewModel? CreateCue(string? type)
        {
            if (Enum.TryParse<CueType>(type, true, out var cueType))
                return CreateCue(cueType);
            return null;
        }

        public CueViewModel CreateCue(CueType type, bool beforeCurrent = false, bool afterLast = false, CueViewModel? src = null)
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
                case CueType.DummyCue: model = new DummyCue(); break;
                case CueType.SoundCue: model = new SoundCue(); break;
                case CueType.TimeCodeCue: model = new TimeCodeCue(); break;
                case CueType.StopCue: model = new StopCue(); ; break;
                case CueType.VolumeCue: model = new VolumeCue(); break;
                case CueType.VideoCue: model = new VideoCue(); break;
                default: throw new NotImplementedException();
            }

            src?.ToModel(model);

            decimal newId = ChooseQID(insertAfterInd);

            model.qid = newId;
            var ret = CueViewModel.FromModel(model, this);
            ret.Bind(model);
            if (insertAfterInd + 1 <= Cues.Count - 1)
            {
                showFile.cues.Insert(insertAfterInd + 1, model);
                Cues.Insert(insertAfterInd + 1, ret);
            }
            else
            {
                showFile.cues.Add(model);
                Cues.Add(ret);
            }

            return ret;
        }

        /// <summary>
        /// Generates a QID at the given insertion point in the cue stack, renumbering cues if needed.
        /// </summary>
        /// <param name="insertAfterInd">The index of the cue after which to insert the new QID.</param>
        /// <param name="ignoreCurrent">When enabled the first parameter is the index of the cue to 
        /// renumber such that it fits in between it's neighbours.</param>
        /// <returns></returns>
        private decimal ChooseQID(int insertAfterInd, bool ignoreCurrent = false)
        {
            if (Cues.Count == 0)
                return 1;

            decimal newId = 1;
            decimal prevId = 0;
            decimal nextId = decimal.MaxValue;

            int insertBeforeInd = insertAfterInd + 1;
            if (ignoreCurrent)
            {
                insertAfterInd--;
                //insertBeforeInd++;
            }

            if (insertAfterInd >= 0)
                prevId = Cues[Math.Min(insertAfterInd, Cues.Count - 1)].QID;

            if (insertBeforeInd - 1 < Cues.Count - 1)
                nextId = Cues[Math.Max(insertBeforeInd, 0)].QID;

            decimal increment = 10;
            for (int i = 0; i < 4; i++)
            {
                increment *= 0.1m;
                newId = ((int)(prevId / increment) * increment) + increment;//(int)(prevId * increment) + increment;
                if (newId < nextId)
                    return newId.Normalize();
            }

            // No suitable cue ID could be found, renumber subsequent cues to fit this one in
            decimal prev = newId;
            for (int i = insertBeforeInd; i < Cues.Count; i++)
            {
                var next = Cues[i].QID;
                if (next > prev)
                    break;
                Cues[i].QID = prev = (next + increment).Normalize();
            }

            return newId.Normalize();
        }

        public void OpenAudioDevice()
        {
            AudioPlaybackManager.OpenOutputDevice(
                ProjectSettings.AudioOutputDriver,
                ProjectSettings.SelectedAudioOutputDeviceKey,
                ProjectSettings.AudioLatency);
        }

        /// <summary>
        /// Attempts to load an embedded resource file from the executing assembly.
        /// </summary>
        /// <param name="path">The path/filename to the file to load.</param>
        /// <returns>A stream of the file contents.</returns>
        /// <exception cref="FileNotFoundException"></exception>
        public static Stream LoadResourceFile(string path)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string? resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(str => str.EndsWith(Path.GetFileName(path)));

            if (resourceName == null || assembly == null)
                throw new FileNotFoundException(path);

            return assembly.GetManifestResourceStream(resourceName) ?? throw new FileNotFoundException(path);
        }
    }

    public class ObservableStruct<T> : ObservableObject where T : struct
    {
        [Reactive] public T Value { get; set; }

        public ObservableStruct() { }

        public ObservableStruct(T value)
        {
            Value = value;
        }
    }
}
