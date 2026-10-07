using System.Collections.Generic;
using UnityEngine;
using Vexa.Core;

namespace Vexa.Client
{
    /// <summary>
    /// Builds placeholder visuals for a map from its collision boxes (one merged mesh per material).
    /// Final maps will be real art; collision will still come from the exported .vxmap so client
    /// prediction and the server always agree.
    /// </summary>
    public static class MapBuilder
    {
        static readonly Dictionary<SurfaceMaterial, Color> Colors = new Dictionary<SurfaceMaterial, Color>
        {
            { SurfaceMaterial.Concrete, new Color(0.62f, 0.61f, 0.58f) },
            { SurfaceMaterial.Plaster, new Color(0.84f, 0.76f, 0.6f) },
            { SurfaceMaterial.Brick, new Color(0.66f, 0.45f, 0.33f) },
            { SurfaceMaterial.Wood, new Color(0.6f, 0.43f, 0.24f) },
            { SurfaceMaterial.Metal, new Color(0.25f, 0.42f, 0.55f) },
            { SurfaceMaterial.Sand, new Color(0.78f, 0.66f, 0.46f) },
            { SurfaceMaterial.Tile, new Color(0.7f, 0.62f, 0.5f) },
            { SurfaceMaterial.Glass, new Color(0.7f, 0.85f, 0.95f, 0.35f) },
        };

        public static Material MakeMaterial(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Sprites/Default"); // always included in builds
            var m = new Material(sh);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            return m;
        }

        public static GameObject Build(MapData map)
        {
            var root = new GameObject("Map_" + map.Name);
            var groups = new Dictionary<SurfaceMaterial, List<StaticBox>>();
            foreach (var b in map.Boxes)
            {
                if (!groups.TryGetValue(b.Material, out var list)) groups[b.Material] = list = new List<StaticBox>();
                list.Add(b);
            }
            foreach (var kv in groups)
            {
                var go = new GameObject(kv.Key.ToString());
                go.transform.SetParent(root.transform, false);
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mf.sharedMesh = BuildMesh(kv.Value);
                mr.sharedMaterial = MakeMaterial(Colors.TryGetValue(kv.Key, out var c) ? c : Color.gray);
            }
            return root;
        }

        static Mesh BuildMesh(List<StaticBox> boxes)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            foreach (var b in boxes)
            {
                Vector3 mn = b.Min.ToU(), mx = b.Max.ToU();
                AddFace(v, n, uv, tri, new Vector3(mn.x, mx.y, mn.z), new Vector3(mn.x, mx.y, mx.z), new Vector3(mx.x, mx.y, mx.z), new Vector3(mx.x, mx.y, mn.z), Vector3.up);
                AddFace(v, n, uv, tri, new Vector3(mn.x, mn.y, mx.z), new Vector3(mn.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mx.z), Vector3.down);
                AddFace(v, n, uv, tri, new Vector3(mn.x, mn.y, mn.z), new Vector3(mn.x, mx.y, mn.z), new Vector3(mx.x, mx.y, mn.z), new Vector3(mx.x, mn.y, mn.z), Vector3.back);
                AddFace(v, n, uv, tri, new Vector3(mx.x, mn.y, mx.z), new Vector3(mx.x, mx.y, mx.z), new Vector3(mn.x, mx.y, mx.z), new Vector3(mn.x, mn.y, mx.z), Vector3.forward);
                AddFace(v, n, uv, tri, new Vector3(mn.x, mn.y, mx.z), new Vector3(mn.x, mx.y, mx.z), new Vector3(mn.x, mx.y, mn.z), new Vector3(mn.x, mn.y, mn.z), Vector3.left);
                AddFace(v, n, uv, tri, new Vector3(mx.x, mn.y, mn.z), new Vector3(mx.x, mx.y, mn.z), new Vector3(mx.x, mx.y, mx.z), new Vector3(mx.x, mn.y, mx.z), Vector3.right);
            }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddFace(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tri, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            for (int k = 0; k < 4; k++) n.Add(normal);
            // world-space UVs so textures tile at a constant density later
            Vector2 U(Vector3 p) => Mathf.Abs(normal.y) > 0.5f ? new Vector2(p.x, p.z) : (Mathf.Abs(normal.x) > 0.5f ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y));
            uv.Add(U(a)); uv.Add(U(b)); uv.Add(U(c)); uv.Add(U(d));
            tri.Add(i); tri.Add(i + 1); tri.Add(i + 2); tri.Add(i); tri.Add(i + 2); tri.Add(i + 3);
        }
    }
}
