using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using MotelGame.Interactions;

namespace MotelGame.Editor
{
    public static class Room101DoorSetup
    {
        const string ScenePath="Assets/Game/Scenes/Motel_Blockout_V3.unity";
        const string DoorPath="Assets/Game/Prefabs/NetworkDoor.prefab";
        const string PlayerPath="Assets/Game/Prefabs/NetworkPlayer.prefab";
        const string ReportDir="Documentation/NetworkedInteractions";
        const string Request="Library/MotelDoorSetup.request";
        const string BuildRequest="Library/MotelDoorBuild.request";
        [InitializeOnLoadMethod]
        static void Initialize(){EditorApplication.delayCall+=Check;}
        static void Check()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)
            {EditorApplication.delayCall+=Check;return;}
            bool installed=true;
            if(File.Exists(Request)){var hash=File.ReadAllText(Request).Trim();File.Delete(Request);installed=Install(hash);}
            if(installed&&File.Exists(BuildRequest)){File.Delete(BuildRequest);Build();}
        }
        static string Hash(string path)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();}
        static void Set(UnityEngine.Object obj,string property,UnityEngine.Object value)
        {var s=new SerializedObject(obj);s.FindProperty(property).objectReferenceValue=value;s.ApplyModifiedPropertiesWithoutUndo();}
        static GameObject Cube(string name,Transform parent,Vector3 position,Vector3 scale,Material material,bool solid)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.transform.SetParent(parent,false);
            obj.transform.localPosition=position;obj.transform.localScale=scale;obj.GetComponent<Renderer>().sharedMaterial=material;
            if(!solid)UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
            return obj;
        }
        static bool Install(string expectedHash)
        {
            Scene previous=SceneManager.GetActiveScene();Scene scene=default;bool opened=false;
            try
            {
                if(Hash(ScenePath)!=expectedHash)throw new Exception("Scene changed since inspection; setup cancelled.");
                scene=SceneManager.GetSceneByPath(ScenePath);
                if(scene.IsValid()&&scene.isLoaded&&scene.isDirty)throw new Exception("Motel scene has unsaved changes. Save or discard them explicitly, then request setup again.");
                if(!scene.IsValid()||!scene.isLoaded){scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);opened=true;}
                SceneManager.SetActiveScene(scene);
                if(File.Exists(DoorPath))throw new Exception("Door prefab already exists; refusing to overwrite.");
                var motel=scene.GetRootGameObjects().Single(g=>g.name=="Motel");
                var room=motel.transform.Find("Rooms/Room101");var old=room.Find("Door_parked_open_90deg");
                if(old==null)throw new Exception("Expected Room101 placeholder not found.");
                var root=new GameObject("NetworkDoor");var no=root.AddComponent<NetworkObject>();
                no.SynchronizeTransform=false;no.AutoObjectParentSync=false;
                var hinge=new GameObject("Hinge").transform;hinge.SetParent(root.transform,false);
                var wood=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/MotelBlockoutV3/Door.mat");
                var metal=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/MotelBlockoutV3/Metal.mat");
                var leaf=Cube("DoorLeaf",hinge,new Vector3(0,1.1f,-.51f),new Vector3(.05f,2.15f,1.02f),wood,true);
                Cube("Handle_Outside",hinge,new Vector3(.065f,1.05f,-.83f),new Vector3(.08f,.06f,.14f),metal,false);
                Cube("Handle_Inside",hinge,new Vector3(-.065f,1.05f,-.83f),new Vector3(.08f,.06f,.14f),metal,false);
                var door=root.AddComponent<NetworkDoor>();Set(door,"hinge",hinge);Set(door,"leaf",leaf.GetComponent<BoxCollider>());
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,DoorPath);UnityEngine.Object.DestroyImmediate(root);
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,room);instance.name="Room101Door";
                instance.transform.localPosition=new Vector3(20.58f,0,13.16f);
                UnityEngine.Object.DestroyImmediate(old.gameObject);
                var player=PrefabUtility.LoadPrefabContents(PlayerPath);
                try
                {
                    var interactor=player.AddComponent<PlayerInteractor>();interactor.enabled=false;
                    Set(interactor,"playerCamera",player.GetComponentInChildren<Camera>(true));
                    PrefabUtility.SaveAsPrefabAsset(player,PlayerPath);
                }
                finally{PrefabUtility.UnloadPrefabContents(player);}
                var registry=AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/Game/Prefabs/NetworkPrefabs.asset");
                if(!registry.PrefabList.Any(p=>p.Prefab==prefab)){registry.Add(new NetworkPrefab{Prefab=prefab});EditorUtility.SetDirty(registry);AssetDatabase.SaveAssetIfDirty(registry);}
                // Save in the real scene, not a temporary scene with a different GUID.
                if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new Exception("Scene save failed.");
                instance.GetComponent<NetworkObject>().SendMessage("OnValidate",SendMessageOptions.DontRequireReceiver);
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.GetComponent<NetworkObject>());
                EditorSceneManager.SaveScene(scene,ScenePath);
                ValidateGeometry(scene);
                File.WriteAllText(ReportDir+"/setup-validation.txt","PASS: Door/player prefabs and Room101 scene saved in Unity "+Application.unityVersion+".\nScene SHA256: "+Hash(ScenePath)+"\nPASS: Door sweep 0..90 degrees, no environment penetrations; closed/open CharacterController clearance checked.\n");
                return true;
            }
            catch(Exception e){File.WriteAllText(ReportDir+"/setup-error.txt",e.ToString());Debug.LogException(e);return false;}
            finally
            {
                if(opened&&scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
                if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
            }
        }
        [MenuItem("Tools/Motel/Validate Room101 Door Geometry")]
        public static void ValidateSaved() => MultipleGuestsSetup.Validate();
        static void ValidateGeometry(Scene original)
        {
            // Work in a preview copy so the user's live scene remains unchanged.
            var scene=EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var roots=scene.GetRootGameObjects();var door=roots.SelectMany(g=>g.GetComponentsInChildren<NetworkDoor>(true)).Single();
                var hinge=door.transform.Find("Hinge");var leaf=hinge.Find("DoorLeaf").GetComponent<BoxCollider>();
                var obstacles=roots.SelectMany(g=>g.GetComponentsInChildren<BoxCollider>(true)).Where(c=>!c.transform.IsChildOf(door.transform)).ToArray();
                for(int angle=0;angle<=90;angle++)
                {
                    hinge.localRotation=Quaternion.Euler(0,angle,0);Physics.SyncTransforms();
                    foreach(var obstacle in obstacles)
                        if(Physics.ComputePenetration(leaf,leaf.transform.position,leaf.transform.rotation,obstacle,obstacle.transform.position,obstacle.transform.rotation,out _,out _))
                            throw new Exception("Door intersects "+obstacle.name+" at "+angle);
                }
                // A probe capsule tests closed blocking and open passage in this disposable scene.
                var probe=new GameObject("DoorClearanceProbe");SceneManager.MoveGameObjectToScene(probe,scene);
                var cc=probe.AddComponent<CharacterController>();cc.height=1.8f;cc.radius=.3f;cc.center=Vector3.up*.9f;cc.skinWidth=.03f;cc.stepOffset=.2f;cc.minMoveDistance=0;
                foreach(bool open in new[]{false,true})
                {
                    cc.enabled=false;probe.transform.position=new Vector3(22.2f,.05f,12.65f);cc.enabled=true;
                    hinge.localRotation=Quaternion.Euler(0,open?90:0,0);Physics.SyncTransforms();
                    for(int i=0;i<75;i++)cc.Move(new Vector3(-4f/60,-2f/60,0));
                    if(open && probe.transform.position.x>19.2f)throw new Exception("Open door does not allow passage.");
                    if(!open && probe.transform.position.x<20.8f)throw new Exception("Closed door does not block player.");
                }
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        [MenuItem("Tools/Motel/Build Networked Interactions Development Player")]
        public static void Build() => PlaytestBuild.Build();
    }
}
