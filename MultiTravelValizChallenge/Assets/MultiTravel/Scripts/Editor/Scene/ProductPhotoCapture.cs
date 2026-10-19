using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MultiTravel.EditorTools.SceneBuild
{
    /// <summary>
    /// Studio stills of product models with their in-game materials (art review, before / after comparisons).
    /// Batch: <c>-executeMethod MultiTravel.EditorTools.SceneBuild.ProductPhotoCapture.Run -qaOut &lt;dir&gt; -models shirt,shirt-hanging</c>.
    /// Each model is rendered from a front three-quarter and a top-down view on a neutral backdrop.
    /// </summary>
    public static class ProductPhotoCapture
    {
        private const int Size = 900;
        private const string ModelsFolder = "Assets/MultiTravel/Art/Models/Products/";

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string outDir = "../docs/qa/products";
            string models = "shirt,shirt-hanging,men-shoes,women-shoes,beach-towel";
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-qaOut")
                {
                    outDir = args[i + 1];
                }
                else if (args[i] == "-models")
                {
                    models = args[i + 1];
                }
            }

            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.62f, 0.64f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.44f, 0.43f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.24f, 0.23f);

            var key = new GameObject("Key").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.25f;
            key.color = new Color(1f, 0.97f, 0.93f);
            key.shadows = LightShadows.Soft;
            key.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            var rim = new GameObject("Rim").AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.45f;
            rim.transform.rotation = Quaternion.Euler(25f, 150f, 0f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = Vector3.one * 2f;
            var floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMat.SetColor("_BaseColor", new Color(0.86f, 0.85f, 0.83f));
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.01f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.86f, 0.85f, 0.83f);
            // Neutral studio look: no project-wide post-processing (tonemapping / colour grading) and no skybox.
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            RenderSettings.skybox = null;
            RenderSettings.fog = false;

            foreach (var raw in models.Split(','))
            {
                string id = raw.Trim();
                // "Assets/..." = any model asset (e.g. Poly Haven scans); otherwise a product model id.
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(id.StartsWith("Assets/", StringComparison.Ordinal) ? id : ModelsFolder + id + ".fbx");
                id = Path.GetFileNameWithoutExtension(id);
                if (asset == null)
                {
                    Debug.LogWarning("[MultiTravel] ProductPhotoCapture: no model " + id);
                    continue;
                }

                var go = (GameObject)UnityEngine.Object.Instantiate(asset);
                var bounds = new Bounds(go.transform.position, Vector3.zero);
                bool any = false;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (r.name.StartsWith("UI.", StringComparison.Ordinal))
                    {
                        r.enabled = false;
                        continue;
                    }

                    bounds = any ? Encapsulate(bounds, r.bounds) : r.bounds;
                    any = true;
                }

                // Rest on the floor (hanging variants hang from their hook at the origin).
                go.transform.position += Vector3.up * (0.001f - bounds.min.y);
                bounds.center += Vector3.up * (0.001f - bounds.min.y);
                float radius = Mathf.Max(0.05f, bounds.extents.magnitude);
                float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;

                // Slot forward is +Z (collar / toe / screen face the participant): shoot from +Z.
                Shot(cam, outDir, id + "_front", bounds.center + new Vector3(0.45f, 0.6f, 1f).normalized * distance, bounds.center);
                Shot(cam, outDir, id + "_top", bounds.center + Vector3.up * distance + Vector3.forward * 0.001f, bounds.center);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static Bounds Encapsulate(Bounds a, Bounds b)
        {
            a.Encapsulate(b);
            return a;
        }

        private static void Shot(Camera cam, string dir, string name, Vector3 position, Vector3 target)
        {
            cam.transform.position = position;
            cam.transform.LookAt(target);
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
