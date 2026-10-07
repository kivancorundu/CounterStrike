using UnityEngine;
using Vexa.Core;

namespace Vexa.Client
{
    /// <summary>
    /// First-person camera: predicted eye position (interpolated between ticks), view angles from local input
    /// for zero-latency aim, CS-style recoil view (punch * 0.9), zoom FOV, step-up smoothing.
    /// </summary>
    public sealed class LocalPlayerCamera
    {
        public const float BaseVerticalFov = 73.74f; // = 90° horizontal at 4:3, like CS
        private Camera _cam;
        private float _smoothY;
        private bool _init;

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
    }
}
