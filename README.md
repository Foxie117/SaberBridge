<p align="center"><img src="docs/logo.png" width="96" alt="SaberBridge logo"></p>

<h1 align="center">SaberBridge</h1>

<p align="center">A one-window converter for Beat Saber mods on Windows and Linux.</p>

SaberBridge does four things:

- **PC avatars.** It turns VRM, VRChat and MMD models into `.avatar` files for the **Custom Avatars** mod (5.4.x, Beat Saber 1.40) and installs them into the game.
- **Quest sabers.** It turns PC **CustomSabers** files (`.saber`) into Quest sabers (`.whacker`).
- **Quest notes.** It turns PC **Custom Notes** files (`.bloq`) into Quest notes (`.cyoob`).
- **Quest avatars.** It turns PC **Custom Avatars** files (`.avatar`, made by anyone) into `.vrm` for the **VRM Qavatars** mod. *New, not yet tested on a headset; reports welcome.*

The Quest sabers and notes load with Qosmetics-format mods on a standalone Quest (Beat Saber 1.29+, for example Qosmetics or Custom Models).

Drop your files into the window and press **Convert**. Unity does the building in the background; you never open it yourself, and the app installs it for you (see [What you need](#what-you-need)). The interface is in English and Russian.

![How to use it: avatars, preview, install, Quest sabers, system check](docs/how-to.webp)

<sub>[Watch as a video (MP4)](docs/how-to.mp4)</sub>

## Download

Get the latest build from [**Releases**](../../releases/latest):

- `SaberBridge-win-x64.exe` for Windows
- `SaberBridge-linux-x64.tar.gz` for Linux

Each is a single file with no installer. On first start the app sets up Unity by itself (see [What you need](#what-you-need)).

## PC avatars

1. **Drop your models** onto the window, or press *Choose files*: `.vrm`, a VRChat `.unitypackage`, an MMD `.pmx`, or a `.zip` or folder that contains them.
2. **Press Convert.** Each model shows its current stage (reading the model, skeleton, physics, materials, IK, cover, packing) with a percentage and a timer.
3. **Check the result.** New avatars appear at the top of the library with a **NEW** badge. A coloured dot on each card shows the result of the avatar check: green means all good, orange means worth a look.
4. **Click a card to open its preview.** Play the idle, wave and walk animations, turn the model with the mouse, and turn the skeleton overlay on or off.
5. **Press Install** (or *Install all*) to copy the avatars into the selected Beat Saber copy. Delete moves a file to the recycle bin. Finished avatars are also kept in `Documents\SaberBridge\PC\Avatars`.

| Input | What happens |
|---|---|
| `.vrm` (0.x and 1.0) | Spring bones become DynamicBone physics; the MToon look is kept with a Beat Saber friendly shader; the VRM thumbnail becomes the cover |
| `.unitypackage` (VRChat avatar) | PhysBones become DynamicBone; Poiyomi colour and decals are baked; the PC version is picked over the Quest one |
| `.pmx` (MikuMikuDance) | Own PMX reader; Japanese bone names are mapped to a humanoid skeleton; outlines are kept |
| `.zip` / folders | Searched for any of the above |

Every avatar gets full-body IK (VRIK), finger poses, first-person head hiding, tail physics when the model has tail bones, a name and a cover.

**Preview.** After converting (or on demand for any installed avatar), Unity loads the `.avatar` the way the mod does and checks it: trackers, IK, finger poses, physics chains, shaders, triangle count and height. It also renders a turntable plus idle, wave and walk animations. The preview window plays them with the skeleton drawn on top (body bones blue, physics bones orange).

| Library with fresh conversions | Converting |
|---|---|
| ![Library](docs/main.png) | ![Converting](docs/converting.png) |
| **Preview with skeleton and avatar check** | **System check** |
| ![Preview](docs/preview.png) | ![System check](docs/system-check.png) |

## Quest sabers, notes and avatars (.saber → .whacker, .bloq → .cyoob, .avatar → .vrm)

1. Open the **Quest** tab. Without Beat Saber on the PC the app opens on it by itself.
2. **Drop `.saber`, `.bloq` and `.avatar` files**, mixed as you like, or a `.zip` with them. They all go into one queue; a badge shows what each one is. Files for the other tab (models) go to the PC queue without switching tabs.
3. **Press Convert once.** Unity builds a PC and an Android (Quest) version of each saber and note set in one run and packs them; avatars are converted in a run of their own.
4. Take the results from the **Quest folder** (by default `Documents\SaberBridge\Quest`): sabers are in `Sabers`, notes in `Notes`, avatars in `Avatars`. Install them on the Quest with **Mods Before Friday**.

![Quest tab](docs/quest.png)

**Sabers**
- **Same layout as the Qosmetics exporter:** `_Whacker` with `LeftSaber` / `RightSaber`, trails with their colours and length, cover image, name and author.
- **Same size in hand as on PC.** Qosmetics resets the saber object's own scale, so SaberBridge moves the author's scale onto the model.
- Sabers with scripts that cannot be read (from other CustomSabers versions) still convert; such objects are rebuilt without them.

**Notes**
- Arrow and dot notes, the bomb, and chain heads and links when the `.bloq` has them; a missing dot note uses the arrow note, like Custom Notes does.
- **Placed like on PC.** Custom Notes puts every note at the centre of the note cube at 0.4 scale with its own rotation; the `.cyoob` reproduces exactly that, whatever transform the author left in the file.
- Note colours follow the Custom Notes "uses note colour" setting and its per-object exceptions.

**Avatars** (for [VRM Qavatars](https://github.com/BSQ-VRM/VRM-Qavatars))
- **VRM 0.x with MToon materials**, because that is what VRM Qavatars reads (it puts every material on MToon). The look comes from each material's texture and colour; SaberBridge-made avatars keep their MToon settings.
- **Skeleton** from the avatar's humanoid Animator, or from its full-body IK (every `.avatar` has VRIK). Tracker objects and the mod's scripts are left out.
- **Size as on PC.** Bones and meshes are normalised the way VRM wants (also for Blender models with a 100× armature); a model whose author left an absurd size (Custom Avatars resizes every avatar in game) is brought to an ordinary height.
- **Physics:** DynamicBone chains and colliders become VRM spring bones. **Expressions:** blink and the A/I/U/E/O mouth shapes when the model has blend shapes with the usual names (VRChat, VRM, MMD).
- **Sized for the Quest:** textures up to 1024 px, blend shapes no expression uses are dropped.
- Reading an `.avatar` needs **Beat Saber with Custom Avatars on this PC** (FinalIK and DynamicBone come from it); the game picker shows on the Quest tab when avatars wait there.
- Not yet: avatars whose meshes were saved without Read/Write.

**Sabers and notes**
- **Meshes, textures and colours carry over.** Meshes saved without Read/Write are recovered through a one-off GPU capture.
- **Shaders.** PC shaders cannot run on the Quest, so they are swapped for Quest shaders. Their look is captured from the PC shader (lighting and colour on a sphere) and baked in, so metal, gradients and tints stay close to the PC version. Procedural PC effects such as fire or distortion become a soft glow. All shaders are built with the Quest's multiview stereo variants.
- **Originals are never changed or moved.** The queue remembers which Quest file came from which original, shows the cover from the converted file, and notices when a converted file is deleted, so you can convert it again.

Want to try it without a saber of your own? [`samples/`](samples) has three simple demo sabers (public domain, CC0).

## What you need

| | PC avatars | Quest sabers and notes | Quest avatars |
|---|---|---|---|
| Unity **2021.3.16f1** with Unity Hub, a free Personal license and the Android module | ✔ | ✔ | ✔ |
| A free **Unity account** (to sign in once for the license) | ✔ | ✔ | ✔ |
| Beat Saber with Custom Avatars on the PC (BSManager or Steam) | ✔ | | ✔ |
| On the Quest | | Beat Saber 1.29+ with a Qosmetics-format mod | VRM Qavatars |
| About 10 GB of free disk space for Unity (about 15 GB during the first setup) | ✔ | ✔ | ✔ |

**Unity installs itself (Windows).** On first start the app sets up everything Unity needs in the background, with a guide window that explains each step:

- It checks the disk space and picks another drive if the system drive is too full.
- It downloads Unity Hub, Unity 2021.3.16f1, the Android module and the Visual C++ runtime (about 3 GB) from Unity's and Microsoft's own servers, showing real sizes, speed and time left. Downloads resume after a dropped connection and switch to another official Unity server when one is blocked. Every file's digital signature is checked before it runs.
- **Windows asks once for admin rights**: Unity's installers need them to write to Program Files. Everything is then installed silently, with a progress bar based on what is written to disk. If Windows Update cannot be reached, the optional .NET Framework 3.5 step of Unity's installer is skipped (Unity works without it).
- **You sign in to Unity Hub once** with a free Unity account. If Hub does not add the free Personal license by itself: the gear → *Licenses* → *Add* → *Get a free personal license*. The app checks the license by starting Unity, then imports its project once.
- If Unity's servers look blocked on your network, the app says so (a VPN helps) and carries on by itself once they can be reached.

On Linux, install Unity Hub and Unity 2021.3.16f1 with the *Windows Build Support (Mono)* and *Android Build Support* modules yourself; the system check lists what is missing.

- The **system check** (the chip at the top) finds Beat Saber (BSManager / Steam), the Custom Avatars version, Unity Hub, the Unity editor, its modules and the license, with a fix for each item. It opens by itself only when the open tab has something to convert and something is missing.
- The avatar converter takes `FinalIK.dll` and `DynamicBone.dll` from your game's `Libs` folder (they come with the Custom Avatars mod).
- On machines where Unity in batch mode gets no license from Unity Hub (new installs with Hub 3), the app runs Unity as a normal editor with its window hidden instead, and remembers that.
- The first conversion of each kind takes a few minutes while Unity imports its project once. After that an avatar takes about a minute and a saber or note a few seconds.

## Good to know

- Finished files: `Documents\SaberBridge\PC\Avatars` and `Documents\SaberBridge\Quest\Sabers` / `Notes` / `Avatars`. Settings, logs (including `setup.log` for the Unity setup) and previews live in `%LOCALAPPDATA%\SaberBridge` (Windows) or `~/.local/share/SaberBridge` (Linux).
- You can drop files onto the `.exe`, or run `SaberBridge model.vrm --convert`. Opening an `.avatar` with the app shows its preview.
- **Updates:** the app looks for a new release on GitHub once a day and shows a note; the Windows `.exe` updates itself in one click (the download is checked against GitHub's SHA-256). Settings show the version and a *Check for updates* button.
- The language (English / Russian) switches at once, without a restart.
- Known limitations and ideas are tracked in [Issues](../../issues).

## Repository layout

```
app/          Avalonia (.NET 9) desktop app: UI, Unity runner, installers, system check
unity/        Unity 2021.3 project for avatars: Custom Avatars templates, converter scripts (Assets/VRM2BS), vendored UniVRM
unity-quest/  Unity 2021.3 project for Quest sabers and notes: converters (Assets/SaberBridge), Quest shaders, CustomSabers / CustomNotes script stubs
samples/      CC0 demo sabers
docs/         README images and the how-to animation
```

Both Unity projects are zipped into the app at build time, so the app is self-contained apart from Unity itself.

### Build

```
dotnet publish app -c Release -r win-x64 -o dist/win
dotnet publish app -c Release -r linux-x64 -o dist/linux
```

Pushing a tag `v*` builds both and attaches them to a GitHub release (`.github/workflows/release.yml`).

### Developing the converter scripts

Open `unity/` in Unity 2021.3.16f1 after copying `FinalIK.dll` and `DynamicBone.dll` from your game's `Libs` folder into `unity/Assets/Libraries/`. Batch entry points:

- `VRM2BS.VrmToAvatarConverter.Run -vrmDir <dir> -outDir <dir> [-coverDir <dir>]`
- `VRM2BS.PmxAvatarConverter.Run -jobsFile <file> -outDir <dir>` (lines `Name|body.pmx[|outfit.pmx]`)
- `VRM2BS.VrcAvatarConverter.Run -importDir Assets/VRC_Import/<Name> -outDir <dir>` (optional `variants.txt` for colour variants)
- `VRM2BS.VrmToAvatarConverter.Validate -outDir <dir>`: loads built bundles like the mod does
- `VRM2BS.AvatarPreview.Run -avatarList <file> -previewDir <dir>`: checks `.avatar` files and renders the preview frames
- `VRM2BS.AvatarToVrm.Run -avatarList <file> -outDir <dir>`: `.avatar` → VRM 0.x for VRM Qavatars; `VRM2BS.AvatarToVrm.Check -outDir <dir>` loads the results back like a VRM 0.x loader and renders front and side pictures

`unity-quest/` needs the Android module and no paid assets:

- `SaberBridge.QuestJobs.Run -jobsFile <file>` (lines `source|output folder`; `.saber` and `.bloq` mixed, as the app uses it)
- `SaberBridge.SaberToWhacker.Run -jobsFile <file>` / `SaberBridge.BloqToCyoob.Run -jobsFile <file>`: one kind only
- `SaberBridge.RenderNotes.Run -dir <folder> -out <folder>`: renders PC (`.bloq`) and Quest (`_pc` from a `.cyoob`) notes the way each mod places them, for side-by-side checks
- `SaberBridge.Probe.Run -dir <folder> -out <file>`: lists what is inside `.saber` / `.whacker` bundles
- `SaberBridge.DemoSaber.Run -out <file.saber> [-name … -len … -width … -guard … -cover …]`: builds the demo sabers

App switches for development: `SABERBRIDGE_DATA=<folder>` uses another data folder (clean demo screenshots); `SABERBRIDGE_SIMULATE_MISSING=1` shows the system check as if nothing were installed (and does not start the automatic Unity setup).

## License

SaberBridge is released under the [MIT License](LICENSE). Bundled third-party parts keep their own licenses (Custom Avatars: LGPL-3.0, UniVRM: MIT); see [LICENSE-THIRD-PARTY.md](LICENSE-THIRD-PARTY.md). FinalIK and DynamicBone are paid assets and are never included.

<sub>Demo models: *Sendagaya Shino*, *Sendagaya Shibu*, *Vita*, *Vivi* and *Victoria Rubin*, the VRoid Studio sample models by pixiv Inc., released under [CC0](https://creativecommons.org/publicdomain/zero/1.0/) (see [pixiv's terms](https://vroid.pixiv.help/hc/en-us/articles/4402614652569)). Demo sabers: made for this project, CC0.</sub>

<sub>SaberBridge is a fan-made tool and is not affiliated with Beat Games, Meta, Unity, pixiv, or the authors of Custom Avatars, CustomSabers, Custom Notes, Qosmetics or VRM Qavatars.</sub>
