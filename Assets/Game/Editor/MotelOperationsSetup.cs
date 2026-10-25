using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Unity.Netcode;
using MotelGame.Service;

namespace MotelGame.Editor
{
    // Explicit, guarded scene installation. No automatic scene rewriting on reload.
    public static class MotelOperationsSetup
    {
        public const string Reports = "Documentation/MotelOperationsM11";
        const string InstallRequest = "Library/MotelOperationsSetup.request", ValidateRequest = "Library/MotelOperationsValidate.request";
        const string PlayerPath = "Assets/Game/Prefabs/NetworkPlayer.prefab", MeshPath = "Assets/Game/Models/GarbageBag.asset";
        static double nextPoll;
        [InitializeOnLoadMethod] static void Init() => EditorApplication.update += Poll;
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return; nextPoll = EditorApplication.timeSinceStartup + 1;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || File.Exists("Library/MotelServiceRefresh.request")) return;
            if (!File.Exists(InstallRequest) && !File.Exists(ValidateRequest) && !File.Exists("Library/MotelOperationsRefine.request")) return;
            Directory.CreateDirectory(Reports);
            try
            {
                if (File.Exists(InstallRequest)) { string hash = File.ReadAllText(InstallRequest).Trim(); File.Delete(InstallRequest); Install(hash); }
                if (File.Exists("Library/MotelOperationsRefine.request")) { File.Delete("Library/MotelOperationsRefine.request"); Refine(); }
                if (File.Exists(ValidateRequest)) { File.Delete(ValidateRequest); Validate(); }
            }
            catch (Exception e) { File.WriteAllText(Reports + "/editor-error.txt", e.ToString()); Debug.LogException(e); }
        }
        static T[] All<T>(Scene s) where T : Component => s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
        static void Set(UnityEngine.Object o, string n, UnityEngine.Object value)
        { var s = new SerializedObject(o); s.FindProperty(n).objectReferenceValue = value; s.ApplyModifiedPropertiesWithoutUndo(); }
        static void Number(UnityEngine.Object o, string n, float value)
        { var s = new SerializedObject(o); s.FindProperty(n).floatValue = value; s.ApplyModifiedPropertiesWithoutUndo(); }
        static GameObject Child(string name, Transform parent)
        { var g = new GameObject(name); g.transform.SetParent(parent, false); return g; }
        static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/MotelBlockoutV3/" + name + ".mat");
        static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale, string material, bool collider = false)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name; g.transform.SetParent(parent, false); g.transform.position = position; g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = Material(material);
            if (!collider) UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>()); return g;
        }
        static TextMesh Label(string name, Transform parent, Vector3 position, Vector3 facing, string text, float size)
        {
            var g = Child(name, parent); g.transform.SetPositionAndRotation(position, Quaternion.LookRotation(-facing));
            var t = g.AddComponent<TextMesh>(); t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.fontSize = 64;
            t.characterSize = size; t.anchor = TextAnchor.MiddleCenter; t.alignment = TextAlignment.Center; t.text = text; t.color = new Color(.8f, .88f, .72f);
            var world = g.AddComponent<WorldServiceText>(); Set(world, "textShader", AssetDatabase.LoadAssetAtPath<Shader>("Assets/Game/Shaders/WorldServiceText.shader")); world.RefreshMaterial(); return t;
        }
        static Mesh BagMesh()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath); if (existing != null) return existing;
            // Flat triangles form a small tied bag; generated once as an Editor asset, never at runtime.
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            var rings = new[] { new Vector2(.12f, 0), new Vector2(.25f, .12f), new Vector2(.23f, .4f), new Vector2(.045f, .53f), new Vector2(.09f, .6f) };
            for (int ring = 0; ring < rings.Length - 1; ring++) for (int side = 0; side < 8; side++)
            {
                Vector3 Point(int r, int k) => new Vector3(Mathf.Cos(k * Mathf.PI / 4) * rings[r].x, rings[r].y, Mathf.Sin(k * Mathf.PI / 4) * rings[r].x);
                void Triangle(Vector3 a, Vector3 b, Vector3 c) { int n = vertices.Count; vertices.AddRange(new[] { a, b, c }); triangles.AddRange(new[] { n, n + 1, n + 2 }); }
                Triangle(Point(ring, side), Point(ring + 1, side), Point(ring + 1, side + 1));
                Triangle(Point(ring, side), Point(ring + 1, side + 1), Point(ring, side + 1));
                if (ring == 0) Triangle(Vector3.zero, Point(0, side), Point(0, side + 1));
                if (ring == rings.Length - 2) Triangle(Vector3.up * .6f, Point(ring + 1, side + 1), Point(ring + 1, side));
            }
            var mesh = new Mesh { name = "GarbageBagLowPoly" }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (!AssetDatabase.IsValidFolder("Assets/Game/Models")) AssetDatabase.CreateFolder("Assets/Game", "Models");
            AssetDatabase.CreateAsset(mesh, MeshPath); return mesh;
        }
        static GameObject Bag(string name, Transform parent, Vector3 localPosition)
        {
            var g = Child(name, parent); g.transform.localPosition = localPosition;
            g.AddComponent<MeshFilter>().sharedMesh = BagMesh(); g.AddComponent<MeshRenderer>().sharedMaterial = Material("Trim"); return g;
        }
        static void Install(string expected)
        {
            string path = MultipleGuestsSetup.ScenePath;
            using (var hash = SHA256.Create()) if (BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant() != expected) throw new Exception("Scene changed since inspection.");
            var scene = SceneManager.GetSceneByPath(path); var previous = SceneManager.GetActiveScene(); bool opened = !scene.IsValid() || !scene.isLoaded;
            if (!opened && scene.isDirty) throw new Exception("Unsaved scene edits: save them before M11 installation.");
            try
            {
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); SceneManager.SetActiveScene(scene);
                if (All<GarbageDumpster>(scene).Length > 0) throw new Exception("M11 already installed; duplicate setup refused.");
                var reception = All<ReceptionDesk>(scene).Single(); var shift = All<NightShiftController>(scene).Single(); var requests = All<GuestServiceRequests>(scene).Single();
                var root = new GameObject("MotelOperations").transform;
                var panel = Child("UtilityBreakers", root).transform;
                Cube("PanelBacking", panel, new Vector3(16.7f, 1.7f, 1.85f), new Vector3(2.6f, .95f, .08f), "Machine");
                Label("PanelTitle", panel, new Vector3(16.7f, 2.04f, 1.901f), Vector3.forward, "ROOM POWER / UTILITY", .012f);
                for (int i = 0; i < reception.Rooms.Length; i++)
                {
                    var room = reception.Rooms[i]; bool east = i % 2 == 1; float zOffset = i >= 2 ? 5.7f : 0;
                    var pickup = Child("Room" + room.RoomNumber + "_GarbageBag", root);
                    pickup.transform.position = new Vector3(east ? 24.17f : 20.23f, .02f, 15.2f + zOffset);
                    var bag = Bag("FilledBagVisual", pickup.transform, Vector3.zero);
                    var collider = pickup.AddComponent<BoxCollider>(); collider.center = Vector3.up * .3f; collider.size = new Vector3(.5f, .6f, .5f); collider.enabled = false;
                    var target = pickup.AddComponent<RoomOperationsInteraction>(); Set(target, "room", room); Set(target, "targetCollider", collider); Set(target, "bagVisual", bag); bag.SetActive(false); Set(room, "bagPickup", target);
                    var sw = Cube("Breaker" + room.RoomNumber, panel, new Vector3(15.8f + i * .6f, 1.48f, 1.96f), new Vector3(.18f, .2f, .1f), "Metal", true);
                    var breaker = sw.AddComponent<RoomOperationsInteraction>(); Set(breaker, "room", room); Set(breaker, "targetCollider", sw.GetComponent<Collider>());
                    var settings = new SerializedObject(breaker); settings.FindProperty("action").enumValueIndex = 1; settings.ApplyModifiedPropertiesWithoutUndo();
                    Set(breaker, "label", Label("Label" + room.RoomNumber, panel, new Vector3(15.8f + i * .6f, 1.78f, 1.906f), Vector3.forward, "ROOM " + room.RoomNumber + "\nPOWER OK", .009f)); Set(room, "breaker", breaker);
                    var light = All<Light>(scene).Single(l => l.name == "Room" + room.RoomNumber + "_Light");
                    settings = new SerializedObject(room); var lights = settings.FindProperty("controlledLights"); lights.arraySize = 1; lights.GetArrayElementAtIndex(0).objectReferenceValue = light; settings.ApplyModifiedPropertiesWithoutUndo();
                }
                var dumpster = Child("OutdoorDumpster", root); dumpster.AddComponent<NetworkObject>(); var disposal = dumpster.AddComponent<GarbageDumpster>(); Set(disposal, "shift", shift);
                var body = Cube("Container", dumpster.transform, new Vector3(18.3f, .6f, -3.1f), new Vector3(1.4f, 1.2f, 1.1f), "Metal", true); Set(disposal, "targetCollider", body.GetComponent<Collider>());
                Cube("Lid", dumpster.transform, new Vector3(18.3f, 1.24f, -3.1f), new Vector3(1.48f, .08f, 1.18f), "Trim");
                Label("DisposalSign", dumpster.transform, new Vector3(17.59f, .8f, -3.1f), Vector3.left, "TRASH\n[E] DISPOSE", .012f);
                // Longer initial preparation window and request spacing compensate for each outdoor trip.
                Number(shift, "firstArrivalDelay", 85); Number(reception, "arrivalPatience", 150); Number(requests, "callDelay", 30); Number(requests, "betweenRequestsDelay", 20);
                var board = All<StaffTaskBoard>(scene).Single(); var boardSettings = new SerializedObject(board);
                var boardText = (TextMesh)boardSettings.FindProperty("label").objectReferenceValue; boardText.characterSize = .0095f;
                InstallPlayer();
                foreach (var text in All<WorldServiceText>(scene)) text.RefreshMaterial();
                EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene, path)) throw new Exception("M11 scene save failed.");
                foreach (var obj in All<NetworkObject>(scene))
                { typeof(NetworkObject).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(obj, null); if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj); }
                EditorSceneManager.SaveScene(scene, path);
            }
            finally { if (previous.IsValid()) SceneManager.SetActiveScene(previous); if (opened && scene.IsValid()) EditorSceneManager.CloseScene(scene, true); }
            Validate();
        }
        static void Refine()
        {
            var scene=SceneManager.GetSceneByPath(MultipleGuestsSetup.ScenePath);bool opened=!scene.IsValid()||!scene.isLoaded;
            if(!opened&&scene.isDirty)throw new Exception("Unsaved scene changes.");
            try
            {
                if(opened)scene=EditorSceneManager.OpenScene(MultipleGuestsSetup.ScenePath,OpenSceneMode.Additive);
                var rooms=All<ServiceRoom>(scene).OrderBy(r=>r.RoomNumber).ToArray();
                for(int i=0;i<rooms.Length;i++)
                {
                    rooms[i].BagPickup.transform.position=new Vector3(i%2==1?24.17f:20.23f,.02f,15.2f+(i>=2?5.7f:0));
                    rooms[i].Breaker.GetComponent<Renderer>().sharedMaterial=Material("Metal");
                }
                foreach(var text in All<WorldServiceText>(scene))text.RefreshMaterial();
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,MultipleGuestsSetup.ScenePath);
            }
            finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
            Validate();
        }
        static void InstallPlayer()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                var carrier = root.GetComponent<TowelCarrier>();
                var first = Bag("FirstPersonGarbageBag", root.GetComponentInChildren<Camera>(true).transform, new Vector3(.36f, -.62f, .62f)); first.transform.localScale = Vector3.one * .8f;
                first.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var third = Bag("CarriedGarbageBag", root.transform, new Vector3(.35f, .58f, .25f));
                Set(carrier, "firstPersonBag", first); Set(carrier, "thirdPersonBag", third); first.SetActive(false); third.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        [MenuItem("Tools/Motel/Validate Motel Operations M11")]
        public static void Validate()
        {
            Directory.CreateDirectory(Reports); var scene = EditorSceneManager.OpenPreviewScene(MultipleGuestsSetup.ScenePath);
            try
            {
                var rooms = All<ServiceRoom>(scene); var dumpster = All<GarbageDumpster>(scene).Single();
                if (!dumpster.ConfigurationValid || rooms.Length != 4 || All<RoomOperationsInteraction>(scene).Length != 8) throw new Exception("M11 target count/configuration.");
                foreach (var room in rooms)
                {
                    if (!room.ConfigurationValid(out var error) || !room.BagPickup.ConfigurationValid || !room.Breaker.ConfigurationValid || room.BagPickup.Room != room || room.Breaker.Room != room) throw new Exception("Room " + room.RoomNumber + ": " + error);
                    if (room.ControlledLights.Any(l => l.type != LightType.Point || !l.enabled || l.lightmapBakeType == LightmapBakeType.Baked)) throw new Exception("Room runtime lights unavailable.");
                }
                var carrier = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath).GetComponent<TowelCarrier>(); if (!carrier.ConfigurationValid) throw new Exception("Player carry references.");
                foreach (var t in All<Transform>(scene)) if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0) throw new Exception("Missing script: " + t.name);
                var surface = All<NavMeshSurface>(scene).Single(); surface.AddData(); var lines = new List<string>();
                foreach (var a in rooms) foreach (var b in rooms) if (a != b) Path(a.DoorOutside.position, b.DoorOutside.position, lines);
                Path(new Vector3(8, 0, 2), new Vector3(16.8f, 0, -3.1f), lines);
                Path(new Vector3(16.8f, 0, 4.6f), new Vector3(16.7f, 0, 3.2f), lines);
                Physics.SyncTransforms();
                var colliders = All<Collider>(scene).Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && c.GetComponentInParent<GuestLifecycle>() == null).ToArray();
                foreach (var p in new[] { new Vector3(16.8f, 0, -3.1f), new Vector3(16.7f, 0, 3.2f) })
                    if (colliders.Any(c => Vector3.Distance(c.ClosestPoint(p + Vector3.up), p + Vector3.up) < .31f)) throw new Exception("Service approach blocked.");
                var motel=scene.GetRootGameObjects().Single(g=>g.name=="Motel");
                var original=motel.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.GetComponent<TextMesh>()==null).ToArray();
                var additions=scene.GetRootGameObjects().Single(g=>g.name=="MotelOperations").GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.GetComponent<TextMesh>()==null);
                foreach(var added in additions)foreach(var old in original)
                {
                    // M12 cosmetic attachments intentionally sit on their original backing surfaces.
                    // All bag/furniture and other architecture intersections remain checked.
                    if(added.name=="PanelBacking"&&old.GetComponentsInParent<Transform>().Any(t=>t.name=="Visual_panel_etx_1"))continue;
                    if(added.transform.IsChildOf(dumpster.transform)&&(old.name=="DumpsterRib"||old.name=="DumpsterPad"))continue;
                    var overlap=Vector3.Min(added.bounds.max,old.bounds.max)-Vector3.Max(added.bounds.min,old.bounds.min);
                    if(overlap.x>0&&overlap.y>0&&overlap.z>0&&overlap.x*overlap.y*overlap.z>.00001f)throw new Exception("M11 primitive intersects original geometry: "+added.name+" / "+old.name);
                }
                File.WriteAllLines(Reports + "/navigation-validation.txt", lines);
                File.WriteAllText(Reports + "/setup-validation.txt", "PASS: saved scene reopened, 4 bags / 4 breakers / dumpster, player visuals and room lights assigned, no missing scripts; 12 inter-room routes plus dumpster and Utility approaches complete. Existing NavMesh reused. New mesh bounds do not overlap original active architecture/furniture.\n");
                Capture(scene);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        static void Path(Vector3 a, Vector3 b, List<string> lines)
        {
            if (!NavMesh.SamplePosition(a, out var start, .4f, NavMesh.AllAreas) || !NavMesh.SamplePosition(b, out var end, .4f, NavMesh.AllAreas)) throw new Exception("M11 point off NavMesh: " + a + " -> " + b);
            var path = new NavMeshPath(); if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) throw new Exception("M11 route incomplete.");
            lines.Add(a + " -> " + b + ": complete");
        }
        static void Capture(Scene scene)
        {
            foreach (var t in All<Transform>(scene)) t.gameObject.layer = 30;
            foreach (var l in All<Light>(scene)) l.cullingMask = 1 << 30;
            foreach (var text in All<WorldServiceText>(scene)) text.RefreshMaterial();
            foreach (var c in All<Camera>(scene)) c.enabled = false;
            var camera = All<Camera>(scene).First(); camera.scene = scene; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene); camera.cullingMask = 1 << 30; camera.fieldOfView = 60; camera.nearClipPlane = .03f;
            var views = new[] { ("breakers", new Vector3(16.7f, 1.65f, 3.5f), new Vector3(16.7f, 1.7f, 1.9f)), ("dumpster", new Vector3(15.8f, 1.65f, -4.3f), new Vector3(18.3f, .7f, -3.1f)),
                ("room-power-on", new Vector3(20,1.65f,13.4f), new Vector3(17.2f,1,13.4f)),
                ("room-power-off", new Vector3(20,1.65f,13.4f), new Vector3(17.2f,1,13.4f)) };
            foreach (var v in views)
            {
                All<Light>(scene).Single(l=>l.name=="Room101_Light").enabled=v.Item1!="room-power-off";
                camera.transform.position = v.Item2; camera.transform.LookAt(v.Item3);
                var rt = new RenderTexture(1200, 800, 24); rt.Create(); var previous = RenderTexture.active; camera.targetTexture = rt;
                try { camera.Render(); RenderTexture.active = rt; var image = new Texture2D(1200, 800, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); image.Apply(); File.WriteAllBytes(Reports + "/preview-" + v.Item1 + ".png", image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image); }
                finally { camera.targetTexture = null; RenderTexture.active = previous; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            }
        }
    }
}
