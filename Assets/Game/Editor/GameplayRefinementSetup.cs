using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using MotelGame.Horror;
namespace MotelGame.Editor
{
    [InitializeOnLoad]
    public static partial class GameplayRefinementSetup
    {
        const string Reports="Documentation/GameplayRefinement", ScenePath="Assets/Game/Scenes/Motel_Layout_V4.unity";
        static double next;
        static GameplayRefinementSetup()=>EditorApplication.update+=Poll;
        static T[] All<T>(Scene s) where T:Component=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).ToArray();
        static string PathOf(Transform t)=>t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<next||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||BuildPipeline.isBuildingPlayer||File.Exists("Library/MotelServiceRefresh.request"))return;
            next=EditorApplication.timeSinceStartup+1;
            foreach(string action in new[]{"Inspect","Install","Navigation","GroundUV","Validate","RestoreSavedReferences","Build"})
            {
                string request="Library/MotelRefinement"+action+".request";
                if(!File.Exists(request))continue;File.Delete(request);Directory.CreateDirectory(Reports);
                try{if(action=="Install")Install();else if(action=="Navigation")Navigation();else if(action=="GroundUV")GroundUV();else if(action=="Validate")UndergroundSetup.Validate();else if(action=="RestoreSavedReferences")RestoreSavedReferences();else if(action=="Build")Build();else Inspect();File.WriteAllText(Reports+"/"+action+".done",DateTime.Now.ToString("O"));}
                catch(Exception e){File.WriteAllText(Reports+"/editor-error.txt",e.ToString());Debug.LogException(e);}
            }
        }
        static void Inspect()
        {
            var s=SceneManager.GetActiveScene();if(s.path!=ScenePath)throw new Exception("Open V4");Physics.SyncTransforms();
            var lines=new List<string>{s.path+" dirty="+s.isDirty};
            foreach(var r in All<Renderer>(s).Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.bounds.min.y<1.5f&&r.bounds.max.y>-.5f&&r.bounds.max.z>5&&r.bounds.min.z<19&&r.bounds.max.x>0&&r.bounds.min.x<18))
                lines.Add("RENDER "+PathOf(r.transform)+" "+r.bounds+" collider="+(r.TryGetComponent<Collider>(out var c)?c.enabled.ToString():"NONE"));
            foreach(var h in All<VentHatch>(s))
            {
                lines.Add("ENTRANCE "+h.room.RoomNumber+" root="+h.transform.position+" yaw="+h.transform.eulerAngles.y);
                foreach(var c in h.room.transform.parent.parent.GetComponentsInChildren<Collider>())lines.Add("ROOM "+PathOf(c.transform)+" "+c.bounds);
            }
            var hunt=All<MimicHunt>(s).Single();var so=new SerializedObject(hunt);lines.Add("SPEED "+so.FindProperty("ductSpeed").floatValue+" / "+so.FindProperty("retreatSpeed").floatValue);
            File.WriteAllLines(Reports+"/inspection.txt",lines);
        }
    }
}
