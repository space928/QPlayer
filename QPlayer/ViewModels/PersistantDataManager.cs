using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace QPlayer.ViewModels;

public class PersistantDataManager : ObservableObject
{
    private const int MAX_RECENT_FILES = 10;
    private const string AUTO_BACK_FILES_NAME = "Recovered Files";

    private string? dataDir;
    private string? autoBackDir;

    private readonly Dispatcher dispatcher;
    private readonly ObservableCollection<RecentFile> recentFilesView = [];
    private readonly ObservableCollection<RecentFile> autoBackupFiles = [];

    public ObservableCollection<RecentFile> RecentFiles => recentFilesView;
    public string AutoBackDir => autoBackDir ?? string.Empty;

    public PersistantDataManager(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        Initialise();
    }

    public void Initialise()
    {
        try
        {
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appdata))
                appdata = Environment.CurrentDirectory;

            dataDir = Path.Combine(appdata, "QPlayer");
            Directory.CreateDirectory(dataDir);
            autoBackDir = Path.Combine(dataDir, "Autobackup");
            Directory.CreateDirectory(AutoBackDir);
        }
        catch (Exception ex)
        {
            MainViewModel.Log($"[PersistantData] Couldn't initialise persistant data directory, program settings won't be loaded!\n{ex}", MainViewModel.LogLevel.Warning);
        }

        LoadRecentFiles();
    }

    private void LoadRecentFiles()
    {
        if (string.IsNullOrEmpty(dataDir))
            return;

        try
        {
            using var f = File.OpenRead(Path.Combine(dataDir, "recent_files.json"));
            var recent = JsonSerializer.Deserialize<RecentFile[]>(f);
            recentFilesView.Clear();
            if (recent != null)
            {
                foreach (var item in recent)
                    recentFilesView.Add(item);
            }

            recentFilesView.Add(new() { Name = AUTO_BACK_FILES_NAME, SubList = autoBackupFiles });
            var _ = RefreshAutoBackFiles();
        }
        catch (Exception ex)
        {
            MainViewModel.Log($"[PersistantData] Couldn't load to recent files list!\n{ex}", MainViewModel.LogLevel.Warning);
        }
    }

    public async Task RefreshAutoBackFiles()
    {
        autoBackupFiles.Clear();
        try
        {
            var files = await Task.Run(() =>
            {
                var paths = Directory.EnumerateFiles(AutoBackDir, "*.qproj");
                var fileInfos = paths.Select(x => new FileInfo(x));
                return fileInfos.OrderByDescending(x => x.LastWriteTimeUtc).ToArray();
            });
            await dispatcher.InvokeAsync(() =>
            {
                foreach (var info in files)
                    autoBackupFiles.Add(new RecentFile()
                    {
                        Name = Path.GetFileNameWithoutExtension(info.Name),
                        Path = info.FullName
                    });
            });
        }
        catch (Exception ex)
        {
            MainViewModel.Log($"Couldn't refresh recovered files list. {ex.Message}\n{ex}", MainViewModel.LogLevel.Warning);
        }
    }

    public void AddRecentFile(string fileName)
    {
        string fileNameShort = Path.GetFileNameWithoutExtension(fileName);
        if (fileNameShort.StartsWith("autoback"))
            return;

        string shortPath = fileName;
        if (fileName.Length > 32)
            shortPath = string.Concat("...", shortPath.AsSpan(fileName.Length - 32, 32));
        RecentFile recent = new()
        {
            Path = fileName,
            Name = $"{fileNameShort} ({shortPath})"
        };

        _ = dispatcher.InvokeAsync(() =>
        {
            if (recentFilesView.Remove(recent))
            {
                recentFilesView.Insert(0, recent);
            }
            else
            {
                recentFilesView.Insert(0, recent);
                if (recentFilesView.Count > MAX_RECENT_FILES)
                    recentFilesView.RemoveAt(recentFilesView.Count - 2); // Remove the last recent file, excluding the list of autoback files
            }
        });

        if (dataDir == null)
            return;

        try
        {
            using var f = File.OpenWrite(Path.Combine(dataDir, "recent_files.json"));
            f.SetLength(0);
            JsonSerializer.Serialize(f, recentFilesView.SkipLast(1).ToArray());
        }
        catch (Exception ex)
        {
            MainViewModel.Log($"[PersistantData] Couldn't save to recent files list!\n{ex}", MainViewModel.LogLevel.Warning);
        }
    }
}

public struct RecentFile
{
    public string Name { get; set; }
    public string? Path { get; set; }
    public ObservableCollection<RecentFile>? SubList { get; set; }
}
