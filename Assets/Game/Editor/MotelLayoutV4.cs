using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.AI.Navigation;
using MotelGame.Service;
using MotelGame.Horror;
using MotelGame.Inspection;
using MotelGame.Interactions;

namespace MotelGame.Editor
{
    [InitializeOnLoad]
    public static partial class MotelLayoutV4
    {
        public const string ScenePath="Assets/Game/Scenes/Motel_Layout_V4.unity";
        const string OldScene="Assets/Game/Scenes/Motel_Blockout_V3.unity";
        const string Reports="Documentation/LevelM16";
        static double next;
        static MotelLayoutV4()=>EditorApplication.update+=Poll;
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<next||BuildPipeline.isBuildingPlayer||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||File.Exists("Library/MotelServiceRefresh.request"))return;
            next=EditorApplication.timeSinceStartup+1;
            foreach(var action in new[]{"Inspect","Setup","Validate","Render","Build"})
            {
                var path="Library/MotelLayout"+action+".request";
                if(!File.Exists(path))continue;
                File.Delete(path);Directory.CreateDirectory(Reports);
                try { if(action=="Setup")Install();else if(action=="Validate")Validate();else if(action=="Render")Render();else if(action=="Build")BuildCandidate();else Inspect(); }
                catch(Exception e){File.WriteAllText(Reports+"/editor-error.txt",e.ToString());Debug.LogException(e);}
            }
        }
        static T[] All<T>(Scene s) where T:Component => s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).ToArray();
        static string PathOf(Transform t)=>t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;
        [Serializable] class ObjectRow
        {public string path;public Vector3 position,rotation,scale;public bool active;public string[] components;public string prefab;public List<string> refs=new();public string[] materials;}
        [Serializable] class Inspection {public string scene;public List<ObjectRow> objects=new();}
        [MenuItem("Tools/Motel/Level V4/Inspect current scene")]
        public static void Inspect()
        {
            Directory.CreateDirectory(Reports);
            var s=SceneManager.GetActiveScene();
            var report=new Inspection{scene=s.path};
            foreach(var t in All<Transform>(s))
            {
                var row=new ObjectRow{path=PathOf(t),position=t.position,rotation=t.eulerAngles,scale=t.lossyScale,active=t.gameObject.activeSelf,
                    components=t.GetComponents<Component>().Where(c=>c!=null).Select(c=>c.GetType().FullName).ToArray(),
                    prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject),
                    materials=t.GetComponent<Renderer>() != null ? t.GetComponent<Renderer>().sharedMaterials.Select(m=>AssetDatabase.GetAssetPath(m)).ToArray() : Array.Empty<string>()};
                foreach(var c in t.GetComponents<MonoBehaviour>().Where(c=>c!=null))
                {
                    var so=new SerializedObject(c);var p=so.GetIterator();
                    while(p.NextVisible(true))if(p.propertyType==SerializedPropertyType.ObjectReference&&p.objectReferenceValue!=null)
                    {var v=p.objectReferenceValue;row.refs.Add(c.GetType().Name+"."+p.propertyPath+"="+(v is Component cmp?PathOf(cmp.transform):v is GameObject go?PathOf(go.transform):AssetDatabase.GetAssetPath(v)));}
                }
                report.objects.Add(row);
            }
            File.WriteAllText(Reports+"/inspection.json",JsonUtility.ToJson(report,true));
            File.WriteAllText(Reports+"/inspection.txt",string.Join("\n",report.objects.Select(o=>o.path+" | "+o.position+" | "+o.rotation+" | "+string.Join(",",o.components))));
        }
    }
}
