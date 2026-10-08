using UnityEngine;
using Vexa.Client.Art;
using Vexa.Core;

namespace Vexa.Client
{
    /// <summary>
    /// First-person weapon and hands. Everything is procedural: sway from mouse movement, walk bob,
    /// recoil kick per shot, reload dip, draw animation on weapon switch, inspect turn and grenade wind-up.
    /// The whole rig is scaled down and pulled toward the camera (same picture, smaller world size), so it
    /// stays inside the player's own collision hull and never pokes through walls.
    /// </summary>
    public sealed class ViewModel
    {
        const float Scale = 0.25f;

        private Transform _root, _weaponPivot;
        private GameObject _weapon, _arms;
        private Transform _rightArm, _leftArm, _muzzle, _support;
        private string _key;
        private Team _armsTeam = (Team)255;
        private Vector2 _sway, _lastAngles;
        private float _bobPhase, _kick, _kickPitch, _deployT = 1f, _lastShotT;
        private bool _hasAngles;

        public bool Visible { get; private set; }

        /// <summary>World position where tracers should start: the muzzle as the player perceives it.</summary>
        public bool TryGetMuzzle(Camera cam, out Vector3 world)
        {
            world = default;
            if (!Visible || _muzzle == null || cam == null) return false;
            var p = _muzzle.position;
            world = cam.transform.position + (p - cam.transform.position) / Scale;
            return true;
        }

        public void OnLocalShot(WeaponDef def)
        {
            if (def == null) return;
            float strength = def.Category == WeaponCategory.Sniper ? 2.2f : def.Category == WeaponCategory.Shotgun ? 1.8f
                           : def.Category == WeaponCategory.Pistol ? 0.9f : def.Category == WeaponCategory.Melee ? 0f : 1f;
            _kick = Mathf.Min(_kick + 0.018f * strength, 0.06f);
            _kickPitch = Mathf.Min(_kickPitch + 2.2f * strength, 9f);
            _lastShotT = Time.time;
        }

        static string KeyFor(in PlayerState st)
        {
            switch (st.Active)
            {
                case WeaponSlotKind.Grenade: return ModelLibrary.GrenadeKey(st.ActiveGrenade);
                case WeaponSlotKind.Bomb: return "c4";
                case WeaponSlotKind.Melee: return "knife";
                default: return ModelLibrary.WeaponKey(st.ActiveSlot.Id);
            }
        }

        public void Update(GameSession s, Camera cam, float dt)
        {
            if (cam == null) return;
            var c = s.Client;
            var st = c.Predicted;
            bool scoped = st.Zoom > 0 && st.ActiveDef.Category == WeaponCategory.Sniper;
            bool show = st.Alive && !s.Spectating && !scoped && !s.IsDemo;
            if (_root == null)
            {
                _root = new GameObject("ViewModel").transform;
                _weaponPivot = new GameObject("Pivot").transform;
                _weaponPivot.SetParent(_root, false);
            }
            if (_root.parent != cam.transform) _root.SetParent(cam.transform, false);
            SetVisible(show);
            if (!show) { _hasAngles = false; return; }
            cam.nearClipPlane = 0.01f;

            // ---- models ----
            var team = st.Team == Team.None ? Team.T : st.Team;
            if (team != _armsTeam) BuildArms(team);
            string key = KeyFor(st);
            if (key != _key) BuildWeapon(key, st);

            // ---- motion ----
            var def = st.ActiveDef;
            bool pistol = def.Category == WeaponCategory.Pistol;
            bool melee = def.Category == WeaponCategory.Melee;
            bool nade = st.Active == WeaponSlotKind.Grenade;
            Vector3 basePos = melee ? new Vector3(0.14f, -0.15f, 0.27f) : nade ? new Vector3(0.13f, -0.15f, 0.28f)
                            : st.Active == WeaponSlotKind.Bomb ? new Vector3(0.02f, -0.2f, 0.33f)
                            : pistol ? new Vector3(0.1f, -0.12f, 0.3f) : new Vector3(0.11f, -0.125f, 0.27f);
            Quaternion baseRot = melee ? Quaternion.Euler(-20f, -12f, -35f) : Quaternion.identity;

            // sway: the gun lags behind the view a little
            var angles = new Vector2(s.Input.Yaw, s.Input.Pitch);
            if (!_hasAngles) { _lastAngles = angles; _hasAngles = true; }
            var d = new Vector2(Mathf.DeltaAngle(_lastAngles.x, angles.x), angles.y - _lastAngles.y);
            _lastAngles = angles;
            _sway = Vector2.Lerp(_sway, Vector2.ClampMagnitude(-d * 0.6f, 6f), 1f - Mathf.Exp(-dt * 12f));
            _sway = Vector2.Lerp(_sway, Vector2.zero, 1f - Mathf.Exp(-dt * 6f));

            // bob while moving on the ground
            float speed = new Vector2(st.Velocity.X, st.Velocity.Z).magnitude;
            float move = st.OnGround ? Mathf.Clamp01(speed / 6f) : 0f;
            _bobPhase += dt * (4f + speed * 1.4f);
            var bob = new Vector3(Mathf.Sin(_bobPhase) * 0.008f, -Mathf.Abs(Mathf.Cos(_bobPhase)) * 0.008f, 0) * move;
            if (!st.OnGround) bob += new Vector3(0, -0.012f, 0);

            // recoil recovers quickly
            _kick = Mathf.Lerp(_kick, 0, 1f - Mathf.Exp(-dt * 14f));
            _kickPitch = Mathf.Lerp(_kickPitch, 0, 1f - Mathf.Exp(-dt * 10f));

            // draw: rise from below after a weapon switch
            _deployT = Mathf.Min(1f, _deployT + dt / 0.35f);
            float deploy = 1f - Mathf.SmoothStep(0, 1, _deployT);

            // reload: dip and roll the weapon
            float pt = c.PlayerTime;
            float reload = 0;
            if (st.Reloading && st.ReloadEndTime > st.ReloadStartTime)
            {
                float p = Mathf.Clamp01((pt - st.ReloadStartTime) / (st.ReloadEndTime - st.ReloadStartTime));
                reload = Mathf.Sin(p * Mathf.PI);
            }
            // inspect: turn the weapon to look at it
            float inspect = 0;
            if (pt < st.InspectEndTime) inspect = Mathf.Sin(Mathf.Clamp01(1f - (st.InspectEndTime - pt) / 3f) * Mathf.PI);
            // grenade wind-up while the pin is pulled
            float windup = nade && st.PinTime > 0 ? 1f : 0f;

            var pos = basePos + bob
                      + new Vector3(0, -0.12f * deploy - 0.06f * reload + 0.03f * windup, -_kick)
                      + new Vector3(_sway.x * 0.0015f, _sway.y * 0.0015f, 0);
            var rot = baseRot
                      * Quaternion.Euler(-_kickPitch + 25f * deploy + 12f * reload - 30f * windup + _sway.y * 0.6f, _sway.x * 0.6f + 55f * inspect, -25f * reload - 20f * inspect);
            _root.localPosition = pos * Scale;
            _root.localRotation = rot;
            _root.localScale = Vector3.one * Scale;
            PlaceArms(melee || nade || st.Active == WeaponSlotKind.Bomb);
        }

        void SetVisible(bool v)
        {
            if (Visible == v) return;
            Visible = v;
            if (_root != null) _root.gameObject.SetActive(v);
        }

        void BuildArms(Team team)
        {
            if (_arms != null) Object.Destroy(_arms);
            _armsTeam = team;
            _arms = ModelLibrary.Arms(team);
            if (_arms == null) return;
            _arms.transform.SetParent(_root, false);
            _rightArm = ModelLibrary.FindDeep(_arms.transform, "RightArm");
            _leftArm = ModelLibrary.FindDeep(_arms.transform, "LeftArm");
            NoShadows(_arms);
        }

        void BuildWeapon(string key, in PlayerState st)
        {
            if (_weapon != null) Object.Destroy(_weapon);
            _key = key;
            _weapon = ModelLibrary.Weapon(key);
            _muzzle = _support = null;
            if (_weapon != null)
            {
                _weapon.transform.SetParent(_weaponPivot, false);
                _weapon.transform.localPosition = Vector3.zero;
                _weapon.transform.localRotation = Quaternion.identity;
                _muzzle = ModelLibrary.FindDeep(_weapon.transform, "Muzzle");
                _support = ModelLibrary.FindDeep(_weapon.transform, "Support");
                NoShadows(_weapon);
            }
            _deployT = 0;
        }

        void PlaceArms(bool oneHanded)
        {
            if (_rightArm != null) { _rightArm.position = _weaponPivot.position; _rightArm.rotation = _weaponPivot.rotation; }
            if (_leftArm != null)
            {
                bool show = !oneHanded && _support != null;
                if (_leftArm.gameObject.activeSelf != show) _leftArm.gameObject.SetActive(show);
                if (show) { _leftArm.position = _support.position; _leftArm.rotation = _weaponPivot.rotation; }
            }
        }

        static void NoShadows(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        public void Clear()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null; _weapon = null; _arms = null; _key = null; _armsTeam = (Team)255;
        }
    }
}
