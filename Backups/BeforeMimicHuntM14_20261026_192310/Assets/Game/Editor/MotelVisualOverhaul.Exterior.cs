using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MotelGame.Editor
{
    public static partial class MotelVisualOverhaul
    {
        static bool InsideBuilding(Vector3 p)
        {
            return (p.x>=-.2f&&p.x<=14.2f&&p.z>=-.2f&&p.z<=10.2f)||
                (p.x>=14.2f&&p.x<=19.4f&&p.z>=1.6f&&p.z<=7)||
                (p.x>=14&&p.x<=23.8f&&p.z>=6.8f&&p.z<=10)||
                (p.x>=15.4f&&p.x<=29&&p.z>=9.8f&&p.z<=21.5f);
        }
        static void ExteriorFaces(Scene scene)
        {
            var stucco=Variant("ExteriorWeatheredPaint",Mat("TexturesII","painted_concrete_wall_pt_5_plain"),new Color(.77f,.79f,.7f));
            foreach(var filter in All<MeshFilter>(scene).Where(f=>f.transform.parent!=null&&(f.transform.parent.name=="Structure"||f.transform.parent.name=="Ceilings")).ToArray())
            {
                if(!filter.name.StartsWith("Wall")&&!filter.name.StartsWith("Ceiling"))continue;
                var source=filter.sharedMesh;var v=source.vertices;var tris=source.triangles;var inside=new List<int>();var outside=new List<int>();
                for(int j=0;j<tris.Length;j+=3)
                {
                    var a=filter.transform.TransformPoint(v[tris[j]]);var b=filter.transform.TransformPoint(v[tris[j+1]]);var c=filter.transform.TransformPoint(v[tris[j+2]]);
                    var normal=Vector3.Cross(b-a,c-a).normalized;var center=(a+b+c)/3;
                    bool exterior=(filter.name.StartsWith("Ceiling")&&normal.y>.7f)||(!filter.name.StartsWith("Ceiling")&&Mathf.Abs(normal.y)<.5f&&!InsideBuilding(center+normal*.3f));
                    (exterior?outside:inside).AddRange(new[]{tris[j],tris[j+1],tris[j+2]});
                }
                if(outside.Count==0)continue;
                string sourcePath=AssetDatabase.GetAssetPath(source);string path=Generated+"/Meshes/Exterior_"+AssetDatabase.AssetPathToGUID(sourcePath)+".asset";
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(mesh==null){mesh=UnityEngine.Object.Instantiate(source);mesh.name=filter.name+"_InteriorExterior";mesh.subMeshCount=2;mesh.SetTriangles(inside,0);mesh.SetTriangles(outside,1);AssetDatabase.CreateAsset(mesh,path);}
                filter.sharedMesh=mesh;var renderer=filter.GetComponent<Renderer>();renderer.sharedMaterials=new[]{renderer.sharedMaterials[0],stucco};
            }
        }
        static partial void UpgradeExterior(Scene scene)
        {
            var root=Find(scene,"Motel/Exterior");var props=FreshDecor(root,"PizzaDoggyExteriorProps");var nature=FreshDecor(root,"PizzaDoggyVegetation");
            var asphalt=Mat("TexturesII","asphalt_old_pt_1");var soil=Mat("TexturesII","grass_dead_pt_2");var concrete=Mat("TexturesII","concrete_pt_5");
            Surface(root.Find("Ground"),soil,Vector3.one*.5f);Surface(root.Find("Parking"),asphalt,Vector3.one*.45f);Surface(root.Find("FrontWalk"),concrete,Vector3.one*.6f);
            ExteriorFaces(scene);
            // Border planting only: arrival, exit, parking bays and dumpster approaches remain clear.
            Vector3[] trees={new(-4.4f,0,-12),new(-4.8f,0,-4),new(-4.3f,0,5),new(-4.7f,0,15),new(-3.8f,0,24),new(3,0,25.6f),new(9,0,26.1f),new(16,0,25.8f),new(22.8f,0,26.2f),new(29,0,25.4f),new(30,0,18),new(30,0,9.8f),new(28.5f,0,2),new(27,0,-6),new(24.8f,0,-14.5f),new(16.5f,0,-16)};
            for(int i=0;i<trees.Length;i++)
            {
                float h=5.7f+(i%4)*.8f,w=i%3==0?3.6f:3.1f;var p=trees[i]+Vector3.up*(h/2-.18f);
                var tree=Model("Nature",i%5==0?"tree_5":i%7==0?"pine_tree_n_1":"pine_tree_n_2",nature,p,new Vector3(w,h,w),i*37%360);
                foreach(var renderer in tree.GetComponentsInChildren<Renderer>())renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var rng=new System.Random(12012);
            for(int i=0;i<66;i++)
            {
                float x,z;
                if(i<22){x=-2.9f+(float)rng.NextDouble()*1.1f;z=-14+(float)rng.NextDouble()*38;}
                else if(i<44){x=-1+(float)rng.NextDouble()*29;z=23.5f+(float)rng.NextDouble()*2;}
                else{x=23.5f+(float)rng.NextDouble()*5;z=-15+(float)rng.NextDouble()*18;}
                float h=.28f+(float)rng.NextDouble()*.35f,w=h*1.8f;
                var obj=Model("Nature",i%5==0?"fern_1":"grass_"+(1+i%3),nature,new Vector3(x,h/2-.18f,z),new Vector3(w,h,w),i*53%360);
                foreach(var renderer in obj.GetComponentsInChildren<Renderer>())renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            for(int i=0;i<8;i++)
            {
                var p=new Vector3(i<4?-2.1f:25.8f,.2f,-10+i%4*7.4f);
                Model("Nature",i%2==0?"stone_1":"stone_3",nature,p,new Vector3(.75f,.7f,1.1f),i*33);
                if(i<5)Model("Nature","fern_2",nature,p+new Vector3(1,.13f,.8f),new Vector3(.9f,.9f,.9f),i*45);
            }
            Model("Nature","tree_stump_1",nature,new Vector3(-3.1f,.2f,20.2f),new Vector3(.75f,.75f,.75f));
            // Lightweight reusable meshes and shared cutout materials, no vegetation colliders or dynamic lights.
            for(int i=0;i<6;i++)
            {
                float z=-12.5f+i*1.9f;Model("Nature","grass_2",nature,new Vector3(19.75f,.05f,z),new Vector3(.45f,.5f,.45f),i*40);
            }
            for(int i=0;i<2;i++)
            {
                float x=i==0?14.35f:-.32f;Model("MegaPackII","rainwater_downpipe_hl_1_middle",props,new Vector3(x,1.9f,.15f),new Vector3(.12f,2.35f,.12f));
                Model("MegaPackII","rainwater_downpipe_hl_1_end",props,new Vector3(x,.4f,.15f),new Vector3(.14f,.65f,.24f));
            }
            var paint=Solid("DumpsterEnamel",new Color(.19f,.27f,.23f),.18f);var rubber=Solid("DarkRubber",new Color(.055f,.06f,.055f),.08f);
            var dumpster=Find(scene,"MotelOperations/OutdoorDumpster");
            if(dumpster==null)dumpster=All<Transform>(scene).Single(t=>t.name=="OutdoorDumpster");
            foreach(var t in dumpster.GetComponentsInChildren<Transform>())if(t.GetComponent<MeshFilter>()!=null&&!t.name.Contains("Label"))Surface(t,t.name.Contains("Lid")?rubber:paint,Vector3.one);
            DetailBox("DumpsterPad",props,new Vector3(18.3f,-.185f,-3.1f),new Vector3(2.2f,.02f,1.9f),concrete);
            for(int i=0;i<5;i++)DetailBox("DumpsterRib",props,new Vector3(17.59f,.62f,-3.52f+i*.21f),new Vector3(.03f,1.02f,.035f),paint);
            Model("MegaPackII","wooden_plank_1",props,new Vector3(19.35f,.11f,-2.9f),new Vector3(.2f,.09f,.8f),20);
            var car=root.Find("Car");var carPaint=Solid("FadedCarPaint",new Color(.27f,.3f,.29f),.35f);var glass=Solid("DarkCarGlass",new Color(.06f,.1f,.11f),.65f);
            foreach(var t in car.GetComponentsInChildren<Transform>())Surface(t,t.name=="CarCabin"?glass:t.name=="Wheel"?rubber:carPaint,Vector3.one);
            DetailBox("CarRoof",props,new Vector3(10.8f,1.42f,-11.3f),new Vector3(1.6f,.05f,2.28f),carPaint);
            for(int side=0;side<2;side++)
            {
                float x=side==0?10.04f:11.56f;
                foreach(float z in new[]{-12.38f,-11.45f,-10.2f})DetailBox("CarWindowPillar",props,new Vector3(x,1.13f,z),new Vector3(.04f,.55f,.06f),carPaint);
                DetailBox("CarDoorHandle",props,new Vector3(side==0?9.89f:11.71f,.87f,-11.35f),new Vector3(.03f,.035f,.16f),Solid("DullChrome",new Color(.38f,.4f,.38f),.65f));
            }
            for(int side=0;side<2;side++)DetailBox("CarHeadlamp",props,new Vector3(10.2f+side*1.2f,.7f,-9.04f),new Vector3(.35f,.16f,.025f),Solid("HeadlightGlass",new Color(.7f,.7f,.55f),.6f));
            foreach(float z in new[]{-13.36f,-9.04f})DetailBox("CarBumper",props,new Vector3(10.8f,.4f,z),new Vector3(1.8f,.12f,.08f),rubber);
            Surface(root.Find("MotelSign"),Solid("MotelSignEnamel",new Color(.11f,.24f,.25f),.25f),Vector3.one);
            foreach(var t in root.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("MotelSign_Glyph")))Hide(t);
            Label("MotelSignLettering",props,"MOTEL",new Vector3(-.7f,3.39f,-5.07f),Vector3.back,.8f,new Color(.88f,.82f,.62f));
            Label("VacancyLettering",props,"VACANCY",new Vector3(-.7f,3.06f,-5.071f),Vector3.back,.25f,new Color(.68f,.76f,.61f));
            Model("MegaPackII","lamp_mx_3_off",props,new Vector3(17.5f,5.58f,-7),new Vector3(.65f,.18f,.48f));Hide(root.Find("StreetlightHead"));
        }
    }
}
