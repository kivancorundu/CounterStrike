using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Vexa.Client
{
    /// <summary>
    /// Graphics presets (DÜŞÜK / ORTA / YÜKSEK / ULTRA). Every setting that costs frames on a weak PC is set here
    /// explicitly, so a preset means the same thing whatever the project's quality levels say:
    /// render scale, shadows, MSAA, HDR, extra lights (fires, muzzle flashes), texture resolution, skinning, and
    /// on DÜŞÜK the low-poly model set (the same one the phone build uses).
    /// The "auto" setting picks DÜŞÜK on integrated graphics and other weak machines.
    /// </summary>
    public static class GraphicsQuality
    {
        public enum Level { Low = 0, Medium = 1, High = 2, Ultra = 3 }

        public static Level Current { get; private set; } = Level.High;

        /// <summary>Use the low-poly models with small textures (phones, and PCs on DÜŞÜK).</summary>
        public static bool LowDetail => PlatformProfile.IsMobile || Current == Level.Low;

        /// <summary>Raised after a preset is applied (things already spawned may want to follow it).</summary>
        public static event Action Changed;

        /// <summary>Best guess for this machine, used while the player hasn't picked a preset.</summary>
        public static Level Detect()
        {
            int ram = SystemInfo.systemMemorySize;       // MB
            int vram = SystemInfo.graphicsMemorySize;    // MB (shared memory on integrated GPUs)
            if (PlatformProfile.IsMobile) return ram >= 6000 ? Level.Medium : Level.Low;
            string gpu = (SystemInfo.graphicsDeviceName ?? "").ToLowerInvariant();
            bool integrated =
                (gpu.Contains("intel") && !gpu.Contains("arc")) ||
                gpu.Contains("radeon(tm) graphics") || gpu.Contains("radeon graphics") || gpu.Contains("vega") && !gpu.Contains("rx ") ||
                gpu.Contains("microsoft basic") || gpu.Contains("llvmpipe") || gpu.Contains("swiftshader");
            if (integrated || vram < 2000 || ram < 6000 || SystemInfo.processorCount <= 2) return Level.Low;
            if (vram < 4000 || SystemInfo.processorCount <= 4) return Level.Medium;
            return Level.High;
        }

        /// <param name="setting">The saved preset, or -1 for automatic.</param>
        public static void Apply(int setting)
        {
            var level = setting < 0 ? Detect() : (Level)Mathf.Clamp(setting, 0, 3);
            Current = level;
            bool low = level == Level.Low, med = level == Level.Medium, ultra = level == Level.Ultra;

            // ---- render pipeline (URP asset; set through reflection so this assembly doesn't need URP) ----
            float scale = low ? 0.7f : med ? 0.85f : 1f;
            float shadowDist = low ? 0f : med ? 30f : ultra ? 90f : 60f;
            int cascades = low || med ? 1 : ultra ? 4 : 2;
            int msaa = low || med ? 1 : ultra ? 4 : 2;
            SetPipeline("renderScale", scale);
            SetPipeline("shadowDistance", Mathf.Max(shadowDist, 1f));
            SetPipeline("shadowCascadeCount", cascades);
            SetPipeline("msaaSampleCount", msaa);
            SetPipeline("supportsHDR", !(low || med));
            SetPipeline("maxAdditionalLightsCount", low ? 0 : med ? 2 : ultra ? 8 : 4);
            SetPipeline("mainLightShadowmapResolution", med ? 1024 : ultra ? 4096 : 2048);
            SetPipeline("supportsCameraOpaqueTexture", false);
            SetPipeline("supportsCameraDepthTexture", false);

            // ---- the same for the built-in pipeline (project not prepared yet) ----
            QualitySettings.shadows = low ? ShadowQuality.Disable : med ? ShadowQuality.HardOnly : ShadowQuality.All;
            QualitySettings.shadowDistance = shadowDist;
            QualitySettings.shadowCascades = cascades;
            QualitySettings.antiAliasing = msaa > 1 ? msaa : 0;
            QualitySettings.pixelLightCount = low ? 0 : med ? 2 : 4;

            // ---- everywhere ----
            int mipLimit = low || med ? 1 : 0;   // 1 = half resolution textures
#if UNITY_2022_2_OR_NEWER
            QualitySettings.globalTextureMipmapLimit = mipLimit;
#else
            QualitySettings.masterTextureLimit = mipLimit;
#endif
            QualitySettings.anisotropicFiltering = low ? AnisotropicFiltering.Disable : ultra ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Enable;
            QualitySettings.skinWeights = low ? SkinWeights.TwoBones : SkinWeights.FourBones;
            QualitySettings.lodBias = low ? 0.6f : med ? 0.8f : 1f;
            QualitySettings.softParticles = false;
            QualitySettings.realtimeReflectionProbes = false;

            ApplySun(level);
            Changed?.Invoke();
        }

        /// <summary>Shadows of the sun: none on DÜŞÜK, hard on ORTA, soft above.</summary>
        public static void ApplySun(Level level)
        {
            foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if (l.type != LightType.Directional) continue;
                l.shadows = level == Level.Low ? LightShadows.None : level == Level.Medium ? LightShadows.Hard : LightShadows.Soft;
            }
        }

        static void SetPipeline(string property, object value)
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) return;
            try
            {
                var p = rp.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanWrite || p.GetSetMethod() == null) return;
                p.SetValue(rp, Convert.ChangeType(value, p.PropertyType));
            }
            catch (Exception) { /* a pipeline version without this setting */ }
        }
    }
}
