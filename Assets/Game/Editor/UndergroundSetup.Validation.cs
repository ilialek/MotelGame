using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using MotelGame.Service;
using MotelGame.Horror;

namespace MotelGame.Editor
{
    public static partial class UndergroundSetup
    {
        [MenuItem("Motel/Underground/Validate current scene")]
        public static void Validate()
        {
            Directory.CreateDirectory(Reports);
            var scene = SceneManager.GetActiveScene();
            var lines = new List<string>();
            void Check(bool ok, string text)
            {
                lines.Add((ok ? "PASS " : "FAIL ") + text);
            }

            Check(scene.path == ScenePath, "Active V4 gameplay scene");
            var hunt = All<MimicHunt>(scene).Single();
            Check(hunt.ConfigurationValid(), "Single configured sphere controller / six room attack points");
            var nodes = hunt.Nodes;
            var seen = new HashSet<VentNode>();
            var queue = new Queue<VentNode>();
            queue.Enqueue(nodes[0]);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                if (!seen.Add(n))
                    continue;
                foreach (var next in n.neighbours)
                    queue.Enqueue(next);
            }

            Check(seen.Count == nodes.Length, "All " + nodes.Length + " graph nodes connected");
            Check(nodes.Sum(n => n.neighbours.Length) / 2 - nodes.Length + 1 >= 2, "At least two alternate graph loops");
            Physics.SyncTransforms();
            foreach (var n in nodes)
            {
                foreach (var next in n.neighbours.Where(p => p.index > n.index))
                {
                    var a = n.transform.position;
                    var b = next.transform.position;
                    var delta = b - a;
                    int samples = Mathf.CeilToInt(delta.magnitude / .2f);
                    var hits = new HashSet<string>();
                    for (int i = 0; i <= samples; i++)
                    {
                        var foot = Vector3.Lerp(a, b, (float)i / samples) - Vector3.up * .425f;
                        foreach (var c in Physics.OverlapCapsule(foot + Vector3.up * .34f, foot + Vector3.up * .56f, .32f, ~0, QueryTriggerInteraction.Ignore))
                            hits.Add(PathOf(c.transform));
                        Check(Physics.Raycast(foot + Vector3.up * .1f, Vector3.down, .25f, ~0, QueryTriggerInteraction.Ignore), "Floor support " + n.index + "->" + next.index + " sample " + i);
                    }

                    Check(hits.Count == 0, "Crouched clearance " + n.index + "->" + next.index + " " + string.Join("; ", hits));
                    Check(!Physics.SphereCast(a, .305f, delta.normalized, out _, delta.magnitude, ~0, QueryTriggerInteraction.Ignore), "Sphere clearance " + n.index + "->" + next.index);
                }

                if (n.hiddenAnchor != null)
                {
                    var p = new[]{n.transform.position, n.hiddenBend.position, n.hiddenAnchor.position};
                    for (int i = 0; i < 2; i++)
                    {
                        var d = p[i + 1] - p[i];
                        Check(!Physics.SphereCast(p[i], .305f, d.normalized, out var hit, d.magnitude, ~0, QueryTriggerInteraction.Ignore), "Hidden elbow clearance " + n.index + " segment " + i + " " + hit.collider?.name);
                    }
                }
            }

            VentEntranceSetup.ValidateRoutes(scene, lines);

            var hashes = All<NetworkObject>(scene).Select(n => new SerializedObject(n).FindProperty("GlobalObjectIdHash").uintValue).ToArray();
            Check(hashes.All(h => h != 0) && hashes.Distinct().Count() == hashes.Length, "Unique scene network identities");
            Check(All<Transform>(scene).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), "No missing scene scripts");
            Check(!hunt.Decoy.gameObject.activeInHierarchy && !All<MimicPresentation>(scene).Any(p => p.enabled && p.gameObject.activeInHierarchy), "Legacy lure and monster visual disabled");
            Check(All<VentNode>(scene).Where(n => !n.underground).All(n => !n.gameObject.activeInHierarchy), "Old ceiling graph inactive");
            File.WriteAllLines(Reports + "/scene-validation.txt", lines);
            if (lines.Any(l => l.StartsWith("FAIL")))
                throw new Exception("Underground geometry validation failed. See " + Reports + "/scene-validation.txt");
            // Reuse the established whole-motel reference, NPC route and door-sweep checks as well.
            MotelLayoutV4.Validate();
        }

        [MenuItem("Motel/Underground/Build validation candidate (Mac)")]
        public static void Build()
        {
            Validate();
            const string folder = "Builds/VentEntranceFinal";
            Directory.CreateDirectory(folder);
            var snapshots = Directory.GetFiles("ProjectSettings").Concat(Directory.GetFiles("Assets/Settings", "*.asset")).ToDictionary(p => p, File.ReadAllBytes);
            try
            {
                var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes = new[]{ScenePath}, locationPathName = folder + "/MotelGame.app", target = BuildTarget.StandaloneOSX, options = BuildOptions.Development});
                File.WriteAllText(Reports + "/candidate-build.txt", result.summary.result + " errors=" + result.summary.totalErrors + " warnings=" + result.summary.totalWarnings + " " + DateTime.Now.ToString("O"));
                if (result.summary.result != BuildResult.Succeeded)
                    throw new Exception("Underground candidate failed");
            }
            finally
            {
                foreach (var pair in snapshots)
                    if (!File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value))
                        File.WriteAllBytes(pair.Key, pair.Value);
                AssetDatabase.Refresh();
            }
        }
    }
}
