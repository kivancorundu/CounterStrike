using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Vexa.Core
{
    public enum Team : byte { None = 0, T = 1, CT = 2 }

    public struct SpawnPoint
    {
        public Team Team;       // None = deathmatch spawn
        public Vector3 Position;
        public float Yaw;
    }

    public struct ZoneRect
    {
        public string Name;
        public float MinX, MinZ, MaxX, MaxZ;
        public bool Contains(Vector3 p) => p.X >= MinX && p.X <= MaxX && p.Z >= MinZ && p.Z <= MaxZ;
    }

    /// <summary>
    /// Map description shared by server and client. Text format (".vxmap"), one entry per line:
    /// <code>
    /// name &lt;id&gt;
    /// box minX minY minZ maxX maxY maxZ material
    /// spawn T|CT|DM x y z yaw
    /// site A|B minX minZ maxX maxZ
    /// buyzone T|CT minX minZ maxX maxZ
    /// </code>
    /// Units are meters. Maps are authored in Unity and exported by the editor tool.
    /// </summary>
    public sealed class MapData
    {
        public string Name = "unnamed";
        public readonly List<StaticBox> Boxes = new List<StaticBox>();
        public readonly List<SpawnPoint> Spawns = new List<SpawnPoint>();
        public readonly List<ZoneRect> Sites = new List<ZoneRect>();
        public readonly List<(Team team, ZoneRect zone)> BuyZones = new List<(Team, ZoneRect)>();

        public CollisionWorld BuildCollision()
        {
            var w = new CollisionWorld();
            foreach (var b in Boxes) w.AddBox(b.Min, b.Max, b.Material);
            w.Build();
            return w;
        }

        public static MapData Parse(string text)
        {
            var m = new MapData();
            var ci = CultureInfo.InvariantCulture;
            int lineNo = 0;
            foreach (var raw in text.Split('\n'))
            {
                lineNo++;
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                float F(int i) => float.Parse(p[i], NumberStyles.Float, ci);
                try
                {
                    switch (p[0])
                    {
                        case "name": m.Name = p[1]; break;
                        case "box":
                            Surfaces.TryParse(p.Length > 7 ? p[7] : "concrete", out var mat);
                            m.Boxes.Add(new StaticBox { Min = Vector3.Min(new Vector3(F(1), F(2), F(3)), new Vector3(F(4), F(5), F(6))), Max = Vector3.Max(new Vector3(F(1), F(2), F(3)), new Vector3(F(4), F(5), F(6))), Material = mat });
                            break;
                        case "spawn":
                            m.Spawns.Add(new SpawnPoint { Team = ParseTeam(p[1]), Position = new Vector3(F(2), F(3), F(4)), Yaw = p.Length > 5 ? F(5) : 0f });
                            break;
                        case "site":
                            m.Sites.Add(new ZoneRect { Name = p[1], MinX = MathF.Min(F(2), F(4)), MinZ = MathF.Min(F(3), F(5)), MaxX = MathF.Max(F(2), F(4)), MaxZ = MathF.Max(F(3), F(5)) });
                            break;
                        case "buyzone":
                            m.BuyZones.Add((ParseTeam(p[1]), new ZoneRect { Name = p[1], MinX = MathF.Min(F(2), F(4)), MinZ = MathF.Min(F(3), F(5)), MaxX = MathF.Max(F(2), F(4)), MaxZ = MathF.Max(F(3), F(5)) }));
                            break;
                        default: throw new FormatException("unknown entry '" + p[0] + "'");
                    }
                }
                catch (Exception e) when (!(e is FormatException && e.Message.StartsWith("map line")))
                {
                    throw new FormatException($"map line {lineNo}: {e.Message}");
                }
            }
            return m;
        }

        public string Serialize()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("name ").Append(Name).Append('\n');
            foreach (var b in Boxes)
                sb.AppendFormat(ci, "box {0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} {6}\n", b.Min.X, b.Min.Y, b.Min.Z, b.Max.X, b.Max.Y, b.Max.Z, b.Material.ToString().ToLowerInvariant());
            foreach (var s in Spawns)
                sb.AppendFormat(ci, "spawn {0} {1:0.###} {2:0.###} {3:0.###} {4:0.#}\n", s.Team == Team.None ? "DM" : s.Team.ToString(), s.Position.X, s.Position.Y, s.Position.Z, s.Yaw);
            foreach (var z in Sites)
                sb.AppendFormat(ci, "site {0} {1:0.###} {2:0.###} {3:0.###} {4:0.###}\n", z.Name, z.MinX, z.MinZ, z.MaxX, z.MaxZ);
            foreach (var (t, z) in BuyZones)
                sb.AppendFormat(ci, "buyzone {0} {1:0.###} {2:0.###} {3:0.###} {4:0.###}\n", t, z.MinX, z.MinZ, z.MaxX, z.MaxZ);
            return sb.ToString();
        }

        private static Team ParseTeam(string s) => s == "T" ? Team.T : s == "CT" ? Team.CT : Team.None;

        public string SiteAt(Vector3 p)
        {
            foreach (var s in Sites) if (s.Contains(p)) return s.Name;
            return null;
        }
    }
}
