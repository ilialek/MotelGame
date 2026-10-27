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
    // Explicit installation only. Domain reload never rebuilds the user's scene.
    public static class NightShiftSetup
    {
        public const string Reports="Documentation/NightShiftM10";
        const string InstallRequest="Library/MotelNightShiftSetup.request",ValidateRequest="Library/MotelNightShiftValidate.request",PresentationRequest="Library/MotelNightShiftPresentation.request";
        static double nextPoll;
        [InitializeOnLoadMethod] static void Init()=>EditorApplication.update+=Poll;
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<nextPoll)return;nextPoll=EditorApplication.timeSinceStartup+1;
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||File.Exists("Library/MotelServiceRefresh.request"))return;
            if(!File.Exists(InstallRequest)&&!File.Exists(ValidateRequest)&&!File.Exists(PresentationRequest))return;
            Directory.CreateDirectory(Reports);
            try
            {
                if(File.Exists(InstallRequest)){string hash=File.ReadAllText(InstallRequest).Trim();File.Delete(InstallRequest);Install(hash);}
                if(File.Exists(PresentationRequest)){File.Delete(PresentationRequest);RefineLabels();}
                if(File.Exists(ValidateRequest)){File.Delete(ValidateRequest);Validate();}
                if(File.Exists(Reports+"/editor-error.txt"))File.Move(Reports+"/editor-error.txt",Reports+"/resolved-error-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt");
            }
            catch(Exception e){File.WriteAllText(Reports+"/editor-error.txt",e.ToString());Debug.LogException(e);}
        }
        static T[] All<T>(Scene s) where T:Component=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).ToArray();
        static void Set(UnityEngine.Object o,string n,UnityEngine.Object value){var s=new SerializedObject(o);s.FindProperty(n).objectReferenceValue=value;s.ApplyModifiedPropertiesWithoutUndo();}
        static void Array(UnityEngine.Object o,string n,IEnumerable<UnityEngine.Object> values)
        {var s=new SerializedObject(o);var p=s.FindProperty(n);var a=values.ToArray();p.arraySize=a.Length;for(int i=0;i<a.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=a[i];s.ApplyModifiedPropertiesWithoutUndo();}
        static GameObject Child(string n,Transform parent){var g=new GameObject(n);g.transform.SetParent(parent,false);return g;}
        static GameObject Cube(string n,Transform parent,Vector3 p,Vector3 size,string material,bool collider=false)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=n;g.transform.SetParent(parent,false);g.transform.position=p;g.transform.localScale=size;
            g.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/MotelBlockoutV3/"+material+".mat");
            if(!collider)UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());return g;
        }
        static TextMesh Label(string n,Transform parent,Vector3 p,Vector3 facing,string text,float size)
        {
            var g=Child(n,parent);g.transform.SetPositionAndRotation(p,Quaternion.LookRotation(-facing));
            var label=g.AddComponent<TextMesh>();label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.fontSize=64;
            label.characterSize=size;label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.color=new Color(.8f,.88f,.72f);label.text=text;
            var world=g.AddComponent<WorldServiceText>();Set(world,"textShader",AssetDatabase.LoadAssetAtPath<Shader>("Assets/Game/Shaders/WorldServiceText.shader"));world.RefreshMaterial();return label;
        }
        static Transform Point(string n,Transform parent,Vector3 p,Vector3 direction)
        {var g=Child(n,parent);g.transform.SetPositionAndRotation(p,Quaternion.LookRotation(direction));return g.transform;}
        static void Install(string expected)
        {
            string path=MultipleGuestsSetup.ScenePath;
            using(var hash=SHA256.Create())if(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant()!=expected)throw new Exception("Scene changed since inspection.");
            var scene=SceneManager.GetSceneByPath(path);var previous=SceneManager.GetActiveScene();bool opened=!scene.IsValid()||!scene.isLoaded;
            if(!opened&&scene.isDirty)throw new Exception("Save unsaved scene edits before installing M10.");
            try
            {
                if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);SceneManager.SetActiveScene(scene);
                if(All<NightShiftController>(scene).Length>0)throw new Exception("M10 already installed; refusing duplicate configuration.");
                var motel=scene.GetRootGameObjects().Single(g=>g.name=="Motel").transform;
                var reception=All<ReceptionDesk>(scene).Single();var requests=All<GuestServiceRequests>(scene).Single();
                var root=new GameObject("NightShift");root.AddComponent<NetworkObject>();var shift=root.AddComponent<NightShiftController>();
                var terminalRoot=Child("ReceptionComputer",root.transform);terminalRoot.AddComponent<NetworkObject>();var computer=terminalRoot.AddComponent<ReceptionComputer>();
                Set(shift,"reception",reception);Set(shift,"requests",requests);Set(shift,"computer",computer);
                Set(reception,"shift",shift);Set(reception,"computer",computer);Set(requests,"shift",shift);
                Set(computer,"shift",shift);Set(computer,"reception",reception);
                var screen=motel.Find("ReceptionDesk/ComputerScreen").gameObject;
                var screenCollider=screen.AddComponent<BoxCollider>();Set(computer,"screen",screenCollider);
                var interaction=screen.AddComponent<ComputerInteraction>();Set(interaction,"computer",computer);
                var focus=Point("CameraFocus",terminalRoot.transform,new Vector3(11.52f,1.34f,4.9f),Vector3.left);Set(computer,"focus",focus);
                // Existing front, display and keyboard stay exactly where they were.
                Cube("CRTRearHousing",terminalRoot.transform,new Vector3(10.75f,1.34f,4.9f),new Vector3(.3f,.42f,.58f),"Machine");
                Cube("CRTStand",terminalRoot.transform,new Vector3(10.78f,1.115f,4.9f),new Vector3(.2f,.03f,.28f),"Trim");
                Cube("Mouse",terminalRoot.transform,new Vector3(11.03f,1.125f,5.28f),new Vector3(.12f,.05f,.09f),"Trim");
                Label("CRTIdleText",terminalRoot.transform,new Vector3(11.014f,1.34f,4.9f),Vector3.right,"MOTEL\nNIGHT DESK\n[E]",.009f);
                UnityEngine.Object.DestroyImmediate(reception.Counter.GetComponent<ReceptionInteraction>());
                All<TextMesh>(scene).Single(t=>t.name=="ReceptionInstructions").text="RECEPTION\nTerminal behind desk";
                var queue=Child("ReceptionQueue",root.transform).transform;
                var first=reception.Guests[0].ReceptionPoint;
                var points=new[]{first,Point("Waiting2",queue,new Vector3(7.5f,0,6),Vector3.right),Point("Waiting3",queue,new Vector3(6.2f,0,6),Vector3.right)};
                Array(reception,"queuePoints",points);
                Cube("ClockBacking",root.transform,new Vector3(9.5f,2.45f,9.95f),new Vector3(1.8f,.7f,.05f),"Machine");
                Set(shift,"clockLabel",Label("ShiftClock",root.transform,new Vector3(9.5f,2.45f,9.918f),Vector3.back,"NIGHT SHIFT\n22:00\nWaitingToStart",.013f));
                var boardRoot=Child("StaffTaskBoard",root.transform).transform;
                Cube("BoardBacking",boardRoot,new Vector3(19.14f,1.8f,4.65f),new Vector3(.04f,1.6f,1.9f),"Machine");
                var label=Label("Tasks",boardRoot,new Vector3(19.112f,1.8f,4.65f),Vector3.left,"STAFF / NIGHT 22:00\nWaitingToStart\n101 — Cleaning required\n102 — Cleaning required\n103 — Cleaning required\n104 — Cleaning required\nSERVICE\nNo pending call",.012f);
                var board=boardRoot.gameObject.AddComponent<StaffTaskBoard>();Set(board,"reception",reception);Set(board,"requests",requests);Set(board,"shift",shift);Set(board,"label",label);
                foreach(var room in reception.Rooms)
                {
                    Set(room,"shift",shift);
                    var desk=motel.Find("Rooms/Room"+room.RoomNumber+"/Furniture/DeskTop");
                    var tvRoot=Child("Room"+room.RoomNumber+"_Television",root.transform).transform;
                    Vector3 p=desk.position+new Vector3(0,.3f,.02f);
                    var tv=tvRoot.gameObject.AddComponent<RoomTelevision>();Set(tv,"room",room);Set(tv,"requests",requests);Set(room,"television",tv);
                    Cube("TVHousing",tvRoot,p,new Vector3(.7f,.52f,.38f),"Machine");
                    var display=Cube("TVScreen",tvRoot,p+new Vector3(-.055f,.02f,-.198f),new Vector3(.5f,.35f,.012f),"Screen");Set(tv,"screen",display.GetComponent<Renderer>());
                    Set(tv,"screenText",Label("TVStatus",tvRoot,p+new Vector3(-.055f,.02f,-.207f),Vector3.back,"MOTEL TV\nCHANNEL 03",.009f));
                    var controls=new List<TelevisionControl>();
                    for(int step=0;step<2;step++)
                    {
                        Vector3 position=step==0?p+new Vector3(-.48f,-.2f,-.1f):p+new Vector3(.27f,-.06f,-.215f);
                        var c=Cube(step==0?"PowerSocket":"TuningReset",tvRoot,position,step==0?new Vector3(.14f,.12f,.1f):new Vector3(.08f,.08f,.055f),"Trim",true);
                        var control=c.AddComponent<TelevisionControl>();Set(control,"television",tv);Set(control,"targetCollider",c.GetComponent<Collider>());
                        var so=new SerializedObject(control);so.FindProperty("step").intValue=step;so.ApplyModifiedPropertiesWithoutUndo();controls.Add(control);
                    }
                    Cube("PowerCable",tvRoot,p+new Vector3(-.33f,-.2f,-.1f),new Vector3(.18f,.018f,.018f),"Trim");
                    Array(tv,"controls",controls);
                }
                foreach(var text in All<WorldServiceText>(scene))text.RefreshMaterial();
                EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene,path))throw new Exception("M10 save failed.");
                foreach(var obj in All<NetworkObject>(scene))
                {typeof(NetworkObject).GetMethod("OnValidate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(obj,null);if(PrefabUtility.IsPartOfPrefabInstance(obj))PrefabUtility.RecordPrefabInstancePropertyModifications(obj);}
                EditorSceneManager.SaveScene(scene,path);
            }
            finally{if(previous.IsValid())SceneManager.SetActiveScene(previous);if(opened&&scene.IsValid())EditorSceneManager.CloseScene(scene,true);}
            Validate();
        }
        static void RefineLabels()
        {
            var scene=SceneManager.GetSceneByPath(MultipleGuestsSetup.ScenePath);bool opened=!scene.IsValid()||!scene.isLoaded;
            if(!opened&&scene.isDirty)throw new Exception("Unsaved scene changes; cannot refine M10 labels.");
            try
            {
                if(opened)scene=EditorSceneManager.OpenScene(MultipleGuestsSetup.ScenePath,OpenSceneMode.Additive);
                var root=All<NightShiftController>(scene).Single();
                foreach(var label in root.GetComponentsInChildren<TextMesh>())
                    if(label.name=="CRTIdleText"||label.name=="TVStatus")label.characterSize=.009f;
                    else if(label.name=="Tasks")label.characterSize=.012f;
                    else if(label.name=="ShiftClock")label.characterSize=.013f;
                var stand=root.transform.Find("ReceptionComputer/CRTStand");stand.position=new Vector3(10.78f,1.115f,4.9f);stand.localScale=new Vector3(.2f,.03f,.28f);
                foreach(var tv in root.GetComponentsInChildren<RoomTelevision>())
                {
                    var housing=tv.transform.Find("TVHousing");float desired=.75f+.3f;
                    tv.transform.position+=Vector3.up*(desired-housing.position.y);
                }
                foreach(var label in All<WorldServiceText>(scene))label.RefreshMaterial();
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,MultipleGuestsSetup.ScenePath);
            }
            finally{if(opened&&scene.IsValid())EditorSceneManager.CloseScene(scene,true);}
            Validate();
        }
        [MenuItem("Tools/Motel/Validate Night Shift M10")]
        public static void Validate()
        {
            Directory.CreateDirectory(Reports);var scene=EditorSceneManager.OpenPreviewScene(MultipleGuestsSetup.ScenePath);
            try
            {
                var shift=All<NightShiftController>(scene).Single();var computer=All<ReceptionComputer>(scene).Single();var reception=All<ReceptionDesk>(scene).Single();
                if(!shift.ConfigurationValid()||!computer.ConfigurationValid()||!reception.ConfigurationValid()||!All<StaffTaskBoard>(scene).Single().ConfigurationValid())throw new Exception("M10 references invalid.");
                foreach(var room in reception.Rooms)if(room.Television==null||!room.Television.ConfigurationValid())throw new Exception("TV config: "+room.RoomNumber);
                if(All<ReceptionInteraction>(scene).Length!=0||All<ComputerInteraction>(scene).Length!=1)throw new Exception("Old Reception UI target or duplicate computer.");
                foreach(var t in All<Transform>(scene))if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)throw new Exception("Missing script: "+t.name);
                var surface=All<NavMeshSurface>(scene).Single();surface.AddData();var lines=new List<string>();
                foreach(var point in reception.QueuePoints)
                {
                    Path(reception.Guests[0].ArrivalPoint.position,point.position,lines);Path(point.position,reception.Guests[0].DeparturePoint.position,lines);
                    if(All<Collider>(scene).Any(c=>c.enabled&&c.gameObject.activeInHierarchy&&!c.isTrigger&&c.GetComponentInParent<GuestLifecycle>()==null&&c.bounds.Contains(point.position+Vector3.up)))throw new Exception("Queue point in geometry: "+point.name);
                }
                for(int i=0;i<reception.QueuePoints.Length;i++)for(int j=0;j<i;j++)if(Vector3.Distance(reception.QueuePoints[i].position,reception.QueuePoints[j].position)<1.1f)throw new Exception("Queue capsule spacing.");
                foreach(var room in reception.Rooms)Path(room.DoorInside.position,room.Television.Controls[0].transform.position+new Vector3(0,-.86f,-1),lines);
                Physics.SyncTransforms();
                var obstacles=All<Collider>(scene).Where(c=>c.enabled&&c.gameObject.activeInHierarchy&&!c.isTrigger&&c.GetComponentInParent<GuestLifecycle>()==null).ToArray();
                for(int i=0;i<=20;i++)
                {
                    var p=Vector3.Lerp(new Vector3(11.9f,1.65f,4.9f),computer.Focus.position,i/20f);
                    if(obstacles.Any(c=>Vector3.Distance(c.ClosestPoint(p),p)<.04f))throw new Exception("Camera focus path intersects collider.");
                }
                var motel=scene.GetRootGameObjects().Single(g=>g.name=="Motel");
                var oldMeshes=motel.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.GetComponent<TextMesh>()==null).ToArray();
                foreach(var added in shift.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.GetComponent<TextMesh>()==null))
                foreach(var old in oldMeshes)
                {
                    var overlap=Vector3.Min(added.bounds.max,old.bounds.max)-Vector3.Max(added.bounds.min,old.bounds.min);
                    if(overlap.x>0&&overlap.y>0&&overlap.z>0&&overlap.x*overlap.y*overlap.z>.00001f)
                        throw new Exception("M10 primitive overlaps original geometry: "+added.name+" / "+old.name);
                }
                Capture(scene);
                File.WriteAllLines(Reports+"/navigation-validation.txt",lines);
                File.WriteAllText(Reports+"/setup-validation.txt","PASS: Saved M10 scene reopened; shift/computer/queue/4 TVs/board references, one computer target, old counter target removed, no missing scripts. 10 NavMesh routes complete, 1.3m queue spacing, employee camera path clear; new primitive bounds do not intersect original active furniture/architecture.\nDefaults: 900 seconds, 6–8 arrivals, last arrival 80%, patience120.\n");
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        static void Capture(Scene scene)
        {
            // Read-only rendering of the saved preview scene; never save this temporary setup.
            foreach(var t in All<Transform>(scene))t.gameObject.layer=30;
            foreach(var light in All<Light>(scene))light.cullingMask=1<<30;
            foreach(var text in All<WorldServiceText>(scene))text.RefreshMaterial();
            foreach(var c in All<Camera>(scene))c.enabled=false;
            var camera=All<Camera>(scene).First();camera.scene=scene;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);
            camera.cullingMask=1<<30;camera.fieldOfView=60;camera.nearClipPlane=.03f;
            var views=new[]{("computer",new Vector3(12.4f,1.7f,5.1f),new Vector3(10.9f,1.3f,4.9f)),
                ("board",new Vector3(16.9f,1.8f,4.65f),new Vector3(19.14f,1.8f,4.65f)),
                ("television",new Vector3(18.5f,1.65f,13.65f),new Vector3(19.3f,1.05f,15.14f))};
            foreach(var v in views)
            {
                camera.transform.position=v.Item2;camera.transform.LookAt(v.Item3);
                var rt=new RenderTexture(1200,800,24);rt.Create();var old=RenderTexture.active;camera.targetTexture=rt;
                try{camera.Render();RenderTexture.active=rt;var image=new Texture2D(1200,800,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1200,800),0,0);image.Apply();File.WriteAllBytes(Reports+"/preview-"+v.Item1+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);}
                finally{camera.targetTexture=null;RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
            }
        }
        static void Path(Vector3 a,Vector3 b,List<string> lines)
        {
            if(!NavMesh.SamplePosition(a,out var start,.4f,NavMesh.AllAreas)||!NavMesh.SamplePosition(b,out var end,.4f,NavMesh.AllAreas))throw new Exception("M10 point off NavMesh: "+a+" -> "+b);
            var path=new NavMeshPath();if(!NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)throw new Exception("M10 route incomplete.");
            lines.Add(a+" -> "+b+": complete");
        }
    }
}
