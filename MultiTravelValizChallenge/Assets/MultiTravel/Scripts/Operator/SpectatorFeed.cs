using System.Collections.Generic;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiTravel.Operator
{
    /// <summary>
    /// Connects the on-screen video surfaces to the scene's <see cref="SpectatorCamera"/>. Panels create their frames through
    /// <see cref="CreateFrame"/>; the operator screen tells the feed whether any surface is currently visible
    /// (<see cref="SetWanted"/>) and calls <see cref="Refresh"/> at 10 Hz. When the scene has no spectator camera every
    /// surface shows a "Görüntü yok" placeholder.
    /// </summary>
    public sealed class SpectatorFeed
    {
        private readonly List<View> views = new List<View>();
        private bool wanted;
        private SpectatorCamera boundCamera;

        /// <summary>True while at least one surface should display live video.</summary>
        public bool Wanted => wanted;

        /// <summary>One video surface (RawImage + placeholder).</summary>
        public sealed class View
        {
            public RawImage Raw;
            public RectTransform Placeholder;
            public bool UsePlaceholder = true;
        }

        /// <summary>
        /// Rounded 16:9 video frame that fits inside its host. Returns the host rect (give it layout / anchors); the frame is a
        /// masked child. <paramref name="overlay"/> is a stretch container above the video for chips and text.
        /// </summary>
        public RectTransform CreateFrame(Transform parent, string name, out RectTransform overlay)
        {
            var host = UiFactory.CreateRect(name, parent);
            UiFactory.SetLayout(host, -1f, -1f, 1f, 1f, 160f, 90f);

            var frame = UiFactory.CreateSurface(host, "Frame", new Color(0.03f, 0.05f, 0.1f, 1f), OperatorUiStyle.RadiusCard, true, false, out _);
            var fitter = frame.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 16f / 9f;
            frame.anchorMin = new Vector2(0.5f, 0.5f);
            frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);

            var viewport = UiFactory.CreateRounded("Viewport", frame, Color.white, OperatorUiStyle.RadiusCard, false);
            UiFactory.Stretch(viewport.rectTransform);
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var view = CreateView(viewport.rectTransform, "Video");
            views.Add(view);

            var edge = UiFactory.CreateImage("Outline", frame, OperatorUiStyle.WithAlpha(OperatorUiStyle.Border, 0.8f), false);
            edge.sprite = UiSprites.Outline(OperatorUiStyle.RadiusCard);
            edge.type = Image.Type.Sliced;
            UiFactory.Stretch(edge.rectTransform);

            overlay = UiFactory.CreateRect("Overlay", frame);
            UiFactory.Stretch(overlay);
            return host;
        }

        /// <summary>Full-screen cover video (fills the parent, cropping to keep 16:9) for use behind other UI.</summary>
        public View CreateBackdrop(Transform parent, string name)
        {
            var rect = UiFactory.CreateRect(name, parent);
            UiFactory.Stretch(rect);
            var holder = UiFactory.CreateRect("Cover", rect);
            UiFactory.Stretch(holder);
            var fitter = holder.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 16f / 9f;
            var view = CreateView(holder, "Video");
            view.UsePlaceholder = false;
            view.Placeholder.gameObject.SetActive(false);
            views.Add(view);
            return view;
        }

        /// <summary>Switches camera rendering on or off (off when no surface is visible).</summary>
        public void SetWanted(bool value)
        {
            wanted = value;
            ApplyWanted();
        }

        /// <summary>Re-binds the camera texture to every surface and toggles placeholders. Call at 10 Hz.</summary>
        public void Refresh()
        {
            var cam = SpectatorCamera.Instance;
            if (cam != boundCamera)
            {
                boundCamera = cam;
                ApplyWanted();
            }

            bool hasFrame = cam != null && cam.HasFrame && cam.Texture != null;
            Texture tex = hasFrame ? cam.Texture : null;
            for (int i = 0; i < views.Count; i++)
            {
                var view = views[i];
                if (view.Raw.texture != tex)
                {
                    view.Raw.texture = tex;
                }

                if (view.Raw.enabled != hasFrame)
                {
                    view.Raw.enabled = hasFrame;
                }

                if (view.UsePlaceholder)
                {
                    bool showPlaceholder = cam == null;
                    if (view.Placeholder.gameObject.activeSelf != showPlaceholder)
                    {
                        view.Placeholder.gameObject.SetActive(showPlaceholder);
                    }
                }
            }
        }

        private void ApplyWanted()
        {
            var cam = SpectatorCamera.Instance;
            if (cam != null && cam.Active != wanted)
            {
                cam.Active = wanted;
            }
        }

        private static View CreateView(RectTransform parent, string name)
        {
            var back = UiFactory.CreateImage("VideoBack", parent, new Color(0.03f, 0.05f, 0.1f, 1f), false);
            UiFactory.Stretch(back.rectTransform);

            var raw = UiFactory.CreateRect(name, parent).gameObject.AddComponent<RawImage>();
            UiFactory.Stretch(raw.rectTransform);
            raw.raycastTarget = false;
            raw.color = Color.white;
            raw.enabled = false;

            var placeholder = UiFactory.CreateRect("Placeholder", parent);
            UiFactory.Stretch(placeholder);
            var text = UiFactory.CreateLabel(placeholder, "Text", "Görüntü yok", OperatorUiStyle.FontBody, OperatorUiStyle.TextMuted,
                FontStyles.Normal, TextAlignmentOptions.Center);
            UiFactory.Stretch(text.rectTransform);
            return new View { Raw = raw, Placeholder = placeholder };
        }
    }
}
