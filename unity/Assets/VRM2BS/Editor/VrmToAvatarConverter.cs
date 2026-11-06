using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomAvatar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UniVRM10;
using UniGLTF.Extensions.VRMC_vrm;

namespace VRM2BS
{
    /// <summary>
    /// Batch converter: VRM (0.x or 1.0) -> Beat Saber Custom Avatars .avatar.
    /// Run from the command line:
    ///   Unity.exe -batchmode -quit -projectPath ... -executeMethod VRM2BS.VrmToAvatarConverter.Run
    ///             -vrmDir "C:\in" -outDir "C:\out" [-only "a.vrm;b.vrm"] [-keepMToon]
    /// </summary>
    public static class VrmToAvatarConverter
    {
        internal const string WorkRoot = "Assets/VRM2BS_Work";
        private const string TemplateScene = "Assets/TemplateFullBody/TemplateFullBody.unity";
        private const string BundlePrefabPath = "Assets/_CustomAvatar.prefab";
        internal const string ShaderName = "VRM2BS/MToon";

        internal struct TrackerOffsets
        {
            public float height;
            public Vector3 headFromHeadBone;
            public Quaternion headRotation;
            public Vector3 leftHandFromBone, rightHandFromBone;
            public float handLength;
            public Quaternion leftHandRotation, rightHandRotation;
            public Vector3 leftArmDir, rightArmDir;
            public Vector3 pelvisFromHips;
            public Quaternion pelvisRotation;
            public Vector3 leftLegFromFoot, rightLegFromFoot;
            public Quaternion leftLegRotation, rightLegRotation;
        }

        internal static TrackerOffsets _template;
        internal static readonly List<string> Report = new List<string>();

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string vrmDir = GetArg(args, "-vrmDir");
            string outDir = GetArg(args, "-outDir");
            string only = GetArg(args, "-only");
            bool keepMToon = args.Contains("-keepMToon");

            if (string.IsNullOrEmpty(vrmDir) || string.IsNullOrEmpty(outDir))
            {
                throw new ArgumentException("-vrmDir and -outDir are required");
            }

            Directory.CreateDirectory(outDir);

            IEnumerable<string> files = Directory.GetFiles(vrmDir, "*.vrm");

            if (!string.IsNullOrEmpty(only))
            {
                HashSet<string> wanted = new HashSet<string>(only.Split(';').Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
                files = files.Where(f => wanted.Contains(Path.GetFileName(f)));
            }

            _template = MeasureTemplate();

            HashSet<string> seenHashes = new HashSet<string>();
            // names already in the output folder stay taken, so a second run never overwrites them
            HashSet<string> usedNames = new HashSet<string>(Directory.GetFiles(outDir, "*.avatar").Select(Path.GetFileNameWithoutExtension), StringComparer.OrdinalIgnoreCase);

            foreach (string file in files.OrderBy(f => f))
            {
                string hash = FileHash(file);

                if (!seenHashes.Add(hash))
                {
                    Log($"SKIP {Path.GetFileName(file)}: identical copy of a file already converted");
                    continue;
                }

                try
                {
                    ConvertOne(file, outDir, usedNames, keepMToon);
                }
                catch (Exception ex)
                {
                    seenHashes.Remove(hash); // let an identical copy try again
                    Log($"FAIL {Path.GetFileName(file)}: {ex}");
                }
            }

            File.WriteAllLines(Path.Combine(outDir, "conversion_report.txt"), Report);
            Debug.Log("[VRM2BS] REPORT\n" + string.Join("\n", Report));
            AvatarPreview.RenderBuiltIfRequested();
        }

        // ---------------------------------------------------------------- template

        internal static TrackerOffsets MeasureTemplate()
        {
            EditorSceneManager.OpenScene(TemplateScene, OpenSceneMode.Single);

            AvatarDescriptor descriptor = UnityEngine.Object.FindObjectOfType<AvatarDescriptor>();
            Transform root = descriptor.transform;
            Animator animator = root.GetComponentInChildren<Animator>();

            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

            TrackerOffsets t = new TrackerOffsets
            {
                height = head.position.y,
                handLength = HandLength(animator, true),
            };

            Transform tHead = root.Find("Head");
            Transform tLeftHand = root.Find("LeftHand");
            Transform tRightHand = root.Find("RightHand");
            Transform tPelvis = root.Find("Pelvis");
            Transform tLeftLeg = root.Find("LeftLeg");
            Transform tRightLeg = root.Find("RightLeg");

            t.headFromHeadBone = tHead.position - head.position;
            t.headRotation = tHead.rotation;
            t.leftHandFromBone = tLeftHand.position - leftHand.position;
            t.rightHandFromBone = tRightHand.position - rightHand.position;
            t.leftHandRotation = tLeftHand.rotation;
            t.rightHandRotation = tRightHand.rotation;
            t.leftArmDir = ArmDir(animator, true);
            t.rightArmDir = ArmDir(animator, false);
            t.pelvisFromHips = tPelvis.position - hips.position;
            t.pelvisRotation = tPelvis.rotation;
            t.leftLegFromFoot = tLeftLeg.position - leftFoot.position;
            t.rightLegFromFoot = tRightLeg.position - rightFoot.position;
            t.leftLegRotation = tLeftLeg.rotation;
            t.rightLegRotation = tRightLeg.rotation;

            Debug.Log($"[VRM2BS] template: head height {t.height:0.000}, hand length {t.handLength:0.000}, " +
                      $"hand offset L {t.leftHandFromBone} R {t.rightHandFromBone}");

            return t;
        }

        internal static Vector3 ArmDir(Animator animator, bool left)
        {
            Transform hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform forearm = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            return (hand.position - forearm.position).normalized;
        }

        // shortest rotation from the template's forearm direction to this model's; a T-pose model gets none,
        // so hands that already held the saber right stay exactly as they were
        internal static Quaternion HandTurn(Animator animator, bool left) =>
            Quaternion.FromToRotation(left ? _template.leftArmDir : _template.rightArmDir, ArmDir(animator, left));

        internal static float HandLength(Animator animator, bool left)
        {
            Transform hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform middle = animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            Transform forearm = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);

            if (middle) return Vector3.Distance(hand.position, middle.position);

            // no fingers: estimate from forearm length (hand-to-knuckles is roughly 0.35 of the forearm)
            return Vector3.Distance(hand.position, forearm.position) * 0.35f;
        }

        // ---------------------------------------------------------------- per model

        private static void ConvertOne(string vrmFile, string outDir, HashSet<string> usedNames, bool keepMToon)
        {
            string id = Path.GetFileNameWithoutExtension(vrmFile);
            string safeId = MakeSafe(id);
            string workDir = $"{WorkRoot}/{safeId}";
            string vrmAssetPath = $"{workDir}/{safeId}.vrm";

            Begin(Path.GetFileName(vrmFile));
            Stage("import");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            if (!AssetDatabase.IsValidFolder(WorkRoot)) AssetDatabase.CreateFolder("Assets", "VRM2BS_Work");
            if (!AssetDatabase.IsValidFolder(workDir)) AssetDatabase.CreateFolder(WorkRoot, safeId);

            File.WriteAllBytes(vrmAssetPath, VrmFixer.FixVrm0SpringBoneGroups(File.ReadAllBytes(vrmFile)));
            AssetDatabase.ImportAsset(vrmAssetPath, ImportAssetOptions.ForceUpdate);

            GameObject imported = AssetDatabase.LoadAssetAtPath<GameObject>(vrmAssetPath);

            if (imported == null) throw new Exception("UniVRM could not import this file");

            GameObject model = UnityEngine.Object.Instantiate(imported);
            model.name = "Model";

            Vrm10Instance vrm = model.GetComponent<Vrm10Instance>();
            Animator animator = model.GetComponent<Animator>();

            if (vrm == null || animator == null || !animator.isHuman) throw new Exception("model has no humanoid rig");

            VRM10ObjectMeta meta = vrm.Vrm != null ? vrm.Vrm.Meta : null;
            string avatarName = !string.IsNullOrWhiteSpace(meta?.Name) ? meta.Name.Trim() : id;
            string author = meta != null && meta.Authors != null ? string.Join(", ", meta.Authors.Where(a => !string.IsNullOrWhiteSpace(a))) : "";

            List<string> notes = new List<string>();

            Stage("skeleton");
            GameObject root = new GameObject(avatarName);
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // ---- data that lives on UniVRM components; read before they are removed
            Vector3 lookAtOffset = vrm.Vrm != null ? vrm.Vrm.LookAt.OffsetFromHead : new Vector3(0, 0.06f, 0);
            List<RendererFirstPersonFlags> firstPerson = vrm.Vrm != null ? vrm.Vrm.FirstPerson.Renderers.ToList() : new List<RendererFirstPersonFlags>();
            Texture2D thumbnail = meta?.Thumbnail;

            Stage("physics");
            int dynamicBones = ConvertSpringBones(vrm, animator, notes);
            GameObject[] firstPersonExcluded = CollectFirstPersonExclusions(model.transform, animator, firstPerson);

            RemoveForeignComponents(root);

            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // ---- materials
            Stage("materials");
            int convertedMaterials = keepMToon ? 0 : ConvertMaterials(model, workDir, notes);

            // ---- trackers
            Stage("ik");
            BuildTrackers(root.transform, animator, lookAtOffset);

            // ---- IK
            VRIKManager vrik = model.AddComponent<VRIKManager>();
            FillVrikReferences(vrik, model.transform, animator);

            if (!vrik.areReferencesFilled) notes.Add("VRIK references incomplete (missing humanoid bones)");

            // ---- finger poses
            if (!SetupFingerPoses(model, animator)) notes.Add("no finger bones, finger poses skipped");

            // ---- descriptor
            AvatarDescriptor descriptor = root.AddComponent<AvatarDescriptor>();
            descriptor.name = avatarName;
            descriptor.author = author;
            descriptor.cover = MakeCover(thumbnail, root, animator, workDir);

            new GameObject("Body").transform.SetParent(root.transform, false);

            if (firstPersonExcluded.Length > 0)
            {
                FirstPersonExclusion exclusion = root.AddComponent<FirstPersonExclusion>();
                exclusion.exclude = firstPersonExcluded;
            }

            // ---- keep an editable prefab, then export
            string keepPrefab = $"{workDir}/{safeId}_avatar.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, keepPrefab);

            string fileName = UniqueName(MakeSafe(avatarName), usedNames) + ".avatar";
            ExportBundle(root, Path.Combine(outDir, fileName));

            UnityEngine.Object.DestroyImmediate(root);

            Log($"OK   {Path.GetFileName(vrmFile)} -> {fileName}  (\"{avatarName}\" by {author}; " +
                $"{dynamicBones} dynamic bone chains, {convertedMaterials} materials, " +
                $"{firstPersonExcluded.Length} first-person exclusions)" +
                (notes.Count > 0 ? "\n       notes: " + string.Join("; ", notes) : ""));
        }

        // ---------------------------------------------------------------- trackers & IK

        internal static void BuildTrackers(Transform root, Animator animator, Vector3 lookAtOffset)
        {
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

            float scale = head.position.y / _template.height;
            float handScale = HandLength(animator, true) / _template.handLength;

            // VRM's look-at offset is the eye position relative to the head bone, in model space
            Vector3 eye = head.position + lookAtOffset;
            if (lookAtOffset.sqrMagnitude < 1e-6f) eye = head.position + _template.headFromHeadBone * scale;

            AddTracker(root, "Head", eye, _template.headRotation);
            // the template is in a T-pose; many models rest in an A-pose (arms down and forward), so turn the
            // template's hand tracker with the model's real forearm, or the saber sits crooked in the hand
            Quaternion lTurn = HandTurn(animator, true), rTurn = HandTurn(animator, false);
            AddTracker(root, "LeftHand", leftHand.position + lTurn * _template.leftHandFromBone * handScale, lTurn * _template.leftHandRotation);
            AddTracker(root, "RightHand", rightHand.position + rTurn * _template.rightHandFromBone * handScale, rTurn * _template.rightHandRotation);
            AddTracker(root, "Pelvis", hips.position + _template.pelvisFromHips * scale, _template.pelvisRotation);

            if (leftFoot && rightFoot)
            {
                Vector3 l = leftFoot.position + _template.leftLegFromFoot * scale;
                Vector3 r = rightFoot.position + _template.rightLegFromFoot * scale;
                AddTracker(root, "LeftLeg", new Vector3(l.x, 0, l.z), _template.leftLegRotation);
                AddTracker(root, "RightLeg", new Vector3(r.x, 0, r.z), _template.rightLegRotation);
            }
        }

        internal static void AddTracker(Transform root, string name, Vector3 position, Quaternion rotation)
        {
            Transform t = new GameObject(name).transform;
            t.SetParent(root, false);
            t.SetPositionAndRotation(position, rotation);
        }

        internal static void FillVrikReferences(VRIKManager vrik, Transform modelRoot, Animator a)
        {
            vrik.references_root = modelRoot;
            vrik.references_pelvis = a.GetBoneTransform(HumanBodyBones.Hips);
            vrik.references_spine = a.GetBoneTransform(HumanBodyBones.Spine);
            vrik.references_chest = a.GetBoneTransform(HumanBodyBones.Chest);
            vrik.references_neck = a.GetBoneTransform(HumanBodyBones.Neck);
            vrik.references_head = a.GetBoneTransform(HumanBodyBones.Head);
            vrik.references_leftShoulder = a.GetBoneTransform(HumanBodyBones.LeftShoulder);
            vrik.references_leftUpperArm = a.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            vrik.references_leftForearm = a.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            vrik.references_leftHand = a.GetBoneTransform(HumanBodyBones.LeftHand);
            vrik.references_rightShoulder = a.GetBoneTransform(HumanBodyBones.RightShoulder);
            vrik.references_rightUpperArm = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
            vrik.references_rightForearm = a.GetBoneTransform(HumanBodyBones.RightLowerArm);
            vrik.references_rightHand = a.GetBoneTransform(HumanBodyBones.RightHand);
            vrik.references_leftThigh = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            vrik.references_leftCalf = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            vrik.references_leftFoot = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            vrik.references_leftToes = a.GetBoneTransform(HumanBodyBones.LeftToes);
            vrik.references_rightThigh = a.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            vrik.references_rightCalf = a.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            vrik.references_rightFoot = a.GetBoneTransform(HumanBodyBones.RightFoot);
            vrik.references_rightToes = a.GetBoneTransform(HumanBodyBones.RightToes);
        }

        // ---------------------------------------------------------------- fingers

        internal static bool SetupFingerPoses(GameObject model, Animator animator)
        {
            if (!animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal) && !animator.GetBoneTransform(HumanBodyBones.RightIndexProximal))
            {
                return false;
            }

            PoseManager poseManager = model.AddComponent<PoseManager>();
            poseManager.animator = animator;

            Transform[] all = model.GetComponentsInChildren<Transform>(true);
            Dictionary<Transform, (Vector3, Quaternion, Vector3)> saved = all.ToDictionary(t => t, t => (t.localPosition, t.localRotation, t.localScale));

            HumanPoseHandler handler = new HumanPoseHandler(animator.avatar, model.transform);
            HumanPose pose = new HumanPose();
            handler.GetHumanPose(ref pose);

            SetFingerMuscles(ref pose, 0.15f, -0.1f, 0.3f);   // relaxed open hand
            handler.SetHumanPose(ref pose);
            poseManager.SaveOpenHandPoses();

            SetFingerMuscles(ref pose, -0.95f, -0.6f, 0f);    // fist around the saber handle
            handler.SetHumanPose(ref pose);
            poseManager.SaveClosedHandPoses();

            handler.Dispose();

            foreach (KeyValuePair<Transform, (Vector3, Quaternion, Vector3)> kv in saved)
            {
                kv.Key.localPosition = kv.Value.Item1;
                kv.Key.localRotation = kv.Value.Item2;
                kv.Key.localScale = kv.Value.Item3;
            }

            return true;
        }

        private static void SetFingerMuscles(ref HumanPose pose, float stretch, float thumbStretch, float spread)
        {
            string[] names = HumanTrait.MuscleName;

            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];
                bool finger = n.StartsWith("Left ") || n.StartsWith("Right ");
                if (!finger) continue;

                bool thumb = n.Contains("Thumb");
                bool isFinger = thumb || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little");
                if (!isFinger) continue;

                if (n.EndsWith("Stretched")) pose.muscles[i] = thumb ? thumbStretch : stretch;
                else if (n.EndsWith("Spread")) pose.muscles[i] = thumb ? 0 : spread;
            }
        }

        // ---------------------------------------------------------------- spring bones

        private static int ConvertSpringBones(Vrm10Instance vrm, Animator animator, List<string> notes)
        {
            List<Vrm10InstanceSpringBone.Spring> springs = vrm.SpringBone?.Springs ?? new List<Vrm10InstanceSpringBone.Spring>();

            HashSet<Transform> humanoid = new HashSet<Transform>();
            foreach (HumanBodyBones b in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (b == HumanBodyBones.LastBone) continue;
                Transform t = animator.GetBoneTransform(b);
                if (t) humanoid.Add(t);
            }

            // every simulated transform, minus humanoid bones (those are driven by IK)
            Dictionary<Transform, VRM10SpringBoneJoint> joints = new Dictionary<Transform, VRM10SpringBoneJoint>();
            Dictionary<Transform, List<VRM10SpringBoneColliderGroup>> groupsByJoint = new Dictionary<Transform, List<VRM10SpringBoneColliderGroup>>();
            int skippedHumanoid = 0;

            foreach (Vrm10InstanceSpringBone.Spring spring in springs)
            {
                foreach (VRM10SpringBoneJoint joint in spring.Joints)
                {
                    if (joint == null) continue;
                    if (humanoid.Contains(joint.transform)) { skippedHumanoid++; continue; }

                    joints[joint.transform] = joint;
                    if (!groupsByJoint.TryGetValue(joint.transform, out List<VRM10SpringBoneColliderGroup> list))
                    {
                        groupsByJoint[joint.transform] = list = new List<VRM10SpringBoneColliderGroup>();
                    }

                    list.AddRange(spring.ColliderGroups.Where(g => g != null));
                }
            }

            if (skippedHumanoid > 0) notes.Add($"{skippedHumanoid} spring joints on humanoid bones ignored");

            int autoTails = AddMissingTailPhysics(vrm.transform, humanoid, joints);
            if (autoTails > 0) notes.Add($"{autoTails} tail(s) had no physics in the VRM, added automatically");

            Dictionary<VRM10SpringBoneCollider, List<DynamicBoneColliderBase>> colliderCache = new Dictionary<VRM10SpringBoneCollider, List<DynamicBoneColliderBase>>();
            int chains = 0;

            foreach (Transform chainRoot in joints.Keys.Where(t => t.parent == null || !joints.ContainsKey(t.parent)).ToList())
            {
                List<Transform> members = chainRoot.GetComponentsInChildren<Transform>(true).Where(joints.ContainsKey).ToList();
                List<VRM10SpringBoneJoint> chainJoints = members.Select(t => joints[t]).ToList();

                DynamicBone db = chainRoot.gameObject.AddComponent<DynamicBone>();
                db.m_Root = chainRoot;
                db.m_UpdateRate = 60;

                float stiffness = chainJoints.Average(j => j.m_stiffnessForce);
                float drag = chainJoints.Average(j => j.m_dragForce);
                float gravityPower = chainJoints.Average(j => j.m_gravityPower);
                Vector3 gravityDir = chainJoints[0].m_gravityDir;
                float radius = chainJoints.Average(j => j.m_jointRadius);

                db.m_Damping = Mathf.Clamp(0.05f + 0.4f * drag, 0.05f, 0.6f);
                db.m_Elasticity = Mathf.Clamp(0.03f + 0.05f * stiffness, 0.02f, 0.3f);
                db.m_Stiffness = Mathf.Clamp(0.05f * stiffness, 0f, 0.4f);
                db.m_Inert = 0f;
                db.m_Gravity = gravityDir.normalized * gravityPower * 0.02f;
                float scale = Mathf.Max(chainRoot.lossyScale.x, 1e-4f);
                db.m_Radius = radius / scale;

                // only the transforms VRM simulated may move; everything else under the chain stays rigid
                db.m_Exclusions = new List<Transform>();
                foreach (Transform member in members)
                {
                    foreach (Transform child in member)
                    {
                        if (!joints.ContainsKey(child) && !db.m_Exclusions.Contains(child)) db.m_Exclusions.Add(child);
                    }
                }

                db.m_Colliders = new List<DynamicBoneColliderBase>();
                foreach (VRM10SpringBoneColliderGroup group in members.SelectMany(m => groupsByJoint[m]).Distinct())
                {
                    foreach (VRM10SpringBoneCollider c in group.Colliders)
                    {
                        if (c == null) continue;
                        if (!colliderCache.TryGetValue(c, out List<DynamicBoneColliderBase> made))
                        {
                            colliderCache[c] = made = MakeColliders(c);
                        }

                        foreach (DynamicBoneColliderBase m in made)
                        {
                            if (!db.m_Colliders.Contains(m)) db.m_Colliders.Add(m);
                        }
                    }
                }

                chains++;
            }

            return chains;
        }

        internal static readonly System.Text.RegularExpressions.Regex TailName =
            new System.Text.RegularExpressions.Regex("tail|shippo|しっぽ|尻尾|尾", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static int AddMissingTailPhysics(Transform modelRoot, HashSet<Transform> humanoid, Dictionary<Transform, VRM10SpringBoneJoint> joints)
        {
            HashSet<Transform> skinBones = new HashSet<Transform>(
                modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.bones).Where(b => b != null));

            bool HasPhysicsAround(Transform t)
            {
                for (Transform p = t; p != null; p = p.parent) if (joints.ContainsKey(p)) return true;
                return t.GetComponentsInChildren<Transform>(true).Any(joints.ContainsKey);
            }

            int added = 0;

            foreach (Transform t in modelRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!TailName.IsMatch(t.name) || humanoid.Contains(t) || !skinBones.Contains(t)) continue;
                if (t.parent != null && TailName.IsMatch(t.parent.name) && skinBones.Contains(t.parent)) continue; // not the start of the tail
                if (HasPhysicsAround(t)) continue;

                // needs at least one more skinned bone below it, otherwise there is nothing to swing
                if (!t.GetComponentsInChildren<Transform>(true).Any(c => c != t && skinBones.Contains(c))) continue;

                DynamicBone db = t.gameObject.AddComponent<DynamicBone>();
                db.m_Root = t;
                db.m_UpdateRate = 60;
                db.m_Damping = 0.2f;
                db.m_Elasticity = 0.08f;
                db.m_Stiffness = 0.15f;
                db.m_Inert = 0f;
                db.m_Gravity = new Vector3(0, -0.003f, 0);
                db.m_Exclusions = t.GetComponentsInChildren<Transform>(true).Where(c => c != t && !skinBones.Contains(c) && skinBones.Contains(c.parent)).ToList();
                added++;
            }

            return added;
        }

        internal static List<DynamicBoneColliderBase> MakeColliders(VRM10SpringBoneCollider c)
        {
            List<DynamicBoneColliderBase> result = new List<DynamicBoneColliderBase>();
            GameObject go = new GameObject("DBCollider");
            go.transform.SetParent(c.transform, false);

            switch (c.ColliderType)
            {
                case VRM10SpringBoneColliderTypes.Sphere:
                case VRM10SpringBoneColliderTypes.SphereInside:
                {
                    go.transform.localPosition = c.Offset;
                    DynamicBoneCollider dc = go.AddComponent<DynamicBoneCollider>();
                    dc.m_Radius = c.Radius;
                    dc.m_Height = 0;
                    dc.m_Bound = c.ColliderType == VRM10SpringBoneColliderTypes.SphereInside ? DynamicBoneColliderBase.Bound.Inside : DynamicBoneColliderBase.Bound.Outside;
                    result.Add(dc);
                    break;
                }

                case VRM10SpringBoneColliderTypes.Capsule:
                case VRM10SpringBoneColliderTypes.CapsuleInside:
                {
                    Vector3 axis = c.Tail - c.Offset;
                    go.transform.localPosition = (c.Offset + c.Tail) * 0.5f;
                    go.transform.localRotation = axis.sqrMagnitude > 1e-8f ? Quaternion.FromToRotation(Vector3.up, axis.normalized) : Quaternion.identity;
                    DynamicBoneCollider dc = go.AddComponent<DynamicBoneCollider>();
                    dc.m_Direction = DynamicBoneColliderBase.Direction.Y;
                    dc.m_Radius = c.Radius;
                    dc.m_Height = axis.magnitude + c.Radius * 2;
                    dc.m_Bound = c.ColliderType == VRM10SpringBoneColliderTypes.CapsuleInside ? DynamicBoneColliderBase.Bound.Inside : DynamicBoneColliderBase.Bound.Outside;
                    result.Add(dc);
                    break;
                }

                default:
                    UnityEngine.Object.DestroyImmediate(go);
                    break;
            }

            return result;
        }

        // ---------------------------------------------------------------- first person

        internal static GameObject[] CollectFirstPersonExclusions(Transform modelRoot, Animator animator, List<RendererFirstPersonFlags> flags)
        {
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            HashSet<GameObject> result = new HashSet<GameObject>();
            HashSet<Renderer> flagged = new HashSet<Renderer>();

            foreach (RendererFirstPersonFlags f in flags)
            {
                Renderer r = f.GetRenderer(modelRoot);
                if (r == null) continue;
                flagged.Add(r);

                if (f.FirstPersonFlag == FirstPersonType.thirdPersonOnly ||
                    (f.FirstPersonFlag == FirstPersonType.auto && IsHeadOnly(r, head)))
                {
                    result.Add(r.gameObject);
                }
            }

            foreach (Renderer r in modelRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!flagged.Contains(r) && IsHeadOnly(r, head)) result.Add(r.gameObject);
            }

            return result.ToArray();
        }

        internal static bool IsHeadOnly(Renderer r, Transform head)
        {
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null && smr.bones.Length > 0)
            {
                BoneWeight[] weights = smr.sharedMesh.boneWeights;
                if (weights.Length == 0) return smr.rootBone && smr.rootBone.IsChildOf(head);

                bool[] underHead = smr.bones.Select(b => b != null && b.IsChildOf(head)).ToArray();
                int inHead = weights.Count(w => w.boneIndex0 < underHead.Length && underHead[w.boneIndex0]);
                return inHead >= weights.Length * 0.95f;
            }

            return r.transform.IsChildOf(head);
        }

        // ---------------------------------------------------------------- materials

        private static int ConvertMaterials(GameObject model, string workDir, List<string> notes)
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null) throw new Exception($"shader {ShaderName} not found");

            string matDir = $"{workDir}/Materials";
            if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder(workDir, "Materials");

            Dictionary<Material, Material> map = new Dictionary<Material, Material>();
            HashSet<string> otherShaders = new HashSet<string>();

            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;

                for (int i = 0; i < mats.Length; i++)
                {
                    Material src = mats[i];
                    if (src == null) continue;

                    if (!map.TryGetValue(src, out Material dst))
                    {
                        dst = ConvertMaterial(src, shader, otherShaders);
                        AssetDatabase.CreateAsset(dst, AssetDatabase.GenerateUniqueAssetPath($"{matDir}/{MakeSafe(src.name)}.mat"));
                        map[src] = dst;
                    }

                    mats[i] = dst;
                }

                r.sharedMaterials = mats;
            }

            if (otherShaders.Count > 0) notes.Add("non-MToon shaders approximated: " + string.Join(", ", otherShaders));

            return map.Count;
        }

        private static Material ConvertMaterial(Material src, Shader shader, HashSet<string> otherShaders)
        {
            bool isMToon = src.shader != null && src.shader.name.Contains("MToon");
            Material dst;

            if (isMToon)
            {
                dst = new Material(src) { shader = shader };
                dst.renderQueue = src.renderQueue;
                return dst;
            }

            otherShaders.Add(src.shader != null ? src.shader.name : "<none>");

            dst = new Material(shader) { name = src.name };
            if (src.HasProperty("_MainTex")) dst.SetTexture("_MainTex", src.GetTexture("_MainTex"));
            if (src.HasProperty("_MainTex")) { dst.SetTextureScale("_MainTex", src.GetTextureScale("_MainTex")); dst.SetTextureOffset("_MainTex", src.GetTextureOffset("_MainTex")); }

            Color color = src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
            dst.SetColor("_Color", color);

            bool unlit = src.shader != null && src.shader.name.IndexOf("unlit", StringComparison.OrdinalIgnoreCase) >= 0;
            dst.SetColor("_ShadeColor", unlit ? color : color * 0.75f);
            if (!unlit) dst.SetTexture("_ShadeTex", src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null);

            if (src.HasProperty("_EmissionColor") && src.IsKeywordEnabled("_EMISSION"))
            {
                dst.SetColor("_EmissionColor", src.GetColor("_EmissionColor"));
                if (src.HasProperty("_EmissionMap")) dst.SetTexture("_EmissionMap", src.GetTexture("_EmissionMap"));
            }

            if (src.HasProperty("_BumpMap") && src.GetTexture("_BumpMap") != null)
            {
                dst.SetTexture("_BumpMap", src.GetTexture("_BumpMap"));
                dst.EnableKeyword("_NORMALMAP");
            }

            float cull = src.HasProperty("_CullMode") ? src.GetFloat("_CullMode") : src.HasProperty("_Cull") ? src.GetFloat("_Cull") : 2;
            dst.SetFloat("_M_CullMode", cull);

            int queue = src.renderQueue;
            bool cutout = src.IsKeywordEnabled("_ALPHATEST_ON") || (queue >= 2450 && queue < 2500);
            bool blend = src.IsKeywordEnabled("_ALPHABLEND_ON") || src.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") || queue >= 2500;

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
                dst.SetFloat("_Cutoff", src.HasProperty("_Cutoff") ? src.GetFloat("_Cutoff") : 0.5f);
                dst.renderQueue = 2450;
            }
            else
            {
                dst.renderQueue = 2000;
            }

            return dst;
        }

        // ---------------------------------------------------------------- cleanup

        internal static void RemoveForeignComponents(GameObject root)
        {
            // everything that is not Unity, Custom Avatars, Final IK or Dynamic Bone would be a missing script in game
            for (int pass = 0; pass < 5; pass++)
            {
                bool removedAny = false;

                foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    string asm = mb.GetType().Assembly.GetName().Name;
                    if (asm == "CustomAvatar" || asm == "DynamicBone" || asm == "FinalIK" || asm.StartsWith("UnityEngine")) continue;

                    UnityEngine.Object.DestroyImmediate(mb);
                    removedAny = true;
                }

                if (!removedAny) break;
            }

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            }
        }

        // ---------------------------------------------------------------- cover

        internal static Sprite MakeCover(Texture2D thumbnail, GameObject root, Animator animator, string workDir)
        {
            Stage("cover");
            string pngPath = $"{workDir}/cover.png";
            byte[] png = null;

            if (thumbnail != null)
            {
                png = ReadableCopy(thumbnail, 512).EncodeToPNG();
            }
            else
            {
                png = RenderPortrait(root, animator);
            }

            if (png == null) return null;

            File.WriteAllBytes(pngPath, png);
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
        }

        internal static Texture2D ReadableCopy(Texture source, int maxSize)
        {
            int w = source.width, h = source.height;
            float k = Mathf.Min(1f, (float)maxSize / Mathf.Max(w, h));
            w = Mathf.Max(1, Mathf.RoundToInt(w * k));
            h = Mathf.Max(1, Mathf.RoundToInt(h * k));

            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        private static byte[] RenderPortrait(GameObject root, Animator animator)
        {
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return null;

            // frame from the chest to the top of the model (ears, hair, hats included)
            Transform chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
            float top = root.GetComponentsInChildren<Renderer>(true).Select(r => r.bounds.max.y).DefaultIfEmpty(head.position.y).Max();
            float bottom = chest != null ? Mathf.Lerp(chest.position.y, head.position.y, 0.3f) : head.position.y * 0.85f;
            float span = Mathf.Max(top - bottom, 0.05f);

            GameObject camGo = new GameObject("CoverCamera");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.16f, 1);
            cam.fieldOfView = 30;
            cam.nearClipPlane = 0.001f;
            Vector3 target = new Vector3(head.position.x, (top + bottom) * 0.5f, head.position.z);
            float distance = span * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;
            camGo.transform.position = target + new Vector3(0, 0, distance);
            camGo.transform.LookAt(target);

            RenderTexture rt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(camGo);
            rt.Release();

            return tex.EncodeToPNG();
        }

        // ---------------------------------------------------------------- export

        internal static void ExportBundle(GameObject root, string destination)
        {
            Stage("bundle");
            PrefabUtility.SaveAsPrefabAsset(root, BundlePrefabPath);
            string tempFolder = Path.Combine(Application.temporaryCachePath, "VRM2BS");
            Directory.CreateDirectory(tempFolder);
            string bundleName = Path.GetFileName(destination).ToLowerInvariant();

            try
            {
                AssetBundleBuild build = new AssetBundleBuild
                {
                    assetBundleName = bundleName,
                    assetNames = new[] { BundlePrefabPath },
                };

                AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(tempFolder, new[] { build }, BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
                if (manifest == null) throw new Exception("asset bundle build failed (see Unity log)");

                File.Copy(Path.Combine(tempFolder, manifest.GetAllAssetBundles()[0]), destination, true);
                ExportCover(root, destination);
                AvatarPreview.Built.Add(destination);
            }
            finally
            {
                AssetDatabase.DeleteAsset(BundlePrefabPath);
            }
        }

        // optional "-coverDir": the GUI shows these next to the results (kept out of the CustomAvatars folder)
        private static void ExportCover(GameObject root, string destination)
        {
            string coverDir = GetArg(Environment.GetCommandLineArgs(), "-coverDir");
            AvatarDescriptor descriptor = root.GetComponent<AvatarDescriptor>();
            Sprite cover = descriptor != null ? descriptor.cover : null;
            if (string.IsNullOrEmpty(coverDir) || cover == null) return;

            Directory.CreateDirectory(coverDir);
            File.WriteAllBytes(Path.Combine(coverDir, Path.GetFileNameWithoutExtension(destination) + ".png"), ReadableCopy(cover.texture, 256).EncodeToPNG());
        }

        // ---------------------------------------------------------------- validation of built files (loads them like the mod does)

        public static void Validate()
        {
            string outDir = GetArg(Environment.GetCommandLineArgs(), "-outDir");
            List<string> lines = new List<string>();
            string[] trackers = { "Head", "LeftHand", "RightHand", "Pelvis", "LeftLeg", "RightLeg" };

            foreach (string file in Directory.GetFiles(outDir, "*.avatar").OrderBy(f => f))
            {
                List<string> problems = new List<string>();
                AssetBundle bundle = AssetBundle.LoadFromFile(file);

                if (bundle == null)
                {
                    lines.Add($"BAD  {Path.GetFileName(file)}: not a loadable asset bundle");
                    continue;
                }

                try
                {
                    GameObject prefab = bundle.LoadAsset<GameObject>("_CustomAvatar");
                    if (prefab == null) { lines.Add($"BAD  {Path.GetFileName(file)}: no _CustomAvatar object"); continue; }

                    AvatarDescriptor d = prefab.GetComponent<AvatarDescriptor>();
                    if (d == null) problems.Add("no AvatarDescriptor");
                    if (d != null && d.cover == null) problems.Add("no cover image");

                    foreach (string t in trackers) if (prefab.transform.Find(t) == null) problems.Add("missing tracker " + t);

                    VRIKManager vrik = prefab.GetComponentInChildren<VRIKManager>(true);
                    if (vrik == null || !vrik.areReferencesFilled) problems.Add("IK references not filled");

                    PoseManager pm = prefab.GetComponentInChildren<PoseManager>(true);
                    bool fingers = pm != null && pm.isValid;

                    int missingScripts = prefab.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
                    if (missingScripts > 0) problems.Add($"{missingScripts} missing scripts");

                    DynamicBone[] dbs = prefab.GetComponentsInChildren<DynamicBone>(true);
                    string tails = string.Join(", ", dbs.Where(b => b.m_Root != null && TailName.IsMatch(b.m_Root.name)).Select(b => b.m_Root.name));

                    List<string> shaders = prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                        .Where(m => m != null).Select(m => m.shader).Distinct()
                        .Select(s => s == null ? "<null>" : s.name + (s.isSupported ? "" : " (UNSUPPORTED)")).ToList();
                    if (shaders.Any(s => s != ShaderName)) problems.Add("unexpected shaders: " + string.Join(", ", shaders.Where(s => s != ShaderName)));

                    float eye = prefab.transform.Find("Head") is Transform h ? h.position.y : 0;

                    lines.Add($"{(problems.Count == 0 ? "OK  " : "WARN")} {Path.GetFileName(file)}: name \"{d?.name}\", author \"{d?.author}\", " +
                              $"eye height {eye:0.00} m, {dbs.Length} physics chains (tail: {(tails.Length > 0 ? tails : "none")}), " +
                              $"finger poses {(fingers ? "yes" : "no")}" + (problems.Count > 0 ? "\n       problems: " + string.Join("; ", problems) : ""));
                }
                finally
                {
                    bundle.Unload(true);
                }
            }

            File.WriteAllLines(Path.Combine(outDir, "validation_report.txt"), lines);
            Debug.Log("[VRM2BS] VALIDATION\n" + string.Join("\n", lines));
        }

        // ---------------------------------------------------------------- preview (for checking results)

        /// <summary>Renders the built .avatar files themselves (front + side), exactly what the game loads.</summary>
        public static void PreviewBundles()
        {
            string[] args = Environment.GetCommandLineArgs();
            string avatarDir = GetArg(args, "-avatarDir");
            string outDir = GetArg(args, "-outDir");
            Directory.CreateDirectory(outDir);

            foreach (string file in Directory.GetFiles(avatarDir, "*.avatar"))
            {
                AssetBundle bundle = AssetBundle.LoadFromFile(file);
                if (bundle == null) continue;

                try
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    GameObject root = UnityEngine.Object.Instantiate(bundle.LoadAsset<GameObject>("_CustomAvatar"));
                    Transform headTracker = root.transform.Find("Head");
                    float h = headTracker != null ? Mathf.Max(headTracker.position.y, 0.2f) : 1.5f;
                    string name = Path.GetFileNameWithoutExtension(file);
                    File.WriteAllBytes(Path.Combine(outDir, name + "_front.png"), RenderView(new Vector3(0, h * 0.55f, h * 1.9f), new Vector3(0, h * 0.55f, 0), 768, 768));
                    File.WriteAllBytes(Path.Combine(outDir, name + "_side.png"), RenderView(new Vector3(h * 1.9f, h * 0.55f, h * 0.3f), new Vector3(0, h * 0.55f, 0), 768, 768));
                }
                finally
                {
                    bundle.Unload(true);
                }
            }
        }

        public static void Preview()
        {
            string outDir = GetArg(Environment.GetCommandLineArgs(), "-outDir");
            Directory.CreateDirectory(outDir);

            foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { WorkRoot }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith("_avatar.prefab")))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));

                Shader marker = Shader.Find("Unlit/Color");
                foreach (string tracker in new[] { "Head", "LeftHand", "RightHand", "Pelvis", "LeftLeg", "RightLeg" })
                {
                    Transform t = root.transform.Find(tracker);
                    if (t == null) continue;
                    GameObject s = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    s.transform.SetParent(t, false);
                    s.transform.localScale = new Vector3(0.03f, 0.03f, 0.25f); // a stick pointing where the saber would go
                    s.transform.localPosition = new Vector3(0, 0, 0.125f);
                    s.GetComponent<Renderer>().sharedMaterial = new Material(marker) { color = tracker.Contains("Left") ? Color.red : tracker.Contains("Right") ? Color.cyan : Color.yellow };
                }

                float h = root.transform.Find("Head").position.y;
                byte[] front = RenderView(new Vector3(0, h * 0.55f, h * 1.9f), new Vector3(0, h * 0.55f, 0), 1024, 1024);
                byte[] side = RenderView(new Vector3(h * 1.9f, h * 0.55f, h * 0.3f), new Vector3(0, h * 0.55f, 0), 1024, 1024);
                string name = Path.GetFileNameWithoutExtension(path).Replace("_avatar", "");
                File.WriteAllBytes(Path.Combine(outDir, name + "_front.png"), front);
                File.WriteAllBytes(Path.Combine(outDir, name + "_side.png"), side);
            }
        }

        private static byte[] RenderView(Vector3 from, Vector3 to, int w, int h)
        {
            GameObject camGo = new GameObject("PreviewCamera");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.25f, 0.25f, 0.3f, 1);
            cam.fieldOfView = 35;
            cam.nearClipPlane = 0.01f;
            camGo.transform.position = from;
            camGo.transform.LookAt(to);

            RenderTexture rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(camGo);
            rt.Release();
            return tex.EncodeToPNG();
        }

        // ---------------------------------------------------------------- helpers

        internal static string GetArg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        internal static string MakeSafe(string s)
        {
            char[] bad = Path.GetInvalidFileNameChars().Concat(new[] { '#', '%', '&', '{', '}', '$', '!', '\'', '@', '+', '`', '=' }).ToArray();
            string r = new string(s.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim().Trim('.');
            return string.IsNullOrEmpty(r) ? "avatar" : r;
        }

        internal static string UniqueName(string name, HashSet<string> used)
        {
            string candidate = name;
            for (int i = 2; !used.Add(candidate); i++) candidate = $"{name} ({i})";
            return candidate;
        }

        internal static string FileHash(string path)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
            using (FileStream fs = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(fs));
            }
        }

        // progress markers for the GUI: "BEGIN <model>" then "STAGE <import|skeleton|physics|materials|ik|cover|bundle>"
        internal static void Begin(string label) => Debug.Log("[VRM2BS] BEGIN " + label);

        internal static void Stage(string name) => Debug.Log("[VRM2BS] STAGE " + name);

        internal static void Log(string line)
        {
            Report.Add(line);
            Debug.Log("[VRM2BS] " + line);
        }
    }
}
