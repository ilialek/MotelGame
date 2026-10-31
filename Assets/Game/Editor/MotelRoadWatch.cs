using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using Unity.Netcode;
using MotelGame.Service;
using MotelGame.Horror;
using MotelGame.Inspection;
using MotelGame.Registration;
using MotelGame.Characters;

namespace MotelGame.Editor
{
    [InitializeOnLoad]
    public static partial class MotelRoadWatch
    {
        const string Reports="Documentation/RoadWatch";
        static double next;
        static MotelRoadWatch()=>EditorApplication.update+=Poll;
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<next||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||BuildPipeline.isBuildingPlayer||File.Exists("Library/MotelServiceRefresh.request"))return;
            next=EditorApplication.timeSinceStartup+1;
            foreach(string action in new[]{"Setup","Photos","Validate","Build"})
            {
                string path="Library/MotelRoadWatch"+action+".request";if(!File.Exists(path))continue;File.Delete(path);Directory.CreateDirectory(Reports);
                try
                {
                    if(action=="Setup")Setup();else if(action=="Photos")CaptureCheckInPortraits();else if(action=="Validate")Validate();
                    else{Validate();MotelLayoutV4.BuildMacTo("Builds/Validation");File.Copy("Documentation/LevelM16/candidate-build.txt",Reports+"/candidate-build.txt",true);}
                    File.WriteAllText(Reports+"/"+action+".done",DateTime.Now.ToString("O"));
                }
                catch(Exception e){File.WriteAllText(Reports+"/editor-error.txt",e.ToString());Debug.LogException(e);break;}
            }
        }
        static T[] All<T>(Scene s)where T:Component=>s.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<T>(true)).ToArray();
        static Scene Scene()
        {var s=SceneManager.GetActiveScene();if(s.isDirty)throw new Exception("Save current scene first.");return s.path==MotelLayoutV4.ScenePath?s:EditorSceneManager.OpenScene(MotelLayoutV4.ScenePath);}
        static void Set(UnityEngine.Object obj,string field,UnityEngine.Object value){var so=new SerializedObject(obj);so.FindProperty(field).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();}
        static void Destroy(UnityEngine.Object obj){if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);}
        static Transform Group(string name,Transform parent,Vector3 position=default)
        {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=position;return t;}
        static Material Material(string name,Color color,bool unlit=false)
        {
            string path=Content+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m!=null)return m;m=new Material(Shader.Find(unlit?"Universal Render Pipeline/Unlit":"Universal Render Pipeline/Lit")){name=name};m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.08f);AssetDatabase.CreateAsset(m,path);return m;
        }
        static GameObject Box(string name,Transform parent,Vector3 position,Vector3 size,Material material,bool solid=false)
        {var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=position;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=material;if(!solid)Destroy(g.GetComponent<Collider>());return g;}
        static TextMesh Text(string name,string text,Transform parent,Vector3 position,float yaw,float size,Color color)
        {
            var t=Group(name,parent,position);t.localRotation=Quaternion.Euler(0,yaw,0);var label=t.gameObject.AddComponent<TextMesh>();label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.fontSize=64;label.characterSize=size;label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.color=color;label.text=text;
            var world=t.gameObject.AddComponent<WorldServiceText>();Set(world,"textShader",AssetDatabase.LoadAssetAtPath<Shader>("Assets/Game/Shaders/WorldServiceText.shader"));world.RefreshMaterial();return label;
        }
        static GameObject VehiclePrefab()
        {
            string path=Content+"/GuestVehicle.prefab";var old=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(old!=null)return old;
            var root=Group("GuestVehicle",null);var view=root.gameObject.AddComponent<GuestVehicleView>();var paint=new List<Renderer>();var plates=new List<TextMesh>();view.bodies=new GameObject[2];
            var metal=Material("VehiclePaint",Color.gray);var glass=Material("Glass",RGB("253B43"));var rubber=Material("Rubber",RGB("171B1D"));var chrome=Material("Bumper",RGB("6F7472"));var white=Material("Plate",RGB("D4CBAA"));var lamp=Material("Headlight",RGB("E0D5AB"),true);var red=Material("TailLight",RGB("8A2425"));
            for(int variant=0;variant<2;variant++)
            {
                bool van=variant==1;var t=Group(van?"Van":"Sedan",root);view.bodies[variant]=t.gameObject;
                paint.Add(Box("LowerBody",t,new Vector3(0,.65f,0),new Vector3(1.8f,.6f,4.2f),metal).GetComponent<Renderer>());
                Box("CabinGlass",t,new Vector3(0,van?1.38f:1.2f,van?-.2f:-.1f),new Vector3(1.58f,van?.98f:.65f,van?3.2f:1.9f),glass);
                paint.Add(Box("Roof",t,new Vector3(0,van?1.89f:1.55f,van?-.2f:-.1f),new Vector3(1.65f,.1f,van?3.25f:2f),metal).GetComponent<Renderer>());
                foreach(float x in new[]{-.8f,.8f})foreach(float z in new[]{van?-1.75f:-1f,van?1.35f:.8f})paint.Add(Box("WindowPillar",t,new Vector3(x,van?1.4f:1.22f,z),new Vector3(.09f,van?1f:.65f,.12f),metal).GetComponent<Renderer>());
                if(van)paint.Add(Box("CargoPanel",t,new Vector3(0,1.35f,-.8f),new Vector3(1.62f,.95f,1.9f),metal).GetComponent<Renderer>());
                else
                {
                    Box("TrunkInterior",t,new Vector3(0,.97f,-1.6f),new Vector3(1.55f,.08f,.82f),rubber);
                    var lid=Group("TrunkHinge",t,new Vector3(0,1.01f,-1.15f));paint.Add(Box("TrunkLid",lid,new Vector3(0,0,-.45f),new Vector3(1.76f,.08f,.9f),metal).GetComponent<Renderer>());
                }
                foreach(float z in new[]{-2.13f,2.13f})
                {
                    Box("Bumper",t,new Vector3(0,.48f,z),new Vector3(1.84f,.16f,.12f),chrome);
                    foreach(float x in new[]{-.65f,.65f})Box("Lamp",t,new Vector3(x,.82f,z),new Vector3(.38f,.24f,.05f),z>0?lamp:red);
                    Box("PlateBacking",t,new Vector3(0,.69f,z),new Vector3(.76f,.23f,.06f),white);
                    plates.Add(Text("LicensePlate","KDH-417",t,new Vector3(0,.69f,z+Mathf.Sign(z)*.035f),z>0?180:0,.047f,RGB("161A1B")));
                }
                foreach(float x in new[]{-.91f,.91f})foreach(float z in new[]{-1.35f,1.35f})
                {
                    var wheel=GameObject.CreatePrimitive(PrimitiveType.Cylinder);wheel.name="Wheel";wheel.transform.SetParent(t,false);wheel.transform.localPosition=new Vector3(x,.35f,z);wheel.transform.localRotation=Quaternion.Euler(0,0,90);wheel.transform.localScale=new Vector3(.68f,.12f,.68f);wheel.GetComponent<Renderer>().sharedMaterial=rubber;Destroy(wheel.GetComponent<Collider>());
                    Box("Hub",t,new Vector3(x*1.12f,.35f,z),new Vector3(.04f,.26f,.26f),chrome);
                }
                t.gameObject.SetActive(!van);
            }
            var body=root.gameObject.AddComponent<BoxCollider>();body.center=new Vector3(0,.85f,0);body.size=new Vector3(1.95f,1.7f,4.35f);
            var obstacle=root.gameObject.AddComponent<NavMeshObstacle>();obstacle.shape=NavMeshObstacleShape.Box;obstacle.center=body.center;obstacle.size=body.size;obstacle.carving=true;
            view.paint=paint.ToArray();view.plates=plates.ToArray();var prefab=PrefabUtility.SaveAsPrefabAsset(root.gameObject,path);Destroy(root.gameObject);return prefab;
        }
        static void Setup()=>PrepareCheckIn();
        public static void Validate()
        {
            var s=Scene();var lines=new List<string>();void Check(bool ok,string message){lines.Add((ok?"PASS ":"FAIL ")+message);}
            var reg=All<RegistrationDesk>(s).Single();Check(reg.ConfigurationValid(),"Six configured cases, three photographs, two workstations and seven registered NPC slots");
            foreach(var c in reg.Cases){Check(c.Valid(out var error),c.guestName+" "+error);if(c.hasReport)Check(c.report.photograph.width==480&&c.report.photograph.height==320&&c.report.photograph.filterMode==FilterMode.Point,c.guestName+" actual 480x320 photo / point filtering");}
            Check(All<BaggageInspection>(s).Length==0&&All<GuestBaggage>(s).Length==0&&All<BaggageHandle>(s).Length==0&&All<AnalyzerPanel>(s).Length==0&&All<HuntScenario>(s).Length==0&&All<MimicDebugMenu>(s).Length==0,"No active baggage scanner, bag, dialogue or debug interaction");
            Check(All<RegistrationPanel>(s).Length==2&&All<VentSurveillance>(s).Single().cameras.Length==10,"Two registration/RoadWatch panels and ten CCTV feeds");
            foreach(var r in reg.Reception.Guests)
            {
                var g=r.GetComponent<RegisteredGuest>();Check(g.character!=null&&g.vehicle!=null&&g.vehicle.plates.Length==4&&g.vehicle.paint.All(r=>r!=null),r.name+" appearance and parked vehicle");
                var path=new NavMeshPath();Check(NavMesh.CalculatePath(r.ArrivalPoint.position,reg.Reception.QueuePoints[0].position,1,path)&&path.status==NavMeshPathStatus.PathComplete,r.name+" vehicle arrival -> reception public route");
            }
            for(int count=3;count<=6;count++)for(int iteration=0;iteration<20;iteration++)
            {var agenda=reg.MakeAgenda(count);int reports=agenda.Count(c=>c.hasReport);if(Mathf.Abs(reports-count*.5f)>.5f||agenda.Distinct().Count()!=count)throw new Exception("Agenda distribution");}
            Check(true,"80 randomized agendas: unique cases, floor/ceil half reports, distributed pairs");
            File.WriteAllLines(Reports+"/validation.txt",lines);if(lines.Any(l=>l.StartsWith("FAIL")))throw new Exception("RoadWatch validation failed");MotelLayoutV4.Validate();
        }
    }
    public static partial class MotelLayoutV4
    {
        public static void RebakeExistingRoadWatch(Scene s)
        {
            privateFloors.Clear();foreach(var t in All<Transform>(s).Where(t=>t.name=="Floor"&&t.parent!=null&&t.parent.parent!=null&&t.parent.parent.name=="01_Architecture"))
            {string name=t.parent.name;if(name.StartsWith("Room")&&int.TryParse(name.Substring(4),out int number))privateFloors.Add((t,number-98));else if(name=="Storage"||name=="Maintenance")privateFloors.Add((t,9));}
            Bake(s,All<Unity.AI.Navigation.NavMeshSurface>(s).Single());
        }
    }
}
