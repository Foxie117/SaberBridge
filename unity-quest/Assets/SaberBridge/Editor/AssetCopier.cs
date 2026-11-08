using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

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
            Mesh c = m.isReadable ? Object.Instantiate(m) : CopyUnreadable(m);
            c.name = m.name;
            AssetDatabase.CreateAsset(c, $"{_dir}/mesh_{_n++}.asset");
            _copies[m] = c;
            Meshes++;
            return c;
        }

        // Meshes saved without "Read/Write" keep no CPU copy once loaded from a bundle, and their GPU buffers cannot be
        // read back. Drawing them once with Hidden/SaberBridge/MeshCapture writes every vertex and triangle into buffers.
        private const int CapturedStride = (3 + 3 + 4 + 4 + 4 + 4) * 4;

        private static Mesh CopyUnreadable(Mesh m)
        {
            Material capture = new Material(Shader.Find("Hidden/SaberBridge/MeshCapture"));
            RenderTexture rt = RenderTexture.GetTemporary(16, 16, 0);
            int n = m.vertexCount;
            float[] raw = new float[n * CapturedStride / 4];
            List<int[]> subTriangles = new List<int[]>();

            using (ComputeBuffer verts = new ComputeBuffer(Mathf.Max(1, n), CapturedStride))
            {
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    SubMeshDescriptor sub = m.GetSubMesh(s);
                    int triCount = sub.topology == MeshTopology.Triangles ? sub.indexCount / 3 : 0;
                    using (ComputeBuffer tris = new ComputeBuffer(Mathf.Max(1, triCount * 3), 4))
                    {
                        Graphics.SetRenderTarget(rt);
                        Graphics.ClearRandomWriteTargets();
                        Graphics.SetRandomWriteTarget(1, verts, false);
                        Graphics.SetRandomWriteTarget(2, tris, false);
                        capture.SetPass(0);
                        Graphics.DrawMeshNow(m, Matrix4x4.identity, s);
                        Graphics.ClearRandomWriteTargets();

                        int[] t = new int[triCount * 3];
                        if (t.Length > 0) tris.GetData(t);
                        subTriangles.Add(t);
                    }
                }

                verts.GetData(raw);
            }

            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(capture);

            Vector3[] pos = new Vector3[n], nrm = new Vector3[n];
            Vector4[] tan = new Vector4[n];
            List<Vector4> uv0 = new List<Vector4>(n), uv1 = new List<Vector4>(n);
            Color[] col = new Color[n];
            for (int i = 0, k = 0; i < n; i++, k += CapturedStride / 4)
            {
                pos[i] = new Vector3(raw[k], raw[k + 1], raw[k + 2]);
                nrm[i] = new Vector3(raw[k + 3], raw[k + 4], raw[k + 5]);
                tan[i] = new Vector4(raw[k + 6], raw[k + 7], raw[k + 8], raw[k + 9]);
                uv0.Add(new Vector4(raw[k + 10], raw[k + 11], raw[k + 12], raw[k + 13]));
                uv1.Add(new Vector4(raw[k + 14], raw[k + 15], raw[k + 16], raw[k + 17]));
                col[i] = new Color(raw[k + 18], raw[k + 19], raw[k + 20], raw[k + 21]);
            }

            Mesh c = new Mesh { indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            c.vertices = pos;
            if (m.HasVertexAttribute(VertexAttribute.Normal)) c.normals = nrm;
            if (m.HasVertexAttribute(VertexAttribute.Tangent)) c.tangents = tan;
            if (m.HasVertexAttribute(VertexAttribute.TexCoord0)) c.SetUVs(0, uv0);
            if (m.HasVertexAttribute(VertexAttribute.TexCoord1)) c.SetUVs(1, uv1);
            if (m.HasVertexAttribute(VertexAttribute.Color)) c.colors = col;
            c.subMeshCount = subTriangles.Count;
            for (int s = 0; s < subTriangles.Count; s++) c.SetTriangles(subTriangles[s], s, false);
            c.bounds = m.bounds;
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
                // the render queue alone is not reliable (opaque metal parts sit in queue 3000 in many sabers);
                // a material is treated as see-through only when the background actually shows through it
                bool transparent = trail || (m.renderQueue >= 2500 && SeeThrough(m));
                c = new Material(Shader.Find(transparent ? "SaberBridge/QuestTrail" : "SaberBridge/QuestSaber")) { name = m.name };
                Color color = Pick(m, Color.white, "_Color", "_TintColor", "_BaseColor", "_MainColor");
                c.SetColor("_Color", color);
                if (m.HasProperty("_EmissionColor") && c.HasProperty("_EmissionColor")) c.SetColor("_EmissionColor", m.GetColor("_EmissionColor"));
                foreach (string f in new[] { "_Glow", "_Bloom", "_CustomColors", "_Cull" })
                    if (m.HasProperty(f) && c.HasProperty(f)) c.SetFloat(f, m.GetFloat(f));
                CopyTex(m, c, "_MainTex", MainTextureNames);
                CopyTex(m, c, "_EmissionMap", "_EmissionMap", "_EmissionTex", "_GlowTex");

                // an effect whose look came only from a procedural PC shader would turn into a solid sheet; keep it a faint glow
                if (transparent && !trail && c.GetTexture("_MainTex") == null)
                {
                    color.a *= 0.3f;
                    c.SetColor("_Color", color);
                }

                if (!transparent) UseMatCap(c, m, m.HasProperty("_CustomColors") && m.GetFloat("_CustomColors") > 0);
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

            Debug.Log($"[SB] MAT {m.name} | {(m.shader ? m.shader.name : "?")} q{m.renderQueue} -> {c.shader.name}{(c.HasProperty("_UseMatCap") && c.GetFloat("_UseMatCap") > 0 ? " +look" : "")}");
            AssetDatabase.CreateAsset(c, $"{_dir}/mat_{_n++}.mat");
            _copies[m] = c;
            _sourceOf[c] = m;
            Materials++;
            return c;
        }

        private readonly Dictionary<Material, Material> _sourceOf = new Dictionary<Material, Material>();

        // PC shaders still render in the editor, so their look (shine, reflections, dark or bright parts) is captured
        // once on a sphere and replayed on the Quest by view direction. With the player's colour the capture is taken
        // in white and tinted in game; otherwise the material's own colour is part of the capture.
        private void UseMatCap(Material quest, Material pc, bool playerColour)
        {
            Color[] white = Capture(pc, Color.white), black = Capture(pc, Color.black);
            if (white == null || black == null) return;
            Color[] tint = new Color[white.Length];
            float sum = 0;
            for (int i = 0; i < white.Length; i++)
            {
                tint[i] = new Color(Mathf.Max(0, white[i].r - black[i].r), Mathf.Max(0, white[i].g - black[i].g), Mathf.Max(0, white[i].b - black[i].b));
                sum += white[i].r + white[i].g + white[i].b;
            }

            // a shader that drew nothing (the background shows inside the sphere) gives no useful capture;
            // a dark material is fine, it just captures dark
            Color[] onWhite = Capture(pc, Color.white, Color.white);
            if (InsideDiff(white, Color.black) < 0.02f && InsideDiff(onWhite, Color.white) < 0.02f) return;
            quest.SetTexture("_MatCap", SaveCap(tint));
            quest.SetTexture("_MatCapBase", SaveCap(black));
            quest.SetFloat("_UseMatCap", 1);
            quest.SetFloat("_Shade", 0);
            if (pc.HasProperty("_Color")) quest.SetColor("_Color", pc.GetColor("_Color"));
        }

        private Texture2D SaveCap(Color[] px)
        {
            Texture2D tex = new Texture2D(CapSize, CapSize, TextureFormat.RGB24, false);
            tex.SetPixels(px);
            tex.Apply();
            string path = $"{_dir}/matcap_{_n++}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = false;
            imp.sRGBTexture = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // where PC shaders keep their colour texture
        private static readonly string[] MainTextureNames = { "_MainTex", "_BaseMap", "_DiffTex", "_Diffuse", "_Albedo", "_AlbedoTex", "_MainTexture", "_BaseColorMap", "_ColorTex", "_Tex", "_Texture" };

        private const int CapSize = 128;
        private const int CapLayer = 31;

        // renders the PC material on a sphere (orthographic, so each pixel is one view-space normal)
        // average colour distance from a background colour, inside the sphere only
        private static float InsideDiff(Color[] px, Color bg)
        {
            if (px == null) return 0;
            float diff = 0;
            int n = 0;
            for (int y = 0; y < CapSize; y++)
            for (int x = 0; x < CapSize; x++)
            {
                float dx = x - CapSize / 2f + 0.5f, dy = y - CapSize / 2f + 0.5f;
                if (dx * dx + dy * dy > CapSize * CapSize * 0.2f) continue;
                Color a = px[y * CapSize + x];
                diff += Mathf.Abs(a.r - bg.r) + Mathf.Abs(a.g - bg.g) + Mathf.Abs(a.b - bg.b);
                n++;
            }

            return n == 0 ? 0 : diff / n;
        }

        // How much of the background shows through the middle of the sphere (0 = opaque, 1 = invisible). The rim is
        // left out: many opaque-looking shaders fade their edges.
        private static bool SeeThrough(Material pc)
        {
            Color[] onBlack = Capture(pc, null, Color.black), onWhite = Capture(pc, null, Color.white);
            if (onBlack == null || onWhite == null) return true;
            float through = 0;
            int n = 0;
            for (int y = 0; y < CapSize; y++)
            for (int x = 0; x < CapSize; x++)
            {
                float dx = x - CapSize / 2f + 0.5f, dy = y - CapSize / 2f + 0.5f;
                if (dx * dx + dy * dy > CapSize * CapSize * 0.09f) continue;
                Color a = onBlack[y * CapSize + x], b = onWhite[y * CapSize + x];
                through += ((b.r - a.r) + (b.g - a.g) + (b.b - a.b)) / 3f;
                n++;
            }

            return n == 0 || through / n > 0.25f;
        }

        private static Color[] Capture(Material pc, Color? color, Color? background = null)
        {
            if (pc == null || pc.shader == null || !pc.shader.isSupported) return null;
            Material probe = new Material(pc);
            if (color.HasValue && probe.HasProperty("_Color")) probe.SetColor("_Color", color.Value);
            // the texture is applied by the Quest shader through the mesh's own UVs, so it must not be baked in
            foreach (string n in MainTextureNames) if (probe.HasProperty(n)) probe.SetTexture(n, null);

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.layer = CapLayer;
            sphere.GetComponent<Renderer>().sharedMaterial = probe;
            sphere.transform.position = new Vector3(0, -1000, 0);

            GameObject camGo = new GameObject("MatCapCamera");
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 0.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background ?? Color.black;
            cam.cullingMask = 1 << CapLayer;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 10;
            camGo.transform.position = sphere.transform.position + new Vector3(0, 0, -2);

            GameObject lightGo = new GameObject("MatCapLight");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << CapLayer;
            lightGo.transform.rotation = Quaternion.Euler(35, 25, 0);

            RenderTexture rt = RenderTexture.GetTemporary(CapSize, CapSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(CapSize, CapSize, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, CapSize, CapSize), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
            Object.DestroyImmediate(sphere);
            Object.DestroyImmediate(probe);
            Color[] result = tex.GetPixels();
            Object.DestroyImmediate(tex);
            return result;
        }

        /// <summary>
        /// Qosmetics tints a material with the player's note colour when its _CustomColors is on. Returns a material
        /// (made once per source and setting) that has it on or off; Unity's own shaders have no such switch, so a
        /// material that must be tinted moves to the Quest shader.
        /// </summary>
        public Material WithCustomColors(Material m, bool on)
        {
            if (m == null) return null;
            bool has = m.HasProperty("_CustomColors");
            if (has && (m.GetFloat("_CustomColors") > 0) == on) return m;
            if (!has && !on) return m;
            var key = (m, on);
            if (_variants.TryGetValue(key, out Material done)) return done;

            Material c;
            Material source = _sourceOf.TryGetValue(m, out Material src) ? src : m;
            if (has)
            {
                c = new Material(m) { name = m.name + (on ? " (cc)" : " (no cc)") };
            }
            else
            {
                c = new Material(Shader.Find("SaberBridge/QuestSaber")) { name = m.name + " (cc)" };
                c.SetColor("_Color", Color.white);
                if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex")) c.SetTexture("_MainTex", m.GetTexture("_MainTex"));
                c.SetFloat("_Glow", 0);
                ReplacedShaders++;
            }

            c.SetFloat("_CustomColors", on ? 1 : 0);
            if (c.shader.name == "SaberBridge/QuestSaber" && c.renderQueue < 2500 && c.GetFloat("_UseMatCap") < 0.5f) UseMatCap(c, source, on);
            AssetDatabase.CreateAsset(c, $"{_dir}/mat_{_n++}.mat");
            _variants[key] = c;
            Materials++;
            return c;
        }

        private readonly Dictionary<(Material, bool), Material> _variants = new Dictionary<(Material, bool), Material>();

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
