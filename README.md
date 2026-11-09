# VRChat Face Tracking OSC Tester

Use this Windows app to exercise native face tracking on a VRChat avatar without a tracking headset.

1. Enable **OSC** in VRChat and load an avatar with **Face Tracking** and **Use All Tracking Keys** enabled.
2. Run `VRChatFaceTrackingOscTester.exe` from the release folder. The release uses the .NET 8 Desktop Runtime.
3. Click **Start streaming** and adjust a slider or type a whole number from 0 to 255. **Quick test** provides **EyeClosedRight**, **EyeClosedLeft**, and **JawOpen**. **Reset all to 0** clears all groups.

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

The player uses the clip's sample rate and duration. It converts MMD blink, wink, pupil, mouth, and other supported facial morphs into native tracking keys. The mouth shapes are approximations built from jaw and lip controls. Unmapped curves are excluded and the app reports how many face curves it mapped. If an exported `.anim` has merged or placeholder blendshape bindings, load the original bundle to recover the separate hashed tracks.

Bundle import, binary `.anim` files, and Ogg audio use the command-line tools that ship with Unity Editor. The app detects standard Unity Hub installations. For a custom install, set the `OSC_TESTER_UNITY_TOOLS` environment variable to the editor's `Editor\Data\Tools` directory before starting the tester. Text `.anim` clips with WAV or MP3 audio can be opened directly.

## Build

Open `VRChatFaceTrackingOscTester.sln` in Visual Studio 2022 with the **.NET desktop development** workload and .NET 8 SDK. Press **F5** to build and run. From PowerShell, run `dotnet build .\VRChatFaceTrackingOscTester.sln -c Release`.

## Protocol references

- [VRChat OSC Face Tracking](https://creators.vrchat.com/avatars/face-tracking/osc/)
- [VRChat OSC Overview](https://docs.vrchat.com/docs/osc-overview)
- [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking)
- [Unity asset archive tools](https://docs.unity.com/en-us/engine/6000.3/manual/assets-and-media/assets-optimizing/assets-analyze-built-assets)
