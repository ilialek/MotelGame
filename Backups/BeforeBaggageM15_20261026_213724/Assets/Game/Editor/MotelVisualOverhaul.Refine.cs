using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using MotelGame.Service;

namespace MotelGame.Editor
{
    public static partial class MotelVisualOverhaul
    {
        static void FitExisting(GameObject obj,Vector3 center,Vector3 size)
        {
            var b=BoundsOf(obj);var s=obj.transform.localScale;
            bool swapped=Mathf.Abs(Mathf.Sin(obj.transform.eulerAngles.y*Mathf.Deg2Rad))>.9f;
            var ratio=new Vector3(size.x/b.size.x,size.y/b.size.y,size.z/b.size.z);
            obj.transform.localScale=Vector3.Scale(s,swapped?new Vector3(ratio.z,ratio.y,ratio.x):ratio);
            obj.transform.position+=center-BoundsOf(obj).center;
        }
        static Bounds ScreenBounds(Transform model)
        {
            Bounds result=default;bool found=false;
            foreach(var f in model.GetComponentsInChildren<MeshFilter>()){
                var m=f.sharedMesh;var v=m.vertices;var mats=f.GetComponent<Renderer>().sharedMaterials;
                for(int j=0;j<m.subMeshCount;j++)if(mats[j].name.StartsWith("crt_screen"))foreach(int index in m.GetIndices(j))
                {var p=f.transform.TransformPoint(v[index]);if(!found){result=new Bounds(p,Vector3.zero);found=true;}else result.Encapsulate(p);}
            }
            if(!found)throw new Exception("Missing CRT screen mesh");return result;
        }
        static void AlignScreen(Transform model,Transform target,bool facesX)
        {
            var source=ScreenBounds(model);var b=target.GetComponent<Renderer>().bounds;var scale=model.localScale;
            // Both CRT models face local +Z. Fit only the two screen axes, preserving their depth.
            scale.x*=((facesX?b.size.z:b.size.x)*1.04f)/(facesX?source.size.z:source.size.x);scale.y*=b.size.y*1.04f/source.size.y;model.localScale=scale;
            source=ScreenBounds(model);var desired=b.center;var delta=desired-source.center;
            if(facesX)delta.x=b.max.x-.002f-source.max.x;else delta.z=b.min.z+.002f-source.min.z;
            model.position+=delta;
        }
        static float ShelfHeight(float y)
        {
            float[] from={0,.23f,.68f,1.14f,1.59f,2.05f,2.06f};float[] to={0,.2f,.8f,1.4f,2.0f,2.35f,2.36f};
            for(int i=1;i<from.Length;i++)if(y<=from[i])return Mathf.LerpUnclamped(to[i-1],to[i],(y-from[i-1])/(from[i]-from[i-1]));return y+.3f;
        }
        static void RefineShelves(Scene scene)
        {
            var decor=Find(scene,"Motel/Storage/PizzaDoggyDecor");if(decor==null)return;
            foreach(var model in decor.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Visual_shelf_mx_")))
            foreach(var f in model.GetComponentsInChildren<MeshFilter>())
            {
                if(f.sharedMesh.name.EndsWith("_AlignedLedges"))continue;
                var mesh=UnityEngine.Object.Instantiate(f.sharedMesh);var vertices=mesh.vertices;
                for(int j=0;j<vertices.Length;j++){var world=f.transform.TransformPoint(vertices[j]);world.y=ShelfHeight(world.y);vertices[j]=f.transform.InverseTransformPoint(world);}
                mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.name+="_AlignedLedges";
                string path=Generated+"/Meshes/Shelf_"+model.position.x.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+"_"+f.name+".asset";
                if(AssetDatabase.LoadAssetAtPath<Mesh>(path)==null)AssetDatabase.CreateAsset(mesh,path);else UnityEngine.Object.DestroyImmediate(mesh);f.sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            }
            foreach(var t in Find(scene,"Motel/Storage/Boxes").GetComponentsInChildren<Transform>().Where(t=>t.name=="Visual_cardboard_box_1"))t.position+=Vector3.up*(.95f-BoundsOf(t.gameObject).center.y);
            foreach(var t in decor.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Visual_")))
            {float y=t.name=="Visual_first_aid_kit_hr_1"?2.49f:t.name=="Visual_paint_can_mx_1"?1.54f:t.name=="Visual_bucket_mx_1"?.41f:t.name=="Visual_electrical_tape_etx_1"?1.44f:-1;if(y>=0)t.position+=Vector3.up*(y-BoundsOf(t.gameObject).center.y);}
        }
        static void BedCover(Transform parent,int index,bool rumpled)
        {
            if(parent.Find("LinenCover")!=null)UnityEngine.Object.DestroyImmediate(parent.Find("LinenCover").gameObject);
            var obj=new GameObject("LinenCover");obj.transform.SetParent(parent,false);
            obj.transform.position=RoomPoint(index,17,0,13.5f);obj.transform.rotation=Quaternion.identity;
            const int n=16;var v=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();
            for(int z=0;z<=n;z++)for(int x=0;x<=n;x++)
            {
                float u=x/(float)n,w=z/(float)n,xx=(u-.5f)*1.68f,zz=(w-.5f)*1.72f;
                float y=.70f+(rumpled?.018f*Mathf.Sin(x*1.6f+z*.8f)+.018f*Mathf.Sin(z*.9f):.006f*Mathf.Sin(x));
                if(x==0||x==n)y-=.07f;
                if(z==0)y-=.055f;
                v.Add(new Vector3(xx,y,zz));uv.Add(new Vector2(u*2,w*2));
                if(x<n&&z<n){int a=z*(n+1)+x;tri.AddRange(new[]{a,a+n+1,a+1,a+1,a+n+1,a+n+2});}
            }
            string path=Generated+"/Meshes/"+(rumpled?"RumpledLinenV2":"MadeLinenV2")+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh==null){mesh=new Mesh();mesh.name=rumpled?"RumpledLinenV2":"MadeLinenV2";mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tri,0);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);}
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            // Plain worn linen keeps the clean/dirty silhouette readable against the dark purchased bed frame.
            var mat=Variant("WornLinen",Mat("TexturesII","painted_concrete_wall_pt_5_plain"),new Color(.82f,.82f,.71f));mat.SetFloat("_Cull",0);mat.SetFloat("_Smoothness",.05f);
            obj.AddComponent<MeshRenderer>().sharedMaterial=mat;
        }
        static void RefineRoom(Scene scene,int i)
        {
            var root=Find(scene,"Motel/Rooms/Room"+(101+i));var decor=root.Find("PizzaDoggyDecor");if(decor==null)return;
            var service=root.Find("ServiceState");
            foreach(var t in service.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("Blanket")))Hide(t);
            BedCover(service.Find("UnmadeBedding"),i,true);BedCover(service.Find("MadeBedding"),i,false);
            foreach(var t in service.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Visual_pillow_mp_1")) t.position+=Vector3.up*(.76f-BoundsOf(t.gameObject).center.y);
            var bin=decor.Find("Visual_bucket_mx_1");bin.position+=RoomPoint(i,20.1f,.15f,14.25f)-BoundsOf(bin.gameObject).center;
            var tile=Mat("TexturesII","tiles_floor_pt_5_1");
            if(decor.Find("BathroomRearTileLining")==null)
            {
                DetailBox("BathroomRearTileLining",decor,RoomPoint(i,19.6f,1.4f,10.008f),new Vector3(2,2.8f,.012f),tile);
                DetailBox("BathroomEastTileLining",decor,RoomPoint(i,20.588f,1.4f,11),new Vector3(.014f,2.8f,2),tile);
                var towel=DetailBox("BathroomHandTowel",decor,RoomPoint(i,18.62f,1.1f,10.9f),new Vector3(.025f,.44f,.36f),AssetDatabase.LoadAssetAtPath<Material>(Generated+"/Materials/WornLinen.mat"));
                DetailBox("TowelRail",decor,RoomPoint(i,18.65f,1.32f,10.9f),new Vector3(.04f,.035f,.45f),Solid("DullChrome",new Color(.38f,.4f,.38f),.65f));
            }
            var room=All<ServiceRoom>(scene).Single(r=>r.RoomNumber==(101+i).ToString());
            var tv=room.Television.transform;var visual=tv.Find("TVHousing").GetChild(0);
            var remote=decor.Find("Visual_tv_remote_mp_1");remote.position+=new Vector3(tv.Find("TVHousing").position.x+.45f,.82f,14.97f+(i>=2?5.7f:0))-BoundsOf(remote.gameObject).center;
            // Screen remains at its established interaction depth. Match the model's bezel to that rectangle.
            FitExisting(visual.gameObject,new Vector3(tv.Find("TVHousing").position.x,1.12f,15.14f+(i>=2?5.7f:0)),new Vector3(.7f,.66f,.38f));
            foreach(string name in new[]{"TVScreen","TVStatus"}){var target=tv.Find(name);var p=target.position;p.y=1.2f;target.position=p;}
            AlignScreen(visual,tv.Find("TVScreen"),false);
            foreach(var r in room.Door.GetComponentsInChildren<Renderer>(true))if(r.name.StartsWith("Handle"))r.enabled=false;
            foreach(var t in root.Find("Lighting").GetComponentsInChildren<Transform>())Hide(t);
        }
        static void RefineSign(Scene scene)
        {
            var label=Find(scene,"Motel/Exterior/PizzaDoggyExteriorProps/VacancyLettering");if(label==null)return;
            var p=label.position;p.z=-5.095f;label.position=p; // In front of the existing support post as well as the sign board.
        }
        static partial void Refine(Scene scene)
        {
            for(int i=0;i<4;i++)RefineRoom(scene,i);
            RefineShelves(scene);
            RefineSign(scene);
            foreach(var rib in All<Transform>(scene).Where(t=>t.name=="DumpsterRib")){var p=rib.position;p.x=19.01f;rib.position=p;}
            var pad=Find(scene,"Motel/Exterior/PizzaDoggyExteriorProps/DumpsterPad");if(pad!=null){pad.position=new Vector3(18.3f,-.1f,-3.1f);pad.localScale=new Vector3(2.2f,.2f,1.9f);}
            var reception=Find(scene,"Motel/ReceptionDesk");
            var monitor=reception.Find("ComputerMonitor/Visual_pc_monitor_mp_1");
            if(monitor!=null)
            {
                FitExisting(monitor.gameObject,new Vector3(10.8f,1.275f,4.9f),new Vector3(.4f,.5f,.62f));
                FitExisting(reception.Find("Keyboard/Visual_pc_keyboard_mp_1").gameObject,new Vector3(11.005f,1.145f,4.9f),new Vector3(.18f,.06f,.53f));
                AlignScreen(monitor,reception.Find("ComputerScreen"),true);
                var mouse=Find(scene,"NightShift/ReceptionComputer/Mouse/Visual_pc_mouse_mp_1");mouse.position+=Vector3.up*(1.145f-BoundsOf(mouse.gameObject).center.y);
                var cctv=reception.Find("CCTVMonitor/Visual_pc_monitor_mp_1");FitExisting(cctv.gameObject,new Vector3(10.81f,1.33f,6.6f),new Vector3(.4f,.58f,.7f));AlignScreen(cctv,reception.Find("CCTVScreen"),true);
            }
            foreach(var t in All<Transform>(scene).Where(t=>t.name.StartsWith("MotelSignText_Glyph")))Hide(t);
            foreach(var label in All<TextMesh>(scene))
                if(label.name=="MotelSignLettering")label.characterSize=.1f;
                else if(label.name=="VacancyLettering")label.characterSize=.031f;
                else if(label.name=="UtilityShelfLabel")label.characterSize=.018f;
                else if(label.name=="VendingLabel")label.characterSize=.023f;
            foreach(var text in All<WorldServiceText>(scene))
            {
                var serialized=new SerializedObject(text);var shader=serialized.FindProperty("textShader");
                if(shader.objectReferenceValue==null){shader.objectReferenceValue=Shader.Find("Motel/World Service Text");serialized.ApplyModifiedPropertiesWithoutUndo();text.RefreshMaterial();}
            }
            var glass=Solid("ClearWindowGlass",new Color(.22f,.29f,.3f,.09f),.4f);
            glass.shader=Shader.Find("Universal Render Pipeline/Unlit");glass.SetFloat("_Surface",1);glass.SetFloat("_Blend",0);glass.SetFloat("_SrcBlend",5);glass.SetFloat("_DstBlend",10);glass.SetFloat("_ZWrite",0);glass.SetFloat("_Cull",2);
            glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");glass.DisableKeyword("_ALPHAPREMULTIPLY_ON");glass.SetOverrideTag("RenderType","Transparent");glass.renderQueue=3000;glass.SetShaderPassEnabled("ShadowCaster",false);
            foreach(var t in All<Transform>(scene).Where(t=>t.parent!=null&&t.parent.name=="Windows"&&t.name.Contains("Glass")))t.GetComponent<Renderer>().sharedMaterial=glass;
            foreach(var renderer in All<MeshRenderer>(scene))
                if(renderer.transform.parent!=null&&(renderer.transform.parent.name=="Structure"||renderer.transform.parent.name=="Ceilings"))
                    GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,GameObjectUtility.GetStaticEditorFlags(renderer.gameObject)|StaticEditorFlags.BatchingStatic);
            foreach(var light in All<Light>(scene))
            {
                if(light.name.StartsWith("Room")){light.intensity=5.2f;light.range=5.8f;light.color=new Color(1,.89f,.74f);}
                else if(light.name.StartsWith("Lobby")){light.intensity=5.8f;light.color=new Color(1,.88f,.72f);}
                else if(light.name.StartsWith("Corridor")){light.intensity=3.7f;light.color=new Color(1,.88f,.74f);}
                else if(light.name.StartsWith("Utility")){light.intensity=4.6f;light.color=new Color(.92f,.95f,1);}
                else if(light.name.StartsWith("Streetlight")){light.intensity=10;light.color=new Color(1,.87f,.65f);}
                else if(light.name=="Moon_Fill"){light.intensity=.28f;}
            }
        }
    }
}
