using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MotelGame.Editor
{
    // Editor-only checks in a disposable preview scene. Never save the user's scene.
    public static class FirstPersonControllerValidation
    {
        const string Request = "Library/MotelFirstPerson.request";
        const string Report = "Documentation/FirstPersonController/unity-validation.txt";
        [InitializeOnLoadMethod]
        static void Initialize() { EditorApplication.delayCall += CheckRequest; }
        static void CheckRequest()
        {
            if (!File.Exists(Request)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            { EditorApplication.delayCall += CheckRequest; return; }
            File.Delete(Request);
            Validate();
        }
        [MenuItem("Tools/Motel/Validate First Person Controller")]
        public static void Validate()
        {
            // Milestone 1 moved the offline player into a network prefab.
            if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/NetworkPlayer.prefab") != null)
            {
                LocalMultiplayerSetup.ValidateSavedConfiguration();
                return;
            }
            Scene preview = default;
            var lines = new List<string>();
            var errors = new List<string>();
            Application.LogCallback collect = (message, stack, type) =>
            { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); };
            Application.logMessageReceived += collect;
            try
            {
                preview = EditorSceneManager.OpenPreviewScene("Assets/Game/Scenes/Motel_Blockout_V3.unity");
                var root = preview.GetRootGameObjects().Single();
                var player = root.GetComponentsInChildren<FirstPersonController>(true).Single();
                var cc = player.GetComponent<CharacterController>();
                var camera = player.GetComponentInChildren<Camera>();
                Require(cc != null && cc.enabled, "Enabled CharacterController");
                Require(camera != null && camera.enabled && camera.CompareTag("MainCamera"), "Player camera");
                Require(root.GetComponentsInChildren<Camera>(true).Count(c=>c.enabled)==1, "Single active camera");
                Require(root.GetComponentsInChildren<AudioListener>(true).Count(l=>l.enabled)==1, "Single audio listener");
                Require(Mathf.Abs(cc.height-1.8f)<.001f && Mathf.Abs(cc.radius-.3f)<.001f, "Capsule dimensions");
                Require(Mathf.Abs(camera.nearClipPlane-.03f)<.001f, "Near clip plane");
                Require(Vector3.Distance(camera.transform.localPosition,new Vector3(0,1.65f,0))<.001f, "Eye height");
                foreach(var t in root.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0, "No missing scripts: "+t.name);
                var data=new SerializedObject(player);
                Require(data.FindProperty("playerCamera").objectReferenceValue==camera, "Explicit camera reference");
                Require(data.FindProperty("walkingSpeed").floatValue==4, "Walking speed");
                lines.Add("PASS: Unity "+Application.unityVersion+" loaded scene and compiled FirstPersonController.");
                lines.Add("PASS: Single enabled player/camera/listener, valid references, capsule 0.6 x 1.8m, eye 1.65m, near plane 0.03m, walk 4m/s.");
                Physics.SyncTransforms();
                // Exercise Unity's actual CharacterController against the saved scene colliders.
                for(int i=0;i<120;i++) cc.Move(Vector3.down*(2f/60));
                Require(cc.isGrounded && Mathf.Abs(player.transform.position.y)<.1f, "Ground collision at lobby spawn");
                var targets=new[]{new Vector2(7,8.4f),new Vector2(22.2f,8.4f),new Vector2(22.2f,12.65f),
                    new Vector2(20.1f,12.65f),new Vector2(22.2f,12.65f),new Vector2(24.3f,12.65f),
                    new Vector2(22.2f,12.65f),new Vector2(22.2f,19.55f),new Vector2(20.1f,19.55f),
                    new Vector2(22.2f,19.55f),new Vector2(24.3f,19.55f)};
                foreach(var target in targets)
                {
                    for(int i=0;i<1200;i++)
                    {
                        var p=player.transform.position;
                        var delta=new Vector3(target.x-p.x,0,target.y-p.z);
                        if(delta.magnitude<.025f)break;
                        cc.Move(Vector3.ClampMagnitude(delta,4f/60)+Vector3.down*(2f/60));
                    }
                    var end=player.transform.position;
                    Require(Vector2.Distance(new Vector2(end.x,end.z),target)<.05f,"Reach waypoint "+target);
                    Require(Mathf.Abs(end.y)<.1f,"Floor support at "+target);
                }
                lines.Add("PASS: Native CharacterController.Move traversed lobby, right corridor, left turn and all four room entrances at 4m/s steps; floor collisions held.");
                // Teleport only the disposable test player, then walk into a solid lobby wall.
                cc.enabled=false;player.transform.position=new Vector3(7,.05f,8.4f);cc.enabled=true;Physics.SyncTransforms();
                for(int i=0;i<180;i++)cc.Move(new Vector3(0,-2f/60,6f/60));
                Require(player.transform.position.z<9.76f && player.transform.position.z>9.5f,"Sprint collision with rear lobby wall");
                Require(camera.transform.position.z+camera.nearClipPlane<10,"Eye remains behind wall");
                lines.Add("PASS: 6m/s motion into rear lobby wall stopped the capsule and kept camera behind wall.");
                Require(errors.Count==0,"No Unity errors during validation: "+string.Join("; ",errors));
                lines.Add("PASS: No error/exception/assert logs during these checks.");
                lines.Add("NOT RUN: Play Mode keyboard/mouse input, cursor capture/focus transitions, subjective responsiveness, all-angle camera clipping and two-player test. These need manual Game view verification.");
            }
            catch(Exception e) { lines.Add("FAIL: "+e); }
            finally
            {
                if(preview.IsValid())EditorSceneManager.ClosePreviewScene(preview);
                Application.logMessageReceived -= collect;
                Directory.CreateDirectory(Path.GetDirectoryName(Report));
                File.WriteAllText(Report,DateTime.UtcNow.ToString("O")+"\n"+string.Join("\n",lines)+"\n");
            }
        }
        static void Require(bool condition,string message)
        { if(!condition)throw new InvalidOperationException(message); }
    }
}
