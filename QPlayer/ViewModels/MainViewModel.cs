using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using QPlayer.Models;
using QPlayer.Views;
using ReactiveUI.Fody.Helpers;
using Rug.Osc;
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
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
        [Reactive] public RelayCommand OpenAboutCommand { get; private set; }

        [Reactive] public RelayCommand CreateCueCommand { get; private set; }

        [Reactive] public RelayCommand CreateGroupCueCommand { get; private set; }
        [Reactive] public RelayCommand CreateDummyCueCommand { get; private set; }
        [Reactive] public RelayCommand CreateSoundCueCommand { get; private set; }
        [Reactive] public RelayCommand CreateTimeCodeCueCommand { get; private set; }
        [Reactive] public RelayCommand CreateStopCueCommand { get; private set; }
        [Reactive] public RelayCommand CreateVolumeCueCommand { get; private set; }

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

        public AudioPlaybackManager AudioPlaybackManager => audioPlaybackManager;

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
        private readonly OSCDriver oscDriver;
        private readonly SynchronizationContext syncContext;
        private readonly Dictionary<decimal, CueViewModel> cuesDict;

        public MainViewModel()
        {
            // Only run initialisation code once.
            // Otherwise the log window resets everything when it's opened
            //if (started)
            //    return;
            //started = true;

            LogList = logList;
            Log("Starting QPlayer...");
            Log("  Copyright Thomas Mathieson 2024");

            syncContext = SynchronizationContext.Current ?? new();
            audioPlaybackManager = new(this);
            oscDriver = new();

            // Bind commands
            OpenLogCommand = new(OpenLogExecute);
            NewProjectCommand = new(NewProjectExecute);
            OpenProjectCommand = new(OpenProjectExecute);
            SaveProjectCommand = new(SaveProjectExecute);
            SaveProjectAsCommand = new(SaveProjectAsExecute);
            PackProjectCommand = new(SaveProjectAsExecute);
            OpenAboutCommand = new(OpenAboutExecute);
            OpenSetttingsCommand = new(OpenSettingsExecute);

            CreateCueCommand = new(ShowCreateCueMenuExecute); ;
            CreateGroupCueCommand = new(() => CreateCue(CueType.GroupCue));
            CreateDummyCueCommand = new(() => CreateCue(CueType.DummyCue));
            CreateSoundCueCommand = new(() => CreateCue(CueType.SoundCue));
            CreateTimeCodeCueCommand = new(() => CreateCue(CueType.TimeCodeCue));
            CreateStopCueCommand = new(() => CreateCue(CueType.StopCue));
            CreateVolumeCueCommand = new(() => CreateCue(CueType.VolumeCue));

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

            ConnectOSC();
            SubscribeOSC();
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

        public void SaveProjectExecute()
        {
            if (ProjectFilePath != null)
                SaveProject(ProjectFilePath);
            else
                SaveProjectAsExecute();
        }

        public void SaveProjectAsExecute()
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
                OpenProject(openFileDialog.FileName);
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

        public void OnQIDChanged(decimal oldVal, decimal newVal, CueViewModel src)
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
                        if (string.IsNullOrEmpty(ProjectFilePath))
                            SaveProjectExecute();
                        else
                            SaveProject(ProjectFilePath);
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
            ProjectSettings = ProjectSettingsViewModel.FromModel(show.showMetadata, this);
            ProjectSettings.Bind(show.showMetadata);
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
            ConnectOSC();
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
            ProjectSettings.ToModel(showFile.showMetadata);
            showFile.columnWidths = ColumnWidths.Select(x => x.Value).ToList();
            showFile.fileFormatVersion = ShowFile.FILE_FORMAT_VERSION;
        }

        public void OpenProject(string path)
        {
            try
            {
                var s = JsonSerializer.Deserialize<ShowFile>(File.ReadAllText(path), jsonSerializerOptions)
                    ?? throw new FileFormatException("Show file deserialized as null!");
                if (s.fileFormatVersion != ShowFile.FILE_FORMAT_VERSION)
                    Log($"Project file version '{s.fileFormatVersion}' does not match QPlayer version '{ShowFile.FILE_FORMAT_VERSION}'!", LogLevel.Warning);
                ProjectFilePath = path;
                LoadShowfileModel(s);

                Log($"Loaded project from disk! {path}");
            }
            catch (Exception e)
            {
                Log($"Couldn't load project from disk. Trying to load {path} \n  failed with: {e}", LogLevel.Warning);
            }
        }

        public void SaveProject(string path)
        {
            try
            {
                EnsureShowfileModelSync();
                File.WriteAllText(path, JsonSerializer.Serialize(showFile, jsonSerializerOptions));
                Log($"Saved project to {path}!");
            }
            catch (Exception e)
            {
                Log($"Couldn't save project to disk. Trying to save {path} \n  failed with: {e}", LogLevel.Warning);
            }
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

        public void ConnectOSC()
        {
            if (oscDriver.OSCConnect(ProjectSettings.OSCNic, ProjectSettings.OSCRXPort, ProjectSettings.OSCTXPort))
            {

            }
        }

        public void MonitorOSC(bool enable)
        {
            if (enable)
            {
                oscDriver.OnRXMessage += OscDriver_LogRXMessage;
                oscDriver.OnTXMessage += OscDriver_LogTXMessage;
            }
            else
            {
                oscDriver.OnRXMessage -= OscDriver_LogRXMessage;
                oscDriver.OnTXMessage -= OscDriver_LogTXMessage;
            }
        }

        private void OscDriver_LogRXMessage(OscPacket obj)
        {
            Log($"OSC RX: {obj}", LogLevel.Info);
        }

        private void OscDriver_LogTXMessage(OscPacket obj)
        {
            Log($"OSC TX: {obj}", LogLevel.Info);
        }

        private void SubscribeOSC()
        {
            oscDriver.Subscribe("/qplayer/go", msg =>
            {
                if (msg.Count > 0)
                {
                    if (FindCue(msg[0], out var cue))
                    {
                        if (msg.Count > 1)
                        {
                            SelectedCue = cue;
                            GoExecute();
                        }
                        else
                        {
                            cue.DelayedGo();
                        }
                    }
                    else
                    {
                        Log($"Couldn't find cue with ID {msg[0]}!", LogLevel.Warning);
                    }
                }
                else
                    GoExecute();
            }, syncContext);
            oscDriver.Subscribe("/qplayer/stop", msg =>
            {
                if (msg.Count > 0)
                {
                    if (FindCue(msg[0], out var cue))
                        cue.Stop();
                }
                else
                    StopExecute();
            }, syncContext);
            oscDriver.Subscribe("/qplayer/pause", msg =>
            {
                if (msg.Count > 0)
                {
                    if (FindCue(msg[0], out var cue))
                        cue.Pause();
                }
                else
                    PauseExecute();
            }, syncContext);
            oscDriver.Subscribe("/qplayer/unpause", msg =>
            {
                if (msg.Count > 0)
                {
                    if (FindCue(msg[0], out var cue) && cue.State == CueState.Paused)
                        cue.Go();
                }
                else
                    UnpauseExecute();
            }, syncContext);
            oscDriver.Subscribe("/qplayer/preload", msg =>
            {
                if (msg.Count > 0)
                {
                    if (FindCue(msg[0], out var cue))
                    {
                        if (msg.Count > 1 && msg[1] is float time)
                            cue.Preload(TimeSpan.FromSeconds(time));
                        else
                            cue.Preload(PreloadTime);
                    }
                }
                else
                {
                    PreloadExecute();
                }
            }, syncContext);

            oscDriver.Subscribe("/qplayer/select", msg =>
            {
                if (msg.Count > 0 && FindCue(msg[0], out var cue))
                {
                    SelectedCue = cue;
                }
            }, syncContext);
            oscDriver.Subscribe("/qplayer/up", _ => SelectedCueInd--, syncContext);
            oscDriver.Subscribe("/qplayer/down", _ => SelectedCueInd++, syncContext);

            oscDriver.Subscribe("/qplayer/save", _ => SaveProjectExecute(), syncContext);
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
