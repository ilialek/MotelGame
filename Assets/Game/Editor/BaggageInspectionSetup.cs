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
using Unity.Netcode;
using MotelGame.Horror;
using MotelGame.Service;
using MotelGame.Inspection;

namespace MotelGame.Editor
{
    [InitializeOnLoad]
    public static class BaggageInspectionSetup
    {
        const string ScenePath = "Assets/Game/Scenes/Motel_Blockout_V3.unity", Reports = "Documentation/BaggageInspectionM15", AssetRoot = "Assets/Game/Inspection";
        static double next;
        static BaggageInspectionSetup() => EditorApplication.update += Poll;
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < next || BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || File.Exists("Library/MotelServiceRefresh.request"))
                return;
            next = EditorApplication.timeSinceStartup + 1;
            try
            {
                foreach (string action in new[]{"Inspect", "Setup", "Repair", "Validate", "Render", "Build"})
                {
                    string request = "Library/MotelBaggage" + action + ".request";
                    if (!File.Exists(request))
                        continue;
                    File.Delete(request);
                    if (action == "Inspect")
                        Inspect();
                    else if (action == "Setup")
                        Install();
                    else if (action == "Repair")
                        RepairBaggagePlacement();
                    else if (action == "Validate")
                        Validate();
                    else if (action == "Render")
                        Render();
                    else
                    {
                        Validate();
                        PlaytestBuild.Build();
                    }
                }
            }
            catch (Exception e)
            {
                Directory.CreateDirectory(Reports);
                File.WriteAllText(Reports + "/editor-error.txt", e.ToString());
                File.Delete("Library/MotelBaggageBuild.request");
                Debug.LogException(e);
            }
        }

        static T[] All<T>(Scene s)
            where T : Component => s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
        static Transform Group(string name, Transform parent, Vector3 local)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = local;
            return g.transform;
        }

        static void Float(UnityEngine.Object o, string field, float value)
        {
            var s = new SerializedObject(o);
            s.FindProperty(field).floatValue = value;
            s.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Items(UnityEngine.Object o, string field, IEnumerable<UnityEngine.Object> values)
        {
            var s = new SerializedObject(o);
            var p = s.FindProperty(field);
            var a = values.ToArray();
            p.arraySize = a.Length;
            for (int i = 0; i < a.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = a[i];
            s.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject Shape(string name, Transform parent, Vector3 local, Vector3 size, Material mat, bool solid = true, PrimitiveType type = PrimitiveType.Cube)
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

        static Material Surface(string name, Color color, bool glow = false)
        {
            string path = AssetRoot + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null)
                return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {name = name};
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", .16f);
            if (glow)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * 2);
            }

            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static TextMesh Label(Transform parent, string name, string text, Vector3 local, float size)
        {
            var t = Group(name, parent, local);
            t.localRotation = Quaternion.identity;
            var m = t.gameObject.AddComponent<TextMesh>();
            m.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m.fontSize = 64;
            m.characterSize = size;
            m.anchor = TextAnchor.MiddleCenter;
            m.alignment = TextAlignment.Center;
            m.text = text;
            SetTextShader(t.gameObject.AddComponent<WorldServiceText>());
            return m;
        }

        static void SetTextShader(WorldServiceText text)
        {
            var o = new SerializedObject(text);
            o.FindProperty("textShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Game/Shaders/WorldServiceText.shader");
            o.ApplyModifiedPropertiesWithoutUndo();
            text.RefreshMaterial();
        }

        [Serializable]
        sealed class CaseSource
        {
            public CaseRow[] cases;
        }

        [Serializable]
        sealed class CaseRow
        {
            public string name;
            public BagShell shell;
            public Color color;
            public bool containsMimic;
            public BaggagePart[] contents;
            public string contentsAnswer, weightAnswer, metalAnswer, packingAnswer, responseAnswer, authorReasoning;
        }

        static BaggageCase[] CreateCases()
        {
            var source = JsonUtility.FromJson<CaseSource>(File.ReadAllText(AssetRoot + "/Editor/Cases.json"));
            var result = new List<BaggageCase>();
            foreach (var row in source.cases)
            {
                string path = AssetRoot + "/Cases/" + row.name + ".asset";
                var c = AssetDatabase.LoadAssetAtPath<BaggageCase>(path);
                if (c == null)
                {
                    c = ScriptableObject.CreateInstance<BaggageCase>();
                    AssetDatabase.CreateAsset(c, path);
                }

                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(row), c);
                c.name = row.name;
                if (!c.Valid(out var error))
                    throw new Exception(row.name + ": " + error);
                EditorUtility.SetDirty(c);
                result.Add(c);
            }

            return result.ToArray();
        }

        [MenuItem("Tools/Motel/Baggage/Install M15")]
        public static void Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.isDirty)
                throw new Exception("Save scene changes before M15 installation.");
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath);
            if (All<BaggageInspection>(scene).Length != 0)
            {
                Validate();
                return;
            }

            Directory.CreateDirectory(Reports);
            Directory.CreateDirectory(AssetRoot + "/Cases");
            AssetDatabase.Refresh();
            string original = File.ReadAllText(ScenePath);
            var oldColliders = All<Collider>(scene).Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && c.GetComponentInParent<GuestLifecycle>() == null).ToArray();
            var root = Group("BaggageInspection_M15", null, Vector3.zero);
            var station = Group("ReceptionLoadAnalyzer", root, new Vector3(9.1f, 0, 4.05f));
            var obstacle = station.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = new Vector3(0, .45f, 0);
            obstacle.size = new Vector3(1.25f, .9f, .82f);
            obstacle.carving = true;
            var network = station.gameObject.AddComponent<NetworkObject>();
            network.AutoObjectParentSync = false;
            var inspection = station.gameObject.AddComponent<BaggageInspection>();
            station.gameObject.AddComponent<AnalyzerPanel>();
            inspection.Reception = All<ReceptionDesk>(scene).Single();
            inspection.Computer = All<ReceptionComputer>(scene).Single();
            inspection.Shift = All<NightShiftController>(scene).Single();
            inspection.Hunt = All<MimicHunt>(scene).Single();
            var scenario = root.gameObject.AddComponent<HuntScenario>();
            scenario.inspection = inspection;
            scenario.hunt = inspection.Hunt;
            var authoredCases = CreateCases();
            Items(inspection, "cases", authoredCases);
            ConfigureScenarioCases(inspection, authoredCases);
            var body = Surface("AnalyzerEnamel", new Color(.20f, .24f, .20f));
            var metal = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/MotelBlockoutV3/Metal.mat");
            var rubber = Surface("BaggageRubber", new Color(.045f, .04f, .035f));
            var cloth = Surface("BaggageFabric", new Color(.26f, .30f, .27f));
            var display = Surface("AnalyzerPhosphor", new Color(.11f, .25f, .13f), true);
            Shape("ScaleCabinet", station, new Vector3(0, .4f, 0), new Vector3(1.15f, .8f, .72f), body);
            for (int i = 0; i < 4; i++)
                Shape("CabinetFoot" + i, station, new Vector3(i % 2 == 0 ? -.46f : .46f, .06f, i < 2 ? -.26f : .26f), new Vector3(.12f, .12f, .12f), rubber);
            inspection.Tray = Group("TiltingWeighTray", station, new Vector3(-.18f, .88f, -.02f));
            Shape("SteelPlatform", inspection.Tray, Vector3.zero, new Vector3(.78f, .07f, .62f), metal);
            inspection.Platform = Group("BaggageRest", inspection.Tray, new Vector3(0, .035f, 0));
            Shape("Actuator", station, new Vector3(-.18f, .78f, -.26f), new Vector3(.18f, .16f, .25f), rubber);
            Shape("CRTCase", station, new Vector3(.33f, 1.21f, .18f), new Vector3(.48f, .6f, .42f), body);
            var screen = Shape("AnalyzerControls", station, new Vector3(.33f, 1.22f, -.039f), new Vector3(.42f, .49f, .025f), display);
            inspection.Controls = screen.GetComponent<Collider>();
            inspection.WorldReadout = Label(station, "InstrumentReadout", "MOTEL / LOAD ANALYZER\nPLACE BAG ON PLATFORM\n[E] USE", new Vector3(.33f, 1.22f, -.057f), .0065f);
            Label(station, "EquipmentPlate", "LOAD LAB / 1978\nWEIGH · TILT · PULSE\n[E] OPERATE", new Vector3(0, .54f, -.367f), .0065f);
            for (int i = 0; i < 3; i++)
                Shape("Switch" + i, station, new Vector3(.22f + i * .11f, .925f, -.24f), new Vector3(.06f, .05f, .06f), rubber, false);
            inspection.Mechanism = station.gameObject.AddComponent<AudioSource>();
            inspection.Mechanism.spatialBlend = 1;
            inspection.Mechanism.minDistance = 1;
            inspection.Mechanism.maxDistance = 7;
            inspection.Mechanism.rolloffMode = AudioRolloffMode.Linear;
            inspection.Mechanism.playOnAwake = false;
            inspection.MotorSound = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetRoot + "/Audio/Motor.wav");
            inspection.DryResponse = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetRoot + "/Audio/Dry.wav");
            inspection.MetallicResponse = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetRoot + "/Audio/Metal.wav");
            var points = new List<Transform>();
            for (int i = 0; i < 4; i++)
            {
                // Clear floor beside the guest, outside the door sweep; route continues to that room's vent.
                var room = inspection.Reception.Rooms[i];
                var p = room.GuestIdle.position + new Vector3(i % 2 == 0 ? -.25f : .25f, .03f, .7f);
                points.Add(Group("Room" + room.RoomNumber + "Baggage", root, p));
            }

            inspection.RoomBagSpots = points.ToArray();
            foreach (var guest in inspection.Reception.Guests)
            {
                var bag = guest.gameObject.AddComponent<GuestBaggage>();
                bag.visual = Group("GuestBaggage", guest.transform, Vector3.zero);
                var handle = bag.visual.gameObject.AddComponent<BoxCollider>();
                handle.center = new Vector3(0, .23f, 0);
                handle.size = new Vector3(.55f, .48f, .34f);
                handle.enabled = false;
                bag.handle = handle;
                bag.visual.gameObject.AddComponent<BaggageHandle>().baggage = bag;
                bag.shells = new GameObject[4];
                bag.lids = new Transform[4];
                var renderers = new List<Renderer>();
                for (int shell = 0; shell < 4; shell++)
                {
                    var t = Group(((BagShell)shell).ToString(), bag.visual, Vector3.zero);
                    bag.shells[shell] = t.gameObject;
                    Vector3 size = shell switch
                    {
                        1 => new Vector3(.55f, .31f, .33f),
                        2 => new Vector3(.36f, .48f, .26f),
                        3 => new Vector3(.49f, .32f, .35f),
                        _ => new Vector3(.53f, .43f, .29f)};
                    var baseShape = Shape("ClosedShell", t, new Vector3(0, size.y * .47f, 0), new Vector3(size.x, size.y * .9f, size.z), cloth, false, shell == 1 ? PrimitiveType.Sphere : PrimitiveType.Cube);
                    renderers.Add(baseShape.GetComponent<Renderer>());
                    var lid = Group("LidHinge", t, new Vector3(0, size.y * .9f, size.z * .5f));
                    bag.lids[shell] = lid;
                    var top = Shape("Lid", lid, new Vector3(0, size.y * .05f, -size.z * .5f), new Vector3(size.x, size.y * .1f, size.z), cloth, false);
                    renderers.Add(top.GetComponent<Renderer>());
                    Shape("CarryHandle", lid, new Vector3(0, size.y * .16f, -size.z * .5f), new Vector3(.17f, .035f, .045f), rubber, false);
                    for (int strap = 0; strap < 2; strap++)
                        Shape("Strap" + strap, t, new Vector3((strap == 0 ? -1 : 1) * size.x * .3f, size.y * .47f, -size.z * .51f), new Vector3(.025f, size.y * .88f, .014f), rubber, false);
                }

                bag.caseRenderers = renderers.ToArray();
                bag.identity = Label(bag.visual, "BagTag", guest.GuestName, new Vector3(0, .22f, -.184f), .0035f);
                bag.visual.gameObject.SetActive(false);
                Float(guest, "stayDuration", 300);
                PrefabUtility.RecordPrefabInstancePropertyModifications(guest);
            }

            // Serialized M14 values do not inherit new C# initializer defaults.
            Float(inspection.Hunt, "warningSeconds", 12);
            Float(inspection.Hunt, "exposedSeconds", 14);
            Float(inspection.Hunt, "explorationSeconds", 85);
            Float(inspection.Hunt, "acquisitionSeconds", 8);
            Float(inspection.Hunt, "emergenceSeconds", 5);
            Physics.SyncTransforms();
            foreach (var c in station.GetComponentsInChildren<Collider>())
                foreach (var o in oldColliders)
                    if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, o, o.transform.position, o.transform.rotation, out _, out var depth) && depth > .005f)
                        throw new Exception("Analyzer intersects existing geometry: " + c.name + " / " + o.name + " depth=" + depth);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            PreserveOriginalRenderRecords(original);
            Validate();
        }

        static void ConfigureScenarioCases(BaggageInspection inspection, BaggageCase[] cases)
        {
            // This is the initial authored content selection. Runtime scenarios use Inspector references.
            Items(inspection, "inspectionPracticeCases", new[]{0, 4, 8, 11, 2, 6, 9, 1, 5, 12, 3, 10, 7, 13}.Select(i => cases[i]));
            Items(inspection, "completeScenarioCases", new[]{0, 4, 11, 7}.Select(i => cases[i]));
            Items(inspection, "huntOnlyCases", new[]{0, 1, 2}.Select(i => cases[i]));
        }

        static void RepairBaggagePlacement()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.isDirty)
                throw new Exception("Save scene before M15 placement adjustment.");
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath);
            string original = File.ReadAllText(ScenePath);
            var inspection = All<BaggageInspection>(scene).Single();
            if (new SerializedObject(inspection).FindProperty("completeScenarioCases").arraySize == 0)
                ConfigureScenarioCases(inspection, AssetDatabase.FindAssets("t:BaggageCase", new[]{AssetRoot + "/Cases"}).Select(id => AssetDatabase.LoadAssetAtPath<BaggageCase>(AssetDatabase.GUIDToAssetPath(id))).OrderBy(c => c.name).ToArray());
            for (int i = 0; i < 4; i++)
                inspection.RoomBagSpots[i].position = inspection.Reception.Rooms[i].GuestIdle.position + new Vector3(i % 2 == 0 ? -.25f : .25f, .03f, .7f);
            foreach (var text in inspection.GetComponentsInChildren<WorldServiceText>(true).Concat(All<GuestBaggage>(scene).SelectMany(b => b.GetComponentsInChildren<WorldServiceText>(true).Where(t => t.transform.IsChildOf(b.visual)))))
            {
                text.transform.localRotation = Quaternion.identity;
                SetTextShader(text);
            }

            inspection.WorldReadout.characterSize = .0065f;
            inspection.WorldReadout.color = new Color(.78f, .9f, .7f);
            Physics.SyncTransforms();
            var probe = new GameObject("BaggagePlacementProbe").AddComponent<BoxCollider>();
            probe.size = new Vector3(.55f, .48f, .34f);
            probe.center = Vector3.up * .24f;
            try
            {
                foreach (var spot in inspection.RoomBagSpots)
                {
                    probe.transform.position = spot.position;
                    foreach (var c in All<Collider>(scene).Where(c => c != probe && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger))
                        if (Physics.ComputePenetration(probe, probe.transform.position, Quaternion.identity, c, c.transform.position, c.transform.rotation, out _, out float depth) && depth > .005f)
                            throw new Exception("Room bag intersects " + c.name + " at " + spot.name + " depth " + depth);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe.gameObject);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            PreserveOriginalRenderRecords(original);
            Validate();
        }

        static void PreserveOriginalRenderRecords(string original)
        {
            const string pattern = @"(?ms)^--- !u!(?:23|137) &\d+\n.*?(?=^--- !u!|\z)";
            var old = Regex.Matches(original, pattern).Cast<Match>().ToDictionary(m => m.Value.Split('\n')[0], m => m.Value);
            string updated = Regex.Replace(File.ReadAllText(ScenePath), pattern, m => old.TryGetValue(m.Value.Split('\n')[0], out var v) ? v : m.Value);
            const string prefabPattern = @"(?ms)^--- !u!1001 &\d+\n.*?(?=^--- !u!|\z)";
            const string materialPattern = @"(?m)^    - target:.*\n      propertyPath: '?m_Materials[^\n]*\n      value:[^\n]*\n      objectReference:[^\n]*\n";
            var prefabs = Regex.Matches(original, prefabPattern).Cast<Match>().ToDictionary(m => m.Value.Split('\n')[0], m => m.Value);
            updated = Regex.Replace(updated, prefabPattern, prefab =>
            {
                if (!prefabs.TryGetValue(prefab.Value.Split('\n')[0], out var previous))
                    return prefab.Value;
                var materials = Regex.Matches(previous, materialPattern).Cast<Match>().ToDictionary(m => string.Join("\n", m.Value.Split('\n').Take(2)), m => m.Value);
                return Regex.Replace(prefab.Value, materialPattern, m => materials.TryGetValue(string.Join("\n", m.Value.Split('\n').Take(2)), out var saved) ? saved : m.Value);
            });
            // URP may add default light-data components while previewing old lights. Do not persist those unrelated changes.
            var existingIds = Regex.Matches(original, @"(?m)^--- !u!\d+ &(\d+)").Cast<Match>().Select(m => m.Groups[1].Value).ToHashSet();
            foreach (var m in Regex.Matches(updated, @"(?ms)^--- !u!114 &(\d+)\n.*?(?=^--- !u!|\z)").Cast<Match>().ToArray())
            {
                var owner = Regex.Match(m.Value, @"m_GameObject: \{fileID: (\d+)\}").Groups[1].Value;
                if (existingIds.Contains(m.Groups[1].Value) || !existingIds.Contains(owner) || !m.Value.Contains("UnityEngine.Rendering.Universal.UniversalAdditionalLightData"))
                    continue;
                updated = updated.Replace(m.Value, "").Replace("  - component: {fileID: " + m.Groups[1].Value + "}\n", "");
            }

            File.WriteAllText(ScenePath, updated);
        }

        static void Inspect()
        {
            Directory.CreateDirectory(Reports);
            var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                All<Unity.AI.Navigation.NavMeshSurface>(scene).Single().AddData();
                var lines = new List<string>();
                foreach (var c in All<Collider>(scene).Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && c.bounds.Intersects(new Bounds(new Vector3(9, 1, 4), new Vector3(4, 1.9f, 4)))))
                    lines.Add(c.name + " " + c.bounds);
                foreach (var g in All<GuestLifecycle>(scene))
                    lines.Add(g.name + " queue " + g.ReceptionPoint.position);
                foreach (var r in All<ServiceRoom>(scene))
                    lines.Add("Room " + r.RoomNumber + " idle " + r.GuestIdle.position);
                File.WriteAllLines(Reports + "/placement-inspection.txt", lines);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void Render()
        {
            var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                foreach (var t in All<Transform>(scene))
                    t.gameObject.layer = 30;
                foreach (var light in All<Light>(scene))
                    light.cullingMask = 1 << 30;
                foreach (var text in All<WorldServiceText>(scene))
                    text.RefreshMaterial();
                foreach (var c in All<Camera>(scene))
                    c.enabled = false;
                var camera = All<Camera>(scene).First();
                camera.scene = scene;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.cullingMask = 1 << 30;
                camera.fieldOfView = 65;
                camera.nearClipPlane = .03f;
                var views = new[]{("analyzer", new Vector3(8.1f, 1.65f, 2.4f), new Vector3(9.1f, 1, 4.05f)), ("lobby", new Vector3(6.7f, 1.65f, 2), new Vector3(9.4f, 1, 5.5f))};
                foreach (var v in views)
                {
                    camera.transform.position = v.Item2;
                    camera.transform.LookAt(v.Item3);
                    var rt = new RenderTexture(1200, 800, 24);
                    rt.Create();
                    var old = RenderTexture.active;
                    camera.targetTexture = rt;
                    try
                    {
                        camera.Render();
                        RenderTexture.active = rt;
                        var image = new Texture2D(1200, 800, TextureFormat.RGB24, false);
                        image.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0);
                        image.Apply();
                        File.WriteAllBytes(Reports + "/preview-" + v.Item1 + ".png", image.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(image);
                    }
                    finally
                    {
                        camera.targetTexture = null;
                        RenderTexture.active = old;
                        rt.Release();
                        UnityEngine.Object.DestroyImmediate(rt);
                    }
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [MenuItem("Tools/Motel/Baggage/Validate M15")]
        public static void Validate()
        {
            Directory.CreateDirectory(Reports);
            var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var scanner = All<BaggageInspection>(scene).Single();
                if (!scanner.ConfigurationValid())
                    throw new Exception("Invalid analyzer references/cases.");
                if (All<GuestBaggage>(scene).Length != 5 || All<HuntScenario>(scene).Length != 1)
                    throw new Exception("Missing/duplicate baggage or scenario components.");
                foreach (var t in All<Transform>(scene))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                        throw new Exception("Missing script " + t.name);
                foreach (var b in All<GuestBaggage>(scene))
                    if (b.visual == null || b.shells.Length != 4 || b.lids.Any(l => l == null) || b.handle == null || b.identity == null)
                        throw new Exception("Baggage references " + b.name);
                All<Unity.AI.Navigation.NavMeshSurface>(scene).Single().AddData();
                var lines = new List<string>();
                for (int i = 0; i < 4; i++)
                {
                    var pos = scanner.RoomBagSpots[i].position;
                    var vent = scanner.Hunt.Nodes.First(n => n.room == scanner.Reception.Rooms[i]);
                    var path = new NavMeshPath();
                    if (!NavMesh.SamplePosition(pos, out var hit, .2f, NavMesh.AllAreas) || !NavMesh.CalculatePath(hit.position, vent.landing.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                        throw new Exception("Baggage/vent path " + i);
                    lines.Add("Room " + scanner.Reception.Rooms[i].RoomNumber + ": bag " + pos + " -> vent " + vent.landing.position + " complete route");
                }

                var ids = All<NetworkObject>(scene).Select(n => new SerializedObject(n).FindProperty("GlobalObjectIdHash").uintValue).ToArray();
                if (ids.Any(i => i == 0) || ids.Distinct().Count() != ids.Length)
                    throw new Exception("Invalid NGO identity.");
                var cases = AssetDatabase.FindAssets("t:BaggageCase", new[]{AssetRoot + "/Cases"}).Select(id => AssetDatabase.LoadAssetAtPath<BaggageCase>(AssetDatabase.GUIDToAssetPath(id))).OrderBy(c => c.name).ToArray();
                foreach (var c in cases)
                {
                    if (!c.Valid(out var error))
                        throw new Exception(c.name + ": " + error);
                    foreach (int tilt in new[]{-1, 0, 1})
                        foreach (bool pulse in new[]{false, true})
                            if (c.Balance(tilt, tilt, pulse) != c.Balance(tilt, tilt, pulse))
                                throw new Exception("Nondeterministic/NaN balance");
                    lines.Add(c.name + " mass=" + c.Mass.ToString("0.0") + " metal=" + c.Metal.ToString("0.0") + " balance L/level/R=" + c.Balance(-1, -1, false).ToString("0.00") + "/" + c.Balance(0, 0, false).ToString("0.00") + "/" + c.Balance(1, 1, false).ToString("0.00") + " pulse onset=" + c.Delay + " decay=" + c.Decay);
                }

                File.WriteAllLines(Reports + "/scene-validation.txt", new[]{"PASS: native saved-scene reopen, analyzer/case/baggage/scenario references, unique NGO identities, no missing scripts, four complete room-to-vent paths; " + cases.Length + " consistent authored cases."}.Concat(lines));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
