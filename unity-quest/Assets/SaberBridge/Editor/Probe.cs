using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SaberBridge
{
    // Lists what is inside .saber / .whacker bundles: scripts they reference and the object tree with components.
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
                foreach (MonoScript s in b.LoadAllAssets<MonoScript>())
                {
                    SerializedObject so = new SerializedObject(s);
                    lines.Add($"   script {so.FindProperty("m_Namespace")?.stringValue}.{so.FindProperty("m_ClassName")?.stringValue} in {so.FindProperty("m_AssemblyName")?.stringValue}");
                }
                foreach (GameObject go in b.LoadAllAssets<GameObject>().Where(g => g.transform.parent == null))
                    Dump(go.transform, "   ", lines);
                b.Unload(true);
            }

            File.WriteAllLines(outFile, lines);
        }

        static void Dump(Transform t, string indent, List<string> lines)
        {
            var comps = t.GetComponents<Component>().Select(c => c == null ? "<missing script>" : c.GetType().Name + (c is UnityEngine.UI.Text tx ? "{" + tx.text.Replace("\n", " ") + "}" : ""));
            lines.Add(indent + t.name + "  [" + string.Join(", ", comps) + "]");
            foreach (Transform c in t) Dump(c, indent + "  ", lines);
        }
    }
}
