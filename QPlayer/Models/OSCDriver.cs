using Rug.Osc;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static QPlayer.ViewModels.MainViewModel;

namespace QPlayer.Models;

internal class OSCDriver : IDisposable
{
    //public ConcurrentQueue<OscPacket> RXMessages { get; private set; } = [];
    public event Action<OscPacket>? OnRXMessage;
    public event Action<OscPacket>? OnTXMessage;

    private OscReceiver? oscReceiver;
    private OscSender? oscSender;

    private IPEndPoint? rxIP;
    private IPEndPoint? txIP;

    private readonly OSCAddressRouter router = new();

    public bool OSCConnect(IPAddress nicAddress, int rxPort, int txPort)
    {
        //RXMessages.Clear();

        rxIP = new IPEndPoint(nicAddress ?? IPAddress.Any, rxPort);
        txIP = new IPEndPoint(nicAddress ?? IPAddress.Broadcast, txPort);

        Log($"Connecting to OSC... rx={rxIP} tx={txIP}");

        try
        {
            Dispose();

            oscReceiver = new(rxIP.Address, rxIP.Port);
            oscReceiver.Connect();

            oscSender = new(txIP.Address, 0, txIP.Port);
            oscSender.Connect();

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

    /// <summary>
    /// Asynchronously sends an OSC packet.
    /// </summary>
    /// <param name="packet"></param>
    public void SendMessage(OscPacket packet)
    {
        OnTXMessage?.Invoke(packet);
        oscSender?.Send(packet);
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
        while (oscReceiver?.State == OscSocketState.Connected)
        {
            try
            {
                var pkt = oscReceiver.Receive();
                //RXMessages.Enqueue(pkt);
                OnRXMessage?.Invoke(pkt);
                if (pkt is OscMessage msg && msg.Address.Length > 0)
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
    public Dictionary<string, OSCAddressRouter>? children;
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
                router.children.Add(part,  nRouter);
                router = nRouter;
            }
        }
        if (syncContext != null)
            router.OnRXMessage += msg => syncContext.Post(_=>handler(msg), null);
        else
            router.OnRXMessage += handler;
    }

    public void ReceiveMessage(OscMessage message, ReadOnlySpan<char> addressSpan)
    {
        OnRXMessage?.Invoke(message);

        if (children != null)
        {
            int slashPos = addressSpan.IndexOf('/');
            string key;
            if (slashPos != -1)
                key = new(addressSpan[..slashPos]);
            else
                key = new(addressSpan);

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
