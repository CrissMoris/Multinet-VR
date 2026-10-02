using MultiTravel.Core.Session;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;

namespace MultiTravel.Operator
{
    /// <summary>
    /// Base class of the operator content panels. A panel owns a full-size root under the centre area, is built once
    /// (<see cref="Build"/>) and is then shown for one or more <see cref="SessionState"/>s. <see cref="Show"/> is called on
    /// every state change that targets the panel (also when the panel is already visible; the 24 px slide + fade only plays when
    /// the panel was hidden), <see cref="Refresh"/> at 10 Hz while visible, <see cref="Tick"/> every frame while visible
    /// (keyboard shortcuts), <see cref="Hide"/> when another panel takes over.
    /// </summary>
    public abstract class OperatorPanel
    {
        private CanvasGroup group;
        private RectTransform body;

        protected OperatorPanel(OperatorContext context)
        {
            Context = context;
        }

        protected OperatorContext Context { get; }

        /// <summary>Full-size root of the panel (inactive while hidden).</summary>
        public RectTransform Root { get; private set; }

        public bool IsVisible => Root != null && Root.gameObject.activeSelf;

        /// <summary>Frame in which the panel was last shown (used to ignore the key press that opened it).</summary>
        protected int ShownFrame { get; private set; } = -1;

        /// <summary>Creates the panel hierarchy under <paramref name="contentArea"/>; the panel starts hidden.</summary>
        public void Build(RectTransform contentArea)
        {
            Root = UiFactory.CreateRect(GetType().Name, contentArea);
            UiFactory.Stretch(Root);
            group = Root.gameObject.AddComponent<CanvasGroup>();
            body = UiFactory.CreateRect("Body", Root);
            UiFactory.Stretch(body);
            OnBuild(body);
            Root.gameObject.SetActive(false);
        }

        /// <summary>True when this panel represents <paramref name="state"/>.</summary>
        public abstract bool HandlesState(SessionState state);

        /// <summary>Makes the panel visible for <paramref name="state"/> and refreshes it.</summary>
        public void Show(SessionState state)
        {
            if (Root == null)
            {
                return;
            }

            bool wasHidden = !Root.gameObject.activeSelf;
            if (wasHidden)
            {
                Root.gameObject.SetActive(true);
                ShownFrame = Time.frameCount;
            }

            OnShow(state);
            Refresh();
            if (wasHidden)
            {
                UiTween.PanelIn(group, body, 24f, UiTween.Slow);
            }
        }

        /// <summary>Hides the panel (no-op when hidden).</summary>
        public void Hide()
        {
            if (Root == null || !Root.gameObject.activeSelf)
            {
                return;
            }

            OnHide();
            UiTween.Cancel(group);
            UiTween.Cancel(body);
            group.alpha = 1f;
            body.anchoredPosition = Vector2.zero;
            Root.gameObject.SetActive(false);
        }

        /// <summary>Updates live values at 10 Hz. Must not allocate when nothing changed.</summary>
        public virtual void Refresh()
        {
        }

        /// <summary>Per-frame hook while visible and no dialog is open (keyboard shortcuts). Must not allocate.</summary>
        public virtual void Tick()
        {
        }

        /// <summary>Releases resources (cancels in-flight requests) when the screen is destroyed.</summary>
        public virtual void Dispose()
        {
        }

        /// <summary>Called when the session returns to Welcome (new participant cycle): drop every per-participant value.</summary>
        public virtual void ClearParticipantData()
        {
        }

        protected abstract void OnBuild(RectTransform root);

        protected virtual void OnShow(SessionState state)
        {
        }

        protected virtual void OnHide()
        {
        }

        /// <summary>True when keyboard shortcuts of this panel may fire in the current frame.</summary>
        protected bool CanUseShortcuts()
        {
            return IsVisible && Time.frameCount > ShownFrame && (Context.Modal == null || !Context.Modal.IsOpen);
        }

        /// <summary>Panel title inside a card.</summary>
        protected static TextMeshProUGUI CreateHeading(Transform card, string text, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            return UiFactory.CreateLabel(card, "Heading", text, OperatorUiStyle.FontTitle, OperatorUiStyle.TextPrimary,
                FontStyles.Bold, alignment);
        }

        /// <summary>Secondary text inside a card.</summary>
        protected static TextMeshProUGUI CreateBody(Transform card, string name, string text,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            return UiFactory.CreateLabel(card, name, text, OperatorUiStyle.FontBody, OperatorUiStyle.TextSecondary,
                FontStyles.Normal, alignment);
        }

        /// <summary>Round badge with an icon (panel header decoration).</summary>
        protected static void CreateBadge(Transform parent, UiIcon icon, Color color, float size = 72f)
        {
            var holder = UiFactory.CreateRect("Badge", parent);
            UiFactory.SetLayout(holder, size, size, 0f, 0f, size, size);
            var circle = UiFactory.CreateImage("Circle", holder, OperatorUiStyle.WithAlpha(color, 0.18f), false);
            circle.sprite = UiSprites.Circle(Mathf.RoundToInt(size));
            UiFactory.Stretch(circle.rectTransform);
            var glyph = UiFactory.CreateIcon(holder, "Icon", icon, Mathf.Round(size * 0.5f), OperatorUiStyle.TextFor(color));
            UiFactory.IgnoreLayout(glyph);
            UiFactory.AnchorCenter(glyph.rectTransform, Mathf.Round(size * 0.5f), Mathf.Round(size * 0.5f));
        }

        /// <summary>Opens the confirmation dialog (falls back to running nothing when the dialog is unavailable).</summary>
        protected void Confirm(string title, string question, string confirmLabel, ButtonStyle style, System.Action onConfirm)
        {
            if (Context.Modal == null)
            {
                Debug.LogWarning("[MultiTravel.Operator] Confirmation dialog is not available; action ignored.");
                return;
            }

            Context.Modal.Ask(title, question, confirmLabel, style, onConfirm);
        }

        /// <summary>Closes the confirmation dialog (panel hidden / cleared).</summary>
        protected void CloseConfirm()
        {
            Context.Modal?.Close();
        }
    }
}
