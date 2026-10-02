using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiTravel.Operator.UI
{
    /// <summary>
    /// Builds uGUI / TextMeshPro building blocks from code (no prefabs, no asset sprites): rounded 9-slice surfaces with soft
    /// shadows, labels on the project font, icons and layout helpers. Higher-level controls live in their own classes
    /// (<see cref="UiButton"/>, <see cref="UiPill"/>, <see cref="UiField"/>, <see cref="UiStatTile"/>, <see cref="UiProgressRing"/>,
    /// <see cref="UiToast"/>, <see cref="ConfirmModal"/>). Construction allocates; nothing here is meant to be called per frame.
    /// </summary>
    public static class UiFactory
    {
        private const int UiLayer = 5;

        // ----- primitives -----

        /// <summary>Creates a child GameObject with a RectTransform on the parent's layer.</summary>
        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : UiLayer;
            var rect = (RectTransform)go.transform;
            if (parent != null)
            {
                rect.SetParent(parent, false);
            }

            rect.localScale = Vector3.one;
            return rect;
        }

        /// <summary>Stretches a rect over its parent with the given insets (pixels at the reference resolution).</summary>
        public static void Stretch(RectTransform rect, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Anchors a rect to the top edge of its parent with a fixed height.</summary>
        public static void AnchorTop(RectTransform rect, float height, float topOffset = 0f)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(0f, -topOffset - height);
            rect.offsetMax = new Vector2(0f, -topOffset);
        }

        /// <summary>Anchors a rect to the bottom edge of its parent with a fixed height.</summary>
        public static void AnchorBottom(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = new Vector2(0f, height);
        }

        /// <summary>Anchors a rect to the left edge, full height, fixed width.</summary>
        public static void AnchorLeft(RectTransform rect, float width)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = new Vector2(width, 0f);
        }

        /// <summary>Anchors a rect to the right edge, full height, fixed width.</summary>
        public static void AnchorRight(RectTransform rect, float width)
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(-width, 0f);
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Centres a rect in its parent with a fixed size (height may be driven by a ContentSizeFitter).</summary>
        public static void AnchorCenter(RectTransform rect, float width, float height)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>Creates a flat (unsliced) image.</summary>
        public static Image CreateImage(string name, Transform parent, Color color, bool raycastTarget = false)
        {
            var rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        /// <summary>Creates a 9-slice rounded rectangle with the given corner radius.</summary>
        public static Image CreateRounded(string name, Transform parent, Color color, int radius, bool raycastTarget = false)
        {
            var image = CreateImage(name, parent, color, raycastTarget);
            ApplyRounded(image, radius);
            return image;
        }

        /// <summary>Gives an existing image a rounded 9-slice sprite.</summary>
        public static void ApplyRounded(Image image, int radius)
        {
            image.sprite = UiSprites.Rounded(radius);
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
        }

        /// <summary>Creates a circle image of the given diameter (fixed size in layout groups).</summary>
        public static Image CreateCircle(string name, Transform parent, float diameter, Color color)
        {
            var image = CreateImage(name, parent, color, false);
            image.sprite = UiSprites.Circle(Mathf.RoundToInt(diameter));
            image.type = Image.Type.Simple;
            SetLayout(image, diameter, diameter, 0f, 0f, diameter, diameter);
            return image;
        }

        /// <summary>Creates an icon image of a fixed size (preferred = minimum = <paramref name="size"/>).</summary>
        public static Image CreateIcon(Transform parent, string name, UiIcon icon, float size, Color color)
        {
            var image = CreateImage(name, parent, color, false);
            image.preserveAspect = true;
            SetIcon(image, icon, size);
            SetLayout(image, size, size, 0f, 0f, size, size);
            return image;
        }

        /// <summary>Swaps the sprite of an icon image (hides the image for <see cref="UiIcon.None"/>).</summary>
        public static void SetIcon(Image image, UiIcon icon, float size)
        {
            image.sprite = UiSprites.Icon(icon, Mathf.RoundToInt(size));
            image.enabled = icon != UiIcon.None;
        }

        /// <summary>1 px horizontal rule.</summary>
        public static Image CreateDivider(Transform parent, string name = "Divider")
        {
            var line = CreateImage(name, parent, OperatorUiStyle.WithAlpha(OperatorUiStyle.Border, 0.45f), false);
            SetLayout(line, -1f, 1f, 1f, 0f, -1f, 1f);
            return line;
        }

        /// <summary>
        /// Full-screen Screen Space Overlay canvas: CanvasScaler ScaleWithScreenSize 1920x1080 with match 0.5, pixel-perfect
        /// off (pixel snapping makes SDF text jitter and blur), plus a GraphicRaycaster.
        /// </summary>
        public static Canvas CreateOverlayCanvas(string name, Transform parent, int sortingOrder)
        {
            var rect = CreateRect(name, parent);
            rect.gameObject.layer = UiLayer;
            var canvas = rect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            canvas.pixelPerfect = false;
            canvas.targetDisplay = 0;

            var scaler = rect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;

            rect.gameObject.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        // ----- text -----

        /// <summary>
        /// Creates a TextMeshPro UGUI label on the project font and the font asset's own material. Sizes below
        /// <see cref="OperatorUiStyle.MinFontSize"/> are raised; auto-sizing is off (fractional sizes soften SDF text).
        /// Rich text is off by default (labels may show participant input); numeric labels turn it on for tabular digits.
        /// </summary>
        public static TextMeshProUGUI CreateLabel(
            Transform parent,
            string name,
            string text,
            float fontSize,
            Color color,
            FontStyles style = FontStyles.Normal,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            var rect = CreateRect(name, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            OperatorUiStyle.ApplyFont(label);
            label.richText = false;
            label.enableAutoSizing = false;
            label.extraPadding = false;
            label.fontSize = Mathf.Max(OperatorUiStyle.MinFontSize, fontSize);
            label.color = color;
            label.fontStyle = style;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            label.text = text ?? string.Empty;
            return label;
        }

        /// <summary>Makes a label single-line with an ellipsis when it does not fit.</summary>
        public static void MakeSingleLine(TextMeshProUGUI label)
        {
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        // ----- surfaces and containers -----

        /// <summary>
        /// Container (no graphic of its own) with decoration children drawn behind its content: soft shadow, rounded fill and
        /// a 1 px outline. The decoration ignores layout groups; put a layout group on the returned rect to arrange content.
        /// </summary>
        public static RectTransform CreateSurface(
            Transform parent,
            string name,
            Color fill,
            int radius,
            bool withShadow,
            bool withOutline,
            out Image fillImage)
        {
            var root = CreateRect(name, parent);
            if (withShadow)
            {
                var shadow = CreateImage("Shadow", root, new Color(0f, 0f, 0f, 0.5f), false);
                shadow.sprite = UiSprites.Shadow();
                shadow.type = Image.Type.Sliced;
                IgnoreLayout(shadow);
                float m = UiSprites.ShadowMargin;
                var r = shadow.rectTransform;
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(-m, -m - 8f);
                r.offsetMax = new Vector2(m, m - 8f);
            }

            fillImage = CreateRounded("Fill", root, fill, radius, true);
            IgnoreLayout(fillImage);
            Stretch(fillImage.rectTransform);

            if (withOutline)
            {
                var outline = CreateImage("Outline", root, OperatorUiStyle.WithAlpha(OperatorUiStyle.Border, 0.6f), false);
                outline.sprite = UiSprites.Outline(radius);
                outline.type = Image.Type.Sliced;
                IgnoreLayout(outline);
                Stretch(outline.rectTransform);
            }

            return root;
        }

        /// <summary>
        /// Card centred in the parent with a vertical layout and a ContentSizeFitter (height follows the content). Children
        /// are added to the returned rect.
        /// </summary>
        public static RectTransform CreateCard(
            Transform parent,
            string name,
            float width,
            int padding = 32,
            float spacing = 16f,
            int radius = OperatorUiStyle.RadiusCard)
        {
            var card = CreateSurface(parent, name, OperatorUiStyle.Card, radius, true, true, out _);
            AnchorCenter(card, width, 200f);
            AddVerticalLayout(card.gameObject, spacing, new RectOffset(padding, padding, padding, padding), TextAnchor.UpperCenter, true, true, true, false);
            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return card;
        }

        /// <summary>Horizontal row container (no graphic) inside a layout group.</summary>
        public static RectTransform CreateRow(Transform parent, string name, float spacing, TextAnchor alignment = TextAnchor.MiddleCenter, bool expandWidth = true)
        {
            var rect = CreateRect(name, parent);
            AddHorizontalLayout(rect.gameObject, spacing, new RectOffset(0, 0, 0, 0), alignment, true, true, expandWidth, false);
            return rect;
        }

        /// <summary>Vertical column container (no graphic) inside a layout group.</summary>
        public static RectTransform CreateColumn(Transform parent, string name, float spacing, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var rect = CreateRect(name, parent);
            AddVerticalLayout(rect.gameObject, spacing, new RectOffset(0, 0, 0, 0), alignment, true, true, true, false);
            return rect;
        }

        /// <summary>Fixed-size empty element for spacing inside layout groups.</summary>
        public static RectTransform CreateSpacer(Transform parent, float width, float height)
        {
            var rect = CreateRect("Spacer", parent);
            SetLayout(rect, width, height, 0f, 0f, width, height);
            return rect;
        }

        /// <summary>Empty element that takes all remaining space in a layout group.</summary>
        public static RectTransform CreateFlexibleSpace(Transform parent)
        {
            var rect = CreateRect("Flex", parent);
            SetLayout(rect, 0f, 0f, 1f, 1f, 0f, 0f);
            return rect;
        }

        public static VerticalLayoutGroup AddVerticalLayout(
            GameObject target,
            float spacing,
            RectOffset padding,
            TextAnchor alignment = TextAnchor.UpperLeft,
            bool controlWidth = true,
            bool controlHeight = true,
            bool expandWidth = true,
            bool expandHeight = false)
        {
            var group = target.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset(0, 0, 0, 0);
            group.childAlignment = alignment;
            group.childControlWidth = controlWidth;
            group.childControlHeight = controlHeight;
            group.childForceExpandWidth = expandWidth;
            group.childForceExpandHeight = expandHeight;
            group.childScaleWidth = false;
            group.childScaleHeight = false;
            return group;
        }

        public static HorizontalLayoutGroup AddHorizontalLayout(
            GameObject target,
            float spacing,
            RectOffset padding,
            TextAnchor alignment = TextAnchor.MiddleLeft,
            bool controlWidth = true,
            bool controlHeight = true,
            bool expandWidth = false,
            bool expandHeight = false)
        {
            var group = target.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset(0, 0, 0, 0);
            group.childAlignment = alignment;
            group.childControlWidth = controlWidth;
            group.childControlHeight = controlHeight;
            group.childForceExpandWidth = expandWidth;
            group.childForceExpandHeight = expandHeight;
            group.childScaleWidth = false;
            group.childScaleHeight = false;
            return group;
        }

        /// <summary>
        /// Adds (or updates) a LayoutElement. Negative values leave the corresponding property unset (-1 = not used by uGUI).
        /// </summary>
        public static LayoutElement SetLayout(
            Component target,
            float preferredWidth = -1f,
            float preferredHeight = -1f,
            float flexibleWidth = -1f,
            float flexibleHeight = -1f,
            float minWidth = -1f,
            float minHeight = -1f)
        {
            var element = target.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = target.gameObject.AddComponent<LayoutElement>();
            }

            element.preferredWidth = preferredWidth;
            element.preferredHeight = preferredHeight;
            element.flexibleWidth = flexibleWidth;
            element.flexibleHeight = flexibleHeight;
            element.minWidth = minWidth;
            element.minHeight = minHeight;
            return element;
        }

        /// <summary>Excludes a child from its parent's layout group.</summary>
        public static void IgnoreLayout(Component target)
        {
            var element = target.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = target.gameObject.AddComponent<LayoutElement>();
            }

            element.ignoreLayout = true;
        }

        /// <summary>Activates / deactivates a GameObject only when the state changes.</summary>
        public static void SetActive(Component target, bool active)
        {
            if (target != null && target.gameObject.activeSelf != active)
            {
                target.gameObject.SetActive(active);
            }
        }

        // ----- button shortcut -----

        /// <summary>Creates a <see cref="UiButton"/> (see <see cref="UiButton.Create"/>).</summary>
        public static UiButton CreateButton(
            Transform parent,
            string name,
            string text,
            ButtonStyle style,
            UnityEngine.Events.UnityAction onClick,
            float height = OperatorUiStyle.ButtonHeight,
            float fontSize = OperatorUiStyle.FontBody,
            float preferredWidth = -1f,
            UiIcon icon = UiIcon.None)
        {
            return UiButton.Create(parent, name, text, style, onClick, height, fontSize, preferredWidth, icon);
        }
    }
}
