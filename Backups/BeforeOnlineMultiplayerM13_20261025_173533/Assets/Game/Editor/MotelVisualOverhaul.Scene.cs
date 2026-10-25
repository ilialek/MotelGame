using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using MotelGame.Service;
using MotelGame.Interactions;

namespace MotelGame.Editor
{
    public static partial class MotelVisualOverhaul
    {
        const string Generated = Art + "/Motel";
        static partial void ExecuteSceneCommand(string command)
        {
            var scene = SceneManager.GetSceneByPath(MultipleGuestsSetup.ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (!opened && scene.isDirty) throw new Exception("Unsaved scene edits; save them before the explicit art pass.");
            var previous = SceneManager.GetActiveScene();
            try
            {
                if (opened) scene = EditorSceneManager.OpenScene(MultipleGuestsSetup.ScenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene); EnsureDirectory(Generated + "/Materials"); EnsureDirectory(Generated + "/Meshes");
                if (command == "room101") UpgradeRoom(scene, 0);
                else if (command == "rooms") { for (int i = 1; i < 4; i++) UpgradeRoom(scene, i); }
                else if (command == "lobby") UpgradeLobby(scene);
                else if (command == "utility") UpgradeUtility(scene);
                else if (command == "exterior") UpgradeExterior(scene);
                else if (command == "refine") Refine(scene);
                else if (command == "sign") RefineSign(scene);
                else if (command != "capture") throw new Exception("Unknown art command: " + command);
                if (command != "capture")
                {
                    foreach (var text in All<WorldServiceText>(scene)) text.RefreshMaterial();
                    AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                }
            }
            finally { if (previous.IsValid()) SceneManager.SetActiveScene(previous); if (opened && scene.IsValid()) EditorSceneManager.CloseScene(scene, true); }
            var preview = EditorSceneManager.OpenPreviewScene(MultipleGuestsSetup.ScenePath);
            try { CaptureScene(preview, command); } finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        static Transform Group(string name, Transform parent)
        {
            var existing = parent.Find(name); if (existing != null) return existing;
            var obj = new GameObject(name); obj.transform.SetParent(parent, false); return obj.transform;
        }
        static void Hide(Transform t) { if (t != null && t.TryGetComponent<Renderer>(out var renderer)) renderer.enabled = false; }
        static Transform Find(Scene scene, string path) => All<Transform>(scene).FirstOrDefault(t => FullPath(t) == path);
        static GameObject Model(string pack, string name, Transform parent, Vector3 center, Vector3 size, float yaw = 0)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(pack, name)); if (prefab == null) throw new Exception("Missing model: " + name);
            var obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
            obj.name = "Visual_" + name; obj.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var bounds = BoundsOf(obj);
            // Scale in world axes before attaching. The wrapper compensates for FBX pivots and scaled original roots.
            var targetScale = new Vector3(size.x / bounds.size.x, size.y / bounds.size.y, size.z / bounds.size.z);
            bool swapped = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) > .9f;
            obj.transform.localScale = swapped ? new Vector3(targetScale.z, targetScale.y, targetScale.x) : targetScale;
            bounds = BoundsOf(obj); obj.transform.position += center - bounds.center;
            obj.transform.SetParent(parent, true);
            foreach (var collider in obj.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            foreach (var renderer in obj.GetComponentsInChildren<Renderer>(true)) renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return obj;
        }
        static Material Variant(string name, Material source, Color tint)
        {
            string path = Generated + "/Materials/" + name + ".mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(source); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", tint); material.enableInstancing = true; EditorUtility.SetDirty(material); return material;
        }
        static Material Solid(string name, Color color, float smoothness = .15f)
        {
            string path = Generated + "/Materials/" + name + ".mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", smoothness); material.enableInstancing = true; EditorUtility.SetDirty(material); return material;
        }
        static void Surface(Transform t, Material material, Vector3 repeats)
        {
            if (t == null || !t.TryGetComponent<MeshFilter>(out var filter) || !t.TryGetComponent<Renderer>(out var renderer)) return;
            renderer.sharedMaterial = material;
            var source = filter.sharedMesh; if (source == null) return;
            string key = FullPath(t) + t.position.ToString("F4") + t.lossyScale.ToString("F4") + repeats.ToString("F4");
            string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").Substring(0, 20);
            string path = Generated + "/Meshes/UV_" + hash + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = UnityEngine.Object.Instantiate(source); mesh.name = t.name + "_WorldScaledUV";
                var uv = new Vector2[mesh.vertexCount]; var positions = mesh.vertices; var normals = mesh.normals;
                for (int i = 0; i < uv.Length; i++)
                {
                    Vector3 point = Vector3.Scale(t.TransformPoint(positions[i]), repeats);
                    Vector3 n = t.TransformDirection(normals[i]);
                    uv[i] = Mathf.Abs(n.y) > .7f ? new Vector2(point.x, point.z) : Mathf.Abs(n.x) > .7f ? new Vector2(point.z, point.y) : new Vector2(point.x, point.y);
                }
                mesh.uv = uv; mesh.RecalculateTangents(); AssetDatabase.CreateAsset(mesh, path);
            }
            filter.sharedMesh = mesh;
        }
        static GameObject DetailBox(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = name; obj.transform.SetParent(parent, false);
            obj.transform.position = position; obj.transform.localScale = size; UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
            Surface(obj.transform, material, Vector3.one); return obj;
        }
        static Vector3 RoomPoint(int i, float x, float y, float z) => new Vector3(i % 2 == 0 ? x : 44.4f - x, y, z + (i >= 2 ? 5.7f : 0));
        static void UpgradeRoom(Scene scene, int index)
        {
            string number = (101 + index).ToString(); var room = All<ServiceRoom>(scene).Single(r => r.RoomNumber == number);
            var root = Find(scene, "Motel/Rooms/Room" + number); if (root.Find("PizzaDoggyDecor") != null) throw new Exception("Room already has art: " + number);
            var decor = Group("PizzaDoggyDecor", root); var furniture = root.Find("Furniture"); var service = root.Find("ServiceState");
            var wall = Variant("Wallpaper" + index, Mat("TexturesII", "wallpaper_wall_pt_1_1"), new[] { new Color(1,.95f,.86f), new Color(.91f,.96f,.92f), new Color(.94f,.9f,.83f), new Color(.88f,.93f,1) }[index]);
            var carpet = Mat("TexturesII", index % 2 == 0 ? "carpet_pt_1" : "carpet_pt_1_2");
            var wood = Mat("TexturesI", "wood_6"); var tile = Mat("TexturesII", "tiles_floor_pt_5_1");
            var ceiling = Mat("TexturesII", "painted_concrete_wall_pt_5_plain");
            foreach (var t in root.Find("Structure").GetComponentsInChildren<Transform>()) Surface(t, t.name.StartsWith("Floor") ? carpet : wall, t.name.StartsWith("Floor") ? new Vector3(.7f,1,.7f) : new Vector3(.65f,.36f,.65f));
            foreach (var t in root.Find("Ceilings").GetComponentsInChildren<Transform>()) Surface(t, ceiling, Vector3.one * .4f);
            foreach (var t in root.Find("Bathroom").GetComponentsInChildren<Transform>()) Surface(t, tile, Vector3.one * .7f);
            foreach (var t in root.Find("Windows").GetComponentsInChildren<Transform>()) if (!t.name.Contains("Glass")) Surface(t, wood, Vector3.one);
            foreach (string name in new[] { "DoubleBed_base", "DoubleBed_mattress", "Headboard", "DeskTop", "ChairSeat", "ChairBack", "Wardrobe", "Toilet" }) Hide(furniture.Find(name));
            foreach (var t in furniture.GetComponentsInChildren<Transform>().Where(t => t.name == "DeskLeg" || t.name == "BedsideTable")) Hide(t);
            Model("MegaPack", index % 2 == 0 ? "bed_4" : "bed_4_1", furniture.Find("DoubleBed_base"), RoomPoint(index,17,.43f,13.6f),new Vector3(1.66f,.86f,2.17f),90);
            foreach (var table in furniture.GetComponentsInChildren<Transform>().Where(t => t.name == "BedsideTable").ToArray())
                Model("MegaPack", index % 2 == 0 ? "bedside_table_1" : "bedside_table_2", table, table.position,new Vector3(.45f,.6f,.5f),180);
            Model("MegaPack","wardrobe_mp_1",furniture.Find("Wardrobe"),RoomPoint(index,16,1.05f,11.7f),new Vector3(.65f,2.1f,1.2f),index%2==0?90:270);
            Model("MegaPack","table_large_3",furniture.Find("DeskTop"),RoomPoint(index,19.3f,.395f,15.12f),new Vector3(1.25f,.79f,.6f),180);
            Model("MegaPack","chair_mp_4",furniture.Find("ChairSeat"),RoomPoint(index,19.3f,.48f,14.45f),new Vector3(.47f,.96f,.48f),0);
            // Existing clean/dirty roots still control visibility. Only their visual children change.
            var fabric = Variant("BedLinen" + index, Mat("MegaPack","old_mattress_mp_2_1"),new Color(1,.98f,.9f));
            foreach (var t in service.Find("UnmadeBedding").GetComponentsInChildren<Transform>()) Surface(t, fabric, Vector3.one * 1.3f);
            foreach (var t in service.Find("MadeBedding").GetComponentsInChildren<Transform>()) Surface(t, fabric, Vector3.one * 1.3f);
            foreach (var t in service.Find("MadeBedding").GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Pillow")).ToArray())
            { Hide(t); Model("MegaPack","pillow_mp_1",t,t.position,new Vector3(.57f,.14f,.36f),index%2==0?0:180); }
            Model("MegaPack","pillow_mp_1",service.Find("UnmadeBedding"),RoomPoint(index,16.65f,.64f,14.2f),new Vector3(.57f,.14f,.36f),15);
            for(int i=0;i<room.Trash.Length;i++)
            {
                var old = room.Trash[i].transform.Find("DiscardedPackaging"); Hide(old);
                Model(i==0?"MegaPackII":"MegaPack",i==0?"plastic_bottle_mx_1_dirty_squashed":"cardboard_box_1",old,room.Trash[i].TargetCollider.bounds.center,new Vector3(.28f,.16f,.24f),i==0?35:15);
            }
            Model("MegaPack","toilet_mp_1",furniture.Find("Toilet"),RoomPoint(index,20.15f,.42f,11.45f),new Vector3(.5f,.84f,.7f),index%2==0?270:90);
            Surface(furniture.Find("Basin"),Solid("Porcelain",new Color(.67f,.68f,.61f),.45f),Vector3.one);
            Surface(furniture.Find("ShowerTray"),tile,Vector3.one*2);
            DetailBox("BathroomTiles",decor,RoomPoint(index,19.6f,.003f,11),new Vector3(1.95f,.006f,1.9f),tile);
            DetailBox("MirrorFrame",decor,RoomPoint(index,19.05f,1.55f,10.035f),new Vector3(.64f,.78f,.045f),wood);
            DetailBox("MirrorGlass",decor,RoomPoint(index,19.05f,1.55f,10.062f),new Vector3(.56f,.69f,.012f),Solid("AgedMirror",new Color(.36f,.43f,.44f),.8f));
            DetailBox("BasinInset",decor,RoomPoint(index,19.05f,.897f,10.32f),new Vector3(.43f,.008f,.29f),Solid("BasinShadow",new Color(.24f,.27f,.26f),.35f));
            DetailBox("FaucetUpright",decor,RoomPoint(index,19.05f,1.0f,10.13f),new Vector3(.032f,.22f,.032f),Solid("DullChrome",new Color(.38f,.4f,.38f),.65f));
            DetailBox("FaucetSpout",decor,RoomPoint(index,19.05f,1.1f,10.2f),new Vector3(.032f,.032f,.15f),Solid("DullChrome",new Color(.38f,.4f,.38f),.65f));
            Model("MegaPackII","soap_mx_1",decor,RoomPoint(index,19.28f,.93f,10.26f),new Vector3(.1f,.035f,.07f),10);
            DetailBox("ShowerRiser",decor,RoomPoint(index,20.45f,1.15f,10.3f),new Vector3(.025f,2.1f,.025f),Solid("DullChrome",new Color(.38f,.4f,.38f),.65f));
            DetailBox("ShowerHead",decor,RoomPoint(index,20.38f,2.2f,10.3f),new Vector3(.18f,.04f,.14f),Solid("DullChrome",new Color(.38f,.4f,.38f),.65f));
            Model("MegaPack","curtain_rod_mp_1",decor,RoomPoint(index,15.7f,2.43f,12.75f),new Vector3(.09f,.06f,3.38f),90);
            var curtain = Variant("Curtain"+index,Mat("MegaPack","fabric_1"),new Color(.83f,.83f,.72f));
            for(int side=0;side<2;side++)for(int fold=0;fold<6;fold++)
                DetailBox("CurtainFold",decor,RoomPoint(index,15.69f+(fold%2)*.035f,1.58f,(side==0?11.18f:13.9f)+fold*.07f),new Vector3(.04f,1.63f,.09f),curtain);
            Model("MegaPack","painting_1",decor,RoomPoint(index,17.1f,1.95f,15.46f),new Vector3(.75f,.58f,.055f),180);
            Model("MegaPack","open_book_mp_1",decor,RoomPoint(index,18.05f,.625f,14.3f),new Vector3(.27f,.05f,.2f),index*20);
            Model("MegaPackII","bucket_mx_1",decor,RoomPoint(index,19.92f,.15f,14.92f),new Vector3(.25f,.3f,.25f),0);
            Model("MegaPack","ceiling_lamp_2_off",decor,RoomPoint(index,18.1f,2.72f,12.9f),new Vector3(.44f,.14f,1.0f),90);
            UpgradeTelevision(scene,room,decor,index);
            UpgradeDoor(room,decor);
            RefineRoom(scene,index);
            // Reuse the existing controlled light; no independent lamp can remain lit during a fault.
        }
        static void UpgradeTelevision(Scene scene,ServiceRoom room,Transform decor,int index)
        {
            var tv = room.Television.transform; var housing=tv.Find("TVHousing");Hide(housing);
            Model("MegaPack",index%2==0?"tv_mp_1":"tv_mp_1_1",housing,housing.position,new Vector3(.68f,.53f,.38f),180);
            // Existing screen, status and two control colliders remain in their exact positions, over the imported CRT face.
            var screen=tv.Find("TVScreen");screen.position+=Vector3.back*.012f;
            var status=tv.Find("TVStatus");status.position+=Vector3.back*.012f;
            Model("MegaPack","tv_remote_mp_1",decor,RoomPoint(index,19.8f,.81f,14.97f),new Vector3(.065f,.025f,.16f),10);
        }
        static void UpgradeDoor(ServiceRoom room,Transform decor)
        {
            var door = room.Door; var leaf = door.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r=>r.GetComponent<Collider>()!=null);
            if(leaf==null)throw new Exception("Door leaf renderer not found: "+room.RoomNumber);
            var b=leaf.bounds; leaf.enabled=false;
            Model("MegaPack","door_2_loose",leaf.transform,b.center,new Vector3(b.size.x+.04f,b.size.y,b.size.z),90);
        }
        static partial void UpgradeLobby(Scene scene);
        static partial void UpgradeUtility(Scene scene);
        static partial void UpgradeExterior(Scene scene);
        static partial void Refine(Scene scene);
    }
}
