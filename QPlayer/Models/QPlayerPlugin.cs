using QPlayer.ViewModels;

namespace QPlayer.Models;

public abstract class QPlayerPlugin
{
    /// <summary>
    /// Called at startup when the plugin is loaded by QPlayer.
    /// </summary>
    /// <param name="mainViewModel"></param>
    public virtual void OnLoad(MainViewModel mainViewModel) { }
    /// <summary>
    /// Called just before QPlayer exits.
    /// </summary>
    public virtual void OnUnload() { }
    /// <summary>
    /// Called just before QPlayer saves a show file.
    /// </summary>
    /// <param name="path"></param>
    public virtual void OnSave(string path) { }
    /// <summary>
    /// Called every time QPlayer starts a cue.
    /// </summary>
    /// <param name="cue"></param>
    public virtual void OnGo(CueViewModel cue) { }
    /// <summary>
    /// Called every 250 ms on the UI thread.
    /// </summary>
    public virtual void OnSlowUpdate() { }
    /// <summary>
    /// Called every 40 ms on the UI thread.
    /// </summary>
    //public void OnFastUpdate();
}
