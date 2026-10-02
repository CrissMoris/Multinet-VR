using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.Mathf;

namespace MultiTravel.Operator.UI
{
    /// <summary>Icons drawn procedurally by <see cref="UiSprites.Icon"/>.</summary>
    public enum UiIcon
    {
        None,
        Check,
        Warning,
        User,
        Trophy,
        Headset,
        Server,
        Refresh,
        Recenter,
        ArrowUp,
        ArrowDown,
        Play,
        Close,
        Female,
        Male,
        Suitcase
    }

    /// <summary>
    /// Runtime-generated sprites (no asset files): 9-slice rounded rectangles, outlines, a soft shadow, circles, rings,
    /// gradients and vector-like icons. Every texture is rendered at 2x the canvas unit size with analytic anti-aliasing so
    /// edges stay crisp on 1080p and on 4K displays. Sprites are cached; creation allocates and is meant for UI construction.
    /// </summary>
    public static class UiSprites
    {
        /// <summary>Texels per canvas unit.</summary>
        public const int Supersample = 2;

        private const float SpritePixelsPerUnit = 100f * Supersample;

        private static readonly Dictionary<int, Sprite> RoundedCache = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> OutlineCache = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> CircleCache = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> RingCache = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> IconCache = new Dictionary<int, Sprite>();
        private static Sprite shadow;
        private static Sprite gradientTopOpaque;
        private static Sprite gradientBottomOpaque;

        /// <summary>Margin (canvas units) a shadow sprite extends beyond the surface it belongs to.</summary>
        public const float ShadowMargin = 20f;

        /// <summary>9-slice rounded rectangle with the given corner radius (canvas units). Use with <c>Image.Type.Sliced</c>.</summary>
        public static Sprite Rounded(int radius)
        {
            radius = Clamp(radius, 2, 64);
            if (RoundedCache.TryGetValue(radius, out var cached) && cached != null)
            {
                return cached;
            }

            int r = radius * Supersample;
            int size = 2 * r + 2;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedDistance(x, y, size, r);
                    pixels[y * size + x] = new Color32(255, 255, 255, Alpha(0.5f - d));
                }
            }

            var sprite = MakeSprite(pixels, size, size, new Vector4(r, r, r, r), SpritePixelsPerUnit);
            RoundedCache[radius] = sprite;
            return sprite;
        }

        /// <summary>9-slice 1-unit outline of a rounded rectangle (transparent centre).</summary>
        public static Sprite Outline(int radius)
        {
            radius = Clamp(radius, 2, 64);
            if (OutlineCache.TryGetValue(radius, out var cached) && cached != null)
            {
                return cached;
            }

            int r = radius * Supersample;
            int size = 2 * r + 2;
            float stroke = Supersample;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedDistance(x, y, size, r);
                    float outer = Clamp01(0.5f - d);
                    float inner = Clamp01(0.5f - (d + stroke));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Clamp01(outer - inner) * 255f + 0.5f));
                }
            }

            var sprite = MakeSprite(pixels, size, size, new Vector4(r, r, r, r), SpritePixelsPerUnit);
            OutlineCache[radius] = sprite;
            return sprite;
        }

        /// <summary>Soft drop shadow (9-slice); size the image <see cref="ShadowMargin"/> larger than the surface on every side.</summary>
        public static Sprite Shadow()
        {
            if (shadow != null)
            {
                return shadow;
            }

            const int size = 128;
            const float margin = ShadowMargin * Supersample;
            const float radius = 24f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float half = size * 0.5f;
                    float inner = half - margin - radius;
                    float dx = Abs(x + 0.5f - half) - inner;
                    float dy = Abs(y + 0.5f - half) - inner;
                    float d = Sqrt(Max(dx, 0f) * Max(dx, 0f) + Max(dy, 0f) * Max(dy, 0f)) + Min(Max(dx, dy), 0f) - radius;
                    float a = d <= 0f ? 1f : Pow(Clamp01(1f - d / margin), 2.2f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f + 0.5f));
                }
            }

            shadow = MakeSprite(pixels, size, size, new Vector4(56f, 56f, 56f, 56f), SpritePixelsPerUnit);
            return shadow;
        }

        /// <summary>Filled circle of the given diameter in canvas units.</summary>
        public static Sprite Circle(int diameter)
        {
            diameter = Clamp(diameter, 4, 512);
            if (CircleCache.TryGetValue(diameter, out var cached) && cached != null)
            {
                return cached;
            }

            int size = diameter * Supersample;
            var pixels = new Color32[size * size];
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - c;
                    float dy = y + 0.5f - c;
                    float d = Sqrt(dx * dx + dy * dy) - c;
                    pixels[y * size + x] = new Color32(255, 255, 255, Alpha(0.5f - d));
                }
            }

            var sprite = MakeSprite(pixels, size, size, Vector4.zero, SpritePixelsPerUnit);
            CircleCache[diameter] = sprite;
            return sprite;
        }

        /// <summary>Annulus (thickness 12% of the diameter) for radial progress (<c>Image.Type.Filled</c>).</summary>
        public static Sprite Ring(int diameter)
        {
            diameter = Clamp(diameter, 16, 512);
            if (RingCache.TryGetValue(diameter, out var cached) && cached != null)
            {
                return cached;
            }

            int size = diameter * Supersample;
            var pixels = new Color32[size * size];
            float c = size * 0.5f;
            float outer = c;
            float inner = c * 0.76f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - c;
                    float dy = y + 0.5f - c;
                    float len = Sqrt(dx * dx + dy * dy);
                    float a = Clamp01(0.5f - (len - outer)) * Clamp01(0.5f + (len - inner));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f + 0.5f));
                }
            }

            var sprite = MakeSprite(pixels, size, size, Vector4.zero, SpritePixelsPerUnit);
            RingCache[diameter] = sprite;
            return sprite;
        }

        /// <summary>Vertical alpha gradient (white): opaque at the top or at the bottom, transparent at the other end.</summary>
        public static Sprite Gradient(bool topOpaque)
        {
            var existing = topOpaque ? gradientTopOpaque : gradientBottomOpaque;
            if (existing != null)
            {
                return existing;
            }

            const int height = 128;
            const int width = 4;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float t = y / (float)(height - 1);
                float a = topOpaque ? t : 1f - t;
                a = a * a * (3f - 2f * a);
                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)(a * 255f + 0.5f));
                }
            }

            var sprite = MakeSprite(pixels, width, height, Vector4.zero, SpritePixelsPerUnit);
            if (topOpaque)
            {
                gradientTopOpaque = sprite;
            }
            else
            {
                gradientBottomOpaque = sprite;
            }

            return sprite;
        }

        /// <summary>White icon rendered for a display size of <paramref name="displaySize"/> canvas units (tint with <c>Image.color</c>).</summary>
        public static Sprite Icon(UiIcon icon, int displaySize)
        {
            if (icon == UiIcon.None)
            {
                return null;
            }

            displaySize = Clamp(displaySize, 8, 256);
            int key = (int)icon * 1000 + displaySize;
            if (IconCache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            int size = displaySize * Supersample;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float d = IconDistance(icon, u, v) * size;
                    pixels[y * size + x] = new Color32(255, 255, 255, Alpha(0.5f - d));
                }
            }

            var sprite = MakeSprite(pixels, size, size, Vector4.zero, SpritePixelsPerUnit);
            IconCache[key] = sprite;
            return sprite;
        }

        // ----- internals -----

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            RoundedCache.Clear();
            OutlineCache.Clear();
            CircleCache.Clear();
            RingCache.Clear();
            IconCache.Clear();
            shadow = null;
            gradientTopOpaque = null;
            gradientBottomOpaque = null;
        }

        private static byte Alpha(float coverage)
        {
            return (byte)(Clamp01(coverage) * 255f + 0.5f);
        }

        private static float RoundedDistance(int x, int y, int size, int r)
        {
            float half = size * 0.5f;
            float inner = half - r;
            float dx = Abs(x + 0.5f - half) - inner;
            float dy = Abs(y + 0.5f - half) - inner;
            float ox = Max(dx, 0f);
            float oy = Max(dy, 0f);
            return Sqrt(ox * ox + oy * oy) + Min(Max(dx, dy), 0f) - r;
        }

        private static Sprite MakeSprite(Color32[] pixels, int width, int height, Vector4 border, float pixelsPerUnit)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "MT_UiProcedural",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), pixelsPerUnit, 0,
                SpriteMeshType.FullRect, border);
            sprite.name = "MT_UiProcedural";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        // ----- signed distance helpers (unit square, y up) -----

        private static float Seg(float px, float py, float ax, float ay, float bx, float by)
        {
            float pax = px - ax;
            float pay = py - ay;
            float bax = bx - ax;
            float bay = by - ay;
            float h = Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay));
            float dx = pax - bax * h;
            float dy = pay - bay * h;
            return Sqrt(dx * dx + dy * dy);
        }

        private static float Disc(float px, float py, float cx, float cy, float r)
        {
            float dx = px - cx;
            float dy = py - cy;
            return Sqrt(dx * dx + dy * dy) - r;
        }

        private static float RingStroke(float px, float py, float cx, float cy, float r, float halfThickness)
        {
            return Abs(Disc(px, py, cx, cy, r)) - halfThickness;
        }

        private static float Box(float px, float py, float cx, float cy, float hw, float hh, float r)
        {
            float dx = Abs(px - cx) - hw + r;
            float dy = Abs(py - cy) - hh + r;
            float ox = Max(dx, 0f);
            float oy = Max(dy, 0f);
            return Sqrt(ox * ox + oy * oy) + Min(Max(dx, dy), 0f) - r;
        }

        private static float Tri(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d = Min(Seg(px, py, ax, ay, bx, by), Min(Seg(px, py, bx, by, cx, cy), Seg(px, py, cx, cy, ax, ay)));
            float s1 = (bx - ax) * (py - ay) - (by - ay) * (px - ax);
            float s2 = (cx - bx) * (py - by) - (cy - by) * (px - bx);
            float s3 = (ax - cx) * (py - cy) - (ay - cy) * (px - cx);
            bool inside = (s1 >= 0f && s2 >= 0f && s3 >= 0f) || (s1 <= 0f && s2 <= 0f && s3 <= 0f);
            return inside ? -d : d;
        }

        private static float IconDistance(UiIcon icon, float x, float y)
        {
            switch (icon)
            {
                case UiIcon.Check:
                    return Min(Seg(x, y, 0.2f, 0.5f, 0.42f, 0.28f), Seg(x, y, 0.42f, 0.28f, 0.8f, 0.7f)) - 0.07f;

                case UiIcon.Warning:
                {
                    float triangle = Min(Seg(x, y, 0.5f, 0.9f, 0.08f, 0.14f),
                        Min(Seg(x, y, 0.08f, 0.14f, 0.92f, 0.14f), Seg(x, y, 0.92f, 0.14f, 0.5f, 0.9f))) - 0.05f;
                    float mark = Min(Seg(x, y, 0.5f, 0.38f, 0.5f, 0.6f) - 0.05f, Disc(x, y, 0.5f, 0.27f, 0.05f));
                    return Min(triangle, mark);
                }

                case UiIcon.User:
                {
                    float head = Disc(x, y, 0.5f, 0.7f, 0.19f);
                    float body = Max(Disc(x, y, 0.5f, 0.06f, 0.37f), y - 0.4f);
                    return Min(head, body);
                }

                case UiIcon.Trophy:
                {
                    float bowl = Max(Disc(x, y, 0.5f, 0.6f, 0.22f), y - 0.8f);
                    float handles = Min(RingStroke(x, y, 0.2f, 0.64f, 0.1f, 0.03f), RingStroke(x, y, 0.8f, 0.64f, 0.1f, 0.03f));
                    float stem = Box(x, y, 0.5f, 0.28f, 0.04f, 0.08f, 0.01f);
                    float baseBox = Box(x, y, 0.5f, 0.16f, 0.17f, 0.045f, 0.03f);
                    return Min(Min(bowl, handles), Min(stem, baseBox));
                }

                case UiIcon.Headset:
                {
                    float body = Box(x, y, 0.5f, 0.5f, 0.44f, 0.27f, 0.14f);
                    float lenses = Min(Box(x, y, 0.3f, 0.5f, 0.12f, 0.12f, 0.07f), Box(x, y, 0.7f, 0.5f, 0.12f, 0.12f, 0.07f));
                    float nose = Disc(x, y, 0.5f, 0.24f, 0.1f);
                    return Max(body, Max(-lenses, -nose));
                }

                case UiIcon.Server:
                {
                    float d = 1f;
                    for (int i = 0; i < 3; i++)
                    {
                        float cy = 0.8f - i * 0.3f;
                        float bar = Box(x, y, 0.5f, cy, 0.4f, 0.11f, 0.06f);
                        bar = Max(bar, -Disc(x, y, 0.74f, cy, 0.045f));
                        bar = Max(bar, -Disc(x, y, 0.62f, cy, 0.045f));
                        d = Min(d, bar);
                    }

                    return d;
                }

                case UiIcon.Refresh:
                    return RefreshDistance(x, y);

                case UiIcon.Recenter:
                {
                    float ring = RingStroke(x, y, 0.5f, 0.5f, 0.25f, 0.045f);
                    float dot = Disc(x, y, 0.5f, 0.5f, 0.075f);
                    float ticks = Min(Min(Seg(x, y, 0.5f, 0.05f, 0.5f, 0.2f), Seg(x, y, 0.5f, 0.8f, 0.5f, 0.95f)),
                        Min(Seg(x, y, 0.05f, 0.5f, 0.2f, 0.5f), Seg(x, y, 0.8f, 0.5f, 0.95f, 0.5f))) - 0.045f;
                    return Min(Min(ring, dot), ticks);
                }

                case UiIcon.ArrowUp:
                    return Min(Seg(x, y, 0.5f, 0.16f, 0.5f, 0.84f),
                        Min(Seg(x, y, 0.5f, 0.84f, 0.2f, 0.54f), Seg(x, y, 0.5f, 0.84f, 0.8f, 0.54f))) - 0.06f;

                case UiIcon.ArrowDown:
                    return Min(Seg(x, y, 0.5f, 0.84f, 0.5f, 0.16f),
                        Min(Seg(x, y, 0.5f, 0.16f, 0.2f, 0.46f), Seg(x, y, 0.5f, 0.16f, 0.8f, 0.46f))) - 0.06f;

                case UiIcon.Play:
                    return Tri(x, y, 0.3f, 0.26f, 0.3f, 0.74f, 0.74f, 0.5f) - 0.05f;

                case UiIcon.Close:
                    return Min(Seg(x, y, 0.22f, 0.22f, 0.78f, 0.78f), Seg(x, y, 0.22f, 0.78f, 0.78f, 0.22f)) - 0.065f;

                case UiIcon.Female:
                    return Min(RingStroke(x, y, 0.5f, 0.64f, 0.23f, 0.045f),
                        Min(Seg(x, y, 0.5f, 0.41f, 0.5f, 0.08f), Seg(x, y, 0.35f, 0.22f, 0.65f, 0.22f)) - 0.045f);

                case UiIcon.Male:
                    return Min(RingStroke(x, y, 0.42f, 0.42f, 0.23f, 0.045f),
                        Min(Seg(x, y, 0.58f, 0.58f, 0.86f, 0.86f),
                            Min(Seg(x, y, 0.6f, 0.86f, 0.86f, 0.86f), Seg(x, y, 0.86f, 0.6f, 0.86f, 0.86f))) - 0.045f);

                case UiIcon.Suitcase:
                {
                    float body = Box(x, y, 0.5f, 0.38f, 0.4f, 0.27f, 0.07f);
                    float slits = Min(Box(x, y, 0.3f, 0.38f, 0.014f, 0.27f, 0f), Box(x, y, 0.7f, 0.38f, 0.014f, 0.27f, 0f));
                    body = Max(body, -slits);
                    float handle = Min(Seg(x, y, 0.37f, 0.64f, 0.37f, 0.82f),
                        Min(Seg(x, y, 0.37f, 0.82f, 0.63f, 0.82f), Seg(x, y, 0.63f, 0.82f, 0.63f, 0.64f))) - 0.04f;
                    return Min(body, handle);
                }

                default:
                    return 1f;
            }
        }

        private static float RefreshDistance(float x, float y)
        {
            const float cx = 0.5f;
            const float cy = 0.5f;
            const float radius = 0.3f;
            const float halfThickness = 0.05f;
            float gapStart = 20f * Deg2Rad;
            float gapEnd = 80f * Deg2Rad;

            float dx = x - cx;
            float dy = y - cy;
            float length = Sqrt(dx * dx + dy * dy);
            float angle = Atan2(dy, dx);
            if (angle < 0f)
            {
                angle += 2f * PI;
            }

            float arc;
            if (angle >= gapEnd || angle <= gapStart)
            {
                arc = Abs(length - radius);
            }
            else
            {
                float e0x = cx + radius * Cos(gapStart);
                float e0y = cy + radius * Sin(gapStart);
                float e1x = cx + radius * Cos(gapEnd);
                float e1y = cy + radius * Sin(gapEnd);
                float d0 = Sqrt((x - e0x) * (x - e0x) + (y - e0y) * (y - e0y));
                float d1 = Sqrt((x - e1x) * (x - e1x) + (y - e1y) * (y - e1y));
                arc = Min(d0, d1);
            }

            // Arrow head on the 20 degree end, pointing along the counter-clockwise tangent into the gap.
            float ex = cx + radius * Cos(gapStart);
            float ey = cy + radius * Sin(gapStart);
            float tx = -Sin(gapStart);
            float ty = Cos(gapStart);
            float nx = Cos(gapStart);
            float ny = Sin(gapStart);
            float head = Tri(x, y,
                ex + tx * 0.17f, ey + ty * 0.17f,
                ex - tx * 0.03f + nx * 0.14f, ey - ty * 0.03f + ny * 0.14f,
                ex - tx * 0.03f - nx * 0.14f, ey - ty * 0.03f - ny * 0.14f) - 0.015f;
            return Min(arc - halfThickness, head);
        }
    }
}
