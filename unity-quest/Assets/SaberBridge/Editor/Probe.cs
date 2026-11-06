using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SaberBridge
{
    // Development aid: lists what is inside .saber / .whacker bundles (object tree, components, materials, script fields).
    //   Unity.exe -batchmode -quit -projectPath unity-quest -executeMethod SaberBridge.Probe.Run -dir <folder> -out <file>
    public static class Probe
    {
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string dir = args[Array.IndexOf(args, "-dir") + 1];
            string outFile = args[Array.IndexOf(args, "-out") + 1];
            List<string> lines = new List<string>();

            foreach (string file in Directory.GetFiles(dir))
            {
                AssetBundle b = AssetBundle.LoadFromFile(file);
                if (b == null) { lines.Add("== " + Path.GetFileName(file) + ": not a bundle"); continue; }
                lines.Add("== " + Path.GetFileName(file) + "  assets: " + string.Join(", ", b.GetAllAssetNames()));
                foreach (GameObject go in b.LoadAllAssets<GameObject>().Where(g => g.transform.parent == null))
                    Dump(go.transform, "   ", lines);
                b.Unload(true);
            }

            File.WriteAllLines(outFile, lines);
        }

        private static string Detail(Component c)
        {
            if (c is Text tx)
            {
                string text = tx.text.Replace("\n", " ");
                return "{" + text.Substring(0, Math.Min(70, text.Length)) + " | mat=" + (tx.material ? tx.material.name + "/" + tx.material.shader.name : "-") + "}";
            }

            if (c is MeshFilter mf && mf.sharedMesh)
            {
                Mesh mesh = mf.sharedMesh;
                string real = "";
                if (mesh.isReadable)
                {
                    // bounds from the actual vertex positions, to catch empty or zeroed vertex data
                    Vector3[] v = mesh.vertices;
                    Bounds b = new Bounds(v.Length > 0 ? v[0] : Vector3.zero, Vector3.zero);
                    foreach (Vector3 p in v) b.Encapsulate(p);
                    real = " realSize=" + b.size + " tris=" + mesh.triangles.Length / 3;
                }

                return "{" + mesh.name + " v" + mesh.vertexCount + " readable=" + mesh.isReadable + " size=" + mesh.bounds.size + real + "}";
            }

            if (c is Renderer r)
            {
                return "{" + string.Join(",", r.sharedMaterials.Select(m => m == null ? "-" :
                    m.name + "/" + (m.shader ? m.shader.name : "?") + "/q" + m.renderQueue + (m.mainTexture ? "/tex " + m.mainTexture.width + "x" + m.mainTexture.height : "/notex"))) + "}";
            }

            if (c is MonoBehaviour mb)
            {
                SerializedObject so = new SerializedObject(mb);
                SerializedProperty it = so.GetIterator();
                List<string> fields = new List<string>();
                bool enter = true;
                while (it.NextVisible(enter))
                {
                    enter = false;
                    if (it.name == "m_Script") continue;
                    string v;
                    switch (it.propertyType)
                    {
                        case SerializedPropertyType.ObjectReference: v = it.objectReferenceValue ? it.objectReferenceValue.name : "null"; break;
                        case SerializedPropertyType.Color: v = it.colorValue.ToString(); break;
                        case SerializedPropertyType.Integer: v = it.intValue.ToString(); break;
                        case SerializedPropertyType.Enum: v = it.enumValueIndex.ToString(); break;
                        case SerializedPropertyType.String: v = it.stringValue; break;
                        default: v = it.propertyType.ToString(); break;
                    }

                    fields.Add(it.name + "=" + v);
                }

                return "{" + string.Join("; ", fields) + "}";
            }

            return "";
        }

        private static void Dump(Transform t, string indent, List<string> lines)
        {
            IEnumerable<string> comps = t.GetComponents<Component>().Select(c => c == null ? "<missing script>" : c.GetType().Name + Detail(c));
            lines.Add(indent + t.name + "  [" + string.Join(", ", comps) + "]");
            foreach (Transform child in t) Dump(child, indent + "  ", lines);
        }
    }
}
