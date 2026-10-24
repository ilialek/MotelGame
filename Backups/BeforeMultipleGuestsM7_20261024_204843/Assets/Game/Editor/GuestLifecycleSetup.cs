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
using UnityEngine.AI;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using MotelGame.Service;
using MotelGame.Interactions;

namespace MotelGame.Editor
{
    public static class GuestLifecycleSetup
    {
        const string ScenePath="Assets/Game/Scenes/Motel_Blockout_V3.unity";
        const string PrefabPath="Assets/Game/Prefabs/NetworkGuest.prefab";
        const string NavPath="Assets/Game/Navigation/MotelGuestNavMesh.asset";
        const string Reports="Documentation/GuestLifecycleM6";
        const string SetupRequest="Library/MotelLifecycleSetup.request", BuildRequest="Library/MotelLifecycleBuild.request", ValidateRequest="Library/MotelLifecycleValidate.request", NavigationRequest="Library/MotelLifecycleNavigation.request";
        static double nextPoll;
        [InitializeOnLoadMethod] static void Initialize() => EditorApplication.update+=Poll;
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<nextPoll)return;nextPoll=EditorApplication.timeSinceStartup+1;
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||File.Exists("Library/MotelServiceRefresh.request"))return;
            if(!File.Exists(SetupRequest)&&!File.Exists(BuildRequest)&&!File.Exists(ValidateRequest)&&!File.Exists(NavigationRequest))return;
            Directory.CreateDirectory(Reports);
            try
            {
                if(File.Exists(SetupRequest)){string hash=File.ReadAllText(SetupRequest).Trim();File.Delete(SetupRequest);Install(hash);}
                if(File.Exists(NavigationRequest)){File.Delete(NavigationRequest);RebakeNavigation();}
                if(File.Exists(ValidateRequest)){File.Delete(ValidateRequest);Validate();}
                if(File.Exists(BuildRequest)){File.Delete(BuildRequest);Build();}
            }
            catch(Exception e){if(File.Exists(BuildRequest))File.Delete(BuildRequest);File.WriteAllText(Reports+"/editor-error.txt",e.ToString());Debug.LogException(e);}
        }
        static string Hash(string path){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        static void Set(UnityEngine.Object o,string name,UnityEngine.Object value){var s=new SerializedObject(o);s.FindProperty(name).objectReferenceValue=value;s.ApplyModifiedPropertiesWithoutUndo();}
        static GameObject Child(string name,Transform parent){var g=new GameObject(name);g.transform.SetParent(parent,false);return g;}
        static Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/MotelBlockoutV3/"+name+".mat");
        static void Part(string name,Transform parent,PrimitiveType type,Vector3 pos,Vector3 scale,string material)
        {
            var g=GameObject.CreatePrimitive(type);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=pos;g.transform.localScale=scale;
            g.GetComponent<Renderer>().sharedMaterial=Mat(material);UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
        }
        static GameObject CreatePrefab()
        {
            if(File.Exists(PrefabPath))throw new Exception("Guest prefab already exists; refusing to overwrite.");
            var root=new GameObject("NetworkGuest");
            try
            {
                var n=root.AddComponent<NetworkObject>();n.AutoObjectParentSync=false;
                var nt=root.AddComponent<NetworkTransform>();nt.AuthorityMode=NetworkTransform.AuthorityModes.Server;nt.Interpolate=true;
                nt.SyncRotAngleX=false;nt.SyncRotAngleZ=false;nt.SyncScaleX=false;nt.SyncScaleY=false;nt.SyncScaleZ=false;
                nt.PositionThreshold=.01f;nt.RotAngleThreshold=.5f;
                var a=root.AddComponent<NavMeshAgent>();a.agentTypeID=0;a.radius=.3f;a.height=1.8f;a.speed=1.6f;a.acceleration=6;a.angularSpeed=240;
                a.stoppingDistance=.08f;a.autoBraking=true;a.autoTraverseOffMeshLink=false;a.enabled=false;
                var body=root.AddComponent<CapsuleCollider>();body.radius=.28f;body.height=1.8f;body.center=new Vector3(0,.9f,0);body.enabled=false;
                var visual=Child("HumanoidPlaceholder",root.transform);
                Part("Torso",visual.transform,PrimitiveType.Cube,new Vector3(0,1.15f,0),new Vector3(.42f,.58f,.25f),"Scale");
                Part("Head",visual.transform,PrimitiveType.Sphere,new Vector3(0,1.64f,0),new Vector3(.29f,.32f,.27f),"Linen");
                Part("NoseForward",visual.transform,PrimitiveType.Cube,new Vector3(0,1.64f,.15f),new Vector3(.07f,.08f,.08f),"Linen");
                foreach(float side in new[]{-1f,1f})
                {
                    Part(side<0?"LeftArm":"RightArm",visual.transform,PrimitiveType.Cube,new Vector3(side*.275f,1.13f,0),new Vector3(.115f,.55f,.16f),"Scale");
                    Part(side<0?"LeftHand":"RightHand",visual.transform,PrimitiveType.Cube,new Vector3(side*.275f,.81f,0),new Vector3(.1f,.13f,.13f),"Linen");
                    Part(side<0?"LeftLeg":"RightLeg",visual.transform,PrimitiveType.Cube,new Vector3(side*.12f,.48f,0),new Vector3(.17f,.74f,.2f),"Furniture");
                    Part(side<0?"LeftFoot":"RightFoot",visual.transform,PrimitiveType.Cube,new Vector3(side*.12f,.06f,.045f),new Vector3(.19f,.12f,.3f),"Metal");
                }
                var guest=root.AddComponent<GuestLifecycle>();Set(guest,"visual",visual);Set(guest,"body",body);visual.SetActive(false);
                return PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        static Transform Point(Transform parent,string name,Vector3 pos,float yaw=0)
        {var g=Child(name,parent);g.transform.position=pos;g.transform.rotation=Quaternion.Euler(0,yaw,0);return g.transform;}
        static void Install(string expected)
        {
            if(Hash(ScenePath)!=expected)throw new Exception("Scene changed after inspection.");
            var scene=SceneManager.GetSceneByPath(ScenePath);var previous=SceneManager.GetActiveScene();bool opened=!scene.IsValid()||!scene.isLoaded;
            if(!opened&&scene.isDirty)throw new Exception("Save unsaved scene edits before M6 setup.");
            try
            {
                if(opened)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);SceneManager.SetActiveScene(scene);
                var roots=scene.GetRootGameObjects();if(roots.Any(g=>g.name=="GuestLifecycleNavigation"))throw new Exception("M6 already installed.");
                var room=roots.SelectMany(g=>g.GetComponentsInChildren<ServiceRoom>(true)).Single();
                var reception=roots.SelectMany(g=>g.GetComponentsInChildren<ReceptionDesk>(true)).Single();
                var requests=roots.SelectMany(g=>g.GetComponentsInChildren<GuestServiceRequests>(true)).Single();
                var door=roots.SelectMany(g=>g.GetComponentsInChildren<NetworkDoor>(true)).Single();
                var navRoot=new GameObject("GuestLifecycleNavigation");var surface=navRoot.AddComponent<NavMeshSurface>();surface.agentTypeID=0;
                var points=Child("Destinations",navRoot.transform).transform;
                var arrival=Point(points,"ParkingArrival",new Vector3(8,0,-10));
                var waiting=Point(points,"ReceptionWaiting",new Vector3(8.8f,0,6),90);
                var outside=Point(points,"RoomDoorOutside",new Vector3(22.2f,0,12.65f),270);
                var inside=Point(points,"RoomDoorInside",new Vector3(19.1f,0,12.65f),270);
                var idle=Point(points,"RoomIdle",new Vector3(18.9f,0,13.65f),90);
                var exit=Point(points,"ParkingDeparture",new Vector3(2,0,-12),180);
                var prefab=CreatePrefab();var guestRoot=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);guestRoot.transform.position=arrival.position;
                var guest=guestRoot.GetComponent<GuestLifecycle>();
                Set(guest,"room",room);Set(guest,"door",door);Set(guest,"arrivalPoint",arrival);Set(guest,"receptionPoint",waiting);
                Set(guest,"doorOutside",outside);Set(guest,"doorInside",inside);Set(guest,"roomIdlePoint",idle);Set(guest,"departurePoint",exit);
                Set(reception,"guest",guest);Set(room,"physicalGuest",guest);Set(requests,"guest",guest);
                reception.WaitingGuest.SetActive(false);room.GuestInRoom.SetActive(false);
                DisableLegacyQueue(scene);
                var oldLabel=new SerializedObject(reception).FindProperty("guestLabel").objectReferenceValue as TextMesh;oldLabel.gameObject.SetActive(false);
                Bake(scene,surface,guest,door);
                PrefabUtility.RecordPrefabInstancePropertyModifications(guest);PrefabUtility.RecordPrefabInstancePropertyModifications(guestRoot.transform);
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);
                guest.NetworkObject.SendMessage("OnValidate",SendMessageOptions.DontRequireReceiver);EditorSceneManager.SaveScene(scene,ScenePath);
                File.WriteAllText(Reports+"/install-validation.txt","Installed physical guest, route references and baked navigation in existing motel scene.\n");
                Validate();
            }
            finally{if(opened&&scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
        }
        static void Bake(Scene scene,NavMeshSurface surface,GuestLifecycle guest,NetworkDoor door)
        {
            Physics.SyncTransforms();var sources=new List<NavMeshBuildSource>();
            var markups=new List<NavMeshBuildMarkup>{new NavMeshBuildMarkup{root=door.transform,ignoreFromBuild=true},new NavMeshBuildMarkup{root=guest.transform,ignoreFromBuild=true}};
            foreach(var root in scene.GetRootGameObjects())
            {
                var batch=new List<NavMeshBuildSource>();UnityEngine.AI.NavMeshBuilder.CollectSources(root.transform,~0,NavMeshCollectGeometry.PhysicsColliders,0,markups,batch);
                sources.AddRange(batch);
            }
            // One fixed profile for this baked NPC surface; no global project agent settings changed.
            var settings=NavMesh.GetSettingsByID(0);settings.agentRadius=.3f;settings.agentHeight=1.8f;settings.agentClimb=.15f;
            settings.overrideVoxelSize=true;settings.voxelSize=.04f;settings.minRegionArea=.1f;
            var data=UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings,sources,new Bounds(new Vector3(12,2,5),new Vector3(40,12,50)),Vector3.zero,Quaternion.identity);
            if(data==null)throw new Exception("NavMesh bake failed.");
            Directory.CreateDirectory("Assets/Game/Navigation");AssetDatabase.Refresh();
            data.name="MotelGuestNavMesh";surface.RemoveData();
            var existing=AssetDatabase.LoadAssetAtPath<NavMeshData>(NavPath);
            if(existing==null)AssetDatabase.CreateAsset(data,NavPath);
            else {EditorUtility.CopySerialized(data,existing);UnityEngine.Object.DestroyImmediate(data);data=existing;EditorUtility.SetDirty(data);}
            surface.navMeshData=data;surface.AddData();
            EditorUtility.SetDirty(surface);AssetDatabase.SaveAssets();
        }
        static void DisableLegacyQueue(Scene scene)
        {
            // The blockout had an extra static queue-person scale marker in addition to
            // M4's waiting guest. Hide that legacy guest too; keep the object for recovery.
            var queue=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true))
                .SingleOrDefault(t=>t.name=="QueueGuest_1.8m");
            if(queue!=null)queue.gameObject.SetActive(false);
        }
        [MenuItem("Tools/Motel/Rebake Guest Navigation M6")]
        public static void RebakeNavigation()
        {
            var scene=SceneManager.GetSceneByPath(ScenePath);var previous=SceneManager.GetActiveScene();bool opened=!scene.IsValid()||!scene.isLoaded;
            if(!opened&&scene.isDirty)throw new Exception("Save scene edits before rebuilding M6 navigation.");
            try
            {
                if(opened)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);SceneManager.SetActiveScene(scene);
                DisableLegacyQueue(scene);
                var roots=scene.GetRootGameObjects();var guest=roots.SelectMany(g=>g.GetComponentsInChildren<GuestLifecycle>(true)).Single();
                var door=roots.SelectMany(g=>g.GetComponentsInChildren<NetworkDoor>(true)).Single();
                var surface=roots.SelectMany(g=>g.GetComponentsInChildren<NavMeshSurface>(true)).Single();
                Bake(scene,surface,guest,door);
                // Match the existing labels' transient URP materials before Unity serializes
                // the scene; loading TextMesh alone assigns a built-in font material.
                foreach(var label in roots.SelectMany(g=>g.GetComponentsInChildren<WorldServiceText>(true)))label.RefreshMaterial();
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);
            }
            finally{if(opened&&scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
            Validate();
        }
        [MenuItem("Tools/Motel/Validate Guest Lifecycle M6")]
        public static void Validate()
        {
            Directory.CreateDirectory(Reports);var scene=EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var roots=scene.GetRootGameObjects();var guest=roots.SelectMany(g=>g.GetComponentsInChildren<GuestLifecycle>(true)).Single();
                if(!guest.ConfigurationValid())throw new Exception("Missing M6 scene references.");
                var nt=guest.GetComponent<NetworkTransform>();if(nt.AuthorityMode!=NetworkTransform.AuthorityModes.Server)throw new Exception("NPC must be server authoritative.");
                var surface=roots.SelectMany(g=>g.GetComponentsInChildren<NavMeshSurface>(true)).Single();
                if(surface.navMeshData==null)throw new Exception("No saved NavMesh.");surface.AddData();
                if(!guest.Room.ConfigurationValid(out var error))throw new Exception(error);
                if(!roots.SelectMany(g=>g.GetComponentsInChildren<GuestServiceRequests>(true)).Single().ConfigurationValid())throw new Exception("M5 references invalid.");
                foreach(var t in roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)))if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)throw new Exception("Missing script: "+t.name);
                var pairs=new[]{(guest.ArrivalPoint,guest.ReceptionPoint),(guest.ReceptionPoint,guest.DoorOutside),(guest.DoorOutside,guest.DoorInside),(guest.DoorInside,guest.RoomIdlePoint),(guest.RoomIdlePoint,guest.DoorInside),(guest.DoorInside,guest.DoorOutside),(guest.DoorOutside,guest.DeparturePoint),(guest.ReceptionPoint,guest.DeparturePoint)};
                var lines=new List<string>();
                foreach(var pair in pairs)
                {
                    if(!NavMesh.SamplePosition(pair.Item1.position,out var start,.35f,NavMesh.AllAreas)||!NavMesh.SamplePosition(pair.Item2.position,out var end,.35f,NavMesh.AllAreas))throw new Exception("Off NavMesh: "+pair.Item1.name+" / "+pair.Item2.name);
                    var path=new NavMeshPath();if(!NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)throw new Exception("Incomplete path: "+pair.Item1.name+" -> "+pair.Item2.name);
                    float length=0;for(int i=1;i<path.corners.Length;i++)length+=Vector3.Distance(path.corners[i-1],path.corners[i]);
                    lines.Add(pair.Item1.name+" -> "+pair.Item2.name+": Complete, "+length.ToString("F2")+" m; corners "+string.Join("; ",path.corners.Select(c=>c.ToString("F2"))));
                }
                File.WriteAllLines(Reports+"/navigation-validation.txt",lines);
                Capture(scene,guest);
                File.WriteAllText(Reports+"/setup-validation.txt","PASS: Saved scene reopened, single physical NPC, server NetworkTransform, all room/M5/navigation references, no missing scripts.\nAll arrival, room, departure paths complete.\nScene SHA256: "+Hash(ScenePath)+"\n");
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        static void Capture(Scene scene,GuestLifecycle guest)
        {
            var roots=scene.GetRootGameObjects();foreach(var t in roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)))t.gameObject.layer=30;
            foreach(var light in roots.SelectMany(g=>g.GetComponentsInChildren<Light>(true)))light.cullingMask=1<<30;
            foreach(var label in roots.SelectMany(g=>g.GetComponentsInChildren<WorldServiceText>(true)))label.RefreshMaterial();
            var camera=roots.SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First();camera.scene=scene;camera.cullingMask=1<<30;camera.enabled=false;camera.fieldOfView=60;
            guest.transform.Find("HumanoidPlaceholder").gameObject.SetActive(true);
            foreach(var point in new[]{guest.ArrivalPoint,guest.ReceptionPoint,guest.RoomIdlePoint})
            {
                guest.transform.SetPositionAndRotation(point.position,point.rotation);
                camera.transform.position=point.position+(point==guest.RoomIdlePoint?new Vector3(.8f,1.65f,1.15f):new Vector3(2,1.65f,2));camera.transform.LookAt(point.position+Vector3.up);
                var rt=new RenderTexture(960,640,24);rt.Create();var old=RenderTexture.active;camera.targetTexture=rt;
                try{camera.Render();RenderTexture.active=rt;var image=new Texture2D(960,640,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,640),0,0);image.Apply();File.WriteAllBytes(Reports+"/preview-"+point.name+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);}
                finally{camera.targetTexture=null;RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
            }
        }
        [MenuItem("Tools/Motel/Build Guest Lifecycle M6")]
        public static void Build()
        {
            var snapshots=new Dictionary<string,byte[]>();foreach(var p in new[]{"Assets/Settings/Mobile_RPAsset.asset","Assets/Settings/DefaultVolumeProfile.asset","Assets/Settings/PC_RPAsset.asset","Assets/Settings/UniversalRenderPipelineGlobalSettings.asset","ProjectSettings/ProjectSettings.asset","ProjectSettings/UnityConnectSettings.asset","ProjectSettings/EditorBuildSettings.asset"})if(File.Exists(p))snapshots[p]=File.ReadAllBytes(p);
            try
            {
                Validate();var r=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName="Builds/GuestLifecycleM6/MotelGame.app",target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
                File.WriteAllText(Reports+"/build-validation.txt",r.summary.result+"\nErrors: "+r.summary.totalErrors+"\nWarnings: "+r.summary.totalWarnings+"\n");
                if(r.summary.result!=BuildResult.Succeeded)throw new Exception("M6 build failed.");
            }
            finally{foreach(var pair in snapshots)File.WriteAllBytes(pair.Key,pair.Value);AssetDatabase.Refresh();}
        }
    }
}
