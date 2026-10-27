using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using Unity.Netcode;
using Unity.Netcode.Components;
using MotelGame.Horror;
using MotelGame.Service;

namespace MotelGame.Editor
{
    [InitializeOnLoad]
    public static class MimicHuntSetup
    {
        const string ScenePath = "Assets/Game/Scenes/Motel_Blockout_V3.unity", Reports = "Documentation/MimicHuntM14", AssetsRoot = "Assets/Game/Horror";
        static double next;
        static MimicHuntSetup() => EditorApplication.update += Poll;
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next || BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || File.Exists("Library/MotelServiceRefresh.request"))
                return;
            next = EditorApplication.timeSinceStartup + 1;
            try
            {
                if (File.Exists("Library/MotelMimicSetup.request"))
                {
                    File.Delete("Library/MotelMimicSetup.request");
                    Install();
                }

                if (File.Exists("Library/MotelMimicInspect.request"))
                {
                    File.Delete("Library/MotelMimicInspect.request");
                    Inspect();
                }

                if (File.Exists("Library/MotelMimicValidate.request"))
                {
                    File.Delete("Library/MotelMimicValidate.request");
                    Validate();
                }

                if (File.Exists("Library/MotelMimicBuild.request"))
                {
                    File.Delete("Library/MotelMimicBuild.request");
                    Validate();
                    PlaytestBuild.Build();
                }
            }
            catch (Exception e)
            {
                Directory.CreateDirectory(Reports);
                File.WriteAllText(Reports + "/editor-error.txt", e.ToString());
                File.Delete("Library/MotelMimicBuild.request");
                Debug.LogException(e);
            }
        }

        static void Set(UnityEngine.Object obj, string field, UnityEngine.Object value)
        {
            var s = new SerializedObject(obj);
            s.FindProperty(field).objectReferenceValue = value;
            s.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Items(UnityEngine.Object obj, string field, IEnumerable<UnityEngine.Object> values)
        {
            var s = new SerializedObject(obj);
            var p = s.FindProperty(field);
            var a = values.ToArray();
            p.arraySize = a.Length;
            for (int i = 0; i < a.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = a[i];
            s.ApplyModifiedPropertiesWithoutUndo();
        }

        static Transform Group(string name, Transform parent, Vector3 pos)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            return g.transform;
        }

        static Material Existing(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/MotelBlockoutV3/" + name + ".mat");
        static Material Surface(string name, Color color, bool glow = false)
        {
            string path = AssetsRoot + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null)
                return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {name = name};
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", .12f);
            if (glow)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * 3);
            }

            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static GameObject Shape(string name, Transform parent, Vector3 local, Vector3 size, Material mat, PrimitiveType type = PrimitiveType.Cube, bool solid = true)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = local;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            if (!solid)
                UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }

        static NetworkObject Net(GameObject g)
        {
            var n = g.AddComponent<NetworkObject>();
            n.AutoObjectParentSync = false;
            return n;
        }

        static AudioClip Audio(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>(AssetsRoot + "/Audio/" + name + ".wav");
        static TextMesh Label(Transform p, string text, Vector3 position, Vector3 direction, float size = .012f)
        {
            var t = Group("Label", p, position);
            t.rotation = Quaternion.LookRotation(direction);
            var m = t.gameObject.AddComponent<TextMesh>();
            m.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m.fontSize = 64;
            m.characterSize = size;
            m.anchor = TextAnchor.MiddleCenter;
            m.alignment = TextAlignment.Center;
            m.text = text;
            t.gameObject.AddComponent<WorldServiceText>().RefreshMaterial();
            return m;
        }

        [MenuItem("Tools/Motel/Mimic/Install M14")]
        public static void Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.isDirty)
                throw new Exception("Save scene changes before M14 installation.");
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath);
            var existing = UnityEngine.Object.FindFirstObjectByType<MimicHunt>();
            if (existing != null)
            {
                if (!File.Exists("Library/MotelMimicRebuild.request"))
                {
                    Validate();
                    return;
                }

                File.Delete("Library/MotelMimicRebuild.request");
                UnityEngine.Object.DestroyImmediate(existing.transform.parent.gameObject);
            }

            Directory.CreateDirectory(Reports);
            Directory.CreateDirectory(AssetsRoot);
            AssetDatabase.Refresh();
            string original = File.ReadAllText(ScenePath);
            var metal = Existing("Metal");
            var dark = Surface("DuctInterior", new Color(.075f, .09f, .085f));
            var flesh = Surface("MimicPaleSkin", new Color(.43f, .45f, .35f));
            var black = Surface("MimicMouth", new Color(.012f, .014f, .012f));
            var tooth = Surface("MimicTeeth", new Color(.76f, .7f, .48f));
            var glow = Surface("HeatGlow", new Color(1, .28f, .035f), true);
            var root = Group("MimicHunt_M14", null, Vector3.zero);
            var vents = Group("Ventilation", root, Vector3.zero);
            var huntRoot = Group("Mimic", root, Vector3.zero);
            Net(huntRoot.gameObject);
            var nt = huntRoot.gameObject.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            var hunt = huntRoot.gameObject.AddComponent<MimicHunt>();
            var shift = UnityEngine.Object.FindFirstObjectByType<NightShiftController>();
            Set(hunt, "shift", shift);
            var nodeList = new List<VentNode>();
            var rooms = shift.Reception.Rooms.OrderBy(r => r.RoomNumber).ToArray();
            for (int i = 0; i < 4; i++)
            {
                bool east = i % 2 == 1;
                float z = 15.25f + (i >= 2 ? 5.7f : 0);
                Vector3 pos = new(east ? 24.23f : 20.17f, 1.85f, z);
                var tr = Group("Vent_Room" + rooms[i].RoomNumber, vents, pos);
                tr.rotation = Quaternion.LookRotation(Vector3.back);
                var n = tr.gameObject.AddComponent<VentNode>();
                n.index = i;
                n.room = rooms[i];
                n.looseFan = i == 3;
                n.mouth = Group("Mouth", tr, pos + tr.forward * .78f);
                n.landing = Group("Landing", tr, new Vector3(east ? 24.4f : 20f, .03f, z - .95f));
                // Surface duct housing stays wholly inside the room; the concealed riser leads above its ceiling.
                Shape("Back", tr, new Vector3(0, .4f, -.22f), new Vector3(.8f, .95f, .06f), dark);
                Shape("Top", tr, new Vector3(0, .88f, .2f), new Vector3(.8f, .06f, .9f), metal);
                Shape("Bottom", tr, new Vector3(0, -.03f, .2f), new Vector3(.8f, .06f, .9f), metal);
                foreach (float x in new[]{-.4f, .4f})
                    Shape("Side", tr, new Vector3(x, .43f, .2f), new Vector3(.06f, .92f, .9f), metal);
                var hinge = Group("GrilleHinge", tr, tr.TransformPoint(new Vector3(0, .84f, .65f)));
                hinge.localRotation = Quaternion.identity;
                n.grille = hinge;
                for (int b = 0; b < 7; b++)
                    Shape("GrilleSlat", hinge, new Vector3(0, -.08f - b * .115f, 0), new Vector3(.75f, .027f, .045f), metal);
                var frame = Shape("InspectionFrame", tr, new Vector3(-.41f, .43f, .65f), new Vector3(.1f, .92f, .1f), metal);
                n.inspection = frame.GetComponent<Collider>();
                var light = Group("VentInspectionLight", tr, tr.TransformPoint(new Vector3(0, .64f, .36f))).gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 1.25f;
                light.intensity = .42f;
                light.color = new Color(.58f, .68f, .5f);
                light.shadows = LightShadows.None;
                n.sound = tr.gameObject.AddComponent<AudioSource>();
                n.sound.spatialBlend = 1;
                n.sound.minDistance = .8f;
                n.sound.maxDistance = 8;
                n.sound.rolloffMode = AudioRolloffMode.Linear;
                n.sound.playOnAwake = false;
                n.rattle = Audio("FanRattle");
                if (n.looseFan)
                {
                    n.fan = Group("LooseFanRotor", tr, tr.TransformPoint(new Vector3(0, .45f, -.1f)));
                    n.fan.localRotation = Quaternion.identity;
                    for (int b = 0; b < 3; b++)
                    {
                        var blade = Shape("FanBlade", n.fan, new Vector3(0, .1f, 0), new Vector3(.12f, .37f, .025f), metal, solid: false);
                        blade.transform.localRotation = Quaternion.Euler(0, 0, b * 120);
                    }
                }

                Label(tr, "VENT / " + rooms[i].RoomNumber, tr.TransformPoint(new Vector3(0, -.18f, .65f)), -tr.forward, .006f);
                nodeList.Add(n);
            }

            for (int i = 0; i < 4; i++)
            {
                var p = nodeList[i].transform.position;
                p.y = 3.3f;
                var n = Group("Riser_" + i, vents, p).gameObject.AddComponent<VentNode>();
                n.index = i + 4;
                nodeList.Add(n);
            }

            for (int j = 0; j < 2; j++)
            {
                var n = Group("Junction_" + (j + 1), vents, new Vector3(22.2f, 3.3f, 15.25f + j * 5.7f)).gameObject.AddComponent<VentNode>();
                n.index = 8 + j;
                nodeList.Add(n);
            }

            for (int i = 0; i < 4; i++)
            {
                nodeList[i].neighbours = new[]{nodeList[i + 4]};
                nodeList[i + 4].neighbours = new[]{nodeList[i], nodeList[i < 2 ? 8 : 9]};
            }

            nodeList[8].neighbours = new[]{nodeList[4], nodeList[5], nodeList[9]};
            nodeList[9].neighbours = new[]{nodeList[6], nodeList[7], nodeList[8]};
            Items(hunt, "nodes", nodeList);
            var duct = Group("ConcealedJunctionDucts", vents, Vector3.zero);
            Shape("MainFloor", duct, new Vector3(22.2f, 3.22f, 18.1f), new Vector3(1.3f, .08f, 9), dark);
            Shape("MainCeiling", duct, new Vector3(22.2f, 4.23f, 18.1f), new Vector3(1.3f, .08f, 9), dark);
            foreach (float x in new[]{21.55f, 22.85f})
                foreach (var segment in new[]{(14.13f, 1.06f), (18.1f, 4.55f), (22.05f, 1.1f)})
                    Shape("MainSide", duct, new Vector3(x, 3.72f, segment.Item1), new Vector3(.08f, 1, segment.Item2), dark);
            for (int j = 0; j < 2; j++)
            {
                float z = 15.25f + j * 5.7f;
                Shape("BranchFloor", duct, new Vector3(22.2f, 3.22f, z), new Vector3(4.9f, .08f, 1.1f), dark);
                Shape("BranchRoof", duct, new Vector3(22.2f, 4.23f, z), new Vector3(4.9f, .08f, 1.1f), dark);
                foreach (float side in new[]{-.55f, .55f})
                    foreach (float x in new[]{20.85f, 23.55f})
                        Shape("BranchSide", duct, new Vector3(x, 3.72f, z + side), new Vector3(1.45f, 1, .08f), dark);
            }

            var surveillance = root.gameObject.AddComponent<VentSurveillance>();
            surveillance.computer = UnityEngine.Object.FindFirstObjectByType<ReceptionComputer>();
            surveillance.cameras = new Camera[2];
            for (int j = 0; j < 2; j++)
            {
                var cam = Group("VentCamera_" + (j + 1), vents, new Vector3(22.2f, 3.75f, j == 0 ? 13.75f : 22.45f));
                cam.LookAt(new Vector3(22.2f, 3.6f, j == 0 ? 16.7f : 19.7f));
                var camera = cam.gameObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.fieldOfView = 72;
                camera.nearClipPlane = .04f;
                camera.farClipPlane = 12;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.allowHDR = false;
                cam.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
                surveillance.cameras[j] = camera;
                var l = cam.gameObject.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 5;
                l.intensity = 1.5f;
                l.color = new Color(.5f, .7f, .58f);
                l.shadows = LightShadows.Soft;
            }

            var visual = Group("CreatureVisual", huntRoot, Vector3.zero);
            var body = Group("Pose", visual, Vector3.zero);
            Shape("Spine", body, new Vector3(0, .52f, 0), new Vector3(.3f, .7f, .48f), flesh, PrimitiveType.Capsule, false);
            Shape("Skull", body, new Vector3(0, .9f, .28f), new Vector3(.36f, .42f, .48f), flesh, PrimitiveType.Sphere, false);
            Shape("FaceCavity", body, new Vector3(0, .84f, .49f), new Vector3(.26f, .24f, .09f), black, PrimitiveType.Sphere, false);
            foreach (float x in new[]{-.11f, .11f})
                Shape("EyeHollow", body, new Vector3(x, 1, .48f), new Vector3(.11f, .09f, .06f), black, PrimitiveType.Sphere, false);
            var jaw = Group("Jaw", body, new Vector3(0, .72f, .34f));
            Shape("LowerJaw", jaw, new Vector3(0, 0, .12f), new Vector3(.3f, .07f, .28f), flesh, PrimitiveType.Cube, false);
            for (int k = 0; k < 7; k++)
            {
                float x = -.11f + k * .037f;
                Shape("UpperTooth", body, new Vector3(x, .87f, .54f), new Vector3(.018f, .075f, .018f), tooth, solid: false);
                Shape("LowerTooth", jaw, new Vector3(x, .06f, .23f), new Vector3(.018f, .09f, .018f), tooth, solid: false);
            }

            var limbs = new List<Transform>();
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1 : 1;
                var limb = Group("HookedLimb" + i, body, new Vector3(side * .18f, i < 2 ? .65f : .3f, i < 2 ? .12f : -.22f));
                limb.localRotation = Quaternion.Euler(i < 2 ? 15 : -35, 0, side * 40);
                Shape("LongBone", limb, new Vector3(side * .14f, -.2f, 0), new Vector3(.09f, .3f, .11f), flesh, PrimitiveType.Capsule, false);
                Shape("Hook", limb, new Vector3(side * .25f, -.42f, .13f), new Vector3(.08f, .2f, .22f), flesh, PrimitiveType.Capsule, false);
                limbs.Add(limb);
            }

            var capsule = huntRoot.gameObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0, .52f, .05f);
            capsule.height = 1.1f;
            capsule.radius = .3f;
            capsule.enabled = false;
            Set(hunt, "body", capsule);
            Set(hunt, "creature", visual.gameObject);
            foreach (var pair in new[]{("scratch", "Scratch"), ("warning", "Warning"), ("attack", "Attack"), ("pain", "Hit"), ("death", "Death")})
                Set(hunt, pair.Item1, Audio(pair.Item2));
            var presentation = visual.gameObject.AddComponent<MimicPresentation>();
            presentation.hunt = hunt;
            presentation.body = body;
            presentation.jaw = jaw;
            presentation.limbs = limbs.ToArray();
            visual.gameObject.SetActive(false);
            var rack = Group("HuntEquipmentStation", root, new Vector3(18.5f, 0, 4.9f));
            Shape("EquipmentShelf", rack, new Vector3(0, .88f, 0), new Vector3(.7f, .08f, .7f), metal);
            Label(rack, "SECURITY EQUIPMENT\nE TAKE / G PLACE", new Vector3(18.5f, 1.55f, 4.9f), Vector3.right, .007f);
            var heat = Equipment(root, hunt, ServiceItem.ThermalDecoy, new Vector3(18.5f, .94f, 4.75f), metal, glow);
            var gun = Equipment(root, hunt, ServiceItem.Firearm, new Vector3(18.5f, .94f, 5.15f), metal, glow);
            Set(hunt, "decoy", heat);
            Set(hunt, "firearm", gun);
            var debug = root.gameObject.AddComponent<MimicDebugMenu>();
            debug.hunt = hunt;
            var guests = shift.Reception.Guests;
            for (int i = 0; i < guests.Length; i++)
            {
                if (guests[i].GetComponent<GuestEvacuation>() == null)
                    guests[i].gameObject.AddComponent<GuestEvacuation>();
                var safe = Group("EvacuationWait_" + i, root, new Vector3(4.3f + i * .85f, 0, 8.7f));
                Set(guests[i], "evacuationPoint", safe);
                PrefabUtility.RecordPrefabInstancePropertyModifications(guests[i]);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            // Preserve existing render records, including the user's pre-M14 material edit.
            const string pattern = @"(?ms)^--- !u!(?:23|137) &\d+\n.*?(?=^--- !u!|\z)";
            var old = Regex.Matches(original, pattern).Cast<Match>().ToDictionary(m => m.Value.Split('\n')[0], m => m.Value);
            string updated = Regex.Replace(File.ReadAllText(ScenePath), pattern, m => old.TryGetValue(m.Value.Split('\n')[0], out var v) ? v : m.Value);
            // Unity also fills empty TextMesh material overrides on existing prefab instances.
            const string prefabPattern = @"(?ms)^--- !u!1001 &\d+\n.*?(?=^--- !u!|\z)";
            const string materialPattern = @"(?m)^    - target:.*\n      propertyPath: '?m_Materials[^\n]*\n      value:[^\n]*\n      objectReference:[^\n]*\n";
            var prefabs = Regex.Matches(original, prefabPattern).Cast<Match>().ToDictionary(m => m.Value.Split('\n')[0], m => m.Value);
            updated = Regex.Replace(updated, prefabPattern, prefab =>
            {
                if (!prefabs.TryGetValue(prefab.Value.Split('\n')[0], out var previous)) return prefab.Value;
                var materials = Regex.Matches(previous, materialPattern).Cast<Match>().ToDictionary(m => string.Join("\n", m.Value.Split('\n').Take(2)), m => m.Value);
                return Regex.Replace(prefab.Value, materialPattern, m => materials.TryGetValue(string.Join("\n", m.Value.Split('\n').Take(2)), out var saved) ? saved : m.Value);
            });
            File.WriteAllText(ScenePath, updated);
            Validate();
        }

        static HuntEquipment Equipment(Transform root, MimicHunt hunt, ServiceItem item, Vector3 pos, Material metal, Material glow)
        {
            var home = Group(item + "Home", root, pos);
            var go = Group(item.ToString(), root, Vector3.zero);
            Net(go.gameObject);
            var e = go.gameObject.AddComponent<HuntEquipment>();
            var s = new SerializedObject(e);
            s.FindProperty("item").enumValueIndex = (int)item;
            s.ApplyModifiedPropertiesWithoutUndo();
            Set(e, "home", home);
            var visual = Group("Visual", go, pos);
            Set(e, "visual", visual);
            var box = visual.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0, .12f, 0);
            box.size = item == ServiceItem.Firearm ? new Vector3(.16f, .25f, .43f) : new Vector3(.32f, .3f, .3f);
            Set(e, "pickup", box);
            if (item == ServiceItem.ThermalDecoy)
            {
                Shape("Case", visual, new Vector3(0, .14f, 0), new Vector3(.28f, .26f, .26f), metal, solid: false);
                for (int i = 0; i < 5; i++)
                    Shape("HeatingFin", visual, new Vector3(-.1f + i * .05f, .16f, .14f), new Vector3(.025f, .2f, .025f), glow, solid: false);
                Shape("Handle", visual, new Vector3(0, .32f, 0), new Vector3(.22f, .035f, .04f), metal, solid: false);
            }
            else
            {
                Shape("PistolSlide", visual, new Vector3(0, .17f, .08f), new Vector3(.1f, .1f, .33f), metal, solid: false);
                var grip = Shape("Grip", visual, new Vector3(0, .06f, -.04f), new Vector3(.085f, .2f, .09f), Existing("Rubber"), solid: false);
                grip.transform.localRotation = Quaternion.Euler(-15, 0, 0);
                var muzzle = Group("Muzzle", visual, pos + new Vector3(0, .17f, .27f));
                var flash = Shape("MuzzleFlash", muzzle, Vector3.zero, new Vector3(.14f, .14f, .22f), glow, PrimitiveType.Sphere, false);
                flash.SetActive(false);
                Set(e, "flash", flash);
                Set(e, "gunshot", Audio("Gunshot"));
                Set(e, "impact", Audio("Hit"));
                Set(e, "impactMaterial", glow);
            }

            return e;
        }

        static void Inspect()
        {
            var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var surface = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Unity.AI.Navigation.NavMeshSurface>(true)).Single();
                surface.AddData();
                var lines = new List<string>();
                for (float x = 18.5f; x <= 20.5f; x += .25f)
                    for (float z = 13.5f; z <= 14.75f; z += .25f)
                        if (NavMesh.SamplePosition(new Vector3(x, .03f, z), out var h, .25f, NavMesh.AllAreas))
                            lines.Add(x + "," + z + " -> " + h.position);
                foreach (var room in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ServiceRoom>(true)))
                {
                    bool ok = NavMesh.SamplePosition(room.GuestIdle.position, out var h, .3f, NavMesh.AllAreas);
                    lines.Add("IDLE " + room.RoomNumber + " " + room.GuestIdle.position + " " + ok + " " + h.position);
                }

                File.WriteAllLines(Reports + "/placement-inspection.txt", lines);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [MenuItem("Tools/Motel/Mimic/Validate M14")]
        public static void Validate()
        {
            Directory.CreateDirectory(Reports);
            var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                T[] All<T>()
                    where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
                var hunt = All<MimicHunt>().Single();
                if (!hunt.ConfigurationValid())
                    throw new Exception("Invalid hunt graph/configuration.");
                if (All<HuntEquipment>().Length != 2 || All<HuntEquipment>().Any(e => !e.ConfigurationValid()))
                    throw new Exception("Equipment configuration.");
                if (All<GuestEvacuation>().Length != 5 || All<VentSurveillance>().Single().cameras.Length != 2)
                    throw new Exception("Guest/CCTV configuration.");
                foreach (var t in All<Transform>())
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                        throw new Exception("Missing script: " + t.name);
                var surface = All<Unity.AI.Navigation.NavMeshSurface>().Single();
                surface.AddData();
                var lines = new List<string>();
                foreach (var node in hunt.Nodes.Where(n => n.room != null))
                {
                    if (!NavMesh.SamplePosition(node.landing.position, out var sampled, .3f, NavMesh.AllAreas))
                    {
                        NavMesh.SamplePosition(node.landing.position, out var closest, 3, NavMesh.AllAreas);
                        throw new Exception("Vent landing off NavMesh: " + node.name + " requested=" + node.landing.position + " closest=" + closest.position);
                    }

                    var path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(node.room.GuestIdle.position, node.landing.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                        throw new Exception("No Mimic approach route: " + node.name);
                    lines.Add(node.name + ": landing on NavMesh, approach complete; graph distance to 101=" + hunt.GraphDistance(node.index, 0));
                }

                foreach (var g in All<GuestLifecycle>())
                {
                    var path = new NavMeshPath();
                    if (g.EvacuationPoint == null || !NavMesh.CalculatePath(g.ReceptionPoint.position, g.EvacuationPoint.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                        throw new Exception("Invalid evacuation route: " + g.name);
                }

                var ids = All<NetworkObject>().Select(n => new SerializedObject(n).FindProperty("GlobalObjectIdHash").uintValue).ToArray();
                if (ids.Any(i => i == 0) || ids.Distinct().Count() != ids.Length)
                    throw new Exception("Invalid NGO identities.");
                File.WriteAllLines(Reports + "/scene-validation.txt", new[]{"PASS: saved scene reopened; no missing scripts; 10 connected vent nodes, 4 room vents, 2 cameras, 1 Mimic, 1 decoy, 1 pistol; 5 evacuation paths; unique NGO scene identities."}.Concat(lines));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
