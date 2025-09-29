using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetBrains.Profiler.Api;
using Microsoft.Win32;
using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.Utilities;
using QPlayer.Views;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
using System.Windows.Threading;
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
        [Reactive] public bool EnableAutosave { get; set; } = true;

        [Reactive] public ProjectSettingsViewModel ProjectSettings { get; private set; }

        [Reactive] public TimeSpan PreloadTime { get; set; }

        [Reactive] public RelayCommand NewProjectCommand { get; private set; }
        [Reactive] public RelayCommand OpenProjectCommand { get; private set; }
        [Reactive] public RelayCommand<string> OpenSpecificProjectCommand { get; private set; }
        [Reactive] public RelayCommand SaveProjectCommand { get; private set; }
        [Reactive] public RelayCommand SaveProjectAsCommand { get; private set; }
        [Reactive] public RelayCommand PackProjectCommand { get; private set; }
        [Reactive] public RelayCommand OpenLogCommand { get; private set; }
        [Reactive] public RelayCommand OpenRemoteNodesCommand { get; private set; }
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

        [Reactive] public ProgressBoxViewModel ProgressBoxViewModel { get; private set; }
        [Reactive] public AudioMeterViewModel MainAudioMeter { get; private set; }

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
        [Reactive] public string WindowTitle => $"QPlayer – {ProjectSettings.Title}";
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
        public ObservableCollection<RecentFile> RecentFiles => persistantDataManager.RecentFiles;
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
        public MSCManager MSCManager => mscManager;
        public PersistantDataManager PersistantDataManager => persistantDataManager;

        private volatile bool fastUpdateInProgress;
        private int selectedCueInd = 0;
        private ShowFile showFile;
        private List<string>? captureResolvedPaths;
        private Dictionary<string, string>? packedPaths;
        private int autoBackInd = 0;
        private static ObservableCollection<string> logList = [];
        private static LogWindow? logWindow;
        private static RemoteNodesWindow? remoteNodesWindow;
        private static SettingsWindow? settingsWindow;
        private static AboutWindow? aboutWindow;
        private static readonly object logListLock = new();
        private readonly JsonSerializerOptions jsonSerializerOptions = new()
        {
            IncludeFields = true,
            AllowTrailingCommas = true,
            WriteIndented = true,
        };
        private readonly Timer fastUpdateTimer;
        private readonly Timer slowUpdateTimer;
        private readonly Timer autosaveTimer;
        private readonly AudioPlaybackManager audioPlaybackManager;
        private readonly SynchronizationContext syncContext;
        private readonly Dispatcher dispatcher;
        private readonly Dictionary<decimal, CueViewModel> cuesDict;
        private readonly OSCManager oscManager;
        private readonly MSCManager mscManager;
        private readonly PersistantDataManager persistantDataManager;
        private readonly NumberFormatInfo numberFormat = CultureInfo.InvariantCulture.NumberFormat;
        private static readonly SolidColorBrush StatusInfoBrush = new(Color.FromArgb(255, 220, 220, 220));
        private static readonly SolidColorBrush StatusWarningBrush = new(Color.FromArgb(255, 200, 220, 50));
        private static readonly SolidColorBrush StatusErrorBrush = new(Color.FromArgb(255, 220, 60, 40));
        private static string lastLogStatusMessage = "";
        private static DateTime lastLogStatusMessageTime = DateTime.MinValue;
        private static LogLevel lastLogStatusLevel = LogLevel.Info;
        private static readonly EnumerationOptions fileSearchEnumerationOptions = new()
        {
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.PlatformDefault,
            MatchType = MatchType.Simple,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false,
            MaxRecursionDepth = 5
        };
        private readonly byte[] defaultShowfile; // So that the unsaved changes check works correctly, we compare against the default showfile

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

            //foreach (FontFamily fontFamily in Fonts.GetFontFamilies(new Uri("pack://application:,,,/"), "./Resources/"))
            //    Log($"Found embedded font: {fontFamily.Source}", LogLevel.Debug);

            syncContext = SynchronizationContext.Current ?? new();
            dispatcher = Dispatcher.CurrentDispatcher;
            audioPlaybackManager = new(this);
            oscManager = new(this);
            mscManager = new(this);
            persistantDataManager = new();
            ProgressBoxViewModel = new();
            MainAudioMeter = new(dispatcher);

            // Bind commands
            OpenLogCommand = new(OpenLogExecute);
            OpenRemoteNodesCommand = new(OpenRemoteNodesExecute);
            NewProjectCommand = new(NewProjectExecute);
            OpenProjectCommand = new(OpenProjectExecute);
            OpenSpecificProjectCommand = new(OpenSpecificProjectExecute);
            SaveProjectCommand = new(() => SaveProjectExecute());
            SaveProjectAsCommand = new(() => SaveProjectAsExecute());
            PackProjectCommand = new(PackProjectExecute);
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

            fastUpdateTimer = new(40);
            fastUpdateTimer.AutoReset = true;
            fastUpdateTimer.Elapsed += FastUpdate;
            fastUpdateTimer.Start();

            slowUpdateTimer = new(TimeSpan.FromMilliseconds(250));
            slowUpdateTimer.AutoReset = true;
            slowUpdateTimer.Elapsed += SlowUpdate;
            slowUpdateTimer.Start();

            autosaveTimer = new(TimeSpan.FromMinutes(5));
            autosaveTimer.AutoReset = true;
            autosaveTimer.Elapsed += AutoSave;
            autosaveTimer.Start();

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

            audioPlaybackManager.OnMixerMeter += MainAudioMeter.ProcessSample;

            showFile = new();
            LoadShowfileModel(showFile);
            CreateCue(CueType.SoundCue);
            /*CreateCue(CueType.GroupCue, afterLast: true);
            CreateCue(CueType.SoundCue, afterLast: true);
            CreateCue(CueType.TimeCodeCue, afterLast: true);*/

            using var ms = new MemoryStream();
            SerializeShowFile(ms).Wait();
            defaultShowfile = ms.ToArray();

            // These are redundant, they are done when we load the showfile
            //ConnectOSC();
            //OpenAudioDevice();
            oscManager.SubscribeOSC();
            mscManager.SubscribeMSC();

            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1)
            {
                if (File.Exists(args[1]))
                {
                    OpenSpecificProjectExecute(args[1]);
                }
            }
        }

        public bool OnExit()
        {
            if (ActiveCues.Count > 0)
            {
                var mbRes = MessageBox.Show("Cues are currently playing! Do you want to exit QPlayer anyway?",
                "Exit QPlayer",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning, MessageBoxResult.Cancel);

                // Abort exit
                if (mbRes == MessageBoxResult.Cancel)
                    return false;
            }

            if (!UnsavedChangedCheck(true))
                return false;

            Log("Shutting down...");
            CloseAboutExecute();
            CloseSettingsExecute();
            CloseLogExecute();
            CloseRemoteNodesExecute();
            slowUpdateTimer.Stop();
            fastUpdateTimer.Stop();
            autosaveTimer.Stop();
            audioPlaybackManager.Stop();
            audioPlaybackManager.Dispose();
            Log("Goodbye!");
            return true;
        }

        private void FastUpdate(object? sender, ElapsedEventArgs e)
        {
            // Skip this update if one is already in progress.
            if (fastUpdateInProgress)
                return;

            fastUpdateInProgress = true;
            try
            {
                dispatcher.Invoke(() =>
                {
                    try
                    {
                        fastUpdateInProgress = true;

                        // Ask each active cue to update it's UI status if it needs too.
                        // Reverse iterate the list, since cues can stop themselves. If a cue stops/starts another cue
                        // during this iteration then a cue may be invoked twice/skipped. This is fine so long as we 
                        // have eventual consistency.
                        for (int i = ActiveCues.Count - 1; i >= 0; i--)
                        {
                            var cue = ActiveCues[i];
                            cue.UpdateUIStatus();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Error while updating cue status: {ex.Message}\n{ex}", LogLevel.Error);
                    }
                    finally
                    {
                        fastUpdateInProgress = false;
                    }
                }, DispatcherPriority.Background);
            } catch { }
        }

        private void SlowUpdate(object? sender, ElapsedEventArgs e)
        {
            OnPropertyChanged(nameof(Clock));

            syncContext.Post(_ =>
            {
                OnSlowUpdate?.Invoke();

                if (DateTime.Now - lastLogStatusMessageTime > TimeSpan.FromSeconds(5))
                {
                    StatusText = $"Ready – {Cues.Count} cues in project";
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

        private void AutoSave(object? sender, ElapsedEventArgs e)
        {
            if (EnableAutosave)
            {
                string path = Path.Combine(persistantDataManager.AutoBackDir, $"autoback{autoBackInd + 1}.qproj");
                autoBackInd = (autoBackInd + 1) % 5;
                SaveProjectAsync(path, false, false).ContinueWith((_) => Log($"Autosaved project to '{path}'."));
            }
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

        public void PackProjectExecute()
        {
            SaveFileDialog saveFileDialog = new()
            {
                AddExtension = true,
                DereferenceLinks = true,
                Filter = "QPlayer Projects (*.qproj)|*.qproj|All files (*.*)|*.*",
                OverwritePrompt = true,
                Title = "Pack QPlayer Project"
            };
            if (saveFileDialog.ShowDialog() ?? false)
            {
                Task.Run(() => PackProject(Path.ChangeExtension(saveFileDialog.FileName, null)))
                    .ContinueWith(_ => syncContext.Post(_ => ProjectFilePath = saveFileDialog.FileName, null));
            }
        }

        public void OpenProjectExecute()
        {
            Log("Opening project...");
            ProgressBoxViewModel.Message = "Opening project...";
            ProgressBoxViewModel.Progress = 0.0f;
            ProgressBoxViewModel.Visible = Visibility.Visible;
            Dispatcher.Yield();
            if (!UnsavedChangedCheck())
            {
                ProgressBoxViewModel.Visible = Visibility.Collapsed;
                return;
            }
            OpenFileDialog openFileDialog = new()
            {
                Multiselect = false,
                Title = "Open QPlayer Project",
                CheckFileExists = true,
                Filter = "QPlayer Projects (*.qproj)|*.qproj|All files (*.*)|*.*"
            };
            if (openFileDialog.ShowDialog() ?? false)
            {
                Dispatcher.Yield();
                Task.Run(() => OpenProject(openFileDialog.FileName));
            }
            else
            {
                ProgressBoxViewModel.Visible = Visibility.Collapsed;
            }
        }

        public void OpenSpecificProjectExecute(string? path)
        {
            Log("Opening project...");
            ProgressBoxViewModel.Message = "Opening project...";
            ProgressBoxViewModel.Progress = 0.0f;
            ProgressBoxViewModel.Visible = Visibility.Visible;
            Dispatcher.Yield();
            if (!UnsavedChangedCheck())
            {
                ProgressBoxViewModel.Visible = Visibility.Collapsed;
                return;
            }
            if (!string.IsNullOrEmpty(path))
            {
                Dispatcher.Yield();
                Task.Run(() => OpenProject(path));
            }
            else
            {
                ProgressBoxViewModel.Visible = Visibility.Collapsed;
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

        public void OpenRemoteNodesExecute()
        {
            if (remoteNodesWindow != null)
            {
                remoteNodesWindow.Activate();
                return;
            }

            remoteNodesWindow = new(new(this));
            //logWindow.Owner = ((Window)e.Source);
            //Log("Opening log...");
            remoteNodesWindow.Closed += (e, x) => { remoteNodesWindow = null; };
            remoteNodesWindow.Show();
        }

        public void CloseRemoteNodesExecute()
        {
            remoteNodesWindow?.Close();
            remoteNodesWindow = null;
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

            // Get the cue to run
            var cue = SelectedCue;
            if (cue == null)
                return;

            CueViewModel? waitCue = null;
            int i = SelectedCueInd;

            while (true)
            {
                // If this cue is enabled, run it
                if (cue.Enabled)
                    cue.DelayedGo(waitCue);

                i++;
                if (i >= Cues.Count) break;

                // Look at the next cue in the stack to determine if we should keep executing cues.
                var next = Cues[i];
                if (next == null)
                    break;

                if (next.Enabled)
                {
                    if (next.Trigger == TriggerMode.Go)
                        break;
                    else if (next.Trigger == TriggerMode.AfterLast)
                        waitCue = cue;
                }
                cue = next;
            }
            SelectedCueInd = i;
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
            //for (int i = ActiveCues.Count - 1; i >= 0; i--)
            //    ActiveCues[i].Stop();
            for (int i = 0; i < Cues.Count; i++)
                Cues[i].Stop();

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
            //var prevCue = Cues[ind - 1];
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
            //var nextCue = Cues[ind + 1];
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

        /// <summary>
        /// Informs the cue stack that the cue ID of a given cue view model has been changed. This should be called whenever a QID is changed.
        /// <para/>
        /// Note that since <see cref="CueViewModel.QID"/>'s setter calls this method, users which change QID's through this 
        /// setter need not call this method.
        /// </summary>
        /// <param name="oldVal"></param>
        /// <param name="newVal"></param>
        /// <param name="src"></param>
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

        /// <summary>
        /// Tries to find a cue view model given a cue ID.
        /// </summary>
        /// <remarks>
        /// The <paramref name="id"/> can be one of the following types:
        /// <see langword="int"/>,
        /// <see langword="float"/>,
        /// <see langword="decimal"/>,
        /// <see langword="string"/>,
        /// </remarks>
        /// <param name="id">The cue ID to search for.</param>
        /// <param name="cue">The returned cue view model if it was found.</param>
        /// <returns><see langword="true"/> if the cue was found.</returns>
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
                    return cuesDict.TryGetValue(decimal.Parse(idString, numberFormat), out cue);
                default:
                    cue = null;
                    Log($"Couldn't find cue with ID: {id}!", LogLevel.Warning);
                    return false;
            }
        }

        /// <summary>
        /// Tries to find a cue view model given a cue ID.
        /// </summary>
        /// <param name="id">The cue ID to search for.</param>
        /// <param name="cue">The returned cue view model if it was found.</param>
        /// <returns><see langword="true"/> if the cue was found.</returns>
        public bool FindCue(decimal id, [NotNullWhen(true)] out CueViewModel? cue)
        {
            return cuesDict.TryGetValue(id, out cue);
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
        /// Converts a path to/from a project relative path. Only paths which are in subdirectories of the project path are made relative.
        /// </summary>
        /// <param name="path">the path to convert</param>
        /// <param name="expand">whether the path should be expanded to an absolute path or made relative to the project</param>
        /// <returns></returns>
        public string ResolvePath(string path, bool expand = true)
        {
            // Used by the "Pack Project" command to collect all used external files
            captureResolvedPaths?.Add(path);
            if (!expand && (packedPaths?.TryGetValue(path, out var res) ?? false))
                return res;

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
                try
                {
                    foreach (string file in Directory.EnumerateFiles(projDir, fileName, fileSearchEnumerationOptions))
                        if (Path.GetFileName(file) == fileName)
                            return file;
                }
                catch (Exception ex)
                {
                    Log($"Unexpected failure while attempting to resolve relative file path. {ex.Message}", LogLevel.Warning);
                }

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
        /// Checks if the project file has unsaved changes and prompts the user to save if needed.
        /// </summary>
        /// <returns>false if the user decided to cancel the current operation.</returns>
        public bool UnsavedChangedCheck(bool canCancel = true)
        {
            ProgressBoxViewModel.Message = "Checking for unsaved changes...";
            ProgressBoxViewModel.Progress = 0.1f;
            Dispatcher.Yield();

            using var ms = new MemoryStream();
            SerializeShowFile(ms).Wait();
            var curr = ms.ToArray();

            byte[]? prev = null;
            try
            {
                prev = string.IsNullOrEmpty(ProjectFilePath) ? null : File.ReadAllBytes(ProjectFilePath);
            }
            catch { }

            // Check it's not the default file
            if (curr != null && curr.SequenceEqual(defaultShowfile))
                return true;

            // Compare current file with last saved file.
            if (curr != null && prev != null && curr.SequenceEqual(prev))
                return true;

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
            return true;
        }

        private void LoadShowfileModel(ShowFile show)
        {
            ProgressBoxViewModel.Progress = 0.3f;
            ProgressBoxViewModel.Message = $"Initialising devices...";
            Dispatcher.Yield();

            // Stop all running cues...
            StopExecute();

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
            for (int i = 0; i < showFile.cues.Count; i++)
            {
                ProgressBoxViewModel.Progress = (i + 1) / (float)showFile.cues.Count;
                ProgressBoxViewModel.Message = $"Loading cues... ({i + 1}/{showFile.cues.Count})";
                Dispatcher.Yield();
                Cue c = showFile.cues[i];
                try
                {
                    var vm = CueViewModel.FromModel(c, this);
                    vm.Bind(c);
                    Cues.Add(vm);
                }
                catch (Exception ex)
                {
                    Log($"Error occurred while trying to create cue from save file! {ex.Message}\n{ex}", LogLevel.Error);
                }
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
            bool resync = false;
            Dictionary<decimal, Cue> cueModels = [];
            foreach (var cue in showFile.cues)
                if (!cueModels.TryAdd(cue.qid, cue))
                    resync = true;

            if (!resync)
            {
                foreach (CueViewModel vm in Cues)
                {
                    if (!cueModels.TryGetValue(vm.QID, out Cue? value))
                    {
                        Log($"Cue with id {vm.QID} exists in the editor but not in the internal model! Potential corruption detected!", LogLevel.Warning);
                        // TODO: If we want to be nice we could just create the model here...
                        resync = true;
                    }
                    else
                    {
                        var q = value;
                        vm.ToModel(q);
                    }
                }
            }
            if (resync)
            {
                Log($"Rebuilding internal cue database...", LogLevel.Info);
                showFile.cues.Clear();
                foreach (var vm in Cues)
                {
                    vm.UnBind();
                    var cue = Cue.CreateCue(vm.Type);
                    vm.Bind(cue);
                    vm.ToModel(cue);
                    showFile.cues.Add(cue);
                }
            }
            ProjectSettings.ToModel(showFile.showSettings);
            showFile.columnWidths = [.. ColumnWidths.Select(x => x.Value)];
            showFile.fileFormatVersion = ShowFile.FILE_FORMAT_VERSION;
        }

        public async Task OpenProject(string path)
        {
            Log($"Loading project from: {path}");
            try
            {
                syncContext.Send(_ =>
                {
                    ProgressBoxViewModel.Message = "Loading project... (Deserializing)";
                    ProgressBoxViewModel.Progress = 0.2f;
                    Dispatcher.Yield();
                }, null);

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
                    f.Position = 0;
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
                    persistantDataManager.AddRecentFile(path);
                    ProjectFilePath = path;
                    Dispatcher.Yield();
                    LoadShowfileModel(showFile);
                }, null);

                Log($"Loaded project from disk! {path}");
            }
            catch (Exception e)
            {
                Log($"Couldn't load project from disk. Trying to load {path} \n  failed with: {e}", LogLevel.Warning);
            }

            syncContext.Send(_ =>
            {
                ProgressBoxViewModel.Visible = Visibility.Collapsed;
            }, null);
        }

        private async Task SerializeShowFile(Stream stream)
        {
            await JsonSerializer.SerializeAsync(stream, showFile, jsonSerializerOptions);
        }

        public async Task SaveProjectAsync(string path, bool allowSynchronisation = true, bool syncModel = true)
        {
            try
            {
                Log("Saving project...");
                syncContext.Send(_ =>
                {
                    ProgressBoxViewModel.Message = "Saving project...";
                    ProgressBoxViewModel.Progress = 0.1f;
                    ProgressBoxViewModel.Visible = Visibility.Visible;
                }, null);
                // For now, this method can't be trusted on other threads, let run on the main thread.
                // Chances are this method is being called from the main thread anyway, so it shouldn't
                // make a difference.
                if (syncModel)
                {
                    syncContext.Send(_ =>
                    {
                        EnsureShowfileModelSync();
                    }, null);
                }

                syncContext.Send(_ =>
                {
                    ProgressBoxViewModel.Progress = 0.5f;
                }, null);

                using var f = File.OpenWrite(path);
                f.SetLength(0);
                using var ms = new MemoryStream();
                await SerializeShowFile(ms);
                ms.Position = 0;
                ms.CopyTo(f);

                if (ProjectSettings.EnableRemoteControl && ProjectSettings.SyncShowFileOnSave && allowSynchronisation)
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

            syncContext.Send(_ =>
            {
                ProgressBoxViewModel.Visible = Visibility.Collapsed;
            }, null);
        }

        public void SaveProject(string path)
        {
            try
            {
                Log("Saving project...");
                ProgressBoxViewModel.Message = "Saving project...";
                ProgressBoxViewModel.Progress = 0.1f;
                ProgressBoxViewModel.Visible = Visibility.Visible;

                EnsureShowfileModelSync();

                ProgressBoxViewModel.Progress = 0.5f;

                using var f = File.OpenWrite(path);
                f.SetLength(0);
                using var ms = new MemoryStream();
                SerializeShowFile(ms).Wait();
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
            ProgressBoxViewModel.Visible = Visibility.Hidden;
        }

        /// <summary>
        /// Saves the project and copies all of it's referenced files to the given directory.
        /// </summary>
        /// <param name="path">The directory to pack the project into.</param>
        public void PackProject(string path)
        {
            try
            {
                Log("Packing project...");
                syncContext.Send(_ =>
                {
                    ProgressBoxViewModel.Message = "Packing project...";
                    ProgressBoxViewModel.Progress = 0.1f;
                    ProgressBoxViewModel.Visible = Visibility.Visible;
                }, null);

                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                string projPath = Path.Combine(path, $"{Path.GetFileName(path)}.qproj");

                // Saving the project should result in all paths being re-resolved as the ViewModel->Model sync occurs
                // While this happens, we capture a list of each path being resolved so we can pack them later.
                captureResolvedPaths = [];
                //SaveProject(path);
                syncContext.Send(_ =>
                {
                    EnsureShowfileModelSync();
                }, null);

                string mediaDir = Path.Combine(path, "Media");
                Directory.CreateDirectory(mediaDir);
                packedPaths = [];

                var pathsDistinct = captureResolvedPaths.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
                captureResolvedPaths = null;
                Dictionary<string, List<(string expanded, string captured)>> expandedPaths = [];
                foreach (var capturedPath in pathsDistinct)
                {
                    var expanded = ResolvePath(capturedPath, true);
                    var fileName = Path.GetFileName(expanded);
                    if (!expandedPaths.TryGetValue(fileName, out var expandedList))
                    {
                        expandedList = [(expanded, capturedPath)];
                        expandedPaths.Add(fileName, expandedList);
                    }
                    else
                    {
                        expandedList.Add((expanded, capturedPath));
                    }
                }

                Log("Copying media...");
                int nCaptured = pathsDistinct.Length;
                var sep = Path.DirectorySeparatorChar;
                foreach (var (fileName, expandedList) in expandedPaths)
                {
                    if (expandedList.Count > 1)
                    {
                        // Remove the common sub-path for files with the same name (but in different directories)
                        var first = expandedList[0].expanded.AsSpan();
                        int trim;
                        for (trim = 0; trim < first.Length;)
                        {
                            int nextEnd = first[trim..].IndexOf(sep);
                            if (nextEnd == -1)
                                goto SubPathFound;
                            nextEnd += trim;
                            var compare = first[trim..nextEnd];
                            for (int i = 1; i < expandedList.Count; i++)
                            {
                                if (!expandedList[i].expanded.AsSpan()[trim..nextEnd].SequenceEqual(compare))
                                    goto SubPathFound;
                            }
                            trim = nextEnd + 1;
                        }
                    SubPathFound:
                        foreach (var expanded in expandedList.DistinctBy(x => x.captured))
                        {
                            var dst = Path.Combine(mediaDir, expanded.expanded[trim..]);
                            Log($"  copying {expanded.expanded}...", LogLevel.Debug);
                            syncContext.Post(_ =>
                            {
                                ProgressBoxViewModel.Message = $"Copying media... ({packedPaths.Count + 1}/{nCaptured})";
                                ProgressBoxViewModel.Progress = packedPaths.Count / (float)nCaptured;
                                ProgressBoxViewModel.Visible = Visibility.Visible;
                            }, null);
                            File.Copy(expanded.expanded, dst, true);

                            // Store the new path in a lookup
                            packedPaths.TryAdd(expanded.captured, Path.GetRelativePath(path, dst));
                        }
                    }
                    else
                    {
                        // Just a single file with this name, copy it to the root
                        var dst = Path.Combine(mediaDir, fileName);
                        Log($"  copying {fileName}...", LogLevel.Debug);
                        syncContext.Post(_ =>
                        {
                            ProgressBoxViewModel.Message = $"Copying media... ({packedPaths.Count + 1}/{nCaptured})";
                            ProgressBoxViewModel.Progress = packedPaths.Count / (float)nCaptured;
                            ProgressBoxViewModel.Visible = Visibility.Visible;
                        }, null);
                        File.Copy(expandedList[0].expanded, dst, true);

                        // Store the new path in a lookup
                        packedPaths.TryAdd(expandedList[0].captured, Path.GetRelativePath(path, dst));
                    }
                }

                syncContext.Send(_ =>
                {
                    ProgressBoxViewModel.Message = $"Saving packed project...";
                    ProgressBoxViewModel.Progress = 1;
                    ProgressBoxViewModel.Visible = Visibility.Visible;
                }, null);

                // Resave the project, applying all the modified paths, this will be
                // done automatically by the path resolver using the packedPaths dict
                // we just made.
                SaveProject(projPath);

                Log($"Successfully packed {packedPaths.Count} media files into '{path}'");
            }
            catch (Exception e)
            {
                Log($"Couldn't pack project to disk. Trying to pack into {path} \n  failed with: {e}", LogLevel.Warning);
            }

            // Reset these when not packing a project
            captureResolvedPaths = null;
            packedPaths = null;
            syncContext.Send(_ =>
            {
                ProgressBoxViewModel.Visible = Visibility.Collapsed;
                ProgressBoxViewModel.Message = string.Empty;
                ProgressBoxViewModel.Progress = 0;
            }, null);
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

            var model = Cue.CreateCue(type);

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
