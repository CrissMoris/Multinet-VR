using MultiTravel.Core.Session;
using MultiTravel.Operator.UI;
using UnityEngine;

namespace MultiTravel.Operator
{
    /// <summary>
    /// Base class of the operator content panels. A panel owns a full-size root under the content area, is built once
    /// (<see cref="Build"/>) and is then shown for one or more <see cref="SessionState"/>s. <see cref="Show"/> is called on
    /// every state change that targets the panel (also when the panel is already visible), <see cref="Refresh"/> at 10 Hz
    /// while visible, <see cref="Hide"/> when another panel takes over.
    /// </summary>
    public abstract class OperatorPanel
    {
        protected OperatorPanel(OperatorContext context)
        {
            Context = context;
        }

        protected OperatorContext Context { get; }

        /// <summary>Full-size root of the panel (inactive while hidden).</summary>
        public RectTransform Root { get; private set; }

        public bool IsVisible => Root != null && Root.gameObject.activeSelf;

        /// <summary>Creates the panel hierarchy under <paramref name="contentArea"/>; the panel starts hidden.</summary>
        public void Build(RectTransform contentArea)
        {
            Root = UiFactory.CreateRect(GetType().Name, contentArea);
            UiFactory.Stretch(Root);
            OnBuild(Root);
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

            if (!Root.gameObject.activeSelf)
            {
                Root.gameObject.SetActive(true);
            }

            OnShow(state);
            Refresh();
        }

        /// <summary>Hides the panel (no-op when hidden).</summary>
        public void Hide()
        {
            if (Root == null || !Root.gameObject.activeSelf)
            {
                return;
            }

            OnHide();
            Root.gameObject.SetActive(false);
        }

        /// <summary>Updates live values. Must not allocate when nothing changed.</summary>
        public virtual void Refresh()
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

        /// <summary>Panel heading inside a card.</summary>
        protected static TMPro.TextMeshProUGUI CreateHeading(Transform card, string text)
        {
            return UiFactory.CreateLabel(card, "Heading", text, OperatorUiStyle.FontTitle, OperatorUiStyle.Primary,
                TMPro.FontStyles.Bold, TMPro.TextAlignmentOptions.Center);
        }

        /// <summary>Secondary text inside a card.</summary>
        protected static TMPro.TextMeshProUGUI CreateBody(Transform card, string name, string text,
            TMPro.TextAlignmentOptions alignment = TMPro.TextAlignmentOptions.Center)
        {
            return UiFactory.CreateLabel(card, name, text, OperatorUiStyle.FontBody, OperatorUiStyle.TextSecondary,
                TMPro.FontStyles.Normal, alignment);
        }
    }
}
