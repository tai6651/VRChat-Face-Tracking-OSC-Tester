using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OscTester;

internal static class UnityAnimationText
{
    public static FaceClip ReadYaml(string file, bool standaloneCamera = false)
    {
        var curves = new Dictionary<string, FaceCurve>(StringComparer.Ordinal);
        var editorCurves = new Dictionary<string, FaceCurve>(StringComparer.Ordinal);
        var humanoid = new Dictionary<int, FaceCurve>();
        var editorHumanoid = new Dictionary<int, FaceCurve>();
        var camera = new CameraTrackBuilder();
        var points = new List<FaceKey>();
        List<FaceKey>[] vectorPoints = [[], [], [], []];
        var key = new KeyFields();
        KeyFields[]? vectorKey = null;
        CameraProperty? vectorProperty = null;
        string name = Path.GetFileNameWithoutExtension(file), section = "", attribute = "", path = "";
        int classId = 0;
        double rate = 0, start = 0, stop = 0, lastTime = 0;
        bool animation = false, inKey = false;

        void FinishKey()
        {
            if (inKey && key.Time is double time)
            {
                if (double.IsFinite(time))
                    lastTime = Math.Max(lastTime, time);
                if (section is "m_FloatCurves" or "m_EditorCurves")
                    points.Add(key.Create());
                if (vectorKey is not null)
                {
                    for (int i = 0; i < vectorKey.Length; i++)
                    {
                        vectorKey[i].Time = time;
                        vectorPoints[i].Add(vectorKey[i].Create());
                    }
                }
            }
            key = new KeyFields();
            vectorKey = null;
            inKey = false;
        }

        void FinishCurve()
        {
            FinishKey();
            bool runtime = section == "m_FloatCurves";
            AddNamedCurve(attribute, classId, path, points, runtime ? curves : editorCurves,
                runtime ? humanoid : editorHumanoid);
            camera.AddNamed(path, classId, attribute, points);
            if (vectorProperty is CameraProperty property)
            {
                for (int i = 0; i < vectorPoints.Length; i++)
                {
                    if (vectorPoints[i].Count != 0)
                        camera.AddNamed(path, property, i, vectorPoints[i]);
                    vectorPoints[i].Clear();
                }
            }
            points.Clear();
            attribute = "";
            path = "";
            classId = 0;
        }

        foreach (string line in File.ReadLines(file))
        {
            if (line == "AnimationClip:")
                animation = true;
            if (!animation)
                continue;
            if (line.StartsWith("--- ", StringComparison.Ordinal))
                break;
            ReadOnlySpan<char> field = line.AsSpan().Trim();
            int separator = field.IndexOf(':');
            if (separator < 0)
                continue;
            ReadOnlySpan<char> label = field[..separator], value = field[(separator + 1)..].Trim();
            int indent = line.Length - line.AsSpan().TrimStart().Length;
            if (indent == 2 && !label.StartsWith("- ", StringComparison.Ordinal))
            {
                FinishCurve();
                section = label.ToString();
                vectorProperty = section switch
                {
                    "m_PositionCurves" => CameraProperty.Position,
                    "m_RotationCurves" => CameraProperty.Quaternion,
                    "m_EulerCurves" => CameraProperty.Euler,
                    "m_ScaleCurves" => CameraProperty.Scale,
                    _ => null
                };
                if (label.SequenceEqual("m_Name"))
                    name = Unquote(value.ToString());
                if (label.SequenceEqual("m_SampleRate"))
                    rate = Number(value);
                continue;
            }
            if (indent == 2 && label.StartsWith("- ", StringComparison.Ordinal))
            {
                FinishCurve();
                continue;
            }
            if (label.SequenceEqual("- serializedVersion") || label.SequenceEqual("- time"))
            {
                FinishKey();
                inKey = true;
                if (vectorProperty is not null)
                    vectorKey = vectorProperty == CameraProperty.Quaternion
                        ? [new(), new(), new(), new()] : [new(), new(), new()];
                if (label.SequenceEqual("- time"))
                    key.Time = Number(value);
                continue;
            }
            if (label.SequenceEqual("attribute"))
            {
                attribute = Unquote(value.ToString());
                continue;
            }
            if (label.SequenceEqual("path"))
                path = Unquote(value.ToString());
            if (label.SequenceEqual("classID"))
                int.TryParse(value, out classId);
            if (label.SequenceEqual("m_StartTime"))
                start = Number(value);
            else if (label.SequenceEqual("m_StopTime"))
                stop = Number(value);
            else if (inKey && vectorKey is not null && value.StartsWith("{") && value.EndsWith("}"))
                ReadVectorField(vectorKey, label, value[1..^1]);
            else if (inKey && vectorKey is not null && label.SequenceEqual("weightedMode"))
            {
                foreach (KeyFields component in vectorKey)
                    component.Read(label, value);
            }
            else if (inKey)
                key.Read(label, value);
        }
        FinishCurve();
        if (!animation)
            throw new InvalidDataException("The selected YAML file does not contain a Unity AnimationClip.");
        foreach (var (morph, curve) in editorCurves)
            curves.TryAdd(morph, curve);
        foreach (var (channel, curve) in editorHumanoid)
            humanoid.TryAdd(channel, curve);
        return CreateClip(name, rate, start, stop, lastTime, curves, humanoid)
            with { Camera = camera.Build(standaloneCamera) };
    }

    private static void ReadVectorField(KeyFields[] keys, ReadOnlySpan<char> label, ReadOnlySpan<char> value)
    {
        while (!value.IsEmpty)
        {
            int comma = value.IndexOf(',');
            ReadOnlySpan<char> pair = (comma < 0 ? value : value[..comma]).Trim();
            int colon = pair.IndexOf(':');
            if (colon == 1)
            {
                int component = "xyzw".IndexOf(pair[0]);
                if (component >= 0 && component < keys.Length)
                    keys[component].Read(label, pair[(colon + 1)..].Trim());
            }
            value = comma < 0 ? [] : value[(comma + 1)..];
        }
    }

    public static uint BindingHash(string name)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in Encoding.UTF8.GetBytes(name))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        return ~crc;
    }

    public static void AddNamedCurve(string attribute, int classId, string path, List<FaceKey> points,
        Dictionary<string, FaceCurve> curves, Dictionary<int, FaceCurve> humanoid)
    {
        if (points.Count == 0)
            return;
        if (attribute.StartsWith("blendShape.", StringComparison.Ordinal))
            curves.TryAdd(attribute["blendShape.".Length..], new FaceCurve(points));
        else if (classId == 95 && path.Length == 0 && HumanoidChannels.TryGetAttribute(attribute, out int channel))
        {
            var curve = new FaceCurve(points);
            if (curve.KeyCount > 0)
                humanoid.TryAdd(channel, curve);
        }
    }

    public static FaceClip CreateClip(string name, double rate, double start, double stop, double lastTime,
        Dictionary<string, FaceCurve> curves, Dictionary<int, FaceCurve> humanoid)
    {
        rate = double.IsFinite(rate) && rate > 0 ? rate : 60;
        start = double.IsFinite(start) ? start : 0;
        if (!double.IsFinite(stop) || stop <= start)
            stop = Math.Max(start, lastTime);
        if (stop - start >= TimeSpan.MaxValue.TotalSeconds || (stop - start) * rate >= int.MaxValue - 1)
            throw new InvalidDataException("The animation timeline exceeds the player limit.");
        return new FaceClip(name, rate, start, stop, curves, humanoid);
    }

    public static double Number(ReadOnlySpan<char> value)
    {
        if (value.SequenceEqual(".inf") || value.SequenceEqual("inf"))
            return double.PositiveInfinity;
        if (value.SequenceEqual("-.inf") || value.SequenceEqual("-inf"))
            return double.NegativeInfinity;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            ? number : double.NaN;
    }

    private static string Unquote(string text)
    {
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            try { return JsonSerializer.Deserialize<string>(text) ?? ""; }
            catch (JsonException) { return text[1..^1]; }
        }
        return text.Length >= 2 && text[0] == '\'' && text[^1] == '\''
            ? text[1..^1].Replace("''", "'") : text;
    }

    internal sealed class KeyFields
    {
        public double? Time;
        private double value, inSlope, outSlope, inWeight = 1.0 / 3, outWeight = 1.0 / 3;
        private int weightedMode;

        public void Read(ReadOnlySpan<char> label, ReadOnlySpan<char> text)
        {
            switch (label)
            {
                case "time": Time = Number(text); break;
                case "value": value = Number(text); break;
                case "inSlope": inSlope = Number(text); break;
                case "outSlope": outSlope = Number(text); break;
                case "inWeight": inWeight = ReadWeight(text); break;
                case "outWeight": outWeight = ReadWeight(text); break;
                case "weightedMode": int.TryParse(text, out weightedMode); break;
            }
        }

        private static double ReadWeight(ReadOnlySpan<char> text) =>
            Number(text) is double weight && double.IsFinite(weight) ? Math.Clamp(weight, 0, 1) : 1.0 / 3;

        public FaceKey Create() => new(Time ?? double.NaN, value, inSlope, outSlope,
            InWeight: (weightedMode & 1) != 0 ? inWeight : null,
            OutWeight: (weightedMode & 2) != 0 ? outWeight : null);
    }
}
