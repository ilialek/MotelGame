using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using UnityEngine.Rendering;
using MotelGame.Service;
using MotelGame.Horror;
using MotelGame.Interactions;

namespace MotelGame.Editor
{
    public static partial class UndergroundSetup
    {
        const float FloorY = -3.1f, CeilingY = -1.8f, HalfWidth = .7f;
        static Transform underground;
        static Material metal, wallMaterial, sphereMaterial;
        static readonly List<VentNode> authoredNodes = new();
        static readonly List<Rect> passages = new(), ceilingHoles = new();
        static Transform Group(string name, Transform parent, Vector3 p = default)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = p;
            return t;
        }

        static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material material, uint layers = 2)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = center;
            g.transform.localScale = size;
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.renderingLayerMask = layers;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return g;
        }

        static void Set(UnityEngine.Object obj, string name, UnityEngine.Object value)
        {
            var s = new SerializedObject(obj);
            s.FindProperty(name).objectReferenceValue = value;
            s.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Bool(UnityEngine.Object obj, string name, bool value)
        {
            var s = new SerializedObject(obj);
            s.FindProperty(name).boolValue = value;
            s.ApplyModifiedPropertiesWithoutUndo();
        }

        static Material Material(string name, Color color)
        {
            string path = "Assets/Game/Materials/Underground/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null)
                return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {name = name};
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", .15f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static VentNode Node(string name, float x, float z)
        {
            var node = Group(name, underground, new Vector3(x, FloorY + .45f, z)).gameObject.AddComponent<VentNode>();
            node.underground = true;
            node.index = authoredNodes.Count;
            node.sector = z < 7 ? 0 : z > 17 ? 2 : 1;
            node.neighbours = Array.Empty<VentNode>();
            authoredNodes.Add(node);
            return node;
        }

        static void Join(VentNode a, VentNode b)
        {
            if (Mathf.Abs(a.transform.position.x - b.transform.position.x) > .01f && Mathf.Abs(a.transform.position.z - b.transform.position.z) > .01f)
                throw new Exception("Non-orthogonal tunnel");
            a.neighbours = a.neighbours.Append(b).ToArray();
            b.neighbours = b.neighbours.Append(a).ToArray();
            var p = a.transform.position;
            var q = b.transform.position;
            passages.Add(Rect.MinMaxRect(Mathf.Min(p.x, q.x) - HalfWidth, Mathf.Min(p.z, q.z) - HalfWidth, Mathf.Max(p.x, q.x) + HalfWidth, Mathf.Max(p.z, q.z) + HalfWidth));
        }

        static void HiddenHole(VentNode n)
        {
            var p = n.transform.position;
            var root = Group("HiddenHole_" + n.index, underground, new Vector3(p.x, 0, p.z));
            n.hiddenBend = Group("Bend", root, new Vector3(0, -1.3f, 0));
            n.hiddenAnchor = Group("HiddenPocket", root, new Vector3(1, -1.3f, 0));
            ceilingHoles.Add(new Rect(p.x - .4f, p.z - .4f, .8f, .8f));
            // A vertical opening with an opaque upper elbow, never an invisible tunnel teleport point.
            Box("Left", root, new Vector3(-.45f, -1.35f, 0), new Vector3(.1f, .9f, 1), wallMaterial);
            Box("Cap", root, new Vector3(.5f, -.9f, 0), new Vector3(2, .1f, 1), wallMaterial);
            Box("PocketFloor", root, new Vector3(.95f, -1.7f, 0), new Vector3(1.1f, .1f, 1), wallMaterial);
            Box("PocketEnd", root, new Vector3(1.5f, -1.3f, 0), new Vector3(.1f, .8f, 1), wallMaterial);
            foreach (float z in new[]{-.45f, .45f})
                Box("Side", root, new Vector3(.5f, -1.35f, z), new Vector3(2, .9f, .1f), wallMaterial);
        }

        static void CutRoomFloor(ServiceRoom room, Vector3 center)
        {
            var original = GameObject.Find("Motel_Layout_V4/01_Architecture/Room" + room.RoomNumber + "/Floor");
            var b = original.GetComponent<Collider>().bounds;
            var material = original.GetComponent<Renderer>().sharedMaterial;
            var parent = original.transform.parent;
            original.SetActive(false);
            foreach (var r in new[]{Rect.MinMaxRect(b.min.x, b.min.z, center.x - .7f, b.max.z), Rect.MinMaxRect(center.x + .7f, b.min.z, b.max.x, b.max.z), Rect.MinMaxRect(center.x - .7f, b.min.z, center.x + .7f, center.z - .7f), Rect.MinMaxRect(center.x - .7f, center.z + .7f, center.x + .7f, b.max.z)})
                Box("FloorAroundHatch", parent, new Vector3(r.center.x, b.center.y, r.center.y), new Vector3(r.width, b.size.y, r.height), material, 1);
        }

        static void Hatch(ServiceRoom room, VentNode branch)
        {
            throw new NotSupportedException("Legacy floor hatch authoring is retired. Use the backed-up V4 scene and VentEntranceSetup migration.");
        }

        static void Shell()
        {
            var geometry = Group("TunnelShell", underground);
            var xs = new SortedSet<float>();
            var zs = new SortedSet<float>();
            foreach (var r in passages.Concat(ceilingHoles))
            {
                xs.Add(r.xMin);
                xs.Add(r.xMax);
                zs.Add(r.yMin);
                zs.Add(r.yMax);
            }

            float[] x = xs.ToArray(), z = zs.ToArray();
            bool[, ] inside = new bool[x.Length - 1, z.Length - 1];
            for (int i = 0; i < x.Length - 1; i++)
                for (int j = 0; j < z.Length - 1; j++)
                    inside[i, j] = passages.Any(r => r.Contains(new Vector2((x[i] + x[i + 1]) / 2, (z[j] + z[j + 1]) / 2)));
            for (int i = 0; i < x.Length - 1; i++)
                for (int j = 0; j < z.Length - 1; j++)
                {
                    if (!inside[i, j])
                        continue;
                    float cx = (x[i] + x[i + 1]) / 2, cz = (z[j] + z[j + 1]) / 2, dx = x[i + 1] - x[i], dz = z[j + 1] - z[j];
                    Box("Floor", geometry, new Vector3(cx, FloorY - .06f, cz), new Vector3(dx, .12f, dz), wallMaterial);
                    if (!ceilingHoles.Any(r => r.Contains(new Vector2(cx, cz))))
                        Box("Roof", geometry, new Vector3(cx, CeilingY + .06f, cz), new Vector3(dx, .12f, dz), wallMaterial);
                    if (i == 0 || !inside[i - 1, j])
                        Box("WallW", geometry, new Vector3(x[i] - .06f, (FloorY + CeilingY) / 2, cz), new Vector3(.12f, 1.54f, dz + .12f), wallMaterial);
                    if (i == x.Length - 2 || !inside[i + 1, j])
                        Box("WallE", geometry, new Vector3(x[i + 1] + .06f, (FloorY + CeilingY) / 2, cz), new Vector3(.12f, 1.54f, dz + .12f), wallMaterial);
                    if (j == 0 || !inside[i, j - 1])
                        Box("WallS", geometry, new Vector3(cx, (FloorY + CeilingY) / 2, z[j] - .06f), new Vector3(dx + .12f, 1.54f, .12f), wallMaterial);
                    if (j == z.Length - 2 || !inside[i, j + 1])
                        Box("WallN", geometry, new Vector3(cx, (FloorY + CeilingY) / 2, z[j + 1] + .06f), new Vector3(dx + .12f, 1.54f, .12f), wallMaterial);
                }
        }

        public static void RefineAccess()
        {
            throw new NotSupportedException("Floor hatches are retired. Use Motel/Underground/Validate wall entrances.");
        }

        [MenuItem("Motel/Underground/Install authored underground network")]
        public static void Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || scene.isDirty)
                throw new Exception("Save and open V4 before underground setup.");
            if (GameObject.Find("Motel_Layout_V4/05_Underground") != null)
                throw new Exception("Underground already authored. Edit existing layout; setup does not overwrite it.");
            Directory.CreateDirectory("Assets/Game/Materials/Underground");
            AssetDatabase.Refresh();
            wallMaterial = Material("DuctSteel", new Color(.055f, .065f, .07f));
            metal = Material("HatchMetal", new Color(.16f, .19f, .19f));
            sphereMaterial = Material("MimicSphere", new Color(.72f, .08f, .48f));
            underground = Group("05_Underground", GameObject.Find("Motel_Layout_V4").transform);
            authoredNodes.Clear();
            passages.Clear();
            ceilingHoles.Clear();
            var oldNodes = All<VentNode>(scene);
            var ceilingMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/PizzaDoggy/TexturesII/Materials/painted_concrete_wall_pt_5_plain.mat");
            foreach (var node in oldNodes)
            {
                if (node.room != null)
                {
                    var p = node.transform.position;
                    Box("FormerVentCeilingPatch", underground, new Vector3(p.x, 2.9f, p.z), new Vector3(.84f, .2f, .84f), ceilingMat, 1);
                    Box("FormerVentRoofPatch", underground, new Vector3(p.x, 3.04f, p.z), new Vector3(.84f, .08f, .84f), metal, 1);
                }

                node.gameObject.SetActive(false);
            }

            GameObject.Find("Motel_Layout_V4/04_Gameplay/Ventilation")?.SetActive(false);
            foreach (var c in All<GuestEvacuation>(scene))
                UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in All<MimicDebugMenu>(scene))
                c.enabled = false;
            // Fixed authored plan: two long sides, three cross corridors, six bed-side access branches.
            var southX = new[]{1.6f, 7.4f, 8.6f, 13.2f, 15.6f};
            var northX = new[]{1.6f, 4f, 8.6f, 9.8f, 15.6f};
            var south = southX.Select(x => Node("SouthJunction", x, 2)).ToArray();
            var north = northX.Select(x => Node("NorthJunction", x, 21.4f)).ToArray();
            for (int i = 1; i < south.Length; i++)
                Join(south[i - 1], south[i]);
            for (int i = 1; i < north.Length; i++)
                Join(north[i - 1], north[i]);
            var westHole = Node("WestHole", 1.6f, 8);
            var westMid = Node("WestCrossing", 1.6f, 11.7f);
            var eastMid = Node("EastCrossing", 15.6f, 11.7f);
            var eastHole = Node("EastHole", 15.6f, 15);
            Join(south[0], westHole);
            Join(westHole, westMid);
            Join(westMid, north[0]);
            Join(south[^1], eastMid);
            Join(eastMid, eastHole);
            Join(eastHole, north[^1]);
            Join(westMid, eastMid);
            foreach (var r in All<ServiceRoom>(scene))
            {
                float hx = r.transform.parent.parent.TransformPoint(new Vector3(1.6f, 0, 4.6f)).x;
                var row = int.Parse(r.RoomNumber) <= 103 ? south : north;
                Hatch(r, row.Single(n => Mathf.Abs(n.transform.position.x - hx) < .01f));
            }

            // These two room branches lie on a through-corridor: split the graph edge at the physical junction.
            foreach (var triplet in new[]{(south[0], westHole, authoredNodes.Single(n => n.room != null && n.room.RoomNumber == "103")), (north[^1], eastHole, authoredNodes.Single(n => n.room != null && n.room.RoomNumber == "106"))})
            {
                triplet.Item1.neighbours = triplet.Item1.neighbours.Where(n => n != triplet.Item2).ToArray();
                triplet.Item2.neighbours = triplet.Item2.neighbours.Where(n => n != triplet.Item1).ToArray();
                Join(triplet.Item3, triplet.Item2);
            }

            foreach (var n in new[]{south[2], north[2], westHole, eastHole})
                HiddenHole(n);
            Shell();
            foreach (var n in new[]{westMid, eastMid})
            {
                var t = Group("DimJunctionMarker", underground, n.transform.position + Vector3.up * .5f);
                var light = t.gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 2.3f;
                light.intensity = .06f;
                light.color = new Color(.24f, .55f, .55f);
                t.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>().renderingLayers = 2;
            }

            var hunt = All<MimicHunt>(scene).Single();
            var serialized = new SerializedObject(hunt);
            var oldVisual = serialized.FindProperty("creature").objectReferenceValue as GameObject;
            oldVisual.SetActive(false);
            var oldBody = serialized.FindProperty("body").objectReferenceValue as Collider;
            UnityEngine.Object.DestroyImmediate(oldBody);
            var visual = Group("UndergroundSphere", hunt.transform);
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.SetParent(visual, false);
            sphere.transform.localScale = Vector3.one * .6f;
            UnityEngine.Object.DestroyImmediate(sphere.GetComponent<Collider>());
            sphere.GetComponent<Renderer>().sharedMaterial = sphereMaterial;
            sphere.GetComponent<Renderer>().renderingLayerMask = 2;
            visual.gameObject.SetActive(false);
            var hitbox = hunt.gameObject.AddComponent<SphereCollider>();
            hitbox.radius = .3f;
            hitbox.enabled = false;
            Set(hunt, "body", hitbox);
            Set(hunt, "creature", visual.gameObject);
            serialized = new SerializedObject(hunt);
            var nodeRefs = serialized.FindProperty("nodes");
            nodeRefs.arraySize = authoredNodes.Count;
            for (int i = 0; i < authoredNodes.Count; i++)
                nodeRefs.GetArrayElementAtIndex(i).objectReferenceValue = authoredNodes[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            hunt.Decoy.gameObject.SetActive(false);
            foreach (var c in All<MimicPresentation>(scene))
                c.enabled = false;
            var surveillance = All<VentSurveillance>(scene).Single();
            for (int i = 0; i < 3; i++)
                surveillance.cameras[i].gameObject.SetActive(false);
            foreach (var no in All<NetworkObject>(scene))
                typeof(NetworkObject).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(no, null);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Validate();
        }
    }
}
