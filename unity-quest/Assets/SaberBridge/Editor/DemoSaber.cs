using System;
using System.IO;
using CustomSaber;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SaberBridge
{
    // Development aid: builds "SaberBridge Demo.saber", a simple CC0 PC saber for README screenshots and tests.
    //   Unity.exe -batchmode -quit -projectPath unity-quest -executeMethod SaberBridge.DemoSaber.Run -out <file.saber>
    public static class DemoSaber
    {
        private const string Dir = "Assets/SaberBridge_Demo";

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string outFile = args[Array.IndexOf(args, "-out") + 1];
            string Arg(string k, string def) => Array.IndexOf(args, k) is int i && i >= 0 ? args[i + 1] : def;
            string name = Arg("-name", "SaberBridge Demo");
            string cover = Arg("-cover", "Assets/SaberBridge/Dev/DemoCover.png");
            float len = float.Parse(Arg("-len", "0.94"), System.Globalization.CultureInfo.InvariantCulture);
            float width = float.Parse(Arg("-width", "0.028"), System.Globalization.CultureInfo.InvariantCulture);
            float guard = float.Parse(Arg("-guard", "0.075"), System.Globalization.CultureInfo.InvariantCulture);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
            AssetDatabase.CreateFolder("Assets", "SaberBridge_Demo");

            Mesh cylinder = SaveMesh(PrimitiveType.Cylinder, "cylinder");
            Material hilt = SaveMat(Shader.Find("Standard"), "hilt", new Color(0.16f, 0.17f, 0.21f));
            hilt.SetFloat("_Metallic", 0.8f);
            hilt.SetFloat("_Glossiness", 0.6f);
            Material accent = SaveMat(Shader.Find("Standard"), "accent", new Color(0.75f, 0.78f, 0.86f));
            Material bladeL = SaveMat(Shader.Find("SaberBridgeDemo/Glow"), "blade left", new Color(1f, 0.24f, 0.37f));
            Material bladeR = SaveMat(Shader.Find("SaberBridgeDemo/Glow"), "blade right", new Color(0.22f, 0.57f, 1f));
            Material trail = SaveMat(Shader.Find("SaberBridgeDemo/Glow"), "trail", Color.white);

            GameObject root = new GameObject("_CustomSaber");
            SaberDescriptor d = root.AddComponent<SaberDescriptor>();
            d.SaberName = name;
            d.AuthorName = "SaberBridge (CC0)";
            d.Description = "Simple demo saber for testing SaberBridge. Public domain (CC0).";
            AssetDatabase.ImportAsset(cover, ImportAssetOptions.ForceUpdate);
            TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(cover);
            ti.textureType = TextureImporterType.Sprite;
            ti.isReadable = true;
            ti.SaveAndReimport();
            d.CoverImage = AssetDatabase.LoadAssetAtPath<Sprite>(cover);

            foreach (bool left in new[] { true, false })
            {
                GameObject saber = new GameObject(left ? "LeftSaber" : "RightSaber");
                saber.transform.SetParent(root.transform, false);
                saber.transform.localPosition = new Vector3(left ? -0.3f : 0.3f, 0, 0);

                // Unity's cylinder is 2 units tall along Y; sabers point along +Z
                Part(saber, cylinder, hilt, new Vector3(0, 0, -0.06f), new Vector3(0.045f, 0.13f, 0.045f));
                Part(saber, cylinder, accent, new Vector3(0, 0, -0.17f), new Vector3(0.05f, 0.02f, 0.05f));
                Part(saber, cylinder, accent, new Vector3(0, 0, 0.07f), new Vector3(guard, 0.012f, guard));
                Part(saber, cylinder, left ? bladeL : bladeR, new Vector3(0, 0, 0.08f + len / 2), new Vector3(width, len / 2, width));

                Transform bottom = new GameObject("TrailStart").transform;
                bottom.SetParent(saber.transform, false);
                bottom.localPosition = new Vector3(0, 0, 0.1f);
                Transform top = new GameObject("TrailEnd").transform;
                top.SetParent(saber.transform, false);
                top.localPosition = new Vector3(0, 0, 0.08f + len);

                CustomTrail t = saber.AddComponent<CustomTrail>();
                t.PointStart = bottom;
                t.PointEnd = top;
                t.TrailMaterial = trail;
                t.colorType = left ? ColorType.LeftSaber : ColorType.RightSaber;
                t.TrailColor = Color.white;
                t.MultiplierColor = Color.white;
                t.Length = 14;
            }

            string prefabPath = Dir + "/_CustomSaber.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);

            string tmp = Path.Combine(Path.GetTempPath(), "SaberBridgeDemo");
            Directory.CreateDirectory(tmp);
            AssetBundleBuild b = new AssetBundleBuild { assetBundleName = "demo.saber", assetNames = new[] { prefabPath } };
            BuildPipeline.BuildAssetBundles(tmp, new[] { b }, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
            Directory.CreateDirectory(Path.GetDirectoryName(outFile));
            File.Copy(Path.Combine(tmp, "demo.saber"), outFile, true);
            Directory.Delete(tmp, true);
            AssetDatabase.DeleteAsset(Dir);
            Debug.Log("[SB] OK demo saber -> " + outFile);
        }

        private static void Part(GameObject saber, Mesh mesh, Material mat, Vector3 pos, Vector3 scale)
        {
            GameObject go = new GameObject(mat.name);
            go.transform.SetParent(saber.transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(90, 0, 0);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static Mesh SaveMesh(PrimitiveType type, string name)
        {
            GameObject tmp = GameObject.CreatePrimitive(type);
            Mesh m = UnityEngine.Object.Instantiate(tmp.GetComponent<MeshFilter>().sharedMesh);
            UnityEngine.Object.DestroyImmediate(tmp);
            AssetDatabase.CreateAsset(m, $"{Dir}/{name}.asset");
            return m;
        }

        private static Material SaveMat(Shader shader, string name, Color color)
        {
            Material m = new Material(shader) { name = name, color = color };
            AssetDatabase.CreateAsset(m, $"{Dir}/{name}.mat");
            return m;
        }
    }
}
