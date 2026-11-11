using System.Numerics;

namespace OscTester;

internal sealed class CameraPose(FaceClip clip, CameraTrack track)
{
    private const float TranslationRange = 100;
    private const float NeutralFov = 38, MinFov = 1, MaxFov = 179;
    private const float QuarterTurn = MathF.PI / 2;
    private static readonly Vector3 RestPosition = new(0, 1.2f, 2.65f);
    private static readonly Quaternion RestRotation = Quaternion.CreateFromYawPitchRoll(MathF.PI, Radians(2), 0);
    private static readonly Quaternion InverseRestRotation = Quaternion.Inverse(RestRotation);
    private static readonly Quaternion HalfTurnAboutY = new(0, 1, 0, 0);

    public string Name => clip.Name;

    public float[] Evaluate(double elapsed, bool flipDirection, out bool limited)
    {
        // Sample at the camera clip's frame rate so interpolation between frames does not soften cuts.
        double frameTime = Math.Floor(elapsed * clip.FrameRate + 0.0001) / clip.FrameRate;
        double time = clip.Start + (elapsed >= clip.Duration ? clip.Duration : frameTime);
        var (position, rotation, fov) = track.Evaluate(time);
        Vector3 cameraPosition = position ?? RestPosition;
        Quaternion cameraRotation = rotation ?? RestRotation;
        if (flipDirection)
        {
            cameraPosition.Z = -cameraPosition.Z;
            // Reflect the forward and up vectors, then restore a right-handed camera basis.
            var mirroredRotation = new Quaternion(-cameraRotation.X, -cameraRotation.Y, cameraRotation.Z, cameraRotation.W);
            cameraRotation = mirroredRotation * HalfTurnAboutY;
        }
        Vector3 offset = cameraPosition - RestPosition;
        Quaternion relative = cameraRotation * InverseRestRotation;
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, relative);
        Vector3 right = Vector3.Transform(Vector3.UnitX, relative);
        Vector3 up = Vector3.Transform(Vector3.UnitY, relative);

        float horizontal = MathF.Sqrt(forward.X * forward.X + forward.Z * forward.Z);
        float pitch = MathF.Atan2(-forward.Y, horizontal);
        bool vertical = horizontal < 1e-6f;
        float yaw = vertical ? MathF.Atan2(-right.Z, right.X) : MathF.Atan2(forward.X, forward.Z);
        float roll = vertical ? 0 : MathF.Atan2(right.Y, up.Y);

        float fieldOfView = fov ?? NeutralFov;
        float fovRange = fieldOfView < NeutralFov ? NeutralFov - MinFov : MaxFov - NeutralFov;
        // The receiver uses separate Y, X and Z rotation nodes ahead of the fixed lens rotation.
        float[] frame =
        [
            offset.X / TranslationRange, offset.Y / TranslationRange, offset.Z / TranslationRange,
            RotationWeight(yaw, 2), RotationWeight(pitch, 1), RotationWeight(roll, 2),
            (fieldOfView - NeutralFov) / fovRange
        ];
        limited = frame.Any(value => !float.IsFinite(value) || MathF.Abs(value) > 1.0001f);
        for (int i = 0; i < frame.Length; i++)
            frame[i] = float.IsNaN(frame[i]) ? 0 : Math.Clamp(frame[i], -1, 1);
        return frame;
    }

    private static float RotationWeight(float angle, int segments)
    {
        // Each 90-degree segment uses Unity's normalized linear quaternion blend.
        float quarters = Math.Clamp(MathF.Abs(angle) / QuarterTurn, 0, segments);
        int segment = Math.Min((int)quarters, segments - 1);
        float halfAngle = (quarters - segment) * (QuarterTurn / 2);
        float sine = MathF.Sin(halfAngle);
        float blend = sine / (sine + MathF.Sin(QuarterTurn / 2 - halfAngle));
        return MathF.CopySign((segment + blend) / segments, angle);
    }

    private static float Radians(float degrees) => degrees * (MathF.PI / 180);
}
