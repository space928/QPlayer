using OscCore;
using QPlayer.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using static QPlayer.ViewModels.MainViewModel;
using Timer = System.Timers.Timer;

namespace QPlayer.ViewModels;

public class OSCManager
{
    private readonly MainViewModel mainViewModel;
    private readonly OSCDriver oscDriver;
    private readonly SynchronizationContext syncContext;
    private readonly Timer discoveryTimer;
    private CancellationTokenSource cancelOSCRestart;
    private ShowFileSender? showFileSender;
    private int restartOSCDelay;

    public OSCManager(MainViewModel mainViewModel)
    {
        this.mainViewModel = mainViewModel;
        this.oscDriver = new();
        restartOSCDelay = 10;
        cancelOSCRestart = new();
        syncContext = SynchronizationContext.Current ?? new();
        discoveryTimer = new(1000);
        discoveryTimer.AutoReset = true;
        discoveryTimer.Elapsed += DiscoveryTimer_Elapsed;
        discoveryTimer.Start();

        //SubscribeOSC();

        oscDriver.OnRXFailure += OscDriver_OnRXFailure;
    }

    private void OscDriver_OnRXFailure()
    {
        restartOSCDelay *= 2;
        restartOSCDelay = Math.Min(restartOSCDelay, 60 * 1000);
        cancelOSCRestart = new();
        Task.Delay(restartOSCDelay, cancelOSCRestart.Token).ContinueWith(_ =>
        {
            syncContext?.Send(_ =>
            {
                Log($"Automatically restarting OSC driver...");
                ConnectOSC();
            }, null);
        });
    }

    private ProjectSettingsViewModel ProjectSettings => mainViewModel.ProjectSettings;

    public void ConnectOSC()
    {
        // Flip the RX and TX ports if this is a remote client.
        if (!ProjectSettings.IsRemoteHost && ProjectSettings.EnableRemoteControl)
            oscDriver.OSCConnect(ProjectSettings.OSCNic, ProjectSettings.OSCSubnet, ProjectSettings.OSCTXPort, ProjectSettings.OSCRXPort);
        else
            oscDriver.OSCConnect(ProjectSettings.OSCNic, ProjectSettings.OSCSubnet, ProjectSettings.OSCRXPort, ProjectSettings.OSCTXPort);
    }

    public void MonitorOSC(bool enable)
    {
        if (enable)
        {
            oscDriver.OnRXMessage += OscDriver_LogRXMessage;
            oscDriver.OnTXMessage += OscDriver_LogTXMessage;
        }
        else
        {
            oscDriver.OnRXMessage -= OscDriver_LogRXMessage;
            oscDriver.OnTXMessage -= OscDriver_LogTXMessage;
        }
    }

    public void SendRemoteGo(string target, decimal qid)
    {
        OscMessage msg = new("/qplayer/remote/go", [target, qid.ToString()]);
        oscDriver.SendMessage(msg);
    }

    public void SendRemotePause(string target, decimal qid)
    {
        OscMessage msg = new("/qplayer/remote/pause", [target, qid.ToString()]);
        oscDriver.SendMessage(msg);
    }

    public void SendRemoteUnpause(string target, decimal qid)
    {
        OscMessage msg = new("/qplayer/remote/unpause", [target, qid.ToString()]);
        oscDriver.SendMessage(msg);
    }

    public void SendRemoteStop(string target, decimal qid)
    {
        OscMessage msg = new("/qplayer/remote/stop", [target, qid.ToString()]);
        oscDriver.SendMessage(msg);
    }

    public void SendRemotePreload(string target, decimal qid, float time)
    {
        OscMessage msg = new("/qplayer/remote/preload", [target, qid.ToString(), time]);
        oscDriver.SendMessage(msg);
    }

    public void SendRemotePing(string target)
    {
        OscMessage msg = new("/qplayer/remote/ping", [target]);
        oscDriver.SendMessage(msg);
    }

    public void SendRemoteUpdateShowFile(IEnumerable<string> targets, byte[] showFile)
    {
        SendRemoteUpdateShowFileAsync(targets, showFile)
            .ContinueWith(x => Log($"Error while sending show file to remote client: '{x.Exception?.Message}'\n{x.Exception}", LogLevel.Error),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public async Task SendRemoteUpdateShowFileAsync(IEnumerable<string> targets, byte[] showFile)
    {
        if (!(showFileSender?.IsComplete ?? true))
        {
            Log($"Can't send showfile until last transfer has completed or timed out!", LogLevel.Warning);
            return;
        }

        try
        {
            foreach (var target in targets)
            {
                showFileSender = new(target, showFile, oscDriver);
                await showFileSender.SendShowFileAsync();
            }
        }
        catch (Exception ex)
        {
            Log($"Error while sending show file to remote client: '{ex.Message}'\n{ex}", LogLevel.Error);
            showFileSender = null;
        }
    }

    public void SendRemoteStatus(string target, decimal qid, CueState state, float? playbackTime = null)
    {
        OscMessage msg;
        if (playbackTime != null)
            msg = new("/qplayer/remote/fb/cue-status", [target, qid.ToString(), (int)state, playbackTime]);
        else
            msg = new("/qplayer/remote/fb/cue-status", [target, qid.ToString(), (int)state]);
        oscDriver.SendMessage(msg);
    }

    private void OscDriver_LogRXMessage(OscMessage obj)
    {
        Log($"OSC RX: {obj}", LogLevel.Info);
    }

    private void OscDriver_LogTXMessage(OscMessage obj)
    {
        Log($"OSC TX: {obj}", LogLevel.Info);
    }

    internal void SubscribeOSC()
    {
        oscDriver.Subscribe("/qplayer/go", msg =>
        {
            if (msg.Count > 0)
            {
                if (mainViewModel.FindCue(msg[0], out var cue))
                {
                    if (msg.Count > 1)
                    {
                        mainViewModel.SelectedCue = cue;
                        mainViewModel.GoExecute();
                    }
                    else
                    {
                        cue.DelayedGo();
                    }
                }
                else
                {
                    Log($"Couldn't find cue with ID {msg[0]}!", LogLevel.Warning);
                }
            }
            else
                mainViewModel.GoExecute();
        }, syncContext);
        oscDriver.Subscribe("/qplayer/stop", msg =>
        {
            if (msg.Count > 0)
            {
                if (mainViewModel.FindCue(msg[0], out var cue))
                    cue.Stop();
            }
            else
                mainViewModel.StopExecute();
        }, syncContext);
        oscDriver.Subscribe("/qplayer/pause", msg =>
        {
            if (msg.Count > 0)
            {
                if (mainViewModel.FindCue(msg[0], out var cue))
                    cue.Pause();
            }
            else
                mainViewModel.PauseExecute();
        }, syncContext);
        oscDriver.Subscribe("/qplayer/unpause", msg =>
        {
            if (msg.Count > 0)
            {
                if (mainViewModel.FindCue(msg[0], out var cue) && cue.State == CueState.Paused)
                    cue.Go();
            }
            else
                mainViewModel.UnpauseExecute();
        }, syncContext);
        oscDriver.Subscribe("/qplayer/preload", msg =>
        {
            if (msg.Count > 0)
            {
                if (mainViewModel.FindCue(msg[0], out var cue))
                {
                    if (msg.Count > 1 && msg[1] is float time)
                        cue.Preload(TimeSpan.FromSeconds(time));
                    else
                        cue.Preload(mainViewModel.PreloadTime);
                }
            }
            else
            {
                mainViewModel.PreloadExecute();
            }
        }, syncContext);

        oscDriver.Subscribe("/qplayer/select", msg =>
        {
            if (msg.Count > 0 && mainViewModel.FindCue(msg[0], out var cue))
            {
                mainViewModel.SelectedCue = cue;
            }
        }, syncContext);
        oscDriver.Subscribe("/qplayer/up", _ => mainViewModel.SelectedCueInd--, syncContext);
        oscDriver.Subscribe("/qplayer/down", _ => mainViewModel.SelectedCueInd++, syncContext);

        oscDriver.Subscribe("/qplayer/save", _ => mainViewModel.SaveProjectExecute(), syncContext);

        SetupRemoteControl();
    }

    private void SetupRemoteControl()
    {
        /*mainViewModel.OnSlowUpdate += () =>
        {
            
        };*/

        oscDriver.Subscribe("/qplayer/remote/discovery", msg =>
        {
            if (msg.Count > 0 && msg[0] is string name)
            {
                bool isNew = ProjectSettings.GetOrAddRemoteNode(name, out var node);
                if (isNew)
                    node.IPAddress = msg.Origin?.Address;
                node.LastDiscoveryTime = DateTime.UtcNow;
            }
        }, syncContext);
        oscDriver.Subscribe("/qplayer/remote/pong", msg =>
        {

        }, syncContext);
        oscDriver.Subscribe("/qplayer/remote/fb/cue-status", msg =>
        {
            if (msg.Count >= 3
                && msg[0] is string name
                && msg[1] is object qid
                && msg[2] is int state)
            {
                if (mainViewModel.FindCue(qid, out var cue))
                {
                    cue.State = (CueState)state;
                    if (cue.State == CueState.Ready)
                        cue.StopInternal();
                    if (msg.Count >= 4 && msg[3] is float time)
                        cue.PlaybackTime = TimeSpan.FromSeconds(time);
                    if (msg.Count >= 5 && msg[4] is float duration)
                        cue.RemoteDuration = TimeSpan.FromSeconds(duration);
                }
            }
        }, syncContext);

        oscDriver.Subscribe("/qplayer/remote/go", msg =>
        {
            if (FindRemoteMessageCue(msg, out var cue))
                cue.DelayedGo();
        }, syncContext);
        oscDriver.Subscribe("/qplayer/remote/pause", msg =>
        {
            if (FindRemoteMessageCue(msg, out var cue))
                cue.Pause();
        }, syncContext);
        oscDriver.Subscribe("/qplayer/remote/unpause", msg =>
        {
            if (FindRemoteMessageCue(msg, out var cue))
                if (cue.State == CueState.Paused)
                    cue.Go();
        }, syncContext);
        oscDriver.Subscribe("/qplayer/remote/stop", msg =>
        {
            if (FindRemoteMessageCue(msg, out var cue))
                cue.Stop();
        }, syncContext);
        oscDriver.Subscribe("/qplayer/remote/preload", msg =>
        {
            if (FindRemoteMessageCue(msg, out var cue))
                if (msg.Count >= 3 && msg[3] is float time)
                    cue.Preload(TimeSpan.FromSeconds(time));
        }, syncContext);
        oscDriver.Subscribe("/qplayer/remote/ping", msg =>
        {
            OscMessage pong = new("/qplayer/remote/pong");
            oscDriver.SendMessage(pong);
        }, syncContext);
        oscDriver.Subscribe("/qplayer/remote/update-show", msg =>
        {
            // TODO: Update showfile OSC command
            Log($"Show file updates over OSC are not yet supported", LogLevel.Warning);
        }, syncContext);
        oscDriver.Subscribe("/qplayer/remote/update-show-ack", msg =>
        {
            if (msg.Count == 2
                && msg[0] is string name
                && msg[1] is int block)
                showFileSender?.ReceiveAck(name, block);
        });
        oscDriver.Subscribe("/qplayer/remote/update-show-nack", msg =>
        {
            if (msg.Count == 2
                && msg[0] is string name
                && msg[1] is int block)
                showFileSender?.ReceiveNack(name, block);
        });
    }

    private bool FindRemoteMessageCue(OscMessage msg, [NotNullWhen(true)] out CueViewModel? cue)
    {
        cue = null;
        if (msg.Count >= 2
            && msg[0] is string name
            && msg[1] is object qid)
        {
            if (mainViewModel.FindCue(qid, out cue))
                return true;
        }
        return false;
    }

    private void DiscoveryTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        if (!(ProjectSettings?.EnableRemoteControl ?? false))
            return;

        // Check remote nodes are still active
        foreach (var node in ProjectSettings.RemoteNodes)
        {
            if (node.CheckIsActive())
            {
                Log($"Remote node '{node.Name}' has not replied in the last 5 seconds, check it's connection!", LogLevel.Warning);
            }
        }

        OscMessage discoveryMsg = new("/qplayer/remote/discovery", [ProjectSettings.NodeName]);
        oscDriver.SendMessage(discoveryMsg);
    }
}

internal class ShowFileSender(string target, byte[] showFile, OSCDriver oscDriver)
{
    private const int BlockSize = 1024;

    public bool IsComplete => isComplete;

    private volatile bool isComplete = false;
    //private volatile int isComplete = false;
    private volatile int totalBlocks = 0;
    private readonly ConcurrentQueue<Block> blocksToSend = [];
    private readonly ConcurrentDictionary<int, Block> sentBlocks = [];

    public void SendShowFile()
    {
        SendShowFileAsync()
            .ContinueWith(x => Log($"Error while sending show file to remote client: '{x.Exception?.Message}'\n{x.Exception}", LogLevel.Error),
            TaskContinuationOptions.OnlyOnFaulted);
        isComplete = true;
    }

    public async Task SendShowFileAsync()
    {
        // Chunk the showfile
        int pos = 0;
        for (int i = 0; i <= showFile.Length / BlockSize; i++)
        {
            Block block = new()
            {
                ind = i,
                buff = new(showFile, pos, Math.Min(BlockSize, showFile.Length - pos))
            };
            blocksToSend.Enqueue(block);
            //sentBlocks.TryAdd(i, block);
            pos += BlockSize;
        }
        totalBlocks = blocksToSend.Count;

        DateTime startTime = DateTime.UtcNow;

        while (!isComplete)
        {
            var now = DateTime.UtcNow;
            if (now - startTime >= TimeSpan.FromSeconds(5))
                throw new TimeoutException("Sending showfile timed out after 5 seconds.");

            if (blocksToSend.TryDequeue(out var block))
            {
                sentBlocks.TryRemove(block.ind, out _);

                block.sentTime = now;
                block.acknowledged = false;
                sentBlocks.TryAdd(block.ind, block);

                await oscDriver.SendMessageAsync(
                    new("/qplayer/remote/update-show", target, block.ind, totalBlocks, block.buff.ToArray()));
            }
            else
            {
                // Retry any blocks that haven't been acknowledged yet
                bool allAcknowledged = true;
                foreach (var sentBlock in sentBlocks.Values)
                {
                    allAcknowledged &= sentBlock.acknowledged;
                    if (!sentBlock.acknowledged && now - sentBlock.sentTime > TimeSpan.FromMilliseconds(250))
                    {
                        blocksToSend.Enqueue(sentBlock);
                    }
                }

                if (allAcknowledged && sentBlocks.Count == totalBlocks)
                {
                    isComplete = true;
                }

                await Task.Delay(50);
            }
        }

        Log($"Successfully sent showfile to remote nodes!", LogLevel.Info);
    }

    public void ReceiveAck(string name, int block)
    {
        if (name != target)
            return;

        // Special case, indicates all blocks have been acknowledged
        if (block == -1)
        {
            isComplete = true;
            return;
        }

        if (sentBlocks.TryRemove(block, out var b))
        {
            b.acknowledged = true;
            sentBlocks.TryAdd(block, b);
        }
    }

    public void ReceiveNack(string name, int block)
    {
        if (name != target)
            return;

        if (sentBlocks.TryRemove(block, out var b))
        {
            blocksToSend.Enqueue(b);
        }
    }

    internal struct Block
    {
        public int ind;
        public bool acknowledged;
        public DateTime sentTime;

        public ArraySegment<byte> buff;
    }
}
