using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiTravel.Operator.UI
{
    /// <summary>Receiver for custom value tweens started with <see cref="UiTween.Value"/> (no delegates, no allocations).</summary>
    public interface IUiTweenTarget
    {
        /// <summary>Called every frame of the tween with the eased value between the start and end values.</summary>
        void OnTween(int id, float value);
    }

    /// <summary>
    /// Tiny tween engine for the operator screen: unscaled time, ease-out cubic, 160-240 ms transitions, a fixed pool of
    /// slots so nothing allocates while tweening. <see cref="OperatorScreen"/> calls <see cref="Tick"/> once per frame.
    /// Starting a tween on a target that already has the same kind of tween replaces it (continuing from the current value).
    /// </summary>
    public static class UiTween
    {
        public const float Fast = 0.16f;
        public const float Normal = 0.2f;
        public const float Slow = 0.24f;

        private const int Capacity = 128;

        private enum Kind
        {
            Panel,
            Fade,
            Color,
            Scale,
            Counter,
            Value
        }

        private sealed class Slot
        {
            public bool Active;
            public Kind Kind;
            public object Key;
            public int Id;
            public float Start;
            public float Duration;
            public float From;
            public float To;
            public Color FromColor;
            public Color ToColor;
            public Vector2 Base;
            public Vector2 Offset;
            public CanvasGroup Group;
            public RectTransform Rect;
            public Graphic Graphic;
            public Transform Transform;
            public TMP_Text Label;
            public IUiTweenTarget Custom;
            public string Format;
        }

        private static readonly Slot[] Slots = CreateSlots();

        /// <summary>Ease-out cubic of a 0..1 progress value.</summary>
        public static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u;
        }

        /// <summary>Advances every running tween. Call once per frame.</summary>
        public static void Tick(float now)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                var slot = Slots[i];
                if (!slot.Active)
                {
                    continue;
                }

                if (IsDead(slot))
                {
                    Release(slot);
                    continue;
                }

                float t = slot.Duration <= 0f ? 1f : (now - slot.Start) / slot.Duration;
                bool done = t >= 1f;
                Apply(slot, EaseOutCubic(t));
                if (done)
                {
                    Release(slot);
                }
            }
        }

        /// <summary>Stops every tween that belongs to <paramref name="key"/> (leaving values where they are).</summary>
        public static void Cancel(object key)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                var slot = Slots[i];
                if (slot.Active && ReferenceEquals(slot.Key, key))
                {
                    Release(slot);
                }
            }
        }

        /// <summary>Stops everything (screen destroyed).</summary>
        public static void Clear()
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                Release(Slots[i]);
            }
        }

        /// <summary>Fade from 0 and slide from <paramref name="slide"/> px below the rest position to the rest position.</summary>
        public static void PanelIn(CanvasGroup group, RectTransform rect, float slide = 24f, float duration = Slow)
        {
            PanelIn(group, rect, new Vector2(0f, -slide), duration);
        }

        /// <summary>Fade from 0 and slide from <paramref name="offset"/> to the rest position of <paramref name="rect"/>.</summary>
        public static void PanelIn(CanvasGroup group, RectTransform rect, Vector2 offset, float duration = Slow)
        {
            var slot = Acquire(Kind.Panel, group, 0);
            if (slot == null)
            {
                group.alpha = 1f;
                return;
            }

            // A replaced slide keeps the original rest position.
            Vector2 rest = slot.Active && slot.Rect == rect ? slot.Base : rect.anchoredPosition;
            Begin(slot, duration);
            slot.Group = group;
            slot.Rect = rect;
            slot.Base = rest;
            slot.Offset = offset;
            group.alpha = 0f;
            rect.anchoredPosition = rest + offset;
        }

        /// <summary>Fades a canvas group to <paramref name="alpha"/>.</summary>
        public static void Fade(CanvasGroup group, float alpha, float duration = Normal)
        {
            var slot = Acquire(Kind.Fade, group, 0);
            if (slot == null || duration <= 0f)
            {
                group.alpha = alpha;
                return;
            }

            Begin(slot, duration);
            slot.Group = group;
            slot.From = group.alpha;
            slot.To = alpha;
        }

        /// <summary>Tweens a graphic's colour (also TMP labels).</summary>
        public static void ColorTo(Graphic graphic, Color color, float duration = Fast)
        {
            var slot = Acquire(Kind.Color, graphic, 0);
            if (slot == null || duration <= 0f)
            {
                graphic.color = color;
                if (slot != null)
                {
                    Release(slot);
                }

                return;
            }

            Begin(slot, duration);
            slot.Graphic = graphic;
            slot.FromColor = graphic.color;
            slot.ToColor = color;
        }

        /// <summary>Tweens a uniform local scale.</summary>
        public static void ScaleTo(Transform target, float scale, float duration = 0.09f)
        {
            var slot = Acquire(Kind.Scale, target, 0);
            if (slot == null || duration <= 0f)
            {
                target.localScale = new Vector3(scale, scale, 1f);
                if (slot != null)
                {
                    Release(slot);
                }

                return;
            }

            Begin(slot, duration);
            slot.Transform = target;
            slot.From = target.localScale.x;
            slot.To = scale;
        }

        /// <summary>
        /// Rolls an integer on a TMP label from <paramref name="from"/> to <paramref name="to"/> using
        /// <c>SetText(format, value)</c> (allocation-free). The format must contain <c>{0}</c>.
        /// </summary>
        public static void Count(TMP_Text label, string format, int from, int to, float duration = 0.6f)
        {
            var slot = Acquire(Kind.Counter, label, 0);
            if (slot == null || duration <= 0f || from == to)
            {
                label.SetText(format, to);
                if (slot != null)
                {
                    Release(slot);
                }

                return;
            }

            Begin(slot, duration);
            slot.Label = label;
            slot.Format = format;
            slot.From = from;
            slot.To = to;
            label.SetText(format, from);
        }

        /// <summary>Tweens a float and reports it to <paramref name="target"/> (<see cref="IUiTweenTarget.OnTween"/>).</summary>
        public static void Value(IUiTweenTarget target, int id, float from, float to, float duration = Normal)
        {
            var slot = Acquire(Kind.Value, target, id);
            if (slot == null || duration <= 0f)
            {
                target.OnTween(id, to);
                if (slot != null)
                {
                    Release(slot);
                }

                return;
            }

            Begin(slot, duration);
            slot.Custom = target;
            slot.From = from;
            slot.To = to;
            target.OnTween(id, from);
        }

        // ----- internals -----

        private static Slot[] CreateSlots()
        {
            var slots = new Slot[Capacity];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new Slot();
            }

            return slots;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Clear();
        }

        private static Slot Acquire(Kind kind, object key, int id)
        {
            Slot free = null;
            for (int i = 0; i < Slots.Length; i++)
            {
                var slot = Slots[i];
                if (slot.Active)
                {
                    if (slot.Kind == kind && slot.Id == id && ReferenceEquals(slot.Key, key))
                    {
                        return slot;
                    }
                }
                else if (free == null)
                {
                    free = slot;
                }
            }

            if (free != null)
            {
                free.Kind = kind;
                free.Key = key;
                free.Id = id;
            }

            return free;
        }

        private static void Begin(Slot slot, float duration)
        {
            slot.Active = true;
            slot.Start = Time.unscaledTime;
            slot.Duration = duration;
        }

        private static void Release(Slot slot)
        {
            slot.Active = false;
            slot.Key = null;
            slot.Group = null;
            slot.Rect = null;
            slot.Graphic = null;
            slot.Transform = null;
            slot.Label = null;
            slot.Custom = null;
            slot.Format = null;
        }

        private static bool IsDead(Slot slot)
        {
            switch (slot.Kind)
            {
                case Kind.Panel:
                    return slot.Group == null || slot.Rect == null;
                case Kind.Fade:
                    return slot.Group == null;
                case Kind.Color:
                    return slot.Graphic == null;
                case Kind.Scale:
                    return slot.Transform == null;
                case Kind.Counter:
                    return slot.Label == null;
                default:
                    return slot.Custom == null;
            }
        }

        private static void Apply(Slot slot, float e)
        {
            switch (slot.Kind)
            {
                case Kind.Panel:
                    slot.Group.alpha = e;
                    slot.Rect.anchoredPosition = slot.Base + slot.Offset * (1f - e);
                    break;
                case Kind.Fade:
                    slot.Group.alpha = Mathf.Lerp(slot.From, slot.To, e);
                    break;
                case Kind.Color:
                    slot.Graphic.color = Color.Lerp(slot.FromColor, slot.ToColor, e);
                    break;
                case Kind.Scale:
                    float scale = Mathf.Lerp(slot.From, slot.To, e);
                    slot.Transform.localScale = new Vector3(scale, scale, 1f);
                    break;
                case Kind.Counter:
                    slot.Label.SetText(slot.Format, Mathf.Round(Mathf.Lerp(slot.From, slot.To, e)));
                    break;
                default:
                    slot.Custom.OnTween(slot.Id, Mathf.Lerp(slot.From, slot.To, e));
                    break;
            }
        }
    }
}
