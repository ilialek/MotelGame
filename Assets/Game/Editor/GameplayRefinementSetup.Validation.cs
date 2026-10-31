using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using MotelGame.Horror;
namespace MotelGame.Editor
{
    public static partial class GameplayRefinementSetup
    {
        public static void ValidateRoutes(Scene scene,List<string> lines)
        {
            void Check(bool ok,string text)=>lines.Add((ok?"PASS ":"FAIL ")+text);
            // Renderer-only environment ground must be tested too, not just Physics colliders.
            var temporary=new List<Collider>();
            foreach(var r in All<MeshRenderer>(scene).Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.name.StartsWith("Ground")&&r.GetComponent<Collider>()==null))
            {var c=r.gameObject.AddComponent<MeshCollider>();c.sharedMesh=r.GetComponent<MeshFilter>().sharedMesh;temporary.Add(c);}
            Physics.SyncTransforms();
            try
            {
                foreach(var h in All<VentHatch>(scene))
                {
                    h.hinge.localRotation=Quaternion.Euler(90,0,0);Physics.SyncTransforms();
                    var collisions=new HashSet<string>();float length=0;
                    for(int j=0;j<h.route.Length-1;j++)
                    {
                        var a=h.route[j].position;var b=h.route[j+1].position;length+=Vector3.Distance(a,b);
                        int count=Mathf.CeilToInt(Vector3.Distance(a,b)/.06f);
                        for(int i=0;i<=count;i++)
                        {
                            var foot=Vector3.Lerp(a,b,(float)i/count);
                            foreach(var c in Physics.OverlapCapsule(foot+Vector3.up*.32f,foot+Vector3.up*.6f,.285f,~0,QueryTriggerInteraction.Ignore))collisions.Add(PathOf(c.transform));
                            Check(Physics.Raycast(foot+Vector3.up*.12f,Vector3.down,.32f,~0,QueryTriggerInteraction.Ignore),"Straight ramp support "+h.room.RoomNumber+" "+j+"/"+i);
                        }
                    }
                    Check(collisions.Count==0,"Render + collider clearance "+h.room.RoomNumber+" "+string.Join("; ",collisions));
                    var a0=h.route[2].position;var a1=h.route[^2].position;
                    Check(Mathf.Abs(Vector3.SignedAngle(Vector3.ProjectOnPlane(a1-a0,Vector3.up),a1-a0,h.transform.right)-35)<.2f,"35 degree straight descent "+h.room.RoomNumber);
                    Check(h.route.Skip(1).All(t=>Mathf.Abs(t.localPosition.x-h.hinge.localPosition.x)<.001f),"No intermediate turn "+h.room.RoomNumber);
                    Check(Physics.CheckCapsule(h.route[1].position+Vector3.up*.32f,h.route[1].position+Vector3.up*1.48f,.28f,~0,QueryTriggerInteraction.Ignore),"Standing entry blocked "+h.room.RoomNumber);
                    var nav=new NavMeshPath();Check(NavMesh.CalculatePath(h.room.DoorOutside.position,h.room.GuestIdle.position,1|(1<<h.room.NavigationArea),nav)&&nav.status==NavMeshPathStatus.PathComplete,"Guest path retained "+h.room.RoomNumber);
                    h.hinge.localRotation=Quaternion.identity;
                    lines.Add("INFO "+h.room.RoomNumber+" approach-to-crossing "+length.ToString("F2")+"m; run "+(RampEnd-6.3f).ToString("F3")+"m; 35deg; clear width 1.10m");
                }
            }
            finally{foreach(var c in temporary)Delete(c);Physics.SyncTransforms();}
            foreach(var name in new[]{"Storage","Maintenance"})
            {
                var floor=GameObject.Find("Motel_Layout_V4/01_Architecture/"+name+"/Floor").transform;
                bool staff=NavMesh.SamplePosition(floor.position+Vector3.up*.15f,out var sample,2,1<<9);
                Check(staff,"Staff navigation area retained "+name);
                Check(staff&&!NavMesh.SamplePosition(sample.position,out var publicHit,.1f,1),"Public guests cannot shortcut through "+name);
            }
            File.WriteAllLines(Reports+"/geometry-validation.txt",lines);
        }
        static void RestoreSavedReferences()
        {
            var scene=SceneManager.GetActiveScene();if(scene.path!=ScenePath||scene.isDirty)throw new Exception("Save user changes before restoring original font references.");
            string before=File.ReadAllText(Path.Combine(File.ReadAllText(Reports+"/backup-location.txt").Trim(),ScenePath)),current=File.ReadAllText(ScenePath);
            const string pattern=@"--- !u!23 &(\d+)\n.*?(?=--- !u!|\z)";
            var originals=Regex.Matches(before,pattern,RegexOptions.Singleline).Cast<Match>().ToDictionary(x=>x.Groups[1].Value,x=>x.Value);
            const string font="  - {fileID: 10100, guid: 0000000000000000e000000000000000, type: 0}";int count=0;
            current=Regex.Replace(current,pattern,x=>{if(!originals.TryGetValue(x.Groups[1].Value,out var old)||!old.Contains(font))return x.Value;var restored=x.Value.Replace("  - {fileID: 0}",font);if(restored!=x.Value){if(restored!=old)throw new Exception("Unexpected original text renderer change");count++;}return restored;},RegexOptions.Singleline);
            File.WriteAllText(ScenePath,current);EditorSceneManager.OpenScene(ScenePath);File.WriteAllText(Reports+"/font-reference-preservation.txt",count.ToString());
        }
        static void Build()
        {
            if(new DriveInfo(Directory.GetCurrentDirectory()).AvailableFreeSpace<1500000000L)throw new Exception("Insufficient disk space for a safe candidate build");
            UndergroundSetup.Validate();
            const string folder="Builds/GameplayRefinementVerifiedCandidate";Directory.CreateDirectory(folder);
            var snapshots=Directory.GetFiles("ProjectSettings").Concat(Directory.GetFiles("Assets/Settings","*.asset")).ToDictionary(p=>p,File.ReadAllBytes);
            try
            {
                var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=folder+"/MotelGame.app",target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
                File.WriteAllText(Reports+"/candidate-build.txt",result.summary.result+" errors="+result.summary.totalErrors+" warnings="+result.summary.totalWarnings+" "+DateTime.Now.ToString("O"));
                if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Candidate build failed");
            }
            finally{foreach(var p in snapshots)if(!File.ReadAllBytes(p.Key).SequenceEqual(p.Value))File.WriteAllBytes(p.Key,p.Value);AssetDatabase.Refresh();}
        }
    }
}
