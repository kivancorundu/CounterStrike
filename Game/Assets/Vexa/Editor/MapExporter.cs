using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vexa.Client.Authoring;
using Vexa.Core;

namespace Vexa.EditorTools
{
    /// <summary>
    /// Exports the open scene's collision into StreamingAssets/Maps/&lt;scene&gt;.vxmap.
    /// Every non-trigger BoxCollider becomes a collision box (must be axis-aligned: only 90° Y rotations),
    /// VexaSpawn objects become spawns and VexaZone objects become bomb sites / buy zones.
    /// </summary>
    public static class MapExporter
    {
        [MenuItem("VEXA/Haritayı Dışa Aktar (.vxmap)")]
        public static void Export()
        {
            var scene = SceneManager.GetActiveScene();
            var map = new MapData { Name = string.IsNullOrEmpty(scene.name) ? "untitled" : scene.name.ToLowerInvariant() };
            int warnings = 0;

            foreach (var col in Object.FindObjectsOfType<BoxCollider>())
            {
                if (!col.enabled || col.isTrigger) continue;
                if (col.GetComponent<VexaZone>() != null) continue;
                var e = col.transform.rotation.eulerAngles;
                bool aligned = Mathf.Abs(Mathf.DeltaAngle(e.x, 0)) < 0.01f && Mathf.Abs(Mathf.DeltaAngle(e.z, 0)) < 0.01f && Mathf.Abs(Mathf.DeltaAngle(e.y, Mathf.Round(e.y / 90f) * 90f)) < 0.01f;
                if (!aligned)
                {
                    warnings++;
                    Debug.LogWarning($"[VEXA] '{col.name}' döndürülmüş; çarpışma kutusu eksen hizalı sınırlarla dışa aktarıldı.", col);
                }
                var b = col.bounds;
                var surf = col.GetComponent<VexaSurface>();
                map.Boxes.Add(new StaticBox
                {
                    Min = new System.Numerics.Vector3(b.min.x, b.min.y, b.min.z),
                    Max = new System.Numerics.Vector3(b.max.x, b.max.y, b.max.z),
                    Material = surf != null ? surf.Material : SurfaceMaterial.Concrete,
                });
            }

            foreach (var sp in Object.FindObjectsOfType<VexaSpawn>())
            {
                var p = sp.transform.position;
                map.Spawns.Add(new SpawnPoint { Team = sp.Team, Position = new System.Numerics.Vector3(p.x, p.y, p.z), Yaw = sp.transform.eulerAngles.y });
            }

            foreach (var z in Object.FindObjectsOfType<VexaZone>())
            {
                var b = z.GetComponent<BoxCollider>().bounds;
                var rect = new ZoneRect { Name = z.Kind == ZoneKind.BombSite ? z.SiteName : z.BuyTeam.ToString(), MinX = b.min.x, MinZ = b.min.z, MaxX = b.max.x, MaxZ = b.max.z };
                if (z.Kind == ZoneKind.BombSite) map.Sites.Add(rect);
                else map.BuyZones.Add((z.BuyTeam, rect));
            }

            var dir = Path.Combine(Application.streamingAssetsPath, "Maps");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, map.Name + ".vxmap");
            File.WriteAllText(path, map.Serialize());
            AssetDatabase.Refresh();
            Debug.Log($"[VEXA] {map.Boxes.Count} kutu, {map.Spawns.Count} doğuş noktası, {map.Sites.Count} bölge -> {path} ({warnings} uyarı)");
            EditorUtility.DisplayDialog("VEXA", $"Harita dışa aktarıldı:\n{path}\n\n{map.Boxes.Count} çarpışma kutusu, {map.Spawns.Count} doğuş noktası.\n{warnings} uyarı (Console'a bak).", "Tamam");
        }

        [MenuItem("VEXA/Test Haritasını Sahneye Yükle")]
        public static void ImportTraining()
        {
            var path = Path.Combine(Application.streamingAssetsPath, "Maps", "training.vxmap");
            var map = MapData.Parse(File.ReadAllText(path));
            var root = new GameObject("training (imported)");
            foreach (var b in map.Boxes)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(root.transform);
                var mn = new Vector3(b.Min.X, b.Min.Y, b.Min.Z); var mx = new Vector3(b.Max.X, b.Max.Y, b.Max.Z);
                go.transform.position = (mn + mx) * 0.5f;
                go.transform.localScale = mx - mn;
                go.AddComponent<VexaSurface>().Material = b.Material;
                go.name = b.Material.ToString();
            }
            foreach (var s in map.Spawns)
            {
                var go = new GameObject("Spawn " + s.Team);
                go.transform.SetParent(root.transform);
                go.transform.position = new Vector3(s.Position.X, s.Position.Y, s.Position.Z);
                go.transform.rotation = Quaternion.Euler(0, s.Yaw, 0);
                go.AddComponent<VexaSpawn>().Team = s.Team;
            }
            Undo.RegisterCreatedObjectUndo(root, "Import training map");
        }
    }
}
