using System;
using UnityEngine;

namespace MultiTravel.Gameplay.Feedback
{
    /// <summary>
    /// Pure procedural sound builders for <see cref="PlacementFeedback"/>. The sample builders are deterministic and
    /// allocation-bounded (one array per call); <see cref="CreateClip"/> wraps samples in an <see cref="AudioClip"/>.
    /// All signals are mono, normalised to a peak of <c>amplitude</c> and use short attack / release ramps (no clicks).
    /// </summary>
    public static class FeedbackAudioSynth
    {
        /// <summary>Default sample rate for generated clips.</summary>
        public const int DefaultSampleRate = 44100;

        /// <summary>Bright two-note rising chime (positive placement): E6 then A6 with soft harmonics.</summary>
        public static float[] PositiveChime(int sampleRate = DefaultSampleRate, float amplitude = 0.5f)
        {
            float duration = 0.42f;
            var samples = Allocate(sampleRate, duration);
            AddTone(samples, sampleRate, 1318.5f, 0f, 0.22f, amplitude * 0.6f, 9f);
            AddTone(samples, sampleRate, 2637f, 0f, 0.18f, amplitude * 0.15f, 14f);
            AddTone(samples, sampleRate, 1760f, 0.09f, 0.33f, amplitude * 0.7f, 7f);
            AddTone(samples, sampleRate, 3520f, 0.09f, 0.25f, amplitude * 0.12f, 12f);
            Normalize(samples, amplitude);
            return samples;
        }

        /// <summary>Low, slightly detuned square-ish buzz (legacy negative sound; <see cref="WrongThud"/> is used in the game).</summary>
        public static float[] NegativeBuzz(int sampleRate = DefaultSampleRate, float amplitude = 0.45f)
        {
            float duration = 0.38f;
            var samples = Allocate(sampleRate, duration);
            int count = samples.Length;
            int attack = Mathf.Max(1, (int)(0.008f * sampleRate));
            int release = Mathf.Max(1, (int)(0.06f * sampleRate));
            for (int i = 0; i < count; i++)
            {
                float time = (float)i / sampleRate;
                float a = SoftSquare(2f * Mathf.PI * 140f * time);
                float b = SoftSquare(2f * Mathf.PI * 147f * time);
                float wobble = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 18f * time);
                samples[i] = (a + b) * 0.5f * wobble * Envelope(i, count, attack, release);
            }

            Normalize(samples, amplitude);
            return samples;
        }

        /// <summary>
        /// Soft wrong-item sound: a low dull thud (decaying 85 Hz body with a short low-passed noise knock) followed by a gentle
        /// falling tone (330 → 196 Hz). Deliberately unharsh (replaces the old buzz in <see cref="PlacementFeedback"/>).
        /// </summary>
        public static float[] WrongThud(int sampleRate = DefaultSampleRate, float amplitude = 0.42f)
        {
            float duration = 0.46f;
            var samples = Allocate(sampleRate, duration);
            int count = samples.Length;

            // Thud: pitch-dropping sine body + low-passed deterministic noise knock.
            var noise = new System.Random(1234);
            float lowPassed = 0f;
            float bodyPhase = 0f;
            int thudLength = Mathf.Min(count, (int)(0.16f * sampleRate));
            int attack = Mathf.Max(1, (int)(0.003f * sampleRate));
            for (int i = 0; i < thudLength; i++)
            {
                float time = (float)i / sampleRate;
                float frequency = Mathf.Lerp(110f, 70f, Mathf.Clamp01(time / 0.12f));
                bodyPhase += 2f * Mathf.PI * frequency / sampleRate;
                float body = Mathf.Sin(bodyPhase) * Mathf.Exp(-22f * time);
                float white = (float)(noise.NextDouble() * 2.0 - 1.0);
                lowPassed += 0.08f * (white - lowPassed);
                float knock = lowPassed * Mathf.Exp(-60f * time) * 2.5f;
                float gain = i < attack ? (float)i / attack : 1f;
                samples[i] += (body * 0.9f + knock) * gain;
            }

            // Falling tone: soft sine glide with a slow attack so it reads as "oops", not as an alarm.
            int toneStart = (int)(0.07f * sampleRate);
            int toneLength = Mathf.Max(1, count - toneStart);
            int toneAttack = Mathf.Max(1, (int)(0.03f * sampleRate));
            int toneRelease = Mathf.Max(1, (int)(0.12f * sampleRate));
            float tonePhase = 0f;
            for (int i = 0; i < toneLength; i++)
            {
                float k = (float)i / toneLength;
                float frequency = Mathf.Lerp(330f, 196f, k * k * (3f - 2f * k));
                tonePhase += 2f * Mathf.PI * frequency / sampleRate;
                float tone = Mathf.Sin(tonePhase) + 0.18f * Mathf.Sin(2f * tonePhase);
                samples[toneStart + i] += tone * 0.32f * Envelope(i, toneLength, toneAttack, toneRelease);
            }

            Normalize(samples, amplitude);
            return samples;
        }

        /// <summary>Very short wooden tick (placement settle, removal, countdown).</summary>
        public static float[] Tick(int sampleRate = DefaultSampleRate, float amplitude = 0.4f)
        {
            float duration = 0.06f;
            var samples = Allocate(sampleRate, duration);
            AddTone(samples, sampleRate, 1900f, 0f, duration, amplitude, 70f);
            AddTone(samples, sampleRate, 950f, 0f, duration, amplitude * 0.5f, 55f);
            Normalize(samples, amplitude);
            return samples;
        }

        /// <summary>Creates a mono clip from samples (call on the main thread).</summary>
        public static AudioClip CreateClip(string name, float[] samples, int sampleRate = DefaultSampleRate)
        {
            if (samples == null || samples.Length == 0)
            {
                throw new ArgumentException("Samples are required.", nameof(samples));
            }

            var clip = AudioClip.Create(name, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static float[] Allocate(int sampleRate, float seconds)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            return new float[Mathf.Max(1, Mathf.CeilToInt(sampleRate * seconds))];
        }

        private static void AddTone(float[] buffer, int sampleRate, float frequency, float start, float length, float gain, float decay)
        {
            int from = Mathf.Clamp((int)(start * sampleRate), 0, buffer.Length);
            int count = Mathf.Clamp((int)(length * sampleRate), 0, buffer.Length - from);
            int attack = Mathf.Max(1, (int)(0.004f * sampleRate));
            int release = Mathf.Max(1, (int)(0.02f * sampleRate));
            for (int i = 0; i < count; i++)
            {
                float time = (float)i / sampleRate;
                float value = Mathf.Sin(2f * Mathf.PI * frequency * time) * Mathf.Exp(-decay * time);
                buffer[from + i] += value * gain * Envelope(i, count, attack, release);
            }
        }

        private static float SoftSquare(float phase)
        {
            float s = Mathf.Sin(phase);
            return s / (Mathf.Abs(s) + 0.2f);
        }

        private static float Envelope(int index, int count, int attack, int release)
        {
            float gain = 1f;
            if (index < attack)
            {
                gain = (float)index / attack;
            }

            int fromEnd = count - 1 - index;
            if (fromEnd < release)
            {
                gain = Mathf.Min(gain, (float)fromEnd / release);
            }

            return gain;
        }

        private static void Normalize(float[] samples, float peak)
        {
            float max = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float abs = Mathf.Abs(samples[i]);
                if (abs > max)
                {
                    max = abs;
                }
            }

            if (max <= 1e-6f)
            {
                return;
            }

            float scale = peak / max;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] *= scale;
            }
        }
    }
}
