using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace MultiTravel.Gameplay.Common
{
    /// <summary>One haptic impulse (amplitude 0..1, duration in seconds).</summary>
    public readonly struct HapticPulse
    {
        public HapticPulse(float amplitude, float duration)
        {
            Amplitude = amplitude;
            Duration = duration;
        }

        public float Amplitude { get; }

        public float Duration { get; }
    }

    /// <summary>
    /// Haptic table of the experience (OVERHAUL_PLAN §5) and the single helper that sends an impulse to the controller that
    /// owns an interactor. Hands have no haptics: every call is a safe no-op for them.
    /// </summary>
    public static class Haptics
    {
        /// <summary>Hover tick when a hand / controller starts hovering an item: 0.08 for 15 ms.</summary>
        public static readonly HapticPulse Hover = new HapticPulse(0.08f, 0.015f);

        /// <summary>Grab confirmation: 0.35 for 40 ms.</summary>
        public static readonly HapticPulse Grab = new HapticPulse(0.35f, 0.04f);

        /// <summary>Correct item placed, first pulse: 0.5 for 30 ms.</summary>
        public static readonly HapticPulse CorrectFirst = new HapticPulse(0.5f, 0.03f);

        /// <summary>Correct item placed, second pulse: 0.3 for 60 ms.</summary>
        public static readonly HapticPulse CorrectSecond = new HapticPulse(0.3f, 0.06f);

        /// <summary>Delay between the start of the two correct-placement pulses (seconds).</summary>
        public const float CorrectSecondDelay = 0.09f;

        /// <summary>Wrong item placed: 0.6 for 90 ms.</summary>
        public static readonly HapticPulse Wrong = new HapticPulse(0.6f, 0.09f);

        /// <summary>Sends <paramref name="pulse"/> to the controller owning <paramref name="interactor"/>.</summary>
        public static bool Send(Transform interactor, HapticPulse pulse)
        {
            return Send(interactor, pulse.Amplitude, pulse.Duration);
        }

        /// <summary>
        /// Sends a haptic impulse to the controller owning <paramref name="interactor"/> through <see cref="HapticImpulsePlayer"/>,
        /// falling back to an <see cref="IXRHapticImpulseProvider"/> in the parents. Returns false when nothing could vibrate.
        /// </summary>
        public static bool Send(Transform interactor, float amplitude, float duration)
        {
            if (interactor == null || amplitude <= 0f || duration <= 0f)
            {
                return false;
            }

            var player = interactor.GetComponentInParent<HapticImpulsePlayer>(true);
            if (player != null && player.SendHapticImpulse(amplitude, duration))
            {
                return true;
            }

            var provider = interactor.GetComponentInParent<IXRHapticImpulseProvider>(true);
            if (provider == null)
            {
                return false;
            }

            var group = provider.GetChannelGroup();
            if (group == null || group.channelCount == 0)
            {
                return false;
            }

            var channel = group.GetChannel();
            return channel != null && channel.SendHapticImpulse(amplitude, duration, 0f);
        }
    }
}
