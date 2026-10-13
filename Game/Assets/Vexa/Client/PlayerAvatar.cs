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
        // Tools/Blender/vexa_anim.py: locomotion comes in four directions (f, b, l, r) per gait, all clips of a gait
        // share one length and phase (right foot strikes at 0), so they blend without the feet fighting
        static readonly string[] ClipNames =
        {
            "idle", "crouch", "jump", "death",
            "walk_f", "walk_b", "walk_l", "walk_r",
            "run_f", "run_b", "run_l", "run_r",
            "crouch_walk_f", "crouch_walk_b", "crouch_walk_l", "crouch_walk_r",
        };
        const int Idle = 0, Crouch = 1, Jump = 2, Death = 3, Walk = 4, Run = 8, CrouchWalk = 12;
        // speeds the clips were made at (m/s): played at speed / reference so the planted feet don't slide
        const float WalkSpeed = 2.8f, RunSpeed = 5.5f, CrouchWalkSpeed = 1.9f;
        private readonly float[] _gaitPhase = new float[3];

        private Team _team = (Team)255;
        private GameObject _model;
        private Transform _chest, _head, _hand;
        private Quaternion _gripToWeapon = Quaternion.identity;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private readonly AnimationClipPlayable[] _playables = new AnimationClipPlayable[ClipNames.Length];
        private readonly float[] _times = new float[ClipNames.Length];
        private readonly float[] _weights = new float[ClipNames.Length];
        private readonly float[] _target = new float[ClipNames.Length];
        private bool _hasGraph;
        private SkinnedMeshRenderer[] _skins;
        private float _pendingDt;      // animation time not evaluated yet (off camera / far away)
        private int _frameSkip;
        private bool _posed;           // the pose was evaluated this frame (LateUpdate may pitch it)
        private bool _everPosed;       // renderers only report visibility after their first frame
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
            // the model is still in its rest (aiming) pose here: remember how the weapon sits relative to the grip, so
            // it follows the animated hands (the clips sway and bob the rifle with both hands on it)
            if (_hand != null) _gripToWeapon = Quaternion.Inverse(_hand.rotation) * transform.rotation;
            _skins = _model.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var smr in _skins)
            {
                // off camera: no skinning (the bind-pose bounds, padded for crouching / lying dead, decide when)
                smr.updateWhenOffscreen = false;
                var b = smr.localBounds; b.Expand(1.2f); smr.localBounds = b;
                smr.quality = GraphicsQuality.Current == GraphicsQuality.Level.Low ? SkinQuality.Bone2 : SkinQuality.Bone4;
            }
            SpringBones.Attach(_model);

            if (!_model.TryGetComponent<Animator>(out var animator)) animator = _model.AddComponent<Animator>();
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
            _everPosed = false;
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
            if (!s.Alive && Mathf.Abs(s.Tumble) > 0.01f)
            {
                // a body thrown by the killing blow tumbles about its middle (ClientGame corpses)
                var axis = Quaternion.Euler(0, s.TumbleYaw, 0) * Vector3.right;
                var q = Quaternion.AngleAxis(s.Tumble, axis);
                var pivot = Vector3.up * 0.9f;
                transform.rotation = q * transform.rotation;
                transform.position = s.Position.ToU() + pivot - q * pivot;
            }
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

            if (_hasGraph) AnimateCulled(s, dt);
            else if (_fallbackPivot != null) AnimateFallback(s, dt);
            UpdateWeapon(s);
        }

        bool Visible()
        {
            if (_skins == null) return true;
            foreach (var r in _skins) if (r != null && r.isVisible) return true;
            return false;
        }

        /// <summary>Animates only bodies the camera can see, and far ones at a lower rate (every 2nd / 3rd frame).</summary>
        void AnimateCulled(in RemoteState s, float dt)
        {
            _pendingDt += dt;
            _posed = false;
            if (_everPosed && !Visible()) return;
            _everPosed = true;
            var cam = Camera.main;
            float dist = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 0f;
            int every = dist > 45f ? 3 : dist > 20f ? 2 : 1;
            if (GraphicsQuality.Current == GraphicsQuality.Level.Low) every++;
            if (every > 1 && s.Alive && ++_frameSkip % every != 0) return;
            Animate(s, Mathf.Min(_pendingDt, 0.25f));
            _pendingDt = 0;
            _posed = true;
        }

        void Animate(in RemoteState s, float dt)
        {
            var vel = s.Velocity.ToU();
            vel.y = 0;
            float speed = vel.magnitude;
            // direction of travel relative to where the body faces: x right, z forward
            var local = Quaternion.Inverse(Quaternion.Euler(0, s.Yaw, 0)) * vel;
            float fx = speed > 0.05f ? local.x / speed : 0f, fz = speed > 0.05f ? local.z / speed : 1f;
            float sum = Mathf.Abs(fx) + Mathf.Abs(fz);
            if (sum < 1e-4f) { fz = 1; sum = 1; }
            float dF = Mathf.Max(0, fz) / sum, dB = Mathf.Max(0, -fz) / sum, dR = Mathf.Max(0, fx) / sum, dL = Mathf.Max(0, -fx) / sum;

            float duck = Mathf.Clamp01(s.DuckAmount);
            var target = _target;
            System.Array.Clear(target, 0, target.Length);
            float walk = 0, run = 0, crouchWalk = 0;
            if (!s.Alive) target[Death] = 1;
            else if (!s.OnGround) target[Jump] = 1;
            else
            {
                float moving = Mathf.Clamp01((speed - 0.15f) / 0.6f);
                run = Mathf.Clamp01((speed - 3.2f) / 1.6f) * moving;
                walk = moving - run;
                target[Idle] = (1 - moving) * (1 - duck);
                target[Crouch] = (1 - moving) * duck;
                crouchWalk = moving * duck;
                walk *= 1 - duck;
                run *= 1 - duck;
                Directional(target, Walk, walk, dF, dB, dL, dR);
                Directional(target, Run, run, dF, dB, dL, dR);
                Directional(target, CrouchWalk, crouchWalk, dF, dB, dL, dR);
            }
            // death and landing should read immediately; the rest blends
            float k = 1f - Mathf.Exp(-dt * (s.Alive ? 10f : 30f));
            float total = 0;
            for (int i = 0; i < _weights.Length; i++) { _weights[i] = Mathf.Lerp(_weights[i], target[i], k); total += _weights[i]; }

            // one phase per gait, advanced at the rate that matches the ground speed
            AdvancePhase(0, Walk, speed / WalkSpeed, dt);
            AdvancePhase(1, Run, speed / RunSpeed, dt);
            AdvancePhase(2, CrouchWalk, speed / CrouchWalkSpeed, dt);
            for (int i = 0; i < _weights.Length; i++)
            {
                if (!_playables[i].IsValid()) continue;
                _mixer.SetInputWeight(i, total > 0 ? _weights[i] / total : 0);
                float len = Mathf.Max(0.01f, _playables[i].GetAnimationClip().length);
                if (i >= Walk)
                {
                    int g = (i - Walk) / 4;
                    _times[i] = _gaitPhase[g] * len;
                }
                else if (i == Death) _times[i] = s.Alive ? 0 : Mathf.Min(len, _times[i] + dt);
                else if (i == Jump) _times[i] = s.OnGround ? 0 : Mathf.Min(len, _times[i] + dt);
                else _times[i] = Mathf.Repeat(_times[i] + dt, len);
                _playables[i].SetTime(_times[i]);
            }
            _graph.Evaluate(0);
        }

        static void Directional(float[] target, int first, float w, float f, float b, float l, float r)
        {
            target[first] = w * f;
            target[first + 1] = w * b;
            target[first + 2] = w * l;
            target[first + 3] = w * r;
        }

        void AdvancePhase(int gait, int firstClip, float rate, float dt)
        {
            if (!_playables[firstClip].IsValid()) return;
            float len = Mathf.Max(0.01f, _playables[firstClip].GetAnimationClip().length);
            _gaitPhase[gait] = Mathf.Repeat(_gaitPhase[gait] + dt * Mathf.Clamp(rate, 0.5f, 1.6f) / len, 1f);
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
            // the aim pitch is added on top of the animated pose, so only on frames the pose was evaluated
            if (!_alive || _chest == null || !_posed) { PlaceWeapon(); return; }
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
                _weapon.transform.rotation = _hand.rotation * _gripToWeapon;
            }
            else
            {
                _weapon.transform.position = transform.position + transform.rotation * new Vector3(0.12f, 1.25f, 0.35f);
                _weapon.transform.rotation = Quaternion.Euler(-_pitch, _yaw, 0);
            }
        }
    }
}
