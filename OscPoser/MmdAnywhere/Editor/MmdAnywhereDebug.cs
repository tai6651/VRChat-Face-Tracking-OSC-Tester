#if VRC_SDK_VRCSDK3
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using static OscTester.MmdAnywhereLayout;
using Object = UnityEngine.Object;

internal static class MmdAnywhereDebug
{
    private const string Menu = "Tools/MMD Anywhere/Install Root Telemetry Panel";
    private const string Panel = "MMD Root Telemetry";
    private const string RawPanel = "MMD Native Face Bytes";
    private const string RawToggle = "MmdAnywhere/DebugRaw";
    private const string RawTitle = "MMD Raw Bytes";
    private const int RawByteCount = WireSize;
    private const int RawRows = 17;
    private const int RawColumns = 6;
    private static string Overlay(int axis) => Panel + "/Action " + "XYZ"[axis];
    private const string DebugToggle = "MmdAnywhere/Debug";
    private const string Folder = "Assets/MMD Anywhere Prototype/Root Telemetry";
    private const string Title = "MMD Root Debug";

    [MenuItem(Menu)]
    public static void Install()
    {
        var selected = Selection.activeGameObject;
        var avatar = selected ? selected.GetComponentInParent<VRCAvatarDescriptor>() : null;
        if (!avatar || !avatar.gameObject.scene.path.StartsWith("Assets/MMD Anywhere Prototype/", StringComparison.Ordinal))
            throw new InvalidOperationException("Select the avatar in its MMD Anywhere test scene.");
        if (avatar.transform.Find(RawPanel))
            throw new InvalidOperationException("This avatar already has the 102-byte telemetry panel.");

        var layers = avatar.baseAnimationLayers;
        int actionIndex = Array.FindIndex(layers, layer => layer.type == VRCAvatarDescriptor.AnimLayerType.Action);
        int fxIndex = Array.FindIndex(layers, layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX);
        var actionSource = actionIndex < 0 ? null : layers[actionIndex].animatorController as AnimatorController;
        var fxSource = fxIndex < 0 ? null : layers[fxIndex].animatorController as AnimatorController;
        if (!actionSource || !fxSource || !actionSource.parameters.Any(p => p.name == EnableParameter && p.type == AnimatorControllerParameterType.Bool))
            throw new InvalidOperationException("Install the native MMD Anywhere controller first.");

        var rawShader = Shader.Find("MMD Anywhere/Native Face Bytes");
        var rawLabels = PanelLabels(rawShader);
        if (!rawShader || !rawLabels)
            throw new InvalidOperationException("Import the MMD Anywhere native byte shader and label texture.");

        if (avatar.transform.Find(Panel))
        {
            InstallRawPanel(avatar, fxSource, rawShader, rawLabels);
            SavePanel(avatar);
            return;
        }

        var shader = Shader.Find("MMD Anywhere/Root Telemetry");
        var labels = PanelLabels(shader);
        if (!shader || !labels)
            throw new InvalidOperationException("Import the MMD Anywhere debug shader and label texture.");

        var menu = avatar.expressionsMenu ? Object.Instantiate(avatar.expressionsMenu)
            : ScriptableObject.CreateInstance<VRCExpressionsMenu>();
        if (menu.controls.Count > 6)
            throw new InvalidOperationException("The test avatar needs two open slots in its Expressions menu.");
        var parameters = Object.Instantiate(avatar.expressionParameters);
        var rawRoot = RawRootRows().Select(i => new VRCExpressionParameters.Parameter
        {
            name = RawParameter(i),
            valueType = VRCExpressionParameters.ValueType.Float,
            networkSynced = false
        });
        parameters.parameters = parameters.parameters.Where(p => p.name != DebugToggle).Concat(rawRoot)
            .Append(new VRCExpressionParameters.Parameter
            {
                name = DebugToggle,
                valueType = VRCExpressionParameters.ValueType.Bool,
                networkSynced = true,
                saved = false
            }).ToArray();
        if (parameters.CalcTotalCost() + 1 > VRCExpressionParameters.MAX_PARAMETER_COST)
            throw new InvalidOperationException("The debug panels need two synced expression bits.");

        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/MMD Anywhere Prototype", "Root Telemetry");
        var action = CopyController(actionSource, "Action");
        var fx = CopyController(fxSource, "FX");
        AddActionReadout(action);
        AddFxReadout(fx);

        var material = new Material(shader) { name = Panel, mainTexture = labels, renderQueue = 3000 };
        AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath(Folder + "/Panel.mat"));
        var mesh = new Mesh
        {
            name = "Telemetry panel",
            vertices = new[] { new Vector3(-.5f, -.328125f), new Vector3(.5f, -.328125f),
                new Vector3(.5f, .328125f), new Vector3(-.5f, .328125f) },
            uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
            triangles = new[] { 0, 1, 2, 0, 2, 3 },
            // Billboarding rotates the quad in the shader, so bound every orientation.
            bounds = new Bounds(Vector3.zero, Vector3.one * 1.25f)
        };
        AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(Folder + "/Panel.asset"));

        var screen = new GameObject(Panel, typeof(MeshFilter), typeof(MeshRenderer));
        screen.transform.SetParent(avatar.transform, false);
        screen.transform.localPosition = new Vector3(1.25f, avatar.ViewPosition.y + .35f, 0);
        ConfigureRenderer(screen, mesh, material);
        AddOverlays(screen.transform, mesh, material);
        screen.SetActive(false);

        menu.controls.Add(new VRCExpressionsMenu.Control
        {
            name = Title,
            type = VRCExpressionsMenu.Control.ControlType.Toggle,
            parameter = new VRCExpressionsMenu.Control.Parameter { name = DebugToggle },
            value = 1
        });
        AssetDatabase.CreateAsset(menu, AssetDatabase.GenerateUniqueAssetPath(Folder + "/Expressions.asset"));
        AssetDatabase.CreateAsset(parameters, AssetDatabase.GenerateUniqueAssetPath(Folder + "/Parameters.asset"));
        layers[actionIndex].animatorController = action;
        layers[fxIndex].animatorController = fx;
        layers[fxIndex].mask = ExcludeActionReadout(fx, avatar);
        avatar.baseAnimationLayers = layers;
        avatar.expressionsMenu = menu;
        avatar.expressionParameters = parameters;
        avatar.customExpressions = true;
        foreach (int i in RawRootRows())
        {
            avatar.faceTrackingParameterBehaviors.Add(new VRCAvatarDescriptor.FaceTrackingParameterBehavior
            {
                targetMode = VRCAvatarDescriptor.ParameterBehaviorTargetMode.ForwardToAnimator,
                combineMode = VRCAvatarDescriptor.ParameterBehaviorCombineMode.Custom,
                outputParameterName = RawParameter(i),
                customExpression = MmdAnywherePrototype.KeyName(RootKeys[i]),
                inputTrackingKeys = Array.Empty<int>(),
                inputTrackingKeyNames = Array.Empty<string>(),
                inputInverted = Array.Empty<bool>()
            });
        }
        InstallRawPanel(avatar, fx, rawShader, rawLabels);
        SavePanel(avatar);
    }

    private static Texture2D PanelLabels(Shader shader)
    {
        string path = AssetDatabase.GetAssetPath(shader);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path.Replace(".shader", ".png"));
    }

    private static void SavePanel(VRCAvatarDescriptor avatar)
    {
        PrefabUtility.RecordPrefabInstancePropertyModifications(avatar);
        EditorUtility.SetDirty(avatar);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(avatar.gameObject.scene);
    }

    internal static void RefreshInstalledPanels(VRCAvatarDescriptor avatar, AnimatorController action, AnimatorController fx)
    {
        if (!avatar.transform.Find(Panel))
            return;

        var parameters = avatar.expressionParameters;
        var entries = parameters.parameters.ToList();
        foreach (int i in RawRootRows())
        {
            string parameter = RawParameter(i);
            MmdAnywherePrototype.Forward(avatar, parameter, MmdAnywherePrototype.KeyName(RootKeys[i]));
            entries.Add(new VRCExpressionParameters.Parameter
            {
                name = parameter, valueType = VRCExpressionParameters.ValueType.Float, networkSynced = false
            });
        }

        AddDebugToggle(DebugToggle);
        AddActionReadout(action);
        for (int i = fx.layers.Length - 1; i >= 0; i--)
        {
            string name = fx.layers[i].name;
            if (name == Panel || name.StartsWith(Panel + " ", StringComparison.Ordinal) ||
                name == RawPanel || name.StartsWith(RawPanel + " ", StringComparison.Ordinal))
                fx.RemoveLayer(i);
        }
        fx.parameters = fx.parameters.Where(p => !p.name.StartsWith("MmdAnywhere/", StringComparison.Ordinal)
            || p.name == EnableParameter || p.name == "MmdAnywhere/BlendWeight" ||
            Enumerable.Range(0, FaceNames.Length).Any(i => p.name == FaceParameter(i))).ToArray();
        AddFxReadout(fx);

        if (avatar.transform.Find(RawPanel))
        {
            AddDebugToggle(RawToggle);
            parameters.parameters = entries.ToArray();
            AddRawFxReadout(fx, ConfigureRawForwarding(avatar));
        }
        else
            parameters.parameters = entries.ToArray();

        var layers = avatar.baseAnimationLayers;
        int fxIndex = Array.FindIndex(layers, layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX);
        layers[fxIndex].mask = ExcludeActionReadout(fx, avatar);
        avatar.baseAnimationLayers = layers;
        EditorUtility.SetDirty(parameters);
        EditorUtility.SetDirty(fx);
        EditorUtility.SetDirty(action);
        PrefabUtility.RecordPrefabInstancePropertyModifications(avatar);
        EditorUtility.SetDirty(avatar);

        void AddDebugToggle(string name) => entries.Add(new VRCExpressionParameters.Parameter
        {
            name = name, valueType = VRCExpressionParameters.ValueType.Bool, networkSynced = true
        });
    }

    private static void InstallRawPanel(VRCAvatarDescriptor avatar, AnimatorController fx, Shader shader, Texture2D labels)
    {
        var menu = avatar.expressionsMenu;
        var parameters = avatar.expressionParameters;
        if (!menu || menu.controls.Count >= 8 || !parameters ||
            parameters.CalcTotalCost() + 1 > VRCExpressionParameters.MAX_PARAMETER_COST)
            throw new InvalidOperationException("The raw panel needs one Expressions menu slot and one synced bit.");

        var root = avatar.transform.Find(Panel);
        var screen = new GameObject(RawPanel, typeof(MeshFilter), typeof(MeshRenderer));
        screen.transform.SetParent(avatar.transform, false);
        screen.transform.localPosition = root.localPosition;
        var material = new Material(shader) { name = RawPanel, mainTexture = labels, renderQueue = 3000 };
        AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath(Folder + "/Native Face Bytes.mat"));
        var mesh = RawGrid();
        AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(Folder + "/Native Face Bytes.asset"));
        ConfigureRenderer(screen, mesh, material);
        screen.SetActive(false);

        AddRawFxReadout(fx, ConfigureRawForwarding(avatar));
        parameters.parameters = parameters.parameters.Append(new VRCExpressionParameters.Parameter
        {
            name = RawToggle,
            valueType = VRCExpressionParameters.ValueType.Bool,
            networkSynced = true,
            saved = false
        }).ToArray();
        menu.controls.Add(new VRCExpressionsMenu.Control
        {
            name = RawTitle,
            type = VRCExpressionsMenu.Control.ControlType.Toggle,
            parameter = new VRCExpressionsMenu.Control.Parameter { name = RawToggle },
            value = 1
        });
        EditorUtility.SetDirty(parameters);
        EditorUtility.SetDirty(menu);
        EditorUtility.SetDirty(fx);
    }

    private static void AddRawFxReadout(AnimatorController fx, string[] rawParameters)
    {
        var rootStates = fx.layers.Single(layer => layer.name == Panel).stateMachine.states;
        var rootHidden = rootStates.Single(state => state.state.name == "Hidden").state;
        var rootVisible = rootStates.Single(state => state.state.name == "Visible").state;
        var showRoot = rootHidden.transitions.Single(transition => transition.destinationState == rootVisible);
        AddParameter(fx, RawToggle, AnimatorControllerParameterType.Bool);
        showRoot.AddCondition(AnimatorConditionMode.IfNot, 0, RawToggle);
        EditorUtility.SetDirty(showRoot);
        Transition(rootVisible, rootHidden).AddCondition(AnimatorConditionMode.If, 0, RawToggle);
        var layer = NewLayer(fx, RawPanel, panelPath: RawPanel);
        var hidden = layer.AddState("Hidden");
        var visible = layer.AddState("Visible");
        hidden.writeDefaultValues = false;
        visible.writeDefaultValues = true;
        layer.defaultState = hidden;
        var active = EditorCurveBinding.FloatCurve(RawPanel, typeof(GameObject), "m_IsActive");
        hidden.motion = Clip(fx, "Hide native bytes", active, 0);
        Transition(hidden, visible).AddCondition(AnimatorConditionMode.If, 0, RawToggle);
        Transition(visible, hidden).AddCondition(AnimatorConditionMode.IfNot, 0, RawToggle);
        var tree = DirectTree(fx);
        tree.AddChild(Clip(fx, "Show native bytes", active, 1));
        for (int i = 0; i < rawParameters.Length; i++)
        {
            AddParameter(fx, rawParameters[i], AnimatorControllerParameterType.Float);
            tree.AddChild(Meter(fx, rawParameters[i], "_Rx" + i, RawPanel));
        }
        SetWeight(tree);
        visible.motion = tree;
        AddIndicator(fx, "UserFaceTracking", "_Tracking", RawPanel);
        AddIndicator(fx, "IsLocal", "_Local", RawPanel);
    }

    internal static string[] ConfigureRawForwarding(VRCAvatarDescriptor avatar)
    {
        var parameters = avatar.expressionParameters;
        var entries = parameters.parameters.ToList();
        var keys = new string[RawByteCount];
        for (int index = 0; index < RawByteCount; index++)
        {
            string key = MmdAnywherePrototype.KeyName(index);
            var existing = avatar.faceTrackingParameterBehaviors.FirstOrDefault(behavior =>
                behavior.targetMode == VRCAvatarDescriptor.ParameterBehaviorTargetMode.ForwardToAnimator &&
                behavior.combineMode == VRCAvatarDescriptor.ParameterBehaviorCombineMode.Custom &&
                behavior.customExpression == key);
            if (existing != null)
                keys[index] = existing.outputParameterName;
            else
            {
                string name = "MmdAnywhere/RawByte" + index.ToString("D3");
                keys[index] = name;
                MmdAnywherePrototype.Forward(avatar, name, key);
                if (entries.All(entry => entry.name != name))
                    entries.Add(new VRCExpressionParameters.Parameter
                    {
                        name = name, valueType = VRCExpressionParameters.ValueType.Float, networkSynced = false
                    });
            }
        }
        parameters.parameters = entries.ToArray();
        EditorUtility.SetDirty(parameters);
        return keys;
    }

    private static Mesh RawGrid()
    {
        // Each quad carries its byte index in UV1. Edge quads also cover the title and footer.
        var vertices = new Vector3[RawByteCount * 4];
        var uv = new Vector2[vertices.Length];
        var indices = new Vector2[vertices.Length];
        var triangles = new int[RawByteCount * 6];
        for (int column = 0; column < RawColumns; column++)
        for (int row = 0; row < RawRows; row++)
        {
            int index = column * RawRows + row;
            float left = column == 0 ? 0 : (32 + 160 * column) / 1024f;
            float right = column == RawColumns - 1 ? 1 : (32 + 160 * (column + 1)) / 1024f;
            float bottom = row == RawRows - 1 ? 0 : 1 - (136 + 48 * (row + 1)) / 1024f;
            float top = row == 0 ? 1 : 1 - (136 + 48 * row) / 1024f;
            int v = index * 4;
            uv[v] = new Vector2(left, bottom);
            uv[v + 1] = new Vector2(right, bottom);
            uv[v + 2] = new Vector2(left, top);
            uv[v + 3] = new Vector2(right, top);
            for (int corner = v; corner < v + 4; corner++)
            {
                vertices[corner] = new Vector3((uv[corner].x - .5f) * 1.6f, (uv[corner].y - .5f) * 1.6f, 0);
                indices[corner] = new Vector2(index, 0);
            }
            int t = index * 6;
            triangles[t] = v;
            triangles[t + 1] = v + 1;
            triangles[t + 2] = v + 2;
            triangles[t + 3] = v + 1;
            triangles[t + 4] = v + 3;
            triangles[t + 5] = v + 2;
        }
        return new Mesh
        {
            name = RawPanel, vertices = vertices, uv = uv, uv2 = indices, triangles = triangles,
            bounds = new Bounds(Vector3.zero, Vector3.one * 2.3f)
        };
    }

    private static void ConfigureRenderer(GameObject screen, Mesh mesh, Material material)
    {
        screen.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = screen.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private static void AddOverlays(Transform parent, Mesh mesh, Material material)
    {
        for (int i = 0; i < 3; i++)
        {
            string name = "Action " + "XYZ"[i];
            var overlay = new Material(material) { name = name, renderQueue = 3001 };
            overlay.SetFloat("_Overlay", 1);
            overlay.SetFloat("_Row", i);
            AssetDatabase.CreateAsset(overlay, AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + name + ".mat"));
            var readout = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            readout.transform.SetParent(parent, false);
            ConfigureRenderer(readout, mesh, overlay);
        }
    }

    private static AvatarMask ExcludeActionReadout(AnimatorController controller, VRCAvatarDescriptor avatar)
    {
        var layers = controller.layers;
        var source = layers[0].avatarMask;
        var mask = source ? Object.Instantiate(source) : new AvatarMask();
        mask.name = "FX without root telemetry markers";
        if (!source)
        {
            mask.AddTransformPath(avatar.transform, true);
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
        }
        for (int i = 0; i < mask.transformCount; i++)
        {
            if (Enumerable.Range(0, 3).Any(axis => mask.GetTransformPath(i) == Overlay(axis)))
                mask.SetTransformActive(i, false);
        }
        AssetDatabase.AddObjectToAsset(mask, controller);
        layers[0].avatarMask = mask;
        controller.layers = layers;
        return mask;
    }

    private static AnimatorController CopyController(AnimatorController source, string kind)
    {
        string path = AssetDatabase.GenerateUniqueAssetPath(Folder + "/MMD Root Debug " + kind + ".controller");
        if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path))
            throw new InvalidOperationException("Could not copy the " + kind + " controller.");
        return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
    }

    private static void AddActionReadout(AnimatorController controller)
    {
        var layer = NewLayer(controller, "MMD Root Telemetry Action", actionReadout: true);
        var idle = layer.AddState("Idle");
        var drive = layer.AddState("Playback");
        idle.writeDefaultValues = false;
        drive.writeDefaultValues = true;
        var standby = DirectTree(controller);
        for (int i = 0; i < 3; i++)
            standby.AddChild(ScaleClip(controller, i, 1));
        SetWeight(standby);
        idle.motion = standby;
        layer.defaultState = idle;

        var enter = Transition(idle, drive);
        enter.AddCondition(AnimatorConditionMode.If, 0, EnableParameter);
        enter.AddCondition(AnimatorConditionMode.If, 0, "UserFaceTracking");
        Transition(drive, idle).AddCondition(AnimatorConditionMode.IfNot, 0, EnableParameter);
        Transition(drive, idle).AddCondition(AnimatorConditionMode.IfNot, 0, "UserFaceTracking");
        var tree = DirectTree(controller);
        for (int i = 0; i < 3; i++)
        {
            string input = TranslationParameter(i);
            var meter = new BlendTree { name = input, blendParameter = input, useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(meter, controller);
            meter.AddChild(ScaleClip(controller, i, 2), 0);
            meter.AddChild(ScaleClip(controller, i, 3), 1);
            tree.AddChild(meter);
        }
        SetWeight(tree);
        drive.motion = tree;
    }

    private static void AddFxReadout(AnimatorController controller)
    {
        AddParameter(controller, DebugToggle, AnimatorControllerParameterType.Bool);
        AddParameter(controller, "IsLocal", AnimatorControllerParameterType.Bool);
        AddParameter(controller, "UserFaceTracking", AnimatorControllerParameterType.Bool);
        AddParameter(controller, EnableParameter, AnimatorControllerParameterType.Bool);
        var layer = NewLayer(controller, "MMD Root Telemetry");
        var hidden = layer.AddState("Hidden");
        var visible = layer.AddState("Visible");
        hidden.writeDefaultValues = false;
        visible.writeDefaultValues = true;
        hidden.motion = Clip(controller, "Hide panel", EditorCurveBinding.FloatCurve(Panel, typeof(GameObject), "m_IsActive"), 0);
        layer.defaultState = hidden;
        Transition(hidden, visible).AddCondition(AnimatorConditionMode.If, 0, DebugToggle);
        Transition(visible, hidden).AddCondition(AnimatorConditionMode.IfNot, 0, DebugToggle);

        var tree = DirectTree(controller);
        tree.AddChild(Clip(controller, "Show panel", EditorCurveBinding.FloatCurve(Panel, typeof(GameObject), "m_IsActive"), 1));
        for (int i = 0; i < RootProperties.Length; i++)
        {
            string parameter = i == 1 ? "MmdAnywhere/" + RootProperties[i] : RawParameter(i);
            AddParameter(controller, parameter, AnimatorControllerParameterType.Float);
            tree.AddChild(Meter(controller, parameter, "_Rx" + i));
        }
        SetWeight(tree);
        visible.motion = tree;
        AddIndicator(controller, EnableParameter, "_Playback");
        AddIndicator(controller, "UserFaceTracking", "_Tracking");
        AddIndicator(controller, "IsLocal", "_Local");
    }

    private static void AddIndicator(AnimatorController controller, string parameter, string property, string panelPath = Panel)
    {
        var layer = NewLayer(controller, panelPath + " " + property.Substring(1), panelPath: panelPath);
        var off = layer.AddState("Off");
        var on = layer.AddState("On");
        off.writeDefaultValues = on.writeDefaultValues = false;
        off.motion = MaterialClip(controller, property, 0, panelPath);
        on.motion = MaterialClip(controller, property, 1, panelPath);
        layer.defaultState = off;
        Transition(off, on).AddCondition(AnimatorConditionMode.If, 0, parameter);
        Transition(on, off).AddCondition(AnimatorConditionMode.IfNot, 0, parameter);
    }

    private static string RawParameter(int index) => "MmdAnywhere/DebugRx" + index;
    private static int[] RawRootRows() => new[] { 0, 2, 3, 4, 5, 6 };

    private static AnimatorStateMachine NewLayer(AnimatorController controller, string name, bool actionReadout = false, string panelPath = Panel)
    {
        controller.AddLayer(name);
        var layers = controller.layers;
        var layer = layers[layers.Length - 1];
        var mask = new AvatarMask { name = panelPath };
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
        mask.transformCount = panelPath == Panel ? 5 : 2;
        mask.SetTransformPath(0, "");
        mask.SetTransformActive(0, false);
        mask.SetTransformPath(1, panelPath);
        mask.SetTransformActive(1, !actionReadout);
        for (int i = 2; i < mask.transformCount; i++)
        {
            mask.SetTransformPath(i, Overlay(i - 2));
            mask.SetTransformActive(i, actionReadout);
        }
        AssetDatabase.AddObjectToAsset(mask, controller);
        layer.avatarMask = mask;
        layer.defaultWeight = 1;
        controller.layers = layers;
        return layer.stateMachine;
    }

    private const string Weight = "MmdAnywhere/DebugWeight";

    private static BlendTree DirectTree(AnimatorController controller)
    {
        if (!controller.parameters.Any(p => p.name == Weight))
            controller.AddParameter(new AnimatorControllerParameter { name = Weight, type = AnimatorControllerParameterType.Float, defaultFloat = 1 });
        var tree = new BlendTree { name = "Panel readout", blendType = BlendTreeType.Direct };
        AssetDatabase.AddObjectToAsset(tree, controller);
        return tree;
    }

    private static void SetWeight(BlendTree tree)
    {
        var children = tree.children;
        for (int i = 0; i < children.Length; i++)
            children[i].directBlendParameter = Weight;
        tree.children = children;
    }

    private static BlendTree Meter(AnimatorController controller, string parameter, string property, string panelPath = Panel)
    {
        var tree = new BlendTree { name = parameter, blendParameter = parameter, useAutomaticThresholds = false };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(MaterialClip(controller, property, 0, panelPath), 0);
        tree.AddChild(MaterialClip(controller, property, 1, panelPath), 1);
        return tree;
    }

    private static AnimationClip ScaleClip(AnimatorController controller, int axis, float value) =>
        Clip(controller, "Position " + axis + " " + value,
            EditorCurveBinding.FloatCurve(Overlay(axis), typeof(Transform), "m_LocalScale.x"), value);

    private static AnimationClip MaterialClip(AnimatorController controller, string property, float value, string panelPath) =>
        Clip(controller, property + " " + value, EditorCurveBinding.FloatCurve(panelPath, typeof(MeshRenderer), "material." + property), value);

    private static AnimationClip Clip(AnimatorController controller, string name, EditorCurveBinding binding, float value)
    {
        var clip = new AnimationClip { name = name };
        AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1f / 60f, value));
        AssetDatabase.AddObjectToAsset(clip, controller);
        return clip;
    }

    private static void AddParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        if (!controller.parameters.Any(p => p.name == name))
            controller.AddParameter(name, type);
    }

    private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to)
    {
        var transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0;
        return transition;
    }
}
#endif
