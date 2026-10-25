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
using MotelGame.Service;

namespace MotelGame.Editor
{
    public static class GuestCheckoutSetup
    {
        public const string Reports="Documentation/GuestCheckoutM9";
        const string InstallRequest="Library/MotelCheckoutSetup.request", ValidateRequest="Library/MotelCheckoutValidate.request";
        const string Prefab="Assets/Game/Prefabs/NetworkGuest.prefab";
        static double nextPoll;
        [InitializeOnLoadMethod] static void Init()=>EditorApplication.update+=Poll;
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<nextPoll)return;nextPoll=EditorApplication.timeSinceStartup+1;
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||File.Exists("Library/MotelServiceRefresh.request"))return;
            if(!File.Exists(InstallRequest)&&!File.Exists(ValidateRequest))return;
            Directory.CreateDirectory(Reports);
            try
            {
                if(File.Exists(InstallRequest)){var hash=File.ReadAllText(InstallRequest).Trim();File.Delete(InstallRequest);Install(hash);}
                if(File.Exists(ValidateRequest)){File.Delete(ValidateRequest);Validate();}
                if(File.Exists(Reports+"/editor-error.txt"))File.Move(Reports+"/editor-error.txt",Reports+"/resolved-editor-error-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt");
            }
            catch(Exception e){File.WriteAllText(Reports+"/editor-error.txt",e.ToString());Debug.LogException(e);}
        }
        static T[] All<T>(Scene scene) where T:Component=>scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).ToArray();
        static void Duration(GuestLifecycle guest)
        {
            var s=new SerializedObject(guest);s.FindProperty("stayDuration").floatValue=120;s.ApplyModifiedPropertiesWithoutUndo();
            if(PrefabUtility.IsPartOfPrefabInstance(guest))PrefabUtility.RecordPrefabInstancePropertyModifications(guest);
        }
        static void Install(string expected)
        {
            string path=MultipleGuestsSetup.ScenePath;
            using(var hash=SHA256.Create())
                if(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant()!=expected)throw new Exception("Scene changed since inspection. Recheck before installing M9.");
            var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.IsValid()||!scene.isLoaded;
            if(!opened&&scene.isDirty)throw new Exception("Save unsaved scene changes before configuring M9.");
            try
            {
                if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                var guests=All<GuestLifecycle>(scene);var reception=All<ReceptionDesk>(scene).Single();var service=All<GuestServiceRequests>(scene).Single();
                if(guests.Length!=5)throw new Exception("Unexpected guest pool.");
                var root=PrefabUtility.LoadPrefabContents(Prefab);
                try{Duration(root.GetComponent<GuestLifecycle>());PrefabUtility.SaveAsPrefabAsset(root,Prefab);}
                finally{PrefabUtility.UnloadPrefabContents(root);}
                foreach(var guest in guests)Duration(guest);
                var s=new SerializedObject(reception);s.FindProperty("serviceRequests").objectReferenceValue=service;s.ApplyModifiedPropertiesWithoutUndo();
                foreach(var label in All<WorldServiceText>(scene))label.RefreshMaterial();
                EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene,path))throw new Exception("M9 scene save failed.");
            }
            finally{if(opened&&scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);}
            Validate();
        }
        [MenuItem("Tools/Motel/Validate Guest Checkout M9")]
        public static void Validate()
        {
            Directory.CreateDirectory(Reports);var scene=EditorSceneManager.OpenPreviewScene(MultipleGuestsSetup.ScenePath);
            try
            {
                var reception=All<ReceptionDesk>(scene).Single();var service=All<GuestServiceRequests>(scene).Single();var guests=All<GuestLifecycle>(scene);var rooms=All<ServiceRoom>(scene);
                if(!reception.ConfigurationValid()||!service.ConfigurationValid()||guests.Length!=5||rooms.Length!=4)throw new Exception("M9 configuration invalid.");
                if(new SerializedObject(reception).FindProperty("serviceRequests").objectReferenceValue!=service)throw new Exception("Reception request system reference mismatch.");
                if(guests.Any(g=>!g.ConfigurationValid()))throw new Exception("Missing guest reference or invalid stay duration.");
                float prefabDuration=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab).GetComponent<GuestLifecycle>().StayDuration;
                if(!(prefabDuration>=1)||float.IsInfinity(prefabDuration))throw new Exception("Prefab stay duration invalid.");
                if(guests.Any(g=>g.ReceptionPoint!=guests[0].ReceptionPoint))throw new Exception("Guests must share coordinated Reception position.");
                foreach(var t in All<Transform>(scene))if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)throw new Exception("Missing script: "+t.name);
                var surface=All<NavMeshSurface>(scene).Single();surface.AddData();var routes=new List<string>();
                foreach(var room in rooms)
                {
                    if(!room.ConfigurationValid(out var error))throw new Exception(error);
                    var points=new[]{room.GuestIdle,room.DoorInside,room.DoorOutside,guests[0].ReceptionPoint,guests[0].DeparturePoint};
                    for(int i=1;i<points.Length;i++)
                    {
                        var a=points[i-1];var b=points[i];
                        if(!NavMesh.SamplePosition(a.position,out var start,.35f,NavMesh.AllAreas)||!NavMesh.SamplePosition(b.position,out var end,.35f,NavMesh.AllAreas))throw new Exception("Checkout point off NavMesh.");
                        var route=new NavMeshPath();if(!NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,route)||route.status!=NavMeshPathStatus.PathComplete)throw new Exception("Incomplete checkout route: "+room.RoomNumber);
                        float distance=0;for(int j=1;j<route.corners.Length;j++)distance+=Vector3.Distance(route.corners[j-1],route.corners[j]);
                        routes.Add(room.RoomNumber+": "+a.name+" -> "+b.name+": complete, "+distance.ToString("F2")+"m");
                    }
                }
                File.WriteAllLines(Reports+"/navigation-validation.txt",routes);
                File.WriteAllText(Reports+"/setup-validation.txt","PASS: Saved scene reopened. Four rooms / five guests, shared Reception slot, exact request system reference, valid configurable durations, all room references, no missing scripts; 16 checkout/departure path segments complete.\nPrefab stay: "+prefabDuration+" seconds. Scene stays: "+string.Join(", ",guests.Select(g=>g.name+"="+g.StayDuration))+"\n");
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
