using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.Models;

/// <summary>
/// Represents an MA MSC packet. This is implemented as a union type, make sure to check the 
/// <see cref="command"/> field before reading the corresponding data field.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct MAMSCPacket
{
    [FieldOffset(0x0)] public byte deviceID;
    [FieldOffset(0x1)] public MSCCommandFormat commandFormat;
    [FieldOffset(0x2)] public MSCCommand command;

    [FieldOffset(0x4)] public MSCGoData goData;
    [FieldOffset(0x4)] public MSCStopData stopData;
    [FieldOffset(0x4)] public MSCResumeData resumeData;
    [FieldOffset(0x4)] public MSCTimedGoData timedGoData;
    [FieldOffset(0x4)] public MSCSetData setData;
    [FieldOffset(0x4)] public MSCFireData fireData;
    [FieldOffset(0x4)] public MSCGoOffData goOffData;

    private static readonly byte[] HEADER_BYTES = [0x47, 0x4D, 0x41, 0x00, 0x4D, 0x53, 0x43, 0x00];

    public static bool TryRead(ReadOnlySpan<byte> buffer, out MAMSCPacket packet)
    {
        packet = default;
        if (buffer.Length < 12)
            return false;

        // Check header
        int pos = 0;
        if (!buffer[..HEADER_BYTES.Length].SequenceEqual(HEADER_BYTES))
            return false;
        pos += HEADER_BYTES.Length;

        int len = BinaryPrimitives.ReadInt32LittleEndian(buffer[pos..]);
        pos += 4;

        // Check valid MSC sysex message
        if (buffer[pos++] != 0xf0 || buffer[pos++] != 0x7f)
            return false;
        packet.deviceID = buffer[pos++];
        if (buffer[pos++] != 2)
            return false;

        packet.commandFormat = (MSCCommandFormat)buffer[pos++];
        packet.command = (MSCCommand)buffer[pos++];

        // Check the last byte is an f7
        if (buffer[^1] != 0xf7)
            return false;
        // Trim the buffer to just the data section
        buffer = buffer[pos..^1];

        // Parse the data
        return packet.command switch
        {
            MSCCommand.Go => MSCGoData.TryRead(buffer, out packet.goData),
            MSCCommand.Stop => MSCStopData.TryRead(buffer, out packet.stopData),
            MSCCommand.Resume => MSCResumeData.TryRead(buffer, out packet.resumeData),
            MSCCommand.TimedGo => MSCTimedGoData.TryRead(buffer, out packet.timedGoData),
            MSCCommand.Set => MSCSetData.TryRead(buffer, out packet.setData),
            MSCCommand.Fire => MSCFireData.TryRead(buffer, out packet.fireData),
            MSCCommand.GoOff => MSCGoOffData.TryRead(buffer, out packet.goOffData),
            _ => false,
        };
    }

    public bool TryWrite(Span<byte> buffer)
    {
        // TODO:
        return false;
    }

    public readonly int ComputeSizeInBytes()
    {
        int size = 12; // header
        size += 7; // MIDI Sysex + MSC headers

        // TODO:
        size += command switch
        {
            MSCCommand.Unknown => 0,
            MSCCommand.Go => 0,
            MSCCommand.Stop => 0,
            MSCCommand.Resume => 0,
            MSCCommand.TimedGo => 0,
            MSCCommand.Set => 0,
            MSCCommand.Fire => 0,
            MSCCommand.GoOff => 0,
            _ => 0,
            //MSCCommand.Go =>
        };
        return size;
    }

    internal static bool TryReadQID(ReadOnlySpan<byte> buffer, out decimal qid)
    {
        qid = default;
        if (buffer.Length < 5)
            return false;

        return decimal.TryParse(buffer, null, out qid);
    }

    internal static bool TryReadExecutorAndPage(ReadOnlySpan<byte> buffer, out byte? executor, out byte? page)
    {
        executor = default;
        page = default;
        // Executor and page can be separated by a null or a period
        int sep = buffer.IndexOfAny((byte)0, (byte)0x2e);
        if (sep == -1)
            return false;

        if (!byte.TryParse(buffer[..sep], null, out byte exec))
            return false;
        executor = exec;

        if (!byte.TryParse(buffer[(sep + 1)..], null, out byte pg))
            return false;
        page = pg;

        return true;
    }
}

public struct MSCGoData
{
    public decimal qid;
    public byte? executor;
    public byte? page;

    public static bool TryRead(ReadOnlySpan<byte> buffer, out MSCGoData data)
    {
        data = default;

        if (!MAMSCPacket.TryReadQID(buffer, out data.qid))
            return false;

        int nextPos = buffer.IndexOf((byte)0);
        if (nextPos == -1)
            return true;

        buffer = buffer[(nextPos + 1)..];
        return MAMSCPacket.TryReadExecutorAndPage(buffer, out data.executor, out data.page);
    }
}

public struct MSCStopData
{
    public decimal? qid;
    public byte? executor;
    public byte? page;

    public static bool TryRead(ReadOnlySpan<byte> buffer, out MSCStopData data)
    {
        data = default;

        // A blank stop command is valid in this context
        if (buffer.IsEmpty)
            return true;

        if (!MAMSCPacket.TryReadQID(buffer, out var qid))
            return false;
        data.qid = qid;

        int nextPos = buffer.IndexOf((byte)0);
        if (nextPos == -1)
            return true;

        buffer = buffer[(nextPos + 1)..];
        return MAMSCPacket.TryReadExecutorAndPage(buffer, out data.executor, out data.page);
    }
}

public struct MSCResumeData
{
    public decimal? qid;
    public byte? executor;
    public byte? page;

    public static bool TryRead(ReadOnlySpan<byte> buffer, out MSCResumeData data)
    {
        data = default;

        // A blank resume command is valid in this context
        if (buffer.IsEmpty)
            return true;

        if (!MAMSCPacket.TryReadQID(buffer, out var qid))
            return false;
        data.qid = qid;

        int nextPos = buffer.IndexOf((byte)0);
        if (nextPos == -1)
            return true;

        buffer = buffer[(nextPos + 1)..];
        return MAMSCPacket.TryReadExecutorAndPage(buffer, out data.executor, out data.page);
    }
}

public struct MSCTimedGoData
{
    public decimal qid;
    public byte? executor;
    public byte? page;
    public MSCTime time;

    public static bool TryRead(ReadOnlySpan<byte> buffer, out MSCTimedGoData data)
    {
        data = default;

        if (!MemoryMarshal.TryRead(buffer, out data.time))
            return false;

        if (!MAMSCPacket.TryReadQID(buffer, out data.qid))
            return false;

        int nextPos = buffer.IndexOf((byte)0);
        if (nextPos == -1)
            return true;

        buffer = buffer[(nextPos + 1)..];
        return MAMSCPacket.TryReadExecutorAndPage(buffer, out data.executor, out data.page);
    }
}

[StructLayout(LayoutKind.Explicit)]
public struct MSCSetData
{
    [FieldOffset(0x0)] public byte fader;
    [FieldOffset(0x1)] public byte page;
    [FieldOffset(0x2)] public byte lowVal;
    [FieldOffset(0x3)] public byte highVal;

    public float FaderValue
    {
        readonly get
        {
            short v = (short)(lowVal | (highVal << 7));
            return v / (128 * 128f);
        }
        set
        {
            short v = (short)(value * (128 * 128));
            lowVal = (byte)(v & 0x7f);
            highVal = (byte)((v & 0x3f80) >> 7);
        }
    }

    public static bool TryRead(ReadOnlySpan<byte> buffer, out MSCSetData data)
    {
        return MemoryMarshal.TryRead(buffer, out data);
    }
}

[StructLayout(LayoutKind.Explicit)]
public struct MSCFireData
{
    [FieldOffset(0x0)] public byte macro;

    public static bool TryRead(ReadOnlySpan<byte> buffer, out MSCFireData data)
    {
        return MemoryMarshal.TryRead(buffer, out data);
    }
}

public struct MSCGoOffData
{
    public decimal qid;
    public byte? executor;
    public byte? page;

    public static bool TryRead(ReadOnlySpan<byte> buffer, out MSCGoOffData data)
    {
        data = default;

        if (!MAMSCPacket.TryReadQID(buffer, out data.qid))
            return false;

        int nextPos = buffer.IndexOf((byte)0);
        if (nextPos == -1)
            return true;

        buffer = buffer[(nextPos + 1)..];
        return MAMSCPacket.TryReadExecutorAndPage(buffer, out data.executor, out data.page);
    }
}

public enum MSCCommandFormat : byte
{
    Unknown = 0,
    GeneralLighting = 1,
    MovingLights = 2,
    All = 0x7f
}

public enum MSCCommand : byte
{
    Unknown = 0,
    /// <summary>
    /// This is the same as a Goto command in grandMA2. It needs to be followed by a cue number.
    /// </summary>
    Go = 1,
    /// <summary>
    /// This is the same as a Pause command in grandMA2. This can be followed by a cue number.
    /// </summary>
    Stop = 2,
    /// <summary>
    /// This will "un-plause" a cue. If a specific cue has been paused, then the cue number needs to be specified with this command.
    /// </summary>
    Resume = 3,
    /// <summary>
    /// This can be used to perform a Goto with a specific fade time. It needs both the time and the cue number - in that order.
    /// </summary>
    TimedGo = 4,

    /// <summary>
    /// Set can be used to set the position of faders. It needs the fader number and page followed by the position.
    /// </summary>
    Set = 6,
    /// <summary>
    /// This can be used to trigger macros. The macro number needs to follow the command. Only macro 1 to 255 can be triggered.
    /// </summary>
    Fire = 7,

    /// <summary>
    /// This command can be used "Off" executors. This needs to followed by a cue number.
    /// </summary>
    GoOff = 11
}

[StructLayout(LayoutKind.Explicit)]
public struct MSCTime
{
    [FieldOffset(0x0)] public byte hours;
    [FieldOffset(0x1)] public byte minutes;
    [FieldOffset(0x2)] public byte seconds;
    [FieldOffset(0x3)] public byte frames;
    [FieldOffset(0x4)] public byte fraction;
}
