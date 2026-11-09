using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace OscTester;

public partial class MainWindow : Window
{
    private readonly byte[] values = new byte[OscPacket.FacePayloadSize];
    private readonly TrackingParameter[] parameters;
    private readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private byte[] packet;
    private OscSession? session;
    private long rateTimestamp;
    private long ratePacketCount;
    private double measuredRate;

    public MainWindow()
    {
        InitializeComponent();
        packet = OscPacket.CreateFacePacket(values);
        parameters = TrackingKeys.Names.Select((_, index) => new TrackingParameter(index, ChangeValue)).ToArray();
        Groups.ItemsSource = new ParameterGroup[]
        {
            new("Quick test", parameters.Where(parameter => parameter.IsFeatured).ToArray()),
            new("Eyes", parameters[..18]),
            new("Eyebrows", parameters[18..26]),
            new("Nose & cheeks", [.. parameters[26..36], .. parameters[62..64]]),
            new("Jaw & mouth", [.. parameters[36..62], .. parameters[64..86]]),
            new("Tongue", parameters[86..98]),
            new("Throat & neck", parameters[98..])
        };
        Groups.SelectedIndex = 0;
        Player.PoseChanged += ApplyAnimationPose;
        statusTimer.Tick += (_, _) => UpdateStatus();
        Closed += (_, _) =>
        {
            Player.Close();
            statusTimer.Stop();
            session?.Stop();
        };
    }

    private void ChangeValue(int index, byte value)
    {
        values[index] = value;
        Volatile.Write(ref packet, OscPacket.CreateFacePacket(values));
    }

    private void ApplyAnimationPose(byte[] pose, int[] channels)
    {
        bool changed = false;
        foreach (int index in channels)
        {
            byte value = pose[index];
            changed |= values[index] != value;
            values[index] = value;
            parameters[index].ApplyPlaybackValue(value);
        }
        if (changed)
            Volatile.Write(ref packet, OscPacket.CreateFacePacket(values));
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        Array.Clear(values);
        Volatile.Write(ref packet, OscPacket.CreateFacePacket(values));
        foreach (TrackingParameter parameter in parameters)
            parameter.Reset();
    }

    private async void StartStop_Click(object sender, RoutedEventArgs e)
    {
        if (session is not null)
        {
            StartStop.IsEnabled = false;
            session.Stop();
            return;
        }

        if (!TryReadEndpoint(ListenIp, ListenPort, "Listen", out IPEndPoint listenEndpoint)
            || !TryReadEndpoint(VrChatIp, VrChatPort, "VRChat", out IPEndPoint destination))
            return;

        if (destination.Address.Equals(IPAddress.Any) || destination.Address.Equals(IPAddress.IPv6Any))
        {
            StreamStatus.Text = "Enter a specific destination in the VRChat IP field.";
            VrChatIp.Focus();
            return;
        }

        using var activeSession = new OscSession(listenEndpoint, destination, () => Volatile.Read(ref packet));
        session = activeSession;
        NetworkFields.IsEnabled = false;
        StartStop.Content = "Stop streaming";
        rateTimestamp = Stopwatch.GetTimestamp();
        ratePacketCount = 0;
        measuredRate = 0;
        StreamStatus.Text = $"Streaming to {destination}  ·  Listening on {listenEndpoint}";
        UpdateStatus();
        statusTimer.Start();

        try
        {
            await activeSession.RunAsync();
            StreamStatus.Text = "Streaming stopped";
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            StreamStatus.Text = $"Listen endpoint {listenEndpoint} is already in use. Close the other listener or choose a different port.";
        }
        catch (Win32Exception ex)
        {
            StreamStatus.Text = $"OSC session ended. {ex.Message}";
        }
        finally
        {
            statusTimer.Stop();
            UpdateStatus();
            session = null;
            NetworkFields.IsEnabled = true;
            StartStop.Content = "Start streaming";
            StartStop.IsEnabled = true;
        }
    }

    private bool TryReadEndpoint(TextBox ip, TextBox port, string label, out IPEndPoint endpoint)
    {
        endpoint = null!;
        if (!IPAddress.TryParse(ip.Text.Trim(), out IPAddress? address))
        {
            StreamStatus.Text = $"Enter a valid {label} IP address.";
            ip.Focus();
            return false;
        }

        if (!ushort.TryParse(port.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ushort number)
            || number == 0)
        {
            StreamStatus.Text = $"Enter a {label} UDP port from 1 to 65535.";
            port.Focus();
            return false;
        }

        endpoint = new IPEndPoint(address, number);
        return true;
    }

    private void UpdateStatus()
    {
        if (session is null)
            return;

        long sent = session.SentPackets;
        long received = session.ReceivedPackets;
        double seconds = Stopwatch.GetElapsedTime(rateTimestamp).TotalSeconds;
        if (seconds >= 1)
        {
            measuredRate = (sent - ratePacketCount) / seconds;
            ratePacketCount = sent;
            rateTimestamp = Stopwatch.GetTimestamp();
        }

        Traffic.Text = $"{measuredRate:F0} Hz  /  {sent:N0} sent  /  {received:N0} received";
        TrackingStatus status = session.Status;
        string avatar = status.AvatarId ?? "Waiting for /avatar/change";
        if (AvatarId.Text != avatar)
            AvatarId.Text = avatar;
        AvatarStatus.Text = $"Native {Display(status.AvatarSupport)}    Face {Display(status.Face)}    Eyes {Display(status.Eye)}";
    }

    private static string Display(bool? state) => state switch
    {
        true => "Yes",
        false => "No",
        null => "—"
    };

    private sealed record ParameterGroup(string Title, TrackingParameter[] Parameters);
}
