using UnityEngine;
using Vexa.Client.UI;

namespace Vexa.Client
{
    /// <summary>
    /// Entry point. Created automatically when any scene starts, so the project runs without
    /// hand-made scenes. Sets up the camera, light, platform profile and the UI Toolkit front end.
    /// </summary>
    public sealed class VexaApp : MonoBehaviour
    {
        static VexaApp _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_instance != null) return;
            var go = new GameObject("VexaApp");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<VexaApp>();
        }

        void Awake()
        {
            PlatformProfile.Apply();
            VexaSettings.Load();
            if (Camera.main == null)
            {
                var go = new GameObject("Main Camera"); go.tag = "MainCamera";
                go.AddComponent<Camera>(); go.AddComponent<AudioListener>();
                go.transform.position = new Vector3(0, 12, -22); go.transform.rotation = Quaternion.Euler(28, 0, 0);
            }
            DontDestroyOnLoad(Camera.main.gameObject);
            if (FindLight() == null)
            {
                var l = new GameObject("Sun").AddComponent<Light>();
                l.type = LightType.Directional; l.intensity = 1.2f; l.shadows = LightShadows.Soft;
                l.transform.rotation = Quaternion.Euler(50, -30, 0);
                DontDestroyOnLoad(l.gameObject);
            }
            gameObject.AddComponent<UiRoot>();
        }

        static Light FindLight()
        {
            foreach (var l in Object.FindObjectsOfType<Light>()) if (l.type == LightType.Directional) return l;
            return null;
        }
    }

    /// <summary>Separate quality targets for the PC and mobile builds (same code base).</summary>
    public static class PlatformProfile
    {
        public static bool IsMobile => Application.isMobilePlatform;

        public static void Apply()
        {
            QualitySettings.vSyncCount = 0;
            if (IsMobile)
            {
                Application.targetFrameRate = 60;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
                QualitySettings.shadowDistance = 25f;
            }
            else
            {
                Application.targetFrameRate = -1; // uncapped for competitive play
                QualitySettings.shadowDistance = 80f;
            }
        }
    }
}
