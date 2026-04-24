using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.SourceGenerator;
using QPlayer.Utilities;
using QPlayer.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace QPlayer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    #region Bindable Properties
    [Reactive, TemplateProp(nameof(SelectedCueInd_Template))]
    private int selectedCueInd;
    private int SelectedCueInd_Template
    {
        get => selectedCueInd;
        set
        {
            var prev = selectedCueInd;
            HandleSelection(prev, ref value);

            selectedCueInd = value;
            foreach (var cue in cues)
                NotifyCueSelectionChanged(cue);
            /*if (prev >= 0 && prev < cues.Count)
                cues[prev].OnSelectionChanged();
            if (value >= 0 && value < cues.Count)
                cues[value].OnSelectionChanged();*/

            if (prev != value)
                OnPropertyChanged(nameof(SelectedCue));
        }
    }
    [Reactive("SelectedCue")]
    private CueViewModel? SelectedCue_Template
    {
        get => SelectedCueInd >= 0 && SelectedCueInd < Cues.Count ? Cues[SelectedCueInd] : null;
        set => SelectedCueInd = FindCueIndex(value);
    }
    [Reactive] private readonly ObservableSelectionSet<CueViewModel> multiSelection;
    [Reactive] private SelectionMode selectionMode = SelectionMode.Normal;
    [Reactive] private readonly ObservableCollection<CueViewModel> cues;
    [Reactive] private readonly ObservableCollection<CueViewModel> activeCues;
    [Reactive] private readonly ObservableCollection<ObservableStruct<float>> columnWidths;
    [Reactive] private readonly ObservableCollection<CueViewModel> draggingCues;
    [Reactive] private bool enableAutosave = true;
    [Reactive("ShowMode"), ChangesProp(nameof(EditMode))]
    private bool ShowMode_Template
    {
        get => showMode;
        set
        {
            showMode = value;
            ShowModeChanged?.Invoke();
        }
    }
    private bool showMode = false;

    [Reactive, Readonly] private ProjectSettingsViewModel projectSettings;

    [Reactive] private TimeSpan preloadTime;

    [Reactive] private readonly EditModeCommand newProjectCommand;
    [Reactive] private readonly EditModeCommand openProjectCommand;
    [Reactive] private readonly EditModeCommand<string> openSpecificProjectCommand;
    [Reactive] private readonly RelayCommand saveProjectCommand;
    [Reactive] private readonly EditModeCommand saveProjectAsCommand;
    [Reactive] private readonly EditModeCommand packProjectCommand;
    [Reactive] private readonly RelayCommand openLogCommand;
    [Reactive] private readonly RelayCommand openRemoteNodesCommand;
    [Reactive] private readonly RelayCommand openSetttingsCommand;
    [Reactive] private readonly EditModeCommand openOnlineManualCommand;
    [Reactive] private readonly RelayCommand openAboutCommand;
    [Reactive] private readonly RelayCommand openPluginManagerCommand;

    [Reactive] private readonly EditModeCommand createCueMenuCommand;

    [Reactive] private readonly EditModeCommand<string> createCueCommand;

    [Reactive] private readonly EditModeCommand undoCommand;
    [Reactive] private readonly EditModeCommand redoCommand;

    [Reactive] private readonly EditModeCommand moveCueUpCommand;
    [Reactive] private readonly EditModeCommand moveCueDownCommand;
    [Reactive] private readonly EditModeCommand selUpCommand;
    [Reactive] private readonly EditModeCommand selDownCommand;
    [Reactive] private readonly EditModeCommand deleteCueCommand;
    [Reactive] private readonly EditModeCommand duplicateCueCommand;

    [Reactive] private readonly RelayCommand goCommand;
    [Reactive] private readonly RelayCommand pauseCommand;
    [Reactive] private readonly RelayCommand unpauseCommand;
    [Reactive] private readonly RelayCommand stopCommand;
    [Reactive] private readonly RelayCommand upCommand;
    [Reactive] private readonly RelayCommand downCommand;
    [Reactive] private readonly RelayCommand preloadCommand;

    [Reactive] private string? projectFilePath;

    [Reactive] private string statusText = "Ready";
    [Reactive] private SolidColorBrush statusTextColour = StatusInfoBrush;

    [Reactive, Readonly] private readonly ProgressBoxViewModel progressBoxViewModel;
    [Reactive, Readonly] private readonly AudioMeterViewModel mainAudioMeter;

    public bool IsAudioActive => audioPlaybackManager.IsDeviceActive;
    public bool EditMode => !ShowMode;

    public static ObservableCollection<string> LogList
    {
        get { return logList; }
        private set
        {
            logList = value;
            BindingOperations.EnableCollectionSynchronization(logList, logListLock);
        }
    }
    public AudioBufferDispatcherViewModel AudioBufferDispatcherDebug { get; private set; }

    public string WindowTitle => $"QPlayer – {ProjectSettings.Title}";
    public string VersionString
    {
        get
        {
            var assembly = Assembly.GetAssembly(typeof(MainViewModel));
            if (assembly == null)
                return string.Empty;
            if (string.IsNullOrEmpty(assembly.Location))
                return $"Version {assembly.GetName().Version}";
            var versionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            return $"Version {versionInfo.ProductVersion}";
        }
    }
    public string CopyrightString
    {
        get
        {
            var assembly = Assembly.GetAssembly(typeof(MainViewModel));
            if (assembly == null)
                return string.Empty;
            if (string.IsNullOrEmpty(assembly.Location))
                return $"Copyright Thomas Mathieson";
            var versionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            return $"Copyright {versionInfo.LegalCopyright}";
        }
    }
    public string Clock => $"{DateTime.Now:HH:mm:ss}";
    public ObservableCollection<RecentFile> RecentFiles => persistantDataManager.RecentFiles;
    public string UndoActionName => UndoManager.UndoActionName;
    public string RedoActionName => UndoManager.RedoActionName;
    internal event Action? OnRegisterCueTypes;
    internal event Action<Vector2>? OnScrollCueList;
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
    public event Action? ShowModeChanged;

    private ShowFile showFile;
    private List<string>? captureResolvedPaths;
    private Dictionary<string, string>? packedPaths;
    private int autoBackInd = 0;
    private CueViewModel? prevPrimarySelectedCue;
    private static ObservableCollection<string> logList = [];
    private static readonly List<Window> openWindows = [];
    private static readonly object logListLock = new();
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly DispatcherTimer fastUpdateTimer;
    private readonly DispatcherTimer slowUpdateTimer;
    private readonly DispatcherTimer autosaveTimer;
    private readonly AudioPlaybackManager audioPlaybackManager;
    private readonly Dispatcher dispatcher;
    private readonly MultiDict<decimal, CueViewModel> cuesDict;
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
    private byte[] defaultShowfile = []; // So that the unsaved changes check works correctly, we compare against the default showfile

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
        Log("  Copyright Thomas Mathieson 2026");

        CueFactory.RegisterAssembly(Assembly.GetExecutingAssembly());
        Log("Loading plugins...");
        PluginLoader.LoadPlugins(this);
        Log($"{PluginLoader.LoadedPlugins.Count} plugins loaded");

        UndoManager.RegisterMainVM(this);
        UndoManager.UndoStackChanged += () =>
        {
            OnPropertyChanged(nameof(UndoActionName));
            OnPropertyChanged(nameof(RedoActionName));
        };

        OnRegisterCueTypes?.Invoke();

        // Configure the json serializer, make sure that the polymorphic type resolver is resolved after the plugins (and cue types) have been loaded.
        jsonSerializerOptions = new()
        {
            IncludeFields = true,
            AllowTrailingCommas = true,
            WriteIndented = true,
            TypeInfoResolver = new PolymorphicTypeResolver()
        };

        //foreach (FontFamily fontFamily in Fonts.GetFontFamilies(new Uri("pack://application:,,,/"), "./Resources/"))
        //    Log($"Found embedded font: {fontFamily.Source}", LogLevel.Debug);

        dispatcher = Dispatcher.CurrentDispatcher;
        audioPlaybackManager = new(this);
        oscManager = new(this);
        mscManager = new(this);
        persistantDataManager = new();
        progressBoxViewModel = new();
        mainAudioMeter = new(dispatcher);

        // Bind commands
        openLogCommand = new(() => OpenWindow<LogWindow>());
        openRemoteNodesCommand = new(() => OpenWindow<RemoteNodesWindow>());
        newProjectCommand = new(NewProjectExecute, this);
        openProjectCommand = new(OpenProjectExecute, this);
        openSpecificProjectCommand = new(OpenSpecificProjectExecute, this);
        saveProjectCommand = new(() => SaveProjectExecute());
        saveProjectAsCommand = new(() => SaveProjectAsExecute(), this);
        packProjectCommand = new(PackProjectExecute, this);
        openOnlineManualCommand = new(OpenOnlineManualExecute, this);
        openAboutCommand = new(() => OpenWindow<AboutWindow>());
        openSetttingsCommand = new(() => OpenWindow<SettingsWindow>());
        openPluginManagerCommand = new(() => OpenWindow<PluginManagerWindow>());

        createCueMenuCommand = new(ShowCreateCueMenuExecute, this);
        createCueCommand = new(type => CreateCue(type), this);

        undoCommand = new(UndoManager.Undo, this);
        redoCommand = new(UndoManager.Redo, this);

        moveCueUpCommand = new(MoveCueUpExecute, this);
        moveCueDownCommand = new(MoveCueDownExecute, this);
        selUpCommand = new(() => MultiSelect(SelectedCueInd - 1, SelectionMode.Add), this);
        selDownCommand = new(() => MultiSelect(SelectedCueInd + 1, SelectionMode.Add), this);
        deleteCueCommand = new(DeleteCueExecute, this);
        duplicateCueCommand = new(DuplicateCueExecute, this);

        goCommand = new(GoExecute);
        pauseCommand = new(Pause);
        unpauseCommand = new(Unpause);
        stopCommand = new(StopExecute);
        upCommand = new(() => SelectedCueInd--);
        downCommand = new(() => SelectedCueInd++);
        preloadCommand = new(PreloadExecute);

        fastUpdateTimer = new(TimeSpan.FromMilliseconds(40), DispatcherPriority.Background, FastUpdate, Dispatcher.CurrentDispatcher);
        fastUpdateTimer.Start();

        slowUpdateTimer = new(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, SlowUpdate, Dispatcher.CurrentDispatcher);
        slowUpdateTimer.Start();

        autosaveTimer = new(TimeSpan.FromMinutes(5), DispatcherPriority.Background, AutoSave, Dispatcher.CurrentDispatcher);
        autosaveTimer.Start();

        columnWidths = new(Enumerable.Repeat<float>(60, 32).Select(x =>
        {
            var observable = new ObservableStruct<float>(x);
            // Bubble the change up
            observable.PropertyChanged += (o, e) =>
            {
                OnPropertyChanged(nameof(ColumnWidths));
            };
            return observable;
        }));
        SetDefaultColumnWidths();
        projectFilePath = null;
        activeCues = [];
        cues = [];
        draggingCues = [];
        cuesDict = [];
        multiSelection = [];
        Cues.CollectionChanged += SyncCueDict;
        ProjectSettings = new(this);
        ProjectSettings.PropertyChanged += ProjectSettings_PropertyChanged;

        audioPlaybackManager.OnMixerMeter += MainAudioMeter.ProcessSample;
        audioPlaybackManager.DeviceStateChanged += (val) => OnPropertyChanged(nameof(IsAudioActive));

        showFile = new();
        LoadShowfileModel(showFile, true).Wait();
        CreateCue(nameof(SoundCue));

        // Wait before doing this so that the audio driver has a chance to be discovered. This is a hack.
        Task.Delay(2000).ContinueWith(async _ =>
        {
            using var ms = new MemoryStream();
            await SerializeShowFile(ms);
            defaultShowfile = ms.ToArray();
        });

        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
        {
            if (File.Exists(args[1]))
            {
                // Needs to happen once the dispatcher has woken up.
                dispatcher.Invoke(() => OpenSpecificProjectExecute(args[1]));
            }
        }

        AudioBufferDispatcherDebug = new();
    }

    public bool OnExit()
    {
        if (!RunningCuesCheck())
            return false;

        if (!UnsavedChangedCheck(true, true).Result)
            return false;

        Log("Shutting down...");
        PluginLoader.OnUnload();
        CloseAllWindows();
        slowUpdateTimer.Stop();
        fastUpdateTimer.Stop();
        autosaveTimer.Stop();
        audioPlaybackManager.Stop();
        audioPlaybackManager.Dispose();
        Log("Goodbye!");
        return true;
    }

    private void FastUpdate(object? sender, EventArgs e)
    {
        try
        {
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
    }

    private void SlowUpdate(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Clock));
        OnPropertyChanged(nameof(IsAudioActive));

        OnSlowUpdate?.Invoke();
        PluginLoader.OnSlowUpdate();

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

        // GC.Collect(0, GCCollectionMode.Forced, false, false);
    }

    private void AutoSave(object? sender, EventArgs e)
    {
        if (EnableAutosave && EditMode)
        {
            string path = Path.Combine(persistantDataManager.AutoBackDir, $"autoback{autoBackInd + 1}.qproj");
            autoBackInd = (autoBackInd + 1) % 5;
            Task.Run(() =>
                SaveProjectAsync(path, false, false).ContinueWith((_) => Log($"Autosaved project to '{path}'.")));
        }
    }

    #region Commands
    /// <summary>
    /// Returns true if a non show mode command can execute.
    /// </summary>
    /// <returns></returns>
    public bool IsEditMode() => EditMode;

    public void NewProjectExecute()
    {
        Log("Creating new project...");
        dispatcher.Invoke(async () =>
        {
            if (!RunningCuesCheck())
                return;
            if (!await UnsavedChangedCheck())
                return;

            ProjectFilePath = null;
            await LoadShowfileModel(new());
        });
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
        if (!RunningCuesCheck())
            return;
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
            Task.Run(async () =>
            {
                await PackProject(Path.ChangeExtension(saveFileDialog.FileName, null));
            });
        }
    }

    public void OpenProjectExecute()
    {
        Log("Opening project...");
        dispatcher.Invoke(async () =>
        {
            ProgressBoxViewModel.Message = "Opening project...";
            ProgressBoxViewModel.Progress = 0.0f;
            ProgressBoxViewModel.Visible = Visibility.Visible;
            await Dispatcher.Yield();
            if (!RunningCuesCheck())
                return;
            if (!await UnsavedChangedCheck())
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
                await Dispatcher.Yield();
                await OpenProject(openFileDialog.FileName);
            }
            else
            {
                ProgressBoxViewModel.Visible = Visibility.Collapsed;
            }
        });
    }

    public void OpenSpecificProjectExecute(string? path)
    {
        Log("Opening project...");
        dispatcher.Invoke(async () =>
        {
            ProgressBoxViewModel.Message = "Opening project...";
            ProgressBoxViewModel.Progress = 0.0f;
            ProgressBoxViewModel.Visible = Visibility.Visible;
            await Dispatcher.Yield();

            if (!RunningCuesCheck())
            {
                ProgressBoxViewModel.Visible = Visibility.Collapsed;
                return;
            }
            if (!await UnsavedChangedCheck())
            {
                ProgressBoxViewModel.Visible = Visibility.Collapsed;
                return;
            }
            if (!string.IsNullOrEmpty(path))
            {
                await Dispatcher.Yield();
                await OpenProject(path);
            }
            else
            {
                ProgressBoxViewModel.Visible = Visibility.Collapsed;
            }
        });
    }

    /// <summary>
    /// Opens or activates a window of the given type.
    /// </summary>
    /// <typeparam name="T">The type of the window to open.</typeparam>
    /// <param name="singleton">When <see langword="true"/>, activates an existing window of the type if it already exists.</param>
    /// <returns>The opened/activated window.</returns>
    public T? OpenWindow<T>(bool singleton = true)
        where T : Window
    {
        if (singleton && openWindows.FirstOrDefault(x => x is T) is Window openWindow)
        {
            openWindow.Activate();
            return (T)openWindow;
        }

        try
        {
            var window = (T)Activator.CreateInstance(typeof(T), this)!;
            window.Closed += (o, e) => openWindows.Remove((Window)o!);
            window.Show();
            openWindows.Add(window);
            return window;
        }
        catch (Exception ex)
        {
            Log($"Couldn't open window: {ex}", LogLevel.Error);
        }
        return null;
    }

    /// <summary>
    /// Closes a window of the given type.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns><see langword="true"/> if a window of the given type was closed.</returns>
    public bool CloseWindow<T>()
    {
        if (openWindows.Find(x => x is T) is Window openWindow)
        {
            openWindows.Remove(openWindow);
            openWindow.Close();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Closes all active windows, excluding the main window.
    /// </summary>
    public void CloseAllWindows()
    {
        var toClose = openWindows.ToArray();
        foreach (var wnd in toClose)
            wnd.Close();
        openWindows.Clear();
    }

    internal void OpenOnlineManualExecute()
    {
        Process.Start(new ProcessStartInfo("https://space928.github.io/QPlayer/reference/") { UseShellExecute = true });
    }

    /// <summary>
    /// Plays the currently selected cue (and advances the selection). Or, if multiple 
    /// cues are selected, plays the selectd cues.
    /// </summary>
    public void GoExecute()
    {
        if (multiSelection.Count <= 1)
            Go(SelectedCue);
        else
            foreach (var cue in multiSelection)
                cue.DelayedGo();
    }

    /// <summary>
    /// Stops all cues. If no cues are playing and multiple cues are selected, clears the multi-selection.
    /// </summary>
    public void StopExecute()
    {
        if (activeCues.Count == 0)
        {
            if (SelectedCue != null)
                multiSelection.Replace(SelectedCue);
            else
                multiSelection.Clear();

            foreach (var cue in cues)
                NotifyCueSelectionChanged(cue);
        }
        Stop();
    }

    public void ScrollCueList(Vector2 delta)
    {
        OnScrollCueList?.Invoke(delta);
    }

    public void PreloadExecute()
    {
        SelectedCue?.Preload(PreloadTime);
    }

    /// <summary>
    /// Moves the selected cues up by one position in the cue stack.
    /// </summary>
    public void MoveCueUpExecute()
    {
        switch (multiSelection.Count)
        {
            case 0: return;
            case 1: MoveCue(SelectedCue!, false); return;
            default: MoveSelectedCues(false); return;
        }
    }

    /// <summary>
    /// Moves the selected cues down by one position in the cue stack.
    /// </summary>
    public void MoveCueDownExecute()
    {
        switch (multiSelection.Count)
        {
            case 0: return;
            case 1: MoveCue(SelectedCue!, true); return;
            default: MoveSelectedCues(true); return;
        }
    }

    /// <summary>
    /// Deletes the selected cues in the cue stack.
    /// </summary>
    public void DeleteCueExecute()
    {
        switch (multiSelection.Count)
        {
            case 0: return;
            case 1: DeleteCue(SelectedCueInd); break;
            default: DeleteCues([.. multiSelection.Select(x => FindCueIndex(x))]); break;
        }
        NotifyCueSelectionChanged(SelectedCue);
    }

    /// <summary>
    /// Duplicates the selected cues in the cue stack.
    /// </summary>
    public void DuplicateCueExecute()
    {
        switch (multiSelection.Count)
        {
            case 0: return;
            case 1: DuplicateCue(null, true); return;
            default: DuplicateCues(multiSelection, true); return;
        }
    }

    internal void ShowCreateCueMenuExecute()
    {
        ContextMenu menu = new();
        menu.IsOpen = true;
        foreach (var qtypeObj in CueFactory.RegisteredCueTypes)
        {
            menu.Items.Add(new MenuItem()
            {
                Header = $"Create {qtypeObj.displayName}",
                Command = new RelayCommand(() => CreateCue(qtypeObj.name))
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
    internal void NotifyQIDChanged(decimal oldVal, decimal newVal, CueViewModel src)
    {
        if (!cuesDict.UpdateKey(oldVal, newVal, src))
            cuesDict.Add(newVal, src);
    }

    public void OpenAudioDevice()
    {
        AudioPlaybackManager.OpenOutputDevice(
            ProjectSettings.AudioOutputDriver,
            ProjectSettings.SelectedAudioOutputDeviceKey,
            ProjectSettings.AudioLatency,
            ProjectSettings.ExclusiveMode,
            ProjectSettings.ChannelOffset);
    }

    private void ProjectSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectSettingsViewModel.Title))
            OnPropertyChanged(nameof(WindowTitle));
    }

    private void SetDefaultColumnWidths()
    {
        int[] widths = [
            50,
            60,
            38,
            68,
            250,
            54,
            48,
            58,
            58,
            72
        ];

        if (columnWidths.Count < widths.Length)
            return;

        for (int i = 0; i < widths.Length; i++)
            columnWidths[i].Value = widths[i];
    }
}
