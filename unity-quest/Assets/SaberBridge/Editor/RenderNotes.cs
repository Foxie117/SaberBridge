using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SaberBridge
{
    // Development aid: renders note bundles (.bloq, or *_pc from a .cyoob) the way each mod places them under the game's
    // note cube: Custom Notes at scale 1 keeping each object's own transform, Qosmetics at scale 0.4 with it reset.
    // A 0.5 m grey frame next to each row shows the size of a normal note.
    //   Unity.exe -batchmode -quit -projectPath unity-quest -executeMethod SaberBridge.RenderNotes.Run -dir <folder> -out <folder>
    public static class RenderNotes
    {
        private static readonly string[] PcParts = { "NoteLeft", "NoteRight", "NoteDotLeft", "NoteDotRight", "NoteBomb" };
        private static readonly string[] QuestParts = { "Notes/LeftArrow", "Notes/RightArrow", "Notes/LeftDot", "Notes/RightDot", "Bomb" };

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string dir = args[Array.IndexOf(args, "-dir") + 1];
            string outDir = args[Array.IndexOf(args, "-out") + 1];
            Directory.CreateDirectory(outDir);

            foreach (string file in Directory.GetFiles(dir))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetBundle b = AssetBundle.LoadFromFile(file);
                if (b == null) continue;
                GameObject prefab = b.LoadAllAssets<GameObject>().FirstOrDefault(g => g.transform.parent == null);
                if (prefab == null) { b.Unload(true); continue; }
                GameObject root = UnityEngine.Object.Instantiate(prefab);
                bool quest = root.transform.Find("Notes") != null;
                string[] parts = quest ? QuestParts : PcParts;

                for (int i = 0; i < parts.Length; i++)
                {
                    Transform src = root.transform.Find(parts[i]);
                    // the game's note cube; notes fly towards the player, so their front faces -Z
                    Transform cube = new GameObject("NoteCube").transform;
                    cube.position = new Vector3(i * 0.8f, 0, 0);
                    Frame(cube.position);
                    if (src == null) continue;
                    if (quest)
                    {
                        Transform holder = new GameObject("Notes").transform;
                        holder.SetParent(cube, false);
                        holder.localScale = Vector3.one * 0.4f;
                        src.SetParent(holder, false);
                        src.localPosition = Vector3.zero;
                        src.localRotation = Quaternion.identity;
                        src.localScale = Vector3.one;
                    }
                    else
                    {
                        src.SetParent(cube, false);
                    }

                    Color tint = parts[i].Contains("Left") ? new Color(0.78f, 0.1f, 0.1f) : new Color(0.15f, 0.5f, 0.85f);
                    foreach (Renderer r in src.GetComponentsInChildren<Renderer>(true))
                    {
                        MaterialPropertyBlock mb = new MaterialPropertyBlock();
                        mb.SetColor("_Color", tint);
                        if (!parts[i].Contains("Bomb")) r.SetPropertyBlock(mb);
                    }
                }

                UnityEngine.Object.DestroyImmediate(root);

                Renderer[] rs = UnityEngine.Object.FindObjectsOfType<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
                Bounds bounds = rs.Length > 0 ? rs[0].bounds : new Bounds(Vector3.zero, Vector3.one);
                foreach (Renderer r in rs) bounds.Encapsulate(r.bounds);

                GameObject camGo = new GameObject("Cam");
                Camera cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.08f, 0.08f, 0.1f, 1);
                cam.orthographic = true;
                cam.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x * 330f / 1200f) * 1.1f;
                cam.farClipPlane = 1000;
                camGo.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z - 50);
                GameObject lightGo = new GameObject("Light");
                Light l = lightGo.AddComponent<Light>();
                l.type = LightType.Directional;
                lightGo.transform.rotation = Quaternion.Euler(30, 20, 0);

                RenderTexture rt = new RenderTexture(1200, 330, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, Path.GetFileName(file) + ".png"), tex.EncodeToPNG());
                b.Unload(true);
            }
        }

        private static void Frame(Vector3 c)
        {
            Material m = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.45f, 0.45f, 0.5f) };
            for (int i = 0; i < 4; i++)
            {
                GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.GetComponent<Renderer>().sharedMaterial = m;
                bool vertical = i < 2;
                bar.transform.position = c + (vertical ? new Vector3(i == 0 ? -0.25f : 0.25f, 0, 0.3f) : new Vector3(0, i == 2 ? -0.25f : 0.25f, 0.3f));
                bar.transform.localScale = vertical ? new Vector3(0.008f, 0.508f, 0.008f) : new Vector3(0.508f, 0.008f, 0.008f);
            }
        }
    }
}
