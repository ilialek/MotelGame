using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
namespace MotelGame.Editor
{
    public static partial class GameplayRefinementSetup
    {
        static void GroundUV()
        {
            var scene=SceneManager.GetActiveScene();if(scene.path!=ScenePath||scene.isDirty)throw new Exception("Open saved V4");
            int count=0;
            foreach(string path in new[]{"Motel_Layout_V4/01_Architecture/Courtyard/Floor","Motel_Layout_V4/02_Exterior/ForestBeyondProperty/ForestGround_Backdrop"})
            {
                var original=All<MeshFilter>(scene).Single(f=>PathOf(f.transform)==path);
                var source=original.sharedMesh;var vertices=source.vertices;var uv=source.uv;var normals=source.normals;var triangles=source.triangles;
                var pieces=original.transform.parent.Find(original.name+"_RampOpenings");
                foreach(var filter in pieces.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);var target=mesh.vertices;var targetNormals=mesh.normals;var mapped=new Vector2[target.Length];
                    for(int i=0;i<target.Length;i++)
                    {
                        Vector3 p=original.transform.InverseTransformPoint(filter.transform.TransformPoint(target[i]));
                        Vector3 normal=original.transform.InverseTransformDirection(filter.transform.TransformDirection(targetNormals[i]));
                        int selected=0;float best=float.NegativeInfinity;
                        for(int t=0;t<triangles.Length;t+=3){float score=Vector3.Dot(normal,normals[triangles[t]]);if(score>best){best=score;selected=t;}}
                        int a=triangles[selected],b=triangles[selected+1],c=triangles[selected+2];
                        var e0=vertices[b]-vertices[a];var e1=vertices[c]-vertices[a];var d=p-vertices[a];
                        float aa=Vector3.Dot(e0,e0),bb=Vector3.Dot(e0,e1),cc=Vector3.Dot(e1,e1),ad=Vector3.Dot(d,e0),bd=Vector3.Dot(d,e1),det=aa*cc-bb*bb;
                        mapped[i]=uv[a]+(uv[b]-uv[a])*(ad*cc-bd*bb)/det+(uv[c]-uv[a])*(bd*aa-ad*bb)/det;
                    }
                    mesh.uv=mapped;mesh.RecalculateTangents();mesh.name="GroundUV_"+(++count).ToString("D4");
                    string asset=MeshFolder+"/"+mesh.name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(asset);
                    if(existing==null){AssetDatabase.CreateAsset(mesh,asset);existing=mesh;}else{EditorUtility.CopySerialized(mesh,existing);Delete(mesh);EditorUtility.SetDirty(existing);}
                    filter.sharedMesh=existing;if(filter.TryGetComponent<MeshCollider>(out var collider))collider.sharedMesh=existing;
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            File.WriteAllText(Reports+"/ground-uv-preservation.txt",count+" ground pieces retain the original slab UV projection. Geometry/collision unchanged.");
        }
    }
}
