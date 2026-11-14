using System.Net;
using System.Net.Sockets;

namespace OscTester;

internal sealed record BodyOutputFrame(float[]? HumanoidPose, byte[]? MmdAnywherePacket, bool MmdAnywhereEnabled);

internal sealed class OscSession(
    IPEndPoint listenEndpoint, IPEndPoint destination, Func<byte[]> getPacket,
    Func<BodyOutputFrame> getBodyFrame, Func<CameraOutputFrame?> getCameraFrame) : IDisposable
{
    public const int TargetRate = 60;

    private readonly CancellationTokenSource stop = new();
    private TrackingStatus status = new();
    private long sentPackets;
    private long receivedPackets;

    public TrackingStatus Status => Volatile.Read(ref status);
    public long SentPackets => Interlocked.Read(ref sentPackets);
    public long ReceivedPackets => Interlocked.Read(ref receivedPackets);

    public async Task RunAsync()
    {
        using var listener = new UdpClient(listenEndpoint.AddressFamily) { ExclusiveAddressUse = true };
        listener.Client.Bind(listenEndpoint);
        using var sender = new Socket(destination.AddressFamily, SocketType.Dgram, ProtocolType.Udp)
        {
            SendTimeout = 100
        };
        using var timer = new FrameTimer();

        var humanoid = new HumanoidOscOutput(packet => sender.SendTo(packet, destination));
        var camera = new CameraOscOutput(packet => sender.SendTo(packet, destination));
        byte[] enableMmd = OscPacket.CreateParameterMessage(MmdAnywhereLayout.EnableParameter, 'T');
        byte[] disableMmd = OscPacket.CreateParameterMessage(MmdAnywhereLayout.EnableParameter, 'F');
        bool mmdSelected = false;
        bool mmdEnabled = false;
        int ticksUntilRefresh = 0;
        Task sending = Task.Run(() =>
        {
            try
            {
                timer.Run(TargetRate, stop.Token, () =>
                {
                    BodyOutputFrame body = getBodyFrame();
                    bool selected = body.MmdAnywherePacket is not null;
                    bool enabled = body.MmdAnywhereEnabled;
                    bool updateGate = selected != mmdSelected || enabled != mmdEnabled ||
                        (selected && --ticksUntilRefresh <= 0);
                    if (updateGate && !enabled)
                        sender.SendTo(disableMmd, destination);
                    if (mmdSelected && !selected)
                        sender.SendTo(MmdAnywherePose.IdlePacket, destination);
                    sender.SendTo(body.MmdAnywherePacket ?? getPacket(), destination);
                    if (updateGate && enabled)
                        sender.SendTo(enableMmd, destination);
                    if (updateGate)
                        ticksUntilRefresh = TargetRate;
                    Interlocked.Increment(ref sentPackets);
                    humanoid.Update(body.HumanoidPose);
                    mmdSelected = selected;
                    mmdEnabled = enabled;
                    camera.Update(getCameraFrame());
                });
            }
            finally
            {
                if (mmdSelected)
                {
                    try { sender.SendTo(disableMmd, destination); }
                    catch (SocketException) { }
                    try { sender.SendTo(MmdAnywherePose.IdlePacket, destination); }
                    catch (SocketException) { }
                }
                try { camera.Release(); }
                catch (SocketException) { }
                try { humanoid.Release(); }
                catch (SocketException) { } // A closed or unreachable destination cannot receive the reset.
            }
        });
        Task receiving = ReceiveAsync(listener);

        await Task.WhenAny(sending, receiving).ConfigureAwait(false);
        stop.Cancel();
        await Task.WhenAll(sending, receiving).ConfigureAwait(false);
    }

    public void Stop() => stop.Cancel();
    public void Dispose() => stop.Dispose();


    private async Task ReceiveAsync(UdpClient listener)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                UdpReceiveResult result = await listener.ReceiveAsync(stop.Token).ConfigureAwait(false);
                Volatile.Write(ref status, OscPacket.ReadStatus(result.Buffer, status));
                Interlocked.Increment(ref receivedPackets);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}
