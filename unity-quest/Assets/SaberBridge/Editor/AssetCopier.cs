using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SaberBridge
{
    /// <summary>
    /// Turns the saber's bundle assets (meshes, materials, textures) into project assets, so the prefab can be rebuilt
    /// for Android. Shaders that only exist compiled for PC are swapped for SaberBridge/QuestSaber or QuestTrail,
    /// keeping colour, texture, emission, glow and the _CustomColors flag the game uses to tint sabers.
    /// </summary>
    internal sealed class AssetCopier
    {
        private readonly string _dir;
        private readonly Dictionary<Object, Object> _copies = new Dictionary<Object, Object>();
        private int _n;

        public int Meshes, Materials, ReplacedShaders;

        public AssetCopier(string dir) => _dir = dir;

        public void CopyRenderers(GameObject root)
        {
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh) mf.sharedMesh = CopyMesh(mf.sharedMesh);
            foreach (SkinnedMeshRenderer s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (s.sharedMesh) s.sharedMesh = CopyMesh(s.sharedMesh);
            foreach (ParticleSystemRenderer p in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (p.mesh) p.mesh = CopyMesh(p.mesh);
                if (p.trailMaterial) p.trailMaterial = CopyMaterial(p.trailMaterial, true);
            }

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                bool trail = r is TrailRenderer || r is LineRenderer;
                r.sharedMaterials = r.sharedMaterials.Select(m => m ? CopyMaterial(m, trail) : null).ToArray();
            }
        }

        private Mesh CopyMesh(Mesh m)
        {
            if (_copies.TryGetValue(m, out Object done)) return (Mesh)done;
            Mesh c = Object.Instantiate(m);
            c.name = m.name;
            AssetDatabase.CreateAsset(c, $"{_dir}/mesh_{_n++}.asset");
            _copies[m] = c;
            Meshes++;
            return c;
        }

        public Material CopyMaterial(Material m, bool trail)
        {
            if (_copies.TryGetValue(m, out Object done)) return (Material)done;

            // keep Unity's own shaders (Standard, Unlit, Particles...) when this project has them; replace the rest
            Shader local = m.shader != null ? Shader.Find(m.shader.name) : null;
            bool keep = local != null && local.isSupported && IsBuiltIn(local);
            Material c;
            if (keep)
            {
                c = new Material(m) { name = m.name, shader = local };
            }
            else
            {
                bool transparent = trail || m.renderQueue >= 2500;
                c = new Material(Shader.Find(transparent ? "SaberBridge/QuestTrail" : "SaberBridge/QuestSaber")) { name = m.name };
                Color color = Pick(m, Color.white, "_Color", "_TintColor", "_BaseColor", "_MainColor");
                c.SetColor("_Color", color);
                if (m.HasProperty("_EmissionColor") && c.HasProperty("_EmissionColor")) c.SetColor("_EmissionColor", m.GetColor("_EmissionColor"));
                foreach (string f in new[] { "_Glow", "_Bloom", "_CustomColors", "_Cull" })
                    if (m.HasProperty(f) && c.HasProperty(f)) c.SetFloat(f, m.GetFloat(f));
                CopyTex(m, c, "_MainTex", "_MainTex", "_BaseMap", "_Tex", "_Texture");
                CopyTex(m, c, "_EmissionMap", "_EmissionMap", "_EmissionTex", "_GlowTex");
                ReplacedShaders++;
            }

            if (keep)
            {
                foreach (string prop in TextureProps(c.shader))
                {
                    Texture t = c.GetTexture(prop);
                    if (t) c.SetTexture(prop, CopyTexture(t));
                }
            }

            AssetDatabase.CreateAsset(c, $"{_dir}/mat_{_n++}.mat");
            _copies[m] = c;
            Materials++;
            return c;
        }

        private static bool IsBuiltIn(Shader s) => AssetDatabase.GetAssetPath(s).StartsWith("Resources/unity_builtin_extra") || s.name.StartsWith("SaberBridge/");

        private static Color Pick(Material m, Color fallback, params string[] names)
        {
            foreach (string n in names) if (m.HasProperty(n)) return m.GetColor(n);
            return fallback;
        }

        private void CopyTex(Material from, Material to, string target, params string[] names)
        {
            foreach (string n in names)
            {
                if (!from.HasProperty(n) || from.GetTexture(n) == null) continue;
                to.SetTexture(target, CopyTexture(from.GetTexture(n)));
                to.SetTextureScale(target, from.GetTextureScale(n));
                to.SetTextureOffset(target, from.GetTextureOffset(n));
                return;
            }
        }

        private static IEnumerable<string> TextureProps(Shader s)
        {
            for (int i = 0; i < ShaderUtil.GetPropertyCount(s); i++)
                if (ShaderUtil.GetPropertyType(s, i) == ShaderUtil.ShaderPropertyType.TexEnv) yield return ShaderUtil.GetPropertyName(s, i);
        }

        // textures come out of the bundle compressed for PC; read them back through the GPU and re-import as PNG
        private Texture CopyTexture(Texture t)
        {
            if (_copies.TryGetValue(t, out Object done)) return (Texture)done;
            if (!(t is Texture2D)) return t;

            Texture2D readable = SaberToWhacker.Readable(t, new Rect(0, 0, t.width, t.height));
            string path = $"{_dir}/tex_{_n++}.png";
            File.WriteAllBytes(path, readable.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = t.wrapMode;
            imp.filterMode = t.filterMode;
            imp.maxTextureSize = Mathf.Min(2048, Mathf.NextPowerOfTwo(Mathf.Max(t.width, t.height)));
            imp.SaveAndReimport();
            Texture2D c = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            _copies[t] = c;
            return c;
        }
    }
}
