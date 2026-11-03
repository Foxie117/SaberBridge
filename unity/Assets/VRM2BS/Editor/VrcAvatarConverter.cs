using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CustomAvatar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UniVRM10;

namespace VRM2BS
{
    /// <summary>
    /// VRChat avatar (scene or prefab extracted from a .unitypackage) -> Beat Saber Custom Avatars .avatar.
    /// VRChat SDK scripts are not in this project, so avatar descriptors, PhysBones and PhysBone colliders are
    /// read straight from the scene/prefab YAML and mapped onto the loaded objects by file ID.
    ///   Unity.exe -batchmode -quit -projectPath ... -executeMethod VRM2BS.VrcAvatarConverter.Run
    ///             -importDir "Assets/VRC_Import/Name" -outDir "C:\out"
    /// </summary>
    public static class VrcAvatarConverter
    {
        private static readonly Regex SkipPath = new Regex("quest|backup", RegexOptions.IgnoreCase);

        private class YamlDoc
        {
            public long id;
            public string text;
        }

        private class PhysBoneData
        {
            public long gameObject, root;
            public float pull, spring, stiffness, immobile, gravity, radius;
            public Vector3 endpoint;
            public List<long> ignore = new List<long>(), colliders = new List<long>();
        }

        private class ColliderData
        {
            public long gameObject, root;
            public int shape;
            public bool inside;
            public float radius, height;
            public Vector3 position;
            public Quaternion rotation = Quaternion.identity;
        }

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string importDir = VrmToAvatarConverter.GetArg(args, "-importDir");
            string outDir = VrmToAvatarConverter.GetArg(args, "-outDir");
            string onlyFile = VrmToAvatarConverter.GetArg(args, "-source");
            Directory.CreateDirectory(outDir);

            VrmToAvatarConverter._template = VrmToAvatarConverter.MeasureTemplate();
            HashSet<string> usedNames = new HashSet<string>(Directory.GetFiles(outDir, "*.avatar").Select(Path.GetFileNameWithoutExtension), StringComparer.OrdinalIgnoreCase);

            List<string> sources = AssetDatabase.FindAssets("t:Scene t:Prefab", new[] { importDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .Where(p => onlyFile == null ? !SkipPath.IsMatch(p) : p.EndsWith(onlyFile))
                .Where(p => File.ReadAllText(p).Contains("\n  ViewPosition:"))
                .OrderBy(p => p)
                .ToList();

            if (sources.Count == 0) VrmToAvatarConverter.Log($"FAIL {importDir}: no VRChat avatar descriptor found");

            foreach (string source in sources)
            {
                try
                {
                    ConvertSource(source, outDir, usedNames);
                }
                catch (Exception ex)
                {
                    VrmToAvatarConverter.Log($"FAIL {source}: {ex}");
                }
            }

            File.AppendAllLines(Path.Combine(outDir, "conversion_report.txt"), VrmToAvatarConverter.Report);
            Debug.Log("[VRM2BS] REPORT\n" + string.Join("\n", VrmToAvatarConverter.Report));
            AvatarPreview.RenderBuiltIfRequested();
        }

        // ---------------------------------------------------------------- per source file

        private static void ConvertSource(string source, string outDir, HashSet<string> usedNames)
        {
            List<YamlDoc> docs = ParseYaml(File.ReadAllText(source));
            bool isScene = source.EndsWith(".unity");

            Dictionary<long, UnityEngine.Object> byId;
            List<GameObject> loadedRoots;

            if (isScene)
            {
                EditorSceneManager.OpenScene(source, OpenSceneMode.Single);
                loadedRoots = EditorSceneManager.GetActiveScene().GetRootGameObjects().ToList();
                byId = MapSceneObjects(loadedRoots, File.ReadAllText(source));
            }
            else
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(source));
                loadedRoots = new List<GameObject> { instance };
                byId = MapPrefabObjects(instance);
            }

            List<PhysBoneData> physBones = docs.Where(d => d.text.Contains("\n  pull:")).Select(ParsePhysBone).ToList();
            Dictionary<long, ColliderData> colliders = docs.Where(d => d.text.Contains("\n  shapeType:")).ToDictionary(d => d.id, ParseCollider);

            foreach (YamlDoc descriptor in docs.Where(d => d.text.Contains("\n  ViewPosition:")))
            {
                long goId = ReadRef(descriptor.text, "m_GameObject");
                if (!byId.TryGetValue(goId, out UnityEngine.Object o) || !(o is GameObject avatarGo))
                {
                    VrmToAvatarConverter.Log($"FAIL {source}: descriptor object {goId} not found in loaded hierarchy");
                    continue;
                }

                Vector3 view = ReadVector(descriptor.text, "ViewPosition");
                string label = $"{Path.GetFileName(source)} / {avatarGo.name}";

                try
                {
                    ConvertAvatar(avatarGo, view, physBones, colliders, byId, source, outDir, usedNames, label);
                }
                catch (Exception ex)
                {
                    VrmToAvatarConverter.Log($"FAIL {label}: {ex}");
                }
            }
        }

        private static void ConvertAvatar(GameObject original, Vector3 viewPosition, List<PhysBoneData> physBones, Dictionary<long, ColliderData> colliders,
                                          Dictionary<long, UnityEngine.Object> byId, string source, string outDir, HashSet<string> usedNames, string label)
        {
            List<string> notes = new List<string>();
            string avatarName = CleanName(original.name);
            string safeId = VrmToAvatarConverter.MakeSafe("vrc_" + Path.GetFileNameWithoutExtension(source) + "_" + avatarName);
            string workDir = $"{VrmToAvatarConverter.WorkRoot}/{safeId}";
            if (!AssetDatabase.IsValidFolder(VrmToAvatarConverter.WorkRoot)) AssetDatabase.CreateFolder("Assets", "VRM2BS_Work");
            if (AssetDatabase.IsValidFolder(workDir)) AssetDatabase.DeleteAsset(workDir); // stale assets would rename new ones ("body 2")
            AssetDatabase.CreateFolder(VrmToAvatarConverter.WorkRoot, safeId);

            VrmToAvatarConverter.Begin(label);
            VrmToAvatarConverter.Stage("skeleton");
            Vector3 eyeWorld = original.transform.TransformPoint(viewPosition);

            // work on a disconnected copy placed at the origin; remember how original transforms map to the copy
            GameObject model = UnityEngine.Object.Instantiate(original);
            model.name = "Model";
            model.SetActive(true);
            Matrix4x4 toCopy = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one) * original.transform.worldToLocalMatrix;
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            model.transform.localScale = original.transform.lossyScale;
            Vector3 eye = model.transform.TransformPoint(original.transform.InverseTransformPoint(eyeWorld));

            Dictionary<Transform, Transform> copyOf = MapHierarchy(original.transform, model.transform);

            Animator animator = model.GetComponent<Animator>() ?? model.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.isHuman);
            if (animator == null || !animator.isHuman || animator.avatar == null) throw new Exception("no humanoid Animator (FBX rig is not set to Humanoid)");

            if (animator.gameObject != model)
            {
                // keep the humanoid object as the model root so VRIK and the pose manager find it
                notes.Add("animator not on avatar root");
            }

            GameObject root = new GameObject(avatarName);
            model.transform.SetParent(root.transform, true);

            VrmToAvatarConverter.Stage("physics");
            int chains = ConvertPhysBones(original.transform, physBones, colliders, byId, copyOf, animator, notes);

            HashSet<Transform> humanoid = HumanoidBones(animator);
            int autoTails = AddMissingTailPhysics(model.transform, humanoid);
            if (autoTails > 0) notes.Add($"{autoTails} tail(s) without physics got it automatically");

            GameObject[] firstPerson = VrmToAvatarConverter.CollectFirstPersonExclusions(model.transform, animator, new List<RendererFirstPersonFlags>());

            VrmToAvatarConverter.RemoveForeignComponents(root);

            // inactive objects in VRChat avatars are usually optional toggles (props, alternative outfits); they stay off
            foreach (Animator a in model.GetComponentsInChildren<Animator>(true)) a.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            VrmToAvatarConverter.Stage("materials");
            int materials = ConvertMaterials(model, workDir, notes);

            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            VrmToAvatarConverter.BuildTrackers(root.transform, animator, eye - head.position);

            VrmToAvatarConverter.Stage("ik");
            VRIKManager vrik = animator.gameObject.AddComponent<VRIKManager>();
            VrmToAvatarConverter.FillVrikReferences(vrik, animator.transform, animator);
            if (!vrik.areReferencesFilled) notes.Add("VRIK references incomplete");

            if (!VrmToAvatarConverter.SetupFingerPoses(animator.gameObject, animator)) notes.Add("no finger bones, finger poses skipped");

            AvatarDescriptor descriptor = root.AddComponent<AvatarDescriptor>();
            descriptor.name = avatarName;
            descriptor.author = "";
            descriptor.cover = VrmToAvatarConverter.MakeCover(null, root, animator, workDir);

            new GameObject("Body").transform.SetParent(root.transform, false);

            if (firstPerson.Length > 0) root.AddComponent<FirstPersonExclusion>().exclude = firstPerson;

            PrefabUtility.SaveAsPrefabAsset(root, $"{workDir}/{safeId}_avatar.prefab");

            string fileName = VrmToAvatarConverter.UniqueName(VrmToAvatarConverter.MakeSafe(avatarName), usedNames) + ".avatar";
            VrmToAvatarConverter.ExportBundle(root, Path.Combine(outDir, fileName));

            VrmToAvatarConverter.Log($"OK   {label} -> {fileName}  ({chains} dynamic bone chains, {materials} materials, {firstPerson.Length} first-person exclusions)" +
                                     (notes.Count > 0 ? "\n       notes: " + string.Join("; ", notes) : ""));

            ExportVariants(root, animator, source, workDir, outDir, usedNames);
            UnityEngine.Object.DestroyImmediate(root);
        }

        // ---------------------------------------------------------------- colour variants

        /// <summary>
        /// Optional "variants.txt" next to the imported package (Assets/VRC_Import/Name/variants.txt), one variant per line:
        ///   Variant Name|materialName=Assets/path/texture.png@x,y,w,h;otherMaterial=...
        /// The rectangle (in 0..1 UV of the texture) is cropped out and used as that material's albedo.
        /// </summary>
        private static void ExportVariants(GameObject root, Animator animator, string source, string workDir, string outDir, HashSet<string> usedNames)
        {
            string importDir = source.Substring(0, source.IndexOf('/', "Assets/VRC_Import/".Length) + 1);
            string spec = importDir + "variants.txt";
            if (!File.Exists(spec)) return;

            AvatarDescriptor baseDescriptor = root.GetComponent<AvatarDescriptor>();
            string baseName = baseDescriptor.name;

            foreach (string line in File.ReadAllLines(spec).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")))
            {
                string[] parts = line.Split('|');
                string variant = parts[0].Trim();
                string variantDir = $"{workDir}/{VrmToAvatarConverter.MakeSafe(variant)}";
                if (!AssetDatabase.IsValidFolder(variantDir)) AssetDatabase.CreateFolder(workDir, VrmToAvatarConverter.MakeSafe(variant));

                Dictionary<string, Texture2D> swaps = new Dictionary<string, Texture2D>();
                foreach (string swap in parts[1].Split(';'))
                {
                    Match m = Regex.Match(swap.Trim(), @"^(.+?)=(.+?)@([\d.]+),([\d.]+),([\d.]+),([\d.]+)$");
                    if (!m.Success) continue;
                    Texture tex = AssetDatabase.LoadAssetAtPath<Texture>(importDir + m.Groups[2].Value);
                    if (tex == null) { VrmToAvatarConverter.Log($"WARN variant {variant}: texture {m.Groups[2].Value} not found"); continue; }
                    swaps[m.Groups[1].Value] = Crop(tex, F(m.Groups[3].Value), F(m.Groups[4].Value), F(m.Groups[5].Value), F(m.Groups[6].Value),
                                                    $"{variantDir}/{VrmToAvatarConverter.MakeSafe(m.Groups[1].Value)}.png");
                }

                GameObject copy = UnityEngine.Object.Instantiate(root);
                Dictionary<Material, Material> replaced = new Dictionary<Material, Material>();

                foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null || !swaps.TryGetValue(mats[i].name, out Texture2D t)) continue;
                        if (!replaced.TryGetValue(mats[i], out Material nm))
                        {
                            nm = new Material(mats[i]) { name = mats[i].name };
                            nm.SetTexture("_MainTex", t);
                            nm.SetTexture("_ShadeTex", t);
                            nm.SetTextureScale("_MainTex", Vector2.one);
                            nm.SetTextureOffset("_MainTex", Vector2.zero);
                            AssetDatabase.CreateAsset(nm, AssetDatabase.GenerateUniqueAssetPath($"{variantDir}/{VrmToAvatarConverter.MakeSafe(nm.name)}.mat"));
                            replaced[mats[i]] = nm;
                        }
                        mats[i] = nm;
                    }
                    r.sharedMaterials = mats;
                }

                string variantName = $"{baseName} - {variant}";
                copy.name = variantName;
                AvatarDescriptor d = copy.GetComponent<AvatarDescriptor>();
                d.name = variantName;
                d.cover = VrmToAvatarConverter.MakeCover(null, copy, copy.GetComponentInChildren<Animator>(), variantDir);

                string fileName = VrmToAvatarConverter.UniqueName(VrmToAvatarConverter.MakeSafe(variantName), usedNames) + ".avatar";
                VrmToAvatarConverter.ExportBundle(copy, Path.Combine(outDir, fileName));
                UnityEngine.Object.DestroyImmediate(copy);

                VrmToAvatarConverter.Log($"OK   variant {variantName} -> {fileName} ({replaced.Count} materials retextured)");
            }
        }

        private static Texture2D Crop(Texture tex, float x, float y, float w, float h, string path)
        {
            Texture2D full = VrmToAvatarConverter.ReadableCopy(tex, 4096);
            int px = Mathf.RoundToInt(x * full.width), py = Mathf.RoundToInt(y * full.height);
            int pw = Mathf.RoundToInt(w * full.width), ph = Mathf.RoundToInt(h * full.height);
            Texture2D cropped = new Texture2D(pw, ph, TextureFormat.RGBA32, true);
            cropped.SetPixels(full.GetPixels(px, py, pw, ph));
            cropped.Apply();
            File.WriteAllBytes(path, cropped.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static string CleanName(string n)
        {
            n = Regex.Replace(n, @"\s*\(\d+\)$", "");
            return string.IsNullOrWhiteSpace(n) ? "Avatar" : n.Trim();
        }

        private static Dictionary<Transform, Transform> MapHierarchy(Transform a, Transform b)
        {
            Dictionary<Transform, Transform> map = new Dictionary<Transform, Transform> { [a] = b };
            for (int i = 0; i < a.childCount && i < b.childCount; i++)
            {
                foreach (KeyValuePair<Transform, Transform> kv in MapHierarchy(a.GetChild(i), b.GetChild(i))) map[kv.Key] = kv.Value;
            }
            return map;
        }

        private static HashSet<Transform> HumanoidBones(Animator animator)
        {
            HashSet<Transform> set = new HashSet<Transform>();
            foreach (HumanBodyBones b in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (b == HumanBodyBones.LastBone) continue;
                Transform t = animator.GetBoneTransform(b);
                if (t) set.Add(t);
            }
            return set;
        }

        // ---------------------------------------------------------------- object ids

        private static Dictionary<long, UnityEngine.Object> MapSceneObjects(List<GameObject> roots, string sceneText)
        {
            Dictionary<long, UnityEngine.Object> map = new Dictionary<long, UnityEngine.Object>();
            Dictionary<(ulong, ulong), UnityEngine.Object> nested = new Dictionary<(ulong, ulong), UnityEngine.Object>();

            foreach (GameObject r in roots)
            {
                foreach (Transform t in r.GetComponentsInChildren<Transform>(true))
                {
                    foreach (UnityEngine.Object o in new UnityEngine.Object[] { t, t.gameObject })
                    {
                        GlobalObjectId gid = GlobalObjectId.GetGlobalObjectIdSlow(o);
                        if (gid.targetPrefabId == 0)
                        {
                            map[(long)gid.targetObjectId] = o;
                        }
                        else
                        {
                            nested[(gid.targetObjectId, gid.targetPrefabId)] = o;
                            map[(long)((gid.targetObjectId ^ gid.targetPrefabId) & 0x7FFFFFFFFFFFFFFF)] = o;
                        }
                    }
                }
            }

            // stripped entries name their source object and prefab instance explicitly; older Unity versions
            // did not derive the stripped ID from those two, so map them by what the file says
            foreach (Match m in Regex.Matches(sceneText,
                         @"^--- !u!\d+ &(-?\d+) stripped\s*\n[^\n]*\n\s+m_CorrespondingSourceObject: \{fileID: (-?\d+)[^\n]*\n(?:[^\n]*\n)?\s+m_PrefabInstance: \{fileID: (-?\d+)",
                         RegexOptions.Multiline))
            {
                ulong src = unchecked((ulong)long.Parse(m.Groups[2].Value));
                ulong inst = unchecked((ulong)long.Parse(m.Groups[3].Value));
                if (nested.TryGetValue((src, inst), out UnityEngine.Object o)) map[long.Parse(m.Groups[1].Value)] = o;
            }

            return map;
        }

        private static Dictionary<long, UnityEngine.Object> MapPrefabObjects(GameObject instance)
        {
            Dictionary<long, UnityEngine.Object> map = new Dictionary<long, UnityEngine.Object>();
            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
            {
                foreach (UnityEngine.Object o in new UnityEngine.Object[] { t, t.gameObject })
                {
                    UnityEngine.Object src = PrefabUtility.GetCorrespondingObjectFromSource(o);
                    if (src != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(src, out string _, out long id)) map[id] = o;
                }
            }
            return map;
        }

        // ---------------------------------------------------------------- YAML

        private static List<YamlDoc> ParseYaml(string text)
        {
            List<YamlDoc> docs = new List<YamlDoc>();
            foreach (Match m in Regex.Matches(text, @"^--- !u!114 &(-?\d+)[^\n]*\n(.*?)(?=^--- |\z)", RegexOptions.Multiline | RegexOptions.Singleline))
            {
                docs.Add(new YamlDoc { id = long.Parse(m.Groups[1].Value), text = "\n" + m.Groups[2].Value });
            }
            return docs;
        }

        private static float ReadFloat(string doc, string key, float fallback = 0)
        {
            Match m = Regex.Match(doc, $@"\n  {key}: ([-\d.eE+]+)\s*\n");
            return m.Success ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : fallback;
        }

        private static long ReadRef(string doc, string key)
        {
            Match m = Regex.Match(doc, $@"\n  {key}: \{{fileID: (-?\d+)");
            return m.Success ? long.Parse(m.Groups[1].Value) : 0;
        }

        private static Vector3 ReadVector(string doc, string key)
        {
            Match m = Regex.Match(doc, $@"\n  {key}: \{{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+)");
            return m.Success ? new Vector3(F(m.Groups[1].Value), F(m.Groups[2].Value), F(m.Groups[3].Value)) : Vector3.zero;
        }

        private static Quaternion ReadQuaternion(string doc, string key)
        {
            Match m = Regex.Match(doc, $@"\n  {key}: \{{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+), w: ([-\d.eE+]+)");
            return m.Success ? new Quaternion(F(m.Groups[1].Value), F(m.Groups[2].Value), F(m.Groups[3].Value), F(m.Groups[4].Value)) : Quaternion.identity;
        }

        private static List<long> ReadRefList(string doc, string key)
        {
            Match m = Regex.Match(doc, $@"\n  {key}:\s*\n((?:  - \{{fileID: -?\d+[^\n]*\n)*)");
            return m.Success ? Regex.Matches(m.Groups[1].Value, @"fileID: (-?\d+)").Cast<Match>().Select(x => long.Parse(x.Groups[1].Value)).Where(x => x != 0).ToList() : new List<long>();
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        private static PhysBoneData ParsePhysBone(YamlDoc d) => new PhysBoneData
        {
            gameObject = ReadRef(d.text, "m_GameObject"),
            root = ReadRef(d.text, "rootTransform"),
            pull = ReadFloat(d.text, "pull", 0.2f),
            spring = ReadFloat(d.text, "spring", 0.2f),
            stiffness = ReadFloat(d.text, "stiffness", 0.2f),
            immobile = ReadFloat(d.text, "immobile", 0f),
            gravity = ReadFloat(d.text, "gravity", 0f),
            radius = ReadFloat(d.text, "radius", 0f),
            endpoint = ReadVector(d.text, "endpointPosition"),
            ignore = ReadRefList(d.text, "ignoreTransforms"),
            colliders = ReadRefList(d.text, "colliders"),
        };

        private static ColliderData ParseCollider(YamlDoc d) => new ColliderData
        {
            gameObject = ReadRef(d.text, "m_GameObject"),
            root = ReadRef(d.text, "rootTransform"),
            shape = (int)ReadFloat(d.text, "shapeType", 0),
            inside = ReadFloat(d.text, "insideBounds", 0) > 0.5f,
            radius = ReadFloat(d.text, "radius", 0.05f),
            height = ReadFloat(d.text, "height", 0f),
            position = ReadVector(d.text, "position"),
            rotation = ReadQuaternion(d.text, "rotation"),
        };

        private static Transform ResolveTransform(Dictionary<long, UnityEngine.Object> byId, long id, long fallbackGameObject)
        {
            if (id != 0 && byId.TryGetValue(id, out UnityEngine.Object o)) return o is GameObject g ? g.transform : o as Transform;
            if (byId.TryGetValue(fallbackGameObject, out UnityEngine.Object go)) return go is GameObject g2 ? g2.transform : go as Transform;
            return null;
        }

        // ---------------------------------------------------------------- physics

        private static int ConvertPhysBones(Transform originalRoot, List<PhysBoneData> physBones, Dictionary<long, ColliderData> colliders,
                                            Dictionary<long, UnityEngine.Object> byId, Dictionary<Transform, Transform> copyOf, Animator animator, List<string> notes)
        {
            HashSet<Transform> humanoid = HumanoidBones(animator);
            Dictionary<long, DynamicBoneCollider> madeColliders = new Dictionary<long, DynamicBoneCollider>();
            int chains = 0, unresolved = 0;

            foreach (PhysBoneData pb in physBones)
            {
                Transform srcRoot = ResolveTransform(byId, pb.root, pb.gameObject);
                if (srcRoot == null || !srcRoot.IsChildOf(originalRoot)) { if (srcRoot == null) unresolved++; continue; }
                if (!copyOf.TryGetValue(srcRoot, out Transform pbRoot)) continue;

                HashSet<Transform> ignored = new HashSet<Transform>(pb.ignore.Select(id => ResolveTransform(byId, id, 0)).Where(t => t != null && copyOf.ContainsKey(t)).Select(t => copyOf[t]));

                // simulated set: the PhysBone subtree, minus ignored subtrees and humanoid bones (those belong to IK)
                List<Transform> sim = new List<Transform>();
                CollectSim(pbRoot, ignored, humanoid, sim, isRoot: true);
                HashSet<Transform> simSet = new HashSet<Transform>(sim);

                List<DynamicBoneColliderBase> dbColliders = new List<DynamicBoneColliderBase>();
                foreach (long cid in pb.colliders)
                {
                    if (!madeColliders.TryGetValue(cid, out DynamicBoneCollider dc))
                    {
                        if (colliders.TryGetValue(cid, out ColliderData cd)) dc = MakeCollider(cd, byId, copyOf);
                        madeColliders[cid] = dc;
                    }
                    if (dc != null) dbColliders.Add(dc);
                }

                foreach (Transform chainRoot in sim.Where(t => !simSet.Contains(t.parent)))
                {
                    if (chainRoot.childCount == 0 && pb.endpoint == Vector3.zero) continue; // nothing to swing

                    DynamicBone db = chainRoot.gameObject.AddComponent<DynamicBone>();
                    db.m_Root = chainRoot;
                    db.m_UpdateRate = 60;
                    db.m_Elasticity = Mathf.Clamp(pb.pull * 0.5f, 0.01f, 0.5f);
                    db.m_Damping = Mathf.Clamp(0.3f - pb.spring * 0.25f, 0.02f, 0.5f);
                    db.m_Stiffness = Mathf.Clamp(pb.stiffness * 0.5f, 0f, 0.8f);
                    db.m_Inert = Mathf.Clamp01(pb.immobile);
                    db.m_Gravity = new Vector3(0, -pb.gravity * 0.03f, 0);
                    db.m_Radius = pb.radius;
                    db.m_EndOffset = pb.endpoint;
                    db.m_Colliders = new List<DynamicBoneColliderBase>(dbColliders);
                    db.m_Exclusions = chainRoot.GetComponentsInChildren<Transform>(true)
                        .Where(t => t != chainRoot && !simSet.Contains(t) && simSet.Contains(t.parent)).ToList();
                    chains++;
                }
            }

            if (unresolved > 0) notes.Add($"{unresolved} PhysBones could not be matched to bones");
            return chains;
        }

        private static void CollectSim(Transform t, HashSet<Transform> ignored, HashSet<Transform> humanoid, List<Transform> sim, bool isRoot)
        {
            if (ignored.Contains(t)) return;
            if (!humanoid.Contains(t)) sim.Add(t);
            foreach (Transform c in t) CollectSim(c, ignored, humanoid, sim, false);
        }

        private static DynamicBoneCollider MakeCollider(ColliderData cd, Dictionary<long, UnityEngine.Object> byId, Dictionary<Transform, Transform> copyOf)
        {
            if (cd.shape == 2) return null; // planes are not supported by this Dynamic Bone version

            Transform src = ResolveTransform(byId, cd.root, cd.gameObject);
            if (src == null || !copyOf.TryGetValue(src, out Transform parent)) return null;

            GameObject go = new GameObject("DBCollider");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = cd.position;
            go.transform.localRotation = cd.rotation;

            DynamicBoneCollider dc = go.AddComponent<DynamicBoneCollider>();
            dc.m_Radius = cd.radius;
            dc.m_Direction = DynamicBoneColliderBase.Direction.Y;
            dc.m_Height = cd.shape == 1 ? Mathf.Max(cd.height, cd.radius * 2) : 0;
            dc.m_Bound = cd.inside ? DynamicBoneColliderBase.Bound.Inside : DynamicBoneColliderBase.Bound.Outside;
            return dc;
        }

        private static int AddMissingTailPhysics(Transform modelRoot, HashSet<Transform> humanoid)
        {
            HashSet<Transform> skinBones = new HashSet<Transform>(modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.bones).Where(b => b != null));
            int added = 0;

            foreach (Transform t in modelRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!VrmToAvatarConverter.TailName.IsMatch(t.name) || humanoid.Contains(t) || !skinBones.Contains(t)) continue;
                if (t.parent != null && VrmToAvatarConverter.TailName.IsMatch(t.parent.name) && skinBones.Contains(t.parent)) continue;
                if (t.GetComponentInParent<DynamicBone>() != null || t.GetComponentsInChildren<DynamicBone>(true).Length > 0) continue;
                if (!t.GetComponentsInChildren<Transform>(true).Any(c => c != t && skinBones.Contains(c))) continue;

                DynamicBone db = t.gameObject.AddComponent<DynamicBone>();
                db.m_Root = t;
                db.m_UpdateRate = 60;
                db.m_Damping = 0.2f;
                db.m_Elasticity = 0.08f;
                db.m_Stiffness = 0.15f;
                db.m_Gravity = new Vector3(0, -0.003f, 0);
                db.m_Exclusions = new List<Transform>();
                added++;
            }

            return added;
        }

        // ---------------------------------------------------------------- materials (shaders are missing, read raw saved properties)

        private static int ConvertMaterials(GameObject model, string workDir, List<string> notes)
        {
            Shader shader = Shader.Find(VrmToAvatarConverter.ShaderName);
            string matDir = $"{workDir}/Materials";
            if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder(workDir, "Materials");

            Dictionary<Material, Material> map = new Dictionary<Material, Material>();
            HashSet<string> shaders = new HashSet<string>();

            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material src = mats[i];
                    if (src == null) continue;
                    if (!map.TryGetValue(src, out Material dst))
                    {
                        shaders.Add(src.shader != null ? src.shader.name : "<missing>");
                        dst = ConvertMaterial(src, shader, matDir, notes);
                        AssetDatabase.CreateAsset(dst, AssetDatabase.GenerateUniqueAssetPath($"{matDir}/{VrmToAvatarConverter.MakeSafe(src.name)}.mat"));
                        map[src] = dst;
                    }
                    mats[i] = dst;
                }
                r.sharedMaterials = mats;
            }

            notes.Add("source shaders: " + string.Join(", ", shaders));
            return map.Count;
        }

        private static Material ConvertMaterial(Material src, Shader shader, string matDir, List<string> notes)
        {
            SerializedObject so = new SerializedObject(src);
            Dictionary<string, SerializedProperty> tex = ReadPropertyMap(so, "m_SavedProperties.m_TexEnvs");
            Dictionary<string, SerializedProperty> floats = ReadPropertyMap(so, "m_SavedProperties.m_Floats");
            Dictionary<string, SerializedProperty> colors = ReadPropertyMap(so, "m_SavedProperties.m_Colors");

            Texture T(string n) => tex.TryGetValue(n, out SerializedProperty p) ? p.FindPropertyRelative("m_Texture").objectReferenceValue as Texture : null;
            float Fl(string n, float d) => floats.TryGetValue(n, out SerializedProperty p) ? p.floatValue : d;
            Color C(string n, Color d) => colors.TryGetValue(n, out SerializedProperty p) ? p.colorValue : d;

            Material dst = new Material(shader) { name = src.name };

            Texture main = T("_MainTex") ?? T("_BaseMap") ?? T("_BaseColorMap");

            Texture baked = BakePoiyomi(src.name, main, T, Fl, C, matDir);
            if (baked != null)
            {
                main = baked;
                notes.Add($"Poiyomi color/decals baked into '{src.name}'");
            }
            dst.SetTexture("_MainTex", main);
            if (tex.TryGetValue("_MainTex", out SerializedProperty mt))
            {
                dst.SetTextureScale("_MainTex", mt.FindPropertyRelative("m_Scale").vector2Value);
                dst.SetTextureOffset("_MainTex", mt.FindPropertyRelative("m_Offset").vector2Value);
            }

            Color color = C("_Color", C("_BaseColor", Color.white));
            dst.SetColor("_Color", color);
            dst.SetColor("_ShadeColor", new Color(color.r * 0.78f, color.g * 0.72f, color.b * 0.8f, color.a));
            dst.SetTexture("_ShadeTex", main);
            dst.SetFloat("_ShadingToonyFactor", 0.85f);

            Texture normal = T("_BumpMap");
            if (normal != null)
            {
                dst.SetTexture("_BumpMap", normal);
                dst.SetFloat("_BumpScale", Fl("_BumpScale", 1));
                dst.EnableKeyword("_NORMALMAP");
            }

            bool emissionOn = Fl("_EnableEmission", 0) > 0.5f || src.IsKeywordEnabled("_EMISSION") || src.shaderKeywords.Contains("_EMISSION");
            if (emissionOn && T("_EmissionMap") != null)
            {
                dst.SetTexture("_EmissionMap", T("_EmissionMap"));
                dst.SetColor("_EmissionColor", C("_EmissionColor", Color.white) * Mathf.Max(Fl("_EmissionStrength", 1), 0.01f));
            }

            dst.SetFloat("_M_CullMode", Fl("_Cull", Fl("_CullMode", 2)));

            // Poiyomi/Standard: _Mode 0 opaque, 1 cutout, 2+ fade/transparent; fall back to the render queue
            int queue = src.renderQueue;
            float mode = Fl("_Mode", -1);
            bool cutout = mode == 1 || (mode < 0 && queue >= 2450 && queue < 2500);
            bool blend = mode >= 2 || (mode < 0 && queue >= 2500);

            if (blend)
            {
                dst.EnableKeyword("_ALPHABLEND_ON");
                dst.SetFloat("_M_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                dst.SetFloat("_M_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                dst.SetFloat("_M_ZWrite", 0);
                dst.renderQueue = 3000;
            }
            else if (cutout)
            {
                dst.EnableKeyword("_ALPHATEST_ON");
                dst.SetFloat("_Cutoff", Fl("_Cutoff", 0.5f));
                dst.renderQueue = 2450;
            }
            else
            {
                dst.renderQueue = 2000;
            }

            return dst;
        }

        /// <summary>
        /// Poiyomi colours the albedo at render time (hue/saturation/brightness, up to four UV decals).
        /// Beat Saber cannot run Poiyomi, so the result is baked into a new texture.
        /// </summary>
        private static Texture BakePoiyomi(string name, Texture main, Func<string, Texture> T, Func<string, float, float> Fl, Func<string, Color, Color> C, string matDir)
        {
            bool adjust = Fl("_MainColorAdjustToggle", 0) > 0.5f &&
                          (Mathf.Abs(Fl("_Saturation", 0)) > 1e-3f || Mathf.Abs(Fl("_MainBrightness", 0)) > 1e-3f ||
                           (Fl("_MainHueShiftToggle", 0) > 0.5f && Mathf.Abs(Fl("_MainHueShift", 0)) > 1e-3f));

            string[] suffixes = { "", "1", "2", "3" };
            List<string> decals = suffixes.Where(s => Fl("_DecalEnabled" + s, 0) > 0.5f && T("_DecalTexture" + s) != null &&
                                                      C("_DecalColor" + s, Color.white).a * Fl("_DecalBlendAlpha" + s, 1) > 1e-3f).ToList();

            if (!adjust && decals.Count == 0) return null;

            int size = Mathf.Clamp(new[] { main }.Concat(decals.Select(s => T("_DecalTexture" + s))).Where(t => t != null).Max(t => Mathf.Max(t.width, t.height)), 64, 2048);
            Texture2D baseTex = main != null ? VrmToAvatarConverter.ReadableCopy(main, size) : null;
            Dictionary<string, Texture2D> decalTex = decals.ToDictionary(s => s, s => VrmToAvatarConverter.ReadableCopy(T("_DecalTexture" + s), size));

            float sat = Fl("_Saturation", 0), bright = Fl("_MainBrightness", 0);
            float hue = Fl("_MainHueShiftToggle", 0) > 0.5f ? Fl("_MainHueShift", 0) : 0;

            Texture2D result = new Texture2D(size, size, TextureFormat.RGBA32, true);
            Color[] px = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    Color c = baseTex != null ? baseTex.GetPixelBilinear(u, v) : Color.white;

                    if (adjust)
                    {
                        if (Mathf.Abs(hue) > 1e-3f)
                        {
                            Color.RGBToHSV(c, out float h, out float s, out float val);
                            Color shifted = Color.HSVToRGB(Mathf.Repeat(h + hue, 1), s, val);
                            c = new Color(shifted.r, shifted.g, shifted.b, c.a);
                        }

                        float lum = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                        float k = 1 + sat;
                        c = new Color(Mathf.Lerp(lum, c.r, k), Mathf.Lerp(lum, c.g, k), Mathf.Lerp(lum, c.b, k), c.a);
                        c = new Color(c.r * (1 + bright), c.g * (1 + bright), c.b * (1 + bright), c.a);
                    }

                    foreach (string s in decals)
                    {
                        Color pos = C("_DecalPosition" + s, new Color(0.5f, 0.5f, 0, 0));
                        Color scl = C("_DecalScale" + s, new Color(1, 1, 0, 0));
                        float rot = -Fl("_DecalRotation" + s, 0) * Mathf.Deg2Rad;
                        Vector2 d = new Vector2(u - pos.r, v - pos.g);
                        d = new Vector2(d.x * Mathf.Cos(rot) - d.y * Mathf.Sin(rot), d.x * Mathf.Sin(rot) + d.y * Mathf.Cos(rot));
                        d = new Vector2(d.x / Mathf.Max(scl.r, 1e-4f) + 0.5f, d.y / Mathf.Max(scl.g, 1e-4f) + 0.5f);
                        if (d.x < 0 || d.x > 1 || d.y < 0 || d.y > 1) continue;

                        Color tint = C("_DecalColor" + s, Color.white);
                        Color dc = decalTex[s].GetPixelBilinear(d.x, d.y);
                        Color dRgb = new Color(dc.r * tint.r, dc.g * tint.g, dc.b * tint.b);
                        float a = dc.a * tint.a * Fl("_DecalBlendAlpha" + s, 1);

                        Color blended;
                        switch ((int)Fl("_DecalBlendType" + s, 0))
                        {
                            case 2: blended = new Color(c.r * dRgb.r, c.g * dRgb.g, c.b * dRgb.b); break;                       // multiply
                            case 4: blended = new Color(1 - (1 - c.r) * (1 - dRgb.r), 1 - (1 - c.g) * (1 - dRgb.g), 1 - (1 - c.b) * (1 - dRgb.b)); break; // screen
                            case 6: blended = new Color(c.r + dRgb.r, c.g + dRgb.g, c.b + dRgb.b); break;                       // add
                            default: blended = dRgb; break;                                                                      // replace
                        }

                        c = new Color(Mathf.Lerp(c.r, blended.r, a), Mathf.Lerp(c.g, blended.g, a), Mathf.Lerp(c.b, blended.b, a), c.a);
                    }

                    px[y * size + x] = c;
                }
            }

            result.SetPixels(px);
            result.Apply();

            string path = AssetDatabase.GenerateUniqueAssetPath($"{matDir}/{VrmToAvatarConverter.MakeSafe(name)}_baked.png");
            File.WriteAllBytes(path, result.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Dictionary<string, SerializedProperty> ReadPropertyMap(SerializedObject so, string path)
        {
            Dictionary<string, SerializedProperty> map = new Dictionary<string, SerializedProperty>();
            SerializedProperty arr = so.FindProperty(path);
            if (arr == null) return map;
            for (int i = 0; i < arr.arraySize; i++)
            {
                SerializedProperty e = arr.GetArrayElementAtIndex(i);
                string key = e.FindPropertyRelative("first")?.stringValue;
                if (key != null) map[key] = e.FindPropertyRelative("second");
            }
            return map;
        }
    }
}
