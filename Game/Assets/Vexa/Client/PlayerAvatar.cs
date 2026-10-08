using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Vexa.Client.Art;
using Vexa.Core;
using Vexa.Core.Net;

namespace Vexa.Client
{
    /// <summary>
    /// Third-person player: the faction's rigged model, blended animation clips (idle / walk / run / crouch /
    /// jump / death) driven by the networked pose, upper body pitched to the aim, and the current weapon in hand.
    /// The model's rest pose matches the server hitboxes. Loose gear swings with <see cref="SpringBones"/>.
    /// Falls back to primitives if the models are missing.
    /// </summary>
    public sealed class PlayerAvatar : MonoBehaviour
    {
        static readonly string[] ClipNames = { "idle", "walk", "run", "crouch", "crouch_walk", "jump", "death" };
        const int Idle = 0, Walk = 1, Run = 2, Crouch = 3, CrouchWalk = 4, Jump = 5, Death = 6;

        private Team _team = (Team)255;
        private GameObject _model;
        private Transform _chest, _head, _hand;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private readonly AnimationClipPlayable[] _playables = new AnimationClipPlayable[ClipNames.Length];
        private readonly float[] _times = new float[ClipNames.Length];
        private readonly float[] _weights = new float[ClipNames.Length];
        private readonly float[] _target = new float[ClipNames.Length];
        private bool _hasGraph;
        private GameObject _weapon;
        private string _weaponKey;
        private float _pitch, _yaw;
        private bool _alive = true;
        private TextMesh _label;
        // primitive fallback
        private Transform _fallbackPivot;
        private float _deadT;

        public static PlayerAvatar Create(string name, Team team)
        {
            var go = new GameObject("Player_" + name);
            var av = go.AddComponent<PlayerAvatar>();
            var lbl = new GameObject("Name");
            lbl.transform.SetParent(go.transform, false);
            lbl.transform.localPosition = new Vector3(0, 2.1f, 0);
            av._label = lbl.AddComponent<TextMesh>();
            av._label.text = name; av._label.characterSize = 0.04f; av._label.fontSize = 48;
            av._label.anchor = TextAnchor.MiddleCenter; av._label.color = Color.white;
            av.Build(team);
            return av;
        }

        void Build(Team team)
        {
            Teardown();
            _team = team;
            _model = ModelLibrary.Character(team == Team.None ? Team.T : team);
            if (_model == null) { BuildFallback(team); return; }
            _model.transform.SetParent(transform, false);
            _chest = ModelLibrary.FindDeep(_model.transform, "Chest");
            _head = ModelLibrary.FindDeep(_model.transform, "Head");
            // weapons hang on the grip point of the right hand (older models only have the hand bone)
            _hand = ModelLibrary.FindDeep(_model.transform, "RightGrip") ?? ModelLibrary.FindDeep(_model.transform, "RightHand");
            foreach (var smr in _model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.updateWhenOffscreen = true;
                smr.quality = SkinQuality.Bone4;
            }
            SpringBones.Attach(_model);

            var animator = _model.GetComponent<Animator>() ?? _model.AddComponent<Animator>();
            animator.applyRootMotion = false;
            _graph = PlayableGraph.Create("Avatar_" + name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _mixer = AnimationMixerPlayable.Create(_graph, ClipNames.Length);
            var output = AnimationPlayableOutput.Create(_graph, "Anim", animator);
            output.SetSourcePlayable(_mixer);
            for (int i = 0; i < ClipNames.Length; i++)
            {
                var clip = ModelLibrary.Clip(team == Team.None ? Team.T : team, ClipNames[i]);
                if (clip == null) continue;
                _playables[i] = AnimationClipPlayable.Create(_graph, clip);
                _playables[i].SetApplyFootIK(false);
                _graph.Connect(_playables[i], 0, _mixer, i);
            }
            _weights[Idle] = 1;
            _hasGraph = true;
        }

        void BuildFallback(Team team)
        {
            var color = team == Team.CT ? new Color(0.25f, 0.36f, 0.55f) : team == Team.T ? new Color(0.55f, 0.45f, 0.28f) : new Color(0.5f, 0.5f, 0.5f);
            _fallbackPivot = new GameObject("Pivot").transform;
            _fallbackPivot.SetParent(transform, false);
            Prim(PrimitiveType.Capsule, _fallbackPivot, new Vector3(0, 0.9f, 0), new Vector3(0.45f, 0.9f, 0.32f), MapBuilder.MakeMaterial(color));
            Prim(PrimitiveType.Sphere, _fallbackPivot, new Vector3(0, 1.68f, 0.04f), new Vector3(0.24f, 0.26f, 0.25f), MapBuilder.MakeMaterial(new Color(0.75f, 0.6f, 0.48f)));
        }

        static void Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(t);
            var col = g.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
        }

        void Teardown()
        {
            if (_hasGraph && _graph.IsValid()) _graph.Destroy();
            _hasGraph = false;
            if (_model != null) Destroy(_model);
            if (_fallbackPivot != null) Destroy(_fallbackPivot.gameObject);
            if (_weapon != null) Destroy(_weapon);
            _weapon = null; _weaponKey = null;
        }

        void OnDestroy() { if (_hasGraph && _graph.IsValid()) _graph.Destroy(); }

        /// <summary>Called every frame with the interpolated network pose.</summary>
        public void Apply(in RemoteState s, string name, bool showName)
        {
            if (s.Team != _team && s.Team != Team.None) Build(s.Team);
            transform.position = s.Position.ToU();
            transform.rotation = Quaternion.Euler(0, s.Yaw, 0);
            _yaw = s.Yaw; _pitch = s.Pitch; _alive = s.Alive;
            float dt = Time.deltaTime;

            if (_label != null)
            {
                bool show = showName && s.Alive;
                if (_label.gameObject.activeSelf != show) _label.gameObject.SetActive(show);
                if (show)
                {
                    _label.text = name;
                    var cam = Camera.main;
                    if (cam != null) _label.transform.rotation = Quaternion.LookRotation(_label.transform.position - cam.transform.position);
                }
            }

            if (_hasGraph) Animate(s, dt);
            else if (_fallbackPivot != null) AnimateFallback(s, dt);
            UpdateWeapon(s);
        }

        void Animate(in RemoteState s, float dt)
        {
            float speed = new Vector2(s.Velocity.X, s.Velocity.Z).magnitude;
            float duck = Mathf.Clamp01(s.DuckAmount);
            var target = _target;
            System.Array.Clear(target, 0, target.Length);
            if (!s.Alive) target[Death] = 1;
            else if (!s.OnGround) target[Jump] = 1;
            else
            {
                float walk = Mathf.Clamp01((speed - 0.2f) / 1.3f) * (1f - Mathf.Clamp01((speed - 3f) / 2f));
                float run = Mathf.Clamp01((speed - 3f) / 2f);
                float idle = Mathf.Max(0, 1f - walk - run);
                target[Idle] = idle * (1 - duck);
                target[Walk] = walk * (1 - duck);
                target[Run] = run * (1 - duck);
                target[Crouch] = idle * duck;
                target[CrouchWalk] = (walk + run) * duck;
            }
            // death and landing should read immediately; the rest blends
            float k = 1f - Mathf.Exp(-dt * (s.Alive ? 12f : 30f));
            float sum = 0;
            for (int i = 0; i < _weights.Length; i++) { _weights[i] = Mathf.Lerp(_weights[i], target[i], k); sum += _weights[i]; }
            for (int i = 0; i < _weights.Length; i++)
            {
                if (!_playables[i].IsValid()) continue;
                _mixer.SetInputWeight(i, sum > 0 ? _weights[i] / sum : 0);
                var clip = _playables[i].GetAnimationClip();
                float len = Mathf.Max(0.01f, clip.length);
                float rate = 1f;
                if (i == Walk) rate = Mathf.Clamp(speed / 1.8f, 0.6f, 1.6f);
                else if (i == Run) rate = Mathf.Clamp(speed / 5.5f, 0.7f, 1.3f);
                else if (i == CrouchWalk) rate = Mathf.Clamp(speed / 1.4f, 0.5f, 1.5f);
                if (i == Death)
                {
                    _times[i] = s.Alive ? 0 : Mathf.Min(len, _times[i] + dt);
                }
                else if (i == Jump) _times[i] = s.OnGround ? 0 : Mathf.Min(len, _times[i] + dt);
                else _times[i] = Mathf.Repeat(_times[i] + dt * rate, len);
                _playables[i].SetTime(_times[i]);
            }
            _graph.Evaluate(0);
        }

        void AnimateFallback(in RemoteState s, float dt)
        {
            float crouch = Mathf.Lerp(1f, SimConstants.DuckHeight / SimConstants.StandHeight, s.DuckAmount);
            _fallbackPivot.localScale = new Vector3(1f, crouch, 1f);
            _deadT = s.Alive ? 0 : Mathf.Min(1f, _deadT + dt * 2.5f);
            _fallbackPivot.localRotation = Quaternion.Euler(-85f * _deadT * _deadT, 0, 0);
        }

        void UpdateWeapon(in RemoteState s)
        {
            string key = !s.Alive ? null : s.Weapon == WeaponId.Grenade ? ModelLibrary.GrenadeKey(s.Grenade == GrenadeType.None ? GrenadeType.HE : s.Grenade) : ModelLibrary.WeaponKey(s.Weapon);
            if (s.Planting || s.Defusing) key = s.Planting ? "c4" : null;
            if (key == _weaponKey) return;
            if (_weapon != null) Destroy(_weapon);
            _weaponKey = key;
            _weapon = key == null ? null : ModelLibrary.Weapon(key);
            if (_weapon != null) _weapon.transform.SetParent(transform, true);
        }

        void LateUpdate()
        {
            if (!_alive || _chest == null) { PlaceWeapon(); return; }
            // aim: pitch the chest (arms and weapon follow) and the head a little more
            var right = transform.right;
            _chest.rotation = Quaternion.AngleAxis(-_pitch * 0.75f, right) * _chest.rotation;
            if (_head != null) _head.rotation = Quaternion.AngleAxis(-_pitch * 0.25f, right) * _head.rotation;
            PlaceWeapon();
        }

        void PlaceWeapon()
        {
            if (_weapon == null) return;
            if (_hand != null)
            {
                _weapon.transform.position = _hand.position;
                _weapon.transform.rotation = Quaternion.Euler(-_pitch * 0.75f, _yaw, 0);
            }
            else
            {
                _weapon.transform.position = transform.position + transform.rotation * new Vector3(0.12f, 1.25f, 0.35f);
                _weapon.transform.rotation = Quaternion.Euler(-_pitch, _yaw, 0);
            }
        }
    }
}
