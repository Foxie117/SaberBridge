using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SaberBridge
{
    /// <summary>
    /// PC CustomSabers ".saber" -> Quest Qosmetics ".whacker".
    ///   Unity.exe -batchmode -quit -projectPath unity-quest -executeMethod SaberBridge.SaberToWhacker.Run -jobsFile jobs.txt
    /// jobs.txt: one "source.saber|output folder" per line. Progress lines start with "[SB] ".
    /// A .whacker is a zip of: name_pc (Windows bundle), name_android (Android bundle), package.json, thumbnail.png.
    /// Qosmetics strips its own scripts on export and keeps trail settings as JSON in disabled UI.Text components,
    /// so the bundles only contain built-in Unity components; this converter writes the same layout.
    /// </summary>
    public static class SaberToWhacker
    {
        private const string WorkRoot = "Assets/SaberBridge_Work";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

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

        private static void Log(string s) => Debug.Log("[SB] " + s);

        private static string Convert(string saberPath, string outDir)
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(saberPath);
            if (bundle == null) throw new Exception("not a Unity asset bundle");

            try
            {
                GameObject prefab = bundle.LoadAllAssets<GameObject>().FirstOrDefault(g => g.transform.parent == null && g.transform.Find("LeftSaber") && g.transform.Find("RightSaber"))
                                    ?? throw new Exception("no LeftSaber / RightSaber in this saber");

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                GameObject root = UnityEngine.Object.Instantiate(prefab);

                string saberName = Path.GetFileNameWithoutExtension(saberPath), author = "", description = "";
                Sprite cover = null;
                ReadDescriptor(root, ref saberName, ref author, ref description, ref cover);

                string id = Safe(saberName);
                string work = $"{WorkRoot}/{id}";
                if (AssetDatabase.IsValidFolder(work)) AssetDatabase.DeleteAsset(work);
                if (!AssetDatabase.IsValidFolder(WorkRoot)) AssetDatabase.CreateFolder("Assets", "SaberBridge_Work");
                AssetDatabase.CreateFolder(WorkRoot, id);

                Log("STAGE assets");
                AssetCopier copier = new AssetCopier(work);
                copier.CopyRenderers(root);

                Log("STAGE trails");
                int trails = ConvertTrails(root, copier);

                RemoveScripts(root);
                root.name = saberName;

                Log("STAGE bundle");
                string fileBase = saberName.ToLowerInvariant();
                string prefabPath = $"{work}/{id}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

                Texture2D thumb = cover != null ? Readable(cover.texture, cover.textureRect) : Thumbnail(root);
                UnityEngine.Object.DestroyImmediate(root);

                string temp = Path.Combine(Path.GetTempPath(), "SaberBridge", id);
                if (Directory.Exists(temp)) Directory.Delete(temp, true);
                Directory.CreateDirectory(temp);
                Build(prefabPath, temp, fileBase + "_pc", BuildTarget.StandaloneWindows64);
                Build(prefabPath, temp, fileBase + "_android", BuildTarget.Android);
                File.WriteAllBytes(Path.Combine(temp, "thumbnail.png"), thumb.EncodeToPNG());
                File.WriteAllText(Path.Combine(temp, "package.json"), PackageJson(fileBase, saberName, author, description, trails > 0));

                Directory.CreateDirectory(outDir);
                string whacker = Path.Combine(outDir, Path.GetFileNameWithoutExtension(saberPath) + ".whacker");
                if (File.Exists(whacker)) File.Delete(whacker);
                using (ZipArchive zip = ZipFile.Open(whacker, ZipArchiveMode.Create))
                {
                    foreach (string f in new[] { fileBase + "_pc", fileBase + "_android", "package.json", "thumbnail.png" })
                        zip.CreateEntryFromFile(Path.Combine(temp, f), f, System.IO.Compression.CompressionLevel.Optimal);
                }

                Directory.Delete(temp, true);
                AssetDatabase.DeleteAsset(work);
                Log($"INFO {trails} trails, {copier.Meshes} meshes, {copier.Materials} materials ({copier.ReplacedShaders} shaders swapped for Quest)");
                return whacker;
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        // ---------------------------------------------------------------- descriptor and trails (read by field name, so no CustomSabers dll is needed)

        private static void ReadDescriptor(GameObject root, ref string name, ref string author, ref string description, ref Sprite cover)
        {
            foreach (MonoBehaviour mb in root.GetComponents<MonoBehaviour>().Where(m => m != null))
            {
                SerializedObject so = new SerializedObject(mb);
                if (so.FindProperty("SaberName") == null) continue;
                name = NonEmpty(so.FindProperty("SaberName").stringValue, name);
                author = so.FindProperty("AuthorName")?.stringValue ?? "";
                description = so.FindProperty("Description")?.stringValue ?? "";
                cover = so.FindProperty("CoverImage")?.objectReferenceValue as Sprite;
            }
        }

        private static int ConvertTrails(GameObject root, AssetCopier copier)
        {
            int id = 0;
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m != null).ToList())
            {
                SerializedObject so = new SerializedObject(mb);
                SerializedProperty start = so.FindProperty("PointStart"), end = so.FindProperty("PointEnd");
                if (start == null || end == null) continue;
                Transform bottom = start.objectReferenceValue as Transform, top = end.objectReferenceValue as Transform;
                Material mat = so.FindProperty("TrailMaterial")?.objectReferenceValue as Material;
                if (bottom == null || top == null || mat == null) continue;

                int colorType = so.FindProperty("colorType")?.enumValueIndex ?? 2;
                Color trailColor = so.FindProperty("TrailColor")?.colorValue ?? Color.white;
                Color multiplier = so.FindProperty("MultiplierColor")?.colorValue ?? Color.white;
                int length = so.FindProperty("Length")?.intValue ?? 20;

                // the trail object: its JSON, plus a MeshRenderer that carries the trail material (as Qosmetics' Trail.OnExport does)
                GameObject trailGo = new GameObject($"Trail{id}");
                trailGo.transform.SetParent(mb.transform, false);
                SetText(trailGo, "{\n  \"trailId\": " + id + ",\n  \"colorType\": " + colorType + ",\n  \"trailColor\": " + ColorJson(trailColor) +
                                 ",\n  \"multiplierColor\": " + ColorJson(multiplier) + ",\n  \"length\": " + length + ",\n  \"whiteStep\": 0.0\n}");
                trailGo.AddComponent<MeshRenderer>().sharedMaterial = copier.CopyMaterial(mat, true);
                SetText(top.gameObject, "{\n  \"isTop\": true,\n  \"trailId\": " + id + "\n}");
                SetText(bottom.gameObject, "{\n  \"isTop\": false,\n  \"trailId\": " + id + "\n}");
                id++;
            }

            return id;
        }

        private static string ColorJson(Color c) =>
            "{\n    \"r\": " + c.r.ToString("0.0######", Inv) + ",\n    \"g\": " + c.g.ToString("0.0######", Inv) + ",\n    \"b\": " + c.b.ToString("0.0######", Inv) + ",\n    \"a\": " + c.a.ToString("0.0######", Inv) + "\n  }";

        private static void SetText(GameObject go, string text)
        {
            Text t = go.GetComponent<Text>() ?? go.AddComponent<Text>();
            t.text = text;
            t.fontSize = 0;
            t.enabled = false;
        }

        // CustomSabers scripts (and anything else that is not Unity's) cannot exist on the Quest
        private static void RemoveScripts(GameObject root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                foreach (MonoBehaviour mb in t.GetComponents<MonoBehaviour>())
                {
                    if (mb != null && !(mb is Text)) UnityEngine.Object.DestroyImmediate(mb);
                }

                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            }
        }

        // ---------------------------------------------------------------- packaging

        private static void Build(string prefabPath, string dir, string name, BuildTarget target)
        {
            AssetBundleBuild b = new AssetBundleBuild { assetBundleName = name, assetNames = new[] { prefabPath } };
            string tmp = Path.Combine(dir, "build_" + target);
            Directory.CreateDirectory(tmp);
            AssetBundleManifest m = BuildPipeline.BuildAssetBundles(tmp, new[] { b }, BuildAssetBundleOptions.None, target);
            if (m == null) throw new Exception("bundle build failed for " + target);
            File.Copy(Path.Combine(tmp, name), Path.Combine(dir, name), true);
            Directory.Delete(tmp, true);
        }

        private static string PackageJson(string fileBase, string name, string author, string description, bool hasTrail)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n  \"androidFileName\": \"").Append(Esc(fileBase + "_android")).Append("\",\n");
            sb.Append("  \"pcFileName\": \"").Append(Esc(fileBase + "_pc")).Append("\",\n");
            sb.Append("  \"descriptor\": {\n    \"objectName\": \"").Append(Esc(name)).Append("\",\n    \"author\": \"").Append(Esc(author));
            sb.Append("\",\n    \"description\": \"").Append(Esc(description)).Append("\",\n    \"coverImage\": \"thumbnail.png\"\n  },\n");
            sb.Append("  \"config\": {\n    \"hasTrail\": ").Append(hasTrail ? "true" : "false").Append("\n  }\n}");
            return sb.ToString();
        }

        private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

        private static string NonEmpty(string s, string fallback) => string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();

        private static string Safe(string s)
        {
            string r = new string(s.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
            return string.IsNullOrEmpty(r) ? "saber" : r;
        }

        // ---------------------------------------------------------------- thumbnail

        internal static Texture2D Readable(Texture source, Rect rect)
        {
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            int w = Mathf.Max(1, (int)rect.width), h = Mathf.Max(1, (int)rect.height);
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(rect.x, source.height - rect.y - h, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        private static Texture2D Thumbnail(GameObject root)
        {
            Renderer[] rs = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
            Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(Vector3.zero, Vector3.one);
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);

            GameObject camGo = new GameObject("ThumbCamera");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.1f, 0.14f, 1);
            cam.fieldOfView = 30;
            float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
            camGo.transform.position = b.center + new Vector3(size * 0.6f, size * 0.2f, size * 1.9f);
            camGo.transform.LookAt(b.center);
            RenderTexture rt = new RenderTexture(512, 512, 24);
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
            return tex;
        }
    }
}
