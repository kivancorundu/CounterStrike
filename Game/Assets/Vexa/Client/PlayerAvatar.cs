using UnityEngine;
using Vexa.Core;
using Vexa.Core.Net;

namespace Vexa.Client
{
    /// <summary>
    /// Placeholder third-person body built from primitives (will be replaced by the real character models).
    /// Proportions match the server hitboxes so what you see is what you hit.
    /// </summary>
    public sealed class PlayerAvatar : MonoBehaviour
    {
        private Transform _body, _head, _gun, _torsoPivot;
        private TextMesh _label;
        private float _deadT;

        public static PlayerAvatar Create(string name, Team team)
        {
            var go = new GameObject("Player_" + name);
            var av = go.AddComponent<PlayerAvatar>();
            var color = team == Team.CT ? new Color(0.25f, 0.36f, 0.55f) : team == Team.T ? new Color(0.55f, 0.45f, 0.28f) : new Color(0.5f, 0.5f, 0.5f);
            var mat = MapBuilder.MakeMaterial(color);
            var skin = MapBuilder.MakeMaterial(new Color(0.75f, 0.6f, 0.48f));
            var dark = MapBuilder.MakeMaterial(new Color(0.12f, 0.12f, 0.13f));

            av._torsoPivot = new GameObject("Pivot").transform;
            av._torsoPivot.SetParent(go.transform, false);
            av._body = Prim(PrimitiveType.Capsule, av._torsoPivot, new Vector3(0, 0.9f, 0), new Vector3(0.45f, 0.9f, 0.32f), mat);
            av._head = Prim(PrimitiveType.Sphere, av._torsoPivot, new Vector3(0, 1.68f, 0.04f), new Vector3(0.24f, 0.26f, 0.25f), skin);
            av._gun = Prim(PrimitiveType.Cube, av._torsoPivot, new Vector3(0.14f, 1.3f, 0.35f), new Vector3(0.06f, 0.08f, 0.6f), dark);

            var lbl = new GameObject("Name");
            lbl.transform.SetParent(go.transform, false);
            lbl.transform.localPosition = new Vector3(0, 2.15f, 0);
            av._label = lbl.AddComponent<TextMesh>();
            av._label.text = name; av._label.characterSize = 0.05f; av._label.fontSize = 48;
            av._label.anchor = TextAnchor.MiddleCenter; av._label.color = Color.white;
            return av;
        }

        static Transform Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(t);
            var col = g.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g.transform;
        }

        public void Apply(in RemoteState s, string name)
        {
            transform.position = s.Position.ToU();
            transform.rotation = Quaternion.Euler(0, s.Yaw, 0);
            float crouch = Mathf.Lerp(1f, SimConstants.DuckHeight / SimConstants.StandHeight, s.DuckAmount);
            _torsoPivot.localScale = new Vector3(1f, crouch, 1f);
            _gun.localRotation = Quaternion.Euler(-s.Pitch * 0.8f, 0, 0);
            if (_label != null)
            {
                _label.text = name + "  " + s.Health;
                var cam = Camera.main;
                if (cam != null) _label.transform.rotation = Quaternion.LookRotation(_label.transform.position - cam.transform.position);
            }
            if (!s.Alive)
            {
                _deadT = Mathf.Min(1f, _deadT + Time.deltaTime * 2.5f);
                _torsoPivot.localRotation = Quaternion.Euler(-85f * _deadT * _deadT, 0, 0);
                if (_label != null) _label.gameObject.SetActive(false);
            }
            else
            {
                _deadT = 0;
                _torsoPivot.localRotation = Quaternion.identity;
                if (_label != null) _label.gameObject.SetActive(true);
            }
        }
    }
}
