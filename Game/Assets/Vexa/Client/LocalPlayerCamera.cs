using UnityEngine;
using Vexa.Core;
using Vexa.Core.Client;

namespace Vexa.Client
{
    /// <summary>
    /// First-person camera: predicted eye position (interpolated between ticks), view angles from local input
    /// for zero-latency aim, CS-style recoil view (punch * 0.9), zoom FOV, step-up smoothing.
    /// While spectating it looks through the watched player's eyes (or follows them in third person).
    /// </summary>
    public sealed class LocalPlayerCamera
    {
        public const float BaseVerticalFov = 73.74f; // = 90° horizontal at 4:3, like CS
        private Camera _cam;
        private float _smoothY;
        private bool _init;
        private Quaternion _specRot = Quaternion.identity;
        private Vector3 _specPos;
        private int _specTarget = -1;

        static float Hfov43ToVertical(float h) => 2f * Mathf.Atan(Mathf.Tan(h * Mathf.Deg2Rad / 2f) * 0.75f) * Mathf.Rad2Deg;

        public void Update(GameSession s, float dt)
        {
            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null) { var go = new GameObject("Main Camera"); go.tag = "MainCamera"; _cam = go.AddComponent<Camera>(); go.AddComponent<AudioListener>(); }
                _cam.nearClipPlane = 0.03f;
                _cam.farClipPlane = 1000f;
            }
            var c = s.Client;
            if (s.Spectating && c.TryGetRemotePose(s.Spectate.Target, out var pose)) { Spectate(s, pose, dt); return; }
            _specTarget = -1;

            var st = c.Predicted;
            var pos = c.RenderPosition.ToU();
            // smooth vertical steps (stairs) so the view doesn't jerk
            float eye = pos.y + c.RenderEyeHeight;
            if (!_init) { _smoothY = eye; _init = true; }
            if (st.OnGround && Mathf.Abs(eye - _smoothY) < 0.6f) _smoothY = Mathf.Lerp(_smoothY, eye, 1f - Mathf.Exp(-dt * 18f));
            else _smoothY = eye;
            _cam.transform.position = new Vector3(pos.x, _smoothY, pos.z);

            WeaponLogic.AimPunch(st, out float pp, out float py);
            float k = WeaponLogic.RecoilScale * WeaponLogic.ViewRecoilTracking;
            if (!st.Alive) { pp = py = 0; _cam.transform.position += Vector3.up * -1.2f; }
            _cam.transform.rotation = UnityBridge.ViewRotation(s.Input.Yaw + py * k, s.Input.Pitch + pp * k);

            var def = st.ActiveDef;
            float fov = BaseVerticalFov;
            if (st.Alive && st.Zoom > 0 && def.ZoomFov != null) fov = Hfov43ToVertical(def.ZoomFov[st.Zoom - 1]);
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, fov, 1f - Mathf.Exp(-dt * 25f));
        }

        void Spectate(GameSession s, in Core.Net.RemoteState pose, float dt)
        {
            float eyeH = Mathf.Lerp(SimConstants.StandEye, SimConstants.DuckEye, pose.DuckAmount);
            var eye = pose.Position.ToU() + Vector3.up * eyeH;
            var rot = UnityBridge.ViewRotation(pose.Yaw, pose.Pitch);
            bool snap = _specTarget != s.Spectate.Target;
            _specTarget = s.Spectate.Target;
            // snapshots carry quantized angles at tick rate: smooth a little so the view doesn't stutter
            _specRot = snap ? rot : Quaternion.Slerp(_specRot, rot, 1f - Mathf.Exp(-dt * 30f));

            Vector3 camPos;
            Quaternion camRot;
            if (s.Spectate.Mode == Spectator.ViewMode.Chase)
            {
                // third person: behind and above, pulled in front of walls
                var flat = UnityBridge.ViewRotation(pose.Yaw, Mathf.Clamp(pose.Pitch, -45f, 30f));
                var back = flat * new Vector3(0.35f, 0.25f, -2.6f);
                var dir = back.normalized;
                float dist = back.magnitude;
                if (s.Client.World.Raycast(eye.ToN(), dir.ToN(), dist, out var hit)) dist = Mathf.Max(0.3f, hit.Distance - 0.15f);
                camPos = eye + dir * dist;
                camRot = Quaternion.LookRotation((eye + flat * Vector3.forward * 6f) - camPos);
            }
            else { camPos = eye; camRot = _specRot; }

            _specPos = snap ? camPos : Vector3.Lerp(_specPos, camPos, 1f - Mathf.Exp(-dt * 40f));
            _cam.transform.position = _specPos;
            _cam.transform.rotation = camRot;
            float fov = pose.Scoped && s.Spectate.Mode == Spectator.ViewMode.InEye ? Hfov43ToVertical(40f) : BaseVerticalFov;
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, fov, 1f - Mathf.Exp(-dt * 25f));
        }
    }
}
