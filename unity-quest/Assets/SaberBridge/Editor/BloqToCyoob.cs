using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static SaberBridge.SaberToWhacker;

namespace SaberBridge
{
    /// <summary>
    /// PC Custom Notes ".bloq" -> Quest Qosmetics ".cyoob".
    ///   Unity.exe -batchmode -quit -projectPath unity-quest -executeMethod SaberBridge.BloqToCyoob.Run -jobsFile jobs.txt
    /// jobs.txt: one "source.bloq|output folder" per line. Progress lines start with "[SB] ", like the saber converter.
    ///
    /// Layout Qosmetics loads (object "_Cyoob"): Notes/{LeftArrow,RightArrow,LeftDot,RightDot}, Bomb,
    /// Chains/{LeftHead,RightHead,LeftLink,RightLink}. Qosmetics resets each of these objects' own transform and puts the
    /// "Notes" object at scale 0.4 under the game's note cube. Custom Notes also ignores each object's own position and
    /// scale: it puts the object at the cube's centre with scale 0.4 and keeps only its rotation. So each PC object goes
    /// inside the matching Quest object at position 0, scale 1, own rotation, and the note has the same size and pose
    /// on the Quest as on PC, whatever transform its author left in the prefab.
    /// </summary>
    public static class BloqToCyoob
    {
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string jobsFile = args[Array.IndexOf(args, "-jobsFile") + 1];
            string[] jobs = File.ReadAllLines(jobsFile).Where(l => l.Trim().Length > 0).ToArray();

            for (int i = 0; i < jobs.Length; i++)
            {
                string[] parts = jobs[i].Split('|');
                Log($"BEGIN {i + 1}/{jobs.Length} {Path.GetFileName(parts[0])}");
                try
                {
                    string result = Convert(parts[0], parts[1]);
                    Log($"OK   {Path.GetFileName(parts[0])} -> {result}");
                }
                catch (Exception ex)
                {
                    Log($"FAIL {Path.GetFileName(parts[0])}: {ex.Message}");
                    Debug.LogException(ex);
                }
            }
        }

        internal static string Convert(string bloqPath, string outDir)
        {
            // a new scene unloads unused assets, which would also wipe the script data of an already loaded bundle
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetBundle bundle = AssetBundle.LoadFromFile(bloqPath);
            if (bundle == null) throw new Exception("not a Unity asset bundle");

            try
            {
                GameObject prefab = bundle.LoadAllAssets<GameObject>().FirstOrDefault(g => g.transform.parent == null && g.transform.Find("NoteLeft") && g.transform.Find("NoteRight"))
                                    ?? throw new Exception("no NoteLeft / NoteRight in this file");
                GameObject pc = UnityEngine.Object.Instantiate(prefab);

                string name = Path.GetFileNameWithoutExtension(bloqPath), author = "", description = "";
                Texture2D icon = null;
                bool showArrows = true, usesNoteColor = false;
                ReadDescriptor(pc, ref name, ref author, ref description, ref icon, ref showArrows, ref usesNoteColor);

                string id = Safe(name);
                string work = $"{WorkRoot}/{id}";
                if (AssetDatabase.IsValidFolder(work)) AssetDatabase.DeleteAsset(work);
                if (!AssetDatabase.IsValidFolder(WorkRoot)) AssetDatabase.CreateFolder("Assets", "SaberBridge_Work");
                AssetDatabase.CreateFolder(WorkRoot, id);

                Log("STAGE assets");
                AssetCopier copier = new AssetCopier(work);
                copier.CopyRenderers(pc);
                ApplyNoteColors(pc, copier, usesNoteColor);
                pc = RemoveScripts(pc);

                Log("STAGE layout");
                GameObject root = new GameObject("_Cyoob");
                Transform notes = Group(root, "Notes");
                Transform leftArrow = Take(pc, "NoteLeft", notes, "LeftArrow");
                Transform rightArrow = Take(pc, "NoteRight", notes, "RightArrow");
                // Custom Notes uses the arrow note when a dot note is missing
                if (!Take(pc, "NoteDotLeft", notes, "LeftDot")) Copy(leftArrow, notes, "LeftDot");
                if (!Take(pc, "NoteDotRight", notes, "RightDot")) Copy(rightArrow, notes, "RightDot");

                bool hasBomb = Take(pc, "NoteBomb", root.transform, "Bomb") != null;

                // chains only when the note has all four parts; otherwise Qosmetics builds them from the notes itself
                bool hasSlider = new[] { "BurstSliderHeadLeft", "BurstSliderHeadRight", "BurstSliderLeft", "BurstSliderRight" }.All(n => pc.transform.Find(n));
                if (hasSlider)
                {
                    Transform chains = Group(root, "Chains");
                    Take(pc, "BurstSliderHeadLeft", chains, "LeftHead");
                    Take(pc, "BurstSliderHeadRight", chains, "RightHead");
                    Take(pc, "BurstSliderLeft", chains, "LeftLink");
                    Take(pc, "BurstSliderRight", chains, "RightLink");
                }

                UnityEngine.Object.DestroyImmediate(pc);
                Log($"INFO parts: notes{(hasBomb ? ", bomb" : "")}{(hasSlider ? ", chains" : "")}; note colour {(usesNoteColor ? "on" : "off")}; arrows {(showArrows ? "shown" : "hidden")}");

                Log("STAGE bundle");
                string fileBase = name.ToLowerInvariant();
                if (!AssetDatabase.IsValidFolder("Assets/Qosmetics")) AssetDatabase.CreateFolder("Assets", "Qosmetics");
                string prefabPath = "Assets/Qosmetics/_Cyoob.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

                // encoded now: building bundles unloads in-memory textures
                byte[] thumbPng = (icon != null ? Readable(icon, new Rect(0, 0, icon.width, icon.height)) : NoteThumbnail(root)).EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(root);

                string temp = Path.Combine(Path.GetTempPath(), "SaberBridge", "cyoob_" + id);
                if (Directory.Exists(temp)) Directory.Delete(temp, true);
                Directory.CreateDirectory(temp);
                Build(prefabPath, temp, fileBase + "_pc", BuildTarget.StandaloneWindows64);
                Build(prefabPath, temp, fileBase + "_android", BuildTarget.Android);
                File.WriteAllBytes(Path.Combine(temp, "thumbnail.png"), thumbPng);
                File.WriteAllText(Path.Combine(temp, "package.json"), PackageJson(fileBase, name, author, description, hasBomb, hasSlider, showArrows));

                Directory.CreateDirectory(outDir);
                string cyoob = Path.Combine(outDir, Path.GetFileNameWithoutExtension(bloqPath) + ".cyoob");
                if (File.Exists(cyoob)) File.Delete(cyoob);
                using (ZipArchive zip = ZipFile.Open(cyoob, ZipArchiveMode.Create))
                {
                    foreach (string f in new[] { fileBase + "_pc", fileBase + "_android", "package.json", "thumbnail.png" })
                        zip.CreateEntryFromFile(Path.Combine(temp, f), f, System.IO.Compression.CompressionLevel.Optimal);
                }

                Directory.Delete(temp, true);
                AssetDatabase.DeleteAsset(prefabPath);
                AssetDatabase.DeleteAsset(work);
                Log($"INFO {copier.Meshes} meshes, {copier.Materials} materials ({copier.ReplacedShaders} shaders swapped for Quest)");
                return cyoob;
            }
            finally
            {
                // building bundles unloads every loaded bundle, the .bloq included
                if (bundle) bundle.Unload(true);
            }
        }

        private static void ReadDescriptor(GameObject root, ref string name, ref string author, ref string description, ref Texture2D icon, ref bool showArrows, ref bool usesNoteColor)
        {
            foreach (MonoBehaviour mb in root.GetComponents<MonoBehaviour>().Where(m => m != null))
            {
                SerializedObject so = new SerializedObject(mb);
                if (so.FindProperty("NoteName") == null) continue;
                name = NonEmpty(so.FindProperty("NoteName").stringValue, name);
                author = so.FindProperty("AuthorName")?.stringValue ?? "";
                description = so.FindProperty("Description")?.stringValue ?? "";
                icon = so.FindProperty("Icon")?.objectReferenceValue as Texture2D;
                showArrows = !(so.FindProperty("DisableBaseNoteArrows")?.boolValue ?? false);
                usesNoteColor = so.FindProperty("UsesNoteColor")?.boolValue ?? false;
            }
        }

        // Custom Notes tints every renderer with the note colour when UsesNoteColor is on, except objects marked with
        // DisableNoteColorOnGameobject; Qosmetics tints the materials whose _CustomColors is on.
        private static void ApplyNoteColors(GameObject pc, AssetCopier copier, bool usesNoteColor)
        {
            foreach (Renderer r in pc.GetComponentsInChildren<Renderer>(true))
            {
                bool marked = r.GetComponents<MonoBehaviour>().Any(m => m != null && m.GetType().Name == "DisableNoteColorOnGameobject");
                bool on = usesNoteColor && !marked;
                r.sharedMaterials = r.sharedMaterials.Select(m => copier.WithCustomColors(m, on)).ToArray();
            }
        }

        private static Transform Group(GameObject root, string name)
        {
            Transform t = new GameObject(name).transform;
            t.SetParent(root.transform, false);
            return t;
        }

        // moves the PC object into a new Quest object of the given name, placed the way Custom Notes places it (centre, scale 0.4 overall, own rotation)
        private static Transform Take(GameObject pc, string pcName, Transform parent, string questName)
        {
            Transform source = pc.transform.Find(pcName);
            if (source == null) return null;

            Transform target = new GameObject(questName).transform;
            target.SetParent(parent, false);
            source.SetParent(target, false);
            source.localPosition = Vector3.zero;
            source.localScale = Vector3.one;
            return target;
        }

        private static void Copy(Transform from, Transform parent, string questName)
        {
            GameObject c = UnityEngine.Object.Instantiate(from.gameObject, parent, false);
            c.name = questName;
        }

        private static string PackageJson(string fileBase, string name, string author, string description, bool hasBomb, bool hasSlider, bool showArrows)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n  \"androidFileName\": \"").Append(Esc(fileBase + "_android")).Append("\",\n");
            sb.Append("  \"pcFileName\": \"").Append(Esc(fileBase + "_pc")).Append("\",\n");
            sb.Append("  \"descriptor\": {\n    \"objectName\": \"").Append(Esc(name)).Append("\",\n    \"author\": \"").Append(Esc(author));
            sb.Append("\",\n    \"description\": \"").Append(Esc(description)).Append("\",\n    \"coverImage\": \"thumbnail.png\"\n  },\n");
            sb.Append("  \"config\": {\n");
            sb.Append("    \"hasDebris\": false,\n    \"hasChainHeadDebris\": false,\n    \"hasChainLinkDebris\": false,\n");
            sb.Append("    \"hasSlider\": ").Append(hasSlider ? "true" : "false").Append(",\n");
            sb.Append("    \"hasBomb\": ").Append(hasBomb ? "true" : "false").Append(",\n");
            sb.Append("    \"showArrows\": ").Append(showArrows ? "true" : "false").Append(",\n");
            sb.Append("    \"isMirrorable\": false\n  }\n}");
            return sb.ToString();
        }

        // the four notes side by side, in their left / right colours, for the list and the Qosmetics menu
        private static Texture2D NoteThumbnail(GameObject root)
        {
            Transform notes = root.transform.Find("Notes");
            string[] order = { "LeftArrow", "RightArrow", "LeftDot", "RightDot" };
            List<GameObject> shown = new List<GameObject>();
            for (int i = 0; i < order.Length; i++)
            {
                Transform t = notes.Find(order[i]);
                if (t == null) continue;
                GameObject c = UnityEngine.Object.Instantiate(t.gameObject);
                c.transform.position = new Vector3((i % 2) * 1.6f - 0.8f, (i / 2) * -1.6f + 0.8f, 0);
                c.transform.rotation = Quaternion.Euler(0, 180, 0);
                Color tint = order[i].StartsWith("Left") ? new Color(0.78f, 0.08f, 0.08f) : new Color(0.16f, 0.56f, 0.86f);
                foreach (Renderer r in c.GetComponentsInChildren<Renderer>(true))
                {
                    MaterialPropertyBlock b = new MaterialPropertyBlock();
                    r.GetPropertyBlock(b);
                    foreach (Material m in r.sharedMaterials)
                        if (m != null && m.HasProperty("_CustomColors") && m.GetFloat("_CustomColors") > 0) b.SetColor("_Color", tint);
                    r.SetPropertyBlock(b);
                }

                shown.Add(c);
            }

            GameObject group = new GameObject("thumb");
            foreach (GameObject g in shown) g.transform.SetParent(group.transform, true);
            Texture2D tex = Thumbnail(group);
            UnityEngine.Object.DestroyImmediate(group);
            return tex;
        }
    }
}
