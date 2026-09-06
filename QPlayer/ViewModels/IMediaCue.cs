using QPlayer.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.ViewModels;

/// <summary>
/// Defines a properties and methods used by a field which uses media file(s).
/// </summary>
/// <remarks>
/// When accessing the <see cref="Path"/> property, the real path should be resolved using 
/// <see cref="MainViewModel.ResolvePath(string, bool)"/>, and the resulting path should be loaded 
/// in <see cref="LoadMediaFiles"/>. The media manager, is responsible for path resolution and may 
/// itself call <see cref="LoadMediaFiles"/> if the path resolution is overridden in the media 
/// manager window.
/// </remarks>
public interface IMediaCue
{
    /// <summary>
    /// The main file path for the media file used by this cue. 
    /// </summary>
    public abstract string Path { get; set; }

    /// <summary>
    /// An enumerable of the property names of the undoable properties on this cue which contain media file paths.
    /// <para/>
    /// Defaults to <c>[nameof(Path)]</c>.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
    public virtual IEnumerable<string> MediaPathProperties => new OneEnumerable<string>(nameof(Path));

    /// <summary>
    /// Loads the media file(s) used by this cue. Generally this is called by the implementing cue itself.
    /// </summary>
    /// <returns>A promise which resolves to <see langword="true"/> if the media files were successfully loaded.</returns>
    public abstract Task<bool> LoadMediaFiles();

    /// <summary>
    /// Unloads the media file(s) used by this cue. Generally this is called by the implementing cue itself.
    /// </summary>
    public void UnloadMediaFiles();
}
