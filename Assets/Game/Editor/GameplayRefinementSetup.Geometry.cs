using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using UnityEngine.Rendering;
using MotelGame.Service;
using MotelGame.Horror;
namespace MotelGame.Editor
{
    public static partial class GameplayRefinementSetup
    {
        const string MeshFolder="Assets/Game/Geometry/StraightVentEntrances";
        static Material duct;
        static Transform shell;
        static int meshIndex;
        const float Slope=35;
        static float RampEnd=>6.3f+3.1f/Mathf.Tan(Slope*Mathf.Deg2Rad);
        static Transform Group(string name, Transform parent, Vector3 p = default)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = p;
            return t;
        }

        static GameObject Box(string name, Transform parent, Vector3 p, Vector3 size, Material mat, uint layer = 2)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = p;
            g.transform.localScale = size;
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.renderingLayerMask = layer;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return g;
        }

        static void Delete(UnityEngine.Object o) => UnityEngine.Object.DestroyImmediate(o);
        // Exact vertical end faces make adjacent floor slopes meet without stair lips or gaps.
        static void Prism(string name, Transform parent, float x0, float x1, float z0, float z1, float low0, float low1, float high0, float high1, Material mat, uint layer = 2, bool solid = true)
        {
            var v = new[]{new Vector3(x0, low0, z0), new Vector3(x1, low0, z0), new Vector3(x1, low1, z1), new Vector3(x0, low1, z1), new Vector3(x0, high0, z0), new Vector3(x1, high0, z0), new Vector3(x1, high1, z1), new Vector3(x0, high1, z1)};
            int[][] faces = {new[]{0, 3, 2, 1}, new[]{4, 5, 6, 7}, new[]{0, 1, 5, 4}, new[]{3, 7, 6, 2}, new[]{0, 4, 7, 3}, new[]{1, 2, 6, 5}};
            // Reverse the indexed quads below for outward normals on the closed prism.
            var verts = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            foreach (var face in faces)
            {
                int start = verts.Count;
                foreach (int i in face)
                    verts.Add(v[i]);
                float w = Vector3.Distance(v[face[0]], v[face[1]]), h = Vector3.Distance(v[face[1]], v[face[2]]);
                uv.AddRange(new[]{Vector2.zero, new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h)});
                tri.AddRange(new[]{start, start + 2, start + 1, start, start + 3, start + 2});
            }

            var mesh = new Mesh{name = name};
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            string asset = MeshFolder + "/" + "Segment_" + (++meshIndex).ToString("D4") + "_" + name + ".asset";
            AssetDatabase.CreateAsset(mesh, asset);
            var g = Group(name, parent).gameObject;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            if(solid)g.AddComponent<MeshCollider>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.renderingLayerMask = layer;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        static void Flight(Transform root, string prefix, float x, float[] z, float[] y, float[] ceiling)
        {
            for (int i = 0; i < z.Length - 1; i++)
            {
                Prism(prefix + "Floor" + i, root, x - .55f, x + .55f, z[i], z[i + 1], y[i] - .12f, y[i + 1] - .12f, y[i], y[i + 1], duct);
                Prism(prefix + "Roof" + i, root, x - .67f, x + .67f, z[i], z[i + 1], ceiling[i], ceiling[i + 1], ceiling[i] + .12f, ceiling[i + 1] + .12f, duct, 3);
                foreach (int side in new[]{-1, 1})
                {
                    float edge = x + side * .61f;
                    Prism(prefix + "Side" + side + "_" + i, root, edge - .06f, edge + .06f, z[i], z[i + 1], y[i] - .12f, y[i + 1] - .12f, ceiling[i] + .12f, ceiling[i + 1] + .12f, duct, 3);
                }
            }
        }

        static Rect WorldRect(Transform t, Rect r)
        {
            var a = t.TransformPoint(new Vector3(r.xMin, 0, r.yMin));
            var b = t.TransformPoint(new Vector3(r.xMax, 0, r.yMax));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
        }

        static Rect LocalRect(Transform t, Bounds b)
        {
            var a = t.InverseTransformPoint(b.min);
            var c = t.InverseTransformPoint(b.max);
            return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.z, c.z), Mathf.Max(a.x, c.x), Mathf.Max(a.z, c.z));
        }

        // Subtract a box only from the existing panel it intersects; retain every unrelated panel.
        static void CutBox(GameObject g, Bounds cut)
        {
            var b = g.GetComponent<Renderer>().bounds;
            bool solid=g.TryGetComponent<Collider>(out var collider)&&collider.enabled;
            var lo = Vector3.Max(b.min, cut.min);
            var hi = Vector3.Min(b.max, cut.max);
            if (hi.x - lo.x < .0005f || hi.y - lo.y < .0005f || hi.z - lo.z < .0005f)
                return;
            var r = g.GetComponent<Renderer>();
            var mat = r.sharedMaterial;
            uint layer = r.renderingLayerMask;
            var parent = g.transform.parent;
            string name = g.name;
            void Piece(Vector3 a, Vector3 c)
            {
                var size = c - a;
                if (size.x < .0005f || size.y < .0005f || size.z < .0005f)
                    return;
                var n = Box(name + "_Cut", parent, Vector3.zero, size, mat, layer);
                n.transform.position = (a + c) * .5f;
                n.transform.rotation = Quaternion.identity;
                if(!solid)Delete(n.GetComponent<Collider>());
            }

            Piece(b.min, new Vector3(lo.x, b.max.y, b.max.z));
            Piece(new Vector3(hi.x, b.min.y, b.min.z), b.max);
            Piece(new Vector3(lo.x, b.min.y, b.min.z), new Vector3(hi.x, lo.y, b.max.z));
            Piece(new Vector3(lo.x, hi.y, b.min.z), new Vector3(hi.x, b.max.y, b.max.z));
            Piece(new Vector3(lo.x, lo.y, b.min.z), new Vector3(hi.x, hi.y, lo.z));
            Piece(new Vector3(lo.x, lo.y, hi.z), new Vector3(hi.x, hi.y, b.max.z));
            Delete(g);
        }


        static void RestoreMainWalls()
        {
            // Restore the old U-return ports only. Keep matching original panels and every floor/roof.
            var floors=shell.GetComponentsInChildren<Renderer>().Where(r=>r.name=="Floor").Select(r=>r.bounds).ToArray();
            var rects=floors.Select(b=>Rect.MinMaxRect(b.min.x,b.min.z,b.max.x,b.max.z)).ToArray();
            var xs=rects.SelectMany(r=>new[]{r.xMin,r.xMax}).Select(x=>Mathf.Round(x*10000)/10000).Distinct().OrderBy(x=>x).ToArray();
            var zs=rects.SelectMany(r=>new[]{r.yMin,r.yMax}).Select(x=>Mathf.Round(x*10000)/10000).Distinct().OrderBy(x=>x).ToArray();
            bool Inside(float x,float z)=>rects.Any(r=>r.Contains(new Vector2(x,z)));
            var old=shell.GetComponentsInChildren<Renderer>().Where(r=>r.name.StartsWith("Wall")).ToList();
            void Wall(string name,Vector3 p,Vector3 size)
            {
                var existing=old.FirstOrDefault(r=>Vector3.Distance(r.bounds.center,p)<.002f&&Vector3.Distance(r.bounds.size,size)<.002f);
                if(existing!=null){old.Remove(existing);return;}
                Box(name,shell,p,size,duct);
            }
            for(int i=0;i<xs.Length-1;i++)for(int j=0;j<zs.Length-1;j++)
            {
                float x=(xs[i]+xs[i+1])*.5f,z=(zs[j]+zs[j+1])*.5f,dx=xs[i+1]-xs[i],dz=zs[j+1]-zs[j];
                if(dx<.001f||dz<.001f||!Inside(x,z))continue;
                if(!Inside(xs[i]-.001f,z))Wall("WallW",new Vector3(xs[i]-.06f,-2.45f,z),new Vector3(.12f,1.54f,dz+.12f));
                if(!Inside(xs[i+1]+.001f,z))Wall("WallE",new Vector3(xs[i+1]+.06f,-2.45f,z),new Vector3(.12f,1.54f,dz+.12f));
                if(!Inside(x,zs[j]-.001f))Wall("WallS",new Vector3(x,-2.45f,zs[j]-.06f),new Vector3(dx+.12f,1.54f,.12f));
                if(!Inside(x,zs[j+1]+.001f))Wall("WallN",new Vector3(x,-2.45f,zs[j+1]+.06f),new Vector3(dx+.12f,1.54f,.12f));
            }
            foreach(var r in old)Delete(r.gameObject);
        }
        static void ShiftOpening(VentHatch h,float x)
        {
            if(Mathf.Abs(x-2.25f)<.01f)return;
            var roomRoot=h.room.transform.parent.parent;
            var wall=GameObject.Find("Motel_Layout_V4/01_Architecture/Room"+h.room.RoomNumber+"_CourtyardWall").transform;
            var paint=wall.GetComponentsInChildren<Renderer>().First(r=>r.name.StartsWith("Wall")).sharedMaterial;
            Box("FormerVentOpeningPatch",roomRoot,new Vector3(2.25f,.525f,6.1f),new Vector3(1.1f,1.05f,.2f),paint,1);
            var rect=WorldRect(h.transform,Rect.MinMaxRect(x-.55f,6,x+.55f,6.2f));
            var cut=new Bounds(new Vector3(rect.center.x,.525f,rect.center.y),new Vector3(rect.width,1.05f,.24f));
            foreach(var r in wall.GetComponentsInChildren<Renderer>().Concat(roomRoot.Find("VentWindowSill").GetComponentsInChildren<Renderer>()).ToArray())
                if(r.name.StartsWith("Wall")||r.name=="Lintel"||r.name=="Sill"||r.name=="Baseboard")CutBox(r.gameObject,cut);
        }
        static void Entrance(VentHatch h)
        {
            float x=h.room.RoomNumber=="103"||h.room.RoomNumber=="106"?4.9f:2.25f;
            ShiftOpening(h,x);
            foreach(Transform t in h.transform.Cast<Transform>().ToArray())
            {
                if(t.name.StartsWith("Grille")){t.localPosition+=Vector3.right*(x-2.25f);continue;}
                Delete(t.gameObject);
            }
            h.leaf.isTrigger=true;
            // Grille swings inward above the crouched player; the descending roof needs no large recess.
            float end=RampEnd;
            Flight(h.transform,"StraightRamp",x,new[]{6.1f,6.3f,6.45f,end,11f},new[]{0f,0f,-.15f*Mathf.Tan(Slope*Mathf.Deg2Rad),-3.1f,-3.1f},new[]{1.15f,1.15f,1.2f-.15f*Mathf.Tan(Slope*Mathf.Deg2Rad),-1.8f,-1.8f});
            var connection=WorldRect(h.transform,Rect.MinMaxRect(x-.55f,end,x+.55f,11.7f));
            var cut=new Bounds(new Vector3(connection.center.x,-2.45f,connection.center.y),new Vector3(connection.width,1.7f,connection.height));
            foreach(var r in shell.GetComponentsInChildren<Renderer>().Where(r=>r.name.StartsWith("Wall")).ToArray())CutBox(r.gameObject,cut);
            h.upper=Group("RoomApproach",h.transform,new Vector3(x>4?5.05f:2.1f,.025f,x>4?5.55f:5.2f));
            h.lower=Group("TunnelConnection",h.transform,new Vector3(x,-3.075f,11.7f));
            var points=new List<Vector3>{h.upper.localPosition,new Vector3(x,.025f,6.1f),new Vector3(x,.08f,6.3f)};
            for(int i=1;i<=4;i++){float t=i/4f;points.Add(new Vector3(x,-3.1f*t+.08f,Mathf.Lerp(6.3f,end,t)));}
            points.Add(h.lower.localPosition);
            h.route=points.Select((p,i)=>Group("Route_"+i,h.transform,p)).ToArray();
            var node=All<VentNode>(SceneManager.GetActiveScene()).Single(n=>n.underground&&n.room==h.room);
            node.landing=h.upper;node.mouth=Group("GrilleMouth",h.transform,new Vector3(x,.5f,6.1f));
            var obstacle=Group("ServiceHousingNavObstacle",h.transform,new Vector3(x,.45f,7.25f)).gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape=NavMeshObstacleShape.Box;obstacle.size=new Vector3(1.34f,2,2.1f);obstacle.carving=true;obstacle.carveOnlyStationary=true;
            EditorUtility.SetDirty(h);EditorUtility.SetDirty(node);
        }
        static void CarveGround(GameObject original,VentHatch[] hatches)
        {
            var parent=Group(original.name+"_RampOpenings",original.transform.parent);
            // All identified ground slabs are axis-aligned cube meshes. Keep the source disabled for rollback.
            var source=original.GetComponent<Renderer>();var bounds=source.bounds;var material=source.sharedMaterial;uint layer=source.renderingLayerMask;
            bool solid=original.TryGetComponent<Collider>(out var collider)&&collider.enabled;
            var first=Box("Ground",parent,bounds.center,bounds.size,material,layer);if(!solid)Delete(first.GetComponent<Collider>());
            original.SetActive(false);
            foreach(var h in hatches)
            {
                float x=h.hinge.localPosition.x;
                var z=new[]{6.1f,6.3f,6.45f,RampEnd};
                var floor=new[]{0f,0f,-.15f*Mathf.Tan(Slope*Mathf.Deg2Rad),-3.1f};
                var roof=new[]{1.15f,1.15f,1.2f-.15f*Mathf.Tan(Slope*Mathf.Deg2Rad),-1.8f};
                for(int k=0;k<z.Length-1;k++)
                {
                    var rect=WorldRect(h.transform,Rect.MinMaxRect(x-.67f,z[k],x+.67f,z[k+1]));
                    bool reversed=h.transform.forward.z<0;
                    float bottom0=(reversed?floor[k+1]:floor[k])-.12f,bottom1=(reversed?floor[k]:floor[k+1])-.12f;
                    float top0=(reversed?roof[k+1]:roof[k])+.12f,top1=(reversed?roof[k]:roof[k+1])+.12f;
                    foreach(var r in parent.GetComponentsInChildren<Renderer>().ToArray())
                    {
                        var b=r.bounds;
                        float x0=Mathf.Max(rect.xMin,b.min.x),x1=Mathf.Min(rect.xMax,b.max.x),z0=Mathf.Max(rect.yMin,b.min.z),z1=Mathf.Min(rect.yMax,b.max.z);
                        if(x1-x0<.0001f||z1-z0<.0001f||Mathf.Max(top0,top1)<=b.min.y||Mathf.Min(bottom0,bottom1)>=b.max.y)continue;
                        float Low(float zz)=>Mathf.Lerp(bottom0,bottom1,(zz-rect.yMin)/rect.height);
                        float High(float zz)=>Mathf.Lerp(top0,top1,(zz-rect.yMin)/rect.height);
                        void Piece(float a,float c,float d,float e,float l0,float l1,float u0,float u1)
                        {
                            if(c-a<.0001f||e-d<.0001f||Mathf.Max(u0-l0,u1-l1)<.0001f)return;
                            if(Mathf.Abs(l1-l0)<.00001f&&Mathf.Abs(u1-u0)<.00001f)
                            {var g=Box("Ground",parent,new Vector3((a+c)*.5f,(l0+u0)*.5f,(d+e)*.5f),new Vector3(c-a,u0-l0,e-d),material,layer);if(!solid)Delete(g.GetComponent<Collider>());}
                            else Prism("GroundSlope",parent,a,c,d,e,l0,l1,u0,u1,material,layer,solid);
                        }
                        Piece(b.min.x,x0,b.min.z,b.max.z,b.min.y,b.min.y,b.max.y,b.max.y);
                        Piece(x1,b.max.x,b.min.z,b.max.z,b.min.y,b.min.y,b.max.y,b.max.y);
                        Piece(x0,x1,b.min.z,z0,b.min.y,b.min.y,b.max.y,b.max.y);
                        Piece(x0,x1,z1,b.max.z,b.min.y,b.min.y,b.max.y,b.max.y);
                        var split=new List<float>{z0,z1};
                        foreach(float y in new[]{b.min.y,b.max.y})foreach(var pair in new[]{(bottom0,bottom1),(top0,top1)})
                        {if(Mathf.Abs(pair.Item2-pair.Item1)<.00001f)continue;float zz=rect.yMin+(y-pair.Item1)/(pair.Item2-pair.Item1)*rect.height;if(zz>z0&&zz<z1)split.Add(zz);}
                        split=split.Distinct().OrderBy(v=>v).ToList();
                        for(int j=0;j<split.Count-1;j++)
                        {
                            float a=split[j],c=split[j+1];
                            Piece(x0,x1,a,c,b.min.y,b.min.y,Mathf.Clamp(Low(a),b.min.y,b.max.y),Mathf.Clamp(Low(c),b.min.y,b.max.y));
                            Piece(x0,x1,a,c,Mathf.Clamp(High(a),b.min.y,b.max.y),Mathf.Clamp(High(c),b.min.y,b.max.y),b.max.y,b.max.y);
                        }
                        Delete(r.gameObject);
                    }
                }
            }
        }
        static void Install()
        {
            var scene=SceneManager.GetActiveScene();if(scene.path!=ScenePath||scene.isDirty)throw new Exception("Open and save V4 before migration.");
            var hatches=All<VentHatch>(scene);if(hatches.Length!=6||hatches.Any(h=>h.transform.Find("StraightRampFloor0")!=null))throw new Exception("Expected six previous U entrances. Migration is one-shot.");
            Directory.CreateDirectory(MeshFolder);AssetDatabase.Refresh();meshIndex=0;
            duct=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/Underground/DuctSteel.mat");
            shell=GameObject.Find("Motel_Layout_V4/05_Underground/TunnelShell").transform;
            RestoreMainWalls();foreach(var h in hatches)Entrance(h);
            var courtyard=GameObject.Find("Motel_Layout_V4/01_Architecture/Courtyard").transform;
            foreach(Transform child in courtyard.Cast<Transform>().Where(t=>t.name=="FloorAroundServiceHousing").ToArray())Delete(child.gameObject);
            var floor=courtyard.Find("Floor").gameObject;floor.SetActive(true);Physics.SyncTransforms();
            CarveGround(floor,hatches);
            CarveGround(GameObject.Find("Motel_Layout_V4/02_Exterior/ForestBeyondProperty/ForestGround_Backdrop"),hatches);
            var hunt=All<MimicHunt>(scene).Single();var so=new SerializedObject(hunt);so.FindProperty("ductSpeed").floatValue=.75f;so.FindProperty("retreatSpeed").floatValue=.9f;
            so.FindProperty("activityAlert").objectReferenceValue=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/UndergroundActivity.wav");so.ApplyModifiedPropertiesWithoutUndo();
            Physics.SyncTransforms();foreach(var no in All<NetworkObject>(scene))typeof(NetworkObject).GetMethod("OnValidate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(no,null);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            File.WriteAllText(Reports+"/installation.txt","Six straight 35 degree ramps; 3.1m drop; 1.1m clear width; run "+(RampEnd-6.3f)+"m. Ground cut against complete outer ramp prisms. 103/106 grille centers shifted to room-local x=4.9; furniture untouched.");
        }
    }
}
