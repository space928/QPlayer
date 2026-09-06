using QPlayer.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace QPlayer.ViewModels;

/*
 All the cue transport control methods in the MainViewModel.
 */

public partial class MainViewModel
{
    public void Go() => Go(SelectedCue);

    public void Go(CueViewModel? cue)
    {
        //dbg_cueStartTime = DateTime.Now;
        //Log($"[Playback Debugging] Go command started! {dbg_cueStartTime:HH:mm:ss.ffff}");
        // MeasureProfiler.StartCollectingData("Go Execute");

        if (cue == null)
            return;

        SelectedCue = cue;
        CueViewModel? waitCue = null;
        int i = SelectedCueInd;

        using var ie = Cues.EnumerateAllFrom(i + 1).GetEnumerator();
        while (true)
        {
            // If this cue is enabled, run it
            if (cue.Enabled)
                cue.DelayedGo(waitCue);

            // Increment the selection
            i++;

            // Check the next cue in the stack
            if (!ie.MoveNext())
                break;
            var next = ie.Current;

            if (next.Enabled)
            {
                if (next.Trigger == TriggerMode.Go)
                    break; // Don't start the next cue automatically
                else if (next.Trigger == TriggerMode.AfterLast)
                    waitCue = cue; // Set the next cue to wait for this one to finish
            }
            cue = next;
        }

        // Use max here to account for re-entrancy
        SelectedCueInd = Math.Max(SelectedCueInd, i);
    }

    public void Pause()
    {
        for (int i = ActiveCues.Count - 1; i >= 0; i--)
            ActiveCues[i].Pause();
    }

    public void Unpause()
    {
        for (int i = ActiveCues.Count - 1; i >= 0; i--)
            if (ActiveCues[i].State == CueState.Paused)
                ActiveCues[i].Go();
    }

    public void Stop()
    {
        //for (int i = ActiveCues.Count - 1; i >= 0; i--)
        //    ActiveCues[i].Stop();
        //for (int i = 0; i < Cues.Count; i++)
        //    Cues[i].Stop();
        foreach (var cue in Cues.EnumerateAll())
            cue.Stop();

        AudioPlaybackManager.StopAllSounds();
    }
}
