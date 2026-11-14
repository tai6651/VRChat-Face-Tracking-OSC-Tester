using System.Numerics;
using static OscTester.MmdAnywhereLayout;

namespace OscTester;

internal sealed class MmdAnywherePose
{
    private readonly HumanoidPose body;
    private readonly FacePose face;

    public static readonly byte[] IdlePacket = OscPacket.CreateFacePacket(new byte[WireSize]);

    public MmdAnywherePose(HumanoidPose body, FacePose face)
    {
        this.body = body;
        this.face = face;
    }

    private static readonly int[][] FaceInputs =
    [
        Keys("EyeClosedLeft"), Keys("EyeClosedRight"), Keys("JawOpen"),
        Keys("MouthStretchLeft", "MouthStretchRight"),
        Keys("LipPuckerUpperLeft", "LipPuckerUpperRight", "LipPuckerLowerLeft", "LipPuckerLowerRight",
            "LipFunnelUpperLeft", "LipFunnelUpperRight", "LipFunnelLowerLeft", "LipFunnelLowerRight"),
        Keys("MouthCornerPullLeft", "MouthCornerPullRight"),
        Keys("EyeConstrictLeft", "EyeConstrictRight")
    ];

    public byte[] Evaluate(double time, Quaternion? previousRotation, out Quaternion rotation)
    {
        rotation = body.EvaluateRotation(time);
        // Keep successive samples on the same quaternion hemisphere before native interpolation.
        if (Quaternion.Dot(previousRotation ?? Quaternion.Identity, rotation) < 0)
            rotation = Quaternion.Negate(rotation);
        Span<byte> tracking = stackalloc byte[OscPacket.FacePayloadSize];
        face.Evaluate(time, tracking);
        return CreatePacket(body, time, rotation, tracking);
    }

    private static byte[] CreatePacket(HumanoidPose body, double time, Quaternion rotation, ReadOnlySpan<byte> tracking)
    {
        Span<byte> values = stackalloc byte[WireSize];
        values.Clear();
        for (int i = 0; i < MuscleCarriers.Length; i++)
        {
            int muscle = MuscleCarriers[i];
            float value = body.EvaluateMuscle(muscle, time);
            if (muscle is 15 or 16)
                value = (value + (muscle == 15 ? 1 : -1) * body.EvaluateMuscle(muscle + 2, time)) * .5f;
            else if (Array.IndexOf(SharedCurlStarts, muscle) >= 0)
                value = (value + body.EvaluateMuscle(muscle + 1, time)) * .5f;
            values[MuscleKey(i)] = Encode(value);
        }
        var translation = body.EvaluateTranslation(time);
        EncodeHorizontal(translation.X, 0, RootXExtraKey, values);
        values[RootKeys[1]] = EncodeStep((int)MathF.Round(TranslationSteps(translation.Y, 1)));
        EncodeHorizontal(translation.Z, 2, RootZExtraKey, values);
        values[RootKeys[3]] = Encode(rotation.X);
        values[RootKeys[4]] = Encode(rotation.Y);
        values[RootKeys[5]] = Encode(rotation.Z);
        values[RootKeys[6]] = Encode(rotation.W);
        for (int i = 0; i < FaceNames.Length; i++)
        {
            byte weight = 0;
            foreach (int key in FaceInputs[i])
                weight = Math.Max(weight, tracking[key]);
            values[FaceKey(i)] = weight;
        }
        return OscPacket.CreateFacePacket(values);
    }

    private static byte Encode(float value)
    {
        int step = (int)MathF.Round(Math.Clamp(value, -1, 1) * SignedSteps);
        return EncodeStep(step);
    }

    private static void EncodeHorizontal(float value, int axis, int extraKey, Span<byte> values)
    {
        float steps = TranslationSteps(value, axis);
        int coarse = (int)MathF.Round(steps);
        values[RootKeys[axis]] = (byte)(Center + coarse);
        values[extraKey] = (byte)MathF.Round(HorizontalFineMin + (steps - coarse + .5f) * HorizontalFineSpan);
    }

    private static float TranslationSteps(float value, int axis)
    {
        float distance = Math.Min(Math.Abs(value), TranslationRange);
        float fineRange = FineTranslationRange(axis);
        float steps = distance <= fineRange
            ? distance / fineRange * FineTranslationSteps
            : FineTranslationSteps + (distance - fineRange) / (TranslationRange - fineRange) * (SignedSteps - FineTranslationSteps);
        return Math.Sign(value) * steps;
    }

    private static byte EncodeStep(int step) => (byte)(Center + step + Math.Sign(step) * ZeroGap);

    private static int[] Keys(params string[] names) => names.Select(name => Array.IndexOf(TrackingKeys.Names, name)).ToArray();
}
