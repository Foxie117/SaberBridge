using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CustomAvatar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VRM;

namespace VRM2BS
{
    /// <summary>
    /// Turns PC Custom Avatars files (.avatar, made by anyone) into VRM 0.x for VRM Qavatars on the Quest.
    /// VRM Qavatars reads only VRM 0.x and puts every material on MToon, so materials become MToon here.
    ///   Unity.exe -quit -projectPath ... -executeMethod VRM2BS.AvatarToVrm.Run -avatarList "list.txt" -outDir "C:\out"
    /// Log markers: "[A2V] BEGIN file", "[A2V] OK file -> out.vrm | notes", "[A2V] FAIL file: reason".
    /// </summary>
    public static class AvatarToVrm
    {
        // Custom Avatars' tracker targets sit right under the avatar root; they are not part of the body
        private static readonly string[] TrackerNames = { "Head", "LeftHand", "RightHand", "Pelvis", "LeftLeg", "RightLeg", "Body" };
        // VRM Qavatars keeps every texture as uncompressed RGBA in the Quest's memory (2048² = 16 MB each)
        private const int MaxTexture = 1024;

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string outDir = VrmToAvatarConverter.GetArg(args, "-outDir");
            Directory.CreateDirectory(outDir);
            foreach (string file in File.ReadAllLines(VrmToAvatarConverter.GetArg(args, "-avatarList")).Where(l => l.Trim().Length > 0))
            {
                // the app reads "[VRM2BS] BEGIN name", "[VRM2BS] OK name -> path", "[VRM2BS] FAIL name: reason"
                string name = Path.GetFileName(file);
                Debug.Log("[VRM2BS] BEGIN " + name);
                try
                {
                    List<string> notes = new List<string>();
                    string output = ConvertOne(file, outDir, notes);
                    Debug.Log($"[VRM2BS] NOTE {name}: {string.Join("; ", notes)}");
                    Debug.Log($"[VRM2BS] OK {name} -> {output}");
                }
                catch (Exception e)
                {
                    Debug.Log($"[VRM2BS] FAIL {name}: {e.Message}");
                    Debug.LogException(e);
                }
            }
        }

        private static string ConvertOne(string file, string outDir, List<string> notes)
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(file);
            if (bundle == null) throw new Exception("not an asset bundle (or made for another Unity version)");
            GameObject root = null;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                GameObject prefab = bundle.LoadAsset<GameObject>("_CustomAvatar");
                if (prefab == null) throw new Exception("no _CustomAvatar inside");
                root = UnityEngine.Object.Instantiate(prefab);
                root.name = "_CustomAvatar";
                root.transform.position = Vector3.zero;
                root.transform.rotation = Quaternion.identity;
                AvatarDescriptor descriptor = root.GetComponent<AvatarDescriptor>();
                string title = descriptor != null && !string.IsNullOrWhiteSpace(descriptor.name) ? descriptor.name : Path.GetFileNameWithoutExtension(file);
                string author = descriptor != null && !string.IsNullOrWhiteSpace(descriptor.author) ? descriptor.author : "";
                Texture2D thumbnail = descriptor != null && descriptor.cover != null ? VrmToAvatarConverter.ReadableCopy(descriptor.cover.texture, 512) : null;

                Dictionary<HumanBodyBones, Transform> bones = HumanBones(root, notes);
                notes.Add($"head at {bones[HumanBodyBones.Head].position.y:0.00} m");
                RemoveTrackers(root, bones);
                List<(object bone, Transform owner)> dynamicBones = CollectDynamicBones(root);
                MakeMeshesReadable(root, notes);
                ConvertMaterials(root, notes);
                // Custom Avatars resizes every avatar to the player in game, so some authors never fixed the size (a 16 m tall model);
                // a VRM keeps its size, so an absurd one is brought to an ordinary height
                float headY = bones[HumanBodyBones.Head].position.y;
                float resize = headY < 0.3f || headY > 3f ? 1.5f / Mathf.Max(headY, 1e-4f) : 1f;
                if (resize != 1f) notes.Add($"resized x{resize:0.###} (head was at {headY:0.00} m)");
                Dictionary<Transform, Matrix4x4> before = Normalize(root, resize);
                ConvertPhysics(root, dynamicBones, before, notes);
                RemoveScripts(root);
                Unique(root, bones);
                BuildHumanoid(root, bones);
                AddBlendShapes(root, notes);
                AddFirstPerson(root, bones);

                VRMMetaObject meta = ScriptableObject.CreateInstance<VRMMetaObject>();
                meta.Title = title;
                meta.Author = author.Length > 0 ? author : "unknown";
                meta.Version = "converted by SaberBridge";
                meta.Reference = Path.GetFileName(file);
                meta.Thumbnail = thumbnail;

                VRMExportSettings settings = ScriptableObject.CreateInstance<VRMExportSettings>();
                // Normalize already did what PoseFreeze does, and it copes with scaled armatures (Blender's 100x) where PoseFreeze does not
                settings.PoseFreeze = false;
                settings.ForceTPose = false;
                settings.UseRuntimeTextureSerializer = true;
                settings.KeepVertexColor = false;
                // only blend shapes used by the VRM expressions matter to VRM Qavatars; the rest can be most of the file (90 MB -> a few)
                settings.ReduceBlendshape = true;
                byte[] bytes = VRMEditorExporter.Export(root, meta, settings);

                string output = Path.Combine(outDir, VrmToAvatarConverter.MakeSafe(Path.GetFileNameWithoutExtension(file)) + ".vrm");
                File.WriteAllBytes(output, bytes);
                notes.Add($"{bytes.Length / 1024} KB");
                return output;
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                bundle.Unload(true);
            }
        }

        // ------------------------------------------------------------------ skeleton

        /// <summary>The humanoid bones: from a humanoid Animator (any .avatar built from a humanoid model), else from VRIK's references.</summary>
        private static Dictionary<HumanBodyBones, Transform> HumanBones(GameObject root, List<string> notes)
        {
            Dictionary<HumanBodyBones, Transform> map = new Dictionary<HumanBodyBones, Transform>();
            Animator animator = root.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.avatar != null && a.isHuman);
            if (animator != null)
            {
                foreach (HumanBodyBones b in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (b == HumanBodyBones.LastBone) continue;
                    Transform t = animator.GetBoneTransform(b);
                    if (t != null) map[b] = t;
                }
            }

            if (!map.ContainsKey(HumanBodyBones.Hips))
            {
                VRIKManager vrik = root.GetComponentInChildren<VRIKManager>(true);
                if (vrik == null) throw new Exception("no humanoid skeleton (no humanoid Animator and no VRIK)");
                void Add(HumanBodyBones b, Transform t) { if (t != null) map[b] = t; }
                Add(HumanBodyBones.Hips, vrik.references_pelvis);
                Add(HumanBodyBones.Spine, vrik.references_spine);
                Add(HumanBodyBones.Chest, vrik.references_chest);
                Add(HumanBodyBones.Neck, vrik.references_neck);
                Add(HumanBodyBones.Head, vrik.references_head);
                Add(HumanBodyBones.LeftShoulder, vrik.references_leftShoulder);
                Add(HumanBodyBones.LeftUpperArm, vrik.references_leftUpperArm);
                Add(HumanBodyBones.LeftLowerArm, vrik.references_leftForearm);
                Add(HumanBodyBones.LeftHand, vrik.references_leftHand);
                Add(HumanBodyBones.RightShoulder, vrik.references_rightShoulder);
                Add(HumanBodyBones.RightUpperArm, vrik.references_rightUpperArm);
                Add(HumanBodyBones.RightLowerArm, vrik.references_rightForearm);
                Add(HumanBodyBones.RightHand, vrik.references_rightHand);
                Add(HumanBodyBones.LeftUpperLeg, vrik.references_leftThigh);
                Add(HumanBodyBones.LeftLowerLeg, vrik.references_leftCalf);
                Add(HumanBodyBones.LeftFoot, vrik.references_leftFoot);
                Add(HumanBodyBones.LeftToes, vrik.references_leftToes);
                Add(HumanBodyBones.RightUpperLeg, vrik.references_rightThigh);
                Add(HumanBodyBones.RightLowerLeg, vrik.references_rightCalf);
                Add(HumanBodyBones.RightFoot, vrik.references_rightFoot);
                Add(HumanBodyBones.RightToes, vrik.references_rightToes);
                notes.Add("skeleton from VRIK");
            }

            HumanBodyBones[] required =
            {
                HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Head,
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
            };
            HumanBodyBones[] missing = required.Where(b => !map.ContainsKey(b)).ToArray();
            if (missing.Length > 0) throw new Exception("the skeleton lacks " + string.Join(", ", missing));
            return map;
        }

        private static void RemoveTrackers(GameObject root, Dictionary<HumanBodyBones, Transform> bones)
        {
            HashSet<Transform> keep = new HashSet<Transform>(bones.Values.SelectMany(b => b.GetComponentsInParent<Transform>(true)));
            foreach (Transform child in root.transform.Cast<Transform>().ToArray())
            {
                if (!TrackerNames.Contains(child.name) || keep.Contains(child)) continue;
                if (child.GetComponentsInChildren<Renderer>(true).Length > 0) continue;
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }

            // other Animators would fight the one built for the export
            foreach (Animator a in root.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(a);
        }

        /// <summary>BuildHumanAvatar and the glTF writer both need unique names; bone names stay as they are where possible.</summary>
        private static void Unique(GameObject root, Dictionary<HumanBodyBones, Transform> bones)
        {
            HashSet<Transform> human = new HashSet<Transform>(bones.Values);
            HashSet<string> used = new HashSet<string>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true).OrderBy(t => human.Contains(t) ? 0 : 1))
            {
                string name = t.name;
                for (int i = 2; used.Contains(name); i++) name = t.name + "_" + i;
                t.name = name;
                used.Add(name);
            }
        }

        private static void BuildHumanoid(GameObject root, Dictionary<HumanBodyBones, Transform> bones)
        {
            HumanDescription d = new HumanDescription
            {
                human = bones.Select(p => new HumanBone
                {
                    boneName = p.Value.name,
                    humanName = HumanTrait.BoneName[(int)p.Key],
                    limit = new HumanLimit { useDefaultValues = true },
                }).ToArray(),
                skeleton = root.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale,
                }).ToArray(),
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0,
                hasTranslationDoF = false,
            };
            Avatar avatar = AvatarBuilder.BuildHumanAvatar(root, d);
            if (avatar == null || !avatar.isValid || !avatar.isHuman) throw new Exception("could not build a humanoid avatar from the skeleton");
            avatar.name = root.name + "Avatar";
            Animator animator = root.AddComponent<Animator>();
            animator.avatar = avatar;
        }

        // ------------------------------------------------------------------ normalising

        /// <summary>
        /// VRM 0.x wants every node unrotated and unscaled. Each vertex (and blend shape, normal, tangent) is moved to where
        /// its bones put it now, the bones keep their world positions with no rotation or scale, and the bind poses become
        /// plain offsets. Returns the old world matrices for the physics colliders.
        /// </summary>
        private static Dictionary<Transform, Matrix4x4> Normalize(GameObject root, float resize)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            Matrix4x4 size = Matrix4x4.Scale(Vector3.one * resize);
            Dictionary<Transform, Matrix4x4> before = all.ToDictionary(t => t, t => size * t.localToWorldMatrix);

            List<(SkinnedMeshRenderer smr, Mesh mesh)> skinned = new List<(SkinnedMeshRenderer, Mesh)>();
            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                Mesh m = smr.sharedMesh;
                Transform[] bones = smr.bones;
                Matrix4x4[] bind = m.bindposes;
                Matrix4x4 own = before[smr.transform];
                Matrix4x4[] skin = new Matrix4x4[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                    skin[i] = bones[i] != null && before.ContainsKey(bones[i]) && i < bind.Length ? before[bones[i]] * bind[i] : own;
                BoneWeight[] weights = m.boneWeights;
                bool hasWeights = weights.Length == m.vertexCount && skin.Length > 0;
                Matrix4x4[] perVertex = new Matrix4x4[m.vertexCount];
                for (int v = 0; v < perVertex.Length; v++)
                    perVertex[v] = hasWeights ? Blend(skin, weights[v]) : own;
                skinned.Add((smr, Bake(m, perVertex)));
            }

            List<(MeshFilter filter, Mesh mesh)> rigid = new List<(MeshFilter, Mesh)>();
            foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                Matrix4x4 own = before[f.transform];
                rigid.Add((f, Bake(f.sharedMesh, Enumerable.Repeat(own, f.sharedMesh.vertexCount).ToArray())));
            }

            // parents come before children, so each node is placed after its parent lost its rotation and scale
            foreach (Transform t in all)
            {
                Vector3 p = before[t].GetColumn(3);
                t.localScale = Vector3.one;
                t.SetPositionAndRotation(t == root.transform ? Vector3.zero : p, Quaternion.identity);
            }

            foreach ((SkinnedMeshRenderer smr, Mesh mesh) in skinned)
            {
                Shift(mesh, -smr.transform.position);
                Transform[] bones = smr.bones;
                mesh.bindposes = bones.Select(b => b != null ? Matrix4x4.Translate(smr.transform.position - b.position) : Matrix4x4.identity).ToArray();
                mesh.RecalculateBounds();
                smr.sharedMesh = mesh;
                smr.localBounds = mesh.bounds;
            }

            foreach ((MeshFilter filter, Mesh mesh) in rigid)
            {
                Shift(mesh, -filter.transform.position);
                mesh.RecalculateBounds();
                filter.sharedMesh = mesh;
            }

            return before;
        }

        private static Matrix4x4 Blend(Matrix4x4[] skin, BoneWeight w)
        {
            Matrix4x4 r = Matrix4x4.zero;
            void Add(int i, float k)
            {
                if (k <= 0 || i < 0 || i >= skin.Length) return;
                Matrix4x4 m = skin[i];
                for (int e = 0; e < 16; e++) r[e] += m[e] * k;
            }

            Add(w.boneIndex0, w.weight0);
            Add(w.boneIndex1, w.weight1);
            Add(w.boneIndex2, w.weight2);
            Add(w.boneIndex3, w.weight3);
            float sum = w.weight0 + w.weight1 + w.weight2 + w.weight3;
            if (sum <= 1e-6f) return skin.Length > 0 ? skin[0] : Matrix4x4.identity;
            if (Mathf.Abs(sum - 1) > 1e-4f)
                for (int e = 0; e < 16; e++) r[e] /= sum;
            return r;
        }

        /// <summary>A copy of the mesh in world space: vertices, normals, tangents and blend shapes through each vertex's matrix.</summary>
        private static Mesh Bake(Mesh src, Matrix4x4[] perVertex)
        {
            Mesh m = UnityEngine.Object.Instantiate(src);
            m.name = src.name;
            Vector3[] v = src.vertices, n = src.normals;
            Vector4[] t = src.tangents;
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = perVertex[i].MultiplyPoint3x4(v[i]);
                if (n.Length == v.Length) n[i] = perVertex[i].MultiplyVector(n[i]).normalized;
                if (t.Length == v.Length)
                {
                    Vector3 d = perVertex[i].MultiplyVector(new Vector3(t[i].x, t[i].y, t[i].z)).normalized;
                    t[i] = new Vector4(d.x, d.y, d.z, t[i].w);
                }
            }

            m.vertices = v;
            if (n.Length == v.Length) m.normals = n;
            if (t.Length == v.Length) m.tangents = t;

            if (src.blendShapeCount > 0)
            {
                m.ClearBlendShapes();
                Vector3[] dv = new Vector3[v.Length], dn = new Vector3[v.Length], dt = new Vector3[v.Length];
                for (int s = 0; s < src.blendShapeCount; s++)
                {
                    for (int f = 0; f < src.GetBlendShapeFrameCount(s); f++)
                    {
                        src.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                        for (int i = 0; i < v.Length; i++)
                        {
                            dv[i] = perVertex[i].MultiplyVector(dv[i]);
                            dn[i] = perVertex[i].MultiplyVector(dn[i]);
                            dt[i] = perVertex[i].MultiplyVector(dt[i]);
                        }

                        m.AddBlendShapeFrame(src.GetBlendShapeName(s), src.GetBlendShapeFrameWeight(s, f), dv, dn, dt);
                    }
                }
            }

            // a mirrored transform (negative scale) turns faces inside out
            if (perVertex.Length > 0 && perVertex[0].determinant < 0)
                for (int sub = 0; sub < m.subMeshCount; sub++)
                    m.SetTriangles(m.GetTriangles(sub).Reverse().ToArray(), sub);
            return m;
        }

        private static void Shift(Mesh m, Vector3 by)
        {
            Vector3[] v = m.vertices;
            for (int i = 0; i < v.Length; i++) v[i] += by;
            m.vertices = v;
        }

        // ------------------------------------------------------------------ scripts and physics

        private static List<(object bone, Transform owner)> CollectDynamicBones(GameObject root)
        {
            return root.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(m => m != null && m.GetType().Name == "DynamicBone")
                .Select(m => ((object)m, m.transform)).ToList();
        }

        /// <summary>DynamicBone chains become VRM spring bones (the closest thing VRM Qavatars simulates).</summary>
        private static void ConvertPhysics(GameObject root, List<(object bone, Transform owner)> dynamicBones, Dictionary<Transform, Matrix4x4> before, List<string> notes)
        {
            if (dynamicBones.Count == 0) return;
            GameObject secondary = new GameObject("secondary");
            secondary.transform.SetParent(root.transform, false);
            Dictionary<object, VRMSpringBoneColliderGroup> groups = new Dictionary<object, VRMSpringBoneColliderGroup>();
            int chains = 0;
            foreach ((object db, Transform _) in dynamicBones)
            {
                List<Transform> roots = new List<Transform>();
                if (Field(db, "m_Root") is Transform r) roots.Add(r);
                if (Field(db, "m_Roots") is IEnumerable many) roots.AddRange(many.OfType<Transform>());
                roots = roots.Where(t => t != null).Distinct().ToList();
                if (roots.Count == 0) continue;

                float elasticity = Num(db, "m_Elasticity", 0.1f), stiffness = Num(db, "m_Stiffness", 0.1f), damping = Num(db, "m_Damping", 0.1f);
                Vector3 gravity = Vec(db, "m_Gravity") + Vec(db, "m_Force");
                VRMSpringBone spring = secondary.AddComponent<VRMSpringBone>();
                spring.m_comment = (roots[0].name + " (DynamicBone)");
                spring.RootBones = roots;
                // DynamicBone pulls back with elasticity and keeps shape with stiffness; VRM has one "stiffness" force
                spring.m_stiffnessForce = Mathf.Clamp(elasticity * 4f + stiffness * 2f, 0.2f, 4f);
                spring.m_dragForce = Mathf.Clamp01(damping + 0.2f);
                spring.m_gravityPower = Mathf.Clamp(gravity.magnitude * 10f, 0, 2f);
                spring.m_gravityDir = gravity.sqrMagnitude > 1e-6f ? gravity.normalized : Vector3.down;
                spring.m_hitRadius = Mathf.Max(0.005f, Num(db, "m_Radius", 0.02f) * Scale(before, roots[0]));

                List<VRMSpringBoneColliderGroup> used = new List<VRMSpringBoneColliderGroup>();
                if (Field(db, "m_Colliders") is IEnumerable colliders)
                {
                    foreach (object c in colliders)
                    {
                        if (c == null || !(c is Component comp) || comp == null) continue;
                        if (!groups.TryGetValue(c, out VRMSpringBoneColliderGroup g))
                        {
                            g = comp.gameObject.AddComponent<VRMSpringBoneColliderGroup>();
                            // DynamicBone sizes are in the collider's own (scaled, rotated) space; the bones are now plain
                            Matrix4x4 was = before.TryGetValue(comp.transform, out Matrix4x4 w) ? w : comp.transform.localToWorldMatrix;
                            float k = Scale(before, comp.transform);
                            float radius = Num(c, "m_Radius", 0.05f) * k;
                            float height = Num(c, "m_Height", 0) * k;
                            Vector3 center = was.MultiplyPoint3x4(Vec(c, "m_Center")) - comp.transform.position;
                            List<VRMSpringBoneColliderGroup.SphereCollider> spheres = new List<VRMSpringBoneColliderGroup.SphereCollider>
                            {
                                new VRMSpringBoneColliderGroup.SphereCollider { Offset = center, Radius = radius },
                            };
                            // a capsule becomes a sphere at each end
                            if (height > radius * 2)
                            {
                                int dir = (int)Num(c, "m_Direction", 1);
                                Vector3 axis = was.MultiplyVector(dir == 0 ? Vector3.right : dir == 2 ? Vector3.forward : Vector3.up).normalized;
                                float half = height / 2 - radius;
                                spheres[0].Offset = center + axis * half;
                                spheres.Add(new VRMSpringBoneColliderGroup.SphereCollider { Offset = center - axis * half, Radius = radius });
                            }

                            g.Colliders = spheres.ToArray();
                            groups[c] = g;
                        }

                        used.Add(g);
                    }
                }

                spring.ColliderGroups = used.ToArray();
                chains++;
            }

            notes.Add($"{chains} physics chains");
        }

        private static float Scale(Dictionary<Transform, Matrix4x4> before, Transform t)
        {
            Vector3 s = before.TryGetValue(t, out Matrix4x4 m) ? m.lossyScale : t.lossyScale;
            return Mathf.Abs((s.x + s.y + s.z) / 3);
        }

        private static object Field(object o, string name)
        {
            FieldInfo f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f?.GetValue(o);
        }

        private static float Num(object o, string name, float fallback) => Field(o, name) switch
        {
            float f => f,
            int i => i,
            Enum e => Convert.ToInt32(e),
            _ => fallback,
        };

        private static Vector3 Vec(object o, string name) => Field(o, name) is Vector3 v ? v : Vector3.zero;

        /// <summary>Everything that is not VRM goes: Custom Avatars, FinalIK, DynamicBone, the author's own scripts.</summary>
        private static void RemoveScripts(GameObject root)
        {
            // a few passes, because some components require others
            for (int pass = 0; pass < 4; pass++)
            {
                foreach (Component c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform || c is Renderer || c is MeshFilter) continue;
                    if (c is VRMSpringBone || c is VRMSpringBoneColliderGroup) continue;
                    if (c is MonoBehaviour || c is Collider || c is Rigidbody || c is Joint || c is Cloth || c is AudioSource || c is Light || c is Camera || c is ParticleSystem)
                        UnityEngine.Object.DestroyImmediate(c);
                }
            }

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

            // particle effects cannot be exported to VRM
            foreach (ParticleSystemRenderer p in root.GetComponentsInChildren<ParticleSystemRenderer>(true)) UnityEngine.Object.DestroyImmediate(p);
        }

        // ------------------------------------------------------------------ meshes

        private static void MakeMeshesReadable(GameObject root, List<string> notes)
        {
            int unreadable = 0;
            foreach (SkinnedMeshRenderer s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (s.sharedMesh == null || s.sharedMesh.isReadable) continue;
                unreadable++;
            }

            foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null || f.sharedMesh.isReadable) continue;
                unreadable++;
            }

            if (unreadable > 0) throw new Exception($"the author saved {unreadable} meshes without Read/Write, so they cannot be read back yet");
        }

        // ------------------------------------------------------------------ materials

        private static readonly string[] MainTextureNames = { "_MainTex", "_BaseMap", "_BaseColorMap", "_Albedo", "_Diffuse", "_Tex" };
        private static readonly string[] ColorNames = { "_Color", "_BaseColor", "_MainColor", "_TintColor", "_Tint" };

        private static readonly string[] MToonFloats =
        {
            "_Cutoff", "_BumpScale", "_ReceiveShadowRate", "_ShadingGradeRate", "_ShadeShift", "_ShadeToony", "_LightColorAttenuation",
            "_IndirectLightIntensity", "_RimLightingMix", "_RimFresnelPower", "_RimLift", "_OutlineWidth", "_OutlineScaledMaxDistance",
            "_OutlineLightingMix", "_OutlineWidthMode", "_OutlineColorMode", "_CullMode",
        };

        private static readonly string[] MToonColors = { "_ShadeColor", "_RimColor", "_EmissionColor", "_OutlineColor" };
        private static readonly string[] MToonTextures = { "_ShadeTexture", "_BumpMap", "_EmissionMap", "_SphereAdd", "_RimTexture", "_OutlineWidthTexture", "_ShadingGradeTexture", "_ReceiveShadowTexture" };

        private static void ConvertMaterials(GameObject root, List<string> notes)
        {
            Dictionary<Material, Material> done = new Dictionary<Material, Material>();
            Dictionary<Texture, Texture2D> textures = new Dictionary<Texture, Texture2D>();
            Texture2D Tex(Texture t)
            {
                if (t == null) return null;
                if (!textures.TryGetValue(t, out Texture2D c))
                {
                    c = VrmToAvatarConverter.ReadableCopy(t, MaxTexture);
                    c.name = t.name;
                    textures[t] = c;
                }

                return c;
            }

            int replaced = 0;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                r.sharedMaterials = r.sharedMaterials.Select(m =>
                {
                    if (m == null) return null;
                    if (done.TryGetValue(m, out Material c)) return c;
                    c = ToMToon(m, Tex);
                    done[m] = c;
                    replaced++;
                    return c;
                }).ToArray();
            }

            notes.Add($"{replaced} materials");
        }

        private static Material ToMToon(Material src, Func<Texture, Texture2D> tex)
        {
            Material m = new Material(Shader.Find(MToon.Utils.ShaderName)) { name = src.name };
            string shader = src.shader != null ? src.shader.name : "";
            bool isMToon = shader.IndexOf("MToon", StringComparison.OrdinalIgnoreCase) >= 0;

            Texture main = MainTextureNames.Where(src.HasProperty).Select(src.GetTexture).FirstOrDefault(t => t != null);
            Color color = ColorNames.Where(src.HasProperty).Select(src.GetColor).DefaultIfEmpty(Color.white).First();
            m.SetTexture("_MainTex", tex(main));
            if (main != null && src.HasProperty("_MainTex"))
            {
                m.SetTextureScale("_MainTex", src.GetTextureScale("_MainTex"));
                m.SetTextureOffset("_MainTex", src.GetTextureOffset("_MainTex"));
            }

            m.SetColor("_Color", color);

            if (isMToon)
            {
                // our own avatars (and VRM-born ones) already carry MToon settings
                foreach (string p in MToonFloats.Where(src.HasProperty)) m.SetFloat(p, src.GetFloat(p));
                foreach (string p in MToonColors.Where(src.HasProperty)) m.SetColor(p, src.GetColor(p));
                foreach (string p in MToonTextures.Where(src.HasProperty)) m.SetTexture(p, tex(src.GetTexture(p)));
                if (src.HasProperty("_BlendMode")) m.SetFloat("_BlendMode", src.GetFloat("_BlendMode"));
            }
            else
            {
                // a lit look close to the PC one: the same colour in light, a little darker in shade, soft edge
                m.SetColor("_ShadeColor", new Color(color.r * 0.72f, color.g * 0.72f, color.b * 0.78f, 1));
                if (main != null) m.SetTexture("_ShadeTexture", tex(main));
                m.SetFloat("_ShadeToony", 0.85f);
                m.SetFloat("_ShadeShift", -0.1f);
                m.SetFloat("_OutlineWidthMode", 0);
                if (src.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", src.GetColor("_EmissionColor") * (src.IsKeywordEnabled("_EMISSION") || shader.Contains("Glow") ? 1 : 0));
                if (src.HasProperty("_EmissionMap") && src.GetTexture("_EmissionMap") != null) m.SetTexture("_EmissionMap", tex(src.GetTexture("_EmissionMap")));
                if (src.HasProperty("_BumpMap") && src.GetTexture("_BumpMap") != null) m.SetTexture("_BumpMap", tex(src.GetTexture("_BumpMap")));
                if (src.HasProperty("_Cull")) m.SetFloat("_CullMode", src.GetFloat("_Cull"));
                if (src.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", src.GetFloat("_Cutoff"));
                m.SetFloat("_BlendMode", (float)GuessRenderMode(src, shader));
            }

            MToon.Utils.ValidateProperties(m, true);
            return m;
        }

        private static MToon.RenderMode GuessRenderMode(Material src, string shader)
        {
            string s = shader.ToLowerInvariant();
            string tag = src.GetTag("RenderType", false, "").ToLowerInvariant();
            if (s.Contains("cutout") || tag.Contains("cutout") || src.IsKeywordEnabled("_ALPHATEST_ON")) return MToon.RenderMode.Cutout;
            if (s.Contains("transparent") || tag == "transparent" || src.IsKeywordEnabled("_ALPHABLEND_ON") || src.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") || src.renderQueue >= 2900)
                return MToon.RenderMode.Transparent;
            return MToon.RenderMode.Opaque;
        }

        // ------------------------------------------------------------------ face and first person

        private static readonly (BlendShapePreset preset, string[] names)[] Presets =
        {
            (BlendShapePreset.Blink, new[] { "blink", "vrc.blink", "blink_both", "eyes_closed", "eye_close", "まばたき", "まばたき2" }),
            (BlendShapePreset.Blink_L, new[] { "vrc.blink_left", "blink_l", "blink_left", "ウィンク", "eye_close_l" }),
            (BlendShapePreset.Blink_R, new[] { "vrc.blink_right", "blink_r", "blink_right", "ウィンク右", "eye_close_r" }),
            (BlendShapePreset.A, new[] { "vrc.v_aa", "a", "aa", "mouth_a", "あ" }),
            (BlendShapePreset.I, new[] { "vrc.v_ih", "i", "ih", "mouth_i", "い" }),
            (BlendShapePreset.U, new[] { "vrc.v_ou", "u", "ou", "mouth_u", "う" }),
            (BlendShapePreset.E, new[] { "vrc.v_e", "e", "mouth_e", "え" }),
            (BlendShapePreset.O, new[] { "vrc.v_oh", "o", "oh", "mouth_o", "お" }),
            (BlendShapePreset.Joy, new[] { "joy", "happy", "smile", "笑い" }),
            (BlendShapePreset.Angry, new[] { "angry", "anger", "怒り" }),
            (BlendShapePreset.Sorrow, new[] { "sorrow", "sad", "困る" }),
            (BlendShapePreset.Fun, new[] { "fun" }),
        };

        /// <summary>VRM expressions from blend shapes with the usual names (VRChat visemes, VRM, MMD), plus empty Neutral clips.</summary>
        private static void AddBlendShapes(GameObject root, List<string> notes)
        {
            BlendShapeAvatar avatar = ScriptableObject.CreateInstance<BlendShapeAvatar>();
            avatar.Clips = new List<BlendShapeClip>();
            SkinnedMeshRenderer[] meshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null && s.sharedMesh.blendShapeCount > 0).ToArray();
            int found = 0;
            foreach (BlendShapePreset preset in Enum.GetValues(typeof(BlendShapePreset)))
            {
                if (preset == BlendShapePreset.Unknown) continue;
                BlendShapeClip clip = ScriptableObject.CreateInstance<BlendShapeClip>();
                clip.Preset = preset;
                clip.BlendShapeName = preset.ToString();
                string[] names = Presets.FirstOrDefault(p => p.preset == preset).names ?? Array.Empty<string>();
                List<BlendShapeBinding> values = new List<BlendShapeBinding>();
                foreach (SkinnedMeshRenderer s in meshes)
                {
                    for (int i = 0; i < s.sharedMesh.blendShapeCount; i++)
                    {
                        string n = s.sharedMesh.GetBlendShapeName(i).Trim().ToLowerInvariant();
                        if (!names.Contains(n)) continue;
                        values.Add(new BlendShapeBinding { RelativePath = RelativePath(root.transform, s.transform), Index = i, Weight = 100 });
                        break;
                    }
                }

                clip.Values = values.ToArray();
                if (values.Count > 0) found++;
                avatar.Clips.Add(clip);
            }

            VRMBlendShapeProxy proxy = root.AddComponent<VRMBlendShapeProxy>();
            proxy.BlendShapeAvatar = avatar;
            if (found > 0) notes.Add($"{found} expressions");
        }

        private static string RelativePath(Transform root, Transform t)
        {
            List<string> parts = new List<string>();
            for (Transform x = t; x != null && x != root; x = x.parent) parts.Insert(0, x.name);
            return string.Join("/", parts);
        }

        private static void AddFirstPerson(GameObject root, Dictionary<HumanBodyBones, Transform> bones)
        {
            VRMFirstPerson fp = root.AddComponent<VRMFirstPerson>();
            fp.FirstPersonBone = bones[HumanBodyBones.Head];
            Transform head = bones[HumanBodyBones.Head];
            Transform eyeL = bones.TryGetValue(HumanBodyBones.LeftEye, out Transform l) ? l : null;
            Transform eyeR = bones.TryGetValue(HumanBodyBones.RightEye, out Transform r) ? r : null;
            Vector3 eyes = eyeL != null && eyeR != null ? (eyeL.position + eyeR.position) / 2 : head.position + Vector3.up * 0.06f + Vector3.forward * 0.06f;
            fp.FirstPersonOffset = head.InverseTransformPoint(eyes);
            fp.TraverseRenderers();
        }

        // ------------------------------------------------------------------ check

        /// <summary>
        /// Reads each .vrm in -outDir back the way a VRM 0.x loader does, checks what VRM Qavatars needs
        /// (VRM 0.x extension, one MToon entry per material, humanoid bones) and renders front and side pictures.
        /// </summary>
        public static void Check()
        {
            string dir = VrmToAvatarConverter.GetArg(Environment.GetCommandLineArgs(), "-outDir");
            string shots = Path.Combine(dir, "check");
            Directory.CreateDirectory(shots);
            foreach (string file in Directory.GetFiles(dir, "*.vrm"))
            {
                try
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    UniGLTF.GltfData data = new UniGLTF.GlbFileParser(file).Parse();
                    VRMData vrm = new VRMData(data);
                    glTF_VRM_extensions ext = vrm.VrmExtension;
                    int mats = data.GLTF.materials?.Count ?? 0;
                    int props = ext.materialProperties?.Count ?? 0;
                    int mtoon = ext.materialProperties?.Count(p => p.shader == "VRM/MToon") ?? 0;
                    int human = ext.humanoid?.humanBones?.Count ?? 0;
                    int images = data.GLTF.images?.Count ?? 0;
                    string mimes = string.Join(",", (data.GLTF.images ?? new List<UniGLTF.glTFImage>()).Select(i => i.mimeType).Distinct());
                    using (VRMImporterContext ctx = new VRMImporterContext(vrm))
                    {
                        UniGLTF.RuntimeGltfInstance instance = UniGLTF.ImporterContextExtensions.Load(ctx);
                        instance.ShowMeshes();
                        instance.EnableUpdateWhenOffscreen();
                        Animator a = instance.GetComponent<Animator>();
                        Shot(instance.gameObject, a, Path.Combine(shots, Path.GetFileNameWithoutExtension(file)));
                        Transform head = a != null ? a.GetBoneTransform(HumanBodyBones.Head) : null;
                        Debug.Log($"[A2V] CHECK {Path.GetFileName(file)}: head at {(head != null ? head.position.y : -1):0.00} m, materials {mats}, vrm0 props {props} (MToon {mtoon}), human bones {human}, images {images} ({mimes}), animator {(a != null && a.isHuman ? "human" : "none")}, blendshape groups {ext.blendShapeMaster?.blendShapeGroups?.Count ?? 0}, springs {ext.secondaryAnimation?.boneGroups?.Count ?? 0}");
                        UnityEngine.Object.DestroyImmediate(instance.gameObject);
                    }
                }
                catch (Exception e)
                {
                    Debug.Log($"[A2V] CHECK FAIL {Path.GetFileName(file)}: {e}");
                }
            }
        }

        private static void Shot(GameObject go, Animator a, string basePath)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>();
            Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(Vector3.up, Vector3.one);
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            float h = Mathf.Clamp(b.max.y, 0.3f, 3f);
            Camera cam = new GameObject("cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.58f, 0.62f);
            cam.fieldOfView = 30;
            Light light = new GameObject("light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(30, 150, 0);
            RenderTexture rt = new RenderTexture(512, 768, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            float dist = h * 0.55f / Mathf.Tan(15 * Mathf.Deg2Rad) * 1.1f;
            Vector3 target = new Vector3(b.center.x, h / 2, b.center.z);
            foreach ((string name, Vector3 dirToCam) in new[] { ("front", Vector3.forward), ("side", Vector3.right) })
            {
                cam.transform.position = target + dirToCam * dist;
                cam.transform.LookAt(target);
                cam.Render();
                RenderTexture.active = rt;
                Texture2D t = new Texture2D(512, 768, TextureFormat.RGB24, false);
                t.ReadPixels(new Rect(0, 0, 512, 768), 0, 0);
                RenderTexture.active = null;
                File.WriteAllBytes($"{basePath}_{name}.png", t.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(t);
            }

            UnityEngine.Object.DestroyImmediate(cam.gameObject);
            UnityEngine.Object.DestroyImmediate(light.gameObject);
            rt.Release();
        }

        // ------------------------------------------------------------------ probe

        public static void Probe()
        {
            string[] args = Environment.GetCommandLineArgs();
            foreach (string file in File.ReadAllLines(VrmToAvatarConverter.GetArg(args, "-avatarList")).Where(l => l.Trim().Length > 0))
            {
                AssetBundle bundle = AssetBundle.LoadFromFile(file);
                if (bundle == null)
                {
                    Debug.Log($"[A2V] PROBE {Path.GetFileName(file)}: no bundle");
                    continue;
                }

                try
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    GameObject root = UnityEngine.Object.Instantiate(bundle.LoadAsset<GameObject>("_CustomAvatar"));
                    List<string> lines = new List<string>();
                    Animator an = root.GetComponentsInChildren<Animator>(true).FirstOrDefault(x => x.isHuman);
                    if (an != null)
                    {
                        List<Transform> chain = new List<Transform>();
                        for (Transform x = an.GetBoneTransform(HumanBodyBones.Head); x != null; x = x.parent) chain.Insert(0, x);
                        foreach (Transform x in chain) lines.Add($"  chain {x.name}: pos {x.localPosition} rot {x.localRotation.eulerAngles} scale {x.localScale}");
                        lines.Add($"  animator on {an.name}");
                    }

                    foreach (SkinnedMeshRenderer s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        lines.Add($"  smr {s.name} parent {s.transform.parent?.name} scale {s.transform.lossyScale} rootBone {s.rootBone?.name}");
                    foreach (SkinnedMeshRenderer s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        if (s.sharedMesh != null) lines.Add($"  smr {s.name}: readable={s.sharedMesh.isReadable} v={s.sharedMesh.vertexCount} weights={s.sharedMesh.HasVertexAttribute(VertexAttribute.BlendWeight)} shapes={s.sharedMesh.blendShapeCount}");
                    foreach (Material mat in root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(x => x != null).Distinct())
                        lines.Add($"  mat {mat.name}: {mat.shader?.name} q{mat.renderQueue}");
                    Debug.Log($"[A2V] PROBE {Path.GetFileName(file)}\n" + string.Join("\n", lines));
                    UnityEngine.Object.DestroyImmediate(root);
                }
                finally
                {
                    bundle.Unload(true);
                }
            }
        }
    }
}
