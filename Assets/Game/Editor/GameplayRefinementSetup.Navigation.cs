using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using MotelGame.Service;
using MotelGame.Horror;
using MotelGame.Interactions;
namespace MotelGame.Editor
{
    public static partial class GameplayRefinementSetup
    {
        static void Navigation()
        {
            var s=SceneManager.GetActiveScene();if(s.path!=ScenePath||s.isDirty)throw new Exception("Open saved V4");
            foreach(var h in All<VentHatch>(s).Where(h=>h.room.RoomNumber=="103"||h.room.RoomNumber=="106"))
            {
                var room=h.room.transform.parent.parent;
                var table=room.GetComponentsInChildren<Transform>().Single(t=>t.name=="BedsideTable"&&(room.InverseTransformPoint(t.position).z<4||Mathf.Abs(room.InverseTransformPoint(t.position).x-3)<.01f));
                h.upper.localPosition=new Vector3(5.15f,.025f,5.55f);h.route[0].position=h.upper.position;
                table.position=room.TransformPoint(new Vector3(3f,.3f,5.65f));
            }
            File.WriteAllText(Reports+"/approach-adjustments.txt","103/106: only the front bedside tables moved to room-local (3,0.3,5.65); all beds, headboards, desks, wardrobes and other furniture preserved. Upper approach x=5.15. NavMesh rebaked with private-room areas retained.");
            var surface=All<NavMeshSurface>(s).Single();surface.RemoveData();Physics.SyncTransforms();
            var marks=All<NetworkDoor>(s).Select(d=>new NavMeshBuildMarkup{root=d.transform,ignoreFromBuild=true})
                .Concat(All<GuestLifecycle>(s).Select(g=>new NavMeshBuildMarkup{root=g.transform,ignoreFromBuild=true}))
                .Concat(All<CleaningSpot>(s).Where(c=>c.name.StartsWith("Trash")).Select(c=>new NavMeshBuildMarkup{root=c.transform,ignoreFromBuild=true}))
                .Concat(All<HuntEquipment>(s).Select(e=>new NavMeshBuildMarkup{root=e.transform,ignoreFromBuild=true}))
                .Concat(All<ServiceRoom>(s).Select(r=>new NavMeshBuildMarkup{root=GameObject.Find("Motel_Layout_V4/01_Architecture/Room"+r.RoomNumber+"/Floor").transform,overrideArea=true,area=r.NavigationArea})).ToList();
            // Preserve the authored staff-only area as well as each private guest-room area.
            // Otherwise public NPC paths take a shortcut through the closed Storage doors.
            foreach(var name in new[]{"Storage","Maintenance"})
                marks.Add(new NavMeshBuildMarkup{root=GameObject.Find("Motel_Layout_V4/01_Architecture/"+name+"/Floor").transform,overrideArea=true,area=9});
            marks.Add(new NavMeshBuildMarkup{root=GameObject.Find("Motel_Layout_V4/05_Underground").transform,ignoreFromBuild=true});
            var sources=new List<NavMeshBuildSource>();
            foreach(var root in s.GetRootGameObjects()){var batch=new List<NavMeshBuildSource>();UnityEngine.AI.NavMeshBuilder.CollectSources(root.transform,~0,NavMeshCollectGeometry.PhysicsColliders,0,marks,batch);sources.AddRange(batch);}
            var settings=NavMesh.GetSettingsByID(0);settings.agentRadius=.4f;settings.agentHeight=1.8f;settings.agentClimb=.15f;settings.overrideVoxelSize=true;settings.voxelSize=.025f;settings.minRegionArea=.1f;
            var data=UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings,sources,new Bounds(new Vector3(12,2,5),new Vector3(80,12,85)),Vector3.zero,Quaternion.identity);
            if(data==null)throw new Exception("Refinement NavMesh bake failed");
            const string path="Assets/Game/Navigation/Motel_Refinement_NavMesh.asset";var saved=AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            if(saved==null){AssetDatabase.CreateAsset(data,path);saved=data;}else{EditorUtility.CopySerialized(data,saved);Delete(data);EditorUtility.SetDirty(saved);}
            var so=new SerializedObject(surface);so.FindProperty("m_NavMeshData").objectReferenceValue=saved;so.ApplyModifiedPropertiesWithoutUndo();surface.AddData();
            EditorSceneManager.MarkSceneDirty(s);EditorSceneManager.SaveScene(s);AssetDatabase.SaveAssets();
        }
    }
}
