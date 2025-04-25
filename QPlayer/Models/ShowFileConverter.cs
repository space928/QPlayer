using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace QPlayer.Models;

/// <summary>
/// This class contains the relevant code to repair and upgrade old show files.
/// </summary>
public class ShowFileConverter
{
    private static readonly JsonSerializerOptions jsonSerializerOptions = new()
    {
        IncludeFields = true,
        AllowTrailingCommas = true,
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private static readonly JsonDocumentOptions jsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Attempts to load a show file one field at a time, skipping any fields which can't be read.
    /// 
    /// This method uses fairly inefficient reflection to try and deserialize as much of the show file as possible.
    /// </summary>
    /// <param name="utf8Stream">The contents of show file to load.</param>
    /// <returns>A ShowFile, with as many parameters loaded as possible.</returns>
    /// <exception cref="FileFormatException"></exception>
    public static async Task<ShowFile> LoadShowFileSafeAsync(Stream utf8Stream)
    {
        using var json = await JsonDocument.ParseAsync(utf8Stream, jsonDocumentOptions);
        return await Task.Run(() => LoadShowFileSafe(json));
    }

    /// <summary>
    /// Attempts to load a show file one field at a time, skipping any fields which can't be read.
    /// 
    /// This method uses fairly inefficient reflection to try and deserialize as much of the show file as possible.
    /// </summary>
    /// <param name="jsonString">The contents of show file to load.</param>
    /// <returns>A ShowFile, with as many parameters loaded as possible.</returns>
    /// <exception cref="FileFormatException"></exception>
    public static ShowFile LoadShowFileSafe(string jsonString)
    {
        using var json = JsonDocument.Parse(jsonString, jsonDocumentOptions);
        return LoadShowFileSafe(json);
    }

    /// <summary>
    /// Attempts to load a show file one field at a time, skipping any fields which can't be read.
    /// 
    /// This method uses fairly inefficient reflection to try and deserialize as much of the show file as possible.
    /// </summary>
    /// <param name="json">The contents of show file to load.</param>
    /// <returns>A ShowFile, with as many parameters loaded as possible.</returns>
    /// <exception cref="FileFormatException"></exception>
    public static ShowFile LoadShowFileSafe(JsonDocument json)
    {
        if (json.RootElement.ValueKind != JsonValueKind.Object)
            throw new FileFormatException("Show file is not a JSON object!");

        ShowFile res = new();

        foreach (var field in json.RootElement.EnumerateObject())
        {
            switch (field.Name)
            {
                case nameof(ShowFile.fileFormatVersion):
                    if (field.Value.TryGetInt32(out var ver))
                        res.fileFormatVersion = ver;
                    break;
                case nameof(ShowFile.columnWidths):
                    if (field.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var width in field.Value.EnumerateArray())
                            if (width.TryGetSingle(out var widthVal))
                                res.columnWidths.Add(widthVal);
                    }
                    break;
                case nameof(ShowFile.showSettings):
                    if (field.Value.ValueKind == JsonValueKind.Object)
                        res.showSettings = LoadShowSettingsSafe(field.Value);
                    break;
                case nameof(ShowFile.cues):
                    if (field.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cue in field.Value.EnumerateArray())
                            if (cue.ValueKind == JsonValueKind.Object)
                                res.cues.Add(LoadCueSafe(cue));
                    }
                    break;
            }
        }

        // Upgrade the show file if needed
        UpgradeShowFile(res, json);

        return res;
    }

    /// <summary>
    /// Upgrades a show file from an older version by parsing attempting to parse old data.
    /// </summary>
    /// <param name="showFile">The show file to merge into.</param>
    /// <param name="jsonString">The json to parse.</param>
    public static async Task UpgradeShowFileAsync(ShowFile showFile, Stream utf8Stream)
    {
        using var json = await JsonDocument.ParseAsync(utf8Stream, jsonDocumentOptions);
        await Task.Run(() => UpgradeShowFile(showFile, json));
    }

    /// <summary>
    /// Upgrades a show file from an older version by parsing attempting to parse old data.
    /// </summary>
    /// <param name="showFile">The show file to merge into.</param>
    /// <param name="jsonString">The json to parse.</param>
    public static void UpgradeShowFile(ShowFile showFile, string jsonString)
    {
        using var json = JsonDocument.Parse(jsonString, jsonDocumentOptions);
        UpgradeShowFile(showFile, json);
    }

    /// <inheritdoc cref="UpgradeShowFile(ShowFile, string)"/>
    public static void UpgradeShowFile(ShowFile showFile, JsonDocument json)
    {
        // When the file format version is upgrade chain new format upgraders here.
        if (showFile.fileFormatVersion < 3)
            UpgradeV2ToV3(showFile, json);
    }

    private static void UpgradeV2ToV3(ShowFile showFile, JsonDocument json)
    {
        MainViewModel.Log($"Upgrading show file from V2 to V3...", MainViewModel.LogLevel.Info);

        if (json.RootElement.ValueKind != JsonValueKind.Object)
            return;

        foreach (var field in json.RootElement.EnumerateObject())
        {
            switch (field.Name)
            {
                case "showMetadata":
                    if (field.Value.ValueKind == JsonValueKind.Object)
                        showFile.showSettings = LoadShowSettingsSafe(field.Value);
                    break;
                case nameof(ShowFile.cues):
                    if (field.Value.ValueKind == JsonValueKind.Array)
                    {
                        int i = 0;
                        foreach (var cue in field.Value.EnumerateArray())
                        {
                            if (cue.ValueKind == JsonValueKind.Object)
                            {
                                var cueLoaded = showFile.cues[i];
                                UpgradeCue(cueLoaded, cue);
                                i++;
                            }
                        }
                    }
                    break;
            }
        }

        static void UpgradeCue(Cue cue, JsonElement json)
        {
            foreach (var field in json.EnumerateObject())
            {
                switch (field.Name)
                {
                    case nameof(Cue.colour):
                        SerializedColour col = default;
                        if (field.Value.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var colField in field.Value.EnumerateObject())
                            {
                                switch (colField.Name)
                                {
                                    case "R":
                                        if (colField.Value.TryGetByte(out var r))
                                            col.r = r / 255f; 
                                        break;
                                    case "G":
                                        if (colField.Value.TryGetByte(out var g))
                                            col.g = g / 255f;
                                        break;
                                    case "B":
                                        if (colField.Value.TryGetByte(out var b))
                                            col.b = b / 255f;
                                        break;
                                    case "A":
                                        if (colField.Value.TryGetByte(out var a))
                                            col.a = a / 255f;
                                        break;
                                }
                            }
                        }
                        cue.colour = col;
                        break;
                }
            }
        }
    }

    private static ShowSettings LoadShowSettingsSafe(JsonElement json)
    {
        ShowSettings settings = new();
        var fields = new Dictionary<string, FieldInfo>(
            typeof(ShowSettings).GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Select(x => new KeyValuePair<string, FieldInfo>(x.Name, x)));

        foreach (var jsonProp in json.EnumerateObject())
        {
            if (fields.TryGetValue(jsonProp.Name, out var fld))
            {
                try
                {
                    var typeInfo = JsonTypeInfo.CreateJsonTypeInfo(fld.FieldType, jsonSerializerOptions);
                    var obj = jsonProp.Value.Deserialize(typeInfo);
                    fld.SetValue(settings, obj);
                }
                catch { }
            }
        }
        return settings;
    }

    private static Cue LoadCueSafe(JsonElement json)
    {
        Cue cue = new();

        // Determine the cue type
        foreach (var field in json.EnumerateObject())
        {
            switch (field.Name)
            {
                case "$type":
                    if (field.Value.ValueKind == JsonValueKind.String)
                    {
                        cue = field.Value.GetString() switch
                        {
                            nameof(DummyCue) => new DummyCue(),
                            nameof(GroupCue) => new GroupCue(),
                            nameof(SoundCue) => new SoundCue(),
                            nameof(StopCue) => new StopCue(),
                            nameof(ShaderParamsCue) => new ShaderParamsCue(),
                            nameof(TimeCodeCue) => new TimeCodeCue(),
                            nameof(VideoCue) => new VideoCue(),
                            nameof(VideoFramingCue) => new VideoFramingCue(),
                            nameof(VolumeCue) => new VolumeCue(),
                            _ => cue,
                        };
                    }
                    goto CueCreated;
                    //case "type":
                    //    goto CueCreated;
            }
        }

    CueCreated:

        var fields = new Dictionary<string, FieldInfo>(
            cue.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Select(x => new KeyValuePair<string, FieldInfo>(x.Name, x)));

        foreach (var jsonProp in json.EnumerateObject())
        {
            if (fields.TryGetValue(jsonProp.Name, out var fld))
            {
                try
                {
                    var typeInfo = JsonTypeInfo.CreateJsonTypeInfo(fld.FieldType, jsonSerializerOptions);
                    var obj = jsonProp.Value.Deserialize(typeInfo);
                    fld.SetValue(cue, obj);
                }
                catch { }
            }
        }

        return cue;
    }
}
