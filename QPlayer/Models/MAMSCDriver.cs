using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static QPlayer.ViewModels.MainViewModel;

namespace QPlayer.Models;

/// <summary>
/// This class manages the receiving and sending of MA Midi Show Control packets sent over UDP.
/// </summary>
public class MAMSCDriver : IDisposable
{
    public event Action<MAMSCPacket>? OnRXMessage;
    public event Action<MAMSCPacket>? OnTXMessage;
    public event Action? OnRXFailure;

    private UdpClient? mscReceiver;
    private UdpClient? mscSender;

    private IPEndPoint? rxIP;
    private IPEndPoint? txIP;
    private IPAddress? broadcastIP;

    private readonly ArrayPool<byte> byteBufferPool = ArrayPool<byte>.Create();
    private readonly List<(MSCCommands commands, Action<MAMSCPacket> handler, SynchronizationContext? sync)> subscribers = [];

    public bool MSCConnect(IPAddress nicAddress, IPAddress subnet, int rxPort, int txPort)
    {
        broadcastIP = MakeBroadcastAddress(nicAddress, subnet);

        rxIP = new IPEndPoint(nicAddress, rxPort);
        //txIP = new IPEndPoint(IPAddress.Broadcast, txPort);
        txIP = new IPEndPoint(broadcastIP, txPort);

        Log($"Connecting to MA-MSC... rx={rxIP} tx={txIP}");

        try
        {
            Dispose();

            mscReceiver = new(new IPEndPoint(rxIP.Address, rxPort));
            //oscReceiver
            //oscReceiver.Connect(rxIP.Address, rxPort);

            mscSender = new(new IPEndPoint(nicAddress, 0));
            //oscSender.Connect(txIP);

            Task.Run(MSCRXThread);
        }
        catch (Exception e)
        {
            Log($"Failed to connect to MSC port: {e}", LogLevel.Error);
            return false;
        }

        Log("Connected to MSC network!");

        return true;
    }

    private static IPAddress MakeBroadcastAddress(IPAddress adapter, IPAddress subnet)
    {
        if (adapter.AddressFamily != AddressFamily.InterNetwork)
            return IPAddress.IPv6Any;

        // TODO: Support IPv6 multicast?
        var adapterAddrBytes = adapter.GetAddressBytes();
        var subnetAddrBytes = subnet.GetAddressBytes();

        ref var adapterUInt = ref Unsafe.As<byte, uint>(ref MemoryMarshal.GetArrayDataReference(adapterAddrBytes));
        ref var subnetUInt = ref Unsafe.As<byte, uint>(ref MemoryMarshal.GetArrayDataReference(subnetAddrBytes));

        adapterUInt |= ~subnetUInt;

        return new(adapterAddrBytes);
    }

    /// <summary>
    /// Asynchronously sends an MA-MSC packet.
    /// </summary>
    /// <param name="packet">The message to send.</param>
    /// <param name="remoteEndPoint">The endpoint to send the message to.</param>
    public void SendMessage(in MAMSCPacket packet, IPEndPoint? remoteEndPoint = null)
    {
        SendMessageAsync(packet, remoteEndPoint)
            .ContinueWith(x => Log($"Error while sending MA-MSC message: '{x.Exception?.Message}'\n{x.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <inheritdoc cref="SendMessage(MAMSCPacket, IPEndPoint?)"/>
    public async Task SendMessageAsync(MAMSCPacket packet, IPEndPoint? remoteEndPoint = null)
    {
        OnTXMessage?.Invoke(packet);

        int packetLen = packet.ComputeSizeInBytes();
        var buff = byteBufferPool.Rent(packetLen);
        if (!packet.TryWrite(buff))
            return;
        var task = mscSender?.SendAsync(buff, packetLen, remoteEndPoint ?? txIP);

        if (task != null) await task;
        byteBufferPool.Return(buff);
    }

    /// <summary>
    /// Subscribes an event handler to MSC messages with a given command.
    /// </summary>
    /// <param name="commands">The commands to subscribe to.</param>
    /// <param name="handler">The event handler to fire when a matching message is received.</param>
    public void Subscribe(MSCCommands commands, Action<MAMSCPacket> handler, SynchronizationContext? syncContext = null)
    {
        lock (this)
        {
            subscribers.Add((commands, handler, syncContext));
        }
    }

    private void MSCRXThread()
    {
        IPEndPoint? remoteEndPoint = null;
        IPEndPoint? endPointAny = new(IPAddress.Any, rxIP?.Port ?? 0);
        IPEndPoint? endPointAnyV6 = new(IPAddress.IPv6Any, rxIP?.Port ?? 0);
        var recvBuff = new byte[0x10000];
        while (mscReceiver?.Client?.IsBound ?? false)
        {
            try
            {
                EndPoint tempRemoteEP = mscReceiver.Client.AddressFamily == AddressFamily.InterNetwork ? endPointAny : endPointAnyV6;
                int received = mscReceiver.Client.ReceiveFrom(recvBuff, recvBuff.Length, SocketFlags.None, ref tempRemoteEP);
                remoteEndPoint = (IPEndPoint)tempRemoteEP;

                if (!MAMSCPacket.TryRead(recvBuff.AsSpan(0, received), out var pkt))
                {
                    Log($"Couldn't read MA-MSC packet, is it malformed? (length={received})", LogLevel.Warning);
                    continue;
                }

                OnRXMessage?.Invoke(pkt);
                var flags = CommandToFlags(pkt.command);
                foreach (var (commands, handler, sync) in subscribers)
                {
                    if ((commands & flags) != 0)
                    {
                        if (sync != null)
                            sync.Post(_ => handler(pkt), null);
                        else
                            handler(pkt);
                    }
                }

                //Log($"Recv osc msg: {pkt}", LogLevel.Debug);
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.Interrupted)
                    continue;

                // TODO: For now, there are cases where this triggers a feedback loop of reconnecting to OSC
                //OnRXFailure?.Invoke();
                Log($"MSC Network connection lost: {e}", LogLevel.Warning);
            }
            catch (Exception e)
            {
                //OnRXFailure?.Invoke();
                Log($"MSC Network connection lost: {e}", LogLevel.Warning);
            }
        }
    }

    private static MSCCommands CommandToFlags(MSCCommand cmd) => cmd switch
    {
        MSCCommand.Unknown => MSCCommands.None,
        MSCCommand.Go => MSCCommands.Go,
        MSCCommand.Stop => MSCCommands.Stop,
        MSCCommand.Resume => MSCCommands.Resume,
        MSCCommand.TimedGo => MSCCommands.TimedGo,
        MSCCommand.Set => MSCCommands.Set,
        MSCCommand.Fire => MSCCommands.Fire,
        MSCCommand.GoOff => MSCCommands.GoOff,
        _ => MSCCommands.None,
    };

    public void Dispose()
    {
        /*if (OnRXMessage != null)
            foreach (var d in OnRXMessage.GetInvocationList())
                OnRXMessage -= d as Action<OscPacket>;
        if (OnTXMessage != null)
            foreach (var d in OnTXMessage.GetInvocationList())
                OnTXMessage -= d as Action<OscPacket>;*/

        mscReceiver?.Dispose();
        mscSender?.Dispose();
        mscReceiver = null;
        mscSender = null;
    }
}

[Flags]
public enum MSCCommands
{
    None = 0,
    /// <summary>
    /// This is the same as a Goto command in grandMA2. It needs to be followed by a cue number.
    /// </summary>
    Go = 1,
    /// <summary>
    /// This is the same as a Pause command in grandMA2. This can be followed by a cue number.
    /// </summary>
    Stop = 1 << 2,
    /// <summary>
    /// This will "un-plause" a cue. If a specific cue has been paused, then the cue number needs to be specified with this command.
    /// </summary>
    Resume = 1 << 3,
    /// <summary>
    /// This can be used to perform a Goto with a specific fade time. It needs both the time and the cue number - in that order.
    /// </summary>
    TimedGo = 1 << 4,
    /// <summary>
    /// Set can be used to set the position of faders. It needs the fader number and page followed by the position.
    /// </summary>
    Set = 1 << 5,
    /// <summary>
    /// This can be used to trigger macros. The macro number needs to follow the command. Only macro 1 to 255 can be triggered.
    /// </summary>
    Fire = 1 << 6,
    /// <summary>
    /// This command can be used "Off" executors. This needs to followed by a cue number.
    /// </summary>
    GoOff = 1 << 7
}
