using System.Collections.Generic;
using UnityEngine;

namespace Vexa.Client.Art
{
    /// <summary>
    /// Secondary motion for the loose parts of a character (radio antenna, dump pouch, drop-leg holster, strap ends,
    /// the rag in the back pocket): every bone named "Jiggle_*" (Tools/Blender/vexa_outfit.py) gets a verlet spring
    /// at its tip that lags behind the body, swings with gravity and settles back. Cheap enough for mobile
    /// (a handful of bones per character, no colliders).
    /// </summary>
    [DefaultExecutionOrder(1000)] // after the animation and PlayerAvatar's aim pitch
    public sealed class SpringBones : MonoBehaviour
    {
        sealed class Spring
        {
            public Transform T;
            public Quaternion RestLocal;
            public Vector3 Tip, Prev;
            public float Stiffness, Drag, Gravity;
            public bool Started;
        }

        const float BoneLength = 0.15f;
        readonly List<Spring> _springs = new List<Spring>();

        public static SpringBones Attach(GameObject model)
        {
            var sb = model.GetComponent<SpringBones>() ?? model.AddComponent<SpringBones>();
            sb.Collect(model.transform);
            return sb;
        }

        void Collect(Transform t)
        {
            if (t.name.StartsWith("Jiggle_")) _springs.Add(Make(t));
            for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i));
        }

        static Spring Make(Transform t)
        {
            // per part feel: whippy antenna, heavy holster
            string n = t.name;
            float stiff = 0.25f, drag = 0.12f, grav = 0.6f;
            if (n.Contains("Antenna")) { stiff = 0.14f; drag = 0.06f; grav = 0.25f; }
            else if (n.Contains("Holster")) { stiff = 0.5f; drag = 0.2f; grav = 0.8f; }
            else if (n.Contains("Pouch")) { stiff = 0.3f; drag = 0.15f; grav = 1f; }
            else if (n.Contains("Rag") || n.Contains("Strap")) { stiff = 0.12f; drag = 0.1f; grav = 1f; }
            return new Spring { T = t, RestLocal = t.localRotation, Stiffness = stiff, Drag = drag, Gravity = grav };
        }

        void OnEnable()
        {
            foreach (var s in _springs) s.Started = false;
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            if (dt <= 0f) return;
            // frame-rate independent stiffness / drag (tuned at 60 fps)
            float f = dt * 60f;
            foreach (var s in _springs)
            {
                var t = s.T;
                t.localRotation = s.RestLocal;
                Vector3 head = t.position;
                float len = BoneLength * t.lossyScale.y;
                Vector3 target = head + t.rotation * Vector3.up * len;
                if (!s.Started || (s.Tip - target).sqrMagnitude > 1f)
                {
                    s.Tip = s.Prev = target;
                    s.Started = true;
                    continue;
                }
                Vector3 vel = (s.Tip - s.Prev) * Mathf.Pow(1f - s.Drag, f);
                s.Prev = s.Tip;
                s.Tip += vel + Vector3.down * (9.81f * s.Gravity * dt * dt);
                s.Tip += (target - s.Tip) * (1f - Mathf.Pow(1f - s.Stiffness, f));
                Vector3 dir = s.Tip - head;
                if (dir.sqrMagnitude < 1e-8f) continue;
                s.Tip = head + dir.normalized * len;
                t.rotation = Quaternion.FromToRotation(target - head, s.Tip - head) * t.rotation;
            }
        }
    }
}
