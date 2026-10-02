using MultiTravel.Gameplay.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace MultiTravel.Gameplay.Presentation
{
    /// <summary>
    /// Shared look of the diegetic displays (stopwatch, scoreboard, result card, captions): brand colours, 3D TMP text
    /// factory and URP-safe runtime materials. Everything here is URP compatible and allocation-free after start-up
    /// (materials and meshes are created once and cached by their owners).
    /// </summary>
    public static class PresentationStyle
    {
        /// <summary>Deep navy used for display faces and the hand tint.</summary>
        public static readonly Color Navy = new Color32(0x07, 0x2A, 0x63, 0xFF);

        /// <summary>Brand blue.</summary>
        public static readonly Color Blue = VrUiStyle.DeepBlue;

        /// <summary>Brand teal (positive, filled pips, success).</summary>
        public static readonly Color Teal = VrUiStyle.Teal;

        /// <summary>Brand orange (negative, accents).</summary>
        public static readonly Color Orange = VrUiStyle.Orange;

        /// <summary>Soft white for primary text on dark faces.</summary>
        public static readonly Color Ivory = new Color(0.98f, 0.98f, 0.96f, 1f);

        /// <summary>Dimmed text / empty pips.</summary>
        public static readonly Color Dim = new Color32(0x35, 0x5C, 0x9A, 0xFF);

        /// <summary>Hex colour of <see cref="Teal"/> for TMP rich text.</summary>
        public const string TealHex = "#19B394";

        /// <summary>Hex colour of <see cref="Dim"/> for TMP rich text.</summary>
        public const string DimHex = "#355C9A";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int BlendId = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int SrcBlendAlphaId = Shader.PropertyToID("_SrcBlendAlpha");
        private static readonly int DstBlendAlphaId = Shader.PropertyToID("_DstBlendAlpha");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int CullId = Shader.PropertyToID("_Cull");

        /// <summary><c>_BaseColor</c> property id (URP) for property blocks.</summary>
        public static int BaseColorProperty => BaseColorId;

        /// <summary><c>_EmissionColor</c> property id for property blocks.</summary>
        public static int EmissionColorProperty => EmissionColorId;

        /// <summary>Creates a world-space TextMeshPro label with the project font. Font size is in TMP 3D units (≈ 0.1 m per 1).</summary>
        public static TextMeshPro CreateText(string name, Transform parent, float fontSize, Color color, TextAlignmentOptions alignment, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshPro>();
            var font = VrUiStyle.Font;
            if (font != null)
            {
                text.font = font;
            }

            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta = size;
            text.text = string.Empty;
            text.richText = true;
            return text;
        }

        /// <summary>Finds the first URP shader that exists in the player (Lit, then Unlit), falling back to Sprites/Default.</summary>
        public static Shader FindUrpShader(bool preferUnlit)
        {
            Shader shader = null;
            if (preferUnlit)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            return shader;
        }

        /// <summary>
        /// Creates a runtime URP material. Transparent materials use alpha blending, no depth write and the Transparent
        /// queue; <paramref name="emission"/> is applied when the shader supports it (Lit).
        /// </summary>
        public static Material CreateMaterial(string name, Color baseColor, Color emission, bool transparent, bool unlit, float smoothness = 0.5f, float metallic = 0f)
        {
            var shader = FindUrpShader(unlit);
            var material = new Material(shader) { name = name };
            SetBaseColor(material, baseColor);
            if (material.HasProperty(SmoothnessId))
            {
                material.SetFloat(SmoothnessId, smoothness);
            }

            if (material.HasProperty(MetallicId))
            {
                material.SetFloat(MetallicId, metallic);
            }

            if (material.HasProperty(EmissionColorId))
            {
                bool emissive = emission.maxColorComponent > 0.0001f;
                material.SetColor(EmissionColorId, emission);
                if (emissive)
                {
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            }

            if (transparent)
            {
                MakeTransparent(material);
            }

            return material;
        }

        /// <summary>Sets <c>_BaseColor</c> (URP) or <c>_Color</c> (legacy) on a material.</summary>
        public static void SetBaseColor(Material material, Color color)
        {
            if (material.HasProperty(BaseColorId))
            {
                material.SetColor(BaseColorId, color);
            }
            else if (material.HasProperty(ColorId))
            {
                material.SetColor(ColorId, color);
            }
        }

        /// <summary>Switches a URP Lit / Unlit material to alpha-blended transparency (no ZWrite, Transparent queue).</summary>
        public static void MakeTransparent(Material material)
        {
            if (material.HasProperty(SurfaceId))
            {
                material.SetFloat(SurfaceId, 1f);
            }

            if (material.HasProperty(BlendId))
            {
                material.SetFloat(BlendId, 0f);
            }

            if (material.HasProperty(SrcBlendId))
            {
                material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
                material.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty(SrcBlendAlphaId))
            {
                material.SetFloat(SrcBlendAlphaId, (float)BlendMode.One);
                material.SetFloat(DstBlendAlphaId, (float)BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty(ZWriteId))
            {
                material.SetFloat(ZWriteId, 0f);
            }

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        /// <summary>Renders both faces (quads seen from behind, rings).</summary>
        public static void MakeDoubleSided(Material material)
        {
            if (material.HasProperty(CullId))
            {
                material.SetFloat(CullId, (float)CullMode.Off);
            }
        }

        /// <summary>Unit quad in the XY plane facing -Z (Unity's default quad orientation), 1 × 1 m.</summary>
        public static Mesh CreateQuadMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Flat ring (annulus) in the XZ plane facing +Y, used for the tutorial hint and highlights.</summary>
        public static Mesh CreateRingMesh(string name, float innerRadius, float outerRadius, int segments)
        {
            segments = Mathf.Max(8, segments);
            var vertices = new Vector3[segments * 2];
            var uv = new Vector2[segments * 2];
            var normals = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * (Mathf.PI * 2f / segments);
                float c = Mathf.Cos(angle);
                float s = Mathf.Sin(angle);
                vertices[i * 2] = new Vector3(c * innerRadius, 0f, s * innerRadius);
                vertices[i * 2 + 1] = new Vector3(c * outerRadius, 0f, s * outerRadius);
                uv[i * 2] = new Vector2((float)i / segments, 0f);
                uv[i * 2 + 1] = new Vector2((float)i / segments, 1f);
                normals[i * 2] = Vector3.up;
                normals[i * 2 + 1] = Vector3.up;

                int next = (i + 1) % segments;
                int t = i * 6;
                triangles[t] = i * 2;
                triangles[t + 1] = next * 2 + 1;
                triangles[t + 2] = i * 2 + 1;
                triangles[t + 3] = i * 2;
                triangles[t + 4] = next * 2;
                triangles[t + 5] = next * 2 + 1;
            }

            var mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Rotates <paramref name="target"/> so its +Z faces away from the camera (TMP text reads correctly), yaw only.</summary>
        public static void FaceCamera(Transform target, Camera camera, bool yawOnly)
        {
            if (camera == null)
            {
                return;
            }

            var direction = target.position - camera.transform.position;
            if (yawOnly)
            {
                direction.y = 0f;
            }

            if (direction.sqrMagnitude > 1e-6f)
            {
                target.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
        }

        /// <summary>Ease-out-back used by scale pops (overshoots slightly before settling).</summary>
        public static float EaseOutBack(float k)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float x = k - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        /// <summary>Smooth step 0..1.</summary>
        public static float SmoothStep(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * (3f - 2f * k);
        }
    }
}
