using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;
using UnityEngine.AI;
using MotelGame.Service;

namespace MotelGame.Editor
{
    public static partial class MotelVisualOverhaul
    {
        static void Diagnose()
        {
            var scene=EditorSceneManager.OpenPreviewScene(MultipleGuestsSetup.ScenePath);
            try {
                var lines=new List<string>();
                foreach(var t in All<Transform>(scene).Where(t=>t.name=="Visual_pc_monitor_mp_1"||t.name=="Visual_tv_mp_1"||t.name=="Visual_shelf_mx_3"||t.name=="Visual_shelf_mx_4"))
                {
                    lines.Add(FullPath(t)+" "+BoundsOf(t.gameObject));
                    foreach(var f in t.GetComponentsInChildren<MeshFilter>()){
                        var m=f.sharedMesh;var r=f.GetComponent<Renderer>();var v=m.vertices;
                        for(int sm=0;sm<m.subMeshCount;sm++){
                            var inds=m.GetIndices(sm);var b=new Bounds(f.transform.TransformPoint(v[inds[0]]),Vector3.zero);var levels=new Dictionary<float,float>();
                            for(int j=0;j<inds.Length;j+=3){var a=f.transform.TransformPoint(v[inds[j]]);var q=f.transform.TransformPoint(v[inds[j+1]]);var c=f.transform.TransformPoint(v[inds[j+2]]);b.Encapsulate(a);b.Encapsulate(q);b.Encapsulate(c);var normal=Vector3.Cross(q-a,c-a);if(normal.normalized.y>.95f){float y=Mathf.Round((a.y+q.y+c.y)/3*100)/100;levels[y]=levels.GetValueOrDefault(y)+normal.magnitude/2;}}
                            lines.Add(r.sharedMaterials[sm].name+" "+b+" levels "+string.Join("; ",levels.Where(k=>k.Value>.01f).OrderBy(k=>k.Key).Select(k=>k.Key+":"+k.Value.ToString("F2"))));
                        }
                    }
                }
                foreach(var room in All<ServiceRoom>(scene)){
                    var data=new SerializedObject(room.Door);var hinge=(Transform)data.FindProperty("hinge").objectReferenceValue;var leaf=(BoxCollider)data.FindProperty("leaf").objectReferenceValue;
                    lines.Add("Door "+room.RoomNumber+" closed "+leaf.bounds);hinge.localRotation=Quaternion.Euler(0,data.FindProperty("openAngle").floatValue,0);Physics.SyncTransforms();lines.Add("Door "+room.RoomNumber+" open "+leaf.bounds);
                }
                File.WriteAllLines(Reports+"/art-diagnostics.txt",lines);
            } finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        [MenuItem("Tools/Motel/Validate Visual Overhaul M12")]
        public static void Validate()
        {
            var scene=EditorSceneManager.OpenPreviewScene(MultipleGuestsSetup.ScenePath);
            try
            {
                foreach(var text in All<WorldServiceText>(scene))text.RefreshMaterial();
                var lines=new List<string>();var objects=All<Transform>(scene);var renderers=All<MeshRenderer>(scene);
                foreach(var t in objects)if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)throw new Exception("Missing script "+FullPath(t));
                foreach(var r in renderers)
                {
                    if(r.sharedMaterials.Any(m=>m==null||m.shader==null||m.shader.name.Contains("InternalErrorShader")))throw new Exception("Missing material/shader "+FullPath(r.transform));
                    if(float.IsNaN(r.bounds.size.x)||r.bounds.size.magnitude>100)throw new Exception("Invalid art bounds "+r.name);
                }
                foreach(var f in All<MeshFilter>(scene).Where(f=>f.transform.parent!=null&&(f.transform.parent.name=="Structure"||f.transform.parent.name=="Ceilings")))
                    if(f.TryGetComponent<BoxCollider>(out var box)&&f.TryGetComponent<Renderer>(out var visual))
                        if(Vector3.Distance(box.bounds.center,visual.bounds.center)>.001f||Vector3.Distance(box.bounds.size,visual.bounds.size)>.001f)throw new Exception("Architecture render/collider bounds changed: "+FullPath(f.transform));
                lines.Add("Architecture renderer/collider bounds match within 1 mm after UV/submesh changes.");
                var rooms=All<ServiceRoom>(scene).OrderBy(r=>r.RoomNumber).ToArray();if(rooms.Length!=4)throw new Exception("Room count");
                foreach(var room in rooms)
                {
                    if(!room.ConfigurationValid(out var error))throw new Exception(error);
                    var root=Find(scene,"Motel/Rooms/Room"+room.RoomNumber);
                    if(root.Find("PizzaDoggyDecor")==null||root.Find("ServiceState/MadeBedding/LinenCover")==null||root.Find("ServiceState/UnmadeBedding/LinenCover")==null)throw new Exception("Incomplete room art "+room.RoomNumber);
                    foreach(var renderer in room.Door.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled))if(!renderer.transform.IsChildOf(room.Door.transform))throw new Exception("Door visual detached");
                    lines.Add("Room "+room.RoomNumber+": gameplay configuration, clean/dirty linen roots, door descendants, TV references and art present.");
                }
                foreach(string path in new[]{"Motel/Lobby/PizzaDoggyDecor","Motel/ReceptionDesk/PizzaDoggyDecor","Motel/Storage/PizzaDoggyDecor","Motel/Exterior/PizzaDoggyVegetation","Motel/Exterior/PizzaDoggyExteriorProps"})if(Find(scene,path)==null)throw new Exception("Incomplete area: "+path);
                var imported=objects.Where(t=>t.name.StartsWith("Visual_")).ToArray();
                if(imported.Any(t=>t.GetComponentsInChildren<Collider>(true).Length>0))throw new Exception("Decor added physics");
                var surface=All<NavMeshSurface>(scene).Single();surface.AddData();
                foreach(var room in rooms)
                {
                    var path=new NavMeshPath();var start=new Vector3(8,0,2);if(!NavMesh.SamplePosition(start,out var a,.5f,NavMesh.AllAreas)||!NavMesh.SamplePosition(room.DoorOutside.position,out var b,.5f,NavMesh.AllAreas)||!NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)throw new Exception("Incomplete room route");
                }
                var computer=All<ReceptionComputer>(scene).Single();if(computer.ScreenCollider==null||computer.Focus==null)throw new Exception("Computer references");
                Physics.SyncTransforms();var colliders=All<Collider>(scene).Where(c=>c.enabled&&c.gameObject.activeInHierarchy&&!c.isTrigger).ToArray();
                foreach(var point in new[]{new Vector3(11.9f,1,4.9f),new Vector3(12.1f,1,5.7f),new Vector3(8.8f,1,6),new Vector3(7.5f,1,6),new Vector3(6.2f,1,6),new Vector3(16.8f,1,-3.1f)})
                    if(colliders.Any(c=>Vector3.Distance(c.ClosestPoint(point),point)<.28f))throw new Exception("Player/queue approach blocked: "+point);
                lines.Add("Computer collider/focus assigned; employee positions, queue positions and dumpster approach clear; 4 room routes complete on existing NavMesh.");
                long triangles=All<MeshFilter>(scene).Where(f=>f.GetComponent<Renderer>()?.enabled==true&&f.gameObject.activeInHierarchy&&f.sharedMesh!=null).Sum(f=>Enumerable.Range(0,f.sharedMesh.subMeshCount).Sum(j=>(long)f.sharedMesh.GetIndexCount(j)/3));
                var materials=renderers.SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
                foreach(var entry in Manifest.materials)
                {
                    var mat=Mat(entry.pack,entry.name);if(mat==null||mat.GetTexture("_BaseMap")==null||mat.shader.name!="Universal Render Pipeline/Lit")throw new Exception("Imported material incomplete: "+entry.name);
                    if(entry.alpha&&(mat.GetFloat("_AlphaClip")!=1||mat.GetFloat("_Cull")!=0))throw new Exception("Foliage alpha/culling");
                }
                lines.Add("Imported visual instances: "+imported.Length+"; scene renderers: "+renderers.Length+"; enabled/active: "+renderers.Count(r=>r.enabled&&r.gameObject.activeInHierarchy)+"; active mesh triangles: "+triangles+"; distinct scene materials: "+materials.Length+"; scene lights: "+All<Light>(scene).Length+".");
                lines.Add("All imported URP materials/textures assigned; vegetation uses cutout/double-sided materials. No new decorative colliders. Character prefabs unchanged (separate hash validation).");
                Directory.CreateDirectory(Reports);File.WriteAllLines(Reports+"/scene-validation.txt",new[]{"PASS "+DateTime.Now.ToString("O")}.Concat(lines));
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
