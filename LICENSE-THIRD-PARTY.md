# Third-party components

| Component | Where | License |
|---|---|---|
| Beat Saber Custom Avatars Unity project and `CustomAvatar.dll` (nicoco007) | `unity/Assets` (templates, scripts, shaders, `Libraries/CustomAvatar.*`) | LGPL-3.0, see `unity/CustomAvatars-LICENSE.txt`; source: https://github.com/nicoco007/BeatSaberCustomAvatars |
| UniVRM v0.131.4 (`com.vrmc.gltf`, `com.vrmc.vrm`, `com.vrmc.univrm`) by VRM Consortium | `unity/Packages` | MIT, see `unity/Packages/UniVRM-LICENSE.txt` |
| Avalonia, CommunityToolkit.Mvvm | NuGet packages of `app/` | MIT |
| CustomSabers script stubs (`SaberDescriptor`, `CustomTrail`, `EventManager`) | `unity-quest/Assets/SaberBridge/Runtime` | Written for this project (MIT); they only repeat the public class and field names so `.saber` files can be read |
| Demo sabers and their covers | `samples/`, `unity-quest/Assets/SaberBridge/Dev` | Made for this project, CC0 |

FinalIK (RootMotion) and DynamicBone (Will Hong) are paid Unity assets. They are **not** in this repository or in the release builds; the app copies them from the user's own Beat Saber install, where the Custom Avatars mod puts them.

SaberBridge writes `.whacker` files in the layout used by [Qosmetics](https://github.com/Qosmetics) but contains no Qosmetics code.
