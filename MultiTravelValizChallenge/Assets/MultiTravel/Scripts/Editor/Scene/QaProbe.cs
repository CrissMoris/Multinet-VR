using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.XR.CoreUtils;

namespace MultiTravel.EditorTools.SceneBuild
{
    public static class QaProbe
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/MultiTravel/Scenes/Main.unity", OpenSceneMode.Single);
            var o = Object.FindAnyObjectByType<XROrigin>();
            Debug.Log("PROBE origin " + o.name + " mode=" + o.RequestedTrackingOriginMode + " yoff=" + o.CameraYOffset);
            foreach (var c in o.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (c == null) continue;
                string n = c.GetType().Name;
                if (n.Contains("Provider") || n.Contains("Locomotion") || n.Contains("CharacterController") || n.Contains("Mediator") || n.Contains("ActionManager") || n.Contains("Teleport"))
                    Debug.Log("PROBE " + c.transform.name + " : " + n + " enabled=" + c.enabled + " path=" + c.transform.parent?.name);
            }
            foreach (var c in o.GetComponentsInChildren<CharacterController>(true)) Debug.Log("PROBE CC " + c.name + " h=" + c.height + " r=" + c.radius);
        }
    }
}
