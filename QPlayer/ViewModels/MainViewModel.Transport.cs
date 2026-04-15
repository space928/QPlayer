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
        for (int i = 0; i < Cues.Count; i++)
            Cues[i].Stop();

        AudioPlaybackManager.StopAllSounds();
    }
}
