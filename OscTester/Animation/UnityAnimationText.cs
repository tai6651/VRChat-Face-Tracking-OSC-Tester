using System.Globalization;
using System.IO;
using System.Text.Json;

namespace OscTester;

internal static class UnityAnimationText
{
    public static FaceClip ReadYaml(string file)
    {
        var curves = new Dictionary<string, FaceCurve>(StringComparer.Ordinal);
        var editorCurves = new Dictionary<string, FaceCurve>(StringComparer.Ordinal);
        var points = new List<FaceKey>();
        var key = new KeyFields();
        string name = Path.GetFileNameWithoutExtension(file), section = "", attribute = "";
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
            }
            key = new KeyFields();
            inKey = false;
        }

        void FinishCurve()
        {
            FinishKey();
            if (points.Count != 0 && attribute.StartsWith("blendShape.", StringComparison.Ordinal))
            {
                var target = section == "m_FloatCurves" ? curves : editorCurves;
                target.TryAdd(attribute["blendShape.".Length..], new FaceCurve(points));
            }
            points.Clear();
            attribute = "";
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
                if (label.SequenceEqual("m_Name"))
                    name = Unquote(value.ToString());
                if (label.SequenceEqual("m_SampleRate"))
                    rate = Number(value);
                continue;
            }
            if (label.SequenceEqual("- curve"))
            {
                FinishCurve();
                continue;
            }
            if (label.SequenceEqual("- serializedVersion") || label.SequenceEqual("- time"))
            {
                FinishKey();
                inKey = true;
                if (label.SequenceEqual("- time"))
                    key.Time = Number(value);
                continue;
            }
            if (label.SequenceEqual("attribute"))
            {
                attribute = Unquote(value.ToString());
                continue;
            }
            if (label.SequenceEqual("m_StartTime"))
                start = Number(value);
            else if (label.SequenceEqual("m_StopTime"))
                stop = Number(value);
            else if (inKey)
                key.Read(label, value);
        }
        FinishCurve();
        if (!animation)
            throw new InvalidDataException("The selected YAML file does not contain a Unity AnimationClip.");
        foreach (var (morph, curve) in editorCurves)
            curves.TryAdd(morph, curve);
        return CreateClip(name, rate, start, stop, lastTime, curves);
    }

    public static FaceClip CreateClip(string name, double rate, double start, double stop, double lastTime,
        Dictionary<string, FaceCurve> curves)
    {
        rate = double.IsFinite(rate) && rate > 0 ? rate : 60;
        start = double.IsFinite(start) ? start : 0;
        if (!double.IsFinite(stop) || stop <= start)
            stop = Math.Max(start, lastTime);
        if (stop - start >= TimeSpan.MaxValue.TotalSeconds || (stop - start) * rate >= int.MaxValue - 1)
            throw new InvalidDataException("The animation timeline exceeds the player limit.");
        return new FaceClip(name, rate, start, stop, curves);
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
