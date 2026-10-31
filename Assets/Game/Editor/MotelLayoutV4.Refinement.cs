using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;
using MotelGame.Inspection;
using MotelGame.Service;
using MotelGame.Horror;

namespace MotelGame.Editor
{
    public static partial class MotelLayoutV4
    {
        const string RefinementReports="Documentation/RefinementM161";
        static double refinementPoll;
        [InitializeOnLoadMethod] static void ListenForRefinement()=>EditorApplication.update+=PollRefinement;
        static void PollRefinement()
        {
            if(EditorApplication.timeSinceStartup<refinementPoll||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||BuildPipeline.isBuildingPlayer||File.Exists("Library/MotelServiceRefresh.request"))return;
            refinementPoll=EditorApplication.timeSinceStartup+1;
            foreach(string action in new[]{"AuditBefore","RenderBefore","Apply","Reload","Validate","RenderAfter","Build"})
            {
                string path="Library/MotelRefinement"+action+".request";
                if(!File.Exists(path))continue;File.Delete(path);Directory.CreateDirectory(RefinementReports);
                try
                {
                    if(action=="Apply")ApplyRefinement();
                    else if(action=="Reload")EditorSceneManager.OpenScene(ScenePath);
                    else if(action=="AuditBefore")AuditRefinement(false);
                    else if(action=="Validate"){Validate();AuditRefinement(true);}
                    else if(action=="Build"){Validate();AuditRefinement(true);BuildMacTo("Builds/Validation");File.Copy(Reports+"/candidate-build.txt",RefinementReports+"/candidate-build.txt",true);}
                    else RenderRefinement(action=="RenderBefore"?"before":"after");
                }
                catch(Exception e){File.WriteAllText(RefinementReports+"/editor-error.txt",e.ToString());Debug.LogException(e);break;}
            }
        }
        static Scene RefinementScene()
        {
            var s=SceneManager.GetActiveScene();
            if(s.isDirty)throw new Exception("Save the current scene before refinement.");
            return s.path==ScenePath?s:EditorSceneManager.OpenScene(ScenePath);
        }
        static void RoomBox(Transform t,Transform room,Vector3 center,Vector3 size)
        {
            t.SetPositionAndRotation(room.TransformPoint(center),room.rotation);
            var scale=t.parent.lossyScale;t.localScale=new Vector3(size.x/scale.x,size.y/scale.y,size.z/scale.z);
        }
        // Idempotent and targeted: same helper is called by the optional V4 rebuild tool.
        static void RefineGeometry(Scene s)
        {
            var arch=Find(s,"Motel_Layout_V4/01_Architecture");
            var end=arch.Find("EastSouthCorridor_Outdoor");
            foreach(var child in end.Cast<Transform>().ToArray())Delete(child.gameObject);
            Box("Wall",end,new Vector3(17.3f,1.4f,-1.7f),new Vector3(.2f,2.8f,3),Paint);
            Box("Baseboard",end,new Vector3(17.3f,.06f,-1.7f),new Vector3(.212f,.12f,3),Trim,false);
            foreach(var room in All<ServiceRoom>(s))
            {
                var root=room.transform.parent.parent;
                var bathroom=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Bathroom");
                var fronts=bathroom.Cast<Transform>().Where(t=>t.name=="BathroomFront").OrderBy(t=>root.InverseTransformPoint(t.position).z).ToArray();
                RoomBox(fronts[0],root,new Vector3(2,1.4f,1.05f),new Vector3(.1f,2.8f,2.1f));
                RoomBox(fronts[1],root,new Vector3(2,1.4f,2.975f),new Vector3(.1f,2.8f,.05f));
                var rear=FindUnder(root,"BathroomRearTileLining");RoomBox(rear,root,new Vector3(.008f,1.4f,1.5f),new Vector3(.012f,2.8f,3));
                var east=FindUnder(root,"BathroomEastTileLining");RoomBox(east,root,new Vector3(1,1.4f,.012f),new Vector3(2,2.8f,.014f));
                var floor=FindUnder(root,"BathroomTiles");RoomBox(floor,root,new Vector3(.975f,.003f,1.5f),new Vector3(1.95f,.006f,3));
            }
            var storage=Find(s,"Motel_Layout_V4/03_Furnishings/StorageFurniture");
            var station=All<Transform>(s).Single(t=>t.name=="HuntEquipmentStation");
            var delta=new Vector3(29.04f,0,-2.6f)-station.position;
            foreach(string name in new[]{"HuntEquipmentStation","ThermalDecoyHome","FirearmHome"})
            {var t=All<Transform>(s).Single(t=>t.name==name);t.SetParent(storage,true);t.position+=delta;}
            // Keep the two pickups far enough apart that one cannot occlude the other's reach ray.
            var shelf=station.Find("EquipmentShelf");shelf.localScale=new Vector3(.7f,.08f,1.4f);
            foreach(var item in new[]{("ThermalDecoyHome",-.42f),("FirearmHome",.42f)})
                All<Transform>(s).Single(t=>t.name==item.Item1).position=station.position+new Vector3(0,.94f,item.Item2);
            foreach(float z in new[]{-.5f,.5f})
            {
                string name=z<0?"ShelfBracket_A":"ShelfBracket_B";var bracket=station.Find(name);
                if(bracket==null)Box(name,station,new Vector3(.29f,.7f,z),new Vector3(.1f,.32f,.06f),Metal,false);
                else bracket.localPosition=new Vector3(.29f,.7f,z);
            }
            foreach(var t in station.GetComponentsInChildren<TextMesh>(true))t.text="EMERGENCY EQUIPMENT\nPistol / thermal bait\nOne carry slot · [G] place";
            foreach(var equipment in All<HuntEquipment>(s))
            {var home=Ref<Transform>(equipment,"home");Ref<Transform>(equipment,"visual").SetPositionAndRotation(home.position,home.rotation);}
        }
        [Serializable] sealed class RefinedCases { public RefinedCase[] cases; }
        [Serializable] sealed class RefinedCase
        {
            public string name;public BagShell shell;public Color color;public bool containsMimic;public BaggagePart[] contents;
            public string contentsAnswer,weightAnswer,metalAnswer,packingAnswer,responseAnswer,authorReasoning;
        }
        [MenuItem("Tools/Motel/M16.1/Apply targeted refinement")]
        static void ApplyRefinement()
        {
            var s=RefinementScene();RefineGeometry(s);
            foreach(var row in JsonUtility.FromJson<RefinedCases>(File.ReadAllText("Assets/Game/Inspection/Editor/Cases.json")).cases)
            {
                var c=AssetDatabase.LoadAssetAtPath<BaggageCase>("Assets/Game/Inspection/Cases/"+row.name+".asset");
                if(c==null)throw new Exception("Missing existing case "+row.name);
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(row),c);
                if(!c.Valid(out var error))throw new Exception(row.name+": "+error);
                EditorUtility.SetDirty(c);
            }
            // Reconstruct existing area markup for this saved scene; never rebuild its architecture.
            privateFloors.Clear();
            foreach(var t in All<Transform>(s).Where(t=>t.name=="Floor"&&t.parent.parent!=null&&t.parent.parent.name=="01_Architecture"))
            {
                string name=t.parent.name;
                if(name.StartsWith("Room")&&int.TryParse(name.Substring(4),out int number))privateFloors.Add((t,number-98));
                else if(name=="Storage"||name=="Maintenance")privateFloors.Add((t,9));
            }
            Bake(s,All<NavMeshSurface>(s).Single());
            EditorSceneManager.MarkSceneDirty(s);EditorSceneManager.SaveScene(s);AssetDatabase.SaveAssets();
            File.WriteAllText(RefinementReports+"/applied.txt",DateTime.Now.ToString("O"));
            AuditRefinement(true);
        }
        static void AuditRefinement(bool corrected)
        {
            var s=RefinementScene();Physics.SyncTransforms();var lines=new List<string>();
            void Check(bool pass,string label)=>lines.Add((pass?"PASS ":"FAIL ")+label);
            bool SegmentHit(Vector3 a,Vector3 b,string name)
            {var direction=b-a;return Physics.RaycastAll(a,direction.normalized,direction.magnitude,~0,QueryTriggerInteraction.Ignore).Any(h=>h.collider.name==name);}
            for(float z=-2.7f;z<-.3f;z+=.3f)foreach(float y in new[]{.15f,1.65f,2.7f})
            {
                var a=new Vector3(16.9f,y,z);var b=new Vector3(17.7f,y,z);
                Check(SegmentHit(a,b,"Wall")&&SegmentHit(b,a,"Wall"),"South corridor east boundary both sides "+z+" y="+y);
            }
            foreach(var room in All<ServiceRoom>(s).OrderBy(r=>r.RoomNumber))
            {
                var root=room.transform.parent.parent;
                foreach(float z in new[]{.15f,.55f,.95f,1.55f,1.95f})foreach(float y in new[]{.15f,1.65f,2.7f})
                {
                    var a=root.TransformPoint(new Vector3(1.7f,y,z));var b=root.TransformPoint(new Vector3(2.3f,y,z));
                    Check(SegmentHit(a,b,"BathroomFront")&&SegmentHit(b,a,"BathroomFront"),room.RoomNumber+" bathroom closure both sides z="+z+" y="+y);
                }
                // Every room has solid outer boundaries except its authored door and window openings.
                foreach(float x in new[]{.2f,1.8f,4f,5.4f})
                {
                    var p=root.TransformPoint(new Vector3(x,2.72f,3));
                    foreach(var d in new[]{root.forward,-root.forward})Check(Physics.Raycast(p,d,3.3f,~0,QueryTriggerInteraction.Ignore),room.RoomNumber+" ceiling-wall continuity");
                }
            }
            float Route(Vector3 a,Vector3 b,string name)
            {
                var path=new NavMeshPath();bool ok=NavMesh.SamplePosition(a,out var start,.4f,NavMesh.AllAreas)&&NavMesh.SamplePosition(b,out var end,.4f,NavMesh.AllAreas)&&NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;
                Check(ok,name+" connected");float length=0;for(int i=1;i<path.corners.Length;i++)length+=Vector3.Distance(path.corners[i-1],path.corners[i]);
                lines.Add(name+": "+length.ToString("F2")+" m / "+(length/4).ToString("F1")+" s excluding doors");return length;
            }
            var rec=All<ReceptionDesk>(s).Single();var station=All<Transform>(s).Single(t=>t.name=="HuntEquipmentStation");var gear=station.position+Vector3.left*.9f;
            Route(rec.QueuePoints[0].position,gear,"Reception -> emergency equipment");
            Route(rec.Rooms[0].GuestIdle.position,gear,"101 -> emergency equipment");
            Route(rec.QueuePoints[0].position,new Vector3(20.3f,0,23.4f),"Reception -> Maintenance");
            foreach(var room in rec.Rooms)Route(rec.QueuePoints[0].position,room.DoorOutside.position,"Reception -> "+room.RoomNumber);
            foreach(var item in All<HuntEquipment>(s))
            {
                var col=item.PickupCollider;
                lines.Add(item.Item+" pickup bounds="+col.bounds+" home="+Ref<Transform>(item,"home").position);
                foreach(var eye in new[]{new Vector3(27.88f,1.68f,-3.34f),new Vector3(col.bounds.center.x-1.25f,1.65f,col.bounds.center.z)})
                {
                    var point=col.ClosestPoint(eye);var delta=point-eye;
                    var hits=Physics.RaycastAll(eye,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
                    Check(hits.All(h=>h.collider==col),item.Item+" unobstructed pickup from "+eye);
                    lines.Add(item.Item+" closest-point probe "+eye+" -> "+point+": "+string.Join(", ",hits.OrderBy(h=>h.distance).Select(h=>PathOf(h.collider.transform))));
                }
            }
            Check(All<NightShiftController>(s).Single().ConfigurationValid(),"Staff workload configuration");
            Check(All<MotelGame.Registration.RegistrationDesk>(s).Length>0?All<MotelGame.Registration.RegistrationDesk>(s).Single().ConfigurationValid():All<BaggageInspection>(s).Single().ConfigurationValid(),"Configured guest evidence and shared record");
            Check(AnalyzerPanel.PrimaryTests.SequenceEqual(new[]{AnalyzerTest.Weight,AnalyzerTest.Metal,AnalyzerTest.Pulse}),"Exactly three primary tests");
            File.WriteAllLines(RefinementReports+(corrected?"/geometry-after.txt":"/geometry-before.txt"),lines);
            if(corrected&&lines.Any(l=>l.StartsWith("FAIL")))throw new Exception("M16.1 geometry audit failed; see report.");
        }
        static void RenderRefinement(string stage)
        {
            var s=EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                foreach(var t in All<Transform>(s))t.gameObject.layer=30;
                foreach(var l in All<Light>(s))l.cullingMask=1<<30;
                foreach(var t in All<WorldServiceText>(s))t.RefreshMaterial();
                foreach(var c in All<Camera>(s))c.enabled=false;
                var camera=All<Camera>(s).First();camera.scene=s;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(s);camera.cullingMask=1<<30;camera.fieldOfView=75;camera.nearClipPlane=.03f;
                var views=new[]{
                    ("transition",new Vector3(14.8f,1.65f,-1.6f),new Vector3(17.3f,1.5f,-1.6f)),
                    ("corridor-outside",new Vector3(20,1.65f,-1.6f),new Vector3(17.3f,1.65f,-1.6f)),
                    ("bathroom-entry",new Vector3(14.4f,1.65f,.45f),new Vector3(13.25f,1.5f,.7f)),
                    ("bathroom-overview",new Vector3(14.6f,1.65f,2.6f),new Vector3(12.7f,1.2f,1.4f)),
                    ("bathroom-inside",new Vector3(12.8f,1.65f,.55f),new Vector3(13.6f,1.5f,.8f)),
                    ("storage",new Vector3(26,1.65f,-2.6f),new Vector3(29.04f,1.2f,-2.6f))};
                foreach(var view in views)
                {
                    camera.transform.position=view.Item2;camera.transform.LookAt(view.Item3);var rt=new RenderTexture(1200,800,24);rt.Create();var old=RenderTexture.active;camera.targetTexture=rt;
                    try{camera.Render();RenderTexture.active=rt;var image=new Texture2D(1200,800,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1200,800),0,0);image.Apply();File.WriteAllBytes(RefinementReports+"/"+stage+"-"+view.Item1+".png",image.EncodeToPNG());Delete(image);}
                    finally{camera.targetTexture=null;RenderTexture.active=old;rt.Release();Delete(rt);}
                }
            }
            finally{EditorSceneManager.ClosePreviewScene(s);}
        }
    }
}
