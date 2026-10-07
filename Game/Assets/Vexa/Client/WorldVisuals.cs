using System.Collections.Generic;
using UnityEngine;
using Vexa.Core;
using Vexa.Core.Client;
using Vexa.Core.Net;

namespace Vexa.Client
{
    /// <summary>
    /// Placeholder 3D visuals for networked world objects: grenades in flight, smoke volumes, fires,
    /// the bomb and dropped weapons. Smoke is drawn opaque enough to block vision like the server's
    /// line-of-sight test does. Real VFX replace these in the art pass.
    /// </summary>
    public sealed class WorldVisuals
    {
        sealed class Smoke { public GameObject Root; public Transform[] Puffs; public Vector3[] Offsets; public Material Mat; }
        sealed class Fire { public GameObject Root; public Transform[] Flames; public Light Light; }

        private readonly Dictionary<int, GameObject> _projectiles = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, Smoke> _smokes = new Dictionary<int, Smoke>();
        private readonly Dictionary<int, Fire> _fires = new Dictionary<int, Fire>();
        private readonly Dictionary<int, GameObject> _items = new Dictionary<int, GameObject>();
        private GameObject _bomb;
        private Light _bombLight;
        private Renderer _bombLed;
        private Material _ledOn, _ledOff;
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _gone = new List<int>();
        private static Material _flameMat, _itemMat, _bombMat;
        private static readonly Dictionary<GrenadeType, Material> _nadeMats = new Dictionary<GrenadeType, Material>();

        public static Material Unlit(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            return new Material(sh) { color = c };
        }

        public static Material Transparent(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            Material m;
            if (sh != null)
            {
                m = new Material(sh);
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            }
            else m = new Material(Shader.Find("Sprites/Default"));
            m.color = c;
            return m;
        }

        static GameObject Prim(PrimitiveType t, Transform parent, Material m, string name)
        {
            var g = GameObject.CreatePrimitive(t);
            g.name = name;
            var col = g.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            if (parent != null) g.transform.SetParent(parent, false);
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        static Material NadeMat(GrenadeType g)
        {
            if (_nadeMats.TryGetValue(g, out var m)) return m;
            Color c;
            switch (g)
            {
                case GrenadeType.HE: c = new Color(0.25f, 0.33f, 0.2f); break;
                case GrenadeType.Flash: c = new Color(0.85f, 0.85f, 0.82f); break;
                case GrenadeType.Smoke: c = new Color(0.45f, 0.5f, 0.55f); break;
                case GrenadeType.Molotov: case GrenadeType.Incendiary: c = new Color(0.75f, 0.3f, 0.12f); break;
                default: c = new Color(0.4f, 0.32f, 0.22f); break;
            }
            m = MapBuilder.MakeMaterial(c);
            _nadeMats[g] = m;
            return m;
        }

        public void Update(ClientGame c, float dt)
        {
            float tickNow = c.RenderTick;

            // ---- grenades in flight ----
            _seen.Clear();
            foreach (var p in c.Projectiles)
            {
                _seen.Add(p.Id);
                if (!_projectiles.TryGetValue(p.Id, out var go))
                {
                    go = Prim(PrimitiveType.Capsule, null, NadeMat(p.Type), "Grenade_" + p.Type);
                    go.transform.localScale = new Vector3(0.07f, 0.06f, 0.07f);
                    _projectiles[p.Id] = go;
                    go.transform.position = p.Position.ToU();
                }
                // snapshots are 64/s; smooth toward the latest position
                go.transform.position = Vector3.Lerp(go.transform.position, p.Position.ToU(), 1f - Mathf.Exp(-dt * 30f));
                go.transform.Rotate(0, 0, 720f * dt, Space.Self);
            }
            Sweep(_projectiles, go => Object.Destroy(go));

            // ---- smokes ----
            _seen.Clear();
            foreach (var a in c.Smokes)
            {
                _seen.Add(a.Id);
                if (!_smokes.TryGetValue(a.Id, out var s)) _smokes[a.Id] = s = MakeSmoke(a);
                float age = (tickNow - a.StartTick) / c.TickRate;
                float left = (a.EndTick - tickNow) / c.TickRate;
                float grow = Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / 1.0f));
                float fade = Mathf.Clamp01(left / 2f);
                for (int i = 0; i < s.Puffs.Length; i++)
                {
                    float wobble = 1f + 0.04f * Mathf.Sin(Time.time * 0.7f + i * 1.7f);
                    s.Puffs[i].localPosition = s.Offsets[i] * a.Radius * grow;
                    s.Puffs[i].localScale = Vector3.one * (a.Radius * 1.25f * grow * wobble);
                }
                var col = s.Mat.color; col.a = 0.97f * fade; s.Mat.color = col;
                if (s.Mat.HasProperty("_BaseColor")) s.Mat.SetColor("_BaseColor", col);
            }
            Sweep(_smokes, s => { Object.Destroy(s.Root); Object.Destroy(s.Mat); });

            // ---- fires ----
            _seen.Clear();
            foreach (var a in c.Fires)
            {
                _seen.Add(a.Id);
                if (!_fires.TryGetValue(a.Id, out var f)) _fires[a.Id] = f = MakeFire(a);
                float t = Time.time;
                for (int i = 0; i < f.Flames.Length; i++)
                {
                    float h = 0.35f + 0.25f * Mathf.PerlinNoise(t * 3f + i, i * 0.37f);
                    f.Flames[i].localScale = new Vector3(0.55f, h, 0.55f);
                }
                f.Light.intensity = 2.2f + Mathf.PerlinNoise(t * 8f, 0.5f) * 1.2f;
            }
            Sweep(_fires, f => Object.Destroy(f.Root));

            // ---- dropped weapons ----
            _seen.Clear();
            if (_itemMat == null) _itemMat = MapBuilder.MakeMaterial(new Color(0.16f, 0.17f, 0.19f));
            foreach (var it in c.WorldItems)
            {
                _seen.Add(it.Id);
                if (!_items.TryGetValue(it.Id, out var go))
                {
                    var def = Weapons.Get(it.Weapon);
                    float len = def == null ? 0.4f : def.Slot == WeaponSlotKind.Primary ? 0.85f : 0.28f;
                    go = Prim(PrimitiveType.Cube, null, _itemMat, "Item_" + it.Weapon);
                    go.transform.localScale = new Vector3(0.07f, 0.12f, len);
                    go.transform.rotation = Quaternion.Euler(0, (it.Id * 73) % 360, 90);
                    _items[it.Id] = go;
                }
                go.transform.position = it.Position.ToU() + Vector3.up * 0.04f;
            }
            Sweep(_items, go => Object.Destroy(go));

            // ---- bomb ----
            var b = c.Bomb;
            bool show = b.State == BombState.Planted || b.State == BombState.Dropped;
            if (show && _bomb == null) MakeBomb();
            if (_bomb != null)
            {
                _bomb.SetActive(show);
                if (show)
                {
                    _bomb.transform.position = b.Position.ToU() + Vector3.up * 0.05f;
                    bool planted = b.State == BombState.Planted;
                    float left = c.BombTimeLeft;
                    // beep interval shrinks as the timer runs out (~1s -> ~0.1s)
                    float period = planted ? Mathf.Lerp(0.12f, 1f, Mathf.Clamp01(left / 40f)) : 10f;
                    bool on = planted && Mathf.Repeat(Time.time, period) < 0.08f + period * 0.1f;
                    _bombLed.sharedMaterial = on ? _ledOn : _ledOff;
                    _bombLight.intensity = on ? 1.6f : 0f;
                }
            }
        }

        void Sweep<T>(Dictionary<int, T> map, System.Action<T> destroy)
        {
            _gone.Clear();
            foreach (var kv in map) if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (var id in _gone) { destroy(map[id]); map.Remove(id); }
        }

        Smoke MakeSmoke(AreaInfo a)
        {
            var root = new GameObject("Smoke_" + a.Id);
            root.transform.position = a.Center.ToU();
            var mat = Transparent(new Color(0.72f, 0.74f, 0.76f, 0.97f));
            var offsets = new[]
            {
                Vector3.zero, new Vector3(0.45f, -0.15f, 0.1f), new Vector3(-0.45f, -0.1f, -0.1f), new Vector3(0.1f, -0.1f, 0.45f),
                new Vector3(-0.1f, -0.2f, -0.45f), new Vector3(0.2f, 0.35f, -0.1f), new Vector3(-0.25f, 0.3f, 0.2f),
            };
            var puffs = new Transform[offsets.Length];
            for (int i = 0; i < offsets.Length; i++) puffs[i] = Prim(PrimitiveType.Sphere, root.transform, mat, "Puff").transform;
            return new Smoke { Root = root, Puffs = puffs, Offsets = offsets, Mat = mat };
        }

        Fire MakeFire(AreaInfo a)
        {
            if (_flameMat == null) _flameMat = Unlit(new Color(1f, 0.55f, 0.15f));
            var root = new GameObject("Fire_" + a.Id);
            root.transform.position = a.Center.ToU() + Vector3.up * 0.05f;
            var flames = new List<Transform>();
            int n = 14;
            for (int i = 0; i < n; i++)
            {
                float ang = i * 2.399963f; // golden angle spiral fills the disc evenly
                float r = a.Radius * 0.9f * Mathf.Sqrt((i + 0.5f) / n);
                var g = Prim(PrimitiveType.Sphere, root.transform, _flameMat, "Flame");
                g.transform.localPosition = new Vector3(Mathf.Cos(ang) * r, 0.1f, Mathf.Sin(ang) * r);
                flames.Add(g.transform);
            }
            var lg = new GameObject("FireLight");
            lg.transform.SetParent(root.transform, false);
            lg.transform.localPosition = Vector3.up * 0.6f;
            var light = lg.AddComponent<Light>();
            light.type = LightType.Point; light.range = a.Radius * 3f; light.color = new Color(1f, 0.55f, 0.2f);
            return new Fire { Root = root, Flames = flames.ToArray(), Light = light };
        }

        void MakeBomb()
        {
            if (_bombMat == null) _bombMat = MapBuilder.MakeMaterial(new Color(0.22f, 0.2f, 0.16f));
            _ledOn = Unlit(new Color(1f, 0.15f, 0.1f));
            _ledOff = Unlit(new Color(0.25f, 0.05f, 0.04f));
            _bomb = new GameObject("Bomb");
            var body = Prim(PrimitiveType.Cube, _bomb.transform, _bombMat, "Body");
            body.transform.localScale = new Vector3(0.3f, 0.09f, 0.2f);
            var led = Prim(PrimitiveType.Cube, _bomb.transform, _ledOff, "Led");
            led.transform.localScale = new Vector3(0.03f, 0.02f, 0.03f);
            led.transform.localPosition = new Vector3(0.09f, 0.055f, 0.05f);
            _bombLed = led.GetComponent<Renderer>();
            var lg = new GameObject("BombLight");
            lg.transform.SetParent(_bomb.transform, false);
            lg.transform.localPosition = new Vector3(0, 0.2f, 0);
            _bombLight = lg.AddComponent<Light>();
            _bombLight.type = LightType.Point; _bombLight.range = 2.5f; _bombLight.color = new Color(1f, 0.2f, 0.1f);
        }

        public void Clear()
        {
            foreach (var g in _projectiles.Values) if (g) Object.Destroy(g);
            foreach (var s in _smokes.Values) { if (s.Root) Object.Destroy(s.Root); if (s.Mat) Object.Destroy(s.Mat); }
            foreach (var f in _fires.Values) if (f.Root) Object.Destroy(f.Root);
            foreach (var g in _items.Values) if (g) Object.Destroy(g);
            _projectiles.Clear(); _smokes.Clear(); _fires.Clear(); _items.Clear();
            if (_bomb != null) Object.Destroy(_bomb);
            _bomb = null;
        }
    }
}
