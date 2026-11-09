using System.Text;

namespace OscTester;

internal sealed record FaceClip(string Name, double FrameRate, double Start, double End,
    IReadOnlyDictionary<string, FaceCurve> Curves)
{
    public double Duration => End - Start;
    public int LastFrame => (int)Math.Ceiling(Math.Max(0, Duration * FrameRate - 0.001));
}

internal readonly record struct FaceKey(double Time, double Value, double InSlope = 0, double OutSlope = 0,
    double? Cubic = null, double Quadratic = 0, double? InWeight = null, double? OutWeight = null);

internal sealed class FaceCurve(IEnumerable<FaceKey> points, bool linear = false)
{
    private readonly FaceKey[] keys = points.Where(key => double.IsFinite(key.Time) && double.IsFinite(key.Value))
        .OrderBy(key => key.Time).DistinctBy(key => key.Time).ToArray();

    public int KeyCount => keys.Length;
    public double LastTime => keys.Length == 0 ? 0 : keys[^1].Time;

    public double Evaluate(double time)
    {
        if (keys.Length == 0)
            return 0;
        if (time <= keys[0].Time)
            return keys[0].Value;
        if (time >= keys[^1].Time)
            return keys[^1].Value;

        int low = 0, high = keys.Length - 1;
        while (high - low > 1)
        {
            int middle = (low + high) / 2;
            if (keys[middle].Time <= time)
                low = middle;
            else
                high = middle;
        }

        FaceKey a = keys[low], b = keys[high];
        double elapsed = time - a.Time;
        double length = b.Time - a.Time;
        double t = elapsed / length;
        if (linear)
            return a.Value + (b.Value - a.Value) * t;
        if (a.Cubic is double cubic)
            return ((cubic * elapsed + a.Quadratic) * elapsed + a.OutSlope) * elapsed + a.Value;
        if (!double.IsFinite(a.OutSlope) || !double.IsFinite(b.InSlope))
            return a.Value;

        double weightA = a.OutWeight ?? 1.0 / 3, weightB = b.InWeight ?? 1.0 / 3;
        if (a.OutWeight.HasValue || b.InWeight.HasValue)
        {
            double left = 0, right = 1;
            for (int i = 0; i < 20; i++)
            {
                double middle = (left + right) / 2;
                if (Bezier(0, weightA, 1 - weightB, 1, middle) < t)
                    left = middle;
                else
                    right = middle;
            }
            t = (left + right) / 2;
        }

        return Bezier(a.Value, a.Value + length * weightA * a.OutSlope,
            b.Value - length * weightB * b.InSlope, b.Value, t);
    }

    private static double Bezier(double a, double b, double c, double d, double t)
    {
        double u = 1 - t;
        return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
    }
}

internal sealed class FacePose(FaceClip clip)
{
    private readonly (FaceCurve Curve, FaceMorphs.Target[] Targets)[] tracks = clip.Curves
        .Select(track => (track.Value, FaceMorphs.GetTargets(track.Key)))
        .Where(track => track.Item2.Length > 0 && track.Value.KeyCount > 0).ToArray();

    public int TrackCount => tracks.Length;
    public int[] Channels { get; } = clip.Curves.Keys.SelectMany(FaceMorphs.GetTargets)
        .Select(target => target.Index).Distinct().ToArray();

    public void Evaluate(double elapsed, Span<byte> result)
    {
        result.Clear();
        double time = clip.Start + elapsed;
        foreach (var (curve, targets) in tracks)
        {
            double weight = curve.Evaluate(time);
            if (!double.IsFinite(weight))
                continue;
            foreach (var (index, gain) in targets)
            {
                byte value = (byte)Math.Round(Math.Clamp(weight / 100, 0, 1) * gain * 255);
                result[index] = Math.Max(result[index], value);
            }
        }
    }
}

internal static class FaceMorphs
{
    public readonly record struct Target(int Index, double Gain);

    private static readonly Dictionary<string, Target[]> Rules = new(StringComparer.Ordinal);
    private static readonly Dictionary<uint, string> Hashes = new();

    static FaceMorphs()
    {
        Add("まばたき|瞬き", Eyes("EyeClosed", 1));
        Add("ウィンク|ウインク|ウィンク２|ｳｨﾝｸ2|ｳｨﾝｸ２", [("EyeClosedLeft", 1)]);
        Add("ウィンク右|ウインク右|ウィンク２右|ｳｨﾝｸ右|ｳｨﾝｸ2右|ｳｨﾝｸ２右", [("EyeClosedRight", 1)]);
        Add("笑い|なごみ|はぅ", [.. Eyes("EyeClosed", 0.8), .. Eyes("EyeSquint", 1)]);
        Add("瞳小|瞳", Eyes("EyeConstrict", 1));
        Add("びっくり", Eyes("EyeWide", 1));
        Add("じと目|ジト目", [.. Eyes("EyeClosed", 0.4), .. Eyes("EyeSquint", 0.7)]);
        Add("にやり|にやり２|にっこり|口角上げ", Eyes("MouthCornerPull", 1));
        Add("口角下げ", Eyes("MouthFrown", 1));
        Add("あ|あ２|ワ", [("JawOpen", 1), .. Eyes("MouthLowerDown", 0.35)]);
        Add("い", [("JawOpen", 0.25), .. Eyes("MouthStretch", 0.85)]);
        Add("う", [("JawOpen", 0.2), .. Lips("LipPucker", 1)]);
        Add("え", [("JawOpen", 0.55), .. Eyes("MouthStretch", 0.6)]);
        Add("お", [("JawOpen", 0.7), .. Lips("LipFunnel", 0.85)]);
        Add("ん", [("MouthClosed", 1)]);
        Add("べー|ぺろっ", [("JawOpen", 0.35), ("TongueOut", 1)]);
        Add("困る", Eyes("BrowInnerUp", 1));
        Add("怒り|下", Eyes("BrowLowerer", 1));
        Add("上", [.. Eyes("BrowInnerUp", 0.8), .. Eyes("BrowOuterUp", 0.8)]);
        Add("にこり", Eyes("BrowOuterUp", 0.65));

        foreach (string name in TrackingKeys.Names)
            Add(name, [(name, 1)]);
    }

    public static Target[] GetTargets(string name) =>
        Rules.GetValueOrDefault(name.Normalize(NormalizationForm.FormKC)) ?? [];

    public static string NameForHash(uint hash) => Hashes.GetValueOrDefault(hash) ?? $"0x{hash:X8}";

    private static void Add(string names, (string Name, double Gain)[] targets)
    {
        Target[] resolved = targets.Select(target =>
            new Target(Array.IndexOf(TrackingKeys.Names, target.Name), target.Gain)).ToArray();
        if (resolved.Any(target => target.Index < 0))
            throw new InvalidOperationException("A face morph references an unknown tracking key.");

        foreach (string alias in names.Split('|'))
        {
            string normalized = alias.Normalize(NormalizationForm.FormKC);
            Rules[normalized] = resolved;
            foreach (string spelling in new[] { alias, normalized, alias.Normalize(NormalizationForm.FormD) })
            {
                Hashes.TryAdd(Crc32(spelling), normalized);
                Hashes.TryAdd(Crc32("blendShape." + spelling), normalized);
            }
        }
    }

    private static uint Crc32(string name)
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

    private static (string, double)[] Eyes(string prefix, double gain) =>
        [(prefix + "Right", gain), (prefix + "Left", gain)];

    private static (string, double)[] Lips(string prefix, double gain) =>
        [.. Eyes(prefix + "Upper", gain), .. Eyes(prefix + "Lower", gain)];
}
