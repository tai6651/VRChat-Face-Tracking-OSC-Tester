using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace OscTester;

public partial class AnimationPlayer : UserControl
{
    private readonly byte[] values = new byte[OscPacket.FacePayloadSize];
    private readonly Stopwatch clock = new();
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render)
    {
        Interval = TimeSpan.FromSeconds(1.0 / OscSession.TargetRate)
    };
    private AnimationImport? import;
    private CancellationTokenSource? loading;
    private FaceClip? clip;
    private FacePose? pose;
    private HumanoidPose? humanoidPose;
    private float[]? humanoidFrame;
    private CameraPose? cameraPose;
    private CameraOutputFrame? cameraFrame;
    private MediaPlayer? media;
    private bool audioReady;
    private bool playbackActive;
    private double position;

    internal event Action<byte[], int[]>? PoseChanged;
    internal float[]? HumanoidFrame => Volatile.Read(ref humanoidFrame);
    internal CameraOutputFrame? CameraFrame => Volatile.Read(ref cameraFrame);

    public AnimationPlayer()
    {
        InitializeComponent();
        timer.Tick += (_, _) => Tick();
    }

    internal void Close()
    {
        loading?.Cancel();
        playbackActive = false;
        PublishHumanoidFrame(position);
        Volatile.Write(ref cameraFrame, null);
        timer.Stop();
        media?.Close();
        import?.Dispose();
    }

    private void BrowseBundle_Click(object sender, RoutedEventArgs e) =>
        Browse(BundlePath, "Unity AssetBundle (*.unity3d)|*.unity3d");

    private void BrowseClip_Click(object sender, RoutedEventArgs e) =>
        Browse(ClipPath, "Unity AnimationClip (*.anim)|*.anim");

    private void BrowseAudio_Click(object sender, RoutedEventArgs e) =>
        Browse(AudioPath, "Audio (*.wav;*.ogg;*.mp3)|*.wav;*.ogg;*.mp3");

    private void ClearAudio_Click(object sender, RoutedEventArgs e) => AudioPath.Clear();

    private void BrowseCamera_Click(object sender, RoutedEventArgs e) =>
        Browse(CameraPath, "Unity camera AnimationClip (*.anim)|*.anim");

    private void ClearCamera_Click(object sender, RoutedEventArgs e) => CameraPath.Clear();

    private void Browse(TextBox path, string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        string? directory = Path.GetDirectoryName(path.Text);
        if (Directory.Exists(directory))
            dialog.InitialDirectory = directory;
        else if (Directory.Exists(@"C:\work\MMD"))
            dialog.InitialDirectory = @"C:\work\MMD";
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            path.Text = dialog.FileName;
    }

    private async void Load_Click(object sender, RoutedEventArgs e)
    {
        bool bundle = InputModes.SelectedIndex == 0;
        string file = bundle ? BundlePath.Text : ClipPath.Text;
        if (string.IsNullOrWhiteSpace(file))
        {
            ReportError(bundle ? "Choose a .unity3d bundle." : "Choose a Unity .anim file.");
            return;
        }

        string? audioFile = string.IsNullOrWhiteSpace(AudioPath.Text) ? null : AudioPath.Text;
        string? cameraFile = !bundle && !string.IsNullOrWhiteSpace(CameraPath.Text) ? CameraPath.Text : null;
        Pause();
        using var request = new CancellationTokenSource();
        loading = request;
        InputModes.IsEnabled = LoadButton.IsEnabled = false;
        SetTransport();
        ImportStatus.Foreground = Brushes.DimGray;
        ImportStatus.Text = "Reading animation…";
        AnimationImport? pending = null;
        try
        {
            pending = new AnimationImport();
            AnimationImport work = pending;
            await Task.Run(() => work.ReadAsync(file, bundle, request.Token), request.Token);
            FaceClip? selected = Choose("Choose animation clip", "Which AnimationClip should the player use?",
                work.Clips, item => $"{item.Name}   •   {FormatTime(item.Duration)}   •   {item.Curves.Count} face tracks",
                "Use selected clip", "Cancel");
            if (selected is null)
            {
                ImportStatus.Text = "Clip selection cancelled.";
                return;
            }

            var mapped = new FacePose(selected);
            FaceClip? selectedCamera = selected.Camera is not null ? selected : null;
            string cameraUnavailable = "No supported camera tracks in this clip.";
            if (cameraFile is not null)
            {
                ImportStatus.Text = "Reading camera animation…";
                using var cameraImport = new AnimationImport();
                await Task.Run(() => cameraImport.ReadAsync(cameraFile, false, request.Token, standaloneCamera: true),
                    request.Token);
                selectedCamera = cameraImport.Clips.FirstOrDefault(item => item.Camera is not null);
                cameraUnavailable = "No supported camera tracks in the selected camera file.";
            }
            else if (bundle && selectedCamera is null)
            {
                var candidates = work.Clips.Where(item => item.Camera is not null).ToArray();
                selectedCamera = Choose("Choose camera clip", "Which camera track should accompany the animation?",
                    candidates, item => $"{item.Name}   •   {FormatTime(item.Duration)}", "Use selected camera",
                    "Play without camera");
                cameraUnavailable = candidates.Length == 0
                    ? "No supported camera tracks in this bundle." : "No camera clip selected.";
            }

            BundleAudio? selectedAudio = bundle
                ? Choose("Choose embedded audio", "Which AudioClip should accompany the animation?",
                    work.AudioCandidates(selected), item => item.Name, "Use selected audio", "Play without audio")
                : null;
            ImportStatus.Text = "Preparing audio…";
            var audio = await Task.Run(() =>
                work.PrepareAudioAsync(bundle, audioFile, selectedAudio, request.Token), request.Token);
            request.Token.ThrowIfCancellationRequested();

            media?.Close();
            media = null;
            audioReady = false;
            import?.Dispose();
            import = pending;
            pending = null;
            if (pose is not null)
            {
                Array.Clear(values);
                PoseChanged?.Invoke(values, pose.Channels);
            }

            clip = selected;
            pose = mapped;
            humanoidPose = new HumanoidPose(selected);
            cameraPose = selectedCamera?.Camera is CameraTrack track ? new CameraPose(selectedCamera, track) : null;
            if (cameraPose is null)
            {
                SendCameraPose.IsChecked = false;
                CameraInfo.Text = cameraUnavailable;
            }
            playbackActive = false;
            HumanoidInfo.Text = humanoidPose.HasTracks
                ? $"{humanoidPose.MuscleCount}/{HumanoidChannels.MuscleCount} muscle tracks"
                    + (humanoidPose.HasRoot ? " · root motion" : "")
                : "No humanoid tracks in this clip";
            string mapping = mapped.TrackCount == 0 ? "no mapped face tracks"
                : $"{mapped.TrackCount} of {selected.Curves.Count} face tracks mapped";
            ClipInfo.Text = $"{selected.Name}   •   {selected.FrameRate:0.###} fps   •   {mapping}";
            ClipInfo.ToolTip = ClipInfo.Text;
            ImportStatus.Text = mapped.TrackCount == 0 && !humanoidPose.HasTracks && cameraPose is null
                ? "No supported face curves. If this is an AssetRipper export with merged bindings, load the original bundle."
                : "Ready";
            AudioStatus.Text = audio.Warning ?? "Opening audio…";
            if (audio.Path is not null)
                OpenAudio(audio.Path);
            Seek(0);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or FormatException or OverflowException)
        {
            ReportError(ex.Message);
        }
        finally
        {
            pending?.Dispose();
            loading = null;
            InputModes.IsEnabled = LoadButton.IsEnabled = true;
            SetTransport();
        }
    }

    private T? Choose<T>(string title, string prompt, IReadOnlyList<T> candidates, Func<T, string> describe,
        string selectLabel, string cancelLabel) where T : class
    {
        if (candidates.Count == 0)
            return null;
        if (candidates.Count == 1)
            return candidates[0];
        var list = new ListBox
        {
            MinHeight = 100,
            ItemsSource = candidates.Select((item, index) => $"{index + 1}. {describe(item)}"),
            SelectedIndex = 0,
            Margin = new Thickness(0, 9, 0, 9)
        };
        var button = new Button { Content = selectLabel, IsDefault = true, Padding = new Thickness(12, 4, 12, 4) };
        var cancel = new Button { Content = cancelLabel, IsCancel = true, Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 4, 12, 4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(button);
        buttons.Children.Add(cancel);
        var content = new DockPanel { Margin = new Thickness(14) };
        var heading = new TextBlock { Text = prompt, FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(heading, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        content.Children.Add(heading);
        content.Children.Add(buttons);
        content.Children.Add(list);
        var dialog = new Window
        {
            Owner = Window.GetWindow(this), Title = title, Content = content,
            Width = 530, Height = 320, MinWidth = 370, MinHeight = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
        button.Click += (_, _) => { if (list.SelectedIndex >= 0) dialog.DialogResult = true; };
        list.MouseDoubleClick += (_, _) => { if (list.SelectedIndex >= 0) dialog.DialogResult = true; };
        return dialog.ShowDialog() == true ? candidates[list.SelectedIndex] : null;
    }

    private void OpenAudio(string file)
    {
        var player = new MediaPlayer();
        media = player;
        player.MediaOpened += (_, _) =>
        {
            if (media != player)
                return;
            audioReady = true;
            AudioStatus.Text = "Audio ready.";
            SyncAudio();
        };
        player.MediaFailed += (_, e) =>
        {
            if (media != player)
                return;
            audioReady = false;
            AudioStatus.Text = $"Audio unavailable. {e.ErrorException.Message}";
        };
        player.Open(new Uri(file, UriKind.Absolute));
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (clip is null || loading is not null || clock.IsRunning)
            return;
        if (position >= clip.Duration)
            Seek(0);
        playbackActive = true;
        PublishHumanoidFrame(position);
        PublishCameraFrame(position);
        clock.Start();
        SyncAudio();
        timer.Start();
        SetTransport();
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => Pause();

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        playbackActive = false;
        clock.Reset();
        timer.Stop();
        Seek(0);
        SetTransport();
    }

    private void Step_Click(object sender, RoutedEventArgs e)
    {
        if (clip is not null && !clock.IsRunning)
            SeekFrame(Math.Min(clip.LastFrame, CurrentFrame() + 1));
    }

    private void FrameInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        CommitFrame();
        e.Handled = true;
    }

    private void FrameInput_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitFrame();

    private void CommitFrame()
    {
        if (clip is null || clock.IsRunning || loading is not null
            || FrameInput.Text == CurrentFrame().ToString(CultureInfo.InvariantCulture))
            return;
        if (int.TryParse(FrameInput.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int frame)
            && frame >= 0 && frame <= clip.LastFrame)
        {
            SeekFrame(frame);
            return;
        }
        ReportError($"Enter a frame from 0 through {clip.LastFrame}.");
        ShowPosition(position);
    }

    private void Tick()
    {
        if (clip is null)
            return;
        double current = CurrentTime();
        if (current >= clip.Duration)
        {
            playbackActive = false;
            clock.Reset();
            timer.Stop();
            position = clip.Duration;
            SyncAudio();
            SetTransport();
        }
        ShowPosition(current);
    }

    private void Pause()
    {
        if (!clock.IsRunning)
            return;
        position = CurrentTime();
        clock.Reset();
        timer.Stop();
        SyncAudio();
        ShowPosition(position);
        SetTransport();
    }

    private void SeekFrame(int frame)
    {
        if (clip is not null)
            Seek(Math.Min(clip.Duration, frame / clip.FrameRate));
    }

    private void Seek(double time)
    {
        position = time;
        SyncAudio();
        ShowPosition(time);
        SetTransport();
    }

    private void SyncAudio()
    {
        if (media is null || !audioReady)
            return;
        double time = CurrentTime();
        bool withinAudio = !media.NaturalDuration.HasTimeSpan || time < media.NaturalDuration.TimeSpan.TotalSeconds;
        media.Pause();
        media.Position = TimeSpan.FromSeconds(withinAudio ? time : media.NaturalDuration.TimeSpan.TotalSeconds);
        if (withinAudio && clock.IsRunning)
            media.Play();
    }

    private double CurrentTime() => Math.Min(clip?.Duration ?? 0, position + clock.Elapsed.TotalSeconds);

    private int CurrentFrame() => FrameAt(CurrentTime());

    private int FrameAt(double time) => clip is null ? 0 : time >= clip.Duration
        ? clip.LastFrame : Math.Min(clip.LastFrame, (int)Math.Floor(time * clip.FrameRate + 0.0001));

    private void ShowPosition(double time)
    {
        if (clip is null || pose is null)
            return;
        FrameInput.Text = FrameAt(time).ToString(CultureInfo.InvariantCulture);
        TimeInfo.Text = $"{FormatTime(time)} / {FormatTime(clip.Duration)}";
        TotalFrames.Text = (clip.LastFrame + 1).ToString(CultureInfo.InvariantCulture);
        pose.Evaluate(time, values);
        PoseChanged?.Invoke(values, pose.Channels);
        PublishHumanoidFrame(time);
        PublishCameraFrame(time);
    }

    private void SendHumanoidPose_Changed(object sender, RoutedEventArgs e) => PublishHumanoidFrame(CurrentTime());

    private void CameraOption_Changed(object sender, RoutedEventArgs e) => PublishCameraFrame(CurrentTime());

    private void PublishCameraFrame(double time)
    {
        CameraOutputFrame? frame = null;
        if (cameraPose is not null)
        {
            bool limited = false;
            if (SendCameraPose.IsChecked == true)
                frame = new CameraOutputFrame(
                    cameraPose.Evaluate(time, FlipCameraDirection.IsChecked == true, out limited), playbackActive);
            CameraInfo.Text = limited ? $"{cameraPose.Name} • exceeds rig range" : cameraPose.Name;
        }
        Volatile.Write(ref cameraFrame, frame);
    }

    private void PublishHumanoidFrame(double time)
    {
        float[]? frame = null;
        if (SendHumanoidPose?.IsChecked == true)
            frame = playbackActive && humanoidPose is { HasTracks: true }
                ? humanoidPose.Evaluate(time) : [];
        Volatile.Write(ref humanoidFrame, frame);
    }

    private void SetTransport()
    {
        bool ready = clip is not null && loading is null;
        PlayButton.IsEnabled = ready && !clock.IsRunning && clip!.Duration > 0;
        PauseButton.IsEnabled = ready && clock.IsRunning;
        StopButton.IsEnabled = ready;
        StepButton.IsEnabled = ready && !clock.IsRunning && CurrentFrame() < clip!.LastFrame;
        FrameInput.IsReadOnly = !ready || clock.IsRunning;
        SendCameraPose.IsEnabled = FlipCameraDirection.IsEnabled = ready && cameraPose is not null;
    }

    private void ReportError(string message)
    {
        ImportStatus.Foreground = Brushes.Firebrick;
        ImportStatus.Text = message;
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Round(seconds * 1000));
        return $"{(long)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
    }
}
