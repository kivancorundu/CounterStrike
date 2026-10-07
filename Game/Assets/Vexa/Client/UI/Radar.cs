using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Vexa.Core;
using Vexa.Core.AI;
using Vexa.Core.Client;
using Vexa.Core.Net;
using NVec3 = System.Numerics.Vector3;

namespace Vexa.Client.UI
{
    /// <summary>
    /// Top-down radar. The map image is generated from the walkable nav grid, so every map gets a radar
    /// automatically (shaded by floor height). Teammates are always shown; enemies only while the local
    /// player can see them (with a short fade like CS's "last seen" marker).
    /// </summary>
    public sealed class RadarView : VisualElement
    {
        const float PxPerMeter = 5f;          // texture resolution
        const float ViewMeters = 56f;         // world meters across the radar at zoom 1
        private readonly float _size;
        private readonly VisualElement _pivot, _map, _self, _dots;
        private readonly List<Label> _siteLabels = new List<Label>();
        private readonly Dictionary<int, VisualElement> _players = new Dictionary<int, VisualElement>();
        private readonly Dictionary<int, float> _lastSeen = new Dictionary<int, float>();
        private readonly Dictionary<int, NVec3> _lastSeenPos = new Dictionary<int, NVec3>();
        private VisualElement _bomb;
        private MapData _builtFor;
        private float _minX, _minZ, _texW, _texH;
        private Texture2D _tex;

        public RadarView(float size)
        {
            _size = size;
            pickingMode = PickingMode.Ignore;
            this.Size(size, size).Bg(new Color(0.04f, 0.05f, 0.07f, 0.78f)).Border(1, Theme.LineStrong);
            style.overflow = Overflow.Hidden;
            _pivot = U.Box().Abs(size / 2, size / 2).Size(0, 0);
            _map = U.Box().Abs(0, 0);
            _dots = U.Box().Abs(0, 0);
            _map.Add(_dots);
            _pivot.Add(_map);
            hierarchy.Add(_pivot);
            // local player: arrow pointing up (the radar rotates around it)
            _self = U.Box().Abs(size / 2 - 7, size / 2 - 9).Size(14, 16);
            var tri = new VisualElement { pickingMode = PickingMode.Ignore }.Size(14, 16);
            tri.generateVisualContent += DrawArrow;
            _self.Add(tri);
            hierarchy.Add(_self);
        }

        static void DrawArrow(MeshGenerationContext ctx)
        {
            var m = ctx.Allocate(3, 3);
            var c = (Color32)Theme.Text;
            m.SetNextVertex(new Vertex { position = new Vector3(7, 0, Vertex.nearZ), tint = c });
            m.SetNextVertex(new Vertex { position = new Vector3(14, 16, Vertex.nearZ), tint = c });
            m.SetNextVertex(new Vertex { position = new Vector3(0, 16, Vertex.nearZ), tint = c });
            m.SetNextIndex(0); m.SetNextIndex(1); m.SetNextIndex(2);
        }

        void Build(ClientGame c)
        {
            _builtFor = c.Map;
            var nav = NavGrid.Build(c.World);
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < nav.Nodes.Count; i++)
            {
                if (!nav.IsPlayable(i)) continue;
                var n = nav.Nodes[i];
                minX = Mathf.Min(minX, n.X); maxX = Mathf.Max(maxX, n.X); minZ = Mathf.Min(minZ, n.Z); maxZ = Mathf.Max(maxZ, n.Z);
                minY = Mathf.Min(minY, n.Y); maxY = Mathf.Max(maxY, n.Y);
            }
            if (minX > maxX) { minX = minZ = -10; maxX = maxZ = 10; minY = maxY = 0; }
            float pad = 4f;
            _minX = minX - pad; _minZ = minZ - pad;
            int w = Mathf.CeilToInt((maxX - minX + 2 * pad) * PxPerMeter), h = Mathf.CeilToInt((maxZ - minZ + 2 * pad) * PxPerMeter);
            if (_tex != null) Object.Destroy(_tex);
            _tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            var height = new float[w * h];
            for (int i = 0; i < height.Length; i++) height[i] = float.MinValue;
            int cell = Mathf.CeilToInt(NavGrid.CellSize * PxPerMeter);
            for (int i = 0; i < nav.Nodes.Count; i++)
            {
                if (!nav.IsPlayable(i)) continue;
                var n = nav.Nodes[i];
                int cx = Mathf.RoundToInt((n.X - _minX) * PxPerMeter), cy = Mathf.RoundToInt((n.Z - _minZ) * PxPerMeter);
                for (int dx = -cell / 2; dx <= cell / 2; dx++)
                    for (int dy = -cell / 2; dy <= cell / 2; dy++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        int k = y * w + x;
                        if (n.Y > height[k]) height[k] = n.Y;
                    }
            }
            float range = Mathf.Max(1f, maxY - minY);
            for (int i = 0; i < px.Length; i++)
            {
                if (height[i] == float.MinValue) { px[i] = new Color32(0, 0, 0, 0); continue; }
                float t = (height[i] - minY) / range;
                byte v = (byte)Mathf.Lerp(62, 128, t);
                px[i] = new Color32(v, (byte)(v + 6), (byte)(v + 16), 235);
            }
            // edge darkening: walkable pixels next to empty space become an outline
            var outp = (Color32[])px.Clone();
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int k = y * w + x;
                    if (px[k].a == 0) continue;
                    if (px[k - 1].a == 0 || px[k + 1].a == 0 || px[k - w].a == 0 || px[k + w].a == 0) outp[k] = new Color32(170, 180, 195, 255);
                }
            _tex.SetPixels32(outp);
            _tex.Apply();
            _texW = w; _texH = h;
            _map.style.width = w; _map.style.height = h;
            _map.style.backgroundImage = new StyleBackground(_tex);

            foreach (var l in _siteLabels) l.RemoveFromHierarchy();
            _siteLabels.Clear();
            foreach (var zone in c.Map.Sites)
            {
                string name = zone.Name;
                var center = new NVec3((zone.MinX + zone.MaxX) / 2, 0, (zone.MinZ + zone.MaxZ) / 2);
                var p = ToMap(center);
                var lbl = U.Head(name, 26, Theme.WithAlpha(Theme.Text, 0.55f)).Abs(p.x - 8, p.y - 16);
                _siteLabels.Add(lbl);
                _map.Insert(0, lbl);
            }
            foreach (var d in _players.Values) d.RemoveFromHierarchy();
            _players.Clear();
        }

        Vector2 ToMap(NVec3 w) => new Vector2((w.X - _minX) * PxPerMeter, _texH - (w.Z - _minZ) * PxPerMeter);

        VisualElement Dot(int id)
        {
            if (_players.TryGetValue(id, out var d)) return d;
            d = U.Box().Size(11, 11).Radius(6).Border(2, Theme.Ink);
            d.style.position = Position.Absolute;
            _dots.Add(d);
            _players[id] = d;
            return d;
        }

        public void Tick(ClientGame c, float viewYaw)
        {
            if (c?.Map == null || c.World == null) return;
            if (_builtFor != c.Map) Build(c);
            float zoom = Mathf.Clamp(VexaSettings.RadarZoom, 0.5f, 2f);
            float scale = _size / (ViewMeters / zoom * PxPerMeter);
            var me = c.RenderPosition;
            var mp = ToMap(me);
            float rot = VexaSettings.RadarRotate ? -viewYaw : 0f;
            _pivot.style.rotate = new Rotate(new Angle(rot));
            _pivot.style.scale = new Scale(new Vector3(scale, scale, 1));
            _map.style.left = -mp.x; _map.style.top = -mp.y;
            _self.style.rotate = new Rotate(new Angle(VexaSettings.RadarRotate ? 0 : viewYaw));
            _self.Show(c.Predicted.Alive);

            float now = Time.unscaledTime;
            var seen = new HashSet<int>();
            var eye = c.Predicted.EyePosition;
            var fwd = VMath.FlatForward(viewYaw);
            foreach (var r in c.Remotes)
            {
                if (!c.TryGetRemotePose(r.Id, out var pose) || !pose.Alive) continue;
                bool mate = c.LocalTeam != Team.None && pose.Team == c.LocalTeam;
                var pos = pose.Position;
                if (!mate)
                {
                    // spotted: in front of us and in line of sight (smoke blocks)
                    var head = pos + new NVec3(0, 1.6f, 0);
                    var to = head - eye;
                    float len = to.Length();
                    bool visible = c.Predicted.Alive && len > 0.01f && NVec3.Dot(new NVec3(fwd.X, 0, fwd.Z), to / len) > 0.2f
                                   && c.World.LineOfSight(eye, head) && !InSmoke(c, eye, to / len, len);
                    if (visible) { _lastSeen[r.Id] = now; _lastSeenPos[r.Id] = pos; }
                    if (!_lastSeen.TryGetValue(r.Id, out var t) || now - t > 2.5f) continue;
                    pos = _lastSeenPos[r.Id];
                }
                seen.Add(r.Id);
                var d = Dot(r.Id);
                var p = ToMap(pos);
                d.style.left = p.x - 5.5f; d.style.top = p.y - 5.5f;
                var col = Theme.TeamColor(pose.Team);
                if (!mate && _lastSeen.TryGetValue(r.Id, out var ls)) col.a = now - ls < 0.2f ? 1f : 0.45f;
                d.style.backgroundColor = col;
                float inv = 1f / scale;
                d.style.scale = new Scale(new Vector3(inv, inv, 1));
                d.Show(true);
            }
            foreach (var kv in _players) if (!seen.Contains(kv.Key)) kv.Value.Show(false);
            foreach (var l in _siteLabels) l.style.rotate = new Rotate(new Angle(-rot));

            // bomb (the server only tells us about it when we're allowed to know)
            var b = c.Bomb;
            bool showBomb = b.State == BombState.Dropped || b.State == BombState.Planted || (b.State == BombState.Carried && c.LocalTeam == Team.T);
            if (_bomb == null)
            {
                _bomb = U.Box().Size(12, 12).Bg(Theme.Danger).Border(2, Theme.Ink);
                _bomb.style.position = Position.Absolute;
                _dots.Add(_bomb);
            }
            _bomb.Show(showBomb);
            if (showBomb)
            {
                var bp = b.Position;
                if (b.State == BombState.Carried && c.TryGetRemotePose(b.CarrierId, out var carrier)) bp = carrier.Position;
                else if (b.State == BombState.Carried && b.CarrierId == c.LocalId) bp = me;
                var p = ToMap(bp);
                _bomb.style.left = p.x - 6; _bomb.style.top = p.y - 6;
                float inv = 1f / scale;
                bool blink = b.State == BombState.Planted && Mathf.Repeat(now, 0.8f) < 0.4f;
                _bomb.style.scale = new Scale(new Vector3(inv * (blink ? 1.3f : 1f), inv * (blink ? 1.3f : 1f), 1));
            }
        }

        static bool InSmoke(ClientGame c, NVec3 o, NVec3 dir, float len)
        {
            float tickNow = c.RenderTick;
            foreach (var s in c.Smokes)
            {
                float grow = Mathf.Clamp01((tickNow - s.StartTick) / c.TickRate);
                float r = s.Radius * grow;
                if (r < 0.3f) continue;
                var oc = s.Center - o;
                float t = NVec3.Dot(oc, dir);
                float d2 = oc.LengthSquared() - t * t;
                if (d2 >= r * r) continue;
                float half = Mathf.Sqrt(r * r - d2);
                if (Mathf.Min(len, t + half) - Mathf.Max(0f, t - half) > 1f) return true;
            }
            return false;
        }
    }
}
