using TMPro;
using UnityEngine;

namespace MultiTravel.Gameplay.Presentation
{
    /// <summary>
    /// One pooled world-space caption (large Turkish text readable at 1 m) that faces the headset. Created once by its
    /// owner; <see cref="Show"/> / <see cref="Hide"/> only toggle the GameObject, <see cref="Tick"/> re-orients it.
    /// </summary>
    public sealed class FloatingCaption
    {
        private readonly TextMeshPro text;
        private readonly Transform transform;
        private Vector3 worldPosition;
        private float shownAt;

        public FloatingCaption(string name, Transform parent, float fontSize, Color color, Vector2 size, bool wrap)
        {
            text = PresentationStyle.CreateText(name, parent, fontSize, color, TextAlignmentOptions.Center, size);
            text.fontStyle = FontStyles.Bold;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.outlineWidth = 0.12f;
            text.outlineColor = new Color32(0x07, 0x2A, 0x63, 0xFF);
            transform = text.transform;
            text.gameObject.SetActive(false);
        }

        /// <summary>The TMP component (tests read <c>Text.text</c>).</summary>
        public TextMeshPro Text => text;

        /// <summary>True while visible.</summary>
        public bool IsShown => text.gameObject.activeSelf;

        /// <summary>Seconds since the caption was shown (0 when hidden).</summary>
        public float ShownSeconds => IsShown ? Time.unscaledTime - shownAt : 0f;

        /// <summary>Shows <paramref name="caption"/> at a world position. Same text is not re-assigned (no allocation).</summary>
        public void Show(string caption, Vector3 position)
        {
            worldPosition = position;
            transform.position = position;
            if (!ReferenceEquals(text.text, caption) && text.text != caption)
            {
                text.text = caption;
            }

            if (!text.gameObject.activeSelf)
            {
                text.gameObject.SetActive(true);
                shownAt = Time.unscaledTime;
            }
        }

        /// <summary>Moves the caption without changing the text.</summary>
        public void MoveTo(Vector3 position)
        {
            worldPosition = position;
            transform.position = position;
        }

        public void Hide()
        {
            if (text.gameObject.activeSelf)
            {
                text.gameObject.SetActive(false);
            }
        }

        /// <summary>Sets the text alpha (fade-ins).</summary>
        public void SetAlpha(float alpha)
        {
            text.alpha = Mathf.Clamp01(alpha);
        }

        /// <summary>Faces the camera (call from the owner's LateUpdate while shown).</summary>
        public void Tick(Camera camera)
        {
            if (!IsShown)
            {
                return;
            }

            transform.position = worldPosition;
            PresentationStyle.FaceCamera(transform, camera, true);
        }

        public void Destroy()
        {
            if (text != null)
            {
                Object.Destroy(text.gameObject);
            }
        }
    }
}
