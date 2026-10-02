using System;
using UnityEngine;

namespace MultiTravel.Gameplay.Audio
{
    /// <summary>
    /// Deterministic procedural fallback clips for <see cref="AudioDirector"/> (OVERHAUL_PLAN §5): used only when the
    /// corresponding serialized <c>AudioClip[]</c> is empty. Every builder allocates exactly one sample array and returns
    /// a normalised mono clip at <see cref="SampleRate"/>; all noise comes from a seeded generator so two stations sound
    /// identical. Filters are simple one-pole / biquad stages, which is plenty for short foley.
    /// </summary>
    public static class ProceduralAudio
    {
        /// <summary>Sample rate of every generated clip.</summary>
        public const int SampleRate = 44100;

        /// <summary>Soft fabric thump: low-passed noise burst with a fast decay (cloth, towel, socks).</summary>
        public static AudioClip Cloth()
        {
            var s = Allocate(0.16f);
            var rng = new Rng(11);
            float lp = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float env = Mathf.Exp(-t * 26f) * Attack(i, 0.003f);
                lp += (rng.Next() - lp) * 0.06f; // ~450 Hz one-pole low-pass
                s[i] = lp * env * 2.2f + Mathf.Sin(2f * Mathf.PI * 95f * t) * Mathf.Exp(-t * 40f) * 0.35f;
            }

            return Create("MT_Foley_Cloth", s, 0.55f);
        }

        /// <summary>Low leather thump: pitch-dropping sine with a short dull noise tail (shoes, bags).</summary>
        public static AudioClip Leather()
        {
            var s = Allocate(0.22f);
            var rng = new Rng(23);
            float lp = 0f;
            float phase = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float frequency = Mathf.Lerp(140f, 62f, Mathf.Clamp01(t * 9f));
                phase += 2f * Mathf.PI * frequency / SampleRate;
                float body = Mathf.Sin(phase) * Mathf.Exp(-t * 18f);
                lp += (rng.Next() - lp) * 0.12f;
                float slap = lp * Mathf.Exp(-t * 70f) * 0.9f;
                s[i] = (body + slap) * Attack(i, 0.002f);
            }

            return Create("MT_Foley_Leather", s, 0.7f);
        }

        /// <summary>Hard object: sharp click followed by a resonant thump (laptop, phone, toys).</summary>
        public static AudioClip Hard()
        {
            var s = Allocate(0.2f);
            var rng = new Rng(37);
            float hp = 0f;
            float previous = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float white = rng.Next();
                hp = 0.92f * (hp + white - previous); // high-pass for the click
                previous = white;
                float click = hp * Mathf.Exp(-t * 400f) * 1.4f;
                float thump = Mathf.Sin(2f * Mathf.PI * 210f * t) * Mathf.Exp(-t * 32f) * 0.8f
                              + Mathf.Sin(2f * Mathf.PI * 480f * t) * Mathf.Exp(-t * 60f) * 0.3f;
                s[i] = (click + thump) * Attack(i, 0.0008f);
            }

            return Create("MT_Foley_Hard", s, 0.65f);
        }

        /// <summary>Short bright metallic ping with two inharmonic partials (jewellery, cufflinks).</summary>
        public static AudioClip Metal()
        {
            var s = Allocate(0.34f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float a = Mathf.Sin(2f * Mathf.PI * 2380f * t) * Mathf.Exp(-t * 14f);
                float b = Mathf.Sin(2f * Mathf.PI * 3170f * t) * Mathf.Exp(-t * 20f) * 0.6f;
                float c = Mathf.Sin(2f * Mathf.PI * 5210f * t) * Mathf.Exp(-t * 34f) * 0.3f;
                s[i] = (a + b + c) * Attack(i, 0.0006f);
            }

            return Create("MT_Foley_Metal", s, 0.4f);
        }

        /// <summary>Dry paper flutter: band-passed noise with two short bursts (documents, books).</summary>
        public static AudioClip Paper()
        {
            var s = Allocate(0.14f);
            var rng = new Rng(53);
            float lp = 0f;
            float lp2 = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float white = rng.Next();
                lp += (white - lp) * 0.45f;   // low-pass ~4 kHz
                lp2 += (lp - lp2) * 0.08f;    // subtract slow part → band-pass
                float band = lp - lp2;
                float env = Mathf.Exp(-t * 45f) + 0.5f * Mathf.Exp(-Mathf.Abs(t - 0.05f) * 160f);
                s[i] = band * env * Attack(i, 0.002f);
            }

            return Create("MT_Foley_Paper", s, 0.45f);
        }

        /// <summary>
        /// Seamless room tone: leaky-integrated (brown) noise, low-passed, 4 s loop with a cross-faded seam, peak at −30 dB.
        /// </summary>
        public static AudioClip RoomTone()
        {
            var s = Allocate(4f);
            var rng = new Rng(71);
            float brown = 0f;
            float lp = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                brown = brown * 0.998f + rng.Next() * 0.02f;
                lp += (brown - lp) * 0.02f;
                s[i] = lp;
            }

            // Cross-fade the last 0.25 s into the first 0.25 s so the loop point is inaudible.
            int fade = (int)(0.25f * SampleRate);
            for (int i = 0; i < fade; i++)
            {
                float k = (float)i / fade;
                int tail = s.Length - fade + i;
                s[tail] = s[tail] * (1f - k) + s[i] * k;
            }

            return Create("MT_RoomTone", s, DecibelsToAmplitude(-30f));
        }

        /// <summary>Suitcase latch: two tight clicks 60 ms apart.</summary>
        public static AudioClip Latch()
        {
            var s = Allocate(0.18f);
            var rng = new Rng(89);
            float hp = 0f;
            float previous = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float white = rng.Next();
                hp = 0.9f * (hp + white - previous);
                previous = white;
                float env = Mathf.Exp(-t * 320f) + 0.8f * Mathf.Exp(-Mathf.Max(0f, t - 0.06f) * 320f) * (t >= 0.06f ? 1f : 0f);
                float tone = Mathf.Sin(2f * Mathf.PI * 1450f * t) * Mathf.Exp(-t * 90f) * 0.4f;
                s[i] = (hp * env + tone) * Attack(i, 0.0005f);
            }

            return Create("MT_Latch", s, 0.55f);
        }

        /// <summary>Zip: noise sweep whose band-pass centre rises from 600 Hz to 2.4 kHz over 0.45 s.</summary>
        public static AudioClip Zip()
        {
            var s = Allocate(0.45f);
            var rng = new Rng(97);
            float low = 0f;
            float band = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float k = t / 0.45f;
                float centre = Mathf.Lerp(600f, 2400f, k);
                float f = Mathf.Clamp01(2f * Mathf.Sin(Mathf.PI * centre / SampleRate));
                float white = rng.Next();
                // State-variable band-pass (Chamberlin), q ≈ 0.6.
                low += f * band;
                float high = white - low - band * 0.6f;
                band += f * high;
                float ripple = 0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * 38f * t); // teeth
                float env = Mathf.Sin(Mathf.PI * k);
                s[i] = band * ripple * env;
            }

            return Create("MT_Zip", s, 0.5f);
        }

        /// <summary>Countdown beep: A5 transposed by <paramref name="semitones"/> (0, +2, +4), 120 ms, soft triangle-ish timbre.</summary>
        public static AudioClip Beep(int semitones, float seconds = 0.12f)
        {
            float frequency = 880f * Mathf.Pow(2f, semitones / 12f);
            var s = Allocate(seconds);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float env = Attack(i, 0.004f) * Release(i, s.Length, 0.03f);
                s[i] = (Mathf.Sin(2f * Mathf.PI * frequency * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * frequency * 2f * t)) * env;
            }

            return Create("MT_Beep_" + semitones, s, 0.5f);
        }

        /// <summary>"BAŞLA!" cue: bright two-note upward call (A5 → E6), 0.35 s.</summary>
        public static AudioClip Start()
        {
            var s = Allocate(0.36f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float frequency = t < 0.1f ? 880f : 1318.5f;
                float env = t < 0.1f ? Mathf.Exp(-t * 8f) : Mathf.Exp(-(t - 0.1f) * 7f);
                s[i] = (Mathf.Sin(2f * Mathf.PI * frequency * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * frequency * 2f * t)) * env * Attack(i, 0.003f) * Release(i, s.Length, 0.04f);
            }

            return Create("MT_Start", s, 0.6f);
        }

        /// <summary>Success motif: E5 G5 B5 E6 arpeggio with a sustained final chord, 1.1 s.</summary>
        public static AudioClip Success()
        {
            float[] notes = { 659.25f, 783.99f, 987.77f, 1318.5f };
            var s = Allocate(1.1f);
            for (int n = 0; n < notes.Length; n++)
            {
                float start = n * 0.11f;
                float length = n == notes.Length - 1 ? 0.75f : 0.28f;
                int from = (int)(start * SampleRate);
                int count = Mathf.Min((int)(length * SampleRate), s.Length - from);
                for (int i = 0; i < count; i++)
                {
                    float t = (float)i / SampleRate;
                    float env = Mathf.Exp(-t * (n == notes.Length - 1 ? 3.5f : 7f)) * Attack(i, 0.004f) * Release(i, count, 0.05f);
                    s[from + i] += (Mathf.Sin(2f * Mathf.PI * notes[n] * t) + 0.2f * Mathf.Sin(2f * Mathf.PI * notes[n] * 2f * t) + 0.08f * Mathf.Sin(2f * Mathf.PI * notes[n] * 3f * t)) * env;
                }
            }

            return Create("MT_Success", s, 0.55f);
        }

        /// <summary>Faint stopwatch tick: 3 ms high-passed click, meant to be played at low volume.</summary>
        public static AudioClip StopwatchTick()
        {
            var s = Allocate(0.03f);
            var rng = new Rng(131);
            float hp = 0f;
            float previous = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float white = rng.Next();
                hp = 0.85f * (hp + white - previous);
                previous = white;
                s[i] = (hp * Mathf.Exp(-t * 900f) + Mathf.Sin(2f * Mathf.PI * 3200f * t) * Mathf.Exp(-t * 500f) * 0.5f) * Attack(i, 0.0003f);
            }

            return Create("MT_StopwatchTick", s, 0.35f);
        }

        /// <summary>Soft UI confirmation blip (tutorial step done).</summary>
        public static AudioClip Confirm()
        {
            var s = Allocate(0.18f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float frequency = t < 0.07f ? 1046.5f : 1568f;
                s[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * Mathf.Exp(-t * 14f) * Attack(i, 0.003f) * Release(i, s.Length, 0.03f);
            }

            return Create("MT_Confirm", s, 0.4f);
        }

        /// <summary>Converts decibels (0 dB = full scale) to a linear amplitude.</summary>
        public static float DecibelsToAmplitude(float decibels)
        {
            return Mathf.Pow(10f, decibels / 20f);
        }

        // ----- helpers -----

        private static float[] Allocate(float seconds)
        {
            return new float[Mathf.Max(1, Mathf.CeilToInt(SampleRate * seconds))];
        }

        private static float T(int index)
        {
            return (float)index / SampleRate;
        }

        private static float Attack(int index, float seconds)
        {
            int samples = Mathf.Max(1, (int)(seconds * SampleRate));
            return index >= samples ? 1f : (float)index / samples;
        }

        private static float Release(int index, int count, float seconds)
        {
            int samples = Mathf.Max(1, (int)(seconds * SampleRate));
            int fromEnd = count - 1 - index;
            return fromEnd >= samples ? 1f : Mathf.Max(0f, (float)fromEnd / samples);
        }

        private static AudioClip Create(string name, float[] samples, float peak)
        {
            float max = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float abs = Math.Abs(samples[i]);
                if (abs > max)
                {
                    max = abs;
                }
            }

            if (max > 1e-6f)
            {
                float scale = peak / max;
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] *= scale;
                }
            }

            var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>Tiny seeded xorshift generator returning white noise in [-1, 1].</summary>
        private struct Rng
        {
            private uint state;

            public Rng(uint seed)
            {
                state = seed * 2654435761u + 0x9E3779B9u;
            }

            public float Next()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return (state & 0xFFFFFF) / 8388607.5f - 1f;
            }
        }
    }
}
