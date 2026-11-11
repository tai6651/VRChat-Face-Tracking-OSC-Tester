using System.Buffers.Binary;

namespace OscTester;

internal sealed record CameraOutputFrame(float[] Pose, bool Enabled);

internal sealed class CameraOscOutput
{
    private const string Prefix = "OscPoser/Camera/";
    private readonly Action<byte[]> send;
    private readonly byte[] disable = OscPacket.CreateParameterMessage(Prefix + "isEnable", 'F');
    private readonly byte[] bundle;
    private readonly int[] arguments;
    private readonly int enabledTag;
    private CameraOutputFrame? lastFrame;
    private int ticksUntilRefresh;

    public CameraOscOutput(Action<byte[]> send)
    {
        this.send = send;
        string[] names = ["X", "Y", "Z", "Yaw", "Pitch", "Roll", "Fov"];
        var messages = names.Select(name =>
            OscPacket.CreateParameterMessage(Prefix + name, 'f', sizeof(float))).ToArray();
        bundle = new byte[16 + messages.Sum(message => sizeof(int) + message.Length) + sizeof(int) + disable.Length];
        "#bundle\0"u8.CopyTo(bundle);
        bundle[15] = 1; // OSC immediate timetag
        arguments = new int[messages.Length];
        int offset = 16;
        for (int i = 0; i < messages.Length; i++)
        {
            byte[] message = messages[i];
            BinaryPrimitives.WriteInt32BigEndian(bundle.AsSpan(offset), message.Length);
            offset += sizeof(int);
            message.CopyTo(bundle, offset);
            offset += message.Length;
            arguments[i] = offset - sizeof(float);
        }
        // Append the gate after the pose so the receiver starts rendering at the current frame.
        BinaryPrimitives.WriteInt32BigEndian(bundle.AsSpan(offset), disable.Length);
        disable.CopyTo(bundle, offset + sizeof(int));
        enabledTag = bundle.Length - 3;
    }

    public void Update(CameraOutputFrame? frame)
    {
        bool unchanged = frame is null ? lastFrame is null
            : lastFrame is not null && frame.Enabled == lastFrame.Enabled && frame.Pose.AsSpan().SequenceEqual(lastFrame.Pose);
        if (--ticksUntilRefresh > 0 && unchanged)
            return;
        if (frame is null)
            send(disable);
        else
        {
            for (int i = 0; i < arguments.Length; i++)
                BinaryPrimitives.WriteSingleBigEndian(bundle.AsSpan(arguments[i]), frame.Pose[i]);
            bundle[enabledTag] = (byte)(frame.Enabled ? 'T' : 'F');
            send(bundle);
        }
        lastFrame = frame;
        ticksUntilRefresh = OscSession.TargetRate;
    }

    public void Release() => send(disable);
}
