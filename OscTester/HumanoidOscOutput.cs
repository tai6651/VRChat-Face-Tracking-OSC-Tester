using System.Buffers.Binary;

namespace OscTester;

internal sealed class HumanoidOscOutput(Action<byte[]> send)
{
    private const string Prefix = "OscPoser/";
    private readonly byte[] enable = OscPacket.CreateParameterMessage(Prefix + "isEnabled", 'T');
    private readonly byte[] disable = OscPacket.CreateParameterMessage(Prefix + "isEnabled", 'F');
    private readonly byte[][] channels = Enumerable.Range(0, HumanoidChannels.PoseSize)
        .Select(i => OscPacket.CreateParameterMessage(Prefix + HumanoidChannels.ParameterName(i), 'f', sizeof(float)))
        .ToArray();
    private float[]? lastPose;
    private bool wasArmed;
    private int ticksUntilRefresh;

    // Null opts out. An empty frame keeps the gate off while the checkbox is checked.
    public void Update(float[]? frame)
    {
        bool armed = frame is not null;
        bool refresh = --ticksUntilRefresh <= 0;
        bool wasActive = lastPose is not null;
        bool active = frame is { Length: > 0 };

        if (frame is { Length: > 0 } pose)
        {
            for (int i = 0; i < channels.Length; i++)
            {
                if (refresh || lastPose is null || pose[i] != lastPose[i])
                {
                    byte[] message = channels[i];
                    BinaryPrimitives.WriteSingleBigEndian(message.AsSpan(message.Length - sizeof(float)), pose[i]);
                    send(message);
                }
            }
        }

        if ((armed && (refresh || !wasArmed || active != wasActive)) || (!armed && wasActive))
            send(active ? enable : disable);

        lastPose = active ? frame : null;
        wasArmed = armed;
        if (refresh)
            ticksUntilRefresh = OscSession.TargetRate;
    }

    public void Release()
    {
        if (lastPose is not null)
            send(disable);
    }
}
