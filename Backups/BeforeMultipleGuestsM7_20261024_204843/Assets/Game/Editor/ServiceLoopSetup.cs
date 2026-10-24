using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using MotelGame.Service;
using MotelGame.Interactions;

namespace MotelGame.Editor
{
    public static class ServiceLoopSetup
    {
        const string ScenePath = "Assets/Game/Scenes/Motel_Blockout_V3.unity";
        const string Reports = "Documentation/ServiceLoopM4";
        const string Request = "Library/MotelServiceSetup.request";
        const string BuildRequest = "Library/MotelServiceBuild.request";
        [InitializeOnLoadMethod] static void Initialize()
        {
            EditorApplication.delayCall += Check;
            EditorApplication.update += PollExplicitRefresh;
        }
        static double nextPoll;
        static void PollExplicitRefresh()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            if ((File.Exists(BuildRequest) || File.Exists("Library/MotelServiceValidate.request")) && !File.Exists("Library/MotelServiceRefresh.request") &&
                !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
            { Check(); return; }
            // An explicit local build-tool request, never an automatic build on arbitrary edits.
            const string refresh = "Library/MotelServiceRefresh.request";
            if (!File.Exists(refresh) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(refresh); AssetDatabase.Refresh(); EditorApplication.delayCall += Check;
        }
        static void Check()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            { EditorApplication.delayCall += Check; return; }
            Directory.CreateDirectory(Reports);
            bool okay = true;
            if (File.Exists("Library/MotelServicePresentation.request"))
            {
                string hash=File.ReadAllText("Library/MotelServicePresentation.request").Trim();
                File.Delete("Library/MotelServicePresentation.request");
                okay=PatchPresentation(hash);
            }
            if (File.Exists(Request)) { string hash = File.ReadAllText(Request).Trim(); File.Delete(Request); okay = Install(hash); }
            if (okay && File.Exists("Library/MotelServiceValidate.request"))
            { File.Delete("Library/MotelServiceValidate.request"); Validate(); }
            if (okay && File.Exists(BuildRequest)) { File.Delete(BuildRequest); Build(); }
        }
        static string Hash(string path)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
        static void Set(UnityEngine.Object obj, string name, UnityEngine.Object value)
        { var s = new SerializedObject(obj); s.FindProperty(name).objectReferenceValue = value; s.ApplyModifiedPropertiesWithoutUndo(); }
        static void SetArray(UnityEngine.Object obj, string name, params UnityEngine.Object[] values)
        {
            var s = new SerializedObject(obj); var p = s.FindProperty(name); p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            s.ApplyModifiedPropertiesWithoutUndo();
        }
        static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/MotelBlockoutV3/" + name + ".mat");
        static GameObject Child(string name, Transform parent)
        { var go = new GameObject(name); go.transform.SetParent(parent, false); return go; }
        static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 size, string material, bool solid = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false);
            go.transform.position = position; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = Material(material);
            if (!solid) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
        static TextMesh Label(string name, Transform parent, Vector3 position, Vector3 facing, string text, float size)
        {
            var go = Child(name, parent); go.transform.position = position; go.transform.rotation = Quaternion.LookRotation(-facing);
            var mesh = go.AddComponent<TextMesh>(); mesh.text = text; mesh.fontSize = 64; mesh.characterSize = size;
            mesh.anchor = TextAnchor.MiddleCenter; mesh.alignment = TextAlignment.Center;
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.GetComponent<Renderer>().sharedMaterial = mesh.font.material;
            mesh.color = new Color(.9f, .86f, .7f);
            var worldText=go.AddComponent<WorldServiceText>();
            Set(worldText,"textShader",AssetDatabase.LoadAssetAtPath<Shader>("Assets/Game/Shaders/WorldServiceText.shader"));
            worldText.RefreshMaterial();return mesh;
        }
        static NetworkObject AddNetwork(GameObject go)
        { var n = go.AddComponent<NetworkObject>(); n.SynchronizeTransform = false; n.AutoObjectParentSync = false; return n; }
        static CleaningSpot ConfigureSpot(GameObject go, ServiceRoom room, int index, Collider collider, GameObject dirty, GameObject clean)
        {
            var spot = go.AddComponent<CleaningSpot>(); Set(spot, "room", room); Set(spot, "targetCollider", collider);
            Set(spot, "dirtyVisual", dirty); Set(spot, "cleanVisual", clean);
            var s = new SerializedObject(spot); s.FindProperty("index").intValue = index; s.ApplyModifiedPropertiesWithoutUndo();
            return spot;
        }
        static bool Install(string expectedHash)
        {
            Scene scene = default, previous = SceneManager.GetActiveScene(); bool opened = false;
            try
            {
                if (Hash(ScenePath) != expectedHash) throw new Exception("Scene changed since inspection. Installation cancelled.");
                scene = SceneManager.GetSceneByPath(ScenePath);
                if (scene.IsValid() && scene.isLoaded && scene.isDirty) throw new Exception("Save or discard unsaved scene edits before M4 installation.");
                if (!scene.IsValid() || !scene.isLoaded) { scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); opened = true; }
                SceneManager.SetActiveScene(scene);
                var roots = scene.GetRootGameObjects();
                if (roots.Any(g => g.name == "MotelService")) throw new Exception("M4 already installed. Refusing duplicate installation.");
                var motel = roots.Single(g => g.name == "Motel").transform;
                var roomRoot = motel.Find("Rooms/Room101");
                var serviceRoot = Child("ServiceState", roomRoot); AddNetwork(serviceRoot);
                var room = serviceRoot.AddComponent<ServiceRoom>();
                var mattress = roomRoot.Find("Furniture/DoubleBed_mattress").gameObject;
                var dirty = Child("UnmadeBedding", serviceRoot.transform);
                var fold1 = Cube("RumpledBlanket", dirty.transform, new Vector3(17.15f,.65f,13.35f), new Vector3(1.25f,.13f,.85f), "Linen");
                fold1.transform.rotation = Quaternion.Euler(0,16,5);
                var fold2 = Cube("BlanketFold", dirty.transform, new Vector3(16.8f,.65f,13.9f), new Vector3(.95f,.12f,.5f), "Linen");
                fold2.transform.rotation = Quaternion.Euler(0,-22,-4);
                var clean = Child("MadeBedding", serviceRoot.transform);
                Cube("StraightBlanket", clean.transform, new Vector3(17,.61f,13.4f), new Vector3(1.52f,.055f,1.55f), "Linen");
                Cube("PillowLeft", clean.transform, new Vector3(16.6f,.66f,14.3f), new Vector3(.58f,.14f,.36f), "Linen");
                Cube("PillowRight", clean.transform, new Vector3(17.4f,.66f,14.3f), new Vector3(.58f,.14f,.36f), "Linen");
                clean.SetActive(false);
                var bed = ConfigureSpot(mattress, room, -1, mattress.GetComponent<Collider>(), dirty, clean);
                var trash = new List<CleaningSpot>();
                var positions = new[] { new Vector3(18.1f,.09f,12.4f), new Vector3(17.15f,.09f,11.7f) };
                for (int i = 0; i < positions.Length; i++)
                {
                    var t = Child("Trash_" + (i+1), serviceRoot.transform); t.transform.position = positions[i];
                    var visual = Cube("DiscardedPackaging", t.transform, positions[i], new Vector3(.28f,.16f,.24f), "Cardboard");
                    var collider = t.AddComponent<BoxCollider>(); collider.size = new Vector3(.32f,.18f,.28f);
                    trash.Add(ConfigureSpot(t, room, i, collider, visual, null));
                }
                var outside = Label("ServiceStatus_Outside", serviceRoot.transform, new Vector3(20.86f,2.15f,13.75f), Vector3.right, "ROOM 101 — DIRTY\nBed: unmade   Trash: 0/2", .009f);
                var inside = Label("ServiceStatus_Inside", serviceRoot.transform, new Vector3(20.49f,2.15f,13.75f), Vector3.left, outside.text, .009f);
                Set(room,"bed",bed); SetArray(room,"trash",trash.Cast<UnityEngine.Object>().ToArray()); SetArray(room,"statusSigns",outside,inside);
                Set(room,"guestInRoom",motel.Find("PlayerScale/Room101Player_1.8m").gameObject);
                var receptionRoot = new GameObject("MotelService"); AddNetwork(receptionRoot);
                var reception = receptionRoot.AddComponent<ReceptionDesk>();
                var counter = motel.Find("ReceptionDesk/Counter_Main_3.4m").GetComponent<Collider>();
                var interaction = counter.gameObject.AddComponent<ReceptionInteraction>(); Set(interaction,"reception",reception);
                SetArray(reception,"rooms",room); Set(reception,"counter",counter);
                Set(reception,"waitingGuest",motel.Find("PlayerScale/WaitingGuest_1.8m").gameObject);
                var guestLabel = Label("WaitingGuestLabel", receptionRoot.transform, new Vector3(4.6f,2.05f,6.2f), Vector3.back, "GUEST WAITING\nRegister at Reception", .012f);
                Set(reception,"guestLabel",guestLabel);
                Label("ReceptionInstructions", receptionRoot.transform, new Vector3(10.28f,1.35f,5.7f), Vector3.left, "RECEPTION\n[E] Room service", .013f);
                Label("RoomGuestLabel", serviceRoot.transform, new Vector3(19.2f,2.05f,13.6f), Vector3.right, "REGISTERED GUEST", .012f).transform.SetParent(motel.Find("PlayerScale/Room101Player_1.8m"),true);
                // This existing scale placeholder now represents the checked-in guest.
                motel.Find("PlayerScale/Room101Player_1.8m").gameObject.SetActive(false);
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new Exception("Scene save failed.");
                foreach (var n in new[]{serviceRoot.GetComponent<NetworkObject>(),receptionRoot.GetComponent<NetworkObject>()})
                    n.SendMessage("OnValidate",SendMessageOptions.DontRequireReceiver);
                EditorSceneManager.SaveScene(scene,ScenePath);
                Validate();
                return true;
            }
            catch (Exception e) { File.WriteAllText(Reports+"/setup-error.txt",e.ToString()); Debug.LogException(e); return false; }
            finally
            {
                if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene,true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
        static bool PatchPresentation(string hash)
        {
            Scene scene=default;bool opened=false;
            try
            {
                if(Hash(ScenePath)!=hash)throw new Exception("Scene changed before presentation patch.");
                scene=SceneManager.GetSceneByPath(ScenePath);
                if(scene.IsValid() && scene.isLoaded && scene.isDirty)throw new Exception("Unsaved scene edits. Save or discard before presentation patch.");
                if(!scene.IsValid() || !scene.isLoaded){scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);opened=true;}
                foreach(var text in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<TextMesh>(true)))
                {
                    if(!new[]{"ServiceStatus_Outside","ServiceStatus_Inside","WaitingGuestLabel","ReceptionInstructions","RoomGuestLabel"}.Contains(text.name))continue;
                    text.characterSize=text.name.StartsWith("ServiceStatus")?.009f:text.name=="ReceptionInstructions"?.013f:.012f;
                    var worldText=text.GetComponent<WorldServiceText>() ?? text.gameObject.AddComponent<WorldServiceText>();
                    Set(worldText,"textShader",AssetDatabase.LoadAssetAtPath<Shader>("Assets/Game/Shaders/WorldServiceText.shader"));
                    worldText.RefreshMaterial();
                }
                return EditorSceneManager.SaveScene(scene,ScenePath);
            }
            catch(Exception e){File.WriteAllText(Reports+"/presentation-error.txt",e.ToString());Debug.LogException(e);return false;}
            finally{if(opened && scene.IsValid())EditorSceneManager.CloseScene(scene,true);}
        }
        [MenuItem("Tools/Motel/Validate Service Loop M4")]
        public static void Validate()
        {
            var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var roots = scene.GetRootGameObjects();
                var rooms = roots.SelectMany(g=>g.GetComponentsInChildren<ServiceRoom>(true)).ToArray();
                if (rooms.Length == 0) throw new Exception("No configured service rooms.");
                foreach (var room in rooms) if (!room.ConfigurationValid(out string error)) throw new Exception(error);
                var reception = roots.SelectMany(g=>g.GetComponentsInChildren<ReceptionDesk>(true)).Single();
                if (!reception.ConfigurationValid() || !rooms.All(r=>reception.Rooms.Contains(r))) throw new Exception("Invalid reception references.");
                foreach (var t in roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0) throw new Exception("Missing script: "+t.name);
                if (roots.SelectMany(g=>g.GetComponentsInChildren<NetworkDoor>(true)).Count()!=1) throw new Exception("Existing door missing.");
                Capture(scene);
                File.WriteAllText(Reports+"/setup-validation.txt","PASS: Scene reopened in Unity "+Application.unityVersion+".\nService rooms, trash spots, bed, status signs, guest visuals and Reception references validated; no missing scripts; original NetworkDoor present.\nScene SHA256: "+Hash(ScenePath)+"\n");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        static void Capture(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            foreach (var t in roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true))) t.gameObject.layer=30;
            foreach (var light in roots.SelectMany(g=>g.GetComponentsInChildren<Light>(true))) light.cullingMask=1<<30;
            foreach(var text in roots.SelectMany(g=>g.GetComponentsInChildren<WorldServiceText>(true)))text.RefreshMaterial();
            var camera=roots.SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First();
            camera.scene=scene;camera.cullingMask=1<<30;camera.enabled=false;camera.fieldOfView=60;
            var views = new[]{
                ("bed",new Vector3(18.9f,1.65f,12.7f),new Vector3(17,.7f,13.6f)),
                ("reception",new Vector3(8.6f,1.65f,5.7f),new Vector3(10.7f,1.2f,5.7f)),
                ("room-sign",new Vector3(22.1f,1.65f,13.75f),new Vector3(20.8f,2,13.75f))};
            foreach(var view in views)
            {
                camera.transform.position=view.Item2;camera.transform.LookAt(view.Item3);
                var rt=new RenderTexture(960,640,24);rt.Create();var old=RenderTexture.active;camera.targetTexture=rt;
                try
                {
                    camera.Render();RenderTexture.active=rt;var image=new Texture2D(960,640,TextureFormat.RGB24,false);
                    image.ReadPixels(new Rect(0,0,960,640),0,0);image.Apply();
                    File.WriteAllBytes(Reports+"/preview-"+view.Item1+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
                }
                finally{camera.targetTexture=null;RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
            }
            File.WriteAllLines(Reports+"/text-bounds.txt",roots.SelectMany(g=>g.GetComponentsInChildren<TextMesh>(true)).Select(t=>t.name+" "+t.GetComponent<Renderer>().bounds.size));
        }
        [MenuItem("Tools/Motel/Build Service Loop M4")]
        public static void Build()
        {
            var snapshots = new Dictionary<string,byte[]>();
            foreach (var path in new[]{"Assets/Settings/DefaultVolumeProfile.asset","Assets/Settings/PC_RPAsset.asset","Assets/Settings/UniversalRenderPipelineGlobalSettings.asset","ProjectSettings/ProjectSettings.asset","ProjectSettings/UnityConnectSettings.asset","ProjectSettings/EditorBuildSettings.asset"})
                if(File.Exists(path)) snapshots[path]=File.ReadAllBytes(path);
            try
            {
                Validate();
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{ScenePath}, locationPathName="Builds/ServiceLoopM4/MotelGame.app", target=BuildTarget.StandaloneOSX, options=BuildOptions.Development });
                File.WriteAllText(Reports+"/build-validation.txt",report.summary.result+"\nErrors: "+report.summary.totalErrors+"\nWarnings: "+report.summary.totalWarnings+"\n");
                if(report.summary.result!=BuildResult.Succeeded) throw new Exception("M4 build failed.");
            }
            catch(Exception e) { File.WriteAllText(Reports+"/build-error.txt",e.ToString());Debug.LogException(e); }
            finally { foreach(var pair in snapshots)File.WriteAllBytes(pair.Key,pair.Value);AssetDatabase.Refresh(); }
        }
    }
}
