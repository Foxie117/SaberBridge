using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CustomAvatar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRM2BS
{
    /// <summary>
    /// Loads built .avatar files the way the mod does, checks them, and renders preview frames for the GUI:
    /// a turntable plus a few procedural animations (idle, wave, walk), with the skeleton projected to 2D per frame.
    ///   Unity.exe -batchmode -quit -projectPath ... -executeMethod VRM2BS.AvatarPreview.Run -avatarList "list.txt" -previewDir "C:\previews"
    /// Output per avatar: previewDir/SHA1-OF-FILE/{preview.json, cover.png, turn_00.jpg ..., idle_00.jpg ...}
    /// </summary>
    public static class AvatarPreview
    {
        private const int Width = 360;
        private const int Height = 480;
        private const int Frames = 24;
        private static readonly string[] Trackers = { "Head", "LeftHand", "RightHand", "Pelvis", "LeftLeg", "RightLeg" };

        // .avatar files built in this Unity session; previewed at the end of a conversion run when "-previewDir" is given
        internal static readonly List<string> Built = new List<string>();

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string list = VrmToAvatarConverter.GetArg(args, "-avatarList");
            RenderAll(File.ReadAllLines(list).Where(l => l.Trim().Length > 0), VrmToAvatarConverter.GetArg(args, "-previewDir"));
        }

        internal static void RenderBuiltIfRequested()
        {
            string dir = VrmToAvatarConverter.GetArg(Environment.GetCommandLineArgs(), "-previewDir");
            if (!string.IsNullOrEmpty(dir) && Built.Count > 0) RenderAll(Built.ToList(), dir);
            Built.Clear();
        }

        private static void RenderAll(IEnumerable<string> files, string previewDir)
        {
            int n = 0;
            List<string> all = files.ToList();
            foreach (string file in all)
            {
                n++;
                Debug.Log($"[VRM2BS] PREVIEW {n}/{all.Count} {Path.GetFileName(file)}");
                try
                {
                    RenderOne(file, Path.Combine(previewDir, Hash(file)));
                }
                catch (Exception ex)
                {
                    Debug.Log($"[VRM2BS] PREVIEW FAIL {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }

        internal static string Hash(string file) => VrmToAvatarConverter.FileHash(file).Replace("-", "");

        private static void RenderOne(string file, string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            List<string> checks = new List<string>();

            AssetBundle bundle = AssetBundle.LoadFromFile(file);
            if (bundle == null)
            {
                checks.Add(Check("bundle", "fail", ""));
                WriteJson(dir, null, null, 0, checks, new Dictionary<string, List<List<float[]>>>());
                return;
            }

            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                GameObject prefab = bundle.LoadAsset<GameObject>("_CustomAvatar");
                if (prefab == null)
                {
                    checks.Add(Check("prefab", "fail", ""));
                    WriteJson(dir, null, null, 0, checks, new Dictionary<string, List<List<float[]>>>());
                    return;
                }

                checks.Add(Check("bundle", "ok", ""));
                GameObject root = UnityEngine.Object.Instantiate(prefab);
                FixShaders(root);

                AvatarDescriptor d = root.GetComponent<AvatarDescriptor>();
                checks.Add(Check("descriptor", d != null && !string.IsNullOrWhiteSpace(d.name) ? "ok" : "warn", d?.name ?? ""));
                checks.Add(Check("cover", d != null && d.cover != null ? "ok" : "warn", ""));
                if (d != null && d.cover != null)
                    File.WriteAllBytes(Path.Combine(dir, "cover.png"), VrmToAvatarConverter.ReadableCopy(d.cover.texture, 256).EncodeToPNG());

                int trackers = Trackers.Count(t => root.transform.Find(t) != null);
                // head + hands only is a valid half-body avatar (legs follow the body)
                checks.Add(Check("trackers", trackers == Trackers.Length ? "ok" : trackers >= 3 ? "info" : "fail", $"{trackers}/{Trackers.Length}"));

                VRIKManager vrik = root.GetComponentInChildren<VRIKManager>(true);
                checks.Add(Check("ik", vrik != null && vrik.areReferencesFilled ? "ok" : "warn", ""));

                PoseManager pm = root.GetComponentInChildren<PoseManager>(true);
                checks.Add(Check("fingers", pm != null && pm.isValid ? "ok" : "warn", ""));

                DynamicBone[] dbs = root.GetComponentsInChildren<DynamicBone>(true);
                HashSet<Transform> physics = new HashSet<Transform>(dbs.Where(b => b.m_Root != null).SelectMany(b => b.m_Root.GetComponentsInChildren<Transform>(true)));
                checks.Add(Check("physics", dbs.Length > 0 ? "ok" : "info", $"{dbs.Length}|{physics.Count}"));

                FirstPersonExclusion fp = root.GetComponent<FirstPersonExclusion>();
                checks.Add(Check("firstperson", "info", (fp != null && fp.exclude != null ? fp.exclude.Length : 0).ToString()));

                int missingScripts = root.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
                if (missingScripts > 0) checks.Add(Check("scripts", "warn", missingScripts.ToString()));

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                Material[] materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
                int broken = materials.Count(m => m.shader == null || !m.shader.isSupported || m.shader.name == "Hidden/InternalErrorShader");
                checks.Add(Check("materials", broken == 0 ? "ok" : "warn", $"{materials.Length}|{broken}"));

                int tris = renderers.Sum(r => r is SkinnedMeshRenderer s && s.sharedMesh != null ? s.sharedMesh.triangles.Length / 3
                    : r.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh != null ? mf.sharedMesh.triangles.Length / 3 : 0);
                checks.Add(Check("triangles", tris > 150000 ? "warn" : "ok", tris.ToString()));

                Animator animator = root.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.isHuman && a.avatar != null);
                checks.Add(Check("humanoid", animator != null ? "ok" : "warn", ""));

                Bounds bounds = Bounds(renderers);

                // stray effect meshes can make the bounds huge; a body is never much taller than its head bone
                Transform headBone = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : root.transform.Find("Head");
                if (headBone != null && bounds.max.y > headBone.position.y * 1.35f + 0.15f)
                {
                    float top = headBone.position.y * 1.2f + 0.1f;
                    bounds = new Bounds(new Vector3(0, top / 2, 0), new Vector3(top * 0.8f, top, top * 0.5f));
                }

                float height = bounds.max.y;
                checks.Add(Check("height", height < 0.4f || height > 3.5f ? "warn" : "ok", height.ToString("0.00", CultureInfo.InvariantCulture)));

                Dictionary<string, List<List<float[]>>> skeleton = RenderFrames(root, animator, physics, bounds, dir);
                WriteJson(dir, d?.name, d?.author, height, checks, skeleton);
                UnityEngine.Object.DestroyImmediate(root);
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        // shaders compiled for another graphics API (a Windows bundle in a Linux editor) render pink; use the project's copy when it has one
        private static void FixShaders(GameObject root)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            foreach (Material m in r.sharedMaterials)
            {
                if (m == null || m.shader == null || m.shader.isSupported) continue;
                Shader local = Shader.Find(m.shader.name);
                if (local != null) m.shader = local;
            }
        }

        private static Bounds Bounds(Renderer[] renderers)
        {
            Renderer[] visible = renderers.Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (visible.Length == 0) return new Bounds(new Vector3(0, 0.85f, 0), new Vector3(1, 1.7f, 1));
            Bounds b = visible[0].bounds;
            foreach (Renderer r in visible.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }

        // ---------------------------------------------------------------- frames

        private static Dictionary<string, List<List<float[]>>> RenderFrames(GameObject root, Animator animator, HashSet<Transform> physics, Bounds bounds, string dir)
        {
            Dictionary<string, List<List<float[]>>> skeleton = new Dictionary<string, List<List<float[]>>>();

            GameObject camGo = new GameObject("PreviewCamera");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.1f, 0.14f, 1);
            cam.fieldOfView = 30;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100;

            // frame the whole body; leave room for raised arms and walking legs
            float h = Mathf.Max(bounds.max.y, 0.3f) * 1.12f;
            float w = Mathf.Max(bounds.size.x, bounds.size.z, h * 0.75f);
            float fitH = h * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float fitW = w * 0.5f / (Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Width / Height);
            Vector3 target = new Vector3(0, h * 0.48f, 0);
            camGo.transform.position = target + new Vector3(0, h * 0.04f, Mathf.Max(fitH, fitW) * 1.05f);
            camGo.transform.LookAt(target);

            // soft key light; the avatar shader has its own view light, others (Standard etc.) need a real one
            GameObject lightGo = new GameObject("PreviewLight");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(35, 160, 0);
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);

            RenderTexture rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            Texture2D tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            cam.targetTexture = rt;

            // every frame is rendered within one editor update; without this the skinned meshes keep the first pose
            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMatrixRecalculationPerRender = true;

            HumanPoseHandler poser = animator != null ? new HumanPoseHandler(animator.avatar, animator.transform) : null;
            HumanPose rest = new HumanPose();
            poser?.GetHumanPose(ref rest);
            Dictionary<string, int> muscle = Enumerable.Range(0, HumanTrait.MuscleCount).ToDictionary(i => HumanTrait.MuscleName[i], i => i);

            List<(Transform a, Transform b, int kind)> bones = BonePairs(root, animator, physics);

            void Shoot(string anim, int frame, float yaw)
            {
                root.transform.rotation = Quaternion.Euler(0, yaw, 0);
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, $"{anim}_{frame:00}.jpg"), tex.EncodeToJPG(88));

                if (!skeleton.TryGetValue(anim, out var frames)) skeleton[anim] = frames = new List<List<float[]>>();
                frames.Add(bones.Select(p =>
                {
                    Vector3 s1 = cam.WorldToScreenPoint(p.a.position), s2 = cam.WorldToScreenPoint(p.b.position);
                    return new[] { s1.x, Height - s1.y, s2.x, Height - s2.y, p.kind };
                }).ToList());
            }

            void Pose(Func<float, Dictionary<string, float>> anim, float t)
            {
                if (poser == null) return;
                HumanPose p = rest;
                p.muscles = (float[])rest.muscles.Clone();
                foreach (var kv in Relaxed()) if (muscle.TryGetValue(kv.Key, out int i)) p.muscles[i] = kv.Value;
                foreach (var kv in anim(t)) if (muscle.TryGetValue(kv.Key, out int i)) p.muscles[i] = kv.Value;
                poser.SetHumanPose(ref p);
            }

            // turntable in the pose the mod calibrates from
            for (int f = 0; f < Frames; f++) Shoot("turn", f, 360f * f / Frames);

            if (poser != null)
            {
                foreach (var (name, anim) in new (string, Func<float, Dictionary<string, float>>)[] { ("idle", Idle), ("wave", Wave), ("walk", Walk) })
                {
                    for (int f = 0; f < Frames; f++)
                    {
                        Pose(anim, (float)f / Frames);
                        Shoot(name, f, name == "walk" ? 35 : 15);
                    }
                }

                poser.SetHumanPose(ref rest);
            }

            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(camGo);
            UnityEngine.Object.DestroyImmediate(lightGo);
            return skeleton;
        }

        // humanoid bones (kind 0), physics chains (kind 1)
        private static List<(Transform, Transform, int)> BonePairs(GameObject root, Animator animator, HashSet<Transform> physics)
        {
            List<(Transform, Transform, int)> pairs = new List<(Transform, Transform, int)>();
            if (animator != null)
            {
                HashSet<Transform> human = new HashSet<Transform>(Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>()
                    .Where(b => b != HumanBodyBones.LastBone).Select(animator.GetBoneTransform).Where(t => t != null));
                foreach (Transform t in human)
                {
                    Transform p = t.parent;
                    while (p != null && !human.Contains(p)) p = p.parent;
                    if (p != null) pairs.Add((p, t, 0));
                }
            }

            foreach (Transform t in physics)
            {
                if (t.parent != null && (physics.Contains(t.parent) || pairs.Any(x => x.Item2 == t.parent))) pairs.Add((t.parent, t, 1));
            }

            return pairs;
        }

        // ---------------------------------------------------------------- procedural animations (muscle space, -1..1)

        private static float Wave01(float t, float phase = 0) => Mathf.Sin((t + phase) * Mathf.PI * 2);

        private static Dictionary<string, float> Relaxed() => new Dictionary<string, float>
        {
            ["Left Arm Down-Up"] = -0.55f, ["Right Arm Down-Up"] = -0.55f,
            ["Left Arm Front-Back"] = 0.1f, ["Right Arm Front-Back"] = 0.1f,
            ["Left Forearm Stretch"] = 0.75f, ["Right Forearm Stretch"] = 0.75f,
        };

        private static Dictionary<string, float> Idle(float t) => new Dictionary<string, float>
        {
            ["Spine Front-Back"] = 0.04f * Wave01(t),
            ["Chest Front-Back"] = 0.05f * Wave01(t, 0.1f),
            ["Spine Left-Right"] = 0.04f * Wave01(t, 0.25f),
            ["Head Turn Left-Right"] = 0.18f * Wave01(t, 0.4f),
            ["Head Nod Down-Up"] = 0.08f * Wave01(t * 2),
            ["Left Arm Down-Up"] = -0.55f + 0.04f * Wave01(t),
            ["Right Arm Down-Up"] = -0.55f + 0.04f * Wave01(t, 0.5f),
        };

        private static Dictionary<string, float> Wave(float t) => new Dictionary<string, float>
        {
            ["Right Arm Down-Up"] = 0.95f,
            ["Right Arm Front-Back"] = 0.25f,
            ["Right Forearm Stretch"] = -0.1f + 0.3f * Wave01(t * 2),
            ["Right Forearm Twist In-Out"] = -0.4f,
            ["Right Hand In-Out"] = 0.4f * Wave01(t * 2),
            ["Head Turn Left-Right"] = -0.12f,
            ["Head Tilt Left-Right"] = 0.1f,
            ["Spine Left-Right"] = 0.05f,
        };

        private static Dictionary<string, float> Walk(float t)
        {
            float s = Wave01(t);
            return new Dictionary<string, float>
            {
                ["Left Upper Leg Front-Back"] = 0.3f * s,
                ["Right Upper Leg Front-Back"] = -0.3f * s,
                ["Left Lower Leg Stretch"] = 1f - 0.7f * Mathf.Max(0, -Wave01(t, 0.15f)),
                ["Right Lower Leg Stretch"] = 1f - 0.7f * Mathf.Max(0, Wave01(t, 0.15f)),
                ["Left Foot Up-Down"] = 0.2f * s,
                ["Right Foot Up-Down"] = -0.2f * s,
                ["Left Arm Front-Back"] = -0.35f * s,
                ["Right Arm Front-Back"] = 0.35f * s,
                ["Spine Twist Left-Right"] = 0.1f * s,
                ["Chest Twist Left-Right"] = -0.12f * s,
            };
        }

        // ---------------------------------------------------------------- output

        private static string Check(string code, string level, string value) =>
            $"{{\"code\":\"{code}\",\"level\":\"{level}\",\"value\":\"{Escape(value)}\"}}";

        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");

        private static void WriteJson(string dir, string name, string author, float height, List<string> checks, Dictionary<string, List<List<float[]>>> skeleton)
        {
            StringBuilder sb = new StringBuilder();
            CultureInfo inv = CultureInfo.InvariantCulture;
            sb.Append("{\"version\":1");
            sb.Append($",\"name\":\"{Escape(name)}\",\"author\":\"{Escape(author)}\",\"height\":{height.ToString("0.00", inv)}");
            sb.Append($",\"width\":{Width},\"height_px\":{Height},\"frames\":{Frames}");
            sb.Append(",\"checks\":[").Append(string.Join(",", checks)).Append("]");
            sb.Append(",\"anims\":{");
            sb.Append(string.Join(",", skeleton.Select(kv => $"\"{kv.Key}\":[" + string.Join(",", kv.Value.Select(frame =>
                "[" + string.Join(",", frame.Select(seg => "[" + string.Join(",", seg.Select(v => v.ToString("0.#", inv))) + "]")) + "]")) + "]")));
            sb.Append("}}");
            File.WriteAllText(Path.Combine(dir, "preview.json"), sb.ToString());
        }
    }
}
