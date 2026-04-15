using QPlayer.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace QPlayer.ViewModels;

/*
 All the file management methods in the MainViewModel.
 */

public partial class MainViewModel
{
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
    /// <remarks>
    /// Must be called on the dispatcher thread.
    /// </remarks>
    /// <returns>false if the user decided to cancel the current operation.</returns>
    public async Task<bool> UnsavedChangedCheck(bool canCancel = true, bool sync = false)
    {
        ProgressBoxViewModel.Message = "Checking for unsaved changes...";
        ProgressBoxViewModel.Progress = 0.1f;
        if (!sync)
            await Dispatcher.Yield();

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

    /// <summary>
    /// Checks if any cues are currently running, and if so prompts the user if they want to cancel the next operation.
    /// </summary>
    /// <param name="canCancel"></param>
    /// <returns>false if the user decided to cancel the current operation.</returns>
    public bool RunningCuesCheck(bool canCancel = true)
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
        return true;
    }

    /// <remarks>
    /// Must be called on the dispatcher thread.
    /// </remarks>
    /// <param name="sync">When true, executes without waiting for the dispatcher.</param>
    private async Task LoadShowfileModel(ShowFile show, bool sync = false)
    {
        UndoManager.ClearUndoStack();
        using var _ = UndoManager.ScopedSuppress();
        ProgressBoxViewModel.Progress = 0.3f;
        ProgressBoxViewModel.Message = $"Initialising devices...";
        if (!sync)
            await Dispatcher.Yield();

        // Stop all running cues...
        Stop();

        showFile = show;
        if (ProjectSettings != null)
        {
            ProjectSettings.Dispose();
            ProjectSettings.PropertyChanged -= ProjectSettings_PropertyChanged;
        }
        ProjectSettings = new ProjectSettingsViewModel(this);
        ProjectSettings.PropertyChanged += ProjectSettings_PropertyChanged;
        ProjectSettings.Bind(show.showSettings);
        ProjectSettings.SyncFromModel();

        for (int i = 0; i < Math.Min(showFile.columnWidths.Count, ColumnWidths.Count); i++)
            ColumnWidths[i].Value = showFile.columnWidths[i];

        Cues.Clear();
        for (int i = 0; i < showFile.cues.Count; i++)
        {
            ProgressBoxViewModel.Progress = (i + 1) / (float)showFile.cues.Count;
            ProgressBoxViewModel.Message = $"Loading cues... ({i + 1}/{showFile.cues.Count})";
            if (!sync)
                await Dispatcher.Yield();
            Cue c = showFile.cues[i];
            try
            {
                var vm = CueFactory.CreateViewModelForCue(c, this)
                    ?? throw new Exception($"Couldn't create view model for cue of type {c.GetType().Name}, qid: {c.qid}!");
                Cues.Add(vm);
            }
            catch (Exception ex)
            {
                Log($"Error occurred while trying to create cue from save file! {ex.Message}\n{ex}", LogLevel.Error);
            }
        }

        OnPropertyChanged(nameof(SelectedCue));
        oscManager.ConnectOSC();
        mscManager.ConnectMSC();
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

        using var _ = UndoManager.ScopedSuppress();

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
                    vm.Bind(q);
                    vm.SyncToModel();
                }
            }
        }
        if (resync)
        {
            Log($"Rebuilding internal cue database...", LogLevel.Info);
            showFile.cues.Clear();
            foreach (var vm in Cues)
            {
                vm.Bind(null);
                var cue = CueFactory.CreateCueForViewModel(vm);
                if (cue == null)
                {
                    Log($"Failed to create model for cue of type '{vm.GetType().Name}' (qid = {vm.QID}).", LogLevel.Warning);
                    continue;
                }
                showFile.cues.Add(cue);
            }
        }
        ProjectSettings.Bind(showFile.showSettings);
        ProjectSettings.SyncToModel();
        showFile.columnWidths = [.. ColumnWidths.Select(x => x.Value)];
        showFile.fileFormatVersion = ShowFile.FILE_FORMAT_VERSION;
    }

    /// <summary>
    /// Opens the project at the specified path.
    /// </summary>
    /// <remarks>
    /// Must be called on the dispatcher thread.
    /// </remarks>
    /// <param name="path"></param>
    /// <returns></returns>
    public async Task OpenProject(string path)
    {
        Log($"Loading project from: {path}");
        try
        {
            ProgressBoxViewModel.Message = "Loading project... (Deserializing)";
            ProgressBoxViewModel.Progress = 0.2f;
            await Dispatcher.Yield();

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

            persistantDataManager.AddRecentFile(path);
            ProjectFilePath = path;
            await LoadShowfileModel(showFile);

            Log($"Loaded project from disk! {path}");
        }
        catch (Exception e)
        {
            Log($"Couldn't load project from disk. Trying to load {path} \n  failed with: {e}", LogLevel.Warning);
        }

        ProgressBoxViewModel.Visible = Visibility.Collapsed;
    }

    private async Task SerializeShowFile(Stream stream)
    {
        await JsonSerializer.SerializeAsync(stream, showFile, jsonSerializerOptions);
    }

    /// <summary>
    /// Saves the show file asynchronously. This method can be called from a worker thread.
    /// </summary>
    /// <param name="path">The path to save the project to.</param>
    /// <param name="allowSynchronisation">Whether syncing the show file with remote clients is allowed.</param>
    /// <param name="syncModel">Whether the internal model should be resynchronised if needed.</param>
    /// <returns></returns>
    public async Task SaveProjectAsync(string path, bool allowSynchronisation = true, bool syncModel = true)
    {
        try
        {
            Log("Saving project...");
            await dispatcher.InvokeAsync(() =>
            {
                ProgressBoxViewModel.Message = "Saving project...";
                ProgressBoxViewModel.Progress = 0.1f;
                ProgressBoxViewModel.Visible = Visibility.Visible;
            });
            // For now, this method can't be trusted on other threads, let run on the main thread.
            // Chances are this method is being called from the main thread anyway, so it shouldn't
            // make a difference.
            if (syncModel)
            {
                await dispatcher.InvokeAsync(() =>
                {
                    EnsureShowfileModelSync();
                });
            }

            await dispatcher.InvokeAsync(() =>
            {
                ProgressBoxViewModel.Progress = 0.5f;
            });

            PluginLoader.OnSave(path);

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

        await dispatcher.InvokeAsync(() =>
        {
            ProgressBoxViewModel.Visible = Visibility.Collapsed;
        });
    }

    /// <summary>
    /// Saves the show file synchronously. This method must be called from the main thread. Prefer <see cref="SaveProjectAsync(string, bool, bool)"/>.
    /// </summary>
    /// <param name="path"></param>
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

            PluginLoader.OnSave(path);

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
    public async Task PackProject(string path)
    {
        using var _ = UndoManager.ScopedSuppress();

        try
        {
            Log("Packing project...");
            await dispatcher.InvokeAsync(() =>
            {
                ProgressBoxViewModel.Message = "Packing project...";
                ProgressBoxViewModel.Progress = 0.1f;
                ProgressBoxViewModel.Visible = Visibility.Visible;
            });

            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            string projPath = Path.Combine(path, $"{Path.GetFileName(path)}.qproj");

            // Saving the project should result in all paths being re-resolved as the ViewModel->Model sync occurs
            // While this happens, we capture a list of each path being resolved so we can pack them later.
            captureResolvedPaths = [];
            //SaveProject(path);
            await dispatcher.InvokeAsync(() =>
            {
                EnsureShowfileModelSync();
            });

            PluginLoader.OnSave(path);

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
                        dispatcher.Invoke(() =>
                        {
                            ProgressBoxViewModel.Message = $"Copying media... ({packedPaths.Count + 1}/{nCaptured})";
                            ProgressBoxViewModel.Progress = packedPaths.Count / (float)nCaptured;
                            ProgressBoxViewModel.Visible = Visibility.Visible;
                        });
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
                    dispatcher.Invoke(() =>
                    {
                        ProgressBoxViewModel.Message = $"Copying media... ({packedPaths.Count + 1}/{nCaptured})";
                        ProgressBoxViewModel.Progress = packedPaths.Count / (float)nCaptured;
                        ProgressBoxViewModel.Visible = Visibility.Visible;
                    });
                    File.Copy(expandedList[0].expanded, dst, true);

                    // Store the new path in a lookup
                    packedPaths.TryAdd(expandedList[0].captured, Path.GetRelativePath(path, dst));
                }
            }

            await dispatcher.InvokeAsync(() =>
            {
                ProgressBoxViewModel.Message = $"Saving packed project...";
                ProgressBoxViewModel.Progress = 1;
                ProgressBoxViewModel.Visible = Visibility.Visible;
            });

            // Resave the project, applying all the modified paths, this will be
            // done automatically by the path resolver using the packedPaths dict
            // we just made.
            await SaveProjectAsync(projPath);
            ProjectFilePath = projPath;

            Log($"Successfully packed {packedPaths.Count} media files into '{path}'");
        }
        catch (Exception e)
        {
            Log($"Couldn't pack project to disk. Trying to pack into {path} \n  failed with: {e}", LogLevel.Warning);
        }

        // Reset these when not packing a project
        captureResolvedPaths = null;
        packedPaths = null;
        await dispatcher.InvokeAsync(() =>
        {
            ProgressBoxViewModel.Visible = Visibility.Collapsed;
            ProgressBoxViewModel.Message = string.Empty;
            ProgressBoxViewModel.Progress = 0;
        });
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
