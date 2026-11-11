using System.Numerics;

namespace OscTester;

internal enum CameraProperty { Position, Quaternion, Euler, Scale, FieldOfView }

internal sealed class CameraNode
{
    public FaceCurve?[] Position { get; } = new FaceCurve?[3];
    public FaceCurve?[] Rotation { get; } = new FaceCurve?[4];
    public FaceCurve?[] Euler { get; } = new FaceCurve?[3];
    public FaceCurve?[] Scale { get; } = new FaceCurve?[3];
    public FaceCurve? FieldOfView { get; private set; }

    public void Add(CameraProperty property, int component, FaceCurve curve)
    {
        switch (property)
        {
            case CameraProperty.Position: Position[component] ??= curve; break;
            case CameraProperty.Quaternion: Rotation[component] ??= curve; break;
            case CameraProperty.Euler: Euler[component] ??= curve; break;
            case CameraProperty.Scale: Scale[component] ??= curve; break;
            case CameraProperty.FieldOfView: FieldOfView ??= curve; break;
        }
    }
}

internal sealed class CameraTrackBuilder
{
    private const string Pivot = "Camera_root";
    private const string Boom = Pivot + "/Camera_root_1";
    private const string Lens = Boom + "/Camera";
    private static readonly uint PivotHash = UnityAnimationText.BindingHash(Pivot);
    private static readonly uint BoomHash = UnityAnimationText.BindingHash(Boom);
    private static readonly uint LensHash = UnityAnimationText.BindingHash(Lens);
    private static readonly uint FovHash = UnityAnimationText.BindingHash("field of view");

    private readonly Dictionary<uint, CameraNode> nodes = [];
    private readonly Dictionary<uint, string> namedPaths = [];

    public static bool IsMmdPath(uint path) => path == PivotHash || path == BoomHash || path == LensHash;

    public static bool IsFieldOfView(int type, uint attribute) => type == 20 && attribute == FovHash;

    public static CameraProperty? TransformProperty(uint attribute) => attribute switch
    {
        1 => CameraProperty.Position, 2 => CameraProperty.Quaternion,
        3 => CameraProperty.Scale, 4 => CameraProperty.Euler, _ => null
    };

    public void AddNamed(string path, int type, string attribute, List<FaceKey> keys)
    {
        if (keys.Count == 0)
            return;
        if (type == 20 && attribute == "field of view")
            AddNamed(path, CameraProperty.FieldOfView, 0, keys);
        else if (type == 4 && attribute.Length > 2 && attribute[^2] == '.')
        {
            int component = "xyzw".IndexOf(attribute[^1]);
            CameraProperty? property = attribute[..^2] switch
            {
                "m_LocalPosition" => CameraProperty.Position,
                "m_LocalRotation" => CameraProperty.Quaternion,
                "localEulerAnglesRaw" or "localEulerAnglesBaked" or "localEulerAngles" => CameraProperty.Euler,
                "m_LocalScale" => CameraProperty.Scale,
                _ => null
            };
            if (property is CameraProperty value && component >= 0
                && component < (value == CameraProperty.Quaternion ? 4 : 3))
                AddNamed(path, value, component, keys);
        }
    }

    public void AddNamed(string path, CameraProperty property, int component, List<FaceKey> keys)
    {
        uint hash = UnityAnimationText.BindingHash(path);
        namedPaths.TryAdd(hash, path);
        Add(hash, property, component, new FaceCurve(keys));
    }

    public void Add(uint path, CameraProperty property, int component, FaceCurve curve)
    {
        if (curve.KeyCount == 0)
            return;
        if (!nodes.TryGetValue(path, out CameraNode? node))
            nodes.Add(path, node = new CameraNode());
        node.Add(property, component, curve);
    }

    public CameraTrack? Build(bool standalone)
    {
        // MMD camera exports animate the pivot and lens offset on separate transforms.
        if (nodes.ContainsKey(PivotHash) && (nodes.ContainsKey(BoomHash) || nodes.ContainsKey(LensHash)))
            return FromPath(Lens, nodes.GetValueOrDefault(LensHash)?.FieldOfView);

        uint[] lenses = nodes.Where(pair => pair.Value.FieldOfView is not null).Select(pair => pair.Key).ToArray();
        if (lenses.Length == 1)
        {
            uint lens = lenses[0];
            return namedPaths.TryGetValue(lens, out string? path)
                ? FromPath(path, nodes[lens].FieldOfView)
                : new CameraTrack([nodes[lens]], nodes[lens].FieldOfView);
        }
        if (!standalone || lenses.Length != 0)
            return null;

        string[] namedCameras = namedPaths.Where(pair => nodes.ContainsKey(pair.Key)
            && (pair.Value == "Camera" || pair.Value.EndsWith("/Camera", StringComparison.Ordinal)))
            .Select(pair => pair.Value).ToArray();
        return namedCameras.Length == 1 ? FromPath(namedCameras[0], null)
            : nodes.ContainsKey(0) ? FromPath("", null) : null;
    }

    private CameraTrack FromPath(string path, FaceCurve? fieldOfView)
    {
        var hierarchy = new List<CameraNode>();
        while (true)
        {
            if (nodes.TryGetValue(UnityAnimationText.BindingHash(path), out CameraNode? node))
                hierarchy.Add(node);
            if (path.Length == 0)
                break;
            int separator = path.LastIndexOf('/');
            path = separator < 0 ? "" : path[..separator];
        }
        hierarchy.Reverse();
        return new CameraTrack(hierarchy.ToArray(), fieldOfView);
    }
}

internal sealed class CameraTrack(CameraNode[] hierarchy, FaceCurve? fieldOfView)
{
    public (Vector3? Position, Quaternion? Rotation, float? FieldOfView) Evaluate(double time)
    {
        Vector3 position = Vector3.Zero;
        Quaternion rotation = Quaternion.Identity;
        Matrix4x4 parent = Matrix4x4.Identity;
        bool hasPosition = false, hasRotation = false;
        foreach (CameraNode node in hierarchy)
        {
            Vector3 localPosition = SampleVector(node.Position, time);
            Quaternion localRotation = Quaternion.Identity;
            if (node.Rotation.Any(curve => curve is not null))
            {
                var raw = new Quaternion(SampleVector(node.Rotation, time), Sample(node.Rotation[3], time, 1));
                if (float.IsFinite(raw.LengthSquared()) && raw.LengthSquared() > 1e-12f)
                    localRotation = Quaternion.Normalize(raw);
                hasRotation = true;
            }
            else if (node.Euler.Any(curve => curve is not null))
            {
                Vector3 euler = SampleVector(node.Euler, time) * (MathF.PI / 180);
                localRotation = Quaternion.CreateFromYawPitchRoll(euler.Y, euler.X, euler.Z);
                hasRotation = true;
            }

            hasPosition |= node.Position.Any(curve => curve is not null);
            var local = Matrix4x4.CreateScale(SampleVector(node.Scale, time, 1))
                * Matrix4x4.CreateFromQuaternion(localRotation) * Matrix4x4.CreateTranslation(localPosition);
            parent = local * parent;
            position = parent.Translation;
            rotation = Quaternion.Normalize(rotation * localRotation);
        }
        return (hasPosition ? position : null, hasRotation ? rotation : null,
            fieldOfView is null ? null : Sample(fieldOfView, time, 38));
    }

    private static Vector3 SampleVector(FaceCurve?[] curves, double time, float fallback = 0) =>
        new(Sample(curves[0], time, fallback), Sample(curves[1], time, fallback), Sample(curves[2], time, fallback));

    private static float Sample(FaceCurve? curve, double time, float fallback)
    {
        float value = curve is null ? fallback : (float)curve.Evaluate(time);
        return float.IsFinite(value) ? value : fallback;
    }
}
