using System.Net;
using System.Net.Sockets;

namespace OscTester;

internal sealed class OscSession(
    IPEndPoint listenEndpoint, IPEndPoint destination, Func<byte[]> getPacket) : IDisposable
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

        Task sending = Task.Run(() => timer.Run(TargetRate, stop.Token, () =>
        {
            sender.SendTo(getPacket(), destination);
            Interlocked.Increment(ref sentPackets);
        }));
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
