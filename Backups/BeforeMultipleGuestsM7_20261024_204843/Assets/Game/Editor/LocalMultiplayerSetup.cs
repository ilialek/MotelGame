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
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using MotelGame.Networking;

namespace MotelGame.Editor
{
    public static class LocalMultiplayerSetup
    {
        const string ScenePath = "Assets/Game/Scenes/Motel_Blockout_V3.unity";
        const string PrefabPath = "Assets/Game/Prefabs/NetworkPlayer.prefab";
        const string ReportDir = "Documentation/LocalMultiplayer";
        const string Request = "Library/MotelMultiplayerSetup.request";
        const string BuildRequest = "Library/MotelMultiplayerBuild.request";
        [InitializeOnLoadMethod]
        static void Initialize() { EditorApplication.delayCall += CheckRequests; }
        static void CheckRequests()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            { EditorApplication.delayCall += CheckRequests; return; }
            if (File.Exists(Request)) { string hash=File.ReadAllText(Request).Trim();File.Delete(Request);Install(hash); }
            if (File.Exists(BuildRequest)) { File.Delete(BuildRequest);BuildDevelopmentPlayer(); }
        }
        static string Hash(string path)
        {
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        static void Set(UnityEngine.Object obj,string name,UnityEngine.Object value)
        {
            var so=new SerializedObject(obj);so.FindProperty(name).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();
        }
        static void Install(string expectedHash)
        {
            const string temp="Assets/Game/Scenes/Motel_Multiplayer_Installation.unity";
            Scene copy=default;Scene previous=SceneManager.GetActiveScene();
            try
            {
                if(Hash(ScenePath)!=expectedHash)throw new Exception("Scene changed since inspection; installation stopped.");
                if(File.Exists(PrefabPath) || File.Exists(temp))throw new Exception("Installation target already exists; refusing to overwrite.");
                File.Copy(ScenePath,temp);AssetDatabase.ImportAsset(temp,ImportAssetOptions.ForceSynchronousImport);
                copy=EditorSceneManager.OpenScene(temp,OpenSceneMode.Additive);SceneManager.SetActiveScene(copy);
                var motel=copy.GetRootGameObjects().Single(g=>g.name=="Motel");
                var original=motel.GetComponentInChildren<FirstPersonController>(true).gameObject;
                var originalCamera=original.GetComponentInChildren<Camera>(true);
                var player=UnityEngine.Object.Instantiate(original);player.name="NetworkPlayer";player.transform.SetParent(null);
                player.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                var movement=player.GetComponent<FirstPersonController>();movement.enabled=false;
                var capsule=player.GetComponent<CharacterController>();capsule.enabled=false;
                var camera=player.GetComponentInChildren<Camera>(true);camera.enabled=false;
                var listener=camera.GetComponent<AudioListener>();listener.enabled=false;
                player.AddComponent<NetworkObject>();
                var nt=player.AddComponent<NetworkTransform>();nt.AuthorityMode=NetworkTransform.AuthorityModes.Owner;
                nt.SyncRotAngleX=false;nt.SyncRotAngleY=true;nt.SyncRotAngleZ=false;
                nt.SyncScaleX=false;nt.SyncScaleY=false;nt.SyncScaleZ=false;
                nt.Interpolate=true;nt.PositionThreshold=.01f;nt.RotAngleThreshold=.5f;
                var body=GameObject.CreatePrimitive(PrimitiveType.Capsule);body.name="RemoteBody";
                body.transform.SetParent(player.transform,false);body.transform.localPosition=new Vector3(0,.9f,0);
                body.transform.localScale=new Vector3(.6f,.9f,.6f);
                UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
                body.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/MotelBlockoutV3/Scale.mat");
                var owner=player.AddComponent<NetworkPlayer>();Set(owner,"playerCamera",camera);Set(owner,"playerListener",listener);Set(owner,"body",body.GetComponent<Renderer>());
                var prefab=PrefabUtility.SaveAsPrefabAsset(player,PrefabPath);UnityEngine.Object.DestroyImmediate(player);
                if(prefab==null)throw new Exception("Player prefab save failed.");
                // Retain an idle lobby view, but remove the scene's offline player.
                originalCamera.transform.SetParent(original.transform.parent,true);
                originalCamera.transform.SetPositionAndRotation(new Vector3(7,1.65f,1.8f),Quaternion.identity);
                originalCamera.name="LobbyPreviewCamera";originalCamera.enabled=true;
                UnityEngine.Object.DestroyImmediate(originalCamera.GetComponent<AudioListener>());
                UnityEngine.Object.DestroyImmediate(original);
                var session=new GameObject("NetworkSession");
                var manager=session.AddComponent<NetworkManager>();var transport=session.AddComponent<UnityTransport>();
                manager.NetworkConfig.NetworkTransport=transport;manager.NetworkConfig.PlayerPrefab=prefab;
                manager.NetworkConfig.ConnectionApproval=true;manager.NetworkConfig.EnableSceneManagement=true;
                manager.NetworkConfig.TickRate=30;
                var endpoints=session.AddComponent<LocalTransportSettings>();Set(endpoints,"transport",transport);
                var ui=session.AddComponent<DevelopmentSession>();Set(ui,"manager",manager);Set(ui,"transportSettings",endpoints);Set(ui,"lobbyCamera",originalCamera);
                ValidateScene(copy);
                if(!EditorSceneManager.SaveScene(copy,temp))throw new Exception("Scene save failed.");
                EditorSceneManager.CloseScene(copy,true);copy=default;
                if(Hash(ScenePath)!=expectedHash)throw new Exception("Original scene changed during installation; refusing to replace.");
                File.Copy(temp,ScenePath,true);AssetDatabase.ImportAsset(ScenePath,ImportAssetOptions.ForceSynchronousImport);
                var verify=EditorSceneManager.OpenPreviewScene(ScenePath);
                try { ValidateScene(verify); } finally { EditorSceneManager.ClosePreviewScene(verify); }
                File.WriteAllText(ReportDir+"/setup-validation.txt","PASS: Saved player prefab and scene, reopened in Unity "+Application.unityVersion+".\nScene SHA256: "+Hash(ScenePath)+"\nOwner-authoritative NetworkTransform, player prefab, UTP, approval, lobby camera and required references verified.\nThis is setup validation, not a two-instance multiplayer test.\n");
            }
            catch(Exception e){File.WriteAllText(ReportDir+"/setup-error.txt",e.ToString());Debug.LogException(e);}
            finally
            {
                if(copy.IsValid()&&copy.isLoaded)EditorSceneManager.CloseScene(copy,true);
                if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
                if(File.Exists(temp))AssetDatabase.DeleteAsset(temp);
            }
        }
        static void ValidateScene(Scene scene)
        {
            var roots=scene.GetRootGameObjects();var manager=roots.SelectMany(g=>g.GetComponentsInChildren<NetworkManager>(true)).Single();
            if(manager.NetworkConfig.PlayerPrefab==null || !(manager.NetworkConfig.NetworkTransport is UnityTransport))throw new Exception("Missing player/transport.");
            if(roots.SelectMany(g=>g.GetComponentsInChildren<FirstPersonController>(true)).Any())throw new Exception("Offline player remains in scene.");
            var player=manager.NetworkConfig.PlayerPrefab;
            if(player.GetComponent<NetworkTransform>().AuthorityMode!=NetworkTransform.AuthorityModes.Owner)throw new Exception("Wrong movement authority.");
            if(player.GetComponent<FirstPersonController>().enabled || player.GetComponent<CharacterController>().enabled || player.GetComponentInChildren<Camera>(true).enabled)throw new Exception("Player must be inert before ownership is assigned.");
            foreach(var root in roots.Append(player))foreach(var t in root.GetComponentsInChildren<Transform>(true))
                if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)throw new Exception("Missing script: "+t.name);
        }
        [MenuItem("Tools/Motel/Validate Local Multiplayer Configuration")]
        public static void ValidateSavedConfiguration()
        {
            Scene preview=default;
            try
            {
                preview=EditorSceneManager.OpenPreviewScene(ScenePath);
                ValidateScene(preview);
                Debug.Log("Motel multiplayer configuration valid. Two-instance results are in Documentation/LocalMultiplayer.");
            }
            finally { if(preview.IsValid())EditorSceneManager.ClosePreviewScene(preview); }
        }
        [MenuItem("Tools/Motel/Build Local Multiplayer Development Player")]
        public static void BuildDevelopmentPlayer()
        {
            // URP/build preparation can serialize unrelated pipeline and project settings.
            // Preserve their bytes so a development build does not become a visual/settings edit.
            var snapshots=new Dictionary<string,byte[]>();
            foreach(var path in new[]{"Assets/Settings/DefaultVolumeProfile.asset", "Assets/Settings/PC_RPAsset.asset",
                "Assets/Settings/UniversalRenderPipelineGlobalSettings.asset", "ProjectSettings/ProjectSettings.asset",
                "ProjectSettings/UnityConnectSettings.asset"})
                if(File.Exists(path))snapshots[path]=File.ReadAllBytes(path);
            try
            {
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes=new[]{ScenePath},locationPathName="Builds/LocalMultiplayer/MotelGame.app",
                    target=BuildTarget.StandaloneOSX,options=BuildOptions.Development });
                File.WriteAllText(ReportDir+"/build-validation.txt",report.summary.result+"\nErrors: "+report.summary.totalErrors+"\nWarnings: "+report.summary.totalWarnings+"\n");
                if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Development build failed: "+report.summary.result);
            }
            catch(Exception e){File.WriteAllText(ReportDir+"/build-error.txt",e.ToString());Debug.LogException(e);}
            finally
            {
                foreach(var item in snapshots)File.WriteAllBytes(item.Key,item.Value);
                AssetDatabase.Refresh();
            }
        }
    }
}
