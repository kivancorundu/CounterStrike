using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vexa.Client.Audio
{
    /// <summary>
    /// Procedurally synthesized placeholder sounds (no audio files needed). Each clip is built once from
    /// noise, filters, envelopes and oscillators. Recorded / designed sounds replace these in the audio pass;
    /// the names stay the same so <see cref="GameAudio"/> doesn't change.
    /// </summary>
    public static class SoundSynth
    {
        public const int Rate = 44100;
        static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();

        public static AudioClip Get(string name)
        {
            if (_clips.TryGetValue(name, out var c)) return c;
            var data = Build(name);
            if (data == null) { _clips[name] = null; return null; }
            Normalize(data, 0.89f);
            c = AudioClip.Create(name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            _clips[name] = c;
            return c;
        }

        // ---------------- building blocks ----------------

        sealed class Rng
        {
            uint _s;
            public Rng(int seed) { _s = (uint)seed * 2654435761u + 1; }
            public float Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return (_s & 0xFFFFFF) / (float)0x800000 - 1f; }
        }

        static int N(float seconds) => Mathf.Max(1, (int)(seconds * Rate));

        /// <summary>Exponential decay envelope with a short linear attack.</summary>
        static float Env(int i, float attack, float tau)
        {
            float t = i / (float)Rate;
            if (t < attack) return t / attack;
            return Mathf.Exp(-(t - attack) / tau);
        }

        /// <summary>Noise through a one-pole low-pass (cutoff may sweep), scaled by an envelope.</summary>
        static void AddNoise(float[] buf, int seed, float gain, float attack, float tau, float cutoffStart, float cutoffEnd, bool highpass = false, int offset = 0)
        {
            var r = new Rng(seed);
            float lp = 0;
            for (int i = 0; i + offset < buf.Length; i++)
            {
                float t = i / (float)buf.Length;
                float fc = Mathf.Lerp(cutoffStart, cutoffEnd, t);
                float a = 1f - Mathf.Exp(-2f * Mathf.PI * fc / Rate);
                float n = r.Next();
                lp += a * (n - lp);
                float v = highpass ? n - lp : lp;
                buf[i + offset] += v * gain * Env(i, attack, tau);
            }
        }

        /// <summary>Sine with an exponential pitch glide.</summary>
        static void AddTone(float[] buf, float gain, float f0, float f1, float glideTau, float attack, float tau, int offset = 0, float duration = -1)
        {
            double phase = 0;
            int end = duration > 0 ? Mathf.Min(buf.Length, offset + N(duration)) : buf.Length;
            for (int i = 0; i + offset < end; i++)
            {
                float t = i / (float)Rate;
                float f = f1 + (f0 - f1) * Mathf.Exp(-t / glideTau);
                phase += 2 * Math.PI * f / Rate;
                buf[i + offset] += (float)Math.Sin(phase) * gain * Env(i, attack, tau);
            }
        }

        /// <summary>Very short broadband click (mechanical contact).</summary>
        static void AddClick(float[] buf, int seed, float gain, int offset, float ringFreq = 0, float ringTau = 0.01f)
        {
            AddNoise(buf, seed, gain, 0.0003f, 0.004f, 9000f, 6000f, true, offset);
            if (ringFreq > 0) AddTone(buf, gain * 0.5f, ringFreq, ringFreq, 1f, 0.0005f, ringTau, offset);
        }

        static void Normalize(float[] buf, float peak)
        {
            float m = 0;
            foreach (var v in buf) m = Mathf.Max(m, Mathf.Abs(v));
            if (m < 1e-6f) return;
            float k = peak / m;
            for (int i = 0; i < buf.Length; i++) buf[i] = (float)Math.Tanh(buf[i] * k * 1.1f) / (float)Math.Tanh(1.1f) * peak;
        }

        // ---------------- the sound list ----------------

        static float[] Build(string name)
        {
            float[] b;
            int seed = name.GetHashCode();
            switch (name)
            {
                // ---- guns: crack (bright noise) + body (low thump) + tail (room) ----
                case "gun_pistol":
                    b = new float[N(0.45f)];
                    AddNoise(b, seed, 1.0f, 0.0005f, 0.035f, 7000, 2500);
                    AddTone(b, 0.7f, 180, 70, 0.02f, 0.001f, 0.06f);
                    AddNoise(b, seed + 1, 0.25f, 0.01f, 0.15f, 900, 400);
                    return b;
                case "gun_smg":
                    b = new float[N(0.35f)];
                    AddNoise(b, seed, 1.0f, 0.0005f, 0.028f, 6500, 2500);
                    AddTone(b, 0.6f, 160, 70, 0.02f, 0.001f, 0.05f);
                    AddNoise(b, seed + 1, 0.2f, 0.01f, 0.11f, 800, 400);
                    return b;
                case "gun_rifle":
                    b = new float[N(0.6f)];
                    AddNoise(b, seed, 1.0f, 0.0004f, 0.05f, 6000, 1800);
                    AddTone(b, 0.9f, 120, 48, 0.03f, 0.001f, 0.09f);
                    AddNoise(b, seed + 1, 0.35f, 0.015f, 0.22f, 700, 300);
                    return b;
                case "gun_sniper":
                    b = new float[N(1.1f)];
                    AddNoise(b, seed, 1.2f, 0.0003f, 0.06f, 9000, 1500);
                    AddTone(b, 1.0f, 100, 38, 0.05f, 0.001f, 0.14f);
                    AddNoise(b, seed + 1, 0.45f, 0.02f, 0.42f, 600, 200);
                    return b;
                case "gun_shotgun":
                    b = new float[N(0.8f)];
                    AddNoise(b, seed, 1.1f, 0.0005f, 0.08f, 4500, 1200);
                    AddTone(b, 1.0f, 90, 40, 0.04f, 0.001f, 0.12f);
                    AddNoise(b, seed + 1, 0.4f, 0.02f, 0.3f, 600, 250);
                    return b;
                case "gun_heavy":
                    b = new float[N(0.5f)];
                    AddNoise(b, seed, 1.0f, 0.0004f, 0.045f, 5500, 1800);
                    AddTone(b, 0.9f, 110, 45, 0.03f, 0.001f, 0.08f);
                    AddNoise(b, seed + 1, 0.3f, 0.015f, 0.18f, 700, 300);
                    return b;
                case "gun_silenced":
                    b = new float[N(0.25f)];
                    AddNoise(b, seed, 0.8f, 0.001f, 0.03f, 3000, 1200);
                    AddClick(b, seed + 2, 0.5f, 0, 2400, 0.02f);
                    AddTone(b, 0.3f, 220, 120, 0.02f, 0.001f, 0.03f);
                    return b;
                case "knife":
                    b = new float[N(0.22f)];
                    AddNoise(b, seed, 0.7f, 0.04f, 0.05f, 1500, 6000, true);
                    return b;
                case "dryfire":
                    b = new float[N(0.12f)];
                    AddClick(b, seed, 0.9f, 0, 1800, 0.015f);
                    return b;
                case "reload":
                    b = new float[N(1.4f)];
                    AddClick(b, seed, 0.8f, N(0.1f), 900, 0.03f);     // magazine out
                    AddNoise(b, seed + 3, 0.15f, 0.05f, 0.08f, 1500, 800, false, N(0.25f));
                    AddClick(b, seed + 1, 1.0f, N(0.75f), 1100, 0.04f); // magazine in
                    AddClick(b, seed + 2, 0.9f, N(1.15f), 1600, 0.03f); // bolt
                    return b;
                case "deploy":
                    b = new float[N(0.3f)];
                    AddNoise(b, seed, 0.3f, 0.02f, 0.06f, 2500, 1500);
                    AddClick(b, seed + 1, 0.7f, N(0.15f), 1300, 0.02f);
                    return b;

                // ---- movement ----
                case "step_concrete": case "step_concrete2":
                    b = new float[N(0.18f)];
                    AddNoise(b, seed, 1.0f, 0.002f, 0.025f, 900, 300);
                    AddClick(b, seed + 1, 0.25f, 0);
                    return b;
                case "step_wood": case "step_wood2":
                    b = new float[N(0.22f)];
                    AddNoise(b, seed, 0.8f, 0.002f, 0.03f, 700, 300);
                    AddTone(b, 0.5f, 210, 160, 0.03f, 0.002f, 0.05f);
                    return b;
                case "step_metal": case "step_metal2":
                    b = new float[N(0.35f)];
                    AddNoise(b, seed, 0.7f, 0.001f, 0.02f, 2500, 900);
                    AddTone(b, 0.35f, 940, 900, 1f, 0.001f, 0.12f);
                    AddTone(b, 0.2f, 1530, 1500, 1f, 0.001f, 0.08f);
                    return b;
                case "step_sand": case "step_sand2":
                    b = new float[N(0.25f)];
                    AddNoise(b, seed, 0.9f, 0.01f, 0.05f, 2500, 900);
                    return b;
                case "land":
                    b = new float[N(0.35f)];
                    AddNoise(b, seed, 1.0f, 0.002f, 0.05f, 700, 200);
                    AddTone(b, 0.8f, 90, 50, 0.03f, 0.002f, 0.07f);
                    return b;

                // ---- grenades & bomb ----
                case "explosion":
                    b = new float[N(2.2f)];
                    AddNoise(b, seed, 1.2f, 0.002f, 0.35f, 4000, 150);
                    AddTone(b, 1.2f, 70, 28, 0.12f, 0.003f, 0.45f);
                    AddNoise(b, seed + 1, 0.3f, 0.2f, 0.8f, 300, 120);
                    return b;
                case "c4_explosion":
                    b = new float[N(3.5f)];
                    AddNoise(b, seed, 1.3f, 0.003f, 0.6f, 3000, 100);
                    AddTone(b, 1.4f, 55, 22, 0.2f, 0.004f, 0.8f);
                    AddNoise(b, seed + 1, 0.4f, 0.3f, 1.4f, 250, 90);
                    return b;
                case "flashbang":
                    b = new float[N(0.9f)];
                    AddNoise(b, seed, 1.2f, 0.0005f, 0.07f, 9000, 3000);
                    AddTone(b, 0.6f, 160, 60, 0.03f, 0.001f, 0.1f);
                    AddNoise(b, seed + 1, 0.2f, 0.02f, 0.3f, 1200, 500);
                    return b;
                case "flash_ring":
                    b = new float[N(3f)];
                    AddTone(b, 0.5f, 3400, 3300, 1f, 0.05f, 1.2f);
                    AddTone(b, 0.2f, 3410, 3310, 1f, 0.05f, 1.2f);
                    return b;
                case "smoke":
                    b = new float[N(2.5f)];
                    AddNoise(b, seed, 0.8f, 0.05f, 0.9f, 3500, 1200, true);
                    AddNoise(b, seed + 1, 0.4f, 0.1f, 1.2f, 600, 300);
                    return b;
                case "molotov":
                    b = new float[N(1.0f)];
                    AddClick(b, seed, 1.0f, 0, 2600, 0.05f);  // glass
                    AddClick(b, seed + 1, 0.6f, N(0.02f), 3800, 0.04f);
                    AddNoise(b, seed + 2, 0.9f, 0.05f, 0.4f, 1500, 500, false, N(0.05f)); // whoosh
                    return b;
                case "fire_loop":
                    {
                        b = new float[N(2f)];
                        var r = new Rng(seed);
                        float lp = 0;
                        for (int i = 0; i < b.Length; i++) { lp += 0.02f * (r.Next() - lp); b[i] = lp * 3f; }
                        for (int k = 0; k < 70; k++) AddClick(b, seed + 10 + k, 0.15f + 0.25f * Mathf.Abs(r.Next()), (int)(Mathf.Abs(r.Next()) * (b.Length - N(0.02f))));
                        // crossfade the ends so the loop is seamless
                        int fade = N(0.05f);
                        for (int i = 0; i < fade; i++) { float t = i / (float)fade; b[i] = b[i] * t + b[b.Length - fade + i] * (1 - t); }
                        Array.Resize(ref b, b.Length - fade);
                        return b;
                    }
                case "nade_bounce":
                    b = new float[N(0.15f)];
                    AddClick(b, seed, 0.8f, 0, 2100, 0.03f);
                    return b;
                case "pin":
                    b = new float[N(0.2f)];
                    AddClick(b, seed, 0.6f, 0, 3000, 0.02f);
                    AddClick(b, seed + 1, 0.4f, N(0.08f), 2400, 0.02f);
                    return b;
                case "bomb_beep":
                    b = new float[N(0.12f)];
                    AddTone(b, 0.8f, 2240, 2240, 1f, 0.002f, 0.04f, 0, 0.09f);
                    return b;
                case "bomb_planted":
                    b = new float[N(0.6f)];
                    for (int k = 0; k < 3; k++) AddTone(b, 0.7f, 2000, 2000, 1f, 0.002f, 0.03f, N(0.12f * k), 0.08f);
                    return b;
                case "bomb_defused":
                    b = new float[N(0.7f)];
                    AddTone(b, 0.7f, 900, 900, 1f, 0.002f, 0.08f, 0, 0.15f);
                    AddTone(b, 0.7f, 1350, 1350, 1f, 0.002f, 0.12f, N(0.16f), 0.3f);
                    return b;
                case "bomb_press":
                    b = new float[N(0.12f)];
                    AddClick(b, seed, 0.5f, 0, 1400, 0.01f);
                    AddTone(b, 0.3f, 1700, 1700, 1f, 0.002f, 0.02f, 0, 0.05f);
                    return b;

                // ---- feedback ----
                case "hit_body":
                    b = new float[N(0.12f)];
                    AddNoise(b, seed, 0.8f, 0.001f, 0.025f, 1200, 500);
                    AddTone(b, 0.5f, 160, 90, 0.02f, 0.001f, 0.03f);
                    return b;
                case "hit_head":
                    b = new float[N(0.45f)];
                    AddTone(b, 0.7f, 2900, 2850, 1f, 0.0008f, 0.09f);
                    AddTone(b, 0.35f, 4350, 4300, 1f, 0.0008f, 0.06f);
                    AddClick(b, seed, 0.5f, 0);
                    return b;
                case "hurt":
                    b = new float[N(0.25f)];
                    AddNoise(b, seed, 1.0f, 0.002f, 0.05f, 500, 200);
                    AddTone(b, 0.6f, 120, 60, 0.04f, 0.002f, 0.08f);
                    return b;
                case "kill":
                    b = new float[N(0.3f)];
                    AddTone(b, 0.5f, 1200, 1200, 1f, 0.002f, 0.05f, 0, 0.07f);
                    AddTone(b, 0.5f, 1800, 1800, 1f, 0.002f, 0.08f, N(0.07f), 0.15f);
                    return b;

                // ---- round / UI ----
                case "round_win":
                    b = new float[N(1.4f)];
                    { float[] notes = { 523.3f, 659.3f, 784f, 1046.5f }; for (int k = 0; k < notes.Length; k++) AddTone(b, 0.45f, notes[k], notes[k], 1f, 0.01f, 0.35f, N(0.11f * k)); }
                    return b;
                case "round_lose":
                    b = new float[N(1.4f)];
                    { float[] notes = { 440f, 392f, 349.2f, 293.7f }; for (int k = 0; k < notes.Length; k++) AddTone(b, 0.45f, notes[k], notes[k], 1f, 0.01f, 0.35f, N(0.13f * k)); }
                    return b;
                case "round_start":
                    b = new float[N(0.6f)];
                    AddTone(b, 0.5f, 660, 660, 1f, 0.005f, 0.12f, 0, 0.12f);
                    AddTone(b, 0.5f, 990, 990, 1f, 0.005f, 0.2f, N(0.14f));
                    return b;
                case "buy":
                    b = new float[N(0.25f)];
                    AddTone(b, 0.5f, 1568, 1568, 1f, 0.002f, 0.04f, 0, 0.06f);
                    AddTone(b, 0.5f, 2093, 2093, 1f, 0.002f, 0.07f, N(0.06f));
                    return b;
                case "denied":
                    b = new float[N(0.25f)];
                    AddTone(b, 0.5f, 330, 300, 0.1f, 0.003f, 0.08f, 0, 0.2f);
                    return b;
                case "ui_click":
                    b = new float[N(0.06f)];
                    AddTone(b, 0.5f, 1800, 1200, 0.01f, 0.001f, 0.015f);
                    AddClick(b, seed, 0.3f, 0);
                    return b;
                case "ui_hover":
                    b = new float[N(0.04f)];
                    AddTone(b, 0.25f, 2600, 2600, 1f, 0.001f, 0.01f);
                    return b;
                case "chat":
                    b = new float[N(0.12f)];
                    AddTone(b, 0.35f, 1320, 1320, 1f, 0.002f, 0.03f, 0, 0.05f);
                    AddTone(b, 0.35f, 1760, 1760, 1f, 0.002f, 0.04f, N(0.05f));
                    return b;
                default:
                    return null;
            }
        }
    }
}
