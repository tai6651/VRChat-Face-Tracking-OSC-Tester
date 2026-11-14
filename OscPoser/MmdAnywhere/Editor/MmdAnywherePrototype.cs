#if VRC_SDK_VRCSDK3
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using static OscTester.MmdAnywhereLayout;
using Object = UnityEngine.Object;

public static class MmdAnywherePrototype
{
    private const string Menu = "Tools/MMD Anywhere/Create Prototype Test Scene";
    private const string Folder = "Assets/MMD Anywhere Prototype";
    private const string Prefix = "MmdAnywhere/";
    private const string UpgradeMenu = "Tools/MMD Anywhere/Update Test Scene for Full Dance";
    private const string TrackingAvailable = "UserFaceTracking";
    private const string Weight = Prefix + "BlendWeight";
    private const string RotationWeight = Prefix + "RotationWeight";
    private static readonly string[] QuaternionWeights =
    {
        "QXPositive", "QXNegative", "QYPositive", "QYNegative", "QZPositive", "QZNegative", "QW"
    };

    [MenuItem(Menu, true)]
    private static bool CanCreateTestScene() => SelectedAvatar() != null;

    [MenuItem(Menu)]
    public static void CreateTestScene()
    {
        var source = SelectedAvatar();
        if (source == null)
            throw new InvalidOperationException("Select a humanoid avatar in the Hierarchy.");
        var animator = source.GetComponent<Animator>();
        if (animator == null || !animator.isHuman)
            throw new InvalidOperationException("The selected avatar needs a humanoid Animator.");
        if (source.transform.parent != null)
            throw new InvalidOperationException("Select a top-level avatar in a saved scene.");

        // Copy the scene so prefab overrides and scene references remain intact.
        var scene = source.gameObject.scene;
        if (string.IsNullOrEmpty(scene.path) || scene.isDirty)
            throw new InvalidOperationException("Save the source scene before creating the test copy.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save any other open scenes before creating the test copy.");
        }
        int rootIndex = source.transform.GetSiblingIndex();

        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets", "MMD Anywhere Prototype");
        string scenePath = AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + scene.name + " - MMD Anywhere.unity");
        if (!EditorSceneManager.SaveScene(scene, scenePath, true))
            throw new InvalidOperationException("Could not save the test scene copy.");
        var testScene = EditorSceneManager.OpenScene(scenePath);
        var avatar = testScene.GetRootGameObjects()[rootIndex].GetComponent<VRCAvatarDescriptor>();
        avatar.name += " - MMD Anywhere";

        // Use a new avatar ID when the test copy is first uploaded.
        var pipeline = avatar.GetComponent<PipelineManager>();
        if (pipeline != null)
        {
            pipeline.blueprintId = "";
            EditorUtility.SetDirty(pipeline);
            PrefabUtility.RecordPrefabInstancePropertyModifications(pipeline);
        }

        InstallFullDance(avatar);
    }

    [MenuItem(UpgradeMenu, true)]
    private static bool CanUpgradeTestScene() => SelectedAvatar() is VRCAvatarDescriptor avatar
        && avatar.gameObject.scene.path.StartsWith(Folder + "/", StringComparison.Ordinal);

    [MenuItem(UpgradeMenu)]
    public static void UpgradeTestScene()
    {
        if (!CanUpgradeTestScene())
            throw new InvalidOperationException("Select the avatar in an MMD Anywhere test scene.");
        var avatar = SelectedAvatar();
        InstallFullDance(avatar);
    }

    private static void InstallFullDance(VRCAvatarDescriptor avatar)
    {
        if (MuscleCarriers.Length + FaceNames.Length != RemainingKeys.Length)
            throw new InvalidOperationException("The native payload slots do not match the muscle and face layout.");
        var mappings = FindFaceBindings(avatar);
        var layers = avatar.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
        int fxIndex = Array.FindIndex(layers, layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX);
        var sourceFx = fxIndex < 0 || layers[fxIndex].isDefault ? null : layers[fxIndex].animatorController;
        if (sourceFx != null && !(sourceFx is AnimatorController))
            throw new InvalidOperationException("The test avatar requires a standard FX Animator Controller.");
        var parameters = CreateParameters(avatar);
        var action = CreateController(Folder + "/MMD Anywhere Full Dance Action.controller");
        var fx = CreateFaceController(sourceFx as AnimatorController, mappings);
        ConfigureDescriptor(avatar, parameters, action, fx);
        MmdAnywhereDebug.RefreshInstalledPanels(avatar, action, fx);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(avatar.gameObject.scene);
        EditorSceneManager.SaveScene(avatar.gameObject.scene);
        Selection.activeGameObject = avatar.gameObject;
    }

    private static VRCAvatarDescriptor SelectedAvatar() => Selection.activeGameObject == null ? null
        : Selection.activeGameObject.GetComponentInParent<VRCAvatarDescriptor>();

    private static VRCExpressionParameters CreateParameters(VRCAvatarDescriptor avatar)
    {
        var parameters = avatar.expressionParameters == null ? ScriptableObject.CreateInstance<VRCExpressionParameters>()
            : Object.Instantiate(avatar.expressionParameters);
        var entries = parameters.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>();
        var names = new[] { EnableParameter }.Concat(Muscles.Select(MuscleParameter))
            .Concat(Enumerable.Range(0, 3).Select(TranslationParameter))
            .Concat(QuaternionWeights.Select(name => Prefix + name))
            .Concat(Enumerable.Range(0, FaceNames.Length).Select(FaceParameter));
        parameters.parameters = entries.Where(p => !p.name.StartsWith(Prefix, StringComparison.Ordinal)).Concat(names.Select(name =>
            new VRCExpressionParameters.Parameter
            {
                name = name,
                valueType = name == EnableParameter ? VRCExpressionParameters.ValueType.Bool : VRCExpressionParameters.ValueType.Float,
                networkSynced = name == EnableParameter,
                saved = false
            })).ToArray();
        if (parameters.CalcTotalCost() > VRCExpressionParameters.MAX_PARAMETER_COST)
            throw new InvalidOperationException("The avatar's existing synced parameters exceed the budget.");
        AssetDatabase.CreateAsset(parameters, AssetDatabase.GenerateUniqueAssetPath(Folder + "/MMD Anywhere Full Dance Parameters.asset"));
        return parameters;
    }

    private static void ConfigureDescriptor(VRCAvatarDescriptor avatar, VRCExpressionParameters parameters,
        AnimatorController action, AnimatorController fx)
    {
        avatar.expressionParameters = parameters;
        avatar.customExpressions = true;

        var layers = avatar.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
        if (layers.Length == 0)
        {
            layers = new[] { VRCAvatarDescriptor.AnimLayerType.Base, VRCAvatarDescriptor.AnimLayerType.Additive,
                VRCAvatarDescriptor.AnimLayerType.Gesture, VRCAvatarDescriptor.AnimLayerType.Action,
                VRCAvatarDescriptor.AnimLayerType.FX }.Select(type => new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = type,
                    isDefault = true
                }).ToArray();
        }
        SetPlayableLayer(layers, VRCAvatarDescriptor.AnimLayerType.Action, action);
        SetPlayableLayer(layers, VRCAvatarDescriptor.AnimLayerType.FX, fx);
        avatar.baseAnimationLayers = layers;
        avatar.customizeAnimationLayers = true;

        avatar.enableFaceTracking = avatar.useAllTrackingKeys = true;
        avatar.faceTrackingBlendshapesSetList?.Clear();
        avatar.faceTrackingParameterBehaviors = new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>();
        AddPlaybackForwarding(avatar);

        PrefabUtility.RecordPrefabInstancePropertyModifications(avatar);
        EditorUtility.SetDirty(avatar);
    }

    private static void AddPlaybackForwarding(VRCAvatarDescriptor avatar)
    {
        for (int i = 0; i < MuscleCarriers.Length; i++)
        {
            int muscle = MuscleCarriers[i];
            string key = KeyName(MuscleKey(i));
            Forward(avatar, MuscleParameter(muscle), key);
            if (muscle == 15)
                Forward(avatar, MuscleParameter(17), key);
            else if (muscle == 16)
                Forward(avatar, MuscleParameter(18), "clamp((256 - " + key + " * 255) / 255, 0, 1) * clamp("
                    + key + " * 255 - " + (MissingByteThreshold - 1) + ", 0, 1)");
            else if (Array.IndexOf(SharedCurlStarts, muscle) >= 0)
                Forward(avatar, MuscleParameter(muscle + 1), key);
        }
        for (int i = 0; i < 3; i++)
        {
            string coarse = KeyName(RootKeys[i]);
            string input = coarse;
            if (i != 1)
            {
                string fine = KeyName(i == 0 ? RootXExtraKey : RootZExtraKey);
                string correction = "(clamp((" + fine + " * 255 - " + HorizontalFineMin + ") / "
                    + HorizontalFineSpan + ", 0, 1) - 0.5)";
                input = "(" + coarse + " + " + correction + " * " + Present(fine) + " / 255) * " + Present(coarse);
            }
            Forward(avatar, TranslationParameter(i), input);
        }

        // Treat a missing quaternion component as an unavailable rotation. A zero native
        // key must never be interpreted as a full negative component.
        string minimum = "min(min(" + KeyName(RootKeys[3]) + ", " + KeyName(RootKeys[4])
            + "), min(" + KeyName(RootKeys[5]) + ", " + KeyName(RootKeys[6]) + "))";
        string valid = "clamp(" + minimum + " * 255 - " + (MissingByteThreshold - 1) + ", 0, 1)";
        string w = SignedKey(RootKeys[6]);
        string hemisphere = "(1 - 2 * clamp(-" + w + " * 4096, 0, 1))";
        for (int i = 0; i < 3; i++)
        {
            string component = SignedKey(RootKeys[3 + i]) + " * " + hemisphere;
            Forward(avatar, Prefix + QuaternionWeights[i * 2], "max(" + component + ", 0) * " + valid);
            Forward(avatar, Prefix + QuaternionWeights[i * 2 + 1], "max(-(" + component + "), 0) * " + valid);
        }
        Forward(avatar, Prefix + QuaternionWeights[6], "1 - (1 - abs(" + w + ")) * " + valid);
        for (int i = 0; i < FaceNames.Length; i++)
            Forward(avatar, FaceParameter(i), KeyName(FaceKey(i)));
    }

    private static string Present(string key) => "clamp(" + key + " * 255 - " + (MissingByteThreshold - 1) + ", 0, 1)";

    private static void SetPlayableLayer(VRCAvatarDescriptor.CustomAnimLayer[] layers, VRCAvatarDescriptor.AnimLayerType type,
        AnimatorController controller)
    {
        int index = Array.FindIndex(layers, layer => layer.type == type);
        layers[index].animatorController = controller;
        layers[index].isDefault = false;
        layers[index].mask = controller.layers[0].avatarMask;
    }

    internal static string KeyName(int index) => ((VRCAvatarDescriptor.FaceTrackingKey)index).ToString();
    private static string SignedKey(int index)
    {
        string value = "(" + KeyName(index) + " * 255 - " + Center + ")";
        return "(clamp((" + value + " - " + ZeroGap + ") / " + SignedSteps + ", 0, 1) + clamp(("
            + value + " + " + ZeroGap + ") / " + SignedSteps + ", -1, 0))";
    }

    internal static void Forward(VRCAvatarDescriptor avatar, string name, string expression)
    {
        avatar.faceTrackingParameterBehaviors.Add(new VRCAvatarDescriptor.FaceTrackingParameterBehavior
        {
            targetMode = VRCAvatarDescriptor.ParameterBehaviorTargetMode.ForwardToAnimator,
            outputParameterName = name,
            combineMode = VRCAvatarDescriptor.ParameterBehaviorCombineMode.Custom,
            customExpression = expression,
            inputTrackingKeys = Array.Empty<int>(),
            inputTrackingKeyNames = Array.Empty<string>(),
            inputInverted = Array.Empty<bool>()
        });
    }

    private static AnimatorController CreateController(string path)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(AssetDatabase.GenerateUniqueAssetPath(path));
        controller.AddParameter(EnableParameter, AnimatorControllerParameterType.Bool);
        controller.AddParameter(TrackingAvailable, AnimatorControllerParameterType.Bool);
        controller.AddParameter(new AnimatorControllerParameter
        {
            name = Weight,
            type = AnimatorControllerParameterType.Float,
            defaultFloat = 1
        });
        controller.AddLayer("MMD Anywhere");
        var layers = controller.layers;
        layers[1].defaultWeight = 1;
        controller.layers = layers;

        var states = layers[1].stateMachine;
        var idle = states.AddState("Idle");
        var drive = states.AddState("Drive Pose");
        var restore = states.AddState("Restore Tracking");
        idle.writeDefaultValues = restore.writeDefaultValues = false;
        drive.writeDefaultValues = true;
        states.defaultState = idle;

        TransitionWhenPlaying(idle, drive);
        TransitionWhenStopped(drive, restore);
        var release = Transition(restore, idle);
        release.hasExitTime = true;
        release.exitTime = 1;
        TransitionWhenPlaying(restore, drive);
        SetTracking(drive, true);
        SetTracking(restore, false);
        SetActionWeight(drive, 1);
        SetActionWeight(idle, 0);

        var pose = AddTree(controller, "Full Dance Pose", BlendTreeType.Direct, Weight);
        foreach (int muscle in Muscles)
        {
            string property = MuscleProperty(muscle);
            var muscleTree = AddAxis(controller, MuscleParameter(muscle),
                EditorCurveBinding.FloatCurve("", typeof(Animator), property), 0, MuscleRange);
            pose.AddChild(muscleTree);
        }

        for (int i = 0; i < 3; i++)
        {
            float origin = i == 1 ? 1 : 0;
            var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), RootProperties[i]);
            string input = TranslationParameter(i);
            var root = AddAxis(controller, input, binding, origin, TranslationRange,
                bakeRoot: true, fineRange: FineTranslationRange(i), zeroGap: i == 1 ? ZeroGap : 0);
            pose.AddChild(root);
        }
        // Give Restore Tracking one evaluation before handing Action back to the avatar.
        restore.motion = AddClip(controller, "Restore Tracking",
            new[] { EditorCurveBinding.FloatCurve("", typeof(Animator), Weight) }, new[] { 1f });

        var rotation = AddTree(controller, "Root Rotation", BlendTreeType.Direct, Weight);
        var rotationBindings = RootProperties.Skip(3).Select(property => EditorCurveBinding.FloatCurve("", typeof(Animator), property)).ToArray();
        for (int i = 0; i < QuaternionWeights.Length; i++)
        {
            string parameter = Prefix + QuaternionWeights[i];
            controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
            var basis = new[] { 0f, 0f, 0f, 0.00001f };
            basis[i / 2] = i % 2 == 0 ? 1 : -1;
            rotation.AddChild(AddClip(controller, QuaternionWeights[i], rotationBindings, basis, bakeRoot: true));
        }
        SetDirectWeights(rotation, QuaternionWeights.Select(name => Prefix + name).ToArray());
        pose.AddChild(rotation);
        controller.AddParameter(new AnimatorControllerParameter
        {
            name = RotationWeight,
            type = AnimatorControllerParameterType.Float,
            defaultFloat = 100000
        });
        var weights = Enumerable.Repeat(Weight, pose.children.Length).ToArray();
        weights[weights.Length - 1] = RotationWeight;
        SetDirectWeights(pose, weights);
        drive.motion = pose;
        return controller;
    }

    private static string MuscleParameter(int muscle) => Prefix + "Muscle" + muscle.ToString("D2");
    private static string MuscleProperty(int muscle)
    {
        string name = HumanTrait.MuscleName[muscle];
        if (muscle < 55)
            return name;

        // Unity's clip binding syntax for fingers differs from HumanTrait's display name.
        string[] parts = name.Split(new[] { ' ' }, 3);
        return parts[0] + "Hand." + parts[1] + "." + parts[2];
    }
    private static EditorCurveBinding[][] FindFaceBindings(VRCAvatarDescriptor avatar)
    {
        var body = avatar.transform.Find("Body");
        var mesh = body == null ? null : body.GetComponent<SkinnedMeshRenderer>();
        if (mesh == null || mesh.sharedMesh == null)
            throw new InvalidOperationException("The full dance requires a Body skinned mesh.");

        var shapes = new[]
        {
            new[] { "EyeClosedLeft" }, new[] { "EyeClosedRight" }, new[] { "JawOpen" },
            new[] { "MouthStretchLeft", "MouthStretchRight" },
            new[] { "LipPuckerUpperLeft", "LipPuckerUpperRight", "LipPuckerLowerLeft", "LipPuckerLowerRight" },
            new[] { "MouthCornerPullLeft", "MouthCornerPullRight" },
            new[] { "EyeConstrictLeft", "EyeConstrictRight" }
        };
        return shapes.Select(group => group.Where(shape => mesh.sharedMesh.GetBlendShapeIndex(shape) >= 0)
            .Select(shape => EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + shape))
            .ToArray()).ToArray();
    }

    private static AnimatorController CreateFaceController(AnimatorController source, EditorCurveBinding[][] bindings)
    {
        const string layerName = "MMD Anywhere Face";
        string path = AssetDatabase.GenerateUniqueAssetPath(Folder + "/MMD Anywhere Full Dance FX.controller");
        AnimatorController controller;
        if (source == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        else
        {
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path))
                throw new InvalidOperationException("Could not copy the source FX controller.");
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        }

        for (int i = controller.layers.Length - 1; i >= 0; i--)
        {
            if (controller.layers[i].name == layerName)
                controller.RemoveLayer(i);
        }
        controller.parameters = controller.parameters.Where(p =>
            p.name != EnableParameter && p.name != Weight && !p.name.StartsWith(Prefix + "Face", StringComparison.Ordinal)).ToArray();
        if (!controller.parameters.Any(p => p.name == TrackingAvailable))
            controller.AddParameter(TrackingAvailable, AnimatorControllerParameterType.Bool);
        controller.AddParameter(EnableParameter, AnimatorControllerParameterType.Bool);
        controller.AddParameter(new AnimatorControllerParameter
        {
            name = Weight,
            type = AnimatorControllerParameterType.Float,
            defaultFloat = 1
        });
        controller.AddLayer(layerName);
        var layers = controller.layers;
        var layer = layers[layers.Length - 1];
        layer.defaultWeight = 1;
        controller.layers = layers;
        var states = layer.stateMachine;
        var idle = states.AddState("Idle");
        var drive = states.AddState("Drive Face");
        var restore = states.AddState("Restore Face");
        idle.writeDefaultValues = restore.writeDefaultValues = false;
        drive.writeDefaultValues = true;
        states.defaultState = idle;
        AddPlaybackTransitions(idle, drive, restore);
        var allBindings = bindings.SelectMany(group => group).ToArray();
        restore.motion = AddClip(controller, "Clear MMD Face", allBindings, new float[allBindings.Length]);
        var release = Transition(restore, idle);
        release.hasExitTime = true;
        release.exitTime = 1;
        var face = AddTree(controller, "MMD Face", BlendTreeType.Direct, Weight);
        for (int i = 0; i < bindings.Length; i++)
        {
            string parameter = FaceParameter(i);
            controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
            face.AddChild(AddClip(controller, FaceNames[i], bindings[i], Enumerable.Repeat(100f, bindings[i].Length).ToArray()));
        }
        SetDirectWeights(face, Enumerable.Range(0, bindings.Length).Select(FaceParameter).ToArray());
        drive.motion = face;
        return controller;
    }

    private static void AddPlaybackTransitions(AnimatorState idle, AnimatorState drive, AnimatorState restore)
    {
        TransitionWhenPlaying(idle, drive);
        TransitionWhenStopped(drive, restore);
    }

    private static AnimatorStateTransition TransitionWhenPlaying(AnimatorState from, AnimatorState to)
    {
        var transition = Transition(from, to);
        transition.AddCondition(AnimatorConditionMode.If, 0, EnableParameter);
        transition.AddCondition(AnimatorConditionMode.If, 0, TrackingAvailable);
        return transition;
    }

    private static void TransitionWhenStopped(AnimatorState from, AnimatorState to)
    {
        Transition(from, to).AddCondition(AnimatorConditionMode.IfNot, 0, EnableParameter);
        Transition(from, to).AddCondition(AnimatorConditionMode.IfNot, 0, TrackingAvailable);
    }

    private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to)
    {
        var transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0;
        return transition;
    }

    private static void SetTracking(AnimatorState state, bool drive)
    {
        var tracking = state.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
        var mode = drive ? VRC_AnimatorTrackingControl.TrackingType.Animation : VRC_AnimatorTrackingControl.TrackingType.Tracking;
        tracking.trackingHead = tracking.trackingLeftHand = tracking.trackingRightHand = mode;
        tracking.trackingHip = tracking.trackingLeftFoot = tracking.trackingRightFoot = mode;
        tracking.trackingLeftFingers = tracking.trackingRightFingers = mode;
        tracking.trackingEyes = tracking.trackingMouth = VRC_AnimatorTrackingControl.TrackingType.Tracking;
    }

    private static void SetActionWeight(AnimatorState state, float weight)
    {
        var layer = state.AddStateMachineBehaviour<VRCPlayableLayerControl>();
        layer.layer = VRC_PlayableLayerControl.BlendableLayer.Action;
        layer.goalWeight = weight;
    }

    private static BlendTree AddTree(AnimatorController controller, string name, BlendTreeType type, string parameter)
    {
        var tree = new BlendTree
        {
            name = name,
            blendType = type,
            blendParameter = parameter,
            useAutomaticThresholds = false
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        return tree;
    }

    private static BlendTree AddAxis(AnimatorController controller, string parameter, EditorCurveBinding binding,
        float center, float range, bool bakeRoot = false, float fineRange = 0, int zeroGap = ZeroGap)
    {
        controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
        var tree = AddTree(controller, binding.propertyName, BlendTreeType.Simple1D, parameter);
        var bindings = new[] { binding };
        var neutral = AddClip(controller, binding.propertyName + " Neutral", bindings, new[] { center }, bakeRoot);
        // A zeroed native key lands at the neutral pose. The middle plateau absorbs rounding around 128.
        tree.AddChild(neutral, MissingByteThreshold / 255f);
        tree.AddChild(AddClip(controller, binding.propertyName + " -1", bindings, new[] { center - range }, bakeRoot),
            (Center - SignedSteps - zeroGap) / 255f);
        if (fineRange > 0)
        {
            int fineEdge = FineTranslationSteps + zeroGap;
            tree.AddChild(AddClip(controller, binding.propertyName + " Fine Min", bindings, new[] { center - fineRange }, bakeRoot),
                (Center - fineEdge) / 255f);
        }
        tree.AddChild(neutral, (Center - zeroGap) / 255f);
        if (zeroGap > 0)
            tree.AddChild(neutral, (Center + zeroGap) / 255f);
        if (fineRange > 0)
        {
            int fineEdge = FineTranslationSteps + zeroGap;
            tree.AddChild(AddClip(controller, binding.propertyName + " Fine Max", bindings, new[] { center + fineRange }, bakeRoot),
                (Center + fineEdge) / 255f);
        }
        tree.AddChild(AddClip(controller, binding.propertyName + " +1", bindings, new[] { center + range }, bakeRoot),
            (Center + SignedSteps + zeroGap) / 255f);
        return tree;
    }

    private static void SetDirectWeights(BlendTree tree, string[] parameters)
    {
        var children = tree.children;
        for (int i = 0; i < children.Length; i++)
            children[i].directBlendParameter = parameters[i];
        tree.children = children;
    }

    private static AnimationClip AddClip(AnimatorController controller, string name, EditorCurveBinding[] bindings,
        float[] values, bool bakeRoot = false)
    {
        var clip = new AnimationClip { name = name, frameRate = 60 };
        for (int i = 0; i < bindings.Length; i++)
            AnimationUtility.SetEditorCurve(clip, bindings[i], AnimationCurve.Constant(0, 1f / 60f, values[i]));
        if (bakeRoot)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopBlendOrientation = settings.loopBlendPositionY = settings.loopBlendPositionXZ = true;
            settings.keepOriginalOrientation = settings.keepOriginalPositionY = settings.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }
        AssetDatabase.AddObjectToAsset(clip, controller);
        return clip;
    }
}
#endif
