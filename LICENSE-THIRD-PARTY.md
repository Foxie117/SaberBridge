# Third-party components

| Component | Where | License |
|---|---|---|
| Beat Saber Custom Avatars Unity project and `CustomAvatar.dll` (nicoco007) | `unity/Assets` (templates, scripts, shaders, `Libraries/CustomAvatar.*`) | LGPL-3.0, see `unity/CustomAvatars-LICENSE.txt`; source: https://github.com/nicoco007/BeatSaberCustomAvatars |
| UniVRM v0.131.4 (`com.vrmc.gltf`, `com.vrmc.vrm`) by VRM Consortium | `unity/Packages` | MIT, see `unity/Packages/UniVRM-LICENSE.txt` |
| Avalonia, CommunityToolkit.Mvvm | NuGet packages of `app/` | MIT |

FinalIK (RootMotion) and DynamicBone (Will Hong) are paid Unity assets. They are **not** in this repository or in the release builds; the app copies them from the user's own Beat Saber install, where the Custom Avatars mod puts them.
