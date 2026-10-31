using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using MotelGame.Service;
using MotelGame.Characters;
using MotelGame.Registration;

namespace MotelGame.Editor
{
    public static partial class MotelRoadWatch
    {
        static void UpgradeVehicleCabin()
        {
            string path=Content+"/GuestVehicle.prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                string meshPath=Content+"/SedanCabin.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if(mesh==null)
                {
                    mesh=new Mesh{name="SlopedCabin"};var corners=new[]{new Vector3(-.82f,0,-1.1f),new Vector3(.82f,0,-1.1f),new Vector3(.82f,0,1.1f),new Vector3(-.82f,0,1.1f),new Vector3(-.73f,.58f,-.77f),new Vector3(.73f,.58f,-.77f),new Vector3(.73f,.58f,.65f),new Vector3(-.73f,.58f,.65f)};
                    int[] faces={0,1,5,4,1,2,6,5,2,3,7,6,3,0,4,7,4,5,6,7,3,2,1,0};var vertices=new System.Collections.Generic.List<Vector3>();var indices=new System.Collections.Generic.List<int>();
                    for(int f=0;f<6;f++){for(int k=0;k<4;k++)vertices.Add(corners[faces[f*4+k]]);int n=f*4;indices.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
                    mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,meshPath);
                }
                var cabin=root.transform.Find("Sedan/CabinGlass");cabin.localPosition=new Vector3(0,.97f,-.1f);cabin.localScale=Vector3.one;cabin.GetComponent<MeshFilter>().sharedMesh=mesh;
                var roof=root.transform.Find("Sedan/Roof");roof.localPosition=new Vector3(0,1.57f,-.16f);roof.localScale=new Vector3(1.5f,.08f,1.5f);
                foreach(var t in root.transform.Find("Sedan").Cast<Transform>().Where(t=>t.name=="WindowPillar").ToArray())Destroy(t.gameObject);
                var view=root.GetComponent<GuestVehicleView>();
                foreach(var plate in view.plates){plate.characterSize=.024f;plate.color=new Color(.015f,.02f,.025f);}
                var plateMaterial=AssetDatabase.LoadAssetAtPath<Material>(Content+"/Materials/Plate.mat");plateMaterial.shader=Shader.Find("Universal Render Pipeline/Unlit");EditorUtility.SetDirty(plateMaterial);
                var paintMaterial=AssetDatabase.LoadAssetAtPath<Material>(Content+"/Materials/VehiclePaint.mat");paintMaterial.EnableKeyword("_EMISSION");EditorUtility.SetDirty(paintMaterial);
                view.paint=view.paint.Where(r=>r!=null).ToArray();
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        static void Photographs()
        {
            UpgradeVehicleCabin();var sourceScene=Scene();var reg=All<RegistrationDesk>(sourceScene).Single();var source=reg.Reception.Guests[0].GetComponentInChildren<CharacterVisuals>(true);
            string[] names={"01_pine_cut","02_marlow_fuel","03_alder_bridge","04_bowling_lot"};
            for(int i=0;i<4;i++)
            {
                var s=EditorSceneManager.NewPreviewScene();var root=new GameObject("RoadWatchStage");SceneManager.MoveGameObjectToScene(root,s);
                try
                {
                    var c=reg.Cases[i];var grass=Material("NightGround",RGB("26362D"));var asphalt=Material("Asphalt",RGB("363B3D"));var line=Material("RoadLine",RGB("ACA77F"));var wood=Material("Timber",RGB("675244"));var orange=Material("SafetyOrange",RGB("AF5A2D"));var blood=Material("RoadsideBlood",RGB("8E1223"));var black=Material("DarkCargo",RGB("292E31"));
                    Box("Ground",root.transform,new Vector3(0,-.15f,0),new Vector3(60,.25f,60),grass);
                    Box("Road",root.transform,new Vector3(-4,-.015f,0),new Vector3(6,.05f,45),asphalt);
                    for(int z=-20;z<24;z+=5)Box("RoadStripe",root.transform,new Vector3(-4,.018f,z),new Vector3(.12f,.008f,2.6f),line);
                    string treePath=AssetDatabase.FindAssets("pine_tree_n_2 t:Prefab",new[]{"Assets/Game/Art/PizzaDoggy"}).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
                    if(treePath!=null)for(int n=0;n<14;n++)
                    {var tree=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(treePath),root.transform);tree.transform.localPosition=new Vector3(n%2==0?11+n%3:-12-n%3,0,4+n*2.4f);tree.transform.localScale=Vector3.one*(.8f+(n%4)*.15f);}
                    var car=UnityEngine.Object.Instantiate(VehiclePrefab(),root.transform);car.name="ReportedVehicle";var cv=car.GetComponent<GuestVehicleView>();
                    cv.Apply(new GuestRegistrationState{Active=true,Visit=1,Vehicle=(byte)c.vehicleKind,Paint=new Vector3(c.vehicleColor.r,c.vehicleColor.g,c.vehicleColor.b),ActualPlate=new Unity.Collections.FixedString32Bytes(c.actualPlate)},true);
                    var actor=UnityEngine.Object.Instantiate(source.gameObject,root.transform);actor.name="PhotographedGuest";actor.SetActive(true);actor.transform.localPosition=new Vector3(1.35f,0,-1.6f);actor.transform.localRotation=Quaternion.Euler(0,160,0);var visual=actor.GetComponent<CharacterVisuals>();visual.ApplyGuestAppearance(c.appearance,c.clothing);visual.SetVisible(true);
                    var idle=AssetDatabase.LoadAllAssetsAtPath("Assets/Game/Art/Characters/Quaternius/Casual.fbx").OfType<AnimationClip>().First(a=>a.name=="Idle");idle.SampleAnimation(visual.Appearance,0);visual.enabled=false;visual.Animator.enabled=false;
                    Vector3 cameraPos=new Vector3(3.8f,1.9f,-5.3f),aim=new Vector3(0,1,-1f);
                    if(i==0)
                    {
                        car.transform.Find("Sedan/TrunkHinge").localRotation=Quaternion.Euler(65,0,0);
                        Box("CoveredLoad",car.transform,new Vector3(0,1.02f,-1.6f),new Vector3(1.15f,.22f,.66f),black);
                        var antler=Box("Antler",car.transform,new Vector3(-.45f,1.18f,-1.92f),new Vector3(.07f,.34f,.07f),wood);antler.transform.localRotation=Quaternion.Euler(15,0,-30);
                        Box("AntlerTip",car.transform,new Vector3(-.52f,1.3f,-1.92f),new Vector3(.17f,.05f,.06f),wood);
                        foreach(float x in new[]{-.56f,.49f})Box("BloodLeak",car.transform,new Vector3(x,.48f,-2.23f),new Vector3(.025f,.94f,.025f),blood);
                        foreach(float x in new[]{-.56f,.49f})
                        {var pool=GameObject.CreatePrimitive(PrimitiveType.Cylinder);pool.name="BloodPuddle";pool.transform.SetParent(car.transform,false);pool.transform.localPosition=new Vector3(x,.028f,-2.35f);pool.transform.localScale=new Vector3(.6f,.008f,.45f);pool.GetComponent<Renderer>().sharedMaterial=blood;Destroy(pool.GetComponent<Collider>());}
                        for(int leg=0;leg<3;leg++){var triangle=Box("WarningTriangle",root.transform,new Vector3(-1.25f,.24f,-3),new Vector3(.55f,.045f,.045f),orange);triangle.transform.localPosition+=new Vector3(leg==1?-.14f:leg==2?.14f:0,leg==0?0:.22f,0);triangle.transform.localRotation=Quaternion.Euler(0,0,leg==1?60:leg==2?-60:0);}
                    }
                    else if(i==1)
                    {
                        Box("ServiceApron",root.transform,new Vector3(1,0,0),new Vector3(11,.08f,12),Material("Concrete",RGB("62645A")));
                        var pump=Group("PumpThree",root.transform,new Vector3(2.9f,0,-.5f));Box("PumpBase",pump,new Vector3(0,.65f,0),new Vector3(.75f,1.3f,.65f),orange);Box("PumpTop",pump,new Vector3(0,1.55f,0),new Vector3(.9f,.65f,.7f),line);
                        Text("PumpNumber","3",pump,new Vector3(0,1.55f,-.36f),0,.07f,Color.black);Box("OpenPanel",pump,new Vector3(.52f,.65f,-.4f),new Vector3(.55f,.75f,.08f),black).transform.localRotation=Quaternion.Euler(0,-40,0);
                        Box("ToolCase",root.transform,new Vector3(2,.15f,-1.5f),new Vector3(.7f,.28f,.4f),black);Box("Spanner",root.transform,new Vector3(2,.3f,-1.5f),new Vector3(.32f,.035f,.04f),line);
                        actor.transform.localPosition=new Vector3(1.7f,0,-.6f);actor.transform.localRotation=Quaternion.Euler(0,100,0);
                        Box("Station",root.transform,new Vector3(1,1.6f,5),new Vector3(11,3.2f,.4f),wood);Text("StationSign","MARLOW FUEL\nAFTER HOURS SERVICE: 555-0193",root.transform,new Vector3(1,2.2f,4.76f),0,.038f,line.color);
                        cameraPos=new Vector3(5.8f,2.2f,-6.8f);aim=new Vector3(1.1f,1,-.2f);
                    }
                    else if(i==2)
                    {
                        foreach(float x in new[]{-5.3f,-.7f})
                        {
                            Box("BridgeRailing",root.transform,new Vector3(x,.6f,5.1f),new Vector3(.13f,.14f,3f),line);
                            foreach(float z in new[]{3.7f,5f,6.3f})Box("BridgePost",root.transform,new Vector3(x,.4f,z),new Vector3(.1f,.8f,.1f),wood);
                        }
                        Box("BridgeNearBrokenDeck",root.transform,new Vector3(-3,.1f,4),new Vector3(4.6f,.2f,2),wood);
                        Box("BridgeFarBrokenDeck",root.transform,new Vector3(-3,.1f,10),new Vector3(4.6f,.2f,2),wood);
                        Box("River",root.transform,new Vector3(-3,.03f,6),new Vector3(16,.03f,7),Material("RiverWater",RGB("182F3C")));
                        var barrier=Group("ClosedBarrier",root.transform,new Vector3(-2,0,2));Box("Crossbar",barrier,new Vector3(0,1.05f,0),new Vector3(5,.27f,.18f),orange);foreach(float x in new[]{-2f,2f})Box("Post",barrier,new Vector3(x,.65f,0),new Vector3(.15f,1.3f,.15f),line);
                        Box("ClosureBoard",barrier,new Vector3(0,1.5f,-.12f),new Vector3(2.5f,.7f,.08f),line);Text("Closed","BRIDGE CLOSED\n18:00 / WASHOUT",barrier,new Vector3(0,1.5f,-.17f),0,.04f,Color.black);
                        actor.transform.localPosition=new Vector3(1.35f,0,-.8f);cameraPos=new Vector3(5.5f,2.3f,-6.6f);aim=new Vector3(-.2f,1.1f,.6f);
                    }
                    else
                    {
                        Box("ParkingLot",root.transform,new Vector3(0,0,0),new Vector3(15,.05f,15),asphalt);
                        for(int n=-1;n<3;n++)Box("BayLine",root.transform,new Vector3(-1.7f+n*3.3f,.035f,-.8f),new Vector3(.1f,.01f,5),line);
                        var wrong=UnityEngine.Object.Instantiate(car,root.transform);wrong.name="SimilarCar";wrong.transform.localPosition=new Vector3(3.3f,0,0);foreach(var p in wrong.GetComponent<GuestVehicleView>().plates)p.text="LMP-525";
                        actor.transform.localPosition=new Vector3(2.1f,0,-2.5f);actor.transform.localRotation=Quaternion.Euler(0,90,0);
                        Box("BowlingWall",root.transform,new Vector3(2,1.9f,5),new Vector3(14,3.8f,.4f),wood);Text("BowlingSign","COUNTY NINE BOWL",root.transform,new Vector3(2,2.8f,4.75f),0,.075f,line.color);
                        cameraPos=new Vector3(6.7f,2.5f,-7.8f);aim=new Vector3(1.5f,1,.1f);
                    }
                    var flash=Group("FlashDirection",root.transform,cameraPos);flash.LookAt(aim);var key=flash.gameObject.AddComponent<Light>();key.type=LightType.Directional;key.intensity=1.3f;key.color=new Color(.78f,.85f,1);key.shadows=LightShadows.None;
                    var light=Group("CameraFlash",root.transform,cameraPos).gameObject.AddComponent<Light>();light.type=LightType.Point;light.intensity=6;light.range=16;light.color=new Color(.8f,.88f,1);light.shadows=LightShadows.None;
                    var fill=Group("RoadLamp",root.transform,new Vector3(-3,6,1)).gameObject.AddComponent<Light>();fill.type=LightType.Point;fill.intensity=10;fill.range=24;fill.color=new Color(1,.7f,.43f);fill.shadows=LightShadows.None;
                    var ct=Group("SnapshotCamera",root.transform,cameraPos);ct.LookAt(aim);var camera=ct.gameObject.AddComponent<Camera>();camera.scene=s;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(s);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=RGB("101A22");camera.fieldOfView=52;camera.nearClipPlane=.05f;camera.farClipPlane=80;
                    foreach(var t in All<Transform>(s))t.gameObject.layer=30;foreach(var l in All<Light>(s))l.cullingMask=1<<30;camera.cullingMask=1<<30;
                    foreach(var text in All<WorldServiceText>(s))text.RefreshMaterial();
                    var rt=new RenderTexture(480,320,24){filterMode=FilterMode.Point};var previous=RenderTexture.active;camera.targetTexture=rt;
                    try
                    {
                        camera.Render();RenderTexture.active=rt;var image=new Texture2D(480,320,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,480,320),0,0);image.Apply();
                        // Small sensor noise on the native low-resolution render; never pixelate a photo-real source.
                        var pixels=image.GetPixels();var random=new System.Random(430+i);for(int p=0;p<pixels.Length;p++){float noise=(float)(random.NextDouble()-.5)*.025f;var color=pixels[p];float gray=color.grayscale;color=Color.Lerp(color,new Color(gray,gray,gray),.12f);pixels[p]=new Color(Mathf.Clamp01(color.r+noise),Mathf.Clamp01(color.g+noise),Mathf.Clamp01(color.b*.93f+noise));}image.SetPixels(pixels);image.Apply();File.WriteAllBytes(Content+"/Photos/"+names[i]+".png",image.EncodeToPNG());Destroy(image);
                    }
                    finally{camera.targetTexture=null;RenderTexture.active=previous;rt.Release();Destroy(rt);}
                }
                finally{EditorSceneManager.ClosePreviewScene(s);}
            }
            AssetDatabase.Refresh();foreach(string name in names){var importer=(TextureImporter)AssetImporter.GetAtPath(Content+"/Photos/"+name+".png");importer.textureType=TextureImporterType.GUI;importer.filterMode=FilterMode.Point;importer.mipmapEnabled=false;importer.npotScale=TextureImporterNPOTScale.None;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=512;importer.SaveAndReimport();}
            CreateCases();
        }
    }
}
