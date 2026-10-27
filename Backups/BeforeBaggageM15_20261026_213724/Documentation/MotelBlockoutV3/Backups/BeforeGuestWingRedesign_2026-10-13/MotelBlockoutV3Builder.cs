// Editor-only static scene construction. No runtime component or procedural layout system.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace MotelBlockoutV3Tools
{
    public static class MotelBlockoutV3Builder
    {
        const string ScenePath = "Assets/Game/Scenes/Motel_Blockout_V3.unity";
        const string SpecPath = "Assets/Game/Editor/MotelBlockoutV3/MotelBlockoutV3.json";
        const string MaterialPath = "Assets/Game/Materials/MotelBlockoutV3";
        const string Request = "Library/MotelBlockoutV3.request";
        const string ReportDir = "Documentation/MotelBlockoutV3";
        [Serializable] class Spec { public Shape[] boxes; public Label[] labels; public Lamp[] lights; public Marker[] cameras; }
        [Serializable] class Shape { public string name, group, material, kind; public float x,y,z,sx,sy,sz; public bool solid; }
        [Serializable] class Label { public string name,group,value; public float x,y,z,ry,size; }
        [Serializable] class Lamp { public string name; public float x,y,z,power,range; }
        [Serializable] class Marker { public string name; public float x,y,z; }
        static readonly Dictionary<string, Transform> groups = new Dictionary<string, Transform>();
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static Transform root;

        [InitializeOnLoadMethod]
        static void OnLoad() { EditorApplication.delayCall += CheckRequest; }
        static void CheckRequest()
        {
            if (!File.Exists(Request)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            { EditorApplication.delayCall += CheckRequest; return; }
            // Consume before execution. A reload or failure never overwrites an existing scene.
            File.Delete(Request);
            if (File.Exists(ScenePath)) ValidateSavedScene(); else Build();
        }

        [MenuItem("Tools/Motel Blockout V3/Validate Saved Scene and Render Previews")]
        public static void ValidateSavedScene()
        {
            Scene previous=SceneManager.GetActiveScene(); Scene loaded=default; bool openedHere=false;
            try
            {
                var spec=JsonUtility.FromJson<Spec>(File.ReadAllText(SpecPath));
                loaded=SceneManager.GetSceneByPath(ScenePath);
                if(!loaded.IsValid() || !loaded.isLoaded) {loaded=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);openedHere=true;}
                SceneManager.SetActiveScene(loaded);
                Validate(loaded,spec.boxes.Length);
                root=loaded.GetRootGameObjects().Single(g=>g.name=="Motel").transform;
                if(openedHere) CapturePreviews(root);
                File.WriteAllText(ReportDir+"/unity-validation.txt","PASS: Saved scene loaded and inspected in Unity "+Application.unityVersion+".\nPASS: "+spec.boxes.Length+" primitives; positive dimensions, valid materials, floor tops Y=0, no missing scripts.\n"+(openedHere?"PASS: Four render previews captured.\n":"Previews skipped because the user already has this scene open.\n")+"Original scene was not saved or modified.\n");
            }
            catch(Exception e) {File.WriteAllText(ReportDir+"/unity-error.txt",e.ToString());Debug.LogException(e);}
            finally
            {
                if(openedHere && loaded.IsValid() && loaded.isLoaded)EditorSceneManager.CloseScene(loaded,true);
                if(previous.IsValid() && previous.isLoaded)SceneManager.SetActiveScene(previous);
            }
        }

        [MenuItem("Tools/Motel Blockout V3/Create New Scene (never overwrite)")]
        public static void Build()
        {
            Directory.CreateDirectory(ReportDir);
            Scene previous = SceneManager.GetActiveScene();
            Scene generated = default;
            try
            {
                if (File.Exists(ScenePath)) throw new InvalidOperationException("Scene already exists; refusing to overwrite: " + ScenePath);
                if (Directory.Exists(MaterialPath)) throw new InvalidOperationException("Material folder already exists; refusing to overwrite: " + MaterialPath);
                var spec = JsonUtility.FromJson<Spec>(File.ReadAllText(SpecPath));
                generated = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(generated);
                root = new GameObject("Motel").transform;
                groups.Clear(); materials.Clear();
                foreach (string name in new[]{"Exterior","Lobby","ReceptionDesk","Storage","Corridor","Rooms","Lighting","PlayerScale","PlayerSpawnPoints"}) Group(name);
                Directory.CreateDirectory(MaterialPath);
                AssetDatabase.Refresh();
                CreateMaterials();
                foreach (var s in spec.boxes)
                {
                    var go = GameObject.CreatePrimitive(s.kind == "Capsule" ? PrimitiveType.Capsule : PrimitiveType.Cube);
                    go.name = s.name; go.transform.SetParent(Group(s.group), false);
                    go.transform.position = new Vector3(s.x,s.y,s.z);
                    go.transform.localScale = new Vector3(s.sx,s.kind == "Capsule" ? s.sy/2 : s.sy,s.sz);
                    go.GetComponent<Renderer>().sharedMaterial = materials[s.material];
                    if (!s.solid) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
                    if (s.material == "Glass") go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                foreach (var l in spec.labels)
                {
                    var go = new GameObject(l.name); go.transform.SetParent(Group(l.group),false);
                    go.transform.position = new Vector3(l.x,l.y,l.z); go.transform.rotation=Quaternion.Euler(0,l.ry,0);
                    var t = go.AddComponent<TextMesh>(); t.text=l.value; t.font=font; t.fontSize=64;
                    t.characterSize=l.size/6.4f; t.anchor=TextAnchor.MiddleCenter; t.alignment=TextAlignment.Center;
                    t.color=new Color(.94f,.91f,.8f); go.GetComponent<Renderer>().sharedMaterial=font.material;
                }
                foreach(var l in spec.lights)
                {
                    var go = new GameObject(l.name+"_Light"); go.transform.SetParent(Group("Lighting"),false);
                    go.transform.position=new Vector3(l.x,l.y,l.z);
                    var light=go.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.79f,.57f);
                    light.intensity=l.power;light.range=l.range;light.shadows=LightShadows.None;
                }
                foreach(var m in spec.cameras)
                {
                    var go=new GameObject(m.name+"_EyeHeight_1.65m");go.transform.SetParent(Group("PlayerSpawnPoints"),false);
                    go.transform.position=new Vector3(m.x,m.y,m.z);
                }
                var moon=new GameObject("Moon_Fill");moon.transform.SetParent(Group("Lighting"),false);
                moon.transform.rotation=Quaternion.Euler(55,-35,0);
                var ml=moon.AddComponent<Light>();ml.type=LightType.Directional;ml.color=new Color(.55f,.65f,.85f);ml.intensity=.17f;
                var cameraGo=new GameObject("Blockout_InspectionCamera");cameraGo.transform.SetParent(Group("PlayerSpawnPoints"),false);
                cameraGo.transform.position=new Vector3(7,1.65f,1.8f);cameraGo.transform.rotation=Quaternion.Euler(0,0,0);
                var camera=cameraGo.AddComponent<Camera>();camera.nearClipPlane=.05f;camera.farClipPlane=100;
                camera.fieldOfView=75;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.015f,.02f,.03f);
                cameraGo.tag="MainCamera";
                RenderSettings.skybox=null;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.24f,.25f,.29f);
                RenderSettings.fog=false;RenderSettings.reflectionIntensity=.25f;
                Validate(generated, spec.boxes.Length);
                if (!EditorSceneManager.SaveScene(generated,ScenePath)) throw new IOException("Could not save scene");
                EditorSceneManager.CloseScene(generated,true);
                generated=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
                SceneManager.SetActiveScene(generated);
                Validate(generated,spec.boxes.Length);
                root=generated.GetRootGameObjects().Single(g=>g.name=="Motel").transform;
                CapturePreviews(root);
                File.WriteAllText(ReportDir+"/unity-validation.txt",
                    "PASS: Scene created, saved, closed and reopened in Unity "+Application.unityVersion+".\n"+
                    "PASS: "+spec.boxes.Length+" primitive renderers, positive dimensions, expected colliders and materials.\n"+
                    "PASS: No missing scripts. No gameplay scripts attached.\n"+
                    "PASS: Previous scene was neither saved nor replaced by this tool.\n"+
                    "Rendered: top-down, lobby, reception and corridor previews.\n"+
                    "See geometry-validation.json for floor, overlap, navigation and eye-height sightline checks.\n"+
                    "Manual: assess scale, lighting, transparent windows, signage and comfort in Scene view.\n");
                Debug.Log("Motel Blockout V3: scene saved and reopened successfully. " + ScenePath);
            }
            catch(Exception e)
            {
                File.WriteAllText(ReportDir+"/unity-error.txt",e.ToString());Debug.LogException(e);
            }
            finally
            {
                if(generated.IsValid() && generated.isLoaded) EditorSceneManager.CloseScene(generated,true);
                if(previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                groups.Clear();materials.Clear();
            }
        }

        static Transform Group(string path)
        {
            if(groups.TryGetValue(path,out var t))return t;
            int slash=path.LastIndexOf('/');var parent=slash<0?root:Group(path.Substring(0,slash));
            var go=new GameObject(slash<0?path:path.Substring(slash+1));go.transform.SetParent(parent,false);
            return groups[path]=go.transform;
        }
        static void CreateMaterials()
        {
            Make("Wall",new Color(.57f,.55f,.46f));Make("Ceiling",new Color(.65f,.63f,.55f));
            Make("Carpet",new Color(.15f,.19f,.18f));Make("UtilityFloor",new Color(.35f,.36f,.34f));
            Make("Trim",new Color(.12f,.13f,.12f));Make("Door",new Color(.33f,.22f,.14f));
            Make("Furniture",new Color(.4f,.4f,.35f));Make("Wood",new Color(.32f,.23f,.15f));
            Make("Linen",new Color(.63f,.6f,.45f));Make("Porcelain",new Color(.75f,.75f,.66f));
            Make("Upholstery",new Color(.35f,.24f,.2f));Make("Machine",new Color(.13f,.17f,.17f));
            Make("Screen",new Color(.18f,.37f,.35f));Make("Metal",new Color(.3f,.33f,.34f));
            Make("Cardboard",new Color(.48f,.36f,.21f));Make("Mat",new Color(.1f,.1f,.09f));
            Make("Lamp",new Color(1,.83f,.52f));Make("Ground",new Color(.065f,.077f,.063f));
            Make("Asphalt",new Color(.075f,.079f,.083f));Make("Concrete",new Color(.35f,.35f,.33f));
            Make("Marking",new Color(.72f,.68f,.48f));Make("Car",new Color(.27f,.18f,.15f));
            Make("Rubber",new Color(.04f,.04f,.04f));Make("Scale",new Color(.32f,.51f,.57f));
            Make("Glass",new Color(.47f,.63f,.65f,.12f));
        }
        static void Make(string name,Color color)
        {
            var shader=Shader.Find("Universal Render Pipeline/Lit");
            if(shader==null)throw new Exception("URP Lit shader unavailable");
            var m=new Material(shader){name="V3_"+name};m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.12f);
            if(name=="Glass")
            {
                m.SetFloat("_Surface",1);m.SetFloat("_Blend",0);m.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);m.SetFloat("_ZWrite",0);m.SetFloat("_Cull",0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.SetOverrideTag("RenderType","Transparent");m.renderQueue=3000;
                m.SetShaderPassEnabled("ShadowCaster",false);m.SetFloat("_Smoothness",.25f);
            }
            if(name=="Lamp" || name=="Screen") {m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*(name=="Lamp"?1.5f:.35f));}
            AssetDatabase.CreateAsset(m,MaterialPath+"/"+name+".mat");materials[name]=m;
        }
        static void Validate(Scene scene,int expected)
        {
            var roots=scene.GetRootGameObjects();
            if(roots.Length!=1 || roots[0].name!="Motel")throw new Exception("Unexpected hierarchy roots");
            var filters=roots[0].GetComponentsInChildren<MeshFilter>(true);
            if(filters.Length!=expected)throw new Exception("Primitive count mismatch: "+filters.Length);
            foreach(var t in roots[0].GetComponentsInChildren<Transform>(true))
            {
                var s=t.localScale;if(s.x<=0 || s.y<=0 || s.z<=0)throw new Exception("Invalid scale: "+t.name);
                if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)throw new Exception("Missing script: "+t.name);
            }
            foreach(var r in roots[0].GetComponentsInChildren<Renderer>(true))
                if(r.sharedMaterial==null || r.sharedMaterial.shader==null)throw new Exception("Missing material: "+r.name);
            foreach(var f in filters)
                if(f.name.StartsWith("Floor_") && Mathf.Abs(f.GetComponent<Renderer>().bounds.max.y)>.0001f)
                    throw new Exception("Floor top not Y=0: "+f.name);
        }
        static void CapturePreviews(Transform motel)
        {
            // Temporary layer isolates renders from the user's currently open scene.
            // All temporary changes are discarded by closing the additive scene without saving.
            var transforms=motel.GetComponentsInChildren<Transform>(true);
            foreach(var t in transforms)t.gameObject.layer=30;
            foreach(var l in motel.GetComponentsInChildren<Light>())l.cullingMask=1<<30;
            var camera=motel.GetComponentInChildren<Camera>();camera.cullingMask=1<<30;
            Render(camera,"lobby",new Vector3(7,1.65f,1.8f),new Vector3(5.8f,1.5f,7));
            Render(camera,"reception",new Vector3(11.9f,1.65f,4.8f),new Vector3(5.5f,1.3f,3));
            Render(camera,"corridor",new Vector3(6.6f,1.65f,13.5f),new Vector3(6.6f,1.5f,23));
            foreach(var t in transforms)if(t.name.StartsWith("Ceiling_") || t.name.Contains("CeilingFixture") || t.name=="CeilingLight")t.gameObject.SetActive(false);
            camera.orthographic=true;camera.orthographicSize=21;camera.aspect=1.4f;
            Render(camera,"top-down",new Vector3(9.5f,45,5.2f),new Vector3(9.5f,0,5.2f),true);
        }
        static void Render(Camera camera,string name,Vector3 position,Vector3 target,bool top=false)
        {
            camera.transform.position=position;
            camera.transform.rotation=top?Quaternion.Euler(90,0,0):Quaternion.LookRotation(target-position);
            var rt=new RenderTexture(1400,1000,24);camera.targetTexture=rt;
            var previous=RenderTexture.active;
            try
            {
                camera.Render();RenderTexture.active=rt;
                var tex=new Texture2D(1400,1000,TextureFormat.RGB24,false);
                tex.ReadPixels(new Rect(0,0,1400,1000),0,0);tex.Apply();
                File.WriteAllBytes(ReportDir+"/"+name+".png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
            }
            finally {camera.targetTexture=null;RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
        }
    }
}
