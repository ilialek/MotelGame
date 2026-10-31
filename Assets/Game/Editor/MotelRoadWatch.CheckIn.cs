using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using Unity.Netcode;
using MotelGame.Registration;
using MotelGame.Service;
using MotelGame.Characters;
using MotelGame.Horror;

namespace MotelGame.Editor
{
    public static partial class MotelRoadWatch
    {
        const string CheckInReports="Documentation/CheckIn";
        [MenuItem("Motel/Check-In/Configure current V4 scene")]
        public static void PrepareCheckIn()
        {
            Directory.CreateDirectory(CheckInReports);Directory.CreateDirectory(Content+"/Portraits");AssetDatabase.Refresh();
            var scene=Scene();var reg=All<RegistrationDesk>(scene).Single();reg.Cases=CheckInCases();
            UpgradeDriverDoors();
            var vehicles=All<Transform>(scene).Single(t=>t.name=="GuestParking");
            foreach(var guest in reg.Reception.Guests)
            {
                int index=Array.IndexOf(reg.Reception.Guests,guest);var bay=vehicles.Find("GuestBay_"+(index+1));
                bay.position=new Vector3(index<6?.5f+3.2f*index:26.5f,0,-22);
                var slot=bay.GetComponent<ParkingSlot>()??bay.gameObject.AddComponent<ParkingSlot>();slot.number=index+1;
                var driver=bay.Find("Arrival");driver.localPosition=new Vector3(-1.35f,0,.4f);slot.driverPoint=driver;
                bay.Find("Departure").localPosition=driver.localPosition;
                MakeRoute(slot);
                var record=guest.GetComponent<RegisteredGuest>();var journey=guest.GetComponent<GuestVehicleJourney>()??guest.gameObject.AddComponent<GuestVehicleJourney>();journey.slot=slot;journey.vehicle=record.vehicle;
                Set(guest,"arrivalPoint",driver);Set(guest,"departurePoint",driver);
                var marker=bay.Find("SpaceNumber");if(marker!=null)Destroy(marker.gameObject);
                var label=Text("SpaceNumber",slot.Code,bay,new Vector3(0,.045f,2.8f),0,.075f,RGB("D5CCAA"));label.transform.rotation=Quaternion.Euler(90,180,0);
                // Car starts hidden; its server-owned journey controls arrival and parking.
                record.vehicle.transform.SetPositionAndRotation(bay.position,Quaternion.identity);record.vehicle.Show(false);
                EditorUtility.SetDirty(slot);EditorUtility.SetDirty(journey);EditorUtility.SetDirty(guest);
            }
            var surveillance=All<VentSurveillance>(scene).Single();surveillance.cameraLabels=new[]{"Vent 1","Vent 2","Vent 3","P01","P02","P03","P04","P05","P06","P07"};
            foreach(var t in All<Transform>(scene).Where(t=>t.name=="ParkingBayLine"))
            {var p=t.position;if(p.z< -15&&p.z> -25){p.z=-22;t.position=p;}}
            var oldMarks=vehicles.Find("AdditionalBayMarkings");if(oldMarks!=null)Destroy(oldMarks.gameObject);
            var marks=Group("AdditionalBayMarkings",vehicles);var paint=Material("SpaceMarking",RGB("AAA58B"));
            foreach(float x in new[]{18.2f,25f,28f})Box("BayEdge",marks,new Vector3(x,.035f,-22),new Vector3(.08f,.01f,5),paint);
            EditorUtility.SetDirty(reg);EditorUtility.SetDirty(surveillance);
            CaptureCheckInPortraits();
            foreach(var no in All<NetworkObject>(scene))typeof(NetworkObject).GetMethod("OnValidate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(no,null);
            MotelLayoutV4.RebakeExistingRoadWatch(scene);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            ValidateCheckIn();
        }
        static void UpgradeDriverDoors()
        {
            string path=Content+"/GuestVehicle.prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<GuestVehicleView>();var renderers=view.paint.Where(r=>r!=null).ToList();var material=AssetDatabase.LoadAssetAtPath<Material>(Content+"/Materials/VehiclePaint.mat");
                foreach(var body in view.bodies)
                {
                    var hinge=body.transform.Find("DriverDoorHinge");if(hinge!=null)continue;
                    hinge=Group("DriverDoorHinge",body.transform,new Vector3(-.925f,.85f,.95f));
                    renderers.Add(Box("DriverDoor",hinge,new Vector3(0,0,-.48f),new Vector3(.045f,.48f,.96f),material).GetComponent<Renderer>());
                    Box("DriverWindow",hinge,new Vector3(.015f,.43f,-.48f),new Vector3(.035f,.38f,.9f),AssetDatabase.LoadAssetAtPath<Material>(Content+"/Materials/Glass.mat"));
                }
                view.paint=renderers.ToArray();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        static void MakeRoute(ParkingSlot slot)
        {
            float x=slot.transform.position.x;var start=new Vector3(x+4,0,-28.5f);var curve=new List<Vector3>();
            var b=new Vector3(x+1.2f,0,-28.5f);var c=new Vector3(x,0,-26.8f);var d=new Vector3(x,0,-24.9f);
            for(int i=0;i<=16;i++){float t=i/16f,u=1-t;curve.Add(u*u*u*start+3*u*u*t*b+3*u*t*t*c+t*t*t*d);}
            curve.Add(slot.transform.position);slot.arrival=new[]{new Vector3(37,0,-28.5f)}.Concat(curve).ToArray();slot.reverse=curve.AsEnumerable().Reverse().ToArray();slot.departure=new[]{start,new Vector3(-13,0,-28.5f)};
        }
        [MenuItem("Motel/Check-In/Capture license portraits")]
        public static void CaptureCheckInPortraits()
        {
            var reg=All<RegistrationDesk>(SceneManager.GetActiveScene()).Single();var source=reg.Reception.Guests[0].GetComponentInChildren<CharacterVisuals>(true);
            foreach(var c in reg.Cases)
            {
                var scene=EditorSceneManager.NewPreviewScene();var root=new GameObject("LicensePortrait");SceneManager.MoveGameObjectToScene(root,scene);
                try
                {
                    var actor=UnityEngine.Object.Instantiate(source.gameObject,root.transform);actor.SetActive(true);actor.transform.localPosition=Vector3.zero;actor.transform.localRotation=Quaternion.identity;
                    var visual=actor.GetComponent<CharacterVisuals>();visual.ApplyGuestAppearance(c.appearance,c.clothing);visual.SetVisible(true);
                    var idle=AnimationUtility.GetAnimationClips(actor).FirstOrDefault(a=>a.name.IndexOf("Idle",StringComparison.OrdinalIgnoreCase)>=0);if(idle!=null)idle.SampleAnimation(actor,0);
                    var ct=Group("PortraitCamera",root.transform,new Vector3(0,1.56f,2));ct.LookAt(new Vector3(0,1.5f,0));var camera=ct.gameObject.AddComponent<Camera>();camera.scene=scene;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);camera.fieldOfView=23;camera.nearClipPlane=.05f;camera.farClipPlane=8;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=RGB("6F7970");
                    var key=Group("PortraitLight",root.transform);key.rotation=Quaternion.Euler(25,160,0);var light=key.gameObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.shadows=LightShadows.None;
                    foreach(var t in All<Transform>(scene))t.gameObject.layer=30;light.cullingMask=camera.cullingMask=1<<30;
                    var rt=new RenderTexture(192,192,24);var previous=RenderTexture.active;camera.targetTexture=rt;
                    string path=Content+"/Portraits/"+c.firstName+"_"+c.lastName+".png";
                    try{camera.Render();RenderTexture.active=rt;var texture=new Texture2D(192,192,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,192,192),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());Destroy(texture);}
                    finally{camera.targetTexture=null;RenderTexture.active=previous;rt.Release();Destroy(rt);}
                    AssetDatabase.ImportAsset(path);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.GUI;importer.filterMode=FilterMode.Point;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.npotScale=TextureImporterNPOTScale.None;importer.SaveAndReimport();c.portrait=AssetDatabase.LoadAssetAtPath<Texture2D>(path);EditorUtility.SetDirty(c);
                }
                finally{EditorSceneManager.ClosePreviewScene(scene);}
            }
            AssetDatabase.SaveAssets();
        }
        [MenuItem("Motel/Check-In/Validate")]
        public static void ValidateCheckIn()
        {
            Directory.CreateDirectory(CheckInReports);var s=Scene();var result=new List<string>();void Check(bool ok,string line){result.Add((ok?"PASS ":"FAIL ")+line);}
            var reg=All<RegistrationDesk>(s).Single();Check(reg.ConfigurationValid(),"Six cases / three reports / workstations / identity data");
            foreach(var c in reg.Cases)Check(c.Valid(out var reason),c.name+" "+reason);
            var journeys=All<GuestVehicleJourney>(s);Check(journeys.Length==7,"Seven reusable NPC vehicle/parking slots");
            Check(journeys.Select(j=>j.slot.number).Distinct().Count()==7,"Unique numbered parking slots");
            Physics.SyncTransforms();
            foreach(var journey in journeys)
            {
                Check(journey.ConfigurationValid(),journey.name+" configured journey");var path=new NavMeshPath();Check(NavMesh.CalculatePath(journey.slot.driverPoint.position,reg.Reception.QueuePoints[0].position,1,path)&&path.status==NavMeshPathStatus.PathComplete,journey.slot.Code+" driver -> reception route");
                foreach(var other in journeys)if(other!=journey){other.vehicle.transform.SetPositionAndRotation(other.slot.transform.position,Quaternion.identity);other.vehicle.Show(true);}
                Physics.SyncTransforms();
                foreach(var route in new[]{journey.slot.arrival,journey.slot.reverse,journey.slot.departure})
                {
                    bool reversing=route==journey.slot.reverse;var errors=new HashSet<string>();var body=journey.vehicle.GetComponent<BoxCollider>();
                    for(float d=0;d<=ParkingSlot.Length(route);d+=.2f)
                    {
                        ParkingSlot.Sample(route,d,reversing,out var p,out var q);
                        foreach(var collider in Physics.OverlapBox(p+q*body.center,body.size*.5f-Vector3.one*.015f,q,~0,QueryTriggerInteraction.Ignore))
                            if(!collider.transform.IsChildOf(journey.vehicle.transform)&&collider.GetComponentInParent<GuestLifecycle>()==null)errors.Add(collider.name);
                    }
                    Check(errors.Count==0,journey.slot.Code+" "+(reversing?"reverse":route==journey.slot.arrival?"arrival":"departure")+" collision sweep (other bays occupied): "+string.Join(",",errors));
                }
            }
            foreach(var j in journeys)j.vehicle.Show(false);
            Check(All<Transform>(s).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"No missing scripts");
            File.WriteAllLines(CheckInReports+"/scene-validation.txt",result);if(result.Any(l=>l.StartsWith("FAIL")))throw new Exception("Check-in validation failed; see "+CheckInReports+"/scene-validation.txt");Validate();
        }
        public static void BuildCheckIn()
        {ValidateCheckIn();MotelLayoutV4.BuildMacTo("Builds/Validation");File.Copy("Documentation/LevelM16/candidate-build.txt",CheckInReports+"/candidate-build.txt",true);}
        public static void PrepareAndBuildCheckIn(){PrepareCheckIn();BuildCheckIn();}
    }
}
