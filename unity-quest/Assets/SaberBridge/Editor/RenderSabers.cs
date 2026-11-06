using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SaberBridge
{
    // Development aid: renders every saber bundle in a folder (.saber or *_pc from a .whacker) to PNG, both sabers side by side.
    //   Unity.exe -batchmode -quit -projectPath unity-quest -executeMethod SaberBridge.RenderSabers.Run -dir <folder> -out <folder>
    public static class RenderSabers
    {
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

                // sabers point along +Z; lay them next to each other, blades up
                Transform left = root.transform.Find("LeftSaber"), right = root.transform.Find("RightSaber");
                if (left) { left.position = new Vector3(-0.15f, 0, 0); left.rotation = Quaternion.Euler(-90, 0, 0); }
                if (right) { right.position = new Vector3(0.15f, 0, 0); right.rotation = Quaternion.Euler(-90, 0, 0); }
                foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(1.0f, true, true);

                Renderer[] rs = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
                Bounds bounds = rs.Length > 0 ? rs[0].bounds : new Bounds(Vector3.zero, Vector3.one);
                foreach (Renderer r in rs) bounds.Encapsulate(r.bounds);

                GameObject camGo = new GameObject("Cam");
                Camera cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.08f, 0.08f, 0.1f, 1);
                cam.fieldOfView = 30;
                cam.nearClipPlane = 0.01f;
                float size = Mathf.Max(bounds.size.x, bounds.size.y) * 1.2f;
                camGo.transform.position = bounds.center + new Vector3(0, 0, -size / (2 * Mathf.Tan(15 * Mathf.Deg2Rad)));
                camGo.transform.LookAt(bounds.center);

                GameObject lightGo = new GameObject("Light");
                Light l = lightGo.AddComponent<Light>();
                l.type = LightType.Directional;
                lightGo.transform.rotation = Quaternion.Euler(30, 30, 0);

                RenderTexture rt = new RenderTexture(512, 768, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(512, 768, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 512, 768), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, Path.GetFileName(file) + ".png"), tex.EncodeToPNG());
                b.Unload(true);
            }
        }
    }
}
