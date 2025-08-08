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

namespace QPlayer.ViewModels;

public class PersistantDataManager : ObservableObject
{
    private const int MAX_RECENT_FILES = 10;

    private string? dataDir;

    private readonly ObservableCollection<RecentFile> recentFiles = [];

    public ObservableCollection<RecentFile> RecentFiles => recentFiles;

    public PersistantDataManager()
    {
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
        }
        catch (Exception ex)
        {
            MainViewModel.Log($"[PersistantData] Couldn't initialise persistant data directory, program settings won't be loaded!\n{ex}");
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
            recentFiles.Clear();
            if (recent != null)
            {
                foreach (var item in recent)
                    recentFiles.Add(item);
            }
        }
        catch (Exception ex)
        {
            MainViewModel.Log($"[PersistantData] Couldn't load to recent files list!\n{ex}");
        }
    }

    public void AddRecentFile(string fileName)
    {
        string shortPath = fileName;
        if (fileName.Length > 32)
            shortPath = string.Concat("...", shortPath.AsSpan(fileName.Length - 32, 32));
        RecentFile recent = new()
        {
            Path = fileName,
            Name = $"{Path.GetFileNameWithoutExtension(fileName)} ({shortPath})"
        };

        if (recentFiles.Remove(recent))
        {
            recentFiles.Insert(0, recent);
        }
        else
        {
            recentFiles.Insert(0, recent);
            if (recentFiles.Count > MAX_RECENT_FILES)
                recentFiles.RemoveAt(recentFiles.Count - 1);
        }

        if (dataDir == null)
            return;

        try
        {
            using var f = File.OpenWrite(Path.Combine(dataDir, "recent_files.json"));
            JsonSerializer.Serialize(f, recentFiles.ToArray());
        }
        catch (Exception ex)
        {
            MainViewModel.Log($"[PersistantData] Couldn't save to recent files list!\n{ex}");
        }
    }
}

public struct RecentFile
{
    public string Name { get; set; }
    public string Path { get; set; }
}
