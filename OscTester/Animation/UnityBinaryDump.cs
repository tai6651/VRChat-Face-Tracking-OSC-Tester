using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace OscTester;

internal sealed record BundleAudio(string Name, string Source, long Offset, long Size, int Subsound, string Member);

internal static partial class UnityBinaryDump
{
    public static void Read(string dump, string member, List<FaceClip> clips, List<BundleAudio> audio,
        bool standaloneCamera = false)
    {
        var lines = new List<string>();
        int classId = 0;
        void Finish()
        {
            if (classId == 74)
                clips.Add(ReadClip(lines, standaloneCamera));
            else if (classId == 83)
                audio.Add(ReadAudio(lines, member));
            lines.Clear();
        }

        foreach (string line in File.ReadLines(dump))
        {
            if (line.StartsWith("ID:", StringComparison.Ordinal))
            {
                Finish();
                classId = int.TryParse(ObjectHeader().Match(line).Groups[1].Value, out int id) ? id : 0;
            }
            else if (classId is 74 or 83)
                lines.Add(line);
        }
        Finish();
    }

    private static BundleAudio ReadAudio(List<string> lines, string member)
    {
        string name = "", source = "";
        long offset = 0, size = 0;
        int subsound = 0;
        foreach (string line in lines)
        {
            var (label, value) = Field(line.Trim());
            switch (label)
            {
                case "m_Name": name = value.Trim('"'); break;
                case "m_Source": source = value.Trim('"'); break;
                case "m_Offset": long.TryParse(value, out offset); break;
                case "m_Size": long.TryParse(value, out size); break;
                case "m_SubsoundIndex": int.TryParse(value, out subsound); break;
            }
        }
        return new BundleAudio(name, source, offset, size, subsound, member);
    }

    private static FaceClip ReadClip(List<string> lines, bool standaloneCamera)
    {
        const uint bodyPath = 0x7B9AA48C;
        var streamed = new List<uint>();
        var dense = new List<double>();
        var constants = new List<double>();
        var bindings = new List<Binding>();
        var curves = new Dictionary<string, FaceCurve>(StringComparer.Ordinal);
        var humanoid = new Dictionary<int, FaceCurve>();
        var camera = new CameraTrackBuilder();
        var section = Section.None;
        int sectionIndent = 0, streamCount = 0, denseCount = 0, denseFrames = 0;
        double frameRate = 0, denseRate = 0, denseStart = 0, start = 0, stop = 0, lastTime = 0;
        string name = "AnimationClip", attributeName = "", explicitPath = "";
        int explicitClass = 0;
        Binding? binding = null;
        var points = new List<FaceKey>();
        UnityAnimationText.KeyFields? key = null;

        void FinishKey()
        {
            if (key?.Time is double time)
            {
                points.Add(key.Create());
                if (double.IsFinite(time))
                    lastTime = Math.Max(lastTime, time);
            }
            key = null;
        }

        void FinishCurve()
        {
            FinishKey();
            UnityAnimationText.AddNamedCurve(attributeName, explicitClass, explicitPath, points, curves, humanoid);
            camera.AddNamed(explicitPath, explicitClass, attributeName, points);
            points.Clear();
            attributeName = "";
            explicitPath = "";
            explicitClass = 0;
        }

        foreach (string line in lines)
        {
            ReadOnlySpan<char> text = line.AsSpan().TrimStart();
            if (text.IsEmpty)
                continue;
            int indent = line.Length - text.Length;
            if (section != Section.None && indent <= sectionIndent)
            {
                FinishCurve();
                section = Section.None;
            }

            if (section is Section.Stream or Section.Dense or Section.Constant
                && text.StartsWith("data (", StringComparison.Ordinal))
            {
                int separator = text.IndexOf(':');
                if (separator >= 0)
                {
                    var remaining = text[(separator + 1)..].Trim();
                    while (!remaining.IsEmpty)
                    {
                        int space = remaining.IndexOf(' ');
                        ReadOnlySpan<char> word = space < 0 ? remaining : remaining[..space];
                        if (section == Section.Stream)
                            streamed.Add(uint.Parse(word, CultureInfo.InvariantCulture));
                        else
                            (section == Section.Dense ? dense : constants).Add(UnityAnimationText.Number(word));
                        remaining = space < 0 ? [] : remaining[(space + 1)..].TrimStart();
                    }
                    continue;
                }
            }
            if (section == Section.None && indent > 2 && !text.StartsWith("m_", StringComparison.Ordinal)
                && !text.StartsWith("genericBindings ", StringComparison.Ordinal))
                continue;

            var (label, value) = Field(text.ToString());
            switch (section)
            {
                case Section.Stream when label == "curveCount":
                    streamCount = Int(value); break;
                case Section.Stream when label == "data" && value.Length != 0:
                    streamed.Add(UInt(value)); break;
                case Section.Dense:
                    switch (label)
                    {
                        case "m_FrameCount": denseFrames = Int(value); break;
                        case "m_CurveCount": denseCount = Int(value); break;
                        case "m_SampleRate": denseRate = UnityAnimationText.Number(value); break;
                        case "m_BeginTime": denseStart = UnityAnimationText.Number(value); break;
                        case "data" when value.Length != 0: dense.Add(UnityAnimationText.Number(value)); break;
                    }
                    break;
                case Section.Constant when label == "data" && value.Length != 0:
                    constants.Add(UnityAnimationText.Number(value)); break;
                case Section.Bindings:
                    if (label == "data" && value.Length == 0)
                    {
                        binding = new Binding();
                        bindings.Add(binding);
                    }
                    if (binding is not null)
                    {
                        switch (label)
                        {
                            case "path": binding.Path = UInt(value); break;
                            case "attribute": binding.Attribute = UInt(value); break;
                            case "typeID": binding.Type = Int(value); break;
                            case "customType": binding.CustomType = Int(value); break;
                        }
                    }
                    break;
                case Section.Explicit:
                    if (label == "data" && indent == sectionIndent + 1)
                        FinishCurve();
                    if (label == "time")
                    {
                        FinishKey();
                        key = new UnityAnimationText.KeyFields();
                    }
                    if (label == "attribute")
                        attributeName = value.Trim('"');
                    if (label == "path")
                        explicitPath = value.Trim('"');
                    if (label == "classID")
                        explicitClass = Int(value);
                    key?.Read(label, value);
                    break;
                case Section.None:
                    section = label switch
                    {
                        "m_StreamedClip" => Section.Stream,
                        "m_DenseClip" => Section.Dense,
                        "m_ConstantClip" => Section.Constant,
                        "genericBindings" => Section.Bindings,
                        "m_FloatCurves" or "m_EditorCurves" => Section.Explicit,
                        _ => Section.None
                    };
                    sectionIndent = indent;
                    if (indent == 1 && label == "m_Name")
                        name = value.Trim('"');
                    if (indent == 1 && label == "m_SampleRate")
                        frameRate = UnityAnimationText.Number(value);
                    if (indent == 2 && label == "m_StartTime")
                        start = UnityAnimationText.Number(value);
                    if (indent == 2 && label == "m_StopTime")
                        stop = UnityAnimationText.Number(value);
                    break;
            }
        }
        FinishCurve();

        var tracks = new Dictionary<int, (Binding Binding, List<FaceKey> Keys, CameraProperty? Camera, int Component)>();
        var lenses = bindings.Where(item => CameraTrackBuilder.IsFieldOfView(item.Type, item.Attribute))
            .Select(item => item.Path).ToHashSet();
        int channel = 0;
        foreach (Binding item in bindings)
        {
            int size = item.Type is 4 or 224
                ? item.Attribute switch { 2 => 4, 1 or 3 or 4 => 3, _ => 1 }
                : 1;
            CameraProperty? cameraProperty = CameraTrackBuilder.IsFieldOfView(item.Type, item.Attribute)
                ? CameraProperty.FieldOfView
                : item.Type == 4 && (CameraTrackBuilder.IsMmdPath(item.Path) || lenses.Contains(item.Path)
                    || standaloneCamera && item.Path == 0)
                    ? CameraTrackBuilder.TransformProperty(item.Attribute) : null;
            if (cameraProperty is not null)
            {
                for (int component = 0; component < size; component++)
                    tracks.Add(channel + component, (item, [], cameraProperty, component));
            }
            else if (item.Type == 137 && item.CustomType == 20 || IsHumanoidBinding(item))
                tracks.Add(channel, (item, [], null, 0));
            channel += size;
        }
        if (tracks.Count == 0)
            return UnityAnimationText.CreateClip(name, frameRate, start, stop, lastTime, curves, humanoid)
                with { Camera = camera.Build(standaloneCamera) };

        // Unity stores a time and key count, then an index and four polynomial coefficients per key.
        for (int cursor = 0; cursor < streamed.Count;)
        {
            if (streamed.Count - cursor < 2)
                throw new InvalidDataException($"Incomplete streamed animation frame in {name}.");
            float time = Float(streamed[cursor++]);
            uint count = streamed[cursor++];
            if (count > (streamed.Count - cursor) / 5)
                throw new InvalidDataException($"Invalid streamed animation key count in {name}.");

            bool realFrame = float.IsFinite(time) && time != float.MinValue;
            if (realFrame)
                lastTime = Math.Max(lastTime, time);
            for (int index = 0; index < count; index++, cursor += 5)
            {
                if (realFrame && tracks.TryGetValue((int)streamed[cursor], out var track))
                {
                    track.Keys.Add(new FaceKey(time, Float(streamed[cursor + 4]),
                        OutSlope: Float(streamed[cursor + 3]), Cubic: Float(streamed[cursor + 1]),
                        Quadratic: Float(streamed[cursor + 2])));
                }
            }
        }

        foreach (var (index, track) in tracks.OrderByDescending(track => track.Value.Binding.Path == bodyPath))
        {
            int offset = index - streamCount;
            bool isDense = offset >= 0 && offset < denseCount;
            if (isDense && double.IsFinite(denseRate) && denseRate > 0)
            {
                int frames = Math.Min(denseFrames, dense.Count / denseCount);
                for (int frame = 0; frame < frames; frame++)
                    track.Keys.Add(new FaceKey(denseStart + frame / denseRate, dense[frame * denseCount + offset]));
            }
            else if (offset >= denseCount && offset - denseCount < constants.Count)
                track.Keys.Add(new FaceKey(start, constants[offset - denseCount]));

            var curve = new FaceCurve(track.Keys, linear: isDense);
            if (curve.KeyCount > 0)
            {
                lastTime = Math.Max(lastTime, curve.LastTime);
                if (track.Camera is CameraProperty property)
                    camera.Add(track.Binding.Path, property, track.Component, curve);
                else if (IsHumanoidBinding(track.Binding))
                    humanoid.TryAdd((int)track.Binding.Attribute, curve);
                else
                    curves.TryAdd(FaceMorphs.NameForHash(track.Binding.Attribute), curve);
            }
        }
        return UnityAnimationText.CreateClip(name, frameRate, start, stop, lastTime, curves, humanoid)
            with { Camera = camera.Build(standaloneCamera) };
    }

    private static bool IsHumanoidBinding(Binding binding) => binding.Type == 95 && binding.CustomType == 8
        && binding.Path == 0 && HumanoidChannels.Supports(binding.Attribute);

    private static (string Label, string Value) Field(string text)
    {
        int split = text.IndexOf(' ');
        if (split < 0)
            return (text, "");
        int type = text.LastIndexOf(" (", StringComparison.Ordinal);
        return (text[..split], type <= split ? "" : text[(split + 1)..type].Trim());
    }

    private static float Float(uint value) => BitConverter.UInt32BitsToSingle(value);
    private static int Int(string value) => int.TryParse(value, out int result) ? result : 0;
    private static uint UInt(string value) => uint.TryParse(value, out uint result) ? result : 0;

    [GeneratedRegex(@"^ID: -?\d+ \(ClassID: (\d+)\)")]
    private static partial Regex ObjectHeader();

    private enum Section { None, Stream, Dense, Constant, Bindings, Explicit }
    private sealed class Binding
    {
        public uint Path, Attribute;
        public int Type, CustomType;
    }
}
