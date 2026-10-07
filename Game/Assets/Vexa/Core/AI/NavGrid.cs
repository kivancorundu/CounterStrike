using System;
using System.Collections.Generic;
using System.Numerics;

namespace Vexa.Core.AI
{
    /// <summary>
    /// Walkable grid generated automatically from the collision world (multi-level: floors under roofs,
    /// platforms, stairs). Edges allow stepping up one stair (18 HU) and dropping down ledges.
    /// </summary>
    public sealed class NavGrid
    {
        public const float CellSize = 0.8f;
        const float HullHalf = 0.3f;
        public readonly List<Vector3> Nodes = new List<Vector3>();
        public readonly List<int[]> Edges = new List<int[]>();
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        private float _ox, _oz;
        private int _w, _h;

        static long Key(int x, int z) => ((long)x << 32) | (uint)z;
        public int CellX(float x) => (int)MathF.Floor((x - _ox) / CellSize);
        public int CellZ(float z) => (int)MathF.Floor((z - _oz) / CellSize);

        public static NavGrid Build(CollisionWorld world)
        {
            var g = new NavGrid();
            var boxes = world.Boxes;
            if (boxes.Count == 0) return g;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var b in boxes) { minX = MathF.Min(minX, b.Min.X); minZ = MathF.Min(minZ, b.Min.Z); maxX = MathF.Max(maxX, b.Max.X); maxZ = MathF.Max(maxZ, b.Max.Z); }
            g._ox = minX; g._oz = minZ;
            g._w = (int)MathF.Ceiling((maxX - minX) / CellSize); g._h = (int)MathF.Ceiling((maxZ - minZ) / CellSize);

            // bucket boxes by cell for fast candidate lookup
            var bucket = new Dictionary<long, List<int>>();
            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                for (int x = g.CellX(b.Min.X); x <= g.CellX(b.Max.X); x++)
                    for (int z = g.CellZ(b.Min.Z); z <= g.CellZ(b.Max.Z); z++)
                    {
                        long k = Key(x, z);
                        if (!bucket.TryGetValue(k, out var l)) bucket[k] = l = new List<int>();
                        l.Add(i);
                    }
            }
            var half = new Vector3(HullHalf, SimConstants.StandHeight * 0.5f, HullHalf);
            for (int x = 0; x < g._w; x++)
                for (int z = 0; z < g._h; z++)
                {
                    if (!bucket.TryGetValue(Key(x, z), out var cand)) continue;
                    float cx = g._ox + (x + 0.5f) * CellSize, cz = g._oz + (z + 0.5f) * CellSize;
                    foreach (int bi in cand)
                    {
                        var b = boxes[bi];
                        if (cx < b.Min.X || cx > b.Max.X || cz < b.Min.Z || cz > b.Max.Z) continue;
                        float y = b.Max.Y;
                        if (y > 40f) continue;
                        var center = new Vector3(cx, y + half.Y + 0.02f, cz);
                        if (world.OverlapBox(center, half)) continue;
                        // skip duplicates (two boxes with the same top)
                        long k = Key(x, z);
                        if (g._cells.TryGetValue(k, out var lv) && lv.Exists(n => MathF.Abs(g.Nodes[n].Y - y) < 0.05f)) continue;
                        if (!g._cells.TryGetValue(k, out lv)) g._cells[k] = lv = new List<int>();
                        lv.Add(g.Nodes.Count);
                        g.Nodes.Add(new Vector3(cx, y, cz));
                    }
                }

            // edges
            var lowHalf = new Vector3(HullHalf, (SimConstants.StandHeight - SimConstants.StepSize) * 0.5f, HullHalf);
            for (int i = 0; i < g.Nodes.Count; i++)
            {
                var a = g.Nodes[i];
                int ax = g.CellX(a.X), az = g.CellZ(a.Z);
                var list = new List<int>(8);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        if (!g._cells.TryGetValue(Key(ax + dx, az + dz), out var lv)) continue;
                        foreach (int j in lv)
                        {
                            var b = g.Nodes[j];
                            float dy = b.Y - a.Y;
                            if (dy > SimConstants.StepSize + 0.02f || dy < -3f) continue;
                            // sweep a hull (raised by a step so stairs pass) from a to b
                            float baseY = MathF.Max(a.Y, b.Y) + SimConstants.StepSize + 0.02f;
                            var from = new Vector3(a.X, baseY + lowHalf.Y, a.Z);
                            var to = new Vector3(b.X, baseY + lowHalf.Y, b.Z);
                            var tr = world.TraceBox(from, to, lowHalf);
                            if (tr.Hit || tr.StartSolid) continue;
                            list.Add(j);
                        }
                    }
                g.Edges.Add(list.ToArray());
            }
            g.ComputeComponents();
            return g;
        }

        // ---- connectivity: only the largest connected area is the playable map (wall tops etc. are islands) ----
        private int[] _component;
        private int _mainComponent;
        public bool IsPlayable(int n) => _component != null && _component[n] == _mainComponent;

        private void ComputeComponents()
        {
            int n = Nodes.Count;
            _component = new int[n];
            for (int i = 0; i < n; i++) _component[i] = -1;
            // undirected view of the edges (drops are one-way, but areas are still connected through stairs)
            var undirected = new List<int>[n];
            for (int i = 0; i < n; i++) undirected[i] = new List<int>(Edges[i]);
            for (int i = 0; i < n; i++) foreach (int j in Edges[i]) undirected[j].Add(i);
            int comp = 0, bestSize = 0;
            var stack = new Stack<int>();
            for (int i = 0; i < n; i++)
            {
                if (_component[i] >= 0) continue;
                int size = 0;
                _component[i] = comp; stack.Push(i);
                while (stack.Count > 0)
                {
                    int c = stack.Pop(); size++;
                    foreach (int nb in undirected[c]) if (_component[nb] < 0) { _component[nb] = comp; stack.Push(nb); }
                }
                if (size > bestSize) { bestSize = size; _mainComponent = comp; }
                comp++;
            }
            _gScore = new float[n]; _came = new int[n]; _stamp = new int[n]; _closed = new int[n];
        }

        private float[] _gScore; private int[] _came, _stamp, _closed; private int _search;

        public int Nearest(Vector3 p)
        {
            int cx = CellX(p.X), cz = CellZ(p.Z);
            int best = -1; float bestD = float.MaxValue;
            for (int r = 0; r < 6 && best < 0; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        if (!_cells.TryGetValue(Key(cx + dx, cz + dz), out var lv)) continue;
                        foreach (int n in lv)
                        {
                            if (!IsPlayable(n)) continue;
                            var q = Nodes[n];
                            float dy = q.Y - p.Y;
                            float d = (q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z) + (dy > 0.6f ? 100f : dy * dy * 4f);
                            if (d < bestD) { bestD = d; best = n; }
                        }
                    }
            return best;
        }

        public List<Vector3> FindPath(Vector3 from, Vector3 to, int maxIter = 30000)
        {
            int s = Nearest(from), goal = Nearest(to);
            if (s < 0 || goal < 0) return null;
            var result = new List<Vector3>();
            if (s == goal) { result.Add(to); return result; }
            _search++;
            var gp = Nodes[goal];
            var heap = new List<(float f, int n)>(256);
            void Push(float f, int n)
            {
                heap.Add((f, n)); int k = heap.Count - 1;
                while (k > 0) { int p = (k - 1) >> 1; if (heap[p].f <= heap[k].f) break; var t = heap[p]; heap[p] = heap[k]; heap[k] = t; k = p; }
            }
            int Pop()
            {
                var top = heap[0]; var last = heap[heap.Count - 1]; heap.RemoveAt(heap.Count - 1);
                if (heap.Count > 0)
                {
                    heap[0] = last; int k = 0;
                    for (;;)
                    {
                        int l = 2 * k + 1, r = l + 1, m = k;
                        if (l < heap.Count && heap[l].f < heap[m].f) m = l;
                        if (r < heap.Count && heap[r].f < heap[m].f) m = r;
                        if (m == k) break;
                        var t = heap[m]; heap[m] = heap[k]; heap[k] = t; k = m;
                    }
                }
                return top.n;
            }
            float G(int n) => _stamp[n] == _search ? _gScore[n] : float.MaxValue;
            _stamp[s] = _search; _gScore[s] = 0; _came[s] = -1;
            Push(Vector3.Distance(Nodes[s], gp), s);
            int iter = 0;
            bool found = false;
            while (heap.Count > 0 && iter++ < maxIter)
            {
                int c = Pop();
                if (c == goal) { found = true; break; }
                if (_closed[c] == _search) continue;
                _closed[c] = _search;
                float gc = _gScore[c];
                foreach (int nb in Edges[c])
                {
                    if (_closed[nb] == _search) continue;
                    float ng = gc + Vector3.Distance(Nodes[c], Nodes[nb]) + (Edges[nb].Length < 8 ? 0.4f : 0f); // keep away from walls
                    if (ng >= G(nb)) continue;
                    _stamp[nb] = _search; _gScore[nb] = ng; _came[nb] = c;
                    Push(ng + Vector3.Distance(Nodes[nb], gp), nb);
                }
            }
            if (!found) return null;
            for (int n = goal; n != s && n >= 0; n = _came[n]) result.Add(Nodes[n]);
            result.Reverse();
            // drop collinear points to make movement smoother
            var smooth = new List<Vector3>();
            for (int i = 0; i < result.Count; i++)
            {
                if (i > 0 && i < result.Count - 1)
                {
                    var d1 = result[i] - result[i - 1]; var d2 = result[i + 1] - result[i];
                    if (MathF.Abs(d1.Y) < 0.01f && MathF.Abs(d2.Y) < 0.01f && Vector3.Distance(Vector3.Normalize(d1), Vector3.Normalize(d2)) < 0.01f) continue;
                }
                smooth.Add(result[i]);
            }
            smooth[smooth.Count - 1] = new Vector3(to.X, smooth[smooth.Count - 1].Y, to.Z);
            return smooth;
        }

        public Vector3 RandomNodeIn(ZoneRect zone, Random rng)
        {
            var candidates = new List<int>();
            for (int i = 0; i < Nodes.Count; i++) if (IsPlayable(i) && zone.Contains(Nodes[i]) && Edges[i].Length >= 3) candidates.Add(i);
            if (candidates.Count == 0) return new Vector3((zone.MinX + zone.MaxX) / 2, 0, (zone.MinZ + zone.MaxZ) / 2);
            return Nodes[candidates[rng.Next(candidates.Count)]];
        }

        public Vector3 RandomNode(Random rng)
        {
            for (int k = 0; k < 64 && Nodes.Count > 0; k++) { int i = rng.Next(Nodes.Count); if (IsPlayable(i)) return Nodes[i]; }
            return Nodes.Count == 0 ? Vector3.Zero : Nodes[0];
        }
    }
}
