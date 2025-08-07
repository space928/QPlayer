using OscCore;
using OscCore.LowLevel;
using QPlayer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static QPlayer.ViewModels.MainViewModel;

namespace QPlayer.ViewModels;

public class MSCManager
{
    private readonly MainViewModel mainViewModel;
    private readonly MAMSCDriver mscDriver;
    private readonly SynchronizationContext syncContext;

    public MSCManager(MainViewModel mainViewModel)
    {
        this.mainViewModel = mainViewModel;
        this.mscDriver = new();
        syncContext = SynchronizationContext.Current ?? new();
    }

    private ProjectSettingsViewModel ProjectSettings => mainViewModel.ProjectSettings;

    public void ConnectMSC()
    {
        mscDriver.MSCConnect(ProjectSettings.OSCNic, ProjectSettings.OSCSubnet, ProjectSettings.MAMSCRXPort, ProjectSettings.MAMSCTXPort);
    }

    public void MonitorMSC(bool enable)
    {
        if (enable)
        {
            mscDriver.OnRXMessage += OscDriver_LogRXMessage;
            mscDriver.OnTXMessage += OscDriver_LogTXMessage;
        }
        else
        {
            mscDriver.OnRXMessage -= OscDriver_LogRXMessage;
            mscDriver.OnTXMessage -= OscDriver_LogTXMessage;
        }
    }

    private void OscDriver_LogRXMessage(MAMSCPacket obj)
    {
        Log($"MSC RX: {obj.command} from {obj.deviceID}", LogLevel.Info);
    }

    private void OscDriver_LogTXMessage(MAMSCPacket obj)
    {
        Log($"MSC TX: {obj.command} from {obj.deviceID}", LogLevel.Info);
    }

    private bool CheckTargetedMessage(MAMSCPacket msg)
    {
        var settings = ProjectSettings;
        if (msg.deviceID != settings.MAMSCRXDevice)
            return false;

        if (settings.MAMSCExecutor == -1 || settings.MAMSCPage == -1)
            return true;

        return msg.command switch
        {
            MSCCommand.Go => msg.goData.page == null || (msg.goData.page == settings.MAMSCPage && msg.goData.executor == settings.MAMSCExecutor),
            MSCCommand.Stop => msg.stopData.page == null || (msg.stopData.page == settings.MAMSCPage && msg.stopData.executor == settings.MAMSCExecutor),
            MSCCommand.Resume => msg.resumeData.page == null || (msg.resumeData.page == settings.MAMSCPage && msg.resumeData.executor == settings.MAMSCExecutor),
            MSCCommand.TimedGo => msg.timedGoData.page == null || (msg.timedGoData.page == settings.MAMSCPage && msg.timedGoData.executor == settings.MAMSCExecutor),
            _ => true,
        };
    }

    internal void SubscribeMSC()
    {
        mscDriver.Subscribe(MSCCommands.Go, msg =>
        {
            if (!CheckTargetedMessage(msg)) return;
            if (mainViewModel.FindCue(msg.goData.qid, out var cue))
            {
                mainViewModel.SelectedCue = cue;
                mainViewModel.GoExecute();
            }
            else
            {
                Log($"Couldn't find cue with ID {msg.goData.qid}!", LogLevel.Info);
            }
        }, syncContext);
        mscDriver.Subscribe(MSCCommands.TimedGo, msg =>
        {
            if (!CheckTargetedMessage(msg)) return;
            if (mainViewModel.FindCue(msg.timedGoData.qid, out var cue))
            {
                // TODO: Currently this ignores the fade time
                mainViewModel.SelectedCue = cue;
                mainViewModel.GoExecute();
            }
            else
            {
                Log($"Couldn't find cue with ID {msg.goData.qid}!", LogLevel.Info);
            }
        }, syncContext);
        mscDriver.Subscribe(MSCCommands.Stop | MSCCommands.GoOff, msg =>
        {
            if (!CheckTargetedMessage(msg)) return;
            if (msg.command == MSCCommand.GoOff)
            {
                if (mainViewModel.FindCue(msg.goOffData.qid, out var cue))
                    cue.Stop();
            }
            else
            {
                if (msg.stopData.qid.HasValue)
                {
                    if (mainViewModel.FindCue(msg.stopData.qid.Value, out var cue))
                        cue.Stop();
                }
                else
                    mainViewModel.StopExecute();
            }
        }, syncContext);
        mscDriver.Subscribe(MSCCommands.Resume, msg =>
        {
            if (msg.resumeData.qid.HasValue)
            {
                if (mainViewModel.FindCue(msg.resumeData.qid.Value, out var cue) && cue.State == CueState.Paused)
                    cue.Go();
            }
            else
                mainViewModel.UnpauseExecute();
        }, syncContext);
    }
}
