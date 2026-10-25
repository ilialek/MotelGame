using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using MotelGame.Service;
using MotelGame.Interactions;

namespace MotelGame.Editor
{
    // Explicit, one-off art integration. Never executes scene edits on ordinary domain reload.
    public static partial class MotelVisualOverhaul
    {
        public const string Reports = "Documentation/MotelVisualOverhaul";
        const string Art = "Assets/Game/Art/PizzaDoggy";
        const string Request = "Library/MotelVisualOverhaul.request";
        const string ManifestPath = "Assets/Game/Editor/MotelVisualOverhaul/ImportManifest.json";
        static double nextPoll;
        [Serializable] public class ImportManifest { public ModelEntry[] models; public TextureEntry[] textures; public MaterialEntry[] materials; }
        [Serializable] public class ModelEntry { public string pack, name, path; public string[] materials; }
        [Serializable] public class TextureEntry { public string path; public bool normal, alpha; }
        [Serializable] public class MaterialEntry { public string pack, name, texture, normal; public bool alpha; }
        [Serializable] class ModelReport { public string name, pack; public Vector3 size, center; public int vertices, triangles, renderers; public string[] materials; }
        [Serializable] class ModelReports { public List<ModelReport> models = new(); }
        [Serializable] class ObjectReport { public string path; public Vector3 position, scale, rotation, size; public string[] components; public string[] materials; }
        [Serializable] class SceneReport { public List<ObjectReport> objects = new(); }
        static ImportManifest Manifest => JsonUtility.FromJson<ImportManifest>(File.ReadAllText(ManifestPath));
        static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
        static string FullPath(Transform t) => t.parent == null ? t.name : FullPath(t.parent) + "/" + t.name;
        static string MaterialPath(string pack, string name) => Art + "/" + pack + "/Materials/" + name + ".mat";
        static Material Mat(string pack, string name) => AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(pack, name));
        static string PrefabPath(string pack, string name) => Art + "/" + pack + "/Prefabs/" + name + ".prefab";
        [InitializeOnLoadMethod] static void Init() => EditorApplication.update += Poll;
        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || File.Exists("Library/MotelServiceRefresh.request")) return;
            string command = File.ReadAllText(Request).Trim(); File.Delete(Request); Directory.CreateDirectory(Reports);
            try
            {
                File.WriteAllText(Reports + "/progress.txt", "Running " + command);
                if (command == "import") Import();
                else if (command == "catalog") Catalog();
                else if (command == "survey") Survey();
                else if (command == "validate") Validate();
                else if (command == "diagnose") Diagnose();
                else ExecuteSceneCommand(command);
                File.WriteAllText(Reports + "/completed-" + command + ".txt", DateTime.Now.ToString("O") + " PASS");
                if (File.Exists(Reports + "/editor-error.txt")) File.Move(Reports + "/editor-error.txt", Reports + "/resolved-error-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            }
            catch (Exception e) { File.WriteAllText(Reports + "/editor-error.txt", e.ToString()); Debug.LogException(e); }
        }
        static void EnsureDirectory(string path) { Directory.CreateDirectory(path); AssetDatabase.Refresh(); }
        static void Import()
        {
            var manifest = Manifest;
            foreach (var item in manifest.textures)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(item.path);
                importer.textureType = item.normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !item.normal; importer.maxTextureSize = 512; importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Bilinear; importer.anisoLevel = 2; importer.wrapMode = TextureWrapMode.Repeat;
                importer.alphaIsTransparency = item.alpha; importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
            foreach (var entry in manifest.materials)
            {
                string path = MaterialPath(entry.pack, entry.name); EnsureDirectory(Path.GetDirectoryName(path));
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(entry.texture));
                material.SetColor("_BaseColor", Color.white); material.SetFloat("_Smoothness", .12f); material.SetFloat("_Metallic", 0);
                material.enableInstancing = true;
                if (!string.IsNullOrEmpty(entry.normal)) { material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(entry.normal)); material.SetFloat("_BumpScale", .3f); material.EnableKeyword("_NORMALMAP"); }
                if (entry.alpha)
                {
                    material.SetFloat("_AlphaClip", 1); material.SetFloat("_Cutoff", .5f); material.SetFloat("_Cull", 0);
                    material.EnableKeyword("_ALPHATEST_ON"); material.SetOverrideTag("RenderType", "TransparentCutout"); material.renderQueue = 2450;
                }
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
            foreach (var entry in manifest.models)
            {
                if (File.Exists(PrefabPath(entry.pack, entry.name))) continue;
                File.WriteAllText(Reports + "/progress.txt", "Import " + entry.pack + "/" + entry.name);
                var importer = (ModelImporter)AssetImporter.GetAtPath(entry.path);
                importer.importAnimation = false; importer.animationType = ModelImporterAnimationType.None;
                importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.isReadable = false; importer.SaveAndReimport();
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(entry.path);
                foreach (var material in source.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct())
                {
                    if (material == null) throw new Exception("Null material in " + entry.name);
                    string name = material.name;
                    var mapped = Mat(entry.pack, name);
                    if (mapped == null) throw new Exception("Unmapped FBX material " + entry.name + ": " + name);
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), mapped);
                }
                importer.SaveAndReimport();
                source = AssetDatabase.LoadAssetAtPath<GameObject>(entry.path);
                var wrapper = new GameObject(entry.name);
                try
                {
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(source); model.transform.SetParent(wrapper.transform, false);
                    foreach (var renderer in model.GetComponentsInChildren<Renderer>()) { renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion; renderer.receiveShadows = true; }
                    EnsureDirectory(Art + "/" + entry.pack + "/Prefabs"); PrefabUtility.SaveAsPrefabAsset(wrapper, PrefabPath(entry.pack, entry.name));
                }
                finally { UnityEngine.Object.DestroyImmediate(wrapper); }
            }
            AssetDatabase.SaveAssets(); Catalog(); Survey();
        }
        static Bounds BoundsOf(GameObject obj)
        {
            var renderers = obj.GetComponentsInChildren<Renderer>(true); if (renderers.Length == 0) throw new Exception("No renderers: " + obj.name);
            var bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds); return bounds;
        }
        static void Catalog()
        {
            Directory.CreateDirectory(Reports + "/Catalog"); var report = new ModelReports();
            var scene = EditorSceneManager.NewPreviewScene();
            var cameraObject = new GameObject("CatalogCamera"); SceneManager.MoveGameObjectToScene(cameraObject, scene); var camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene); camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.16f, .18f, .18f); camera.fieldOfView = 36; camera.nearClipPlane = .01f;
            for (int i = 0; i < 2; i++) { var g = new GameObject("StudioLight"); SceneManager.MoveGameObjectToScene(g, scene); var light = g.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = i == 0 ? 1.7f : .8f; light.color = i == 0 ? new Color(1, .92f, .82f) : new Color(.7f, .8f, 1); light.cullingMask = 1 << 30; g.transform.rotation = Quaternion.Euler(i == 0 ? 45 : 25, i == 0 ? -30 : 150, 0); }
            try
            {
                foreach (var entry in Manifest.models)
                {
                    var obj = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(entry.pack, entry.name)), scene);
                    try
                    {
                        foreach (var t in obj.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
                        var bounds = BoundsOf(obj); var meshes = obj.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh).Where(m => m != null).ToArray();
                        report.models.Add(new ModelReport { name = entry.name, pack = entry.pack, size = bounds.size, center = bounds.center,
                            vertices = meshes.Sum(m => m.vertexCount), triangles = meshes.Sum(m => Enumerable.Range(0, m.subMeshCount).Sum(i => (int)m.GetIndexCount(i) / 3)), renderers = obj.GetComponentsInChildren<Renderer>(true).Length,
                            materials = obj.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Select(m => m.name).Distinct().ToArray() });
                        float radius = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                        camera.transform.position = bounds.center + new Vector3(.8f, .65f, -1.3f).normalized * radius * 2.3f; camera.transform.LookAt(bounds.center);
                        Render(camera, Reports + "/Catalog/" + entry.pack + "_" + entry.name + ".png", 480, 360);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(obj); }
                }
                File.WriteAllText(Reports + "/model-catalog.json", JsonUtility.ToJson(report, true));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        static void Render(Camera camera, string file, int width = 1440, int height = 900)
        {
            var rt = new RenderTexture(width, height, 24); rt.Create(); var previous = RenderTexture.active; camera.targetTexture = rt;
            try { camera.Render(); RenderTexture.active = rt; var image = new Texture2D(width, height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply(); File.WriteAllBytes(file, image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image); }
            finally { camera.targetTexture = null; RenderTexture.active = previous; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
        }
        static void Survey()
        {
            var scene = EditorSceneManager.OpenPreviewScene(MultipleGuestsSetup.ScenePath);
            try
            {
                var report = new SceneReport();
                foreach (var t in All<Transform>(scene))
                {
                    var renderer = t.GetComponent<Renderer>();
                    report.objects.Add(new ObjectReport { path = FullPath(t), position = t.position, scale = t.lossyScale, rotation = t.eulerAngles,
                        size = renderer == null ? Vector3.zero : renderer.bounds.size, components = t.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name).ToArray(),
                        materials = renderer == null ? System.Array.Empty<string>() : renderer.sharedMaterials.Select(m => m == null ? "MISSING" : m.name).ToArray() });
                }
                File.WriteAllText(Reports + "/scene-survey.json", JsonUtility.ToJson(report, true));
                CaptureScene(scene, "before");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        static void CaptureScene(Scene scene, string suffix)
        {
            Directory.CreateDirectory(Reports + "/Views");
            foreach (var t in All<Transform>(scene)) t.gameObject.layer = 30;
            foreach (var light in All<Light>(scene)) light.cullingMask = 1 << 30;
            foreach (var text in All<WorldServiceText>(scene)) text.RefreshMaterial();
            foreach (var c in All<Camera>(scene)) c.enabled = false;
            var camera = All<Camera>(scene).First(); camera.scene = scene; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene); camera.cullingMask = 1 << 30; camera.fieldOfView = 68; camera.nearClipPlane = .03f;
            var views = new[] {
                ("room101", new Vector3(20,1.65f,13),new Vector3(17,1.05f,13.7f)),
                ("room101-desk",new Vector3(18.6f,1.65f,12.7f),new Vector3(19.3f,1.1f,15.1f)),
                ("bathroom101",new Vector3(19.1f,1.6f,11.85f),new Vector3(19.7f,.9f,10.6f)),
                ("lobby",new Vector3(8.2f,1.65f,1.5f),new Vector3(3.1f,1.3f,6)),
                ("reception",new Vector3(7.3f,1.65f,7.5f),new Vector3(11.8f,1.2f,4.7f)),
                ("computer",new Vector3(12.1f,1.65f,5.2f),new Vector3(10.8f,1.3f,4.9f)),
                ("utility-shelves",new Vector3(16.7f,1.65f,4.3f),new Vector3(17,1.25f,6.35f)),
                ("utility",new Vector3(14.9f,1.65f,5.7f),new Vector3(17.1f,1.35f,3.4f)),
                ("corridor",new Vector3(22.2f,1.65f,10),new Vector3(22.2f,1.6f,20)),
                ("exterior",new Vector3(3,1.65f,-16.5f),new Vector3(8,1.5f,1)),
                ("parking",new Vector3(15.5f,1.65f,-.8f),new Vector3(6,.8f,-10)),
                ("dumpster",new Vector3(15.8f,1.65f,-4.8f),new Vector3(18.3f,.8f,-3.1f)) };
            foreach (var view in views) { camera.transform.position = view.Item2; camera.transform.LookAt(view.Item3); Render(camera, Reports + "/Views/" + view.Item1 + "-" + suffix + ".png"); }
            for(int i=1;i<4;i++) {
                camera.transform.position=RoomPoint(i,20,1.65f,13);camera.transform.LookAt(RoomPoint(i,17,1.05f,13.7f));Render(camera,Reports+"/Views/room"+(101+i)+"-"+suffix+".png");
                camera.transform.position=RoomPoint(i,19.1f,1.6f,11.85f);camera.transform.LookAt(RoomPoint(i,19.7f,.9f,10.6f));Render(camera,Reports+"/Views/bathroom"+(101+i)+"-"+suffix+".png");
                camera.transform.position=RoomPoint(i,18.6f,1.65f,12.7f);camera.transform.LookAt(RoomPoint(i,19.3f,1.2f,15.1f));Render(camera,Reports+"/Views/desk"+(101+i)+"-"+suffix+".png");
            }
            var first=Find(scene,"Motel/Rooms/Room101/ServiceState");
            if(first!=null){first.Find("MadeBedding").gameObject.SetActive(true);first.Find("UnmadeBedding").gameObject.SetActive(false);camera.transform.position=new Vector3(20,1.65f,13);camera.transform.LookAt(new Vector3(17,1.05f,13.7f));Render(camera,Reports+"/Views/room101-clean-"+suffix+".png");}
            var room=All<ServiceRoom>(scene).Single(r=>r.RoomNumber=="101");foreach(var light in room.ControlledLights)light.enabled=false;
            Render(camera,Reports+"/Views/room101-poweroff-"+suffix+".png");
        }
        static partial void ExecuteSceneCommand(string command);
    }
}
