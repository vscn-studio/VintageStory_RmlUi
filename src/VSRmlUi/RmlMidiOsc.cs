using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VSRmlUi;

public readonly record struct MidiMessage(byte Status, byte Data1, byte Data2)
{
    public int Channel => Status & 0x0f;
    public int Command => Status & 0xf0;
    public static MidiMessage NoteOn(int channel, int note, int velocity)
    {
        if (channel is < 0 or > 15 || note is < 0 or > 127 || velocity is < 0 or > 127) throw new ArgumentOutOfRangeException(nameof(note));
        return new((byte)(0x90 | channel), (byte)note, (byte)velocity);
    }
    public static MidiMessage ControlChange(int channel, int controller, int value)
    {
        if (channel is < 0 or > 15 || controller is < 0 or > 127 || value is < 0 or > 127) throw new ArgumentOutOfRangeException(nameof(controller));
        return new((byte)(0xb0 | channel), (byte)controller, (byte)value);
    }
}

/// <summary>Host supplies a platform MIDI port implementation; this interface owns no device.</summary>
public interface IMidiPort
{
    event Action<MidiMessage>? Received;
    void Send(MidiMessage message);
}

public sealed record OscMessage(string Address, IReadOnlyList<object> Arguments);

public static class OscCodec
{
    public static byte[] Encode(OscMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Address) || !message.Address.StartsWith('/')) throw new ArgumentException("OSC address must start with '/'.");
        using var stream = new MemoryStream();
        WriteString(stream, message.Address);
        var tags = new StringBuilder(",");
        foreach (var arg in message.Arguments)
            tags.Append(arg switch { int => 'i', float => 'f', string => 's', _ => throw new ArgumentException("Only OSC int32, float32 and strings are supported.") });
        WriteString(stream, tags.ToString());
        Span<byte> bytes = stackalloc byte[4];
        foreach (var arg in message.Arguments)
        {
            switch (arg)
            {
                case int value: BinaryPrimitives.WriteInt32BigEndian(bytes, value); stream.Write(bytes); break;
                case float value: BinaryPrimitives.WriteInt32BigEndian(bytes, BitConverter.SingleToInt32Bits(value)); stream.Write(bytes); break;
                case string value: WriteString(stream, value); break;
            }
        }
        return stream.ToArray();
    }

    public static OscMessage Decode(ReadOnlySpan<byte> packet)
    {
        int offset = 0;
        string address = ReadString(packet, ref offset), tags = ReadString(packet, ref offset);
        if (!address.StartsWith('/') || !tags.StartsWith(',')) throw new FormatException("Invalid OSC message.");
        var args = new List<object>();
        foreach (char tag in tags.AsSpan(1))
        {
            if (tag == 's') args.Add(ReadString(packet, ref offset));
            else if (tag is 'i' or 'f')
            {
                if (offset + 4 > packet.Length) throw new FormatException("Truncated OSC argument.");
                int bits = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(offset, 4)); offset += 4;
                args.Add(tag == 'i' ? (object)bits : BitConverter.Int32BitsToSingle(bits));
            }
            else throw new FormatException("Unsupported OSC type tag.");
        }
        return new(address, args);
    }

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value); stream.Write(bytes); stream.WriteByte(0);
        while (stream.Position % 4 != 0) stream.WriteByte(0);
    }

    private static string ReadString(ReadOnlySpan<byte> packet, ref int offset)
    {
        int end = packet[offset..].IndexOf((byte)0);
        if (end < 0) throw new FormatException("Unterminated OSC string.");
        string value = Encoding.UTF8.GetString(packet.Slice(offset, end));
        offset = (offset + end + 4) & ~3;
        if (offset > packet.Length) throw new FormatException("Truncated OSC string.");
        return value;
    }
}

/// <summary>UDP OSC transport; received callbacks run on the caller of ReceiveAsync.</summary>
public sealed class OscUdpPort : IDisposable
{
    private readonly UdpClient client;
    public int LocalPort => ((IPEndPoint)client.Client.LocalEndPoint!).Port;
    public OscUdpPort(int localPort = 0) => client = new UdpClient(new IPEndPoint(IPAddress.Loopback, localPort));
    public async Task SendAsync(OscMessage message, IPEndPoint endpoint, CancellationToken cancellationToken = default)
        => await client.SendAsync(OscCodec.Encode(message), endpoint, cancellationToken);
    public async Task<OscMessage> ReceiveAsync(CancellationToken cancellationToken = default)
        => OscCodec.Decode((await client.ReceiveAsync(cancellationToken)).Buffer);
    public void Dispose() => client.Dispose();
}

public static class RmlMidiOscMonitor
{
    public static string Markup(string id)
        => $"<div id='{System.Net.WebUtility.HtmlEncode(id)}' class='vs-midi-osc'><div class='vs-midi-osc-header'><span>MIDI</span><span>OSC</span></div><div id='{System.Net.WebUtility.HtmlEncode(id)}-log' class='vs-midi-osc-log'>Waiting for messages</div></div>";
    public static void Show(RmlDocument document, string id, string source, string message)
        => document.GetElementById(id + "-log")!.Text = source + "  " + message;
}
