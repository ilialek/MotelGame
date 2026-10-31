using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Netcode;
using Unity.AI.Navigation;
using MotelGame.Service;
using MotelGame.Horror;
using MotelGame.Inspection;
using MotelGame.Interactions;
using MotelGame.Networking;

namespace MotelGame.Editor
{
    public static partial class MotelLayoutV4
    {
        static readonly Vector3 LobbyOffset=new(15.4f,0,-14.4f);
        static ServiceRoom[] rooms;
        static ReceptionDesk reception;
        static NightShiftController shift;
        static BaggageInspection inspection;
        static MimicHunt hunt;
        static Transform Find(Scene s,string p)=>All<Transform>(s).First(t=>PathOf(t)==p);
        static void Set(UnityEngine.Object obj,string field,UnityEngine.Object value)
        {var so=new SerializedObject(obj);so.FindProperty(field).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
        static void Value(UnityEngine.Object obj,string field,object value)
        {
            var so=new SerializedObject(obj);var p=so.FindProperty(field);
            if(value is int i)p.intValue=i;else if(value is bool b)p.boolValue=b;else if(value is float f)p.floatValue=f;else if(value is string s)p.stringValue=s;else if(value is Vector3 v)p.vector3Value=v;
            else throw new ArgumentException(field);so.ApplyModifiedPropertiesWithoutUndo();
        }
        static T Ref<T>(UnityEngine.Object obj,string field) where T:UnityEngine.Object=>new SerializedObject(obj).FindProperty(field).objectReferenceValue as T;
        static void Items(UnityEngine.Object obj,string field,IEnumerable<UnityEngine.Object> values)
        {
            var so=new SerializedObject(obj);var p=so.FindProperty(field);var a=values.ToArray();p.arraySize=a.Length;
            for(int i=0;i<a.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=a[i];so.ApplyModifiedPropertiesWithoutUndo();
        }
        static void Keep(Transform t,Transform parent,Vector3 delta=default){t.SetParent(parent,true);t.position+=delta;}
        static void Delete(UnityEngine.Object obj){if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);}
        static void Position(Transform t,Vector3 p,float yaw=0){t.SetPositionAndRotation(p,Quaternion.Euler(0,yaw,0));}
        static void Unpack(Scene s)
        {
            foreach(var g in s.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject).Where(PrefabUtility.IsOutermostPrefabInstanceRoot).ToArray())
                if(g!=null&&PrefabUtility.IsPartOfPrefabInstance(g))PrefabUtility.UnpackPrefabInstance(g,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        }
        [MenuItem("Tools/Motel/Level V4/Rebuild from preserved M15")]
        public static void Install()
        {
            if(SceneManager.GetActiveScene().isDirty)throw new Exception("Save your current scene before rebuilding V4.");
            Directory.CreateDirectory(Reports);Directory.CreateDirectory("Assets/Game/Navigation");
            var s=EditorSceneManager.OpenScene(OldScene);
            if(!EditorSceneManager.SaveScene(s,ScenePath))throw new Exception("Cannot create V4 scene.");
            Unpack(s); // Only the new scene instances are unpacked. Source prefabs/assets remain untouched.
            var oldRoots=s.GetRootGameObjects();
            level=Group("Motel_Layout_V4",null);architecture=Group("01_Architecture",level);outdoors=Group("02_Exterior",level);furnishings=Group("03_Furnishings",level);gameplay=Group("04_Gameplay",level);
            reception=All<ReceptionDesk>(s).Single();shift=All<NightShiftController>(s).Single();inspection=All<BaggageInspection>(s).Single();hunt=All<MimicHunt>(s).Single();
            var service=All<GuestServiceRequests>(s).Single();var computer=inspection.Computer;var surveillance=All<VentSurveillance>(s).Single();
            var originalRooms=reception.Rooms.ToArray();var originalGuests=reception.Guests.ToArray();
            // Gather one complete configured room, including formerly external components, before duplication.
            var source=Find(s,"Motel/Rooms/Room101");var sr=originalRooms[0];var breakerSource=sr.Breaker;var oldVents=All<VentNode>(s).First().transform.parent;var canonical=Group("RoomTemplate",level);
            foreach(var t in new[]{sr.DoorOutside.parent,sr.Delivery.transform,sr.Television.transform,sr.BagPickup.transform,inspection.RoomBagSpots[0],Ref<GameObject>(sr,"guestInRoom").transform,Ref<Light>(sr,"controlledLights.Array.data[0]").transform})
                Keep(t,source);
            var vent=All<VentNode>(s).Single(n=>n.room==sr);Keep(vent.transform,source);
            foreach(var name in new[]{"Structure","Ceilings","Windows","Signs","RoomNumberPlaque"})Delete(source.Find(name)?.gameObject);
            foreach(var t in source.Find("PizzaDoggyDecor").Cast<Transform>().Where(t=>t.name.Contains("Curtain")||t.name.Contains("curtain")).ToArray())Delete(t.gameObject);
            doorPrototype=UnityEngine.Object.Instantiate(sr.Door.gameObject,level);doorPrototype.name="DoorTemplate";doorPrototype.SetActive(false);Delete(sr.Door.gameObject);
            Keep(source,canonical);source.localPosition=new Vector3(-10,0,21.6f);source.localRotation=Quaternion.Euler(0,90,0);source.name="ReusedRoomContents";
            // Guest-door placement changes; furniture remains at real-world scale.
            var wardrobe=source.Find("Furniture/Wardrobe");wardrobe.position=new Vector3(.38f,1.05f,4.3f);wardrobe.rotation=Quaternion.identity;
            var roomGroup=Group("GuestRooms",gameplay);rooms=new ServiceRoom[6];
            for(int i=0;i<6;i++)
            {
                int num=101+i;var root=UnityEngine.Object.Instantiate(canonical.gameObject,roomGroup).transform;root.name="Room"+num;
                var r=root.GetComponentInChildren<ServiceRoom>();rooms[i]=r;
                var a=Door("Room"+num+"_CorridorDoor",root,new Vector3(3.16f,0,-.1f),90);
                var b=Door("Room"+num+"_CourtyardDoor",root,new Vector3(.49f,0,6.1f),270);
                Set(r,"door",a);Set(r,"courtyardDoor",b);Value(r,"roomNumber",num.ToString());Value(r,"navigationArea",num-98);
                Position(r.DoorOutside,new Vector3(2.65f,0,-1.15f),0);
                Position(r.DoorInside,new Vector3(2.65f,0,1.7f),180);
                Position(r.GuestIdle,new Vector3(3.8f,0,2.9f),270);
                var bag=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Room101Baggage");bag.name="Room"+num+"Baggage";Position(bag,new Vector3(3.8f,.03f,3.6f));
                Position(r.BagPickup.transform,new Vector3(4.95f,.02f,3.3f));
                // Delivery plate stays on the public face of the corridor doorway.
                Position(r.Delivery.transform,new Vector3(3.6f,1.05f,-.28f),90);
                var signs=new SerializedObject(r).FindProperty("statusSigns");
                Position(((TextMesh)signs.GetArrayElementAtIndex(0).objectReferenceValue).transform,new Vector3(3.7f,2.1f,-.22f),0);
                Position(((TextMesh)signs.GetArrayElementAtIndex(1).objectReferenceValue).transform,new Vector3(3.7f,2.1f,.03f),180);
                foreach(var text in root.GetComponentsInChildren<TextMesh>(true))text.text=text.text.Replace("101",num.ToString());
                var n=root.GetComponentInChildren<VentNode>();n.room=r;n.index=i;n.looseFan=num==104;
                // The former harmless rotor belongs to 104; create the same simple rotor when absent.
                if(n.looseFan&&n.fan==null){n.fan=Group("LooseFanRotor",n.transform,new Vector3(0,.45f,-.1f));for(int blade=0;blade<3;blade++){var v=Box("FanBlade",n.fan,new Vector3(0,.1f,0),new Vector3(.12f,.37f,.025f),Metal,false);v.transform.localRotation=Quaternion.Euler(0,0,blade*120);}}
                var light=Ref<Light>(r,"controlledLights.Array.data[0]");Position(light.transform,new Vector3(2.8f,2.45f,3));light.range=6.5f;light.intensity=2.1f;
                Label(num.ToString(),root,new Vector3(2.65f,2.5f,-.22f),0,.04f);
                Label(num.ToString(),root,new Vector3(1,2.5f,6.22f),180,.04f);
                if(num<=103)root.SetPositionAndRotation(new Vector3((103-num)*5.8f,0,0),Quaternion.identity);
                else root.SetPositionAndRotation(new Vector3((num-104)*5.8f+5.6f,0,23.4f),Quaternion.Euler(0,180,0));
            }
            Delete(canonical.gameObject);
            // Reuse the entire working registration setup and its references together.
            var lobby=Group("LobbyFurniture",furnishings);
            Keep(Find(s,"Motel/ReceptionDesk"),lobby,LobbyOffset);
            var waitingArea=Find(s,"Motel/Lobby/WaitingArea");Keep(waitingArea,lobby,new Vector3(18.2f,0,-15.4f));
            foreach(Transform t in waitingArea)
                t.position+=t.name.StartsWith("Sofa_A")?new Vector3(-.75f,0,-.8f):t.name.StartsWith("Sofa_B")?new Vector3(-.4f,0,-5.5f):t.name.StartsWith("Coffee")?new Vector3(-.5f,0,-.8f):new Vector3(0,0,.4f);
            foreach(var name in new[]{"VendingMachine","VendingDisplay","EntryMat"})Keep(Find(s,"Motel/Lobby/"+name),lobby,LobbyOffset);
            // Relevant decorative props stay with their interactable parent; the old building is discarded.
            foreach(var t in Find(s,"Motel/Lobby/PizzaDoggyDecor").Cast<Transform>().Where(t=>!t.name.Contains("Ceiling")&&!t.name.Contains("ceiling")&&!t.name.Contains("Cornice")&&!t.name.Contains("Curtain")).ToArray())
                if(t.position.x>5&&t.position.z<9)Keep(t,lobby,LobbyOffset);
            var waiting=Ref<GameObject>(reception,"waitingGuest");Keep(waiting.transform,gameplay);waiting.SetActive(false);
            Keep(reception.transform,gameplay,LobbyOffset);Keep(shift.transform,gameplay);
            // Remove old room TVs; the complete room copies now own their TVs.
            foreach(var old in originalRooms.Skip(1))Delete(old.Television.gameObject);
            Keep(computer.transform,lobby,LobbyOffset);
            foreach(var name in new[]{"ClockBacking","ShiftClock"})Keep(shift.transform.Find(name),lobby,LobbyOffset);
            var board=All<StaffTaskBoard>(s).Single();Keep(board.transform,lobby,new Vector3(10.15f,0,-10.65f));
            Items(reception,"rooms",rooms);Items(service,"rooms",rooms);
            Keep(service.transform,gameplay);Value(service,"supplyLocation","Storage");Value(service,"maintenanceLocation","Maintenance");
            foreach(var old in originalRooms.Skip(1))Delete(old.Delivery.gameObject);
            Keep(service.transform.Find("PhoneRingtone"),lobby,LobbyOffset);Keep(service.transform.Find("PhoneStatus"),lobby,LobbyOffset);
            foreach(var t in service.transform.Cast<Transform>().Where(t=>t.name.Contains("DeliveryLabel")).ToArray())Delete(t.gameObject);
            // Storage carries supplies. Breakers and hunt equipment have a separate maintenance address.
            var storage=Group("StorageFurniture",furnishings);var storageDelta=new Vector3(10,0,-5.4f);
            foreach(var name in new[]{"Shelving","Boxes"})Keep(Find(s,"Motel/Storage/"+name),storage,storageDelta);
            foreach(var name in new[]{"UtilityTowelStack","TowelShelfLabel"})Keep(service.transform.Find(name),storage,storageDelta);
            var maintenance=Group("MaintenanceFurniture",furnishings);
            Keep(Find(s,"MimicHunt_M14/HuntEquipmentStation"),maintenance,new Vector3(3.1f,0,16.4f));
            foreach(var name in new[]{"ThermalDecoyHome","FirearmHome"})Keep(Find(s,"MimicHunt_M14/"+name),maintenance,new Vector3(3.1f,0,16.4f));
            Keep(Find(s,"Motel/Storage/MaintenanceCart"),maintenance,new Vector3(3,0,18.8f));
            
            Box("BreakerBoard",maintenance,new Vector3(20.3f,1.65f,26.24f),new Vector3(4.7f,1.05f,.15f),Metal);
            for(int i=0;i<rooms.Length;i++)
            {
                var breaker=UnityEngine.Object.Instantiate(breakerSource.gameObject,maintenance).GetComponent<RoomOperationsInteraction>();
                breaker.name="Breaker"+rooms[i].RoomNumber;Position(breaker.transform,new Vector3(18.4f+i*.75f,1.48f,26.1f),180);
                Set(breaker,"room",rooms[i]);Set(rooms[i],"breaker",breaker);
                Set(breaker,"label",Label(rooms[i].RoomNumber,maintenance,new Vector3(18.4f+i*.75f,2.06f,26.05f),0,.027f));
            }
            Label("ROOM CIRCUITS",maintenance,new Vector3(20.3f,2.42f,26.03f),0,.03f);
            var dumpster=All<GarbageDumpster>(s).Single();Keep(dumpster.transform,outdoors,new Vector3(-18.9f,0,34));
            // Retain networking, scenarios and threat/equipment objects. Replace physical ducts below.
            Keep(hunt.transform.parent,gameplay);
            Delete(oldVents.gameObject); // path root moved; resolved below if necessary
            Keep(inspection.transform.parent,gameplay);Keep(inspection.transform,lobby,LobbyOffset);
            Items(inspection,"RoomBagSpots",rooms.Select(r=>r.transform.parent.parent.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Room"+r.RoomNumber+"Baggage")));
            var so=new SerializedObject(inspection);var cases=so.FindProperty("cases");
            Items(inspection,"completeScenarioCases",new[]{0,4,11,7,1,2}.Select(i=>cases.GetArrayElementAtIndex(i).objectReferenceValue));
            // Independent physical CCTV seat, same lease/cursor/interaction implementation.
            var cctvRoot=Group("CCTVWorkstation",lobby);var cctv=cctvRoot.gameObject.AddComponent<ReceptionComputer>();
            cctvRoot.gameObject.GetComponent<NetworkObject>().AutoObjectParentSync=false;
            Value(cctv,"surveillanceOnly",true);Set(cctv,"reception",reception);Set(cctv,"shift",shift);
            var screen=FindUnder(lobby,"CCTVScreen");var screenCollider=screen.GetComponent<Collider>();if(screenCollider==null)screenCollider=screen.gameObject.AddComponent<BoxCollider>();
            var focus=Group("CCTVFocus",cctvRoot,screen.position+new Vector3(.75f,.12f,0));focus.LookAt(screen.position);
            Set(cctv,"screen",screenCollider);Set(cctv,"focus",focus);
            var interact=screen.gameObject.AddComponent<ComputerInteraction>();Set(interact,"computer",cctv);surveillance.monitoringStation=cctv;
            // Queue and safe waiting points are outside the corridor and desk working aisle.
            var anchors=Group("PublicNavigation",gameplay);var arrival=Group("Arrival",anchors,new Vector3(22,0,-22));var departure=Group("Departure",anchors,new Vector3(25.5f,0,-23));
            var points=Enumerable.Range(0,3).Select(i=>Group("Queue_"+i,anchors,new Vector3(25,0,-8.7f+i*1.6f))).ToArray();foreach(var t in points)t.rotation=Quaternion.Euler(0,90,0);
            Items(reception,"queuePoints",points);
            var guests=new List<GuestLifecycle>(originalGuests);
            for(int i=5;i<7;i++)guests.Add(UnityEngine.Object.Instantiate(originalGuests[i-5].gameObject).GetComponent<GuestLifecycle>());
            for(int i=0;i<guests.Count;i++)
            {
                var guest=guests[i];Keep(guest.transform,gameplay);guest.name="Guest_"+(char)('A'+i);Value(guest,"guestName","Guest "+(char)('A'+i));
                Set(guest,"arrivalPoint",arrival);Set(guest,"departurePoint",departure);Set(guest,"receptionPoint",points[0]);
                Set(guest,"evacuationPoint",Group("Evacuation_"+i,anchors,new Vector3(19+i*.9f,0,-5.25f)));
                Value(guest,"restrictPublicRoutes",true);guest.GetComponent<NavMeshAgent>().avoidancePriority=15+i*10;guest.ApplyIdentity();
            }
            Items(reception,"guests",guests);
            var session=All<DevelopmentSession>(s).Single();Keep(session.transform,null);var preview=Ref<Camera>(session,"lobbyCamera");Keep(preview.transform,gameplay);Position(preview.transform,new Vector3(23.4f,1.65f,-12.8f));
            var spawns=new SerializedObject(session);var sp=spawns.FindProperty("spawnPositions");sp.arraySize=4;
            for(int i=0;i<4;i++)sp.GetArrayElementAtIndex(i).vector3Value=new Vector3(23.2f+(i%2)*1.3f,.05f,-12.8f+(i/2)*1.3f);spawns.ApplyModifiedPropertiesWithoutUndo();
            var nav=All<NavMeshSurface>(s).Single();Keep(nav.transform,gameplay);foreach(Transform t in nav.transform.Cast<Transform>().ToArray())Delete(t.gameObject);
            // Remove every old architectural root; only explicitly migrated objects survive.
            foreach(var root in oldRoots)if(root!=null&&root!=session.gameObject&&root.transform.parent==null)Delete(root);
            foreach(var equipment in All<HuntEquipment>(s)){var home=Ref<Transform>(equipment,"home");var visual=Ref<Transform>(equipment,"visual");visual.SetPositionAndRotation(home.position,home.rotation);}
            Architecture();Delete(doorPrototype);doorPrototype=null;ExteriorScene();Ventilation(s,surveillance);RefineGeometry(s);
            Bake(s,nav);
            
            EditorSceneManager.MarkSceneDirty(s);EditorSceneManager.SaveScene(s);
            foreach(var no in All<NetworkObject>(s))typeof(NetworkObject).GetMethod("OnValidate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(no,null);
            EditorSceneManager.SaveScene(s);AssetDatabase.SaveAssets();
            File.WriteAllText(Reports+"/installation.txt","V4 generated from preserved M15 components. Six rooms, twelve guest doors, four staff doors. "+DateTime.Now.ToString("O"));
            Inspect();Validate();
        }
        static Transform FindUnder(Transform root,string name)=>root.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
        static void Bake(Scene s,NavMeshSurface surface)
        {
            surface.RemoveData();Physics.SyncTransforms();
            var sources=new List<NavMeshBuildSource>();
            var marks=All<NetworkDoor>(s).Select(d=>new NavMeshBuildMarkup{root=d.transform,ignoreFromBuild=true})
                .Concat(All<GuestLifecycle>(s).Select(g=>new NavMeshBuildMarkup{root=g.transform,ignoreFromBuild=true}))
                .Concat(All<CleaningSpot>(s).Where(c=>c.name.StartsWith("Trash")).Select(c=>new NavMeshBuildMarkup{root=c.transform,ignoreFromBuild=true}))
                .Concat(All<HuntEquipment>(s).Select(e=>new NavMeshBuildMarkup{root=e.transform,ignoreFromBuild=true}))
                .Concat(privateFloors.Select(f=>new NavMeshBuildMarkup{root=f.floor,overrideArea=true,area=f.area})).ToList();
            foreach(var root in s.GetRootGameObjects()){var batch=new List<NavMeshBuildSource>();UnityEngine.AI.NavMeshBuilder.CollectSources(root.transform,~0,NavMeshCollectGeometry.PhysicsColliders,0,marks,batch);sources.AddRange(batch);}
            var settings=NavMesh.GetSettingsByID(0);settings.agentRadius=.4f;settings.agentHeight=1.8f;settings.agentClimb=.15f;settings.overrideVoxelSize=true;settings.voxelSize=.05f;settings.minRegionArea=.1f;
            var data=UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings,sources,new Bounds(new Vector3(12,2,5),new Vector3(80,12,85)),Vector3.zero,Quaternion.identity);
            if(data==null)throw new Exception("V4 NavMesh bake failed.");
            const string path="Assets/Game/Navigation/Motel_Layout_V4_NavMesh.asset";var old=AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            if(old==null){AssetDatabase.CreateAsset(data,path);old=data;}else{EditorUtility.CopySerialized(data,old);Delete(data);EditorUtility.SetDirty(old);}
            var so=new SerializedObject(surface);so.FindProperty("m_NavMeshData").objectReferenceValue=old;so.ApplyModifiedPropertiesWithoutUndo();surface.AddData();
        }
    }
}
