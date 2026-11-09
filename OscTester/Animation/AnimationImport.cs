using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;

namespace OscTester;

internal sealed class AnimationImport : IDisposable
{
    private readonly DirectoryInfo workspace = Directory.CreateTempSubdirectory("vrchat-osc-animation-");
    private readonly List<FaceClip> clips = [];
    private readonly List<BundleAudio> audio = [];
    private string[] members = [];
    private string? toolsPath;

    public IReadOnlyList<FaceClip> Clips => clips;

    public IReadOnlyList<BundleAudio> AudioCandidates(FaceClip clip)
    {
        var matches = audio.Where(item => string.Equals(item.Name, clip.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length > 0 ? matches : audio;
    }

    public async Task ReadAsync(string path, bool bundle, CancellationToken cancellation)
    {
        if (!bundle && IsYaml(path))
        {
            clips.Add(UnityAnimationText.ReadYaml(path));
            return;
        }

        toolsPath = FindUnityTools();
        if (bundle)
        {
            string staged = Path.Combine(workspace.FullName, "input.unity3d");
            File.Copy(path, staged);
            await RunAsync(Path.Combine(toolsPath, "WebExtract.exe"), ["input.unity3d"], cancellation)
                .ConfigureAwait(false);
            string extracted = staged + "_data";
            if (!Directory.Exists(extracted))
                throw new InvalidDataException("Unity could not unpack the selected AssetBundle.");
            members = Directory.GetFiles(extracted, "*", SearchOption.AllDirectories);
        }
        else
            members = [path];

        foreach (string member in members.Where(IsSerializedFile))
        {
            cancellation.ThrowIfCancellationRequested();
            string dump = Path.Combine(workspace.FullName, "asset.txt");
            await RunAsync(Path.Combine(toolsPath, "binary2text.exe"), [member, dump], cancellation)
                .ConfigureAwait(false);
            if (!File.Exists(dump))
                throw new InvalidDataException($"Unity could not read {Path.GetFileName(member)}.");
            UnityBinaryDump.Read(dump, member, clips, audio);
            File.Delete(dump);
        }
        if (clips.Count == 0)
            throw new InvalidDataException("No Unity AnimationClip was found in the selected file.");
    }

    public async Task<(string? Path, string? Warning)> PrepareAudioAsync(
        bool bundle, string? manualAudio, BundleAudio? track, CancellationToken cancellation)
    {
        try
        {
            if (!bundle)
            {
                if (manualAudio is null)
                    return (null, "No audio file selected.");
                if (!Path.GetExtension(manualAudio).Equals(".ogg", StringComparison.OrdinalIgnoreCase))
                    return (manualAudio, null);
                string staged = Path.Combine(workspace.FullName, "manual.ogg");
                File.Copy(manualAudio, staged, overwrite: true);
                return (await ConvertAudioAsync(staged, cancellation).ConfigureAwait(false), null);
            }

            if (track is null)
                return (null, audio.Count == 0 ? "No embedded audio file found." : "No embedded audio selected.");
            if (track.Subsound != 0)
                throw new InvalidDataException("The embedded audio uses a non-primary FSB subsound.");
            if (track.Size <= 0 || string.IsNullOrEmpty(track.Source))
                throw new InvalidDataException("The embedded audio resource has no readable data.");

            string sourceName = Path.GetFileName(track.Source);
            string nearby = Path.Combine(Path.GetDirectoryName(track.Member)!, sourceName);
            string? source = File.Exists(nearby) ? nearby : members.FirstOrDefault(member =>
                string.Equals(Path.GetFileName(member), sourceName, StringComparison.OrdinalIgnoreCase));
            if (source is null)
                throw new InvalidDataException("The embedded audio resource is missing from the bundle.");

            await using var input = File.OpenRead(source);
            if (track.Offset < 0 || track.Offset > input.Length || track.Size > input.Length - track.Offset)
                throw new InvalidDataException("The embedded audio points outside its resource file.");
            input.Position = track.Offset;
            byte[] buffer = new byte[81920];
            int headerLength = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(track.Size, 12)), cancellation)
                .ConfigureAwait(false);
            string extension = AudioExtension(buffer.AsSpan(0, headerLength));
            string output = Path.Combine(workspace.FullName, "embedded" + extension);
            input.Position = track.Offset;
            await using (var destination = File.Create(output))
            {
                long remaining = track.Size;
                while (remaining > 0)
                {
                    int count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(remaining, buffer.Length)),
                        cancellation).ConfigureAwait(false);
                    if (count == 0)
                        throw new EndOfStreamException("The embedded audio resource ended early.");
                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellation).ConfigureAwait(false);
                    remaining -= count;
                }
            }
            return (extension is ".wav" or ".mp3" ? output
                : await ConvertAudioAsync(output, cancellation).ConfigureAwait(false), null);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return (null, $"Audio unavailable. {ex.Message}");
        }
    }

    public void Dispose()
    {
        try { workspace.Delete(recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<string> ConvertAudioAsync(string input, CancellationToken cancellation)
    {
        toolsPath ??= FindUnityTools();
        string tool = Path.Combine(toolsPath, "FSBTool", "x64", "FSBTool64.exe");
        if (!File.Exists(tool))
            throw new FileNotFoundException("Unity's FSBTool64.exe was not found.");
        string output = Path.Combine(workspace.FullName, "audio.wav");
        string cache = Directory.CreateDirectory(Path.Combine(workspace.FullName, "fsb-cache")).FullName;
        await RunAsync(tool, ["-i", input, "-o", output, "-l", Path.GetDirectoryName(tool)!, "-h", cache,
            "-c", "pcm", "-C", "wav"], cancellation).ConfigureAwait(false);
        if (!File.Exists(output) || new FileInfo(output).Length <= 44)
            throw new InvalidDataException("Unity could not convert the selected audio to WAV.");
        return output;
    }

    private async Task RunAsync(string tool, string[] arguments, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(tool)
        {
            WorkingDirectory = workspace.FullName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);
        cancellation.ThrowIfCancellationRequested();
        using var process = Process.Start(info) ?? throw new IOException($"Could not start {Path.GetFileName(tool)}.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            await process.WaitForExitAsync().ConfigureAwait(false);
            throw;
        }
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            string message = string.IsNullOrWhiteSpace(stderr.Result) ? stdout.Result : stderr.Result;
            throw new InvalidDataException($"{Path.GetFileName(tool)} failed. {message.Trim()}");
        }
    }

    private static string FindUnityTools()
    {
        string? overridePath = Environment.GetEnvironmentVariable("OSC_TESTER_UNITY_TOOLS");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (HasTools(overridePath))
                return overridePath;
            throw new DirectoryNotFoundException("OSC_TESTER_UNITY_TOOLS does not contain WebExtract.exe and binary2text.exe.");
        }
        string editors = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Unity", "Hub", "Editor");
        if (Directory.Exists(editors))
        {
            var installed = Directory.EnumerateDirectories(editors)
                .Select(path => (Tools: Path.Combine(path, "Editor", "Data", "Tools"), Version: EditorVersion(path)))
                .Where(editor => HasTools(editor.Tools)).OrderByDescending(editor => editor.Version);
            if (installed.FirstOrDefault().Tools is string tools)
                return tools;
        }
        throw new DirectoryNotFoundException("Unity Editor tools were not found. Install Unity through Hub or set OSC_TESTER_UNITY_TOOLS to Editor\\Data\\Tools.");
    }

    private static Version EditorVersion(string path)
    {
        string name = Path.GetFileName(path);
        int length = 0;
        while (length < name.Length && (char.IsAsciiDigit(name[length]) || name[length] == '.'))
            length++;
        return Version.TryParse(name.AsSpan(0, length), out Version? version) ? version : new Version(0, 0);
    }

    private static bool HasTools(string path) => File.Exists(Path.Combine(path, "WebExtract.exe"))
        && File.Exists(Path.Combine(path, "binary2text.exe"));

    private static bool IsYaml(string file)
    {
        using var input = File.OpenRead(file);
        Span<byte> bytes = stackalloc byte[8];
        int read = input.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        var header = bytes[..read];
        if (header.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
            header = header[3..];
        return header.StartsWith("%YAML"u8) || header.StartsWith("---"u8);
    }

    private static bool IsSerializedFile(string file)
    {
        using var input = File.OpenRead(file);
        Span<byte> header = stackalloc byte[20];
        if (input.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length)
            return false;
        uint version = BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
        return version is >= 9 and <= 50 && header[16] <= 1;
    }

    private static string AudioExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith("RIFF"u8)) return ".wav";
        if (bytes.StartsWith("OggS"u8)) return ".ogg";
        if (bytes.StartsWith("ID3"u8)) return ".mp3";
        return ".fsb";
    }
}
