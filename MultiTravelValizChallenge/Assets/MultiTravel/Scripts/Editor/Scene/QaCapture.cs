using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MultiTravel.EditorTools.SceneBuild
{
    /// <summary>
    /// Renders fixed-camera QA stills of the Main scene (player eye forward/left/right, wide overview, suitcase close-up).
    /// Batch: <c>-executeMethod MultiTravel.EditorTools.SceneBuild.QaCapture.Run -qaOut &lt;dir&gt;</c>.
    /// </summary>
    public static class QaCapture
    {
        private const int Width = 1600;
        private const int Height = 1000;

        public static void Run()
        {
            string outDir = "../docs/qa/" + DateTime.Now.ToString("yyyy-MM-dd") + "/v4";
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-qaOut")
                {
                    outDir = args[i + 1];
                }
            }

            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene("Assets/MultiTravel/Scenes/Main.unity", OpenSceneMode.Single);
            var camGo = new GameObject("QaCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 80f;
            cam.nearClipPlane = 0.05f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;

            Shot(cam, outDir, "scene-eye-front", new Vector3(0f, 1.70f, 0f), Quaternion.Euler(10f, 0f, 0f));
            Shot(cam, outDir, "scene-eye-left", new Vector3(0f, 1.70f, 0f), Quaternion.Euler(10f, -75f, 0f));
            Shot(cam, outDir, "scene-eye-right", new Vector3(0f, 1.70f, 0f), Quaternion.Euler(10f, 75f, 0f));
            Shot(cam, outDir, "scene-eye-down", new Vector3(0f, 1.70f, 0f), Quaternion.Euler(50f, 0f, 0f));
            Shot(cam, outDir, "scene-overview", new Vector3(0f, 2.4f, -2.8f), Quaternion.Euler(22f, 0f, 0f));
            Shot(cam, outDir, "scene-top", new Vector3(0f, 5.0f, 0.8f), Quaternion.Euler(90f, 0f, 0f));
            UnityEngine.Object.DestroyImmediate(camGo);
        }

        private static void Shot(Camera cam, string dir, string name, Vector3 pos, Quaternion rot)
        {
            cam.transform.SetPositionAndRotation(pos, rot);
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
