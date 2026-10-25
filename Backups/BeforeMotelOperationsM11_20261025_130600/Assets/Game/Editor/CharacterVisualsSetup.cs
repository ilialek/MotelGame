using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using UnityEngine.SceneManagement;
using MotelGame.Characters;
using MotelGame.Networking;
using MotelGame.Service;
using UnityEngine;

namespace MotelGame.Editor
{
    public static class CharacterVisualsSetup
    {
        public const string Art="Assets/Game/Art/Characters/Quaternius";
        public const string Reports="Documentation/CharacterVisualsM8";
        static double nextPoll;
        [InitializeOnLoadMethod] static void Init()=>EditorApplication.update+=Poll;
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<nextPoll)return;nextPoll=EditorApplication.timeSinceStartup+1;
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||File.Exists("Library/MotelServiceRefresh.request"))return;
            const string request="Library/MotelCharactersImport.request";
            if(!File.Exists("Library/MotelCharactersScale.request")&&!File.Exists(request)&&!File.Exists("Library/MotelCharactersInstall.request")&&!File.Exists("Library/MotelCharactersValidate.request"))return;Directory.CreateDirectory(Reports);
            try {
                if(File.Exists("Library/MotelCharactersScale.request")){File.Delete("Library/MotelCharactersScale.request");RepairScale();}
                if(File.Exists(request)){File.Delete(request);InspectImports();}
                if(File.Exists("Library/MotelCharactersInstall.request")){File.Delete("Library/MotelCharactersInstall.request");Install();}
                if(File.Exists("Library/MotelCharactersValidate.request")){File.Delete("Library/MotelCharactersValidate.request");Validate();Capture();CaptureMotel();MeasureStride();}
            } catch(Exception e){File.WriteAllText(Reports+"/editor-error.txt",e.ToString());Debug.LogException(e);}
        }
        static void InspectImports()
        {
            var report=new StringBuilder();
            foreach(var name in new[]{"Casual","LongSleeve","Suit"})
            {
                string path=Art+"/"+name+".fbx";
                var importer=(ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType=ModelImporterAnimationType.Generic;
                importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importCameras=false;importer.importLights=false;importer.addCollider=false;
                importer.importAnimation=true;importer.animationCompression=ModelImporterAnimationCompression.Off;
                var clips=importer.defaultClipAnimations;
                foreach(var c in clips){c.name=c.name.Split('|').Last();c.loopTime=true;c.loopPose=true;c.lockRootRotation=true;c.lockRootHeightY=true;c.lockRootPositionXZ=true;}
                importer.clipAnimations=clips;importer.SaveAndReimport();
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var instance=UnityEngine.Object.Instantiate(model);
                try
                {
                    report.AppendLine(name+" root="+instance.transform.localScale);
                    report.AppendLine("BEFORE sample baked="+MeshBounds(instance));
                    Clip("Idle").SampleAnimation(instance,0);
                    report.AppendLine("AFTER sample baked="+MeshBounds(instance));
                    foreach(var t in instance.GetComponentsInChildren<Transform>())if(t.localScale!=Vector3.one)report.AppendLine(t.name+" scale "+t.localScale.ToString("G9"));
                    foreach(var a in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(x=>!x.name.StartsWith("__preview")))report.AppendLine("Clip "+a.name+" length="+a.length+" human="+a.humanMotion);
                    foreach(var r in instance.GetComponentsInChildren<SkinnedMeshRenderer>())report.AppendLine("Mesh "+r.name+" bounds="+r.bounds+" tris="+r.sharedMesh.triangles.Length/3+" materials="+string.Join(",",r.sharedMaterials.Select(m=>m.name+":"+m.color)));
                    var av=model.GetComponent<Animator>()?.avatar;report.AppendLine("Avatar valid="+(av!=null&&av.isValid)+" human="+(av!=null&&av.isHuman));
                }finally{UnityEngine.Object.DestroyImmediate(instance);}
            }
            File.WriteAllText(Reports+"/import-inspection.txt",report.ToString());
        }
        static readonly string[] Models={"Casual","LongSleeve","Suit"};
        const string Player="Assets/Game/Prefabs/NetworkPlayer.prefab", Guest="Assets/Game/Prefabs/NetworkGuest.prefab";
        static void Set(UnityEngine.Object o,string n,UnityEngine.Object v){var s=new SerializedObject(o);s.FindProperty(n).objectReferenceValue=v;s.ApplyModifiedPropertiesWithoutUndo();}
        static void Number(UnityEngine.Object o,string n,int v){var s=new SerializedObject(o);s.FindProperty(n).intValue=v;s.ApplyModifiedPropertiesWithoutUndo();}
        static void Float(UnityEngine.Object o,string n,float v){var s=new SerializedObject(o);s.FindProperty(n).floatValue=v;s.ApplyModifiedPropertiesWithoutUndo();}
        static T[] All<T>(Scene s) where T:Component=>s.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<T>(true)).ToArray();
        static AnimationClip Clip(string name)=>AssetDatabase.LoadAllAssetsAtPath(Art+"/Casual.fbx").OfType<AnimationClip>().Single(x=>x.name==name);
        static AnimatorController Controller()
        {
            string path=Art+"/Locomotion.controller";
            var existing=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);if(existing!=null)return existing;
            var c=AnimatorController.CreateAnimatorControllerAtPath(path);
            c.AddParameter("Speed",AnimatorControllerParameterType.Float);c.AddParameter("WalkRate",AnimatorControllerParameterType.Float);c.AddParameter("RunRate",AnimatorControllerParameterType.Float);
            var sm=c.layers[0].stateMachine;
            var idle=sm.AddState("Idle");idle.motion=Clip("Idle");sm.defaultState=idle;
            var walk=sm.AddState("Walk");walk.motion=Clip("Walk");walk.speedParameter="WalkRate";walk.speedParameterActive=true;
            var run=sm.AddState("Run");run.motion=Clip("Run");run.speedParameter="RunRate";run.speedParameterActive=true;
            Transition(idle,walk,AnimatorConditionMode.Greater,.1f);
            Transition(walk,idle,AnimatorConditionMode.Less,.1f);
            Transition(walk,run,AnimatorConditionMode.Greater,4.8f);
            Transition(run,walk,AnimatorConditionMode.Less,4.5f);
            AssetDatabase.SaveAssets();return c;
        }
        static void Transition(AnimatorState a,AnimatorState b,AnimatorConditionMode mode,float threshold)
        {var t=a.AddTransition(b);t.hasExitTime=false;t.hasFixedDuration=true;t.duration=.12f;t.AddCondition(mode,threshold,"Speed");}
        static Bounds MeshBounds(GameObject g)
        {
            bool first=true;var b=new Bounds();
            foreach(var r in g.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh=new Mesh();r.BakeMesh(mesh,true);
                foreach(var v in mesh.vertices){var p=r.transform.TransformPoint(v);if(first){b=new Bounds(p,Vector3.zero);first=false;}else b.Encapsulate(p);}
                UnityEngine.Object.DestroyImmediate(mesh);
            }
            return b;
        }
        static GameObject MakeModel(string name,Transform parent,AnimatorController controller)
        {
            var g=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Art+"/"+name+".fbx"),parent);
            foreach(var t in g.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Icosphere").ToArray())UnityEngine.Object.DestroyImmediate(t.gameObject);
            g.name=name;g.transform.localPosition=Vector3.zero;g.transform.localRotation=Quaternion.identity;
            Clip("Idle").SampleAnimation(g,0);
            var bounds=MeshBounds(g);float factor=1.8f/bounds.size.y;
            File.AppendAllText(Reports+"/model-sizes.txt",name+" bounds="+bounds+" factor="+factor.ToString("G9")+"\n");
            g.transform.localScale=Vector3.one*factor;
            g.transform.localPosition=Vector3.up*(-bounds.min.y*factor);
            foreach(var r in g.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.updateWhenOffscreen=true;
                r.sharedMaterials=r.sharedMaterials.Select(m=>Material(name,m)).ToArray();
            }
            var animator=g.GetComponent<Animator>();animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            return g;
        }
        static Material Material(string model,Material source)
        {
            string path=Art+"/Materials/"+model+"_"+source.name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m!=null)return m;
            m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.name=model+"_"+source.name;
            m.SetColor("_BaseColor",source.color);m.SetFloat("_Metallic",0);m.SetFloat("_Smoothness",.05f);
            AssetDatabase.CreateAsset(m,path);return m;
        }
        static CharacterVisuals AddVisual(Transform root,AnimatorController controller,bool player)
        {
            var old=root.Find("CharacterVisual");if(old!=null)throw new Exception("M8 already installed.");
            var g=new GameObject("CharacterVisual");g.transform.SetParent(root,false);
            var variants=Models.Take(player?1:3).Select(n=>MakeModel(n,g.transform,controller)).ToArray();
            var v=g.AddComponent<CharacterVisuals>();Set(v,"motionRoot",root);Float(v,"clipWalkSpeed",2.1f);Float(v,"clipRunSpeed",4.5f);
            var s=new SerializedObject(v);var a=s.FindProperty("appearances");a.arraySize=variants.Length;
            for(int i=0;i<variants.Length;i++)a.GetArrayElementAtIndex(i).objectReferenceValue=variants[i];
            s.FindProperty("walkSpeed").floatValue=player?4:1.6f;s.FindProperty("runSpeed").floatValue=player?6:1.6f;s.FindProperty("canRun").boolValue=player;s.ApplyModifiedPropertiesWithoutUndo();
            v.SelectAppearance();return v;
        }
        static void SetGuestAppearance(GuestLifecycle guest,int index)
        {
            var v=guest.GetComponentInChildren<CharacterVisuals>(true);Number(v,"appearanceIndex",index);v.SelectAppearance();
            var renderer=v.Appearance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Set(guest,"shirt",renderer);Number(guest,"shirtMaterialIndex",System.Array.FindIndex(renderer.sharedMaterials,m=>m.name.EndsWith("_Shirt")));
            guest.ApplyIdentity();
            if(PrefabUtility.IsPartOfPrefabInstance(guest))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(guest);PrefabUtility.RecordPrefabInstancePropertyModifications(v);
                foreach(var a in v.GetComponentsInChildren<Animator>(true))PrefabUtility.RecordPrefabInstancePropertyModifications(a.gameObject);
            }
        }
        [MenuItem("Tools/Motel/Install Character Visuals M8")]
        public static void Install()
        {
            var scene=SceneManager.GetSceneByPath(MultipleGuestsSetup.ScenePath);
            bool opened=!scene.IsValid()||!scene.isLoaded;
            if(!opened&&scene.isDirty)throw new Exception("Save unsaved scene edits before M8 installation.");
            if(opened)scene=EditorSceneManager.OpenScene(MultipleGuestsSetup.ScenePath,OpenSceneMode.Additive);
            Directory.CreateDirectory(Art+"/Materials");AssetDatabase.Refresh();var c=Controller();
            var p=PrefabUtility.LoadPrefabContents(Player);
            try
            {
                var v=AddVisual(p.transform,c,true);Set(p.GetComponent<NetworkPlayer>(),"characterVisuals",v);
                p.transform.Find("RemoteBody").GetComponent<Renderer>().enabled=false;
                var hand=v.Appearance.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Palm.R");
                var towel=p.transform.Find("CarriedTowel");towel.SetParent(hand,false);
                // The hand has FBX unit scaling in its ancestors; preserve the existing towel's world size.
                towel.localPosition=Vector3.zero;towel.rotation=p.transform.rotation;
                var scale=hand.lossyScale;towel.localScale=new Vector3(.4f/Mathf.Abs(scale.x),.10f/Mathf.Abs(scale.y),.26f/Mathf.Abs(scale.z));
                PrefabUtility.SaveAsPrefabAsset(p,Player);
            }finally{PrefabUtility.UnloadPrefabContents(p);}
            p=PrefabUtility.LoadPrefabContents(Guest);
            try
            {
                var v=AddVisual(p.transform,c,false);var old=p.transform.Find("HumanoidPlaceholder");
                var label=old.Find("GuestName");label.SetParent(v.transform,true);
                // Only visual primitives are hidden. Root body collider / navigation / network stay intact.
                old.gameObject.SetActive(false);
                Set(p.GetComponent<GuestLifecycle>(),"visual",v.gameObject);SetGuestAppearance(p.GetComponent<GuestLifecycle>(),0);
                PrefabUtility.SaveAsPrefabAsset(p,Guest);
            }finally{PrefabUtility.UnloadPrefabContents(p);}
            int i=0;
            foreach(var g in All<GuestLifecycle>(scene).OrderBy(x=>x.name))
            {
                var v=g.GetComponentInChildren<CharacterVisuals>(true);
                Set(g,"visual",v.gameObject);Set(g,"nameLabel",v.GetComponentInChildren<TextMesh>(true));
                SetGuestAppearance(g,i++%3);
            }
            foreach(var t in All<Transform>(scene).Where(t=>t.name=="EmployeeComputer_1.8m"||t.name=="EmployeeSecond_1.8m"||t.name=="CorridorPlayer_1.8m"))t.GetComponent<Renderer>().enabled=false;
            foreach(var label in All<WorldServiceText>(scene))label.RefreshMaterial();
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,MultipleGuestsSetup.ScenePath);
            AssetDatabase.SaveAssets();
            if(opened)EditorSceneManager.CloseScene(scene,true);
            Validate();Capture();
            File.WriteAllText(Reports+"/installed.txt",DateTime.UtcNow.ToString("O"));
        }
        static void RepairScale()
        {
            foreach(var path in new[]{Player,Guest})
            {
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var visual=root.GetComponentInChildren<CharacterVisuals>(true);Float(visual,"clipWalkSpeed",2.1f);Float(visual,"clipRunSpeed",4.5f);
                    foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Icosphere").ToArray())UnityEngine.Object.DestroyImmediate(t.gameObject);
                    foreach(var a in root.GetComponentsInChildren<CharacterVisuals>(true).Single().GetComponentsInChildren<Animator>(true))
                    {
                        var t=a.transform;bool active=t.gameObject.activeSelf;t.gameObject.SetActive(true);t.localScale=Vector3.one;t.localPosition=Vector3.zero;
                        Clip("Idle").SampleAnimation(t.gameObject,0);var bounds=MeshBounds(t.gameObject);float scale=1.8f/bounds.size.y;
                        t.localScale=Vector3.one*scale;t.localPosition=Vector3.up*(-bounds.min.y*scale);t.gameObject.SetActive(active);
                    }
                    if(path==Player)
                    {
                        var towel=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="CarriedTowel");var scale=towel.parent.lossyScale;
                        towel.localScale=new Vector3(.4f/Mathf.Abs(scale.x),.10f/Mathf.Abs(scale.y),.26f/Mathf.Abs(scale.z));
                    }
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            var scene=SceneManager.GetSceneByPath(MultipleGuestsSetup.ScenePath);bool opened=!scene.IsValid()||!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(MultipleGuestsSetup.ScenePath,OpenSceneMode.Additive);
            foreach(var t in All<Transform>(scene).Where(t=>t.name=="CorridorPlayer_1.8m"))t.GetComponent<Renderer>().enabled=false;
            foreach(var label in All<WorldServiceText>(scene))label.RefreshMaterial();
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,MultipleGuestsSetup.ScenePath);
            if(opened)EditorSceneManager.CloseScene(scene,true);
        }
        [MenuItem("Tools/Motel/Validate Character Visuals M8")]
        public static void Validate()
        {
            var lines=new StringBuilder();
            foreach(var path in new[]{Player,Guest})
            {
                var root=AssetDatabase.LoadAssetAtPath<GameObject>(path);var v=root.GetComponentInChildren<CharacterVisuals>(true);
                if(v==null||!v.ConfigurationValid())throw new Exception("Invalid character references "+path);
                if(v.GetComponentsInChildren<Collider>(true).Length>0)throw new Exception("Collider added to visual");
                foreach(var a in v.GetComponentsInChildren<Animator>(true))
                {
                    if(a.applyRootMotion)throw new Exception("Root motion enabled");
                    if(a.runtimeAnimatorController.animationClips.Length!=3)throw new Exception("Expected Idle / Walk / Run clips");
                    lines.AppendLine(path+" "+a.name+": valid Generic avatar, three clips, root motion disabled");
                }
                var instance=UnityEngine.Object.Instantiate(root);
                try
                {
                    foreach(var a in instance.GetComponentsInChildren<CharacterVisuals>(true).Single().GetComponentsInChildren<Animator>(true))
                    {
                        a.gameObject.SetActive(true);a.enabled=false;Clip("Idle").SampleAnimation(a.gameObject,0);
                        var bounds=MeshBounds(a.gameObject);
                        if(Mathf.Abs(bounds.size.y-1.8f)>.03f || Mathf.Abs(bounds.min.y)>.025f)throw new Exception("Invalid model scale/floor "+a.name+" "+bounds);
                        var leg=a.GetComponentsInChildren<Transform>().First(t=>t.name=="LowerLeg.L");
                        var position=a.transform.localPosition;var rotation=a.transform.localRotation;
                        foreach(var name in new[]{"Idle","Walk","Run"})
                        {
                            var clip=Clip(name);float maxAngle=0;Quaternion first=Quaternion.identity;
                            for(int f=0;f<20;f++)
                            {
                                clip.SampleAnimation(a.gameObject,clip.length*f/20);
                                if(f==0)first=leg.localRotation;maxAngle=Mathf.Max(maxAngle,Quaternion.Angle(first,leg.localRotation));
                                if(Vector3.Distance(position,a.transform.localPosition)>.001f || Quaternion.Angle(rotation,a.transform.localRotation)>.01f)throw new Exception("Clip changes model root "+name);
                            }
                            if(name!="Idle"&&maxAngle<15)throw new Exception("Missing leg animation "+name);
                            lines.AppendLine(a.name+" "+name+": sampled 20 poses; leg range="+maxAngle.ToString("F1")+" degrees; model root fixed");
                        }
                    }
                }finally{UnityEngine.Object.DestroyImmediate(instance);}
                if(path==Player && (root.GetComponent<CharacterController>().height!=1.8f || root.GetComponentInChildren<Camera>(true).transform.localPosition.y!=1.65f))throw new Exception("Player dimensions changed");
                if(root.transform.Find(path==Player?"RemoteBody":"HumanoidPlaceholder").GetComponentInChildren<Renderer>(true).enabled && path==Player)throw new Exception("Capsule visible");
            }
            var scene=EditorSceneManager.OpenPreviewScene(MultipleGuestsSetup.ScenePath);
            try
            {
                var guests=All<GuestLifecycle>(scene);if(guests.Length!=5)throw new Exception("Guest pool changed");
                foreach(var g in guests)
                {
                    var v=g.GetComponentInChildren<CharacterVisuals>(true);if(!g.ConfigurationValid()||!v.ConfigurationValid())throw new Exception("Invalid guest "+g.name);
                    if(g.transform.Find("HumanoidPlaceholder").gameObject.activeSelf)throw new Exception("Legacy guest geometry visible");
                    lines.AppendLine(g.name+": "+v.Appearance.name+", root collider / NavMeshAgent / NetworkObject preserved");
                }
                foreach(var t in All<Transform>(scene).Where(t=>t.name=="EmployeeComputer_1.8m"||t.name=="EmployeeSecond_1.8m"||t.name=="CorridorPlayer_1.8m"))if(t.GetComponent<Renderer>().enabled)throw new Exception("Static capsule marker visible");
            }finally{EditorSceneManager.ClosePreviewScene(scene);}
            File.WriteAllText(Reports+"/native-validation.txt",lines.ToString());
        }
        static void Capture()
        {
            File.WriteAllText(Reports+"/preview-diagnostics.txt", "Final normalized models / sampled poses\n");
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var cameraGo=new GameObject("M8PreviewCamera");SceneManager.MoveGameObjectToScene(cameraGo,scene);var camera=cameraGo.AddComponent<Camera>();camera.scene=scene;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);camera.cameraType=CameraType.Preview;camera.enabled=false;camera.cullingMask=1<<30;camera.backgroundColor=new Color(.08f,.09f,.11f);camera.clearFlags=CameraClearFlags.SolidColor;
                var lightGo=new GameObject("M8PreviewLight");SceneManager.MoveGameObjectToScene(lightGo,scene);var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.cullingMask=1<<30;light.transform.rotation=Quaternion.Euler(35,-35,0);
                var models=Models.Select(n=>MakeModel(n,new GameObject(n+"Preview").transform,Controller())).ToArray();
                for(int i=0;i<models.Length;i++)
                {
                    SceneManager.MoveGameObjectToScene(models[i].transform.parent.gameObject,scene);models[i].transform.parent.position=new Vector3((i-1)*1.25f,0,0);
                    foreach(var t in models[i].GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
                }
                camera.transform.position=new Vector3(3,1.8f,5);camera.transform.LookAt(new Vector3(0,.9f,0));camera.fieldOfView=42;
                foreach(var pose in new[]{"Idle","Walk","Run"})
                {
                    foreach(var m in models){m.GetComponent<Animator>().enabled=false;Clip(pose).SampleAnimation(m,Clip(pose).length*.25f);foreach(var r in m.GetComponentsInChildren<SkinnedMeshRenderer>())r.forceMatrixRecalculationPerRender=true;}
                    File.AppendAllText(Reports+"/preview-diagnostics.txt",pose+": "+string.Join("; ",models.Select(m=>m.name+" position="+m.transform.position+" scale="+m.transform.localScale+" baked="+MeshBounds(m)+" renderer="+m.GetComponentInChildren<SkinnedMeshRenderer>().bounds))+"\n");
                    Render(camera,Reports+"/preview-"+pose+".png");
                }
            }finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        static void CaptureMotel()
        {
            var scene=EditorSceneManager.OpenPreviewScene(MultipleGuestsSetup.ScenePath);
            try
            {
                foreach(var t in All<Transform>(scene))t.gameObject.layer=30;
                foreach(var light in All<Light>(scene))light.cullingMask=1<<30;
                foreach(var label in All<WorldServiceText>(scene))label.RefreshMaterial();
                var guests=All<GuestLifecycle>(scene).OrderBy(g=>g.name).ToArray();
                for(int i=0;i<guests.Length;i++)
                {
                    var g=guests[i];var v=g.GetComponentInChildren<CharacterVisuals>(true);v.gameObject.SetActive(i<3);
                    if(i>=3)continue;
                    g.transform.SetPositionAndRotation(g.ReceptionPoint.position+new Vector3(-i*1.1f,0,i*.3f),Quaternion.Euler(0,180,0));
                    var a=v.Appearance.GetComponent<Animator>();a.enabled=false;Clip("Idle").SampleAnimation(a.gameObject,.4f);
                    foreach(var r in a.GetComponentsInChildren<SkinnedMeshRenderer>())r.forceMatrixRecalculationPerRender=true;
                    g.ApplyIdentity();
                }
                var player=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Player));SceneManager.MoveGameObjectToScene(player,scene);
                player.transform.SetPositionAndRotation(guests[0].ReceptionPoint.position+new Vector3(1.3f,0,-.5f),Quaternion.Euler(0,180,0));
                foreach(var t in player.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
                var pv=player.GetComponentInChildren<CharacterVisuals>();pv.SetVisible(true);var pa=pv.Appearance.GetComponent<Animator>();pa.enabled=false;Clip("Idle").SampleAnimation(pa.gameObject,.4f);
                foreach(var r in pa.GetComponentsInChildren<SkinnedMeshRenderer>())r.forceMatrixRecalculationPerRender=true;
                player.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="CarriedTowel").gameObject.SetActive(true);
                foreach(var c in All<Camera>(scene))c.enabled=false;
                var camera=All<Camera>(scene).First();camera.scene=scene;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);camera.cullingMask=1<<30;camera.fieldOfView=60;
                camera.transform.position=guests[0].ReceptionPoint.position+new Vector3(-1,1.65f,-4);camera.transform.LookAt(guests[0].ReceptionPoint.position+new Vector3(-.4f,.95f,0));
                Render(camera,Reports+"/preview-motel.png");
            }finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        static void MeasureStride()
        {
            var scene=EditorSceneManager.NewPreviewScene();var parent=new GameObject("StrideMeasure");SceneManager.MoveGameObjectToScene(parent,scene);
            try
            {
                var model=MakeModel("Casual",parent.transform,Controller());model.GetComponent<Animator>().enabled=false;
                var foot=model.GetComponentsInChildren<Transform>().Single(t=>t.name=="Foot.L");var report=new StringBuilder();
                foreach(var name in new[]{"Walk","Run"})
                {
                    var clip=Clip(name);var points=new Vector3[121];
                    for(int i=0;i<points.Length;i++){clip.SampleAnimation(model,clip.length*i/120);points[i]=foot.position;}
                    float minimum=points.Min(p=>p.y);var speeds=new System.Collections.Generic.List<float>();
                    for(int i=1;i<points.Length;i++)
                    {
                        float speed=(points[i].z-points[i-1].z)/(clip.length/120);
                        if(points[i].y<minimum+.035f&&points[i-1].y<minimum+.035f&&Mathf.Abs(speed)>.2f)speeds.Add(speed);
                    }
                    speeds.Sort();report.AppendLine(name+" planted-foot speeds="+string.Join(",",speeds.Select(v=>v.ToString("F2"))));
                }
                File.WriteAllText(Reports+"/stride-measurement.txt",report.ToString());
            }finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        static void Render(Camera camera,string path)
        {
            var rt=new RenderTexture(1200,720,24);rt.Create();var old=RenderTexture.active;camera.targetTexture=rt;
            try{camera.Render();RenderTexture.active=rt;var image=new Texture2D(1200,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1200,720),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);}
            finally{camera.targetTexture=null;RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
        }
    }
}
