using System.Collections.Generic;
using UnityEngine;
using Vexa.Core;

namespace Vexa.Client
{
    /// <summary>
    /// Tracers, impact marks, muzzle flash and blood. Impacts are computed from the deterministic pellet
    /// directions, so every client draws them where the server's bullets actually went.
    /// </summary>
    public sealed class ShotEffects
    {
        sealed class Tracer { public LineRenderer Line; public float Life; }
        private readonly List<Tracer> _tracers = new List<Tracer>();
        private readonly Queue<GameObject> _decals = new Queue<GameObject>();
        private readonly List<(GameObject go, float life)> _temp = new List<(GameObject, float)>();
        private Material _tracerMat, _decalMat, _bloodMat, _flashMat;
        private Light _flash;
        private float _flashT;

        void EnsureMaterials()
        {
            if (_tracerMat != null) return;
            var unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            _tracerMat = new Material(Shader.Find("Sprites/Default") ?? unlit) { color = new Color(1f, 0.85f, 0.45f, 0.8f) };
            _decalMat = new Material(unlit) { color = new Color(0.08f, 0.07f, 0.06f) };
            _bloodMat = new Material(unlit) { color = new Color(0.55f, 0.05f, 0.05f) };
            _flashMat = new Material(unlit) { color = new Color(1f, 0.8f, 0.4f) };
            var lg = new GameObject("MuzzleLight");
            _flash = lg.AddComponent<Light>();
            _flash.type = LightType.Point; _flash.range = 6f; _flash.color = new Color(1f, 0.75f, 0.45f); _flash.intensity = 0;
        }

        public void Shot(CollisionWorld world, in ShotInfo shot, Vector3 muzzle, bool local)
        {
            EnsureMaterials();
            var def = Weapons.Get(shot.Weapon);
            if ((shot.Flags & (ShotFlags.Melee)) != 0) return;
            float range = def != null ? def.Range : 100f;
            for (int i = 0; i < shot.Pellets; i++)
            {
                var dir = shot.PelletDirection(i);
                Vector3 end;
                if (world.Raycast(shot.Origin, dir, range, out var hit))
                {
                    end = hit.Point.ToU();
                    Decal(end, hit.Normal.ToU());
                }
                else end = (shot.Origin + dir * Mathf.Min(range, 200f)).ToU();
                if (i == 0 || shot.Pellets <= 3) AddTracer(muzzle, end, local ? 0.5f : 1f);
            }
            if ((shot.Flags & ShotFlags.Silenced) == 0)
            {
                _flash.transform.position = muzzle;
                _flash.intensity = 3f;
                _flashT = 0.05f;
                if (!local) Temp(PrimitiveType.Sphere, muzzle, 0.12f, _flashMat, 0.04f);
            }
        }

        void AddTracer(Vector3 a, Vector3 b, float alpha)
        {
            var go = new GameObject("Tracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = _tracerMat;
            lr.positionCount = 2;
            lr.SetPosition(0, a); lr.SetPosition(1, b);
            lr.startWidth = 0.012f; lr.endWidth = 0.006f;
            lr.startColor = new Color(1f, 0.9f, 0.5f, 0.9f * alpha); lr.endColor = new Color(1f, 0.8f, 0.4f, 0.2f * alpha);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tracers.Add(new Tracer { Line = lr, Life = 0.06f });
        }

        void Decal(Vector3 p, Vector3 n)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.transform.position = p + n * 0.003f;
            q.transform.rotation = Quaternion.LookRotation(-n);
            q.transform.localScale = Vector3.one * 0.05f;
            q.GetComponent<Renderer>().sharedMaterial = _decalMat;
            _decals.Enqueue(q);
            while (_decals.Count > 200) Object.Destroy(_decals.Dequeue());
        }

        public void Blood(Vector3 p, bool head)
        {
            EnsureMaterials();
            Temp(PrimitiveType.Sphere, p, head ? 0.18f : 0.1f, _bloodMat, 0.25f);
        }

        void Temp(PrimitiveType t, Vector3 p, float size, Material m, float life)
        {
            var g = GameObject.CreatePrimitive(t);
            Object.Destroy(g.GetComponent<Collider>());
            g.transform.position = p; g.transform.localScale = Vector3.one * size;
            g.GetComponent<Renderer>().sharedMaterial = m;
            _temp.Add((g, life));
        }

        public void Update(float dt)
        {
            for (int i = _tracers.Count - 1; i >= 0; i--)
            {
                _tracers[i].Life -= dt;
                if (_tracers[i].Life <= 0) { Object.Destroy(_tracers[i].Line.gameObject); _tracers.RemoveAt(i); }
            }
            for (int i = _temp.Count - 1; i >= 0; i--)
            {
                var (g, l) = _temp[i];
                l -= dt;
                if (l <= 0) { Object.Destroy(g); _temp.RemoveAt(i); } else _temp[i] = (g, l);
            }
            if (_flash != null)
            {
                _flashT -= dt;
                _flash.intensity = _flashT > 0 ? 3f : 0f;
            }
        }

        public void Clear()
        {
            foreach (var t in _tracers) if (t.Line) Object.Destroy(t.Line.gameObject);
            _tracers.Clear();
            while (_decals.Count > 0) Object.Destroy(_decals.Dequeue());
            foreach (var (g, _) in _temp) if (g) Object.Destroy(g);
            _temp.Clear();
            if (_flash != null) Object.Destroy(_flash.gameObject);
        }
    }
}
