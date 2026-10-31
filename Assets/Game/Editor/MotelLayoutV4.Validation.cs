using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using Unity.Netcode;
using MotelGame.Service;
using MotelGame.Horror;
using MotelGame.Inspection;
using MotelGame.Interactions;

namespace MotelGame.Editor
{
    public static partial class MotelLayoutV4
    {
        [MenuItem("Tools/Motel/Level V4/Validate saved scene")]
        public static void Validate()
        {
            var s=SceneManager.GetActiveScene();if(s.path!=ScenePath)s=EditorSceneManager.OpenScene(ScenePath);
            var lines=new List<string>();void Check(bool ok,string message){lines.Add((ok?"PASS ":"FAIL ")+message);}
            var allRooms=All<ServiceRoom>(s).OrderBy(r=>r.RoomNumber).ToArray();
            Check(allRooms.Length==6&&string.Join(",",allRooms.Select(r=>r.RoomNumber))=="101,102,103,104,105,106","Six configured rooms in numeric order");
            foreach(var r in allRooms)Check(r.ConfigurationValid(out _)&&r.CourtyardDoor!=null,"Room "+r.RoomNumber+" gameplay references and secondary door");
            Check(All<ReceptionDesk>(s).Single().ConfigurationValid(),"Reception / seven guest pool / queue references");
            Check(All<MotelGame.Registration.RegistrationDesk>(s).Length>0?All<MotelGame.Registration.RegistrationDesk>(s).Single().ConfigurationValid():All<BaggageInspection>(s).Single().ConfigurationValid(),"Guest inspection / registration configuration");
            Check(All<MimicHunt>(s).Single().ConfigurationValid(),"Vent graph / six room branches / threat references");
            Check(All<NetworkManager>(s).Single().transform.parent==null,"NetworkManager is a scene root");
            Check(All<NetworkDoor>(s).Length==17,"Twelve room doors, four staff doors and backyard exit");
            var stations=All<ReceptionComputer>(s);Check(stations.Length==2&&stations.Count(c=>c.SurveillanceOnly)==1,"Independent registration and CCTV workstations");
            var network=All<NetworkObject>(s);var hashes=network.Select(n=>new SerializedObject(n).FindProperty("GlobalObjectIdHash")?.uintValue??0).ToArray();
            Check(hashes.All(h=>h!=0)&&hashes.Distinct().Count()==hashes.Length,"Unique nonzero in-scene NGO hashes: "+hashes.Length);
            var missing=new List<string>();
            foreach(var t in All<Transform>(s))
            {
                if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)missing.Add(PathOf(t)+" missing script");
                foreach(var c in t.GetComponents<MonoBehaviour>().Where(c=>c!=null))
                {
                    var so=new SerializedObject(c);var p=so.GetIterator();while(p.NextVisible(true))
                        if(p.propertyType==SerializedPropertyType.ObjectReference&&p.objectReferenceValue==null&&p.objectReferenceInstanceIDValue!=0)missing.Add(PathOf(t)+":"+p.propertyPath);
                }
            }
            Check(missing.Count==0,"No missing scripts or broken serialized references "+string.Join("; ",missing));
            float Distance(Vector3 a,Vector3 b,int mask,string label)
            {
                var p=new NavMeshPath();bool valid=NavMesh.SamplePosition(a,out var start,.4f,mask)&&NavMesh.SamplePosition(b,out var end,.4f,mask)&&NavMesh.CalculatePath(start.position,end.position,mask,p)&&p.status==NavMeshPathStatus.PathComplete;
                Check(valid,label+" navigable");if(!valid)lines.Add("Path corners: "+string.Join("; ",p.corners.Select(c=>c.ToString())));float length=0;for(int i=1;i<p.corners.Length;i++)length+=Vector3.Distance(p.corners[i-1],p.corners[i]);lines.Add(label+": "+length.ToString("F2")+" m / "+(length/4).ToString("F1")+" s walking");return length;
            }
            foreach(var r in allRooms)
            {
                var so=new SerializedObject(r.Door);var center=so.FindProperty("sweepCenter").vector3Value;var half=so.FindProperty("sweepSize").vector3Value*.5f;
                var p=r.Door.transform.InverseTransformPoint(r.DoorInside.position);
                Check(Mathf.Abs(p.x-center.x)>half.x+.55f||Mathf.Abs(p.z-center.z)>half.z+.55f,r.RoomNumber+" interior NPC approach clears door swing including stopping tolerance");
            }
            var rec=All<ReceptionDesk>(s).Single();var lobby=rec.QueuePoints[0].position;
            foreach(var r in allRooms)
            {
                Distance(lobby,r.DoorOutside.position,1,"Lobby -> "+r.RoomNumber+" (public areas only)");
                Distance(r.DoorOutside.position,r.GuestIdle.position,1|(1<<r.NavigationArea),r.RoomNumber+" assigned-room route");
                var d=r.CourtyardDoor;var center=d.transform.TransformPoint(new Vector3(0,0,-.51f));
                Distance(r.DoorInside.position,center-d.transform.right*.95f,1<<r.NavigationArea,r.RoomNumber+" interior door-to-door (private area only)");
                var n=All<MimicHunt>(s).Single().Nodes.Single(n=>n.room==r);Distance(r.GuestIdle.position,n.landing.position,NavMesh.AllAreas,r.RoomNumber+" guest -> vent landing");
            }
            Distance(lobby,new Vector3(27,0,-1),NavMesh.AllAreas,"Lobby -> Storage");
            Distance(lobby,new Vector3(20.3f,0,23.4f),NavMesh.AllAreas,"Lobby -> Maintenance");
            Distance(allRooms[2].GuestIdle.position,new Vector3(-2,0,30.8f),NavMesh.AllAreas,"103 -> Dumpster");
            Distance(allRooms[5].GuestIdle.position,new Vector3(-2,0,30.8f),NavMesh.AllAreas,"106 -> Dumpster");
            Distance(new Vector3(20.3f,0,23.4f),new Vector3(14.5f,0,11.7f),NavMesh.AllAreas,"Maintenance -> Courtyard");
            // Continuous graph, not just reciprocal local links.
            var nodes=All<MimicHunt>(s).Single().Nodes;var visited=new HashSet<VentNode>();var todo=new Queue<VentNode>();todo.Enqueue(nodes[0]);while(todo.Count>0){var n=todo.Dequeue();if(!visited.Add(n))continue;foreach(var neighbour in n.neighbours)todo.Enqueue(neighbour);}
            Check(visited.Count==nodes.Length,"Vent graph connected: "+nodes.Length+" nodes");
            Physics.SyncTransforms();
            foreach(string kind in new[]{"Wall","Floor"})
            {
                var boxes=All<BoxCollider>(s).Where(c=>c.name==kind&&PathOf(c.transform).Contains("/01_Architecture/")).ToArray();var overlaps=new List<string>();
                for(int i=0;i<boxes.Length;i++)for(int j=i+1;j<boxes.Length;j++)
                {
                    var a=boxes[i].bounds;var b=boxes[j].bounds;var size=Vector3.Min(a.max,b.max)-Vector3.Max(a.min,b.min);
                    if(size.x>.001f&&size.y>.001f&&size.z>.001f)overlaps.Add(PathOf(boxes[i].transform)+" / "+PathOf(boxes[j].transform));
                }
                Check(overlaps.Count==0,kind+" segments have no duplicate volume: "+string.Join("; ",overlaps));
            }
            var scanner=All<BaggageInspection>(s).SingleOrDefault();var sight=new List<string>();
            if(scanner!=null)foreach(var eye in new[]{new Vector3(23,1.68f,-10.31f),new Vector3(24.75f,1.68f,-12.22f)})
                foreach(var target in new[]{scanner.Controls.ClosestPoint(eye),scanner.Controls.bounds.center})
                {var d=target-eye;sight.Add(eye+" -> "+target+" distance="+d.magnitude+" hits="+string.Join(",",Physics.RaycastAll(eye,d.normalized,d.magnitude,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance).Select(h=>PathOf(h.collider.transform))));}
            File.WriteAllText(Reports+"/analyzer-sightlines.txt",string.Join("\n",sight));
            var visible=new List<string>();var targets=new List<Vector3>();
            foreach(float z in new[]{-1.6f,25f})foreach(float x in new[]{.5f,3f,8.6f,14.3f,16.5f})targets.Add(new Vector3(x,1.65f,z));
            for(float x=17.7f;x<=29.1f;x+=.6f)for(float z=-14.1f;z<=-4.6f;z+=.6f)
                foreach(var target in targets){var eye=new Vector3(x,1.65f,z);if(!Physics.Linecast(eye,target,~0,QueryTriggerInteraction.Ignore))visible.Add(eye+" -> "+target);}
            Check(visible.Count==0,"Lobby eye-height grid has no direct corridor view ("+visible.Count+" leaks)");if(visible.Count>0)lines.AddRange(visible.Take(10));
            // Each actual leaf must clear the environment throughout its 90-degree swing.
            var environment=All<Collider>(s).Where(c=>c.enabled&&c.gameObject.activeInHierarchy&&!c.isTrigger&&c.GetComponentInParent<NetworkDoor>()==null&&c.GetComponentInParent<GuestLifecycle>()==null).ToArray();
            foreach(var door in All<NetworkDoor>(s))
            {
                var hinge=Ref<Transform>(door,"hinge");var leaf=Ref<BoxCollider>(door,"leaf");var original=hinge.localRotation;var hits=new HashSet<string>();
                for(int angle=0;angle<=90;angle+=5)
                {
                    hinge.localRotation=Quaternion.Euler(0,angle,0);Physics.SyncTransforms();
                    foreach(var c in environment)
                        if(c.bounds.Intersects(leaf.bounds)&&Physics.ComputePenetration(leaf,leaf.transform.position,leaf.transform.rotation,c,c.transform.position,c.transform.rotation,out _,out float depth)&&depth>.01f)hits.Add(PathOf(c.transform));
                }
                hinge.localRotation=original;Check(hits.Count==0,door.name+" sweep: "+string.Join(", ",hits));
            }
            Physics.SyncTransforms();
            var grid=new List<string>();
            for(float z=6.4f;z>=-.4f;z-=.2f){string row=z.ToString("F1")+" ";for(float x=11.2f;x<17.6f;x+=.2f){var p=new Vector3(x,.02f,z);row+=NavMesh.SamplePosition(p,out var hit,.045f,NavMesh.AllAreas)?(hit.mask==8?"R":hit.mask==1?"w":"?"):"#";}grid.Add(row);}
            File.WriteAllText(Reports+"/room101-nav-grid.txt",string.Join("\n",grid));
            Directory.CreateDirectory(Reports);File.WriteAllText(Reports+"/scene-validation.txt",string.Join("\n",lines));
            if(lines.Any(l=>l.StartsWith("FAIL")))throw new Exception("V4 validation failed; see "+Reports+"/scene-validation.txt");
            File.WriteAllText(Reports+"/validation-passed.txt",DateTime.Now.ToString("O"));
        }
        [MenuItem("Tools/Motel/Level V4/Build validation candidate (Mac)")]
        public static void BuildCandidate()
        {
            BuildMacTo("Builds/Validation");
        }
        public static void BuildMacTo(string output)
        {
            Validate();Directory.CreateDirectory(output);
            var snapshots=new Dictionary<string,byte[]>();
            foreach(var path in new[]{"Assets/Settings/Mobile_RPAsset.asset","Assets/Settings/DefaultVolumeProfile.asset","Assets/Settings/PC_RPAsset.asset","Assets/Settings/UniversalRenderPipelineGlobalSettings.asset","ProjectSettings/ProjectSettings.asset","ProjectSettings/UnityConnectSettings.asset","ProjectSettings/EditorBuildSettings.asset"})
                if(File.Exists(path))snapshots[path]=File.ReadAllBytes(path);
            try
            {
                var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=output+"/MotelGame.app",target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
                File.WriteAllText(Reports+"/candidate-build.txt",result.summary.result+" | errors="+result.summary.totalErrors+" warnings="+result.summary.totalWarnings+" | "+DateTime.Now.ToString("O")+" | "+output);
                if(result.summary.result!=BuildResult.Succeeded||result.summary.totalErrors>0)throw new Exception("V4 Mac build failed");
            }
            finally{foreach(var pair in snapshots)File.WriteAllBytes(pair.Key,pair.Value);AssetDatabase.Refresh();}
        }
    }
}
