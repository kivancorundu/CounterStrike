using System;
using System.Collections.Generic;
using System.Numerics;

namespace Vexa.Core
{
    public struct StaticBox
    {
        public Vector3 Min, Max;
        public SurfaceMaterial Material;
    }

    public struct TraceResult
    {
        public float Fraction;     // 0..1 along the requested move
        public Vector3 EndCenter;  // box center at the end of the trace
        public Vector3 Normal;
        public bool StartSolid;
        public int BoxIndex;
        public bool Hit => Fraction < 1f;
    }

    public struct RayHit
    {
        public float Distance;      // entry distance
        public float ExitDistance;  // exit distance from the same box (for wall thickness)
        public Vector3 Point;
        public Vector3 Normal;
        public int BoxIndex;
        public SurfaceMaterial Material;
    }

    /// <summary>
    /// Static level collision made of axis-aligned boxes, with a uniform XZ grid for broad phase.
    /// Used identically by client prediction and the authoritative server, so movement results match.
    /// </summary>
    public sealed class CollisionWorld
    {
        public const float SurfaceEpsilon = 0.001f;
        private readonly List<StaticBox> _boxes = new List<StaticBox>();
        private List<int>[] _cells = Array.Empty<List<int>>();
        private int[] _stamp = Array.Empty<int>();
        private int _stampId;
        private float _cellSize = 4f;
        private int _gw, _gh;
        private float _ox, _oz;

        public int Count => _boxes.Count;
        public StaticBox GetBox(int i) => _boxes[i];
        public IReadOnlyList<StaticBox> Boxes => _boxes;

        public void AddBox(Vector3 min, Vector3 max, SurfaceMaterial mat)
        {
            _boxes.Add(new StaticBox { Min = Vector3.Min(min, max), Max = Vector3.Max(min, max), Material = mat });
        }

        public void Build(float cellSize = 4f)
        {
            _cellSize = cellSize;
            if (_boxes.Count == 0) { _gw = _gh = 1; _ox = _oz = 0; _cells = new[] { new List<int>() }; _stamp = new int[0]; return; }
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var b in _boxes)
            {
                minX = MathF.Min(minX, b.Min.X); minZ = MathF.Min(minZ, b.Min.Z);
                maxX = MathF.Max(maxX, b.Max.X); maxZ = MathF.Max(maxZ, b.Max.Z);
            }
            _ox = minX - cellSize; _oz = minZ - cellSize;
            _gw = (int)MathF.Ceiling((maxX - _ox) / cellSize) + 2;
            _gh = (int)MathF.Ceiling((maxZ - _oz) / cellSize) + 2;
            _cells = new List<int>[_gw * _gh];
            for (int i = 0; i < _cells.Length; i++) _cells[i] = new List<int>(4);
            for (int i = 0; i < _boxes.Count; i++)
            {
                var b = _boxes[i];
                int c0 = CellX(b.Min.X), c1 = CellX(b.Max.X), r0 = CellZ(b.Min.Z), r1 = CellZ(b.Max.Z);
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++) _cells[r * _gw + c].Add(i);
            }
            _stamp = new int[_boxes.Count];
        }

        private int CellX(float x) => VMath.Clamp((int)MathF.Floor((x - _ox) / _cellSize), 0, _gw - 1);
        private int CellZ(float z) => VMath.Clamp((int)MathF.Floor((z - _oz) / _cellSize), 0, _gh - 1);

        private void Gather(float minX, float minZ, float maxX, float maxZ, List<int> outList)
        {
            outList.Clear();
            _stampId++;
            if (_stampId == int.MaxValue) { Array.Clear(_stamp, 0, _stamp.Length); _stampId = 1; }
            int c0 = CellX(minX), c1 = CellX(maxX), r0 = CellZ(minZ), r1 = CellZ(maxZ);
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                    foreach (int i in _cells[r * _gw + c])
                        if (_stamp[i] != _stampId) { _stamp[i] = _stampId; outList.Add(i); }
        }

        [ThreadStatic] private static List<int> _tmp;
        private static List<int> Tmp => _tmp ?? (_tmp = new List<int>(64));

        /// <summary>Sweeps an axis-aligned box (center + half extents) from start to end.</summary>
        public TraceResult TraceBox(Vector3 start, Vector3 end, Vector3 half)
        {
            var res = new TraceResult { Fraction = 1f, EndCenter = end, BoxIndex = -1 };
            Vector3 d = end - start;
            var list = Tmp;
            Gather(MathF.Min(start.X, end.X) - half.X, MathF.Min(start.Z, end.Z) - half.Z,
                   MathF.Max(start.X, end.X) + half.X, MathF.Max(start.Z, end.Z) + half.Z, list);
            float best = 1f;
            Vector3 bestN = Vector3.Zero;
            int bestI = -1;
            foreach (int i in list)
            {
                var b = _boxes[i];
                Vector3 mn = b.Min - half, mx = b.Max + half;
                if (!SweepSlab(start, d, mn, mx, out float tEnter, out float tExit, out int axis, out bool inside)) continue;
                if (inside)
                {
                    // already overlapping: only block if the move pushes deeper into the box
                    Vector3 c = (b.Min + b.Max) * 0.5f;
                    if (Vector3.Dot(d, start - c) >= 0f) continue;
                    res.StartSolid = true;
                    continue;
                }
                if (tEnter < best)
                {
                    best = tEnter; bestI = i;
                    bestN = AxisNormal(axis, d);
                }
            }
            if (bestI >= 0)
            {
                float len = d.Length();
                float back = len > 1e-6f ? SurfaceEpsilon / len : 0f;
                float f = MathF.Max(0f, best - back);
                res.Fraction = f;
                res.EndCenter = start + d * f;
                res.Normal = bestN;
                res.BoxIndex = bestI;
            }
            return res;
        }

        private static Vector3 AxisNormal(int axis, Vector3 d)
        {
            switch (axis)
            {
                case 0: return new Vector3(d.X > 0 ? -1 : 1, 0, 0);
                case 1: return new Vector3(0, d.Y > 0 ? -1 : 1, 0);
                default: return new Vector3(0, 0, d.Z > 0 ? -1 : 1);
            }
        }

        // slab test for segment start + d*t, t in [0,1]
        private static bool SweepSlab(Vector3 s, Vector3 d, Vector3 mn, Vector3 mx, out float tEnter, out float tExit, out int axis, out bool inside)
        {
            tEnter = float.NegativeInfinity; tExit = float.PositiveInfinity; axis = -1; inside = false;
            for (int a = 0; a < 3; a++)
            {
                float sa = a == 0 ? s.X : a == 1 ? s.Y : s.Z;
                float da = a == 0 ? d.X : a == 1 ? d.Y : d.Z;
                float lo = a == 0 ? mn.X : a == 1 ? mn.Y : mn.Z;
                float hi = a == 0 ? mx.X : a == 1 ? mx.Y : mx.Z;
                if (MathF.Abs(da) < 1e-9f)
                {
                    if (sa <= lo || sa >= hi) return false;
                    continue;
                }
                float t1 = (lo - sa) / da, t2 = (hi - sa) / da;
                if (t1 > t2) { var tmp = t1; t1 = t2; t2 = tmp; }
                if (t1 > tEnter) { tEnter = t1; axis = a; }
                if (t2 < tExit) tExit = t2;
                if (tEnter > tExit) return false;
            }
            if (tExit <= 0f) return false;
            if (axis == -1)
            {
                // parallel on every axis and strictly inside
                inside = true; tEnter = 0; return true;
            }
            if (tEnter < 0f)
            {
                // the start point lies inside the box (beyond a tiny tolerance)
                if (tEnter < -1e-5f || IsStrictlyInside(s, mn, mx)) { inside = true; return true; }
                tEnter = 0f;
            }
            return tEnter <= 1f;
        }

        private static bool IsStrictlyInside(Vector3 p, Vector3 mn, Vector3 mx)
        {
            const float e = 1e-5f;
            return p.X > mn.X + e && p.X < mx.X - e && p.Y > mn.Y + e && p.Y < mx.Y - e && p.Z > mn.Z + e && p.Z < mx.Z - e;
        }

        public bool OverlapBox(Vector3 center, Vector3 half, out int boxIndex)
        {
            var list = Tmp;
            Gather(center.X - half.X, center.Z - half.Z, center.X + half.X, center.Z + half.Z, list);
            Vector3 mn = center - half, mx = center + half;
            const float e = 1e-4f;
            foreach (int i in list)
            {
                var b = _boxes[i];
                if (mn.X < b.Max.X - e && mx.X > b.Min.X + e && mn.Y < b.Max.Y - e && mx.Y > b.Min.Y + e && mn.Z < b.Max.Z - e && mx.Z > b.Min.Z + e)
                { boxIndex = i; return true; }
            }
            boxIndex = -1;
            return false;
        }

        public bool OverlapBox(Vector3 center, Vector3 half) => OverlapBox(center, half, out _);

        /// <summary>Push a box out of any static geometry it overlaps (minimal translation).</summary>
        public Vector3 Depenetrate(Vector3 center, Vector3 half)
        {
            for (int iter = 0; iter < 4; iter++)
            {
                if (!OverlapBox(center, half, out int i)) break;
                var b = _boxes[i];
                float px1 = b.Max.X - (center.X - half.X), px2 = (center.X + half.X) - b.Min.X;
                float py1 = b.Max.Y - (center.Y - half.Y), py2 = (center.Y + half.Y) - b.Min.Y;
                float pz1 = b.Max.Z - (center.Z - half.Z), pz2 = (center.Z + half.Z) - b.Min.Z;
                float best = py1; Vector3 dir = new Vector3(0, 1, 0);
                if (px1 < best) { best = px1; dir = new Vector3(1, 0, 0); }
                if (px2 < best) { best = px2; dir = new Vector3(-1, 0, 0); }
                if (pz1 < best) { best = pz1; dir = new Vector3(0, 0, 1); }
                if (pz2 < best) { best = pz2; dir = new Vector3(0, 0, -1); }
                if (py2 < best) { best = py2; dir = new Vector3(0, -1, 0); }
                center += dir * (best + SurfaceEpsilon);
            }
            return center;
        }

        /// <summary>Ray against static geometry; returns the nearest box with entry and exit distances.</summary>
        public bool Raycast(Vector3 origin, Vector3 dir, float maxDist, out RayHit hit)
        {
            hit = default;
            hit.BoxIndex = -1;
            float best = maxDist;
            var list = Tmp;
            Vector3 end = origin + dir * maxDist;
            // walk the grid cells under the ray in chunks to keep the candidate list small
            float segLen = _cellSize * 4f;
            int segs = Math.Max(1, (int)MathF.Ceiling(maxDist / segLen));
            for (int sgi = 0; sgi < segs; sgi++)
            {
                float t0 = sgi * segLen, t1 = MathF.Min(maxDist, t0 + segLen);
                if (t0 > best) break;
                Vector3 a = origin + dir * t0, b = origin + dir * t1;
                Gather(MathF.Min(a.X, b.X), MathF.Min(a.Z, b.Z), MathF.Max(a.X, b.X), MathF.Max(a.Z, b.Z), list);
                foreach (int i in list)
                {
                    var bx = _boxes[i];
                    if (!RaySlab(origin, dir, bx.Min, bx.Max, out float te, out float tx, out int axis)) continue;
                    if (te < 0f || te >= best) continue;
                    best = te;
                    hit.Distance = te; hit.ExitDistance = tx; hit.BoxIndex = i; hit.Material = bx.Material;
                    hit.Point = origin + dir * te;
                    hit.Normal = AxisNormal(axis, dir);
                }
            }
            _ = end;
            return hit.BoxIndex >= 0;
        }

        private static bool RaySlab(Vector3 o, Vector3 d, Vector3 mn, Vector3 mx, out float tEnter, out float tExit, out int axis)
        {
            tEnter = float.NegativeInfinity; tExit = float.PositiveInfinity; axis = 0;
            for (int a = 0; a < 3; a++)
            {
                float oa = a == 0 ? o.X : a == 1 ? o.Y : o.Z;
                float da = a == 0 ? d.X : a == 1 ? d.Y : d.Z;
                float lo = a == 0 ? mn.X : a == 1 ? mn.Y : mn.Z;
                float hi = a == 0 ? mx.X : a == 1 ? mx.Y : mx.Z;
                if (MathF.Abs(da) < 1e-9f) { if (oa < lo || oa > hi) return false; continue; }
                float t1 = (lo - oa) / da, t2 = (hi - oa) / da;
                if (t1 > t2) { var tmp = t1; t1 = t2; t2 = tmp; }
                if (t1 > tEnter) { tEnter = t1; axis = a; }
                if (t2 < tExit) tExit = t2;
                if (tEnter > tExit) return false;
            }
            return tExit >= 0f;
        }

        public bool LineOfSight(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float len = d.Length();
            if (len < 1e-4f) return true;
            return !Raycast(a, d / len, len, out _);
        }
    }
}
