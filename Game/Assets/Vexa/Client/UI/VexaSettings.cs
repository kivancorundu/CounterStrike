using System;
using System.Globalization;
using UnityEngine;

namespace Vexa.Client.UI
{
    [Serializable]
    public struct CrosshairStyle
    {
        public float Length, Gap, Thickness, Outline;
        public bool Dot, TStyle, Dynamic;
        public int ColorIndex;

        public static readonly Color[] Palette =
        {
            new Color(1f, 1f, 1f), new Color(0.29f, 1f, 0.42f), new Color(1f, 0.88f, 0.29f),
            new Color(0.29f, 0.91f, 1f), new Color(1f, 0.36f, 0.82f), new Color(1f, 0.29f, 0.29f),
        };
        public static readonly string[] PaletteNames = { "Beyaz", "Yeşil", "Sarı", "Camgöbeği", "Pembe", "Kırmızı" };

        public Color Color => Palette[Mathf.Clamp(ColorIndex, 0, Palette.Length - 1)];

        public static CrosshairStyle Classic => new CrosshairStyle { Length = 6, Gap = 3, Thickness = 2, Outline = 1, ColorIndex = 1 };
        public static CrosshairStyle DotOnly => new CrosshairStyle { Length = 0, Gap = 0, Thickness = 3, Outline = 1, Dot = true, ColorIndex = 1 };
        public static CrosshairStyle Small => new CrosshairStyle { Length = 3, Gap = 1, Thickness = 1, Outline = 0, ColorIndex = 3 };
        public static CrosshairStyle T => new CrosshairStyle { Length = 5, Gap = 2, Thickness = 1.5f, Outline = 1, Dot = true, TStyle = true, ColorIndex = 0 };

        /// <summary>Short text code so players can share crosshairs ("VX:6:-1:2:1:001:1"; ':' because gaps can be negative).</summary>
        public string ToCode()
        {
            string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
            return $"VX:{F(Length)}:{F(Gap)}:{F(Thickness)}:{F(Outline)}:{(Dot ? 1 : 0)}{(TStyle ? 1 : 0)}{(Dynamic ? 1 : 0)}:{ColorIndex}";
        }

        public static bool TryParse(string code, out CrosshairStyle s)
        {
            s = Classic;
            if (string.IsNullOrEmpty(code)) return false;
            var p = code.Trim().ToUpperInvariant().Split(':');
            if (p.Length != 7 || p[0] != "VX" || p[5].Length != 3) return false;
            bool ok = float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out s.Length)
                    & float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out s.Gap)
                    & float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out s.Thickness)
                    & float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out s.Outline)
                    & int.TryParse(p[6], out s.ColorIndex);
            s.Dot = p[5][0] == '1'; s.TStyle = p[5][1] == '1'; s.Dynamic = p[5][2] == '1';
            s.Length = Mathf.Clamp(s.Length, 0, 20); s.Gap = Mathf.Clamp(s.Gap, -3, 10);
            s.Thickness = Mathf.Clamp(s.Thickness, 0.5f, 6); s.Outline = Mathf.Clamp(s.Outline, 0, 3);
            s.ColorIndex = Mathf.Clamp(s.ColorIndex, 0, Palette.Length - 1);
            return ok;
        }
    }

    /// <summary>Player settings persisted in PlayerPrefs.</summary>
    public static class VexaSettings
    {
        public static string Name = "Oyuncu";
        public static float Sensitivity = 2f;
        public static float ZoomSensitivity = 1f;
        public static float TouchSensitivity = 0.18f;
        public static CrosshairStyle Crosshair = CrosshairStyle.Classic;
        public static int Quality = -1;          // -1 = platform default
        public static int FpsCap = 0;            // 0 = unlimited (PC) / 60 (mobile)
        public static float Volume = 0.8f;
        public static bool ShowNetStats = true;
        public static bool RadarRotate = true;
        public static float RadarZoom = 1f;
        public static float HudScale = 1f;
        public static string LastHost = "127.0.0.1";
        public static int LastPort = 27015;
        public static int LastMode = 0, LastMap = 0, LastSide = 0, LastDifficulty = 1, LastTeamSize = 5, LastTick = 64, LastDmBots = 7;

        public static event Action Changed;

        public static void Load()
        {
            Name = PlayerPrefs.GetString("vexa.name", Name);
            Sensitivity = PlayerPrefs.GetFloat("vexa.sens", Sensitivity);
            ZoomSensitivity = PlayerPrefs.GetFloat("vexa.zoomsens", ZoomSensitivity);
            TouchSensitivity = PlayerPrefs.GetFloat("vexa.touchsens", TouchSensitivity);
            if (!CrosshairStyle.TryParse(PlayerPrefs.GetString("vexa.crosshair", ""), out Crosshair)) Crosshair = CrosshairStyle.Classic;
            Quality = PlayerPrefs.GetInt("vexa.quality", Quality);
            FpsCap = PlayerPrefs.GetInt("vexa.fpscap", FpsCap);
            Volume = PlayerPrefs.GetFloat("vexa.volume", Volume);
            ShowNetStats = PlayerPrefs.GetInt("vexa.netstats", ShowNetStats ? 1 : 0) == 1;
            RadarRotate = PlayerPrefs.GetInt("vexa.radarrotate", RadarRotate ? 1 : 0) == 1;
            RadarZoom = PlayerPrefs.GetFloat("vexa.radarzoom", RadarZoom);
            HudScale = PlayerPrefs.GetFloat("vexa.hudscale", HudScale);
            LastHost = PlayerPrefs.GetString("vexa.host", LastHost);
            LastPort = PlayerPrefs.GetInt("vexa.port", LastPort);
            LastMode = PlayerPrefs.GetInt("vexa.mode", LastMode);
            LastMap = PlayerPrefs.GetInt("vexa.map", LastMap);
            LastSide = PlayerPrefs.GetInt("vexa.side", LastSide);
            LastDifficulty = PlayerPrefs.GetInt("vexa.difficulty", LastDifficulty);
            LastTeamSize = PlayerPrefs.GetInt("vexa.teamsize", LastTeamSize);
            LastTick = PlayerPrefs.GetInt("vexa.tick", LastTick);
            LastDmBots = PlayerPrefs.GetInt("vexa.dmbots", LastDmBots);
            Apply();
        }

        private static bool _dirty;
        private static float _nextFlush;
        private static int _appliedQuality = int.MinValue;

        /// <summary>Apply now; the disk write is batched (see <see cref="Flush"/>) so dragging a slider doesn't hitch.</summary>
        public static void Save()
        {
            _dirty = true;
            Apply();
        }

        /// <summary>Called every frame by the UI: writes PlayerPrefs at most once per second.</summary>
        public static void FlushIfDue()
        {
            if (!_dirty || Time.unscaledTime < _nextFlush) return;
            Flush();
        }

        public static void Flush()
        {
            if (!_dirty) return;
            _dirty = false;
            _nextFlush = Time.unscaledTime + 1f;
            PlayerPrefs.SetString("vexa.name", Name);
            PlayerPrefs.SetFloat("vexa.sens", Sensitivity);
            PlayerPrefs.SetFloat("vexa.zoomsens", ZoomSensitivity);
            PlayerPrefs.SetFloat("vexa.touchsens", TouchSensitivity);
            PlayerPrefs.SetString("vexa.crosshair", Crosshair.ToCode());
            PlayerPrefs.SetInt("vexa.quality", Quality);
            PlayerPrefs.SetInt("vexa.fpscap", FpsCap);
            PlayerPrefs.SetFloat("vexa.volume", Volume);
            PlayerPrefs.SetInt("vexa.netstats", ShowNetStats ? 1 : 0);
            PlayerPrefs.SetInt("vexa.radarrotate", RadarRotate ? 1 : 0);
            PlayerPrefs.SetFloat("vexa.radarzoom", RadarZoom);
            PlayerPrefs.SetFloat("vexa.hudscale", HudScale);
            PlayerPrefs.SetString("vexa.host", LastHost);
            PlayerPrefs.SetInt("vexa.port", LastPort);
            PlayerPrefs.SetInt("vexa.mode", LastMode);
            PlayerPrefs.SetInt("vexa.map", LastMap);
            PlayerPrefs.SetInt("vexa.side", LastSide);
            PlayerPrefs.SetInt("vexa.difficulty", LastDifficulty);
            PlayerPrefs.SetInt("vexa.teamsize", LastTeamSize);
            PlayerPrefs.SetInt("vexa.tick", LastTick);
            PlayerPrefs.SetInt("vexa.dmbots", LastDmBots);
            PlayerPrefs.Save();
        }

        public static readonly string[] QualityNames = { "DÜŞÜK", "ORTA", "YÜKSEK", "ULTRA" };
        public static readonly int[] FpsCaps = { 0, 60, 144, 240 };

        public static void Apply()
        {
            int levels = QualitySettings.names.Length;
            if (Quality >= 0 && levels > 0 && Quality != _appliedQuality)
            {
                _appliedQuality = Quality;
                QualitySettings.SetQualityLevel(Mathf.Clamp(Mathf.RoundToInt(Quality / 3f * (levels - 1)), 0, levels - 1), true);
            }
            if (Application.isMobilePlatform) Application.targetFrameRate = FpsCap > 0 ? FpsCap : 60;
            else Application.targetFrameRate = FpsCap > 0 ? FpsCap : -1;
            AudioListener.volume = Volume;
            var s = GameSession.Current;
            if (s != null)
            {
                s.Input.Sensitivity = Sensitivity;
                s.Input.TouchSensitivity = TouchSensitivity;
            }
            Changed?.Invoke();
        }
    }
}
