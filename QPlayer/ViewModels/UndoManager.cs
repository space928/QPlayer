using QPlayer.Utilities;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.ViewModels;

/// <summary>
/// The global undo manager, this provides methods to record actions in a way that can be undone or redone. This class is not 
/// thread-safe and must be called from the main thread.
/// </summary>
public static class UndoManager
{
    public const int MAX_HISTORY = 50;
    private static readonly Deque<UndoAction> undoStack = [];
    private static readonly Deque<UndoAction> redoStack = [];
    private static readonly StringDict<SetPropDelegate> cachedSetters = [];
    private static MainViewModel? mainViewModel;
    private static int suppressRecordingCounter = 0;

    private delegate void SetPropDelegate(object target, object? value);

    public static event Action? UndoStackChanged;

    public static bool CanUndo => undoStack.Count > 0;
    public static bool CanRedo => redoStack.Count > 0;

    public static string UndoActionName
    {
        get
        {
            if (undoStack.TryPeekEnd(out var item))
                return "Undo " + item.ToString();
            return "Undo (none)";
        }
    }

    public static string RedoActionName
    {
        get
        {
            if (redoStack.TryPeekEnd(out var item))
                return "Redo " + item.ToString();
            return "Redo (none)";
        }
    }

    public static int UndoHistoryCount => undoStack.Count;

    public static bool IsRecordingSuppressed => suppressRecordingCounter > 0;

    internal static void RegisterMainVM(MainViewModel vm)
    {
        mainViewModel = vm;
    }

    /*
    private bool Example
    {
        get => field;
        set
        {
            var prev = field;
            field = value;
            RegisterAction(nameof(Example), this, prev, value);
        }
    }
    */

    /// <summary>
    /// Registers a property change action to the undo stack.
    /// </summary>
    /// <param name="path">The path to the property being changed.</param>
    /// <param name="target">The object being changed.</param>
    /// <param name="oldValue">The previous value.</param>
    /// <param name="newValue">The new value after this action has occured.</param>
    public static void RegisterAction<T>(string path, object target, T? oldValue, T? newValue)
    {
        if (IsRecordingSuppressed)
            return;

        // Try to merge this action with the last action
        ref var top = ref undoStack.MutablePeekEnd();
        if (!Unsafe.IsNullRef(ref top))
        {
            if (top.target == target && top.path == path)
            {
                top.newValue = newValue;
                return;
            }
        }

        redoStack.Clear();
        undoStack.PushEnd(new(path, target, oldValue, newValue));
        if (undoStack.Count > MAX_HISTORY)
            undoStack.PopStart();

        UndoStackChanged?.Invoke();
        // MainViewModel.Log($"[UNDO] Registered {undoStack.MutablePeekEnd()}");
    }

    /// <summary>
    /// Registers an action to the undo stack.
    /// </summary>
    /// <param name="actionDesc">A brief description of the action (in the past tense).</param>
    /// <param name="undoFunc">A function to call to undo the action.</param>
    /// <param name="redoFunc">A function to call to redo the action.</param>
    public static void RegisterAction(string actionDesc, Action undoFunc, Action redoFunc)
    {
        if (IsRecordingSuppressed)
            return;

        redoStack.Clear();
        undoStack.PushEnd(new(actionDesc, undoFunc, redoFunc));
        if (undoStack.Count > MAX_HISTORY)
            undoStack.PopStart();

        UndoStackChanged?.Invoke();
        // MainViewModel.Log($"[UNDO] Registered {undoStack.MutablePeekEnd()}");
    }

    /// <summary>
    /// Undoes the last action registered to the undo stack.
    /// </summary>
    public static void Undo()
    {
        if (!undoStack.TryPopEnd(out var action))
            return;

        redoStack.PushEnd(action);
        if (redoStack.Count > MAX_HISTORY)
            redoStack.PopStart();
        UndoStackChanged?.Invoke();

        // If the undo action was captured in a closure, use that
        if (action.undoAction != null)
        {
            try
            {
                SuppressRecording();
                action.undoAction();
            }
            catch (Exception ex)
            {
                MainViewModel.Log($"Couldn't undo action {action}: {ex}", MainViewModel.LogLevel.Error);
            }
            UnSuppressRecording();
            return;
        }

        // Otherwise, it's a property change and we need to find the relevant setter
        var target = action.target;
        if (target == null)
            return;

        // Use a cached setter if we have one
        if (!cachedSetters.TryGetValue(action.path, out var setProp))
        {
            // Generate a new setter
            setProp = GenerateSetter(target, action.path);
            if (setProp == null)
                return;
            cachedSetters.Add(action.path, setProp);
        }

        SuppressRecording();
        try
        {
            setProp(target, action.oldValue);
        }
        catch (Exception ex)
        {
            MainViewModel.Log($"Couldn't undo action {action}: {ex}", MainViewModel.LogLevel.Error);
        }
        UnSuppressRecording();
    }

    /// <summary>
    /// Redoes the last action undone by the undo manager.
    /// </summary>
    public static void Redo()
    {
        if (!redoStack.TryPopEnd(out var action))
            return;

        undoStack.PushEnd(action);
        if (undoStack.Count > MAX_HISTORY)
            undoStack.PopStart();
        UndoStackChanged?.Invoke();

        // If the undo action was captured in a closure, use that
        if (action.redoAction != null)
        {
            SuppressRecording();
            try
            {
                action.redoAction();
            }
            catch (Exception ex)
            {
                MainViewModel.Log($"Couldn't redo action {action}: {ex}", MainViewModel.LogLevel.Error);
            }
            UnSuppressRecording();
            return;
        }

        // Otherwise, it's a property change and we need to find the relevant setter
        var target = action.target;
        if (target == null)
            return;

        // Use a cached setter if we have one
        if (!cachedSetters.TryGetValue(action.path, out var setProp))
        {
            // Generate a new setter
            setProp = GenerateSetter(target, action.path);
            if (setProp == null)
                return;
            cachedSetters.Add(action.path, setProp);
        }

        SuppressRecording();
        try
        {
            setProp(target, action.newValue);
        }
        catch (Exception ex)
        {
            MainViewModel.Log($"Couldn't redo action {action}: {ex}", MainViewModel.LogLevel.Error);
        }
        UnSuppressRecording();
    }

    /// <summary>
    /// Clears the undo and redo history. Necessary after drastic changes which would invalidate the undo history, 
    /// this also allows the GC to reclaim objects being kept alive by the undo history.
    /// </summary>
    public static void ClearUndoStack()
    {
        undoStack.Clear();
        redoStack.Clear();
        UndoStackChanged?.Invoke();
    }

    /// <summary>
    /// When used with a <see langword="using"/> block, suppresses undo action recording for the scope of this function.
    /// </summary>
    /// <returns></returns>
    public static ScopedSuppressRecording ScopedSuppress() => new();

    /// <summary>
    /// Temporarily suppresses the recording of new undo actions until <see cref="UnSupressRecording"/> is called.
    /// </summary>
    public static void SuppressRecording()
    {
        suppressRecordingCounter++;
    }

    /// <summary>
    /// Stops suppressing undo recording. Counterpart of <see cref="SuppressRecording"/>.
    /// </summary>
    public static void UnSuppressRecording()
    {
        suppressRecordingCounter--;
    }

    /// <summary>
    /// Forcefully stops suppressing undo recording. Counterpart of <see cref="SuppressRecording"/>.
    /// </summary>
    public static void ResetSuppressRecording()
    {
        suppressRecordingCounter = 0; 
    }

    private static SetPropDelegate? GenerateSetter(object targetTemplate, string name)
    {
        var type = targetTemplate.GetType();
        PropertyInfo? prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (prop == null)
            return null;

        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        var setter = Expression.Assign(
            Expression.Property(Expression.Convert(target, type), prop), 
            Expression.Convert(value, prop.PropertyType));

        return Expression.Lambda<SetPropDelegate>(Expression.Block(setter), target, value).Compile();
    }

    public readonly struct ScopedSuppressRecording : IDisposable
    {
        public ScopedSuppressRecording()
        {
            SuppressRecording();
        }

        public void Dispose()
        {
            UnSuppressRecording();
        }
    }

    private struct UndoAction
    {
        public object? target; // Should this be a WeakRef?
        public string path;

        public object? oldValue;
        public object? newValue;

        public Action? undoAction;
        public Action? redoAction;

        public UndoAction(string path, object target, object? oldValue, object? newValue)
        {
            this.path = path;
            this.target = target;
            this.oldValue = oldValue;
            this.newValue = newValue;
        }

        public UndoAction(string path, Action? undoAction, Action? redoAction)
        {
            this.path = path;
            this.undoAction = undoAction;
            this.redoAction = redoAction;
        }

        public override readonly string ToString()
        {
            return target switch
            {
                CueViewModel cue => $"Changed Q{cue.QID} / {path}",
                EQViewModel => $"Changed EQ / {path}",
                AudioLimiterViewModel => $"Changed Limiter / {path}",
                ProjectSettingsViewModel => $"Changed Project Settings / {path}",
                object obj => $"Change {TargetName(obj)} / {path}",
                _ => path
            };
        }

        private static string TargetName(object obj)
        {
            var s = obj.ToString() ?? "none";
            int dotInd = s.IndexOf('.');
            if (dotInd != -1 && dotInd != s.Length - 1)
                s = s[(dotInd + 1)..];
            if (s.EndsWith("ViewModel"))
                s = s[..^"ViewModel".Length];
            return s;
        }
    }
}
