using System.Globalization;
using System.Numerics;

namespace OscTester;

internal static class HumanoidChannels
{
    // Modern Unity clip attributes place Motion, Root and four IK targets before the muscles.
    public const int Root = 7;
    public const int FirstMuscle = 42;
    public const int MuscleCount = 95;
    public const int PoseSize = MuscleCount + 10;

    private static readonly string[] RootParameters =
    [
        "DanceX", "DanceY", "DanceZ",
        "BodyQXPositive", "BodyQXNegative", "BodyQYPositive", "BodyQYNegative",
        "BodyQZPositive", "BodyQZNegative", "BodyQW"
    ];

    private static readonly string[] BodyMuscles =
    [
        "Spine Front-Back", "Spine Left-Right", "Spine Twist Left-Right",
        "Chest Front-Back", "Chest Left-Right", "Chest Twist Left-Right",
        "UpperChest Front-Back", "UpperChest Left-Right", "UpperChest Twist Left-Right",
        "Neck Nod Down-Up", "Neck Tilt Left-Right", "Neck Turn Left-Right",
        "Head Nod Down-Up", "Head Tilt Left-Right", "Head Turn Left-Right",
        "Left Eye Down-Up", "Left Eye In-Out", "Right Eye Down-Up",
        "Right Eye In-Out", "Jaw Close", "Jaw Left-Right",
        "Left Upper Leg Front-Back", "Left Upper Leg In-Out", "Left Upper Leg Twist In-Out",
        "Left Lower Leg Stretch", "Left Lower Leg Twist In-Out", "Left Foot Up-Down",
        "Left Foot Twist In-Out", "Left Toes Up-Down", "Right Upper Leg Front-Back",
        "Right Upper Leg In-Out", "Right Upper Leg Twist In-Out", "Right Lower Leg Stretch",
        "Right Lower Leg Twist In-Out", "Right Foot Up-Down", "Right Foot Twist In-Out",
        "Right Toes Up-Down", "Left Shoulder Down-Up", "Left Shoulder Front-Back",
        "Left Arm Down-Up", "Left Arm Front-Back", "Left Arm Twist In-Out",
        "Left Forearm Stretch", "Left Forearm Twist In-Out", "Left Hand Down-Up",
        "Left Hand In-Out", "Right Shoulder Down-Up", "Right Shoulder Front-Back",
        "Right Arm Down-Up", "Right Arm Front-Back", "Right Arm Twist In-Out",
        "Right Forearm Stretch", "Right Forearm Twist In-Out", "Right Hand Down-Up",
        "Right Hand In-Out",
    ];

    private static readonly Dictionary<string, int> NamedAttributes = BuildNamedAttributes();

    public static bool Supports(uint attribute) => attribute < Root + 7
        || attribute is >= FirstMuscle and < FirstMuscle + MuscleCount;

    public static bool TryGetAttribute(string property, out int attribute) =>
        NamedAttributes.TryGetValue(property, out attribute);

    public static string ParameterName(int index) => index < MuscleCount
        ? "Muscle" + index.ToString("D2", CultureInfo.InvariantCulture)
        : RootParameters[index - MuscleCount];

    private static Dictionary<string, int> BuildNamedAttributes()
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < BodyMuscles.Length; i++)
            result.Add(BodyMuscles[i], FirstMuscle + i);

        int finger = FirstMuscle + BodyMuscles.Length;
        foreach (string side in new[] { "Left", "Right" })
        {
            foreach (string name in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                foreach (string motion in new[] { "1 Stretched", "Spread", "2 Stretched", "3 Stretched" })
                {
                    result.Add($"{side} {name} {motion}", finger);
                    result.Add($"{side}Hand.{name}.{motion}", finger);
                    finger++;
                }
            }
        }

        string[] components = ["T.x", "T.y", "T.z", "Q.x", "Q.y", "Q.z", "Q.w"];
        for (int i = 0; i < components.Length; i++)
        {
            result.Add("Motion" + components[i], i);
            result.Add("Root" + components[i], Root + i);
        }
        return result;
    }
}

internal sealed class HumanoidPose
{
    private const double TranslationRange = 5;
    private const double MuscleRange = 3;

    private readonly FaceCurve?[] muscles = new FaceCurve?[HumanoidChannels.MuscleCount];
    private readonly FaceCurve?[] root = new FaceCurve?[7];
    private readonly double start;
    private readonly double initialX, verticalOrigin, initialZ;
    private readonly double cos, sin;
    private readonly Quaternion headingInverse;

    public HumanoidPose(FaceClip clip)
    {
        start = clip.Start;
        var curves = clip.HumanoidCurves;
        for (int i = 0; i < muscles.Length; i++)
            muscles[i] = curves.GetValueOrDefault(HumanoidChannels.FirstMuscle + i);
        MuscleCount = muscles.Count(curve => curve is not null);

        // RootT and RootQ describe the baked humanoid body. Motion is a fallback for motion-only clips.
        int translation = Enumerable.Range(HumanoidChannels.Root, 3).Any(curves.ContainsKey)
            ? HumanoidChannels.Root : 0;
        int rotation = Enumerable.Range(HumanoidChannels.Root + 3, 4).Any(curves.ContainsKey)
            ? HumanoidChannels.Root : 0;
        for (int i = 0; i < root.Length; i++)
            root[i] = curves.GetValueOrDefault((i < 3 ? translation : rotation) + i);

        HasRoot = root.Any(curve => curve is not null);
        initialX = Sample(root[0], start);
        verticalOrigin = translation == HumanoidChannels.Root && root[1] is not null ? 1 : Sample(root[1], start);
        initialZ = Sample(root[2], start);
        var initialRotation = ReadRotation(start);
        var forward = Vector3.Transform(Vector3.UnitZ, initialRotation);
        double initialYaw = Math.Atan2(forward.X, forward.Z);
        (sin, cos) = Math.SinCos(initialYaw);
        headingInverse = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)-initialYaw);
    }

    public int MuscleCount { get; }
    public bool HasRoot { get; }
    public bool HasTracks => MuscleCount != 0 || HasRoot;

    public float EvaluateMuscle(int index, double elapsed) =>
        Normalize(Sample(muscles[index], start + elapsed) / MuscleRange);

    public Quaternion EvaluateRotation(double elapsed) =>
        Quaternion.Normalize(headingInverse * ReadRotation(start + elapsed));

    public Vector3 EvaluateTranslation(double elapsed)
    {
        double time = start + elapsed;
        double x = Sample(root[0], time) - initialX;
        double y = Sample(root[1], time) - verticalOrigin;
        double z = Sample(root[2], time) - initialZ;
        return new Vector3((float)(cos * x - sin * z), (float)y, (float)(sin * x + cos * z));
    }

    public float[] Evaluate(double elapsed)
    {
        var values = new float[HumanoidChannels.PoseSize];
        for (int i = 0; i < muscles.Length; i++)
            values[i] = EvaluateMuscle(i, elapsed);

        int offset = HumanoidChannels.MuscleCount;
        var translation = EvaluateTranslation(elapsed);
        values[offset] = Normalize(translation.X / TranslationRange);
        values[offset + 1] = Normalize(translation.Y / TranslationRange);
        values[offset + 2] = Normalize(translation.Z / TranslationRange);
        var bodyRotation = EvaluateRotation(elapsed);
        // Equivalent quaternions q and -q use the hemisphere with a nonnegative W.
        float sign = bodyRotation.W < 0 ? -1 : 1;
        values[offset + 3] = Math.Max(0, sign * bodyRotation.X);
        values[offset + 4] = Math.Max(0, -sign * bodyRotation.X);
        values[offset + 5] = Math.Max(0, sign * bodyRotation.Y);
        values[offset + 6] = Math.Max(0, -sign * bodyRotation.Y);
        values[offset + 7] = Math.Max(0, sign * bodyRotation.Z);
        values[offset + 8] = Math.Max(0, -sign * bodyRotation.Z);
        values[offset + 9] = Math.Abs(bodyRotation.W);
        return values;
    }

    private Quaternion ReadRotation(double time)
    {
        var rotation = new Quaternion((float)Sample(root[3], time), (float)Sample(root[4], time),
            (float)Sample(root[5], time), (float)Sample(root[6], time, 1));
        float lengthSquared = rotation.LengthSquared();
        return float.IsFinite(lengthSquared) && lengthSquared > 1e-12f
            ? Quaternion.Normalize(rotation) : Quaternion.Identity;
    }

    private static double Sample(FaceCurve? curve, double time, double fallback = 0)
    {
        double value = curve?.Evaluate(time) ?? fallback;
        return double.IsFinite(value) ? value : fallback;
    }

    private static float Normalize(double value) => (float)Math.Clamp(value, -1, 1);
}
