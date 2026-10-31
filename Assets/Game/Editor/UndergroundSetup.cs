using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using MotelGame.Service;
using MotelGame.Horror;

namespace MotelGame.Editor
{
    [InitializeOnLoad]
    public static partial class UndergroundSetup
    {
        const string ScenePath = "Assets/Game/Scenes/Motel_Layout_V4.unity", Reports = "Documentation/Underground";
        static double next;
        static UndergroundSetup() => EditorApplication.update += Poll;
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || UnityEditor.BuildPipeline.isBuildingPlayer || File.Exists("Library/MotelServiceRefresh.request"))
                return;
            next = EditorApplication.timeSinceStartup + 1;
            foreach (var action in new[]{"Inspect", "Install", "Validate", "RefineAccess", "Build"})
            {
                string file = "Library/MotelUnderground" + action + ".request";
                if (!File.Exists(file))
                    continue;
                File.Delete(file);
                Directory.CreateDirectory(Reports);
                try
                {
                    if (action == "Inspect")
                        Inspect();
                    else if (action == "Install")
                        Install();
                    else if (action == "Validate")
                        Validate();
                    else if (action == "RefineAccess")
                        RefineAccess();
                    else
                        Build();
                    File.WriteAllText(Reports + "/" + action + ".done", DateTime.Now.ToString("O"));
                }
                catch (Exception e)
                {
                    File.WriteAllText(Reports + "/editor-error.txt", e.ToString());
                    Debug.LogException(e);
                }
            }
        }

        static T[] All<T>(Scene scene)
            where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
        public static void Inspect()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                throw new Exception("Open V4 for inspection; no scene switched automatically.");
            Physics.SyncTransforms();
            var lines = new List<string>();
            foreach (var room in All<ServiceRoom>(scene))
            {
                var root = room.transform.parent.parent;
                lines.Add("ROOM " + room.RoomNumber + " root=" + PathOf(root) + " pos=" + root.position + " yaw=" + root.eulerAngles);
                foreach (var c in root.GetComponentsInChildren<Collider>(true).Where(c => c.name.Contains("Bed") || c.name.Contains("bed") || c.name.Contains("Floor") || c.name.Contains("Trash")))
                    lines.Add("  " + PathOf(c.transform) + " " + c.bounds);
                lines.Add("  Idle " + room.GuestIdle.position + " inside=" + room.DoorInside.position);
            }

            foreach (var c in All<Collider>(scene).Where(c => c.enabled && c.gameObject.activeInHierarchy && c.bounds.min.y < -.01f && c.bounds.max.y > -4 && c.bounds.max.x > 0 && c.bounds.min.x < 18 && c.bounds.max.z > 0 && c.bounds.min.z < 24))
                lines.Add("BELOW " + PathOf(c.transform) + " " + c.bounds + " rotation=" + c.transform.eulerAngles);
            foreach (var n in All<VentNode>(scene))
                lines.Add("VENT " + PathOf(n.transform) + " " + n.transform.position);
            foreach (var t in All<Transform>(scene).Where(t => t.name.ToLowerInvariant().Contains("curtain")))
                lines.Add("CURTAIN " + PathOf(t) + " active=" + t.gameObject.activeInHierarchy);
            foreach (float x in new[]{13.42f, 13.7f, 14.1f})
                foreach (var c in Physics.OverlapCapsule(new Vector3(x, .37f, 3.5f), new Vector3(x, 1.46f, 3.5f), .34f, ~0, QueryTriggerInteraction.Ignore))
                    lines.Add("ROOM APPROACH " + x + " " + PathOf(c.transform) + " " + c.bounds);
            File.WriteAllLines(Reports + "/inspection.txt", lines);
        }
    }
}
