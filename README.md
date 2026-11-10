# VRChat Face Tracking OSC Tester

Use this Windows app to exercise native face tracking on a VRChat avatar without a tracking headset.

1. Enable **OSC** in VRChat and load an avatar with **Face Tracking** and **Use All Tracking Keys** enabled.
2. Run `VRChatFaceTrackingOscTester.exe` from the release folder. The release uses the .NET 8 Desktop Runtime.
3. Click **Start streaming** and adjust a slider or type a whole number from 0 to 255. **Quick test** provides **EyeClosedRight**, **EyeClosedLeft**, and **JawOpen**. **Reset all to 0** clears all groups.

![Tracking keys tab streaming a jaw-open pose to a VRChat avatar](doc/screenshot_01.jpg)

## Network settings

| GUI setting | Default | Direction |
| --- | --- | --- |
| Listen IP | `127.0.0.1` | Local address to bind for incoming status |
| Listen port | `9001` | VRChat → tester |
| VRChat IP | `127.0.0.1` | Destination address for tracking |
| Send to VRChat port | `9000` | Tester → VRChat |

Stop streaming to change the endpoints. For a custom VRChat setup, match the ports and status destination to VRChat's `--osc=inPort:senderIP:outPort` launch argument.

The app sends all 102 Unified Expressions keys in a single `/tracking/face/v1` OSC blob per frame and targets 60 frames per second. UDP packet counts show local activity. The avatar ID and tracking status populate when VRChat sends its status messages to the configured listen endpoint. Switch avatars to trigger a fresh `/avatar/change` message. A dash indicates that the relevant status has yet to arrive.

## Animation player

1. Open **Animation player**. Under **Unity3D bundle**, select a `.unity3d` AssetBundle. Under **Manual files**, select a Unity `.anim` and optionally a WAV, Ogg, or MP3.
2. Click **Load selection**. If the bundle contains multiple animation clips, choose which one to use. The player prefers embedded audio with the same name as the clip and offers a choice if the audio is ambiguous. A missing or unreadable audio track leaves the animation available.
3. Click **Start streaming** to send the pose to VRChat, then click **Play**. **Pause** holds the pose and lets you adjust values on the **Tracking keys** tab. **Step +1** advances one source frame. When paused or stopped, enter a zero-based frame number and press Enter to seek. **Stop** returns to frame 0.

![Animation player tab playing a Unity3D bundle with audio and showing the current time and frame](doc/screenshot_02.jpg)

The player uses the clip's sample rate and duration. It converts MMD blink, wink, pupil, mouth, and other supported facial morphs into native tracking keys. The mouth shapes are approximations built from jaw and lip controls. Unmapped curves are excluded and the app reports how many face curves it mapped. If an exported `.anim` has merged or placeholder blendshape bindings, load the original bundle to recover the separate hashed tracks.

Bundle import, binary `.anim` files, and Ogg audio use the command-line tools that ship with Unity Editor. The app detects standard Unity Hub installations. For a custom install, set the `OSC_TESTER_UNITY_TOOLS` environment variable to the editor's `Editor\Data\Tools` directory before starting the tester. Text `.anim` clips with WAV or MP3 audio can be opened directly.

### Send humanoid pose data

Enable **Send humanoid pose data** in the Animation player and start OSC streaming. A clip with humanoid curves sends its pose alongside native face tracking. The checkbox starts unchecked.

Use the tester and `OscPoser` Unity assets from the same release. The full-body rotation channels and muscle encoding require matching versions.

Copy the [OscPoser folder](OscPoser/) into your VRChat Unity project's `Assets` directory. Use one installation method per avatar.

- **VRCFury**. Drag **OscPoser.prefab** directly under the avatar root. Its Full Controller component merges the Action controller and expression parameters during the avatar build and preserves the `OscPoser/` parameter names. [VRCFury Full Controller](https://vrcfury.com/components/full-controller/)
- **Manual**. Assign **OscPoser Action** to **Playable Layers → Action** on the avatar descriptor. Merge the entries from **OscPoser Expression Parameters** into the avatar's existing Expression Parameters asset.

The supplied entries are unsynced, unsaved, and default to zero. The OSC address is `/avatar/parameters/` followed by the complete, case-sensitive parameter name.

| Parameter | Type | Value |
| --- | --- | --- |
| `OscPoser/isEnabled` | Bool | True after **Play** and throughout **Pause**. False before playback, on **Stop** or completion, and when humanoid output is unchecked during playback. |
| `OscPoser/Muscle00` through `OscPoser/Muscle94` | Float | Unity `HumanTrait.MuscleName` index order. A wire value of ±1 represents a muscle value of ±3. Missing curves send 0. |
| `OscPoser/DanceX`, `OscPoser/DanceY`, `OscPoser/DanceZ` | Float | Multiply by 5 for humanoid scale units. X and Z are relative to the clip's starting position and heading. Y is relative to the controller's neutral height of 1 humanoid unit. |
| `OscPoser/BodyQXPositive`, `BodyQXNegative`, `BodyQYPositive`, `BodyQYNegative`, `BodyQZPositive`, `BodyQZNegative`, `BodyQW` | Float | Full body orientation relative to the initial heading. Each name uses the `OscPoser/` prefix. Subtract each negative weight from its positive weight to recover X, Y, and Z. W is nonnegative. |

The controller has an empty **Base Layer** and an **OscPoser** layer. Entry goes to **Idle**. On the local avatar, a true `OscPoser/isEnabled` enters **Drive Pose**, which switches body tracking to **Animation** and raises the Action layer weight. A false gate enters **Restore Tracking**, which returns body tracking to **Tracking** and lowers the Action layer weight before returning to Idle. Eyes and mouth are left under their existing tracking controls.

The controller embeds its blend trees and clips. For example, `OscPoser/Muscle14` drives **Head Turn Left-Right**, and `OscPoser/Muscle55` drives **Left Thumb 1 Stretched**. The travel and body orientation channels drive baked humanoid root curves on the avatar Animator. Unity scales translation to the avatar. The offsets move the avatar model around its playback origin while the VRChat player capsule stays at that origin.

The reader uses named Animator curves in text `.anim` files and humanoid bindings in modern binary clips and bundles. A clip without humanoid tracks keeps the gate off. Step and frame seek update the held pose when paused. Seeking before **Play** leaves the gate off.

## Convert MMD animations

- To convert an existing MikuMikuDance `.vmd` file, follow the converter's tutorial to create a Unity `.anim` clip and add the facial expression curves. Load the resulting clip under **Animation player → Manual files**. [UnityMMDConverter tutorial](https://github.com/maoxig/UnityMMDConverter#tutorial)
- To try a preconverted dance, download a `.unity3d` file and load it under **Animation player → Unity3D bundle**. [Preconverted dance bundles on Google Drive](https://drive.google.com/drive/folders/1YU7-Hz-O8-9B2E58mxQxexJTBTCT42jr?usp=sharing)

## Build

Open `VRChatFaceTrackingOscTester.sln` in Visual Studio 2022 with the **.NET desktop development** workload and .NET 8 SDK. Press **F5** to build and run. From PowerShell, run `dotnet build .\VRChatFaceTrackingOscTester.sln -c Release`.

## Protocol references

- [VRChat OSC Face Tracking](https://creators.vrchat.com/avatars/face-tracking/osc/)
- [VRChat OSC Overview](https://docs.vrchat.com/docs/osc-overview)
- [VRChat OSC avatar parameters](https://docs.vrchat.com/docs/osc-avatar-parameters)
- [Unity humanoid muscle names](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/HumanTrait.MuscleName.html)
- [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking)
- [Unity asset archive tools](https://docs.unity.com/en-us/engine/6000.3/manual/assets-and-media/assets-optimizing/assets-analyze-built-assets)
