using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using MotelGame.Service;

namespace MotelGame.Editor
{
    public static partial class MotelVisualOverhaul
    {
        static Transform FreshDecor(Transform area,string name="PizzaDoggyDecor")
        {if(area.Find(name)!=null)throw new Exception("Art already installed: "+FullPath(area));return Group(name,area);}
        static void Architecture(Transform area,Material floor,Material wall)
        {
            var ceiling=Mat("TexturesII","painted_concrete_wall_pt_5_plain");
            foreach(var t in area.GetComponentsInChildren<Transform>(true).ToArray())
            {
                if(t.parent==null)continue;
                if(t.parent.name=="Structure")Surface(t,t.name.StartsWith("Floor")?floor:wall,t.name.StartsWith("Floor")?Vector3.one*.65f:new Vector3(.65f,.4f,.65f));
                else if(t.parent.name=="Ceilings")Surface(t,ceiling,Vector3.one*.4f);
                else if(t.parent.name=="Windows"&&!t.name.Contains("Glass"))Surface(t,Mat("TexturesI","wood_6"),Vector3.one);
            }
        }
        static void Label(string name,Transform parent,string content,Vector3 pos,Vector3 normal,float height,Color color)
        {
            var g=new GameObject(name);g.transform.SetParent(parent,false);g.transform.position=pos;g.transform.rotation=Quaternion.LookRotation(-normal);
            var text=g.AddComponent<TextMesh>();text.text=content;text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=64;text.characterSize=height/64;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.color=color;
            g.AddComponent<WorldServiceText>().RefreshMaterial();
        }
        static partial void UpgradeLobby(Scene scene)
        {
            var lobby=Find(scene,"Motel/Lobby");var decor=FreshDecor(lobby);var reception=Find(scene,"Motel/ReceptionDesk");var office=FreshDecor(reception);
            Architecture(lobby,Mat("TexturesI","tiles_floor_6_1"),Mat("TexturesII","old_wallpaper_wall_pt_2_plain"));
            Architecture(Find(scene,"Motel/Corridor"),Mat("TexturesII","carpet_pt_1_2"),Mat("TexturesII","wallpaper_wall_pt_1_1"));
            var wood=Mat("TexturesI","wood_wall_1");var darkWood=Mat("TexturesI","wood_6");
            foreach(var t in reception.GetComponentsInChildren<Transform>().ToArray())
                if(t.name.StartsWith("Counter")||t.name.Contains("Shelf")||t.name=="KeyRack")Surface(t,wood,Vector3.one*.8f);
            DetailBox("CounterTop",office,new Vector3(10.7f,1.104f,5.7f),new Vector3(.81f,.008f,3.42f),darkWood);
            DetailBox("ReturnTop",office,new Vector3(12.5f,1.104f,3.6f),new Vector3(2.82f,.008f,.81f),darkWood);
            for(int i=0;i<5;i++)DetailBox("CounterPanelTrim",office,new Vector3(10.292f,.53f,4.15f+i*.77f),new Vector3(.018f,.92f,.035f),darkWood);
            var waiting=lobby.Find("WaitingArea");foreach(var t in waiting.GetComponentsInChildren<Transform>())Hide(t);
            Model("MegaPack","sofa_3",waiting.Find("Sofa_A_2.2m_Seat"),new Vector3(1.05f,.5f,4.6f),new Vector3(.95f,1,2.2f),90);
            Model("MegaPack","sofa_3_1",waiting.Find("Sofa_B_2.2m_Seat"),new Vector3(2.8f,.5f,7.4f),new Vector3(2.2f,1,.95f),180);
            Model("MegaPack","coffee_table_1",waiting.Find("CoffeeTable_1.2x0.65"),new Vector3(2.8f,.225f,4.7f),new Vector3(1.2f,.45f,.65f));
            foreach(var t in waiting.GetComponentsInChildren<Transform>().Where(t=>t.name=="WaitingChair").ToArray())Model("MegaPack","armchair_2",t,new Vector3(t.position.x,.48f,t.position.z),new Vector3(.65f,.96f,.65f),270);
            Model("MegaPack","open_book_mp_1",decor,new Vector3(2.6f,.48f,4.65f),new Vector3(.28f,.055f,.21f),15);
            Model("MegaPack","ashtray_1",decor,new Vector3(3.16f,.47f,4.75f),new Vector3(.13f,.035f,.13f));
            Model("MegaPack","potted_cactus_mp_1",decor,new Vector3(1.3f,.32f,8.65f),new Vector3(.45f,.64f,.45f));
            Model("MegaPackII","luggage_mp_1",decor,new Vector3(2.45f,.25f,8.2f),new Vector3(.6f,.5f,.27f),180);
            Model("MegaPack","painting_1",decor,new Vector3(3.1f,2.05f,9.95f),new Vector3(1.05f,.82f,.06f),180);
            Model("MegaPack","clock_1",decor,new Vector3(12.2f,2.45f,9.94f),new Vector3(.38f,.38f,.08f),180);
            foreach(var t in lobby.Find("Lighting").GetComponentsInChildren<Transform>().Where(t=>t.GetComponent<Renderer>()!=null).ToArray())
            {Hide(t);Model("MegaPack","ceiling_lamp_1_off",t,new Vector3(t.position.x,3.1f,t.position.z),new Vector3(.7f,.18f,.7f));}
            var corridor=Find(scene,"Motel/Corridor");
            foreach(var t in corridor.Find("Lighting").GetComponentsInChildren<Transform>().Where(t=>t.GetComponent<Renderer>()!=null).ToArray())
            {Hide(t);Model("MegaPack","ceiling_lamp_2_off",t,new Vector3(t.position.x,2.7f,t.position.z),new Vector3(.4f,.18f,1));}
            Surface(lobby.Find("EntryMat"),Mat("TexturesII","carpet_pt_1"),Vector3.one*1.5f);
            // CRT, keyboard and mouse are visual children; the screen collider and focus transform stay untouched.
            Hide(reception.Find("ComputerMonitor"));Hide(reception.Find("Keyboard"));
            var computer=Find(scene,"NightShift/ReceptionComputer");Hide(computer.Find("CRTRearHousing"));Hide(computer.Find("CRTStand"));Hide(computer.Find("Mouse"));
            Model("MegaPack","pc_monitor_mp_1",reception.Find("ComputerMonitor"),new Vector3(10.8f,1.365f,4.9f),new Vector3(.4f,.5f,.62f),90);
            Model("MegaPack","pc_keyboard_mp_1",reception.Find("Keyboard"),new Vector3(11.05f,1.13f,4.9f),new Vector3(.27f,.06f,.53f),90);
            Model("MegaPack","pc_mouse_mp_1",computer.Find("Mouse"),new Vector3(11.03f,1.13f,5.28f),new Vector3(.11f,.06f,.07f),90);
            Model("MegaPack","pc_mp_1",office,new Vector3(12.95f,1.39f,3.82f),new Vector3(.28f,.32f,.28f),0);
            Hide(reception.Find("CCTVMonitor"));
            Model("MegaPack","pc_monitor_mp_1",reception.Find("CCTVMonitor"),new Vector3(10.81f,1.4f,6.6f),new Vector3(.4f,.54f,.64f),90);
            foreach(var key in reception.GetComponentsInChildren<Transform>().Where(t=>t.name=="KeyPlaceholder").ToArray())
            {Hide(key);Model("MegaPack","key_mp_1",key,key.position,new Vector3(.035f,.14f,.055f),270);}
            Model("MegaPack","notebook_1",office,new Vector3(10.65f,1.13f,6.05f),new Vector3(.24f,.04f,.17f),90);
            Model("MegaPack","pen_mp_1",office,new Vector3(10.9f,1.12f,6.1f),new Vector3(.01f,.01f,.14f),15);
            Model("Tech","cup_etx_1",office,new Vector3(12.15f,1.32f,3.83f),new Vector3(.14f,.16f,.11f),0);
            for(int j=0;j<3;j++)Model("MegaPack","book_mp_1",office,new Vector3(11.6f+j*.12f,1.68f,3.82f),new Vector3(.08f,.22f,.19f));
            Model("MegaPack","cardboard_box_2",office,new Vector3(13.35f,1.72f,3.84f),new Vector3(.36f,.27f,.27f));
            // Existing telephone is a gameplay target; retain it and add a legible handset/keypad.
            var phone=reception.Find("Telephone");var plastic=Solid("TelephonePlastic",new Color(.21f,.24f,.22f),.22f);Surface(phone,plastic,Vector3.one);
            DetailBox("PhoneHandset",office,new Vector3(10.72f,1.245f,5.75f),new Vector3(.07f,.06f,.25f),plastic);
            for(int row=0;row<4;row++)for(int col=0;col<3;col++)DetailBox("PhoneKey",office,new Vector3(10.81f+row*.023f,1.224f,5.71f+col*.03f),new Vector3(.017f,.009f,.019f),Solid("PhoneKeys",new Color(.65f,.63f,.52f)));
            Surface(lobby.Find("VendingMachine"),Solid("VendingEnamel",new Color(.19f,.28f,.26f),.25f),Vector3.one);
            Surface(lobby.Find("VendingDisplay"),Solid("VendingGlass",new Color(.06f,.1f,.1f),.6f),Vector3.one);
            Label("VendingLabel",decor,"COLD DRINKS",new Vector3(12.8f,1.75f,.658f),Vector3.back,.18f,new Color(.83f,.79f,.61f));
            for(int j=0;j<3;j++)Model("MegaPackII","plastic_bottle_mx_1_dirty",decor,new Vector3(12.55f+j*.22f,1.2f,.628f),new Vector3(.09f,.25f,.05f));
        }
        static partial void UpgradeUtility(Scene scene)
        {
            var root=Find(scene,"Motel/Storage");var decor=FreshDecor(root);var metal=Mat("TexturesII","concrete_pt_1");
            Architecture(root,Mat("TexturesII","linoleum_pt_1"),Mat("TexturesII","painted_concrete_wall_pt_5_plain"));
            foreach(var t in root.Find("Shelving").GetComponentsInChildren<Transform>())Hide(t);
            for(int i=0;i<3;i++)
            {
                float x=new[]{15.2f,17,18.7f}[i];
                Model("MegaPackII",i==1?"shelf_mx_4":"shelf_mx_3",decor,new Vector3(x,1.03f,6.35f),new Vector3(1.2f,2.06f,.65f),180);
            }
            foreach(var t in root.Find("Boxes").GetComponentsInChildren<Transform>().Where(t=>t.name=="SupplyBox").ToArray())
            {Hide(t);Model("MegaPack","cardboard_box_1",t,t.position,new Vector3(.5f,.3f,.45f));}
            Hide(root.Find("ElectricalPanel"));Model("Tech","fuse_box_etx_1",root.Find("ElectricalPanel"),new Vector3(19.08f,1.5f,2.9f),new Vector3(.2f,.85f,.65f),270);
            Surface(root.Find("MaintenanceCart"),metal,Vector3.one);
            Model("MegaPackII","toolbox_mx_1",decor,new Vector3(15.3f,1.14f,2.6f),new Vector3(.54f,.28f,.3f));
            Model("MegaPackII","paint_can_mx_1",decor,new Vector3(15.2f,1.58f,6.3f),new Vector3(.24f,.28f,.24f));
            Model("MegaPackII","first_aid_kit_hr_1",decor,new Vector3(15.15f,2.2f,6.36f),new Vector3(.4f,.26f,.24f));
            Model("Tech","electrical_tape_etx_1",decor,new Vector3(18.5f,1.51f,6.3f),new Vector3(.11f,.08f,.11f));
            Model("MegaPackII","bucket_mx_1",decor,new Vector3(18.5f,.21f,6.28f),new Vector3(.34f,.42f,.34f));
            Model("MegaPack","lamp_3_off",decor,new Vector3(16.7f,2.67f,4.3f),new Vector3(1.2f,.2f,.25f));
            Surface(root.Find("StorageDoor_parked_open"),Mat("TexturesI","wood_wall_1"),Vector3.one);
            var towels=Find(scene,"GuestService/UtilityTowelStack");
            foreach(var t in towels.GetComponentsInChildren<Transform>())Surface(t,AssetDatabase.LoadAssetAtPath<Material>(Generated+"/Materials/WornLinen.mat"),Vector3.one*2);
            // Keep the original towel ledge as a thin, exactly aligned support within the new rack.
            DetailBox("TowelLedge",decor,new Vector3(17,1.405f,6.32f),new Vector3(.7f,.035f,.45f),metal);
            for(int i=0;i<4;i++)
                Model("Tech","panel_etx_1",decor,new Vector3(15.8f+i*.6f,1.65f,1.88f),new Vector3(.45f,.68f,.04f),0);
            Model("MegaPack","vent_mp_1",decor,new Vector3(19.17f,2.35f,5.9f),new Vector3(.045f,.3f,.38f),270);
            Label("UtilityShelfLabel",decor,"CLEAN LINEN",new Vector3(17,2.16f,6.0f),Vector3.back,.15f,new Color(.82f,.82f,.72f));
        }
    }
}
