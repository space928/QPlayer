﻿using Mathieson.Dev;
using OscCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using QPlayer.Utilities;
using System.Collections;
using System.Runtime.CompilerServices;
using static QPlayer.ViewModels.MainViewModel;
using System.Buffers;
using System.Runtime.InteropServices;

namespace QPlayer.Models;

public class OSCDriver : IDisposable
{
    //public ConcurrentQueue<OscPacket> RXMessages { get; private set; } = [];
    public event Action<OscMessage>? OnRXMessage;
    public event Action<OscMessage>? OnTXMessage;

    private UdpClient? oscReceiver;
    private UdpClient? oscSender;

    private IPEndPoint? rxIP;
    private IPEndPoint? txIP;
    private IPAddress? broadcastIP;

    private readonly ArrayPool<byte> byteBufferPool = ArrayPool<byte>.Create();

    private readonly OSCAddressRouter router = new();

    public bool OSCConnect(IPAddress nicAddress, IPAddress subnet, int rxPort, int txPort)
    {
        broadcastIP = MakeBroadcastAddress(nicAddress, subnet);

        rxIP = new IPEndPoint(nicAddress, rxPort);
        //txIP = new IPEndPoint(IPAddress.Broadcast, txPort);
        txIP = new IPEndPoint(broadcastIP, txPort);

        Log($"Connecting to OSC... rx={rxIP} tx={txIP}");

        try
        {
            Dispose();

            oscReceiver = new(new IPEndPoint(rxIP.Address, 0));
            //oscReceiver
            //oscReceiver.Connect()

            oscSender = new(new IPEndPoint(nicAddress, 0));
            //oscSender.Connect(txIP);

            Task.Run(OSCRXThread);
        }
        catch (Exception e)
        {
            Log($"Failed to connect to OSC port: {e}", LogLevel.Error);
            return false;
        }

        Log("Connected to OSC network!");

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
    /// Asynchronously sends an OSC packet.
    /// </summary>
    /// <param name="packet">The message to send.</param>
    /// <param name="remoteEndPoint">The endpoint to send the message to.</param>
    public void SendMessage(in OscMessage packet, IPEndPoint? remoteEndPoint = null)
    {
        SendMessageAsync(packet, remoteEndPoint)
            .ContinueWith(x => Log($"Error while sending OSC message: '{x.Exception?.Message}'\n{x.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <inheritdoc cref="SendMessage(OscMessage, IPEndPoint?)"/>
    public async Task SendMessageAsync(OscMessage packet, IPEndPoint? remoteEndPoint = null)
    {
        OnTXMessage?.Invoke(packet);

        var buff = byteBufferPool.Rent(packet.SizeInBytes);
        int len = packet.Write(buff, 0);
        var task = oscSender?.SendAsync(buff, len, remoteEndPoint ?? txIP);

        if (task != null) await task;
        byteBufferPool.Return(buff);
    }

    /// <summary>
    /// Subscribes an event handler to OSC messages following a specified pattern.
    /// </summary>
    /// <remarks>
    /// Address patterns are of the form: "/foo/?/bar"
    /// <br/>
    /// Where '?' indicates a wildcard which matches any single address part.
    /// </remarks>
    /// <param name="pattern">The adddress pattern to match.</param>
    /// <param name="handler">The event handler to fire when a matching message is received.</param>
    public void Subscribe(string pattern, Action<OscMessage> handler, SynchronizationContext? syncContext = null)
    {
        OSCAddressRouter.Subscribe(router, pattern, handler, syncContext);
    }

    private void OSCRXThread()
    {
        IPEndPoint? remoteEndPoint = null;
        IPEndPoint? endPointAny = new(IPAddress.Any, rxIP?.Port ?? 0);
        IPEndPoint? endPointAnyV6 = new(IPAddress.IPv6Any, rxIP?.Port ?? 0);
        var recvBuff = new byte[0x10000];
        while (oscReceiver?.Client?.Connected ?? false)
        {
            try
            {
                EndPoint tempRemoteEP = oscReceiver.Client.AddressFamily == AddressFamily.InterNetwork ? endPointAny : endPointAnyV6;
                int received = oscReceiver.Client.ReceiveFrom(recvBuff, recvBuff.Length, SocketFlags.None, ref tempRemoteEP);
                remoteEndPoint = (IPEndPoint)tempRemoteEP;

                var pkt = OscPacket.Read(recvBuff, 0, received, remoteEndPoint);

                // TODO: Support bundles
                if (pkt.Kind == OscPacketKind.OscBundle)
                    continue;

                var msg = pkt.OscMessage;
                //RXMessages.Enqueue(pkt);
                OnRXMessage?.Invoke(msg);
                if (!string.IsNullOrEmpty(msg.Address))
                    router.ReceiveMessage(msg, msg.Address.AsSpan(1));

                //Log($"Recv osc msg: {pkt}", LogLevel.Debug);
            }
            catch (Exception e)
            {
                Log($"OSC Network connection lost: {e}", LogLevel.Warning);
            }
        }
    }

    public void Dispose()
    {
        /*if (OnRXMessage != null)
            foreach (var d in OnRXMessage.GetInvocationList())
                OnRXMessage -= d as Action<OscPacket>;
        if (OnTXMessage != null)
            foreach (var d in OnTXMessage.GetInvocationList())
                OnTXMessage -= d as Action<OscPacket>;*/

        oscReceiver?.Dispose();
        oscSender?.Dispose();
        oscReceiver = null;
        oscSender = null;
    }
}

public class OSCAddressRouter
{
    public StringDict<OSCAddressRouter>? children;
    public event Action<OscMessage>? OnRXMessage;

    public static void Subscribe(OSCAddressRouter root, string pattern, Action<OscMessage> handler, SynchronizationContext? syncContext = null)
    {
        var parts = pattern.Split('/');
        OSCAddressRouter router = root;
        for (int i = 1; i < parts.Length; i++)
        {
            string part = parts[i];
            if (router.children?.TryGetValue(part, out var child) ?? false)
            {
                router = child;
            }
            else
            {
                router.children ??= [];
                var nRouter = new OSCAddressRouter();
                router.children.Add(part, nRouter);
                router = nRouter;
            }
        }
        if (syncContext != null)
            router.OnRXMessage += msg => syncContext.Post(_ => handler(msg), null);
        else
            router.OnRXMessage += handler;
    }

    public void ReceiveMessage(OscMessage message, ReadOnlySpan<char> addressSpan)
    {
        OnRXMessage?.Invoke(message);

        if (children != null)
        {
            int slashPos = addressSpan.IndexOf('/');
            ReadOnlySpan<char> key;
            if (slashPos != -1)
                key = addressSpan[..slashPos];
            else
                key = addressSpan;

            if (children.TryGetValue(key, out var child))
                child.ReceiveMessage(message, addressSpan[(slashPos + 1)..]);
            if (children.TryGetValue("?", out var child1))
                child1.ReceiveMessage(message, addressSpan[(slashPos + 1)..]);
        }
    }
}

public static class OSCMessageParser
{
    /// <summary>
    /// Parses a string representing an OSC message into an address and a list of arguments.<br/>
    /// Arguments must be separated by spaces and are parsed automatically
    /// Supports:
    ///  - strings -> Surrounded by double quotes
    ///  - ints
    ///  - floats
    ///  - bools
    ///  - blobs -> Surrounded by backticks
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    public static (string address, object[] args) ParseOSCMessage(string message)
    {
        ReadOnlyMemory<char> msg = message.AsMemory();
        int argsStart = message.IndexOf(' ');
        var args = new List<object>();
        string address = message;
        if (argsStart != -1)
        {
            address = message[..argsStart];
            var strArgs = message[(argsStart + 1)..].Split(' ');
            for (int i = 0; i < strArgs.Length; i++)
            {
                if (bool.TryParse(strArgs[i], out var bVal))
                    args.Add(bVal);
                else if (int.TryParse(strArgs[i], out var iVal))
                    args.Add(iVal);
                else if (float.TryParse(strArgs[i], out var fVal))
                    args.Add(fVal);
                else if (strArgs[i].Length > 0 && strArgs[i][0] == '\"')
                {
                    if (strArgs[i].Length > 1 && strArgs[i][^1] == '\"')
                    {
                        args.Add(strArgs[i][1..^1]);
                    }
                    else
                    {
                        // String must have spaces in it, search for the next arg that ends in a double quote
                        StringBuilder sb = new(strArgs[i][1..]);
                        do
                        {
                            i++;
                            sb.Append(' ');
                            sb.Append(strArgs[i]);
                        } while (i < strArgs.Length && strArgs[i][^1] != '\"');

                        if (strArgs[i][^1] != '\"')
                            throw new ArgumentException($"Unparsable OSC argument, string is not closed: {sb.ToString()}");

                        args.Add(sb.ToString());
                    }
                }
                else if (strArgs[i].Length > 3 && strArgs[i][0] == '`' && strArgs[i][^1] == '`')
                {
                    args.Add(StringToByteArrayFastest(strArgs[i][1..^1]));
                }
                else
                {
                    throw new ArgumentException($"Unparsable OSC argument encountered: {strArgs[i]}");
                }
            }
        }

        return (address, args.ToArray());
    }

    // https://stackoverflow.com/a/9995303
    private static byte[] StringToByteArrayFastest(string hex)
    {
        if (hex.Length % 2 == 1)
            throw new Exception("The binary key cannot have an odd number of digits");

        byte[] arr = new byte[hex.Length >> 1];

        for (int i = 0; i < hex.Length >> 1; ++i)
        {
            arr[i] = (byte)((GetHexVal(hex[i << 1]) << 4) + (GetHexVal(hex[(i << 1) + 1])));
        }

        return arr;
    }

    private static int GetHexVal(char hex)
    {
        int val = (int)hex;
        //For uppercase A-F letters:
        //return val - (val < 58 ? 48 : 55);
        //For lowercase a-f letters:
        //return val - (val < 58 ? 48 : 87);
        //Or the two combined, but a bit slower:
        return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
    }
}

/*public class OscSenderTargetted : OscSocket
{
    public override OscSocketType OscSocketType => throw new NotImplementedException();

    protected override void OnClosing()
    {
        throw new NotImplementedException();
    }

    protected override void OnConnect()
    {
        throw new NotImplementedException();
    }
}*/
