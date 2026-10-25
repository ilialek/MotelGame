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
using Unity.Netcode.Components;
using MotelGame.Service;
using MotelGame.Interactions;

namespace MotelGame.Editor
{
    public static class MultipleGuestsSetup
    {
        public const string ScenePath="Assets/Game/Scenes/Motel_Blockout_V3.unity";
        public const string Reports="Documentation/MultipleGuestsM7";
        const string PrefabPath="Assets/Game/Prefabs/NetworkGuest.prefab";
        const string Request="Library/MotelMultipleGuestsSetup.request", BuildRequest="Library/MotelPlaytestBuild.request";
        static double nextPoll;
        [InitializeOnLoadMethod] static void Init()=>EditorApplication.update+=Poll;
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<nextPoll)return;nextPoll=EditorApplication.timeSinceStartup+1;
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||File.Exists("Library/MotelServiceRefresh.request"))return;
            if(!File.Exists("Library/MotelMultipleGuestsDoors.request")&&!File.Exists(Request)&&!File.Exists(BuildRequest)&&!File.Exists("Library/MotelMultipleGuestsValidate.request"))return;
            Directory.CreateDirectory(Reports);
            try
            {
                if(File.Exists("Library/MotelMultipleGuestsDoors.request")){File.Delete("Library/MotelMultipleGuestsDoors.request");RepairUpperDoors();}
                if(File.Exists(Request)){var hash=File.ReadAllText(Request).Trim();File.Delete(Request);Install(hash);}
                if(File.Exists("Library/MotelMultipleGuestsValidate.request")){File.Delete("Library/MotelMultipleGuestsValidate.request");Validate();}
                if(File.Exists(BuildRequest)){File.Delete(BuildRequest);PlaytestBuild.Build();}
                if(File.Exists(Reports+"/editor-error.txt"))File.Move(Reports+"/editor-error.txt",Reports+"/resolved-editor-error-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt");
            }
            catch(Exception e){if(File.Exists(BuildRequest))File.Delete(BuildRequest);File.WriteAllText(Reports+"/editor-error.txt",e.ToString());Debug.LogException(e);}
        }
        static string Hash(string p){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(p))).Replace("-","").ToLowerInvariant();}
        static void Set(UnityEngine.Object o,string n,UnityEngine.Object v){var s=new SerializedObject(o);s.FindProperty(n).objectReferenceValue=v;s.ApplyModifiedPropertiesWithoutUndo();}
        static void Array(UnityEngine.Object o,string n,IEnumerable<UnityEngine.Object> values)
        {var s=new SerializedObject(o);var p=s.FindProperty(n);var a=values.ToArray();p.arraySize=a.Length;for(int i=0;i<a.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=a[i];s.ApplyModifiedPropertiesWithoutUndo();}
        static void Text(UnityEngine.Object o,string n,string v){var s=new SerializedObject(o);s.FindProperty(n).stringValue=v;s.ApplyModifiedPropertiesWithoutUndo();}
        static GameObject Child(string name,Transform parent){var g=new GameObject(name);g.transform.SetParent(parent,false);return g;}
        static Vector3 Map(Vector3 p,int index){if(index%2==1)p.x=44.4f-p.x;p.z+=index>=2?5.7f:0;return p;}
        static Quaternion MapRotation(Quaternion q,int index)=>index%2==1?new Quaternion(q.x,-q.y,-q.z,q.w):q;
        static T[] All<T>(Scene scene) where T:Component=>scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).ToArray();
        static Transform Point(Transform parent,string name,Vector3 p,float yaw=0){var g=Child(name,parent);g.transform.SetPositionAndRotation(p,Quaternion.Euler(0,yaw,0));return g.transform;}
        static void Save(Scene scene)
        {
            foreach(var label in All<WorldServiceText>(scene))label.RefreshMaterial();
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);
            foreach(var n in All<NetworkObject>(scene))
            {typeof(NetworkObject).GetMethod("OnValidate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(n,null);if(PrefabUtility.IsPartOfPrefabInstance(n))PrefabUtility.RecordPrefabInstancePropertyModifications(n);}
            EditorSceneManager.SaveScene(scene,ScenePath);
        }
        static void Install(string expected)
        {
            if(Hash(ScenePath)!=expected)throw new Exception("Scene changed after M7 inspection.");
            var scene=SceneManager.GetSceneByPath(ScenePath);var previous=SceneManager.GetActiveScene();bool opened=!scene.IsValid()||!scene.isLoaded;
            if(!opened&&scene.isDirty)throw new Exception("Save unsaved scene edits before installing M7.");
            try
            {
                if(opened)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);SceneManager.SetActiveScene(scene);
                if(All<ServiceRoom>(scene).Length!=1)throw new Exception("M7 already installed or unexpected room configuration.");
                var motel=scene.GetRootGameObjects().Single(g=>g.name=="Motel").transform;
                var source=All<ServiceRoom>(scene).Single();var sourceBed=source.Bed;
                var reception=All<ReceptionDesk>(scene).Single();var requests=All<GuestServiceRequests>(scene).Single();
                var originalGuest=All<GuestLifecycle>(scene).Single();
                var nav=All<NavMeshSurface>(scene).Single();
                var oldPoints=nav.transform.Find("Destinations");
                var sourceDelivery=All<GuestServiceInteraction>(scene).Single(x=>x.Action==GuestServiceAction.DeliverTowel);
                var deliveryLabel=requests.transform.Find("DeliveryLabel");
                var list=new List<ServiceRoom>();
                for(int index=0;index<4;index++)
                {
                    string number=(101+index).ToString();var roomRoot=motel.Find("Rooms/Room"+number);
                    var room=index==0?source:UnityEngine.Object.Instantiate(source.gameObject,roomRoot).GetComponent<ServiceRoom>();
                    room.name="ServiceState";Text(room,"roomNumber",number);
                    if(index>0)
                    {
                        var transforms=room.GetComponentsInChildren<Transform>(true).Where(t=>t!=room.transform).ToArray();
                        var positions=transforms.Select(t=>t.position).ToArray();var rotations=transforms.Select(t=>t.rotation).ToArray();
                        for(int j=0;j<transforms.Length;j++)transforms[j].SetPositionAndRotation(Map(positions[j],index),MapRotation(rotations[j],index));
                        var mattress=roomRoot.Find("Furniture/DoubleBed_mattress");var bed=mattress.gameObject.AddComponent<CleaningSpot>();
                        EditorUtility.CopySerialized(sourceBed,bed);Set(bed,"room",room);Set(bed,"targetCollider",mattress.GetComponent<Collider>());
                        Set(bed,"dirtyVisual",room.transform.Find("UnmadeBedding").gameObject);Set(bed,"cleanVisual",room.transform.Find("MadeBedding").gameObject);
                        Set(room,"bed",bed);Set(room,"guestInRoom",null);
                        foreach(var sign in room.GetComponentsInChildren<TextMesh>(true))sign.text=sign.text.Replace("101",number);
                    }
                    NetworkDoor door;
                    if(index==0)door=roomRoot.GetComponentInChildren<NetworkDoor>(true);
                    else
                    {
                        roomRoot.Find("Door_parked_open_90deg").gameObject.SetActive(false);
                        var doorRoot=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/NetworkDoor.prefab"),roomRoot);
                        doorRoot.name="Room"+number+"Door";
                        doorRoot.transform.SetPositionAndRotation(new Vector3(index%2==0?20.58f:23.82f,0,(index>=2?6.9f:0)+(index%2==0?13.16f:12.14f)),Quaternion.Euler(0,index%2==0?0:180,0));
                        PrefabUtility.RecordPrefabInstancePropertyModifications(doorRoot.transform);door=doorRoot.GetComponent<NetworkDoor>();
                    }
                    var destinations=index==0?oldPoints:Child("Room"+number+"Destinations",nav.transform).transform;
                    var outside=index==0?oldPoints.Find("RoomDoorOutside"):Point(destinations,"RoomDoorOutside",Map(new Vector3(22.2f,0,index>=2?13.85f:12.65f),index),index%2==0?270:90);
                    var inside=index==0?oldPoints.Find("RoomDoorInside"):Point(destinations,"RoomDoorInside",Map(new Vector3(19.1f,0,index>=2?13.85f:12.65f),index),index%2==0?270:90);
                    var idle=index==0?oldPoints.Find("RoomIdle"):Point(destinations,"RoomIdle",Map(new Vector3(18.9f,0,13.65f),index),index%2==0?90:270);
                    var delivery=sourceDelivery;
                    if(index>0)
                    {
                        delivery=UnityEngine.Object.Instantiate(sourceDelivery.gameObject,requests.transform).GetComponent<GuestServiceInteraction>();
                        delivery.name="Room"+number+"Delivery";delivery.transform.position=Map(sourceDelivery.transform.position,index);Set(delivery,"deliveryRoom",room);
                        var label=UnityEngine.Object.Instantiate(deliveryLabel,requests.transform);label.name="Room"+number+"DeliveryLabel";
                        label.SetPositionAndRotation(Map(deliveryLabel.position,index),MapRotation(deliveryLabel.rotation,index));label.GetComponent<TextMesh>().text="ROOM "+number+"\n[E] Deliver towel";
                    }
                    Set(room,"door",door);Set(room,"doorOutside",outside);Set(room,"doorInside",inside);Set(room,"guestIdle",idle);Set(room,"delivery",delivery);
                    list.Add(room);
                }
                AdjustUpperRoomFixtures(scene);
                Array(reception,"rooms",list);Array(requests,"rooms",list);
                ConfigureGuestPrefab();var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                var guests=new List<GuestLifecycle>();var colors=new[]{new Color(.15f,.4f,.55f),new Color(.3f,.55f,.25f),new Color(.65f,.38f,.15f),new Color(.5f,.25f,.55f),new Color(.65f,.6f,.35f)};
                for(int i=0;i<list.Count+1;i++)
                {
                    var guest=i==0?originalGuest:((GameObject)PrefabUtility.InstantiatePrefab(prefab,scene)).GetComponent<GuestLifecycle>();
                    guest.name="NetworkGuest_"+(char)('A'+i);Text(guest,"guestName","Guest "+(char)('A'+i));
                    Set(guest,"arrivalPoint",oldPoints.Find("ParkingArrival"));Set(guest,"receptionPoint",oldPoints.Find("ReceptionWaiting"));Set(guest,"departurePoint",oldPoints.Find("ParkingDeparture"));
                    // New prefab fields are assigned explicitly on the pre-existing instance too.
                    Set(guest,"shirt",guest.transform.Find("HumanoidPlaceholder/Torso").GetComponent<Renderer>());
                    Set(guest,"nameLabel",guest.transform.Find("HumanoidPlaceholder/GuestName").GetComponent<TextMesh>());
                    var serialized=new SerializedObject(guest);serialized.FindProperty("shirtColor").colorValue=colors[i];serialized.ApplyModifiedPropertiesWithoutUndo();
                    guest.GetComponent<NavMeshAgent>().avoidancePriority=20+i*10;
                    guest.GetComponent<NavMeshAgent>().obstacleAvoidanceType=ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                    guest.ApplyIdentity();PrefabUtility.RecordPrefabInstancePropertyModifications(guest);PrefabUtility.RecordPrefabInstancePropertyModifications(guest.GetComponent<NavMeshAgent>());
                    guests.Add(guest);
                }
                Array(reception,"guests",guests);Bake(scene);Save(scene);
                File.WriteAllText(Reports+"/install-validation.txt","Configured Rooms 101–104, five bounded guest instances, one Reception arrival slot and one global phone request.\n");
            }
            finally{if(opened&&scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
            Validate();
        }
        static void AdjustUpperRoomFixtures(Scene scene)
        {
            // The upper pair's architectural door openings are 1.2 m farther along
            // the corridor than a straight translation of the lower rooms.
            foreach(var room in All<ServiceRoom>(scene).Where(r=>r.RoomNumber=="103"||r.RoomNumber=="104"))
            {
                bool east=room.RoomNumber=="104";
                room.Door.transform.SetPositionAndRotation(new Vector3(east?23.82f:20.58f,0,east?19.04f:20.06f),Quaternion.Euler(0,east?180:0,0));
                PrefabUtility.RecordPrefabInstancePropertyModifications(room.Door.transform);
                room.DoorOutside.position=new Vector3(22.2f,0,19.55f);
                room.DoorInside.position=new Vector3(east?25.3f:19.1f,0,19.55f);
                foreach(var t in room.transform.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("Status") || t.name.Contains("Sign")))
                    if(t.GetComponent<TextMesh>()!=null){var p=t.position;p.z=18.45f;t.position=p;}
                var tray=room.Delivery.transform;var p2=tray.position;p2.z=18.45f;tray.position=p2;
                var label=tray.parent.Find("Room"+room.RoomNumber+"DeliveryLabel");if(label!=null){p2=label.position;p2.z=18.45f;label.position=p2;}
            }
        }
        static void RepairUpperDoors()
        {
            var scene=SceneManager.GetSceneByPath(ScenePath);bool opened=!scene.IsValid()||!scene.isLoaded;
            if(!opened&&scene.isDirty)throw new Exception("Save scene changes before configuring doors.");
            try{if(opened)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);AdjustUpperRoomFixtures(scene);Bake(scene);Save(scene);}
            finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
            Validate();
        }
        static void ConfigureGuestPrefab()
        {
            var root=PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var guest=root.GetComponent<GuestLifecycle>();var label=Child("GuestName",root.transform.Find("HumanoidPlaceholder"));label.transform.localPosition=new Vector3(0,2.05f,0);
                var text=label.AddComponent<TextMesh>();text.fontSize=64;text.characterSize=.009f;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;
                text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.text="Guest";text.color=new Color(.9f,.86f,.7f);
                var world=label.AddComponent<WorldServiceText>();Set(world,"textShader",AssetDatabase.LoadAssetAtPath<Shader>("Assets/Game/Shaders/WorldServiceText.shader"));world.RefreshMaterial();
                Set(guest,"nameLabel",text);Set(guest,"shirt",root.transform.Find("HumanoidPlaceholder/Torso").GetComponent<Renderer>());
                root.GetComponent<NavMeshAgent>().radius=.34f;
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        static void Bake(Scene scene)
        {
            Physics.SyncTransforms();var surface=All<NavMeshSurface>(scene).Single();var sources=new List<NavMeshBuildSource>();
            var markups=All<NetworkDoor>(scene).Select(d=>new NavMeshBuildMarkup{root=d.transform,ignoreFromBuild=true})
                .Concat(All<GuestLifecycle>(scene).Select(g=>new NavMeshBuildMarkup{root=g.transform,ignoreFromBuild=true})).ToList();
            foreach(var root in scene.GetRootGameObjects())
            {var batch=new List<NavMeshBuildSource>();UnityEngine.AI.NavMeshBuilder.CollectSources(root.transform,~0,NavMeshCollectGeometry.PhysicsColliders,0,markups,batch);sources.AddRange(batch);}
            var settings=NavMesh.GetSettingsByID(0);settings.agentRadius=.3f;settings.agentHeight=1.8f;settings.agentClimb=.15f;settings.overrideVoxelSize=true;settings.voxelSize=.04f;settings.minRegionArea=.1f;
            var data=UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings,sources,new Bounds(new Vector3(12,2,5),new Vector3(40,12,50)),Vector3.zero,Quaternion.identity);
            if(data==null)throw new Exception("M7 NavMesh bake failed.");
            surface.RemoveData();var saved=surface.navMeshData;data.name=saved.name;EditorUtility.CopySerialized(data,saved);UnityEngine.Object.DestroyImmediate(data);EditorUtility.SetDirty(saved);surface.AddData();AssetDatabase.SaveAssets();
        }
        [MenuItem("Tools/Motel/Rebake Playtest Navigation")]
        public static void RebakeNavigation()
        {
            var scene=SceneManager.GetSceneByPath(ScenePath);bool opened=!scene.IsValid()||!scene.isLoaded;
            if(!opened&&scene.isDirty)throw new Exception("Save scene changes before navigation rebuild.");
            try{if(opened)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);Bake(scene);Save(scene);}
            finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
            Validate();
        }
        [MenuItem("Tools/Motel/Validate Multiple Guests M7")]
        public static void Validate()
        {
            Directory.CreateDirectory(Reports);var scene=EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var rooms=All<ServiceRoom>(scene).OrderBy(r=>r.RoomNumber).ToArray();var guests=All<GuestLifecycle>(scene);var reception=All<ReceptionDesk>(scene).Single();var service=All<GuestServiceRequests>(scene).Single();
                if(rooms.Length!=4 || guests.Length!=5 || !reception.ConfigurationValid() || !service.ConfigurationValid())throw new Exception("Invalid room/guest/reception/service counts or references.");
                foreach(var room in rooms)if(!room.ConfigurationValid(out var error))throw new Exception(room.RoomNumber+": "+error);
                foreach(var guest in guests)if(!guest.ConfigurationValid() || guest.GetComponent<NetworkTransform>().AuthorityMode!=NetworkTransform.AuthorityModes.Server)throw new Exception("Invalid guest: "+guest.name);
                foreach(var t in All<Transform>(scene))if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)throw new Exception("Missing script: "+t.name);
                foreach(var room in rooms)if(!reception.Rooms.Contains(room) || !service.Rooms.Contains(room))throw new Exception("Room registration missing.");
                var surface=All<NavMeshSurface>(scene).Single();surface.AddData();var routes=new List<string>();
                foreach(var pair in new[]{(guests[0].ArrivalPoint,guests[0].ReceptionPoint)})ValidatePath(pair.Item1,pair.Item2,routes);
                foreach(var room in rooms)
                {
                    foreach(var pair in new[]{(guests[0].ReceptionPoint,room.DoorOutside),(room.DoorOutside,room.DoorInside),(room.DoorInside,room.GuestIdle),(room.GuestIdle,room.DoorInside),(room.DoorInside,room.DoorOutside),(room.DoorOutside,guests[0].DeparturePoint)})ValidatePath(pair.Item1,pair.Item2,routes,room.RoomNumber+": ");
                    ValidateDoor(scene,room);
                }
                File.WriteAllLines(Reports+"/navigation-validation.txt",routes);
                var hashes=All<NetworkObject>(scene).Select(n=>new SerializedObject(n).FindProperty("GlobalObjectIdHash").uintValue).ToArray();
                if(hashes.Any(h=>h==0)||hashes.Distinct().Count()!=hashes.Length)throw new Exception("Duplicate/missing NGO scene identity.");
                File.WriteAllText(Reports+"/setup-validation.txt","PASS: Saved scene reopened; 4 independent rooms, 5 configured server NPCs, one reception/service system; all references and network scene identities valid; no missing scripts.\nAll four door sweeps and CharacterController passages clear.\nScene SHA256: "+Hash(ScenePath)+"\n");
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        static void ValidatePath(Transform a,Transform b,List<string> lines,string prefix="")
        {
            if(!NavMesh.SamplePosition(a.position,out var start,.35f,NavMesh.AllAreas)||!NavMesh.SamplePosition(b.position,out var end,.35f,NavMesh.AllAreas))throw new Exception("Off NavMesh: "+prefix+a.name+" / "+b.name);
            var path=new NavMeshPath();if(!NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)throw new Exception("Incomplete route: "+prefix+a.name+" -> "+b.name);
            float length=0;for(int i=1;i<path.corners.Length;i++)length+=Vector3.Distance(path.corners[i-1],path.corners[i]);lines.Add(prefix+a.name+" -> "+b.name+": complete, "+length.ToString("F2")+" m");
        }
        static void ValidateDoor(Scene scene,ServiceRoom room)
        {
            var door=room.Door;var hinge=door.transform.Find("Hinge");var leaf=hinge.GetComponentInChildren<BoxCollider>();
            var obstacles=All<Collider>(scene).Where(c=>c.enabled&&c.gameObject.activeInHierarchy&&!c.isTrigger&&!c.transform.IsChildOf(door.transform)&&c.GetComponentInParent<GuestLifecycle>()==null).ToArray();
            for(int angle=0;angle<=90;angle+=2)
            {
                hinge.localRotation=Quaternion.Euler(0,angle,0);Physics.SyncTransforms();
                foreach(var c in obstacles)if(Physics.ComputePenetration(leaf,leaf.transform.position,leaf.transform.rotation,c,c.transform.position,c.transform.rotation,out _,out float depth)&&depth>.002f)
                    throw new Exception("Door "+room.RoomNumber+" intersects "+c.name+" at "+angle+" depth="+depth);
            }
            var probe=new GameObject("DoorClearanceProbe");SceneManager.MoveGameObjectToScene(probe,scene);var cc=probe.AddComponent<CharacterController>();cc.height=1.8f;cc.radius=.3f;cc.center=Vector3.up*.9f;cc.skinWidth=.03f;cc.stepOffset=.2f;cc.minMoveDistance=0;
            try
            {
                foreach(bool open in new[]{false,true})
                {
                    cc.enabled=false;probe.transform.position=room.DoorOutside.position+Vector3.up*.05f;cc.enabled=true;hinge.localRotation=Quaternion.Euler(0,open?90:0,0);Physics.SyncTransforms();
                    Vector3 direction=(room.DoorInside.position-room.DoorOutside.position).normalized;
                    for(int i=0;i<60;i++)cc.Move(direction*(4f/60)+Vector3.down*(2f/60));
                    float remaining=Vector3.Dot(room.DoorInside.position-probe.transform.position,direction);
                    if(open&&remaining>.15f)throw new Exception("Room "+room.RoomNumber+" open door blocks player.");
                    if(!open&&remaining<.7f)throw new Exception("Room "+room.RoomNumber+" closed door does not block player.");
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(probe);hinge.localRotation=Quaternion.identity;}
        }
    }
}
