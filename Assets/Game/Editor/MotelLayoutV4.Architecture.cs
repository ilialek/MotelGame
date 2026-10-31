using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using MotelGame.Interactions;

namespace MotelGame.Editor
{
    public static partial class MotelLayoutV4
    {
        const float Wall=.2f, Height=2.8f;
        static GameObject doorPrototype;
        static Transform level, architecture, outdoors, furnishings, gameplay;
        static List<(Transform floor,int area)> privateFloors=new();
        static Material Mat(string path)=>AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/"+path+".mat");
        static Material Paint=>Mat("Art/PizzaDoggy/TexturesII/Materials/painted_concrete_wall_pt_5_plain");
        static Material Exterior=>Mat("Art/PizzaDoggy/Motel/Materials/ExteriorWeatheredPaint");
        static Material Trim=>Mat("Materials/MotelBlockoutV3/Trim");
        static Material Metal=>Mat("Materials/MotelBlockoutV3/Metal");
        static Material Concrete=>Mat("Art/PizzaDoggy/TexturesII/Materials/concrete_pt_1");
        static Material Carpet=>Mat("Art/PizzaDoggy/TexturesII/Materials/carpet_pt_1");
        static Transform Group(string name,Transform parent,Vector3 p=default)
        {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=p;return t;}
        static GameObject Box(string name,Transform parent,Vector3 position,Vector3 size,Material mat,bool solid=true)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=position;g.transform.localScale=size;
            g.GetComponent<Renderer>().sharedMaterial=mat;if(!solid)UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());return g;
        }
        static void LightAt(string name,Transform parent,Vector3 pos,float range=7,float intensity=2.2f,bool cold=false)
        {
            var t=Group(name,parent,pos);var l=t.gameObject.AddComponent<Light>();l.type=LightType.Point;l.range=range;l.intensity=intensity;
            l.color=cold?new Color(.62f,.75f,.83f):new Color(1,.77f,.53f);l.shadows=LightShadows.None;
            Box("Fixture",t,Vector3.up*.16f,new Vector3(.4f,.06f,.3f),Mat("Materials/MotelBlockoutV3/Lamp"),false);
        }
        static Transform Floor(string name,float x0,float x1,float z0,float z1,Material material,float ceiling=Height,int area=0)
        {
            var t=Group(name,architecture);
            var f=Box("Floor",t,new Vector3((x0+x1)/2,-.1f,(z0+z1)/2),new Vector3(x1-x0,.2f,z1-z0),material).transform;
            if(area>0)privateFloors.Add((f,area));
            if(ceiling>0)
            {
                Box("Ceiling",t,new Vector3((x0+x1)/2,ceiling+.1f,(z0+z1)/2),new Vector3(x1-x0,.2f,z1-z0),Paint);
                Box("Roof",t,new Vector3((x0+x1)/2,ceiling+.24f,(z0+z1)/2),new Vector3(x1-x0,.08f,z1-z0),Metal,false);
            }
            return t;
        }
        // Wall endpoints meet at their faces; openings are subtracted before geometry is created.
        static void WallLine(string name, bool horizontal,float fixedCoord,float start,float end,params (float a,float b,float sill,float top)[] cuts)
        {
            var t=Group(name,architecture);
            var breaks=new SortedSet<float>{start,end};
            foreach(var c in cuts){breaks.Add(Mathf.Max(start,c.a));breaks.Add(Mathf.Min(end,c.b));}
            var x=breaks.ToArray();
            for(int i=0;i<x.Length-1;i++)
            {
                float mid=(x[i]+x[i+1])/2;var cut=cuts.FirstOrDefault(c=>mid>c.a&&mid<c.b);
                if(cut.b>cut.a){Segment(0,cut.sill);Segment(cut.top,Height);}
                else Segment(0,Height);
                void Segment(float lo,float hi)
                {
                    if(hi-lo<.001f||x[i+1]-x[i]<.001f)return;
                    var pos=horizontal?new Vector3(mid,(lo+hi)/2,fixedCoord):new Vector3(fixedCoord,(lo+hi)/2,mid);
                    var size=horizontal?new Vector3(x[i+1]-x[i],hi-lo,Wall):new Vector3(Wall,hi-lo,x[i+1]-x[i]);
                    Box("Wall",t,pos,size,Paint);
                    if(lo==0)
                    {
                        var skirt=size;skirt.y=.12f;if(horizontal)skirt.z+=.012f;else skirt.x+=.012f;
                        var sp=pos;sp.y=.06f;Box("Baseboard",t,sp,skirt,Trim,false);
                    }
                }
            }
            foreach(var c in cuts.Where(c=>c.sill>0))Window(t,horizontal,fixedCoord,c.a,c.b,c.sill,c.top);
        }
        static void Window(Transform t,bool horizontal,float fixedCoord,float a,float b,float sill,float top)
        {
            Vector3 Point(float u,float y)=>horizontal?new Vector3(u,y,fixedCoord):new Vector3(fixedCoord,y,u);
            Vector3 Size(float length,float height,float depth)=>horizontal?new Vector3(length,height,depth):new Vector3(depth,height,length);
            Box("Glass",t,Point((a+b)/2,(sill+top)/2),Size(b-a,top-sill,.028f),Mat("Art/PizzaDoggy/Motel/Materials/ClearWindowGlass"));
            foreach(float u in new[]{a+.025f,b-.025f})Box("Frame",t,Point(u,(sill+top)/2),Size(.05f,top-sill,.08f),Trim,false);
            foreach(float y in new[]{sill+.025f,top-.025f})Box("Frame",t,Point((a+b)/2,y),Size(b-a,.05f,.08f),Trim,false);
        }
        static TextMesh Label(string text,Transform parent,Vector3 pos,float yaw=0,float size=.035f)
        {
            var t=Group("Sign_"+text.Replace("\n","_"),parent,pos);t.localRotation=Quaternion.Euler(0,yaw,0);
            var mesh=t.gameObject.AddComponent<TextMesh>();mesh.text=text;mesh.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");mesh.fontSize=64;
            mesh.characterSize=size;mesh.anchor=TextAnchor.MiddleCenter;mesh.alignment=TextAlignment.Center;mesh.color=new Color(.87f,.84f,.67f);
            var world=t.gameObject.AddComponent<MotelGame.Service.WorldServiceText>();Set(world,"textShader",AssetDatabase.LoadAssetAtPath<Shader>("Assets/Game/Shaders/WorldServiceText.shader"));world.RefreshMaterial();return mesh;
        }
        static NetworkDoor Door(string name,Transform parent,Vector3 hinge,float yaw)
        {
            var g=doorPrototype!=null?UnityEngine.Object.Instantiate(doorPrototype,parent):(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/NetworkDoor.prefab"),parent);g.SetActive(true);
            g.name=name;g.transform.localPosition=hinge;g.transform.localRotation=Quaternion.Euler(0,yaw,0);
            var door=g.GetComponent<NetworkDoor>();var leaf=Ref<BoxCollider>(door,"leaf");
            var obstacle=leaf.gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();obstacle.shape=UnityEngine.AI.NavMeshObstacleShape.Box;obstacle.center=leaf.center;obstacle.size=leaf.size;obstacle.carving=true;obstacle.carveOnlyStationary=false;obstacle.enabled=false;Set(door,"openLeafObstacle",obstacle);
            return door;
        }
        static void Architecture()
        {
            privateFloors.Clear();
            // Shared room boundaries. Each wall exists once, including the 0.2 m inter-room walls.
            for(int row=0;row<2;row++)
            {
                float z=row==0?0:17.4f;
                for(int col=0;col<3;col++)
                {
                    float x=col*5.8f;int number=row==0?103-col:104+col;
                    var floor=Floor("Room"+number,x-.1f,x+5.7f,z-.1f,z+6.1f,Carpet,0,number-98);
                    var vent=rooms.Single(r=>r.RoomNumber==number.ToString()).transform.parent.parent.GetComponentInChildren<MotelGame.Horror.VentNode>();
                    var center=vent.transform.position;float left=center.x-.42f,right=center.x+.42f,near=center.z-.42f,far=center.z+.42f;
                    foreach(var rect in new[]{(x-.1f,left,z-.1f,z+6.1f),(right,x+5.7f,z-.1f,z+6.1f),(left,right,z-.1f,near),(left,right,far,z+6.1f)})
                    {
                        if(rect.Item2-rect.Item1<.005f)continue;
                        Box("CeilingAroundVent",floor,new Vector3((rect.Item1+rect.Item2)/2,Height+.1f,(rect.Item3+rect.Item4)/2),new Vector3(rect.Item2-rect.Item1,.2f,rect.Item4-rect.Item3),Paint);
                        Box("RoofAroundVent",floor,new Vector3((rect.Item1+rect.Item2)/2,Height+.24f,(rect.Item3+rect.Item4)/2),new Vector3(rect.Item2-rect.Item1,.08f,rect.Item4-rect.Item3),Metal,false);
                    }
                    float corridor=row==0?x+2.65f:x+2.95f,court=row==0?x+1f:x+4.6f;
                    WallLine("Room"+number+"_CorridorWall",true,row==0?z-.1f:z+6.1f,x,x+5.6f,(corridor-.6f,corridor+.6f,0,2.25f));
                    WallLine("Room"+number+"_CourtyardWall",true,row==0?z+6.1f:z-.1f,x,x+5.6f,(court-.6f,court+.6f,0,2.25f),row==0?(x+2.1f,x+4.8f,.85f,2.25f):(x+.8f,x+3.5f,.85f,2.25f));
                    if(col==0)WallLine("WingWestWall_"+row,false,-.1f,z-.2f,z+6.2f);
                    WallLine("RoomPartyWall_"+number,false,x+5.7f,z-.2f,z+6.2f);
                }
            }
            Floor("SouthCorridor",-.1f,17.4f,-3.1f,-.1f,Carpet);
            Floor("NorthCorridor",-.1f,17.3f,23.5f,26.5f,Carpet);
            Floor("WestPublicConnector",-3.2f,-.1f,-3.2f,26.5f,Carpet);
            WallLine("SouthCorridor_ParkingWall",true,-3.1f,-3,17.2f,(11.6f,14.4f,0,Height));
            WallLine("NorthCorridor_BackyardWall",true,26.5f,-3,17.4f,(-2.2f,-1f,0,2.25f),(2,5,.9f,2.2f),(9,12,.9f,2.2f));
            WallLine("WestConnector_Outside",false,-3.1f,-3.2f,26.6f,(1,4,.9f,2.2f),(10,13,.9f,2.2f),(20,23,.9f,2.2f));
            WallLine("WestConnector_Yard",false,-.1f,6.2f,17.2f,(10.3f,13.1f,0,Height),(7,9.5f,.9f,2.2f),(14,16.4f,.9f,2.2f));
            WallLine("EastSouthCorridor_Outdoor",false,17.3f,-3.2f,-.2f);
            // Structural dogleg below the south wing: lobby -> west -> north -> guest corridor.
            Floor("LobbyTransition_EastWest",11.5f,17.3f,-9.1f,-6.1f,Carpet);
            Floor("LobbyTransition_NorthSouth",11.5f,14.5f,-6.1f,-3.1f,Carpet);
            WallLine("Transition_South",true,-9.1f,11.4f,17.2f);
            WallLine("Transition_West",false,11.5f,-9, -3.2f);
            WallLine("Transition_InnerCorner",true,-6.1f,14.6f,17.2f);
            WallLine("Transition_EastReturn",false,14.5f,-6.2f,-3.2f);
            // Lobby and directly adjacent storage; no corridor between these two rooms.
            Floor("Lobby",17.3f,29.5f,-14.5f,-4.3f,Mat("Art/PizzaDoggy/TexturesII/Materials/linoleum_pt_1"));
            WallLine("Lobby_West",false,17.3f,-14.6f,-4.2f,(-9,-6.2f,0,Height),(-13.5f,-10.1f,.5f,2.45f));
            WallLine("Lobby_Front",true,-14.5f,17.4f,29.4f,(22.1f,24.7f,0,2.4f),(18.2f,21.5f,.5f,2.45f),(25.4f,28.6f,.5f,2.45f));
            WallLine("Lobby_East",false,29.5f,-14.6f,-4.2f);
            WallLine("Lobby_StorageShared",true,-4.3f,17.4f,29.4f,(26.4f,27.6f,0,2.25f));
            Floor("Storage",24.3f,29.5f,-4.3f,1.4f,Concrete,Height,9);
            WallLine("Storage_West",false,24.3f,-4.2f,1.5f,(-1.6f,-.4f,0,2.25f));
            WallLine("Storage_East",false,29.5f,-4.2f,1.5f);
            WallLine("Storage_North",true,1.4f,24.4f,29.4f);
            Door("Storage_LobbyDoor",architecture,new Vector3(27.51f,0,-4.3f),90);
            Door("Storage_CourtyardDoor",architecture,new Vector3(24.3f,0,-1.51f),180);
            Floor("Maintenance",17.3f,23.5f,20.3f,26.5f,Concrete,Height,9);
            WallLine("Maintenance_West",false,17.3f,23.6f,26.4f,(24.4f,25.6f,0,2.25f));
            WallLine("Maintenance_South",true,20.3f,17.4f,23.4f);
            WallLine("Maintenance_East",false,23.5f,20.2f,26.6f,(22.6f,23.8f,0,2.25f));
            WallLine("Maintenance_North",true,26.5f,17.4f,23.4f);
            Door("Maintenance_CorridorDoor",architecture,new Vector3(17.3f,0,24.49f),180);
            Door("Maintenance_BackyardDoor",architecture,new Vector3(23.5f,0,23.71f),0);
            Door("Backyard_PublicExit",architecture,new Vector3(-2.11f,0,26.5f),270);
            // Deliberate flat thresholds. The exterior ground shares y=0 with the interior floors.
            Floor("Courtyard",-.1f,17.4f,6.1f,17.3f,Concrete,0);
            Floor("EastOutdoorWalk",17.4f,24.3f,-4.3f,20.3f,Concrete,0);
            Floor("MaintenanceExitWalk",23.5f,27.2f,20.3f,26.5f,Concrete,0);
            Floor("Backyard",-5f,31f,26.5f,34.6f,Mat("Art/PizzaDoggy/TexturesII/Materials/grass_dead_pt_2"),0);
            Floor("Parking",-5,31,-26,-16.7f,Mat("Art/PizzaDoggy/TexturesII/Materials/asphalt_old_pt_1"),0);
            Floor("ParkingNearWing",-5,17.3f,-16.7f,-9.2f,Mat("Art/PizzaDoggy/TexturesII/Materials/asphalt_old_pt_1"),0);
            Floor("ParkingNorthApron",-3.2f,11.4f,-9.2f,-3.2f,Concrete,0);
            Floor("FrontWalk",17.3f,29.5f,-16.7f,-14.5f,Concrete,0);
            Floor("EastServiceClearing",24.3f,31f,1.4f,20.3f,Concrete,0);
            Floor("LobbyEastApron",29.5f,31f,-16.7f,1.4f,Concrete,0);
            Floor("MaintenanceEastApron",27.2f,31f,20.3f,26.5f,Concrete,0);
            Floor("WestServiceStrip",-5,-3.2f,-9.2f,26.5f,Concrete,0);
            // Courtyard branches and east route are intentionally outdoors, not a third enclosed corridor.
            LightAt("Lobby_Main",architecture,new Vector3(23,2.5f,-8),8,2.8f);
            LightAt("Reception_WorkLight",architecture,new Vector3(27.3f,2.5f,-9),5,2);
            LightAt("WaitingArea_Light",architecture,new Vector3(20.2f,2.5f,-11),6,2);
            LightAt("Storage_Light",architecture,new Vector3(27,2.5f,-1),6,2.4f,true);
            LightAt("Maintenance_Light",architecture,new Vector3(20.4f,2.5f,23.4f),7,2.5f,true);
            for(int i=0;i<3;i++){LightAt("SouthCorridorLamp",architecture,new Vector3(2.8f+i*5.8f,2.4f,-1.6f),5,1.35f);LightAt("NorthCorridorLamp",architecture,new Vector3(2.8f+i*5.8f,2.4f,25),5,1.35f);}
            foreach(float z in new[]{1f,9f,17f,25f})LightAt("WestConnectorLamp",architecture,new Vector3(-1.6f,2.4f,z),6,1.3f);
            LightAt("TransitionLamp",architecture,new Vector3(13,2.4f,-5),5,1.5f);
            LightAt("TransitionLobbyLamp",architecture,new Vector3(16,2.4f,-7.6f),5,1.5f);
            Label("ROOMS 101–106  →",architecture,new Vector3(17.42f,2.45f,-7.6f),270,.022f);
            Label("101–103  ←     COURTYARD  →",architecture,new Vector3(13,2.4f,-.23f),0,.017f);
            Label("104–106  →",architecture,new Vector3(-1.6f,2.35f,26.38f),0,.023f);
            Label("STORAGE",architecture,new Vector3(27,2.45f,-4.42f),0,.025f);
            Label("MAINTENANCE",architecture,new Vector3(17.18f,2.45f,25),90,.018f);
        }
    }
}
