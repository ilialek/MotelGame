using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using MotelGame.Horror;

namespace MotelGame.Editor
{
    public static partial class MotelLayoutV4
    {
        static GameObject Model(string pack,string name,Transform parent,Vector3 ground,float height,float yaw=0)
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/PizzaDoggy/"+pack+"/Prefabs/"+name+".prefab");
            if(asset==null)throw new Exception("Missing existing art asset "+name);
            var go=(GameObject)PrefabUtility.InstantiatePrefab(asset,parent);go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.Euler(0,yaw,0);
            foreach(var c in go.GetComponentsInChildren<Collider>())Delete(c);
            var renderers=go.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
            go.transform.localScale*=height/bounds.size.y;
            bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
            go.transform.position+=ground-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
            foreach(var r in renderers)r.shadowCastingMode=ShadowCastingMode.Off;
            return go;
        }
        static void ExteriorScene()
        {
            var nature=Group("ForestBeyondProperty",outdoors);
            Box("ForestGround_Backdrop",nature,new Vector3(12,-.16f,7),new Vector3(80,.2f,90),Mat("Art/PizzaDoggy/TexturesII/Materials/grass_dead_pt_2"),false);
            var moon=Group("Moonlight",outdoors).gameObject.AddComponent<Light>();moon.type=LightType.Directional;moon.color=new Color(.45f,.6f,.78f);moon.intensity=.08f;moon.transform.rotation=Quaternion.Euler(45,-35,0);moon.shadows=LightShadows.None;
            // Sparse low rail fence: view to the forest remains open at eye height.
            foreach(var line in new[]{(new Vector3(-5,0,-26),new Vector3(-5,0,34.5f)),(new Vector3(-5,0,34.5f),new Vector3(31,0,34.5f)),(new Vector3(31,0,34.5f),new Vector3(31,0,-26))})
            {
                int count=Mathf.CeilToInt(Vector3.Distance(line.Item1,line.Item2)/2.7f);
                for(int i=0;i<=count;i++)
                {
                    var p=Vector3.Lerp(line.Item1,line.Item2,(float)i/count);Box("FencePost",outdoors,p+Vector3.up*.65f,new Vector3(.12f,1.3f,.12f),Trim);
                    if(i==count)continue;var q=Vector3.Lerp(line.Item1,line.Item2,(float)(i+1)/count);float length=Vector3.Distance(p,q);
                    foreach(float y in new[]{.35f,.85f}){var rail=Box("FenceRail",outdoors,(p+q)/2+Vector3.up*y,new Vector3(.07f,.08f,length),Trim);rail.transform.rotation=Quaternion.LookRotation(q-p);}
                }
            }
            var random=new System.Random(1601);
            for(int i=0;i<72;i++)
            {
                var p=i<24?new Vector3(-7-(i%3)*2.3f,0,-13+(i/3)*6.8f):i<48?new Vector3(-6+((i-24)/3)*5.3f,0,37+(i%3)*2.2f):new Vector3(33+(i%3)*2.5f,0,-14+((i-48)/3)*7);
                p+=new Vector3((float)random.NextDouble(),0,(float)random.NextDouble());
                Model("Nature",i%6==0?"tree_5":"pine_tree_n_2",nature,p,6.8f+i%4*.8f,i*37);
            }
            for(int i=0;i<24;i++)Model("Nature",i%2==0?"fern_1":"grass_2",outdoors,new Vector3(-3+(i%12)*2.5f,0,32+(i/12)*.8f),.3f+i%3*.1f,i*41);
            // Limited central planting: at least 3 m of clear maneuvering space on each side.
            Box("CourtyardPlantingBed",outdoors,new Vector3(8.6f,.1f,11.7f),new Vector3(3.2f,.2f,2.6f),Mat("Art/PizzaDoggy/TexturesII/Materials/grass_dead_pt_2"));
            Model("Nature","tree_5",outdoors,new Vector3(8.6f,.2f,11.7f),4.7f);
            Box("CourtyardTreeTrunkCollision",outdoors,new Vector3(8.6f,1.3f,11.7f),new Vector3(.42f,2.6f,.42f),Trim);
            for(int i=0;i<3;i++)Model("Nature","fern_2",outdoors,new Vector3(7.5f+i*.9f,.2f,12.3f),.5f,i*80);
            foreach(var p in new[]{new Vector3(3,2.4f,7),new Vector3(14,2.4f,16.6f),new Vector3(19.5f,2.5f,6),new Vector3(25,2.5f,18),new Vector3(1,2.6f,28),new Vector3(14,2.6f,29),new Vector3(25,2.6f,29)})
            {Box("ServiceLampPost",outdoors,new Vector3(p.x,p.y/2,p.z),new Vector3(.12f,p.y,.12f),Metal);LightAt("OutdoorLamp",outdoors,p,8,2.2f,true);}
            for(int i=0;i<6;i++)
            {
                float x=-1+i*3.2f;
                Box("ParkingBayLine",outdoors,new Vector3(x,.008f,-20),new Vector3(.09f,.016f,5),Mat("Materials/MotelBlockoutV3/Marking"),false);
                if(i==0||i==2||i==4)
                {
                    var car=Group("ParkedCar_"+i,outdoors,new Vector3(x+1.5f,0,-20));var paint=Mat("Art/PizzaDoggy/Motel/Materials/FadedCarPaint");
                    Box("Body",car,new Vector3(0,.6f,0),new Vector3(1.8f,.65f,4.2f),paint);
                    Box("Cabin",car,new Vector3(0,1.1f,-.2f),new Vector3(1.55f,.65f,2.25f),Mat("Art/PizzaDoggy/Motel/Materials/DarkCarGlass"));
                    Box("Roof",car,new Vector3(0,1.44f,-.2f),new Vector3(1.56f,.06f,2.1f),paint,false);
                    foreach(float side in new[]{-.92f,.92f})foreach(float z in new[]{-1.25f,1.25f})
                    {var wheel=GameObject.CreatePrimitive(PrimitiveType.Cylinder);wheel.name="Wheel";wheel.transform.SetParent(car,false);wheel.transform.localPosition=new Vector3(side,.34f,z);wheel.transform.localRotation=Quaternion.Euler(0,0,90);wheel.transform.localScale=new Vector3(.63f,.12f,.63f);wheel.GetComponent<Renderer>().sharedMaterial=Mat("Art/PizzaDoggy/Motel/Materials/DarkRubber");}
                }
            }
            LightAt("ParkingLamp",outdoors,new Vector3(16,4,-19),15,3.5f,true);Box("ParkingLampPole",outdoors,new Vector3(16,2,-19),new Vector3(.15f,4,.15f),Metal);
            Box("Road",outdoors,new Vector3(12,-.11f,-28.5f),new Vector3(55,.2f,5),Mat("Art/PizzaDoggy/TexturesII/Materials/asphalt_old_pt_1"));
            // Visible low roadside barriers keep the non-playable forest beyond the colliding ground.
            foreach(var rail in new[]{(new Vector3(12,.65f,-30.9f),new Vector3(55,1.3f,.15f)),(new Vector3(-15.4f,.65f,-28.5f),new Vector3(.15f,1.3f,5)),(new Vector3(39.4f,.65f,-28.5f),new Vector3(.15f,1.3f,5)),(new Vector3(-10.2f,.65f,-26),new Vector3(10.4f,1.3f,.15f)),(new Vector3(35.2f,.65f,-26),new Vector3(8.4f,1.3f,.15f))})
                Box("RoadGuardrail",outdoors,rail.Item1,rail.Item2,Metal);
            for(int i=0;i<9;i++)Box("RoadCenterline",outdoors,new Vector3(-9+i*5,.008f,-28.5f),new Vector3(2.6f,.016f,.1f),Mat("Materials/MotelBlockoutV3/Marking"),false);
            Box("MotelSignPost",outdoors,new Vector3(29,1.7f,-23),new Vector3(.15f,3.4f,.15f),Metal);
            Box("MotelSign",outdoors,new Vector3(29,3,-23),new Vector3(3.3f,1.2f,.18f),Mat("Art/PizzaDoggy/Motel/Materials/MotelSignEnamel"));
            Label("PINE REST\nMOTEL",outdoors,new Vector3(29,3,-23.1f),0,.055f);
            LightAt("MotelSignLight",outdoors,new Vector3(29,3,-24),5,2);
            Label("REFUSE / SERVICE YARD",outdoors,new Vector3(-1.6f,2.4f,26.62f),180,.017f);
            Model("MegaPackII","wooden_plank_1",outdoors,new Vector3(3,0,32),.15f,25);
            // The forest is background. Low visible fences mark the gameplay bounds without a tall opaque wall.
        }
        static void Ventilation(Scene s,VentSurveillance surveillance)
        {
            var vents=Group("Ventilation",gameplay);var list=rooms.Select(r=>r.transform.parent.parent.GetComponentInChildren<VentNode>()).ToList();
            foreach(var n in list){n.neighbours=Array.Empty<VentNode>();}
            void Edge(VentNode a,VentNode b){a.neighbours=a.neighbours.Append(b).ToArray();b.neighbours=b.neighbours.Append(a).ToArray();}
            VentNode Node(string name,Vector3 p){var n=Group(name,vents,p).gameObject.AddComponent<VentNode>();n.index=list.Count;n.neighbours=Array.Empty<VentNode>();list.Add(n);return n;}
            var junctions=new VentNode[6];
            for(int i=0;i<6;i++)
            {
                var room=list[i];var p=room.transform.position;p.y=3.4f;var riser=Node("Room"+room.room.RoomNumber+"_Riser",p);Edge(room,riser);
                junctions[i]=Node("Room"+room.room.RoomNumber+"_MainJunction",new Vector3(p.x,3.4f,i<3?-1.6f:25));Edge(riser,junctions[i]);
            }
            var sw=Node("WestSouthTurn",new Vector3(-1.6f,3.4f,-1.6f));var nw=Node("WestNorthTurn",new Vector3(-1.6f,3.4f,25));
            Edge(junctions[0],junctions[1]);Edge(junctions[1],junctions[2]);Edge(junctions[2],sw);Edge(sw,nw);
            Edge(nw,junctions[3]);Edge(junctions[3],junctions[4]);Edge(junctions[4],junctions[5]);Items(hunt,"nodes",list);
            var ductMat=Mat("Horror/DuctInterior");
            foreach(var a in list)foreach(var b in a.neighbours.Where(n=>n.index>a.index))
            {
                var delta=b.transform.position-a.transform.position;float length=delta.magnitude;
                if(Mathf.Abs(delta.y)>.1f)
                {
                    // Open the reused vent housing and connect it through a matching ceiling aperture.
                    Delete(a.transform.Find("Top")?.gameObject);
                    var shaft=Group("RiserHousing_"+a.room.RoomNumber,vents,new Vector3(a.transform.position.x,0,a.transform.position.z));
                    foreach(float side in new[]{-.4f,.4f})
                    {
                        Box("ShaftSide",shaft,new Vector3(side,3.03f,0),new Vector3(.04f,.66f,.8f),ductMat,false);
                        Box("ShaftSide",shaft,new Vector3(0,3.03f,side),new Vector3(.8f,.66f,.04f),ductMat,false);
                    }
                    continue;
                }
                var t=Group("Duct_"+a.index+"_"+b.index,vents,(a.transform.position+b.transform.position)/2);t.rotation=Quaternion.LookRotation(delta);
                Box("Floor",t,new Vector3(0,-.09f,0),new Vector3(1.1f,.08f,length-1.1f),ductMat,false);
                Box("Roof",t,new Vector3(0,1.02f,0),new Vector3(1.1f,.08f,length-1.1f),ductMat,false);
                foreach(float x in new[]{-.55f,.55f})Box("Side",t,new Vector3(x,.47f,0),new Vector3(.06f,1.02f,Mathf.Max(.1f,length-1.1f)),ductMat,false);
            }
            // Junction cells close unused directions; only real graph connections remain open.
            foreach(var n in list.Where(n=>n.room==null))
            {
                var t=Group("JunctionHousing_"+n.index,vents,n.transform.position);
                Box("Roof",t,new Vector3(0,1.02f,0),new Vector3(1.1f,.08f,1.1f),ductMat,false);
                if(!n.name.EndsWith("_Riser"))Box("Floor",t,new Vector3(0,-.09f,0),new Vector3(1.1f,.08f,1.1f),ductMat,false);
                else foreach(float side in new[]{-1f,1f})
                {
                    Box("RiserFloorRim",t,new Vector3(side*.485f,-.09f,0),new Vector3(.13f,.08f,1.1f),ductMat,false);
                    Box("RiserFloorRim",t,new Vector3(0,-.09f,side*.485f),new Vector3(.84f,.08f,.13f),ductMat,false);
                }
                foreach(var d in new[]{Vector3.forward,Vector3.back,Vector3.right,Vector3.left})
                    if(!n.neighbours.Any(o=>Vector3.Dot((o.transform.position-n.transform.position).normalized,d)>.9f))
                        Box("ClosedDuctEnd",t,d*.55f+Vector3.up*.47f,new Vector3(Mathf.Abs(d.x)>.5f?.06f:1.1f,1.02f,Mathf.Abs(d.z)>.5f?.06f:1.1f),ductMat,false);
            }
            surveillance.cameras=new Camera[3];
            var camPositions=new[]{junctions[0].transform.position+new Vector3(-.15f,.5f,0),nw.transform.position+new Vector3(0,.5f,-.4f),junctions[5].transform.position+new Vector3(-.15f,.5f,0)};
            var looks=new[]{new Vector3(10,3.7f,-1.6f),new Vector3(-1.6f,3.7f,17),new Vector3(3,3.7f,25)};
            for(int i=0;i<3;i++)
            {
                var t=Group("VentCamera_"+(i+1),vents,camPositions[i]);t.LookAt(looks[i]);var c=t.gameObject.AddComponent<Camera>();c.enabled=false;c.fieldOfView=72;c.nearClipPlane=.04f;c.farClipPlane=18;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=Color.black;c.allowHDR=false;
                t.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;surveillance.cameras[i]=c;
                var l=t.gameObject.AddComponent<Light>();l.type=LightType.Point;l.range=9;l.intensity=2;l.color=new Color(.5f,.7f,.58f);l.shadows=LightShadows.None;
            }
        }
    }
}
