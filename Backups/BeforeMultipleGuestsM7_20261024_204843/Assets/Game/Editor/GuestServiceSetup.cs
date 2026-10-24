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
    public static class GuestServiceSetup
    {
        const string ScenePath = "Assets/Game/Scenes/Motel_Blockout_V3.unity";
        const string PlayerPath = "Assets/Game/Prefabs/NetworkPlayer.prefab";
        const string Reports = "Documentation/GuestServiceM5";
        const string InstallRequest = "Library/MotelGuestSetup.request";
        const string BuildRequest = "Library/MotelGuestBuild.request";
        const string ValidateRequest = "Library/MotelGuestValidate.request";
        static double nextPoll;
        [InitializeOnLoadMethod] static void Initialize() => EditorApplication.update += Poll;
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
                File.Exists("Library/MotelServiceRefresh.request")) return;
            if (!File.Exists(InstallRequest) && !File.Exists(BuildRequest) && !File.Exists(ValidateRequest)) return;
            Directory.CreateDirectory(Reports);
            try
            {
                if (File.Exists(InstallRequest))
                {
                    string[] hashes = File.ReadAllLines(InstallRequest); File.Delete(InstallRequest);
                    Install(hashes[0].Trim(), hashes[1].Trim());
                }
                if (File.Exists(ValidateRequest)) { File.Delete(ValidateRequest); Validate(); }
                if (File.Exists(BuildRequest)) { File.Delete(BuildRequest); Build(); }
            }
            catch (Exception e)
            {
                // A failed setup must never be followed by a build of an incomplete scene.
                if (File.Exists(BuildRequest)) File.Delete(BuildRequest);
                File.WriteAllText(Reports + "/editor-error.txt", e.ToString()); Debug.LogException(e);
            }
        }
        static string Hash(string path)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
        static void Set(UnityEngine.Object obj, string name, UnityEngine.Object value)
        { var s = new SerializedObject(obj); s.FindProperty(name).objectReferenceValue = value; s.ApplyModifiedPropertiesWithoutUndo(); }
        static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/MotelBlockoutV3/" + name + ".mat");
        static GameObject Child(string name, Transform parent)
        { var g = new GameObject(name); g.transform.SetParent(parent, false); return g; }
        static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 size, string material, bool solid = false)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name; g.transform.SetParent(parent, false);
            g.transform.localPosition = position; g.transform.localScale = size; g.GetComponent<Renderer>().sharedMaterial = Material(material);
            if (!solid) UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }
        static TextMesh Label(string name, Transform parent, Vector3 position, Vector3 facing, string text, float size)
        {
            var g = Child(name, parent); g.transform.position = position; g.transform.rotation = Quaternion.LookRotation(-facing);
            var label = g.AddComponent<TextMesh>(); label.text = text; label.fontSize = 64; label.characterSize = size;
            label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.color = new Color(.9f,.86f,.7f);
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var world = g.AddComponent<WorldServiceText>();
            Set(world,"textShader",AssetDatabase.LoadAssetAtPath<Shader>("Assets/Game/Shaders/WorldServiceText.shader"));
            world.RefreshMaterial(); return label;
        }
        static GuestServiceInteraction Interaction(GameObject g, GuestServiceRequests service, GuestServiceAction action, ServiceRoom room = null)
        {
            var i = g.AddComponent<GuestServiceInteraction>(); Set(i,"service",service); Set(i,"deliveryRoom",room); Set(i,"targetCollider",g.GetComponent<Collider>());
            var s = new SerializedObject(i); s.FindProperty("action").enumValueIndex = (int)action; s.ApplyModifiedPropertiesWithoutUndo(); return i;
        }
        static void Install(string sceneHash, string playerHash)
        {
            if (Hash(ScenePath) != sceneHash || Hash(PlayerPath) != playerHash) throw new Exception("Scene/player changed since inspection; installation cancelled.");
            Scene scene = SceneManager.GetSceneByPath(ScenePath), previous = SceneManager.GetActiveScene(); bool opened = false;
            if (scene.IsValid() && scene.isLoaded && scene.isDirty) throw new Exception("Unsaved motel scene edits; save before installing M5.");
            try
            {
                if (!scene.IsValid() || !scene.isLoaded) { scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); opened = true; }
                SceneManager.SetActiveScene(scene);
                var roots = scene.GetRootGameObjects();
                if (roots.Any(g => g.name == "GuestService")) throw new Exception("M5 already installed; refusing duplicate objects.");
                var motel = roots.Single(g => g.name == "Motel").transform;
                var room = motel.Find("Rooms/Room101/ServiceState").GetComponent<ServiceRoom>();
                var root = new GameObject("GuestService");
                var network = root.AddComponent<NetworkObject>(); network.SynchronizeTransform = false; network.AutoObjectParentSync = false;
                var service = root.AddComponent<GuestServiceRequests>(); Set(service,"room",room);
                var telephone = motel.Find("ReceptionDesk/Telephone").gameObject;
                if (telephone.GetComponent<Collider>() == null) telephone.AddComponent<BoxCollider>();
                var phone = Interaction(telephone,service,GuestServiceAction.AnswerPhone); Set(service,"phone",phone);
                var audioObject = Child("PhoneRingtone",root.transform); audioObject.transform.position = telephone.transform.position;
                var audio = audioObject.AddComponent<AudioSource>(); audio.clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/PhoneRing.wav");
                audio.playOnAwake = false; audio.loop = true; audio.spatialBlend = 1; audio.volume = .5f; audio.minDistance = 3; audio.maxDistance = 26;
                audio.rolloffMode = AudioRolloffMode.Linear; audio.dopplerLevel = 0; Set(service,"ringtone",audio);
                Set(service,"phoneLabel",Label("PhoneStatus",root.transform,new Vector3(10.8f,1.58f,5.75f),Vector3.left,"PHONE\nReception",.009f));
                var stack = Child("UtilityTowelStack",root.transform); stack.transform.position = new Vector3(17,1.55f,6.32f);
                for (int i = 0; i < 3; i++) Cube("FoldedTowel_"+i,stack.transform,new Vector3(0,-.075f+i*.07f,0),new Vector3(.58f,.065f,.38f),"Linen");
                var stackCollider = stack.AddComponent<BoxCollider>(); stackCollider.size = new Vector3(.58f,.21f,.38f);
                Set(service,"towelSupply",Interaction(stack,service,GuestServiceAction.TakeTowel));
                Label("TowelShelfLabel",root.transform,new Vector3(17,1.79f,6.05f),Vector3.back,"CLEAN TOWELS\n[E] Take one",.009f);
                // A shallow wall-mounted delivery tray, outside the 1.1 m door opening and its sweep.
                var tray = Cube("Room101Delivery",root.transform,new Vector3(20.87f,1.05f,13.75f),new Vector3(.14f,.08f,.55f),"Wood",true);
                Set(service,"delivery",Interaction(tray,service,GuestServiceAction.DeliverTowel,room));
                Label("DeliveryLabel",root.transform,new Vector3(20.96f,1.43f,13.75f),Vector3.right,"ROOM 101\n[E] Deliver towel",.009f);
                InstallPlayer();
                if (!EditorSceneManager.SaveScene(scene,ScenePath)) throw new Exception("Scene save failed.");
                network.SendMessage("OnValidate",SendMessageOptions.DontRequireReceiver); EditorSceneManager.SaveScene(scene,ScenePath);
                File.WriteAllText(Reports+"/install-validation.txt","Installed M5 on existing scene and player prefab.\n");
                Validate();
            }
            finally
            {
                if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene,true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
        static void InstallPlayer()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                if (root.GetComponent<TowelCarrier>() != null) throw new Exception("Player already has towel carrier.");
                var carrier = root.AddComponent<TowelCarrier>();
                var first = Cube("FirstPersonTowel",root.GetComponentInChildren<Camera>(true).transform,new Vector3(.23f,-.28f,.5f),new Vector3(.38f,.08f,.28f),"Linen");
                first.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var third = Cube("CarriedTowel",root.transform,new Vector3(0,1.15f,.46f),new Vector3(.48f,.12f,.3f),"Linen");
                Set(carrier,"firstPersonTowel",first); Set(carrier,"thirdPersonTowel",third); first.SetActive(false); third.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root,PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        [MenuItem("Tools/Motel/Validate Guest Service M5")]
        public static void Validate()
        {
            Directory.CreateDirectory(Reports);
            var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var roots = scene.GetRootGameObjects();
                var service = roots.SelectMany(g=>g.GetComponentsInChildren<GuestServiceRequests>(true)).Single();
                if (!service.ConfigurationValid()) throw new Exception($"Invalid M5 configuration: room={service.Room}, phone={service.Phone}, collider={service.Phone.TargetCollider}, supply={service.TowelSupply.TargetCollider}, delivery={service.Delivery.Room}, audio={service.Ringtone}, clip={service.Ringtone.clip}, loop={service.Ringtone.loop}.");
                if (!service.Room.ConfigurationValid(out var error)) throw new Exception(error);
                if (!roots.SelectMany(g=>g.GetComponentsInChildren<ReceptionDesk>(true)).Single().ConfigurationValid()) throw new Exception("Invalid M4 reception.");
                if (roots.SelectMany(g=>g.GetComponentsInChildren<NetworkDoor>(true)).Count()!=1) throw new Exception("Existing network door missing.");
                foreach (var t in roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0) throw new Exception("Missing script: "+t.name);
                var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
                if (!player.GetComponent<TowelCarrier>().ConfigurationValid) throw new Exception("Unassigned towel visuals.");
                if (service.Delivery.TargetCollider.bounds.min.z < 13.2f || service.Delivery.TargetCollider.bounds.max.x > 21)
                    throw new Exception("Delivery tray infringes door or corridor clearance.");
                Capture(scene);
                File.WriteAllText(Reports+"/setup-validation.txt","PASS: Scene reopened in Unity "+Application.unityVersion+".\nM5 room/phone/supply/delivery/audio/carry references valid. M4/door intact. No missing scripts.\nDoor opening preserved; corridor clearance at tray >= 2.6 m.\nScene SHA256: "+Hash(ScenePath)+"\n");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        static void Capture(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            foreach (var t in roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true))) t.gameObject.layer = 30;
            foreach (var light in roots.SelectMany(g=>g.GetComponentsInChildren<Light>(true))) light.cullingMask = 1<<30;
            foreach (var label in roots.SelectMany(g=>g.GetComponentsInChildren<WorldServiceText>(true))) label.RefreshMaterial();
            var camera = roots.SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First();
            camera.scene = scene; camera.cullingMask = 1<<30; camera.enabled = false; camera.fieldOfView = 60;
            foreach (var view in new[]{
                ("phone",new Vector3(9.5f,1.65f,5.75f),new Vector3(10.8f,1.3f,5.75f)),
                ("utility",new Vector3(16.8f,1.65f,4.85f),new Vector3(17,1.55f,6.32f)),
                ("delivery",new Vector3(22.2f,1.65f,13.3f),new Vector3(20.85f,1.5f,13.6f))})
            {
                camera.transform.position = view.Item2; camera.transform.LookAt(view.Item3);
                CaptureCamera(camera,view.Item1);
            }
            // Render only a preview-scene copy, without mutating the player prefab or live scene.
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath),scene);
            player.transform.position = new Vector3(6,.03f,2);
            foreach (var t in player.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
            var ownerCamera = player.GetComponentInChildren<Camera>(true);
            ownerCamera.scene = scene; ownerCamera.cullingMask = 1<<30; ownerCamera.enabled = false;
            var config = new SerializedObject(player.GetComponent<TowelCarrier>());
            var first = (GameObject)config.FindProperty("firstPersonTowel").objectReferenceValue;
            var third = (GameObject)config.FindProperty("thirdPersonTowel").objectReferenceValue;
            var body = player.transform.Find("RemoteBody").GetComponent<Renderer>();
            body.enabled = false; first.SetActive(true); third.SetActive(false); CaptureCamera(ownerCamera,"carry-owner");
            first.SetActive(false); third.SetActive(true); body.enabled = true;
            camera.transform.position = new Vector3(6,1.65f,4); camera.transform.LookAt(new Vector3(6,1.15f,2)); CaptureCamera(camera,"carry-remote");
        }
        static void CaptureCamera(Camera camera, string name)
        {
            var rt = new RenderTexture(960,640,24); rt.Create(); var old = RenderTexture.active; camera.targetTexture = rt;
            try
            {
                camera.Render(); RenderTexture.active = rt; var image = new Texture2D(960,640,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,960,640),0,0); image.Apply();
                File.WriteAllBytes(Reports+"/preview-"+name+".png",image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
            }
            finally { camera.targetTexture = null; RenderTexture.active = old; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
        }
        [MenuItem("Tools/Motel/Build Guest Service M5")]
        public static void Build()
        {
            var snapshots = new Dictionary<string,byte[]>();
            foreach (var path in new[]{"Assets/Settings/Mobile_RPAsset.asset","Assets/Settings/DefaultVolumeProfile.asset","Assets/Settings/PC_RPAsset.asset","Assets/Settings/UniversalRenderPipelineGlobalSettings.asset","ProjectSettings/ProjectSettings.asset","ProjectSettings/UnityConnectSettings.asset","ProjectSettings/EditorBuildSettings.asset"})
                if (File.Exists(path)) snapshots[path] = File.ReadAllBytes(path);
            try
            {
                Validate();
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{ScenePath}, locationPathName="Builds/GuestServiceM5/MotelGame.app", target=BuildTarget.StandaloneOSX, options=BuildOptions.Development });
                File.WriteAllText(Reports+"/build-validation.txt",report.summary.result+"\nErrors: "+report.summary.totalErrors+"\nWarnings: "+report.summary.totalWarnings+"\n");
                if (report.summary.result!=BuildResult.Succeeded) throw new Exception("M5 build failed.");
            }
            finally { foreach (var pair in snapshots) File.WriteAllBytes(pair.Key,pair.Value); AssetDatabase.Refresh(); }
        }
    }
}
