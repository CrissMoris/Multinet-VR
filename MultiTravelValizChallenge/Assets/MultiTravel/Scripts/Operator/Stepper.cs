using MultiTravel.Core.Session;
using MultiTravel.Operator.Panels;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiTravel.Operator
{
    /// <summary>
    /// Left rail (300 px): the six session steps Karşılama → Kayıt → Cinsiyet → Talimat → Oyun → Sonuç with the current
    /// <see cref="SessionState"/> highlighted, finished steps ticked, and the active participant at the bottom.
    /// </summary>
    public sealed class Stepper
    {
        /// <summary>Number of steps.</summary>
        public const int StepCount = 6;

        private static readonly string[] Labels = { "Karşılama", "Kayıt", "Cinsiyet", "Talimat", "Oyun", "Sonuç" };

        private readonly OperatorContext context;
        private readonly Step[] steps = new Step[StepCount];
        private int current = -2;

        private RectTransform participantCard;
        private TextMeshProUGUI participantName;
        private TextMeshProUGUI participantSet;
        private ParticipantSession shownSession;
        private bool shownGenderSelected;

        public Stepper(OperatorContext context)
        {
            this.context = context;
        }

        /// <summary>Index of the step that represents <paramref name="state"/>, or -1 for none (Fatal).</summary>
        public static int StepFor(SessionState state)
        {
            switch (state)
            {
                case SessionState.Welcome:
                    return 0;
                case SessionState.Registration:
                    return 1;
                case SessionState.GenderSelection:
                    return 2;
                case SessionState.Instructions:
                    return 3;
                case SessionState.Loading:
                case SessionState.Countdown:
                case SessionState.Playing:
                    return 4;
                case SessionState.Completed:
                case SessionState.Submitting:
                case SessionState.SubmissionFailed:
                case SessionState.Finished:
                    return 5;
                default:
                    return -1;
            }
        }

        public void Build(RectTransform host)
        {
            var card = UiFactory.CreateSurface(host, "Stepper", OperatorUiStyle.Card, OperatorUiStyle.RadiusCard, true, true, out _);
            UiFactory.Stretch(card, 24f, 24f, 12f, 24f);
            UiFactory.AddVerticalLayout(card.gameObject, 16f, new RectOffset(24, 24, 28, 24), TextAnchor.UpperLeft, true, true, true, false);

            var caption = UiFactory.CreateLabel(card, "Caption", "OTURUM ADIMLARI", OperatorUiStyle.FontCaption, OperatorUiStyle.TextMuted, FontStyles.Bold);
            caption.characterSpacing = 4f;
            UiFactory.CreateSpacer(card, 1f, 4f);

            for (int i = 0; i < StepCount; i++)
            {
                steps[i] = BuildStep(card, i);
            }

            UiFactory.CreateFlexibleSpace(card);

            participantCard = UiFactory.CreateSurface(card, "Participant", OperatorUiStyle.Elevated, OperatorUiStyle.RadiusButton, false, true, out _);
            UiFactory.AddVerticalLayout(participantCard.gameObject, 4f, new RectOffset(16, 16, 14, 14), TextAnchor.UpperLeft, true, true, true, false);
            var row = UiFactory.CreateRow(participantCard, "Row", 8f, TextAnchor.MiddleLeft, false);
            UiFactory.CreateIcon(row, "Icon", UiIcon.User, 20f, OperatorUiStyle.TextSecondary);
            UiFactory.CreateLabel(row, "Caption", "Katılımcı", OperatorUiStyle.FontLabel, OperatorUiStyle.TextSecondary, FontStyles.Bold);
            participantName = UiFactory.CreateLabel(participantCard, "Name", string.Empty, OperatorUiStyle.FontBody, OperatorUiStyle.TextPrimary, FontStyles.Bold);
            UiFactory.MakeSingleLine(participantName);
            participantSet = UiFactory.CreateLabel(participantCard, "Set", string.Empty, OperatorUiStyle.FontLabel, OperatorUiStyle.TextMuted);
            UiFactory.MakeSingleLine(participantSet);
            participantCard.gameObject.SetActive(false);

            for (int i = 0; i < StepCount; i++)
            {
                Apply(i, StepVisual.Future, false);
            }
        }

        /// <summary>Highlights the step of <paramref name="state"/>; earlier steps become done.</summary>
        public void SetState(SessionState state)
        {
            int index = StepFor(state);
            if (index < 0)
            {
                // Fatal: keep the previous highlight but mark it as failed.
                if (current >= 0)
                {
                    Apply(current, StepVisual.Failed, true);
                }

                return;
            }

            current = index;
            for (int i = 0; i < StepCount; i++)
            {
                var visual = i < index ? StepVisual.Done : i == index ? StepVisual.Current : StepVisual.Future;
                // The result step is complete once finished.
                if (i == 5 && i == index && state == SessionState.Finished)
                {
                    visual = StepVisual.Done;
                }

                Apply(i, visual, true);
            }
        }

        /// <summary>10 Hz: participant card.</summary>
        public void Refresh()
        {
            var session = context.IsBound ? context.Session.Current : null;
            bool gender = session != null && session.GenderSelected;
            if (session == shownSession && gender == shownGenderSelected)
            {
                return;
            }

            shownSession = session;
            shownGenderSelected = gender;
            UiFactory.SetActive(participantCard, session != null);
            if (session == null)
            {
                return;
            }

            participantName.text = ParticipantText.FullName(session.Input);
            participantSet.text = gender ? ParticipantText.GenderLabel(session.Gender) + " ürün seti" : "Ürün seti seçilmedi";
        }

        // ----- internals -----

        private enum StepVisual
        {
            Future,
            Current,
            Done,
            Failed
        }

        private sealed class Step
        {
            public Image Background;
            public Image Marker;
            public TextMeshProUGUI Number;
            public Image Check;
            public TextMeshProUGUI Label;
            public Image Connector;
        }

        private static Step BuildStep(RectTransform parent, int index)
        {
            var step = new Step();
            var surface = UiFactory.CreateSurface(parent, "Step" + (index + 1), OperatorUiStyle.WithAlpha(OperatorUiStyle.Elevated, 0f),
                OperatorUiStyle.RadiusButton, false, false, out step.Background);
            UiFactory.SetLayout(surface, -1f, 56f, 1f, 0f, -1f, 56f);

            step.Marker = UiFactory.CreateImage("Marker", surface, OperatorUiStyle.Border, false);
            step.Marker.sprite = UiSprites.Circle(32);
            var markerRect = step.Marker.rectTransform;
            markerRect.anchorMin = new Vector2(0f, 0.5f);
            markerRect.anchorMax = new Vector2(0f, 0.5f);
            markerRect.pivot = new Vector2(0f, 0.5f);
            markerRect.sizeDelta = new Vector2(32f, 32f);
            markerRect.anchoredPosition = new Vector2(12f, 0f);

            step.Number = UiFactory.CreateLabel(markerRect, "Number", (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                OperatorUiStyle.FontLabel, OperatorUiStyle.TextSecondary, FontStyles.Bold, TextAlignmentOptions.Center);
            UiFactory.Stretch(step.Number.rectTransform);

            step.Check = UiFactory.CreateIcon(markerRect, "Check", UiIcon.Check, 20f, OperatorUiStyle.TextOnBright);
            UiFactory.IgnoreLayout(step.Check);
            UiFactory.AnchorCenter(step.Check.rectTransform, 20f, 20f);
            step.Check.gameObject.SetActive(false);

            step.Label = UiFactory.CreateLabel(surface, "Label", Labels[index], OperatorUiStyle.FontBody, OperatorUiStyle.TextSecondary,
                FontStyles.Normal, TextAlignmentOptions.Left);
            UiFactory.MakeSingleLine(step.Label);
            var labelRect = step.Label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(60f, 0f);
            labelRect.offsetMax = new Vector2(-12f, 0f);

            if (index < StepCount - 1)
            {
                // Connector to the next marker (drawn inside the 16 px gap below the row, behind the markers).
                step.Connector = UiFactory.CreateImage("Connector", surface, OperatorUiStyle.WithAlpha(OperatorUiStyle.Border, 0.6f), false);
                UiFactory.IgnoreLayout(step.Connector);
                var c = step.Connector.rectTransform;
                c.anchorMin = new Vector2(0f, 0f);
                c.anchorMax = new Vector2(0f, 0f);
                c.pivot = new Vector2(0.5f, 1f);
                c.anchoredPosition = new Vector2(28f, 12f);
                c.sizeDelta = new Vector2(2f, 40f);
                c.SetAsFirstSibling();
                step.Background.rectTransform.SetAsFirstSibling();
            }

            return step;
        }

        private void Apply(int index, StepVisual visual, bool animate)
        {
            var step = steps[index];
            float duration = animate ? UiTween.Normal : 0f;
            Color background = OperatorUiStyle.WithAlpha(OperatorUiStyle.Elevated, 0f);
            Color marker = OperatorUiStyle.Border;
            Color text = OperatorUiStyle.TextMuted;
            Color number = OperatorUiStyle.TextSecondary;
            bool showCheck = false;

            switch (visual)
            {
                case StepVisual.Current:
                    background = OperatorUiStyle.Elevated;
                    marker = OperatorUiStyle.Primary;
                    text = OperatorUiStyle.TextPrimary;
                    number = Color.white;
                    break;
                case StepVisual.Done:
                    marker = OperatorUiStyle.Accent;
                    text = OperatorUiStyle.TextSecondary;
                    showCheck = true;
                    break;
                case StepVisual.Failed:
                    background = OperatorUiStyle.WithAlpha(OperatorUiStyle.Danger, 0.2f);
                    marker = OperatorUiStyle.Danger;
                    text = OperatorUiStyle.TextPrimary;
                    number = Color.white;
                    break;
            }

            UiTween.ColorTo(step.Background, background, duration);
            UiTween.ColorTo(step.Marker, marker, duration);
            UiTween.ColorTo(step.Label, text, duration);
            step.Number.color = number;
            step.Number.gameObject.SetActive(!showCheck);
            step.Check.gameObject.SetActive(showCheck);
            if (step.Connector != null)
            {
                UiTween.ColorTo(step.Connector, visual == StepVisual.Done ? OperatorUiStyle.WithAlpha(OperatorUiStyle.Accent, 0.8f)
                    : OperatorUiStyle.WithAlpha(OperatorUiStyle.Border, 0.6f), duration);
            }
        }
    }
}
