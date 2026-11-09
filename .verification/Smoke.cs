using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OscTester;

static class Smoke
{
    [STAThread]
    static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try { await Verify(); app.Shutdown(0); }
            catch (Exception e) { Console.Error.WriteLine(e); app.Shutdown(1); }
        };
        return app.Run();
    }

    static async Task Verify()
    {
        var window = new MainWindow { ShowInTaskbar = false, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000 };
        window.Show();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        T Get<T>(string name) => (T)window.FindName(name);
        var listen = Get<TextBox>("ListenPort");
        var destination = Get<TextBox>("VrChatPort");
        var status = Get<TextBlock>("StreamStatus");
        var start = Get<Button>("StartStop");
        var tabs = Get<TabControl>("Groups");
        Check(listen.Text == "9001" && destination.Text == "9000"
            && Get<TextBox>("ListenIp").Text == "127.0.0.1" && Get<TextBox>("VrChatIp").Text == "127.0.0.1", "default endpoints");
        string[] expected = "EyeLookOutRight EyeLookInRight EyeLookUpRight EyeLookDownRight EyeLookOutLeft EyeLookInLeft EyeLookUpLeft EyeLookDownLeft EyeClosedRight EyeClosedLeft EyeDilationRight EyeDilationLeft EyeConstrictRight EyeConstrictLeft EyeSquintRight EyeSquintLeft EyeWideRight EyeWideLeft BrowPinchRight BrowPinchLeft BrowLowererRight BrowLowererLeft BrowInnerUpRight BrowInnerUpLeft BrowOuterUpRight BrowOuterUpLeft NasalDilationRight NasalDilationLeft NasalConstrictRight NasalConstrictLeft CheekSquintRight CheekSquintLeft CheekPuffRight CheekPuffLeft CheekSuckRight CheekSuckLeft JawOpen JawRight JawLeft JawForward JawBackward JawClench JawMandibleRaise MouthClosed LipSuckUpperRight LipSuckUpperLeft LipSuckLowerRight LipSuckLowerLeft LipSuckCornerRight LipSuckCornerLeft LipFunnelUpperRight LipFunnelUpperLeft LipFunnelLowerRight LipFunnelLowerLeft LipPuckerUpperRight LipPuckerUpperLeft LipPuckerLowerRight LipPuckerLowerLeft MouthUpperUpRight MouthUpperUpLeft MouthUpperDeepenRight MouthUpperDeepenLeft NoseSneerRight NoseSneerLeft MouthLowerDownRight MouthLowerDownLeft MouthUpperRight MouthUpperLeft MouthLowerRight MouthLowerLeft MouthCornerPullRight MouthCornerPullLeft MouthCornerSlantRight MouthCornerSlantLeft MouthFrownRight MouthFrownLeft MouthStretchRight MouthStretchLeft MouthDimpleRight MouthDimpleLeft MouthRaiserUpper MouthRaiserLower MouthPressRight MouthPressLeft MouthTightenerRight MouthTightenerLeft TongueOut TongueUp TongueDown TongueRight TongueLeft TongueRoll TongueBendDown TongueCurlUp TongueSquish TongueFlat TongueTwistRight TongueTwistLeft SoftPalateClose ThroatSwallow NeckFlexRight NeckFlexLeft".Split(' ');
        var keyType = typeof(MainWindow).Assembly.GetType("OscTester.TrackingKeys")!;
        Check(((string[])keyType.GetField("Names")!.GetValue(null)!).SequenceEqual(expected), "exact official 102-key order");
        string[] Keys(object group) =>
            ((Array)group.GetType().GetProperty("Parameters")!.GetValue(group)!).Cast<object>()
                .Select(row => (string)row.GetType().GetProperty("Name")!.GetValue(row)!).ToArray();
        Check(tabs.Items.Cast<object>().Skip(1).SelectMany(Keys).Order().SequenceEqual(expected.Order()), "groups contain every key once");
        string[] quick = ["EyeClosedRight", "EyeClosedLeft", "JawOpen"];
        Check(Keys(tabs.Items[0]).SequenceEqual(quick), "quick test key order");
        var labels = Tree<TextBlock>(tabs).Where(tb => quick.Contains(tb.Text)).ToArray();
        Check(labels.Length == 3 && labels.All(tb => tb.FontWeight == FontWeights.Bold), "bold quick test labels");

        using var sink = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var reserve = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var reply = (IPEndPoint)reserve.Client.LocalEndPoint!;
        listen.Text = "65536";
        Click(start);
        Check(status.Text.Contains("1 to 65535"), "invalid port");
        listen.Text = reply.Port.ToString();
        destination.Text = ((IPEndPoint)sink.Client.LocalEndPoint!).Port.ToString();
        Click(start);
        Check(status.Text.Contains("already in use") || status.Text.Contains("OSC session ended"), "port conflict");
        reserve.Dispose();
        Click(start);
        Check(status.Text.Contains("Streaming to"), "start after conflict");

        var sessionField = typeof(MainWindow).GetField("session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var session = sessionField.GetValue(window)!;
        var sentProperty = session.GetType().GetProperty("SentPackets")!;
        long Sent() => (long)sentProperty.GetValue(session)!;
        var packetField = typeof(MainWindow).GetField("packet", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        byte[] Frame() => (byte[])packetField.GetValue(window)!;
        byte[] frame = Frame();
        Check(frame.Length == 132, "packet length");
        Check(frame.AsSpan(0, 20).SequenceEqual(OscString("/tracking/face/v1")), "OSC address");
        Check(frame.AsSpan(20, 4).SequenceEqual(OscString(",b")), "blob type tag");
        Check(BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(24)) == 102, "blob length");
        Check(frame[130] == 0 && frame[131] == 0, "padding");
        Check(frame.AsSpan(28, 102).IndexOfAnyExcept((byte)0) == -1, "initial neutral frame");
        byte[] expectedValues = Enumerable.Range(0, 102).Select(value => (byte)value).ToArray();
        Check(OscPacket.CreateFacePacket(expectedValues).AsSpan(28, 102).SequenceEqual(expectedValues), "all values retain their byte positions");
        await Task.Delay(250);
        var clock = Stopwatch.StartNew();
        long before = Sent();
        await Task.Delay(2050);
        double rate = (Sent() - before) / clock.Elapsed.TotalSeconds;
        Check(rate > 57 && rate < 63, $"near 60 Hz ({rate:F2})");
        Console.WriteLine($"Sender completed UDP sends at {rate:F2} Hz.");

        var edits = Tree<TextBox>(tabs).ToArray();
        Slider SliderFor(string name) => Tree<Slider>(tabs).Single(item => AutomationProperties.GetName(item) == name);
        SliderFor("JawOpen").SetCurrentValue(Slider.ValueProperty, 231d);
        edits[0].SetCurrentValue(TextBox.TextProperty, "255");
        edits[1].SetCurrentValue(TextBox.TextProperty, "142");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(edits[2].Text == "231" && SliderFor("EyeClosedLeft").Value == 142, "slider and text sync both ways");
        Check(Frame()[64] == 231 && Frame()[36] == 255 && Frame()[37] == 142, "featured controls update correct payload bytes");
        edits[0].SetCurrentValue(TextBox.TextProperty, "256");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(((SolidColorBrush)edits[0].BorderBrush).Color == Color.FromRgb(0xB4, 0x23, 0x28), "invalid tracking value highlighted");
        Check(Frame()[36] == 255, "last valid value survives invalid input");

        tabs.SelectedIndex = 1;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(SliderFor("EyeClosedRight").Value == 255, "quick test and group share state");
        tabs.SelectedIndex = 0;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

        var tracking = OscPacket.ReadStatus(Bundle(
            Message("/avatar/change", ",s", "avtr_example"),
            Message("/avatar/avatarfullfacetracking", ",T"),
            Message("/avatar/parameters/UserFaceTracking", ",T"),
            Message("/avatar/parameters/UserEyeTracking", ",F")), new TrackingStatus());
        Check(tracking == new TrackingStatus("avtr_example", true, true, false), "incoming bundle and OSC booleans");
        tracking = OscPacket.ReadStatus(Message("/avatar/change", ",s", "avtr_next"), tracking);
        Check(tracking == new TrackingStatus("avtr_next", null, true, false), "new avatar clears previous support");
        Check(OscPacket.ReadStatus(new byte[] { 255, 0, 0 }, tracking) == tracking, "malformed packet ignored");

        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var png = File.Create(Path.Combine(Environment.CurrentDirectory, ".verification", "window.png"))) encoder.Save(png);

        Click(Tree<Button>(window).Single(button => Equals(button.Content, "Reset all to 0")));
        Check(Frame().AsSpan(28, 102).IndexOfAnyExcept((byte)0) == -1, "reset clears payload");
        Check(Tree<TextBox>(tabs).All(box => box.Text == "0"), "reset clears editors and invalid drafts");
        Click(start);
        await Task.Delay(180);
        Check(Equals(start.Content, "Start streaming") && listen.IsEnabled, "stop releases GUI");
        long stoppedCount = Sent();
        await Task.Delay(150);
        Check(Sent() == stoppedCount, "stop halts sends");
        window.Close();
        Console.WriteLine("Passed WPF controls, packet layout, status, reset and stop.");
    }

    static byte[] OscString(string value) => Encoding.ASCII.GetBytes(value.PadRight((value.Length + 4) & ~3, '\0'));
    static byte[] Message(params string[] elements) => elements.SelectMany(OscString).ToArray();
    static byte[] Bundle(params byte[][] messages)
    {
        using var output = new MemoryStream();
        output.Write("#bundle\0"u8);
        output.Write(new byte[] {0,0,0,0,0,0,0,1});
        var size = new byte[4];
        foreach (var message in messages)
        {
            BinaryPrimitives.WriteInt32BigEndian(size, message.Length);
            output.Write(size);
            output.Write(message);
        }
        return output.ToArray();
    }
    static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static void Check(bool passed, string name) { if (!passed) throw new Exception("Failed: " + name); }
    static IEnumerable<T> Tree<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (T descendant in Tree<T>(child)) yield return descendant;
        }
    }
}
