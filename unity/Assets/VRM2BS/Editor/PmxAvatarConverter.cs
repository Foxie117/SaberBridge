using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CustomAvatar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UniVRM10;

namespace VRM2BS
{
    /// <summary>
    /// MMD model (.pmx) -> Beat Saber Custom Avatars .avatar, built directly in Unity (no Blender, no third-party importer).
    /// Optional extra .pmx files (outfits made for the same skeleton) are merged onto the body by bone name.
    ///   Unity.exe -batchmode -quit -projectPath ... -executeMethod VRM2BS.PmxAvatarConverter.Run -outDir "C:\out"
    ///             -jobs "Avatar Name|C:\a\body.pmx|C:\b\outfit.pmx;Other Name|C:\c\body.pmx"
    /// </summary>
    public static class PmxAvatarConverter
    {
        private const float MmdToMeters = 0.08f;
        private const string ImportRoot = "Assets/PMX_Import";

        // ---------------------------------------------------------------- PMX data

        private class PmxVertex
        {
            public Vector3 position, normal;
            public Vector2 uv;
            public int[] bones = new int[4];
            public float[] weights = new float[4];
        }

        private class PmxMaterial
        {
            public string name;
            public Color diffuse, edgeColor;
            public int flags;
            public float edgeSize;
            public string texture;
            public int indexCount;
        }

        private class PmxBone
        {
            public string name;
            public Vector3 position;
            public int parent;
        }

        private class PmxModel
        {
            public string name, directory;
            public List<PmxVertex> vertices = new List<PmxVertex>();
            public int[] indices;
            public List<PmxMaterial> materials = new List<PmxMaterial>();
            public List<PmxBone> bones = new List<PmxBone>();
        }

        private class Reader
        {
            private readonly BinaryReader _r;
            public int encoding, addUv, vertexSize, textureSize, materialSize, boneSize;

            public Reader(byte[] data) { _r = new BinaryReader(new MemoryStream(data)); }

            public float F() => _r.ReadSingle();
            public int I() => _r.ReadInt32();
            public byte B() => _r.ReadByte();
            public ushort U16() => _r.ReadUInt16();
            public Vector3 V3() => new Vector3(F(), F(), F());
            public Vector2 V2() => new Vector2(F(), F());
            public Color C4() => new Color(F(), F(), F(), F());
            public void Skip(int n) => _r.BaseStream.Seek(n, SeekOrigin.Current);

            public string Text()
            {
                int n = I();
                byte[] b = _r.ReadBytes(n);
                return encoding == 0 ? Encoding.Unicode.GetString(b) : Encoding.UTF8.GetString(b);
            }

            public int Index(int size) => size == 1 ? (sbyte)B() : size == 2 ? _r.ReadInt16() : I();
            public int VertexIndex() => vertexSize == 1 ? B() : vertexSize == 2 ? U16() : I();
        }

        private static PmxModel ReadPmx(string path)
        {
            Reader r = new Reader(File.ReadAllBytes(path));
            if (Encoding.ASCII.GetString(new[] { r.B(), r.B(), r.B(), r.B() }) != "PMX ") throw new Exception("not a PMX file");
            r.F();
            int globals = r.B();
            byte[] g = new byte[Math.Max(globals, 8)];
            for (int i = 0; i < globals; i++) g[i] = r.B();
            r.encoding = g[0]; r.addUv = g[1]; r.vertexSize = g[2]; r.textureSize = g[3]; r.materialSize = g[4]; r.boneSize = g[5];

            PmxModel m = new PmxModel { directory = Path.GetDirectoryName(path) };
            m.name = r.Text(); r.Text(); r.Text(); r.Text();

            int vertexCount = r.I();
            for (int i = 0; i < vertexCount; i++)
            {
                PmxVertex v = new PmxVertex { position = r.V3(), normal = r.V3(), uv = r.V2() };
                r.Skip(16 * r.addUv);
                int type = r.B();
                switch (type)
                {
                    case 0:
                        v.bones[0] = r.Index(r.boneSize); v.weights[0] = 1;
                        break;
                    case 1:
                    case 3:
                        v.bones[0] = r.Index(r.boneSize); v.bones[1] = r.Index(r.boneSize);
                        v.weights[0] = r.F(); v.weights[1] = 1 - v.weights[0];
                        if (type == 3) r.Skip(36); // SDEF C, R0, R1: treated as linear blend
                        break;
                    default:
                        for (int k = 0; k < 4; k++) v.bones[k] = r.Index(r.boneSize);
                        for (int k = 0; k < 4; k++) v.weights[k] = r.F();
                        break;
                }
                r.F(); // edge scale
                m.vertices.Add(v);
            }

            int indexCount = r.I();
            m.indices = new int[indexCount];
            for (int i = 0; i < indexCount; i++) m.indices[i] = r.VertexIndex();

            List<string> textures = new List<string>();
            int textureCount = r.I();
            for (int i = 0; i < textureCount; i++) textures.Add(r.Text().Replace('\\', '/'));

            int materialCount = r.I();
            for (int i = 0; i < materialCount; i++)
            {
                PmxMaterial mat = new PmxMaterial { name = r.Text() };
                r.Text();
                mat.diffuse = r.C4();
                r.Skip(12 + 4 + 12); // specular, specular power, ambient
                mat.flags = r.B();
                mat.edgeColor = r.C4();
                mat.edgeSize = r.F();
                int tex = r.Index(r.textureSize);
                r.Index(r.textureSize); // sphere
                r.B();                  // sphere mode
                if (r.B() == 0) r.Index(r.textureSize); else r.B(); // toon
                r.Text();
                mat.indexCount = r.I();
                mat.texture = tex >= 0 && tex < textures.Count ? textures[tex] : null;
                m.materials.Add(mat);
            }

            int boneCount = r.I();
            for (int i = 0; i < boneCount; i++)
            {
                PmxBone b = new PmxBone { name = r.Text() };
                r.Text();
                b.position = r.V3();
                b.parent = r.Index(r.boneSize);
                r.I();
                int flags = r.U16();
                if ((flags & 0x0001) != 0) r.Index(r.boneSize); else r.Skip(12);
                if ((flags & 0x0300) != 0) { r.Index(r.boneSize); r.F(); }
                if ((flags & 0x0400) != 0) r.Skip(12);
                if ((flags & 0x0800) != 0) r.Skip(24);
                if ((flags & 0x2000) != 0) r.I();
                if ((flags & 0x0020) != 0)
                {
                    r.Index(r.boneSize); r.I(); r.F();
                    int links = r.I();
                    for (int k = 0; k < links; k++)
                    {
                        r.Index(r.boneSize);
                        if (r.B() != 0) r.Skip(24);
                    }
                }
                m.bones.Add(b);
            }

            return m;
        }

        // MMD (left-handed, Y up, facing -Z, 8 cm units) -> Unity (facing +Z, metres)
        private static Vector3 ToUnity(Vector3 p) => new Vector3(-p.x, p.y, -p.z) * MmdToMeters;
        private static Vector3 ToUnityDir(Vector3 n) => new Vector3(-n.x, n.y, -n.z);

        // ---------------------------------------------------------------- humanoid mapping

        private static string Canonical(string name)
        {
            // "左腕" / "腕_L" / "腕.L" -> "腕_L"
            name = name.Trim();
            if (name.StartsWith("左")) return name.Substring(1) + "_L";
            if (name.StartsWith("右")) return name.Substring(1) + "_R";
            return name.Replace(".L", "_L").Replace(".R", "_R");
        }

        private static readonly (string mmd, HumanBodyBones human)[] Map =
        {
            ("下半身", HumanBodyBones.Hips), ("上半身", HumanBodyBones.Spine), ("上半身２", HumanBodyBones.Chest),
            ("首", HumanBodyBones.Neck), ("頭", HumanBodyBones.Head),
            ("目_L", HumanBodyBones.LeftEye), ("目_R", HumanBodyBones.RightEye),
            ("肩_L", HumanBodyBones.LeftShoulder), ("腕_L", HumanBodyBones.LeftUpperArm), ("ひじ_L", HumanBodyBones.LeftLowerArm), ("手首_L", HumanBodyBones.LeftHand),
            ("肩_R", HumanBodyBones.RightShoulder), ("腕_R", HumanBodyBones.RightUpperArm), ("ひじ_R", HumanBodyBones.RightLowerArm), ("手首_R", HumanBodyBones.RightHand),
            ("足D_L", HumanBodyBones.LeftUpperLeg), ("ひざD_L", HumanBodyBones.LeftLowerLeg), ("足首D_L", HumanBodyBones.LeftFoot), ("足先EX_L", HumanBodyBones.LeftToes),
            ("足D_R", HumanBodyBones.RightUpperLeg), ("ひざD_R", HumanBodyBones.RightLowerLeg), ("足首D_R", HumanBodyBones.RightFoot), ("足先EX_R", HumanBodyBones.RightToes),
            ("足_L", HumanBodyBones.LeftUpperLeg), ("ひざ_L", HumanBodyBones.LeftLowerLeg), ("足首_L", HumanBodyBones.LeftFoot), ("つま先_L", HumanBodyBones.LeftToes),
            ("足_R", HumanBodyBones.RightUpperLeg), ("ひざ_R", HumanBodyBones.RightLowerLeg), ("足首_R", HumanBodyBones.RightFoot), ("つま先_R", HumanBodyBones.RightToes),
            ("親指０_L", HumanBodyBones.LeftThumbProximal), ("親指１_L", HumanBodyBones.LeftThumbIntermediate), ("親指２_L", HumanBodyBones.LeftThumbDistal),
            ("人指１_L", HumanBodyBones.LeftIndexProximal), ("人指２_L", HumanBodyBones.LeftIndexIntermediate), ("人指３_L", HumanBodyBones.LeftIndexDistal),
            ("中指１_L", HumanBodyBones.LeftMiddleProximal), ("中指２_L", HumanBodyBones.LeftMiddleIntermediate), ("中指３_L", HumanBodyBones.LeftMiddleDistal),
            ("薬指１_L", HumanBodyBones.LeftRingProximal), ("薬指２_L", HumanBodyBones.LeftRingIntermediate), ("薬指３_L", HumanBodyBones.LeftRingDistal),
            ("小指１_L", HumanBodyBones.LeftLittleProximal), ("小指２_L", HumanBodyBones.LeftLittleIntermediate), ("小指３_L", HumanBodyBones.LeftLittleDistal),
            ("親指０_R", HumanBodyBones.RightThumbProximal), ("親指１_R", HumanBodyBones.RightThumbIntermediate), ("親指２_R", HumanBodyBones.RightThumbDistal),
            ("人指１_R", HumanBodyBones.RightIndexProximal), ("人指２_R", HumanBodyBones.RightIndexIntermediate), ("人指３_R", HumanBodyBones.RightIndexDistal),
            ("中指１_R", HumanBodyBones.RightMiddleProximal), ("中指２_R", HumanBodyBones.RightMiddleIntermediate), ("中指３_R", HumanBodyBones.RightMiddleDistal),
            ("薬指１_R", HumanBodyBones.RightRingProximal), ("薬指２_R", HumanBodyBones.RightRingIntermediate), ("薬指３_R", HumanBodyBones.RightRingDistal),
            ("小指１_R", HumanBodyBones.RightLittleProximal), ("小指２_R", HumanBodyBones.RightLittleIntermediate), ("小指３_R", HumanBodyBones.RightLittleDistal),
        };

        // ---------------------------------------------------------------- entry

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string outDir = VrmToAvatarConverter.GetArg(args, "-outDir");
            string jobs = VrmToAvatarConverter.GetArg(args, "-jobs");
            string jobsFile = VrmToAvatarConverter.GetArg(args, "-jobsFile");
            // a jobs file has one job per line, so its paths may contain ";"
            IEnumerable<string> jobList = jobsFile != null ? File.ReadAllLines(jobsFile).Where(l => !l.StartsWith("#")) : jobs.Split(';');
            Directory.CreateDirectory(outDir);

            VrmToAvatarConverter._template = VrmToAvatarConverter.MeasureTemplate();
            HashSet<string> usedNames = new HashSet<string>(Directory.GetFiles(outDir, "*.avatar").Select(Path.GetFileNameWithoutExtension), StringComparer.OrdinalIgnoreCase);

            foreach (string job in jobList.Where(j => j.Trim().Length > 0))
            {
                string[] parts = job.Split('|').Select(p => p.Trim()).ToArray();
                try
                {
                    ConvertJob(parts[0], parts.Skip(1).ToArray(), outDir, usedNames);
                }
                catch (Exception ex)
                {
                    VrmToAvatarConverter.Log($"FAIL {parts[0]}: {ex}");
                }
            }

            File.AppendAllLines(Path.Combine(outDir, "conversion_report.txt"), VrmToAvatarConverter.Report);
            Debug.Log("[VRM2BS] REPORT\n" + string.Join("\n", VrmToAvatarConverter.Report));
        }

        private static void ConvertJob(string avatarName, string[] pmxFiles, string outDir, HashSet<string> usedNames)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            List<string> notes = new List<string>();

            string safeId = VrmToAvatarConverter.MakeSafe("pmx_" + avatarName);
            string workDir = $"{VrmToAvatarConverter.WorkRoot}/{safeId}";
            if (!AssetDatabase.IsValidFolder(VrmToAvatarConverter.WorkRoot)) AssetDatabase.CreateFolder("Assets", "VRM2BS_Work");
            if (AssetDatabase.IsValidFolder(workDir)) AssetDatabase.DeleteAsset(workDir);
            AssetDatabase.CreateFolder(VrmToAvatarConverter.WorkRoot, safeId);

            VrmToAvatarConverter.Begin(avatarName);
            VrmToAvatarConverter.Stage("import");
            List<PmxModel> models = pmxFiles.Select(ReadPmx).ToList();
            PmxModel body = models[0];

            VrmToAvatarConverter.Stage("skeleton");
            // ---- skeleton from the body; MMD keeps upper and lower body as siblings, Unity wants the spine under the hips
            GameObject model = new GameObject("Model");
            Transform armature = new GameObject("Armature").transform;
            armature.SetParent(model.transform, false);

            Transform[] bones = new Transform[body.bones.Count];
            for (int i = 0; i < body.bones.Count; i++) bones[i] = new GameObject(body.bones[i].name).transform;

            Dictionary<string, int> byCanonical = new Dictionary<string, int>();
            for (int i = 0; i < body.bones.Count; i++)
            {
                string c = Canonical(body.bones[i].name);
                if (!byCanonical.ContainsKey(c)) byCanonical[c] = i;
            }

            int[] parents = body.bones.Select(b => b.parent).ToArray();
            if (byCanonical.TryGetValue("上半身", out int upper) && byCanonical.TryGetValue("下半身", out int lower) && parents[upper] == parents[lower])
            {
                parents[upper] = lower;
            }

            // place every bone first, then parent with worldPositionStays: PMX may list a parent after its child
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].SetParent(armature, false);
                bones[i].SetPositionAndRotation(ToUnity(body.bones[i].position), Quaternion.identity);
            }

            for (int i = 0; i < bones.Length; i++)
            {
                if (parents[i] >= 0 && parents[i] < bones.Length && parents[i] != i) bones[i].SetParent(bones[parents[i]], true);
            }

            Matrix4x4[] bindposes = bones.Select(b => b.worldToLocalMatrix * model.transform.localToWorldMatrix).ToArray();

            VrmToAvatarConverter.Stage("materials");
            // ---- meshes: one renderer per material so head-only parts can be hidden in first person
            int materialCount = 0;
            foreach (PmxModel pmx in models)
            {
                int[] boneRemap = RemapBones(pmx, body, byCanonical);
                int start = 0;

                foreach (PmxMaterial mat in pmx.materials)
                {
                    int[] tri = pmx.indices.Skip(start).Take(mat.indexCount).ToArray();
                    start += mat.indexCount;
                    if (tri.Length == 0) continue;

                    Material material = MakeMaterial(pmx, mat, workDir, notes);
                    BuildRenderer(model.transform, pmx, mat.name, tri, boneRemap, bones, bindposes, material, workDir);
                    materialCount++;
                }
            }

            // ---- humanoid avatar
            Avatar avatar = BuildAvatar(model, bones, byCanonical, notes);
            AssetDatabase.CreateAsset(avatar, $"{workDir}/avatar.asset");
            Animator animator = model.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (!avatar.isValid || !avatar.isHuman) throw new Exception("humanoid avatar could not be built from the MMD skeleton");

            GameObject root = new GameObject(avatarName);
            model.transform.SetParent(root.transform, false);

            HashSet<Transform> humanoid = new HashSet<Transform>(Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>()
                .Where(b => b != HumanBodyBones.LastBone).Select(animator.GetBoneTransform).Where(t => t != null));
            VrmToAvatarConverter.Stage("physics");
            int tails = AddTailPhysics(model.transform, humanoid);
            if (tails > 0) notes.Add($"{tails} tail(s) got physics");

            GameObject[] firstPerson = VrmToAvatarConverter.CollectFirstPersonExclusions(model.transform, animator, new List<RendererFirstPersonFlags>());

            // eye position: between the eye bones, otherwise a bit in front of the head
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform le = animator.GetBoneTransform(HumanBodyBones.LeftEye), re = animator.GetBoneTransform(HumanBodyBones.RightEye);
            Vector3 eye = le && re ? (le.position + re.position) * 0.5f : head.position + new Vector3(0, 0.06f, 0.08f);
            VrmToAvatarConverter.BuildTrackers(root.transform, animator, eye - head.position);

            VrmToAvatarConverter.Stage("ik");
            VRIKManager vrik = model.AddComponent<VRIKManager>();
            VrmToAvatarConverter.FillVrikReferences(vrik, model.transform, animator);
            if (!vrik.areReferencesFilled) notes.Add("VRIK references incomplete");

            if (!VrmToAvatarConverter.SetupFingerPoses(model, animator)) notes.Add("no finger bones");

            AvatarDescriptor descriptor = root.AddComponent<AvatarDescriptor>();
            descriptor.name = avatarName;
            descriptor.author = body.name;
            descriptor.cover = VrmToAvatarConverter.MakeCover(null, root, animator, workDir);

            new GameObject("Body").transform.SetParent(root.transform, false);
            if (firstPerson.Length > 0) root.AddComponent<FirstPersonExclusion>().exclude = firstPerson;

            PrefabUtility.SaveAsPrefabAsset(root, $"{workDir}/{safeId}_avatar.prefab");
            string fileName = VrmToAvatarConverter.UniqueName(VrmToAvatarConverter.MakeSafe(avatarName), usedNames) + ".avatar";
            VrmToAvatarConverter.ExportBundle(root, Path.Combine(outDir, fileName));
            UnityEngine.Object.DestroyImmediate(root);

            float height = (eye.y) / 0.93f;
            VrmToAvatarConverter.Log($"OK   {avatarName} ({string.Join(" + ", pmxFiles.Select(Path.GetFileName))}) -> {fileName}  " +
                                     $"({materialCount} materials, about {height:0.00} m tall, {firstPerson.Length} first-person exclusions)" +
                                     (notes.Count > 0 ? "\n       notes: " + string.Join("; ", notes.Distinct()) : ""));
        }

        private static int[] RemapBones(PmxModel pmx, PmxModel body, Dictionary<string, int> bodyByCanonical)
        {
            int[] remap = new int[pmx.bones.Count];
            for (int i = 0; i < pmx.bones.Count; i++)
            {
                // walk up the outfit's own hierarchy until a bone with the same name exists on the body
                int b = i;
                remap[i] = 0;
                while (b >= 0 && b < pmx.bones.Count)
                {
                    if (bodyByCanonical.TryGetValue(Canonical(pmx.bones[b].name), out int target)) { remap[i] = target; break; }
                    b = pmx.bones[b].parent;
                }
            }
            return remap;
        }

        private static void BuildRenderer(Transform parent, PmxModel pmx, string name, int[] tri, int[] boneRemap, Transform[] bones, Matrix4x4[] bindposes, Material material, string workDir)
        {
            Dictionary<int, int> local = new Dictionary<int, int>();
            List<Vector3> pos = new List<Vector3>(), nrm = new List<Vector3>();
            List<Vector2> uv = new List<Vector2>();
            List<BoneWeight> weights = new List<BoneWeight>();

            int Add(int src)
            {
                if (local.TryGetValue(src, out int i)) return i;
                PmxVertex v = pmx.vertices[src];
                pos.Add(ToUnity(v.position));
                nrm.Add(ToUnityDir(v.normal).normalized);
                uv.Add(new Vector2(v.uv.x, 1 - v.uv.y));

                (int bone, float w)[] infl = Enumerable.Range(0, 4)
                    .Where(k => v.weights[k] > 0 && v.bones[k] >= 0 && v.bones[k] < boneRemap.Length)
                    .Select(k => (boneRemap[v.bones[k]], v.weights[k]))
                    .GroupBy(x => x.Item1).Select(gr => (gr.Key, gr.Sum(x => x.Item2)))
                    .OrderByDescending(x => x.Item2).Take(4).ToArray();
                float sum = Mathf.Max(infl.Sum(x => x.w), 1e-6f);
                BoneWeight bw = new BoneWeight();
                if (infl.Length > 0) { bw.boneIndex0 = infl[0].bone; bw.weight0 = infl[0].w / sum; }
                if (infl.Length > 1) { bw.boneIndex1 = infl[1].bone; bw.weight1 = infl[1].w / sum; }
                if (infl.Length > 2) { bw.boneIndex2 = infl[2].bone; bw.weight2 = infl[2].w / sum; }
                if (infl.Length > 3) { bw.boneIndex3 = infl[3].bone; bw.weight3 = infl[3].w / sum; }
                weights.Add(bw);

                local[src] = pos.Count - 1;
                return pos.Count - 1;
            }

            int[] triangles = tri.Select(Add).ToArray();

            Mesh mesh = new Mesh { name = name };
            if (pos.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = bindposes;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath($"{workDir}/{VrmToAvatarConverter.MakeSafe(name)}.asset"));

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            SkinnedMeshRenderer smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bones[0];
            smr.sharedMaterial = material;
            smr.updateWhenOffscreen = true;
        }

        private static Material MakeMaterial(PmxModel pmx, PmxMaterial m, string workDir, List<string> notes)
        {
            Material mat = new Material(Shader.Find(VrmToAvatarConverter.ShaderName)) { name = m.name };
            Texture2D tex = null;

            if (!string.IsNullOrEmpty(m.texture))
            {
                tex = ImportTexture(Path.Combine(pmx.directory, m.texture), workDir);
                if (tex == null) notes.Add($"texture {m.texture} missing");
            }

            Color c = new Color(m.diffuse.r, m.diffuse.g, m.diffuse.b, m.diffuse.a);
            mat.SetTexture("_MainTex", tex);
            mat.SetTexture("_ShadeTex", tex);
            mat.SetColor("_Color", c);
            mat.SetColor("_ShadeColor", new Color(c.r * 0.8f, c.g * 0.74f, c.b * 0.82f, c.a));
            mat.SetFloat("_ShadingToonyFactor", 0.9f);
            mat.SetFloat("_M_CullMode", (m.flags & 0x01) != 0 ? 0 : 2);

            if ((m.flags & 0x10) != 0 && m.edgeSize > 0)
            {
                mat.SetFloat("_OutlineWidthMode", 1);
                mat.SetFloat("_OutlineWidth", 0.0012f * m.edgeSize);
                mat.SetColor("_OutlineColor", m.edgeColor);
                mat.SetFloat("_OutlineLightingMix", 0);
                mat.EnableKeyword("_MTOON_OUTLINE_WORLD");
            }

            bool texAlpha = tex != null && ((TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex))).DoesSourceTextureHaveAlpha();
            if (c.a < 0.99f)
            {
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.SetFloat("_M_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_M_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_M_ZWrite", 0);
                mat.renderQueue = 3000;
            }
            else if (texAlpha)
            {
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cutoff", 0.5f);
                mat.renderQueue = 2450;
            }
            else
            {
                mat.renderQueue = 2000;
            }

            AssetDatabase.CreateAsset(mat, AssetDatabase.GenerateUniqueAssetPath($"{workDir}/{VrmToAvatarConverter.MakeSafe(m.name)}.mat"));
            return mat;
        }

        private static Texture2D ImportTexture(string file, string workDir)
        {
            if (!File.Exists(file)) return null;
            string texDir = $"{workDir}/Textures";
            if (!AssetDatabase.IsValidFolder(texDir)) AssetDatabase.CreateFolder(workDir, "Textures");
            string dst = $"{texDir}/{Path.GetFileName(file)}";
            if (!File.Exists(dst))
            {
                File.Copy(file, dst);
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
        }

        private static Avatar BuildAvatar(GameObject model, Transform[] bones, Dictionary<string, int> byCanonical, List<string> notes)
        {
            List<HumanBone> human = new List<HumanBone>();
            HashSet<HumanBodyBones> used = new HashSet<HumanBodyBones>();

            foreach ((string mmd, HumanBodyBones hb) in Map)
            {
                if (used.Contains(hb) || !byCanonical.TryGetValue(mmd, out int i)) continue;
                used.Add(hb);
                human.Add(new HumanBone { boneName = bones[i].name, humanName = HumanTrait.BoneName[(int)hb], limit = new HumanLimit { useDefaultValues = true } });
            }

            List<SkeletonBone> skeleton = model.GetComponentsInChildren<Transform>(true)
                .Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToList();

            HumanDescription desc = new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton.ToArray(),
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0, hasTranslationDoF = false,
            };

            Avatar avatar = AvatarBuilder.BuildHumanAvatar(model, desc);
            avatar.name = "Avatar";
            notes.Add($"{human.Count} humanoid bones mapped");
            return avatar;
        }

        private static int AddTailPhysics(Transform modelRoot, HashSet<Transform> humanoid)
        {
            int added = 0;
            foreach (Transform t in modelRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!VrmToAvatarConverter.TailName.IsMatch(t.name) || humanoid.Contains(t) || t.childCount == 0) continue;
                if (t.parent != null && VrmToAvatarConverter.TailName.IsMatch(t.parent.name)) continue;

                DynamicBone db = t.gameObject.AddComponent<DynamicBone>();
                db.m_Root = t;
                db.m_UpdateRate = 60;
                db.m_Damping = 0.2f;
                db.m_Elasticity = 0.08f;
                db.m_Stiffness = 0.15f;
                db.m_Gravity = new Vector3(0, -0.003f, 0);
                db.m_EndLength = 1f; // MMD tails end at the last bone; give the tip room to swing
                db.m_Exclusions = new List<Transform>();
                added++;
            }
            return added;
        }
    }
}
