using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using MotelGame.Registration;

namespace MotelGame.Editor
{
    public static partial class MotelRoadWatch
    {
        const string Content="Assets/Game/Registration";
        static Color RGB(string hex){ColorUtility.TryParseHtmlString("#"+hex,out var c);return c;}
        static GuestCase Case(int i,string name,int appearance,string shirt,GuestVehicleKind kind,string paint,string plate,bool reservation,string driving,string origin,string purpose,bool mimic,string follow,string answer,string reasoning)
        {
            string path=Content+"/Cases/"+(i+1).ToString("00")+"_"+name.Replace(' ','_')+".asset";
            var c=AssetDatabase.LoadAssetAtPath<GuestCase>(path);
            if(c==null){c=ScriptableObject.CreateInstance<GuestCase>();AssetDatabase.CreateAsset(c,path);}
            c.guestName=name;c.appearance=appearance;c.clothing=RGB(shirt);c.vehicleKind=kind;c.vehicleColor=RGB(paint);c.actualPlate=plate;c.claimedPlate=plate;
            c.hasReservation=reservation;c.reservationEntry=reservation?name+" / one room / tonight / late arrival":"No reservation found — walk-in";
            c.nameAnswer=name+".";c.reservationAnswer=reservation?"Yes. One room for tonight, under "+name.Split(' ')[1]+".":"No reservation. Do you have a room for one night?";
            c.drivingAnswer=driving;c.vehicleDescription=driving;c.plateAnswer=plate+". It's parked outside.";c.originAnswer=origin;c.purposeAnswer=purpose;
            c.disguisedMimic=mimic;c.disguiseSeconds=45;c.followUpQuestion=follow;c.followUpAnswer=answer;c.authorReasoning=reasoning;c.hasReport=false;c.report=new RoadWatchReport();EditorUtility.SetDirty(c);return c;
        }
        static GuestCase[] CreateCases()
        {
            Directory.CreateDirectory(Content+"/Cases");Directory.CreateDirectory(Content+"/Photos");Directory.CreateDirectory(Content+"/Materials");AssetDatabase.Refresh();
            var a=new GuestCase[8];
            a[0]=Case(0,"Wade Mercer",1,"4E6549",GuestVehicleKind.Sedan,"582C35","KDH-417",false,
                "Yes. Burgundy sedan, the old one with the square lights.","Pine Cut Road. I hit a deer just beyond the logging turnoff.","The wildlife depot is closed. I need to stay until it opens at six.",false,
                "Why is blood running out of your trunk in this photograph?","I called county wildlife. Their officer asked me to bring the deer to the depot. The tarp tore. You can see its antler by the open lid.",
                "Human. Admitting gives an ordinary stay; refusing loses it. The open trunk, blood and covered antler support a roadkill explanation, not a monster verdict.");
            a[1]=Case(1,"Ellis Boone",1,"B96836",GuestVehicleKind.Van,"C4C3AD","RVT-208",true,
                "White service van. I work on fuel pumps.","Marlow Fuel, south of here. The night attendant called about a jammed dispenser.","My next service call is in the morning. I booked a room on the way.",false,
                "RoadWatch says someone was opening the locked pumps. Was that you?","Yes. The attendant left the service key in the red box. The photo shows my tools under pump three. I was there to repair it.",
                "Human. The tools, service van and explanation fit an after-hours maintenance job. Admit normal stay; refusal loses a customer.");
            a[2]=Case(2,"Jonah Voss",2,"697076",GuestVehicleKind.Sedan,"354B60","JNX-630",true,
                "Dark blue sedan. It ran fine all the way here.","Eastbank, straight across Alder Bridge. I crossed around ten, no detour.","Visiting my brother tomorrow. The road was dry and completely clear.",true,
                "Alder Bridge was closed at six. How did you cross at ten?","There was no barrier and no water. I drove straight across. Your report must be about another bridge.",
                "Mimic. Same car and guest photographed at closed flooded bridge; insists on impossible crossing. Admit: normal room entry then existing Mimic hunt after 45 seconds. Refuse: leaves safely.");
            a[3]=Case(3,"Peter Shaw",0,"A58E6B",GuestVehicleKind.Sedan,"677B83","LMP-552",false,
                "Blue-gray sedan. Borrowed it from my sister today.","The bowling alley on County Nine. I couldn't find the right car for a while.","My sister lives another two hours away. Too tired to drive back tonight.",false,
                "Why were you trying another car's door at the bowling alley?","Same model and color as my sister's. Her plate is LMP-552. The car I tried was LMP-525. I checked the tag and moved on.",
                "Human. Similar cars and transposed plates explain the attempted door. Admit ordinary stay; refusal loses it.");
            a[4]=Case(4,"Sam Ortiz",1,"344A67",GuestVehicleKind.Van,"4F5557","BRC-319",false,
                "Gray van. My delivery gear is still in it.","County Memorial Hospital. The last supply drop took longer than planned.","Rest before the morning delivery run.",false,"","","Human with no report. Vehicle and account agree. Admit ordinary stay; refusal loses it.");
            a[5]=Case(5,"Martin Cole",2,"685443",GuestVehicleKind.Sedan,"9D936F","HVA-084",true,
                "Beige sedan, parked near the front.","Northfield. The sales meeting ran past dinner.","I'm due at a supplier in this county at eight tomorrow.",false,"","","Human with no report. Reservation and visible car agree. Admit ordinary stay; refusal loses it.");
            a[6]=Case(6,"Roy Keller",0,"333638",GuestVehicleKind.Sedan,"252D34","WQN-701",false,
                "White delivery van. I parked it in the bay when I got here.","The warehouse on Mill Road. I drove alone.","A room until morning. Nothing else.",true,
                "Your bay has a black sedan marked WQN-701. You said a white van?","No. I arrived in a white van, WQN-710. I haven't changed vehicles. You must be looking at somebody else's bay.",
                "Mimic with no report. Assigned bay contains black sedan WQN-701, contradicting vehicle and plate claims; doubles down. Admit leads to existing hunt after 45 seconds in room; refuse prevents threat.");
            a[6].claimedPlate="WQN-710";a[6].plateAnswer="WQN-710. I'm certain of it.";
            a[7]=Case(7,"Theo Grant",0,"83827D",GuestVehicleKind.Sedan,"4D6C5B","DSE-246",false,
                "Green sedan. Garage loaner; mine broke down this afternoon.","West Lake. The garage gave me the car just before closing.","Waiting for a replacement alternator tomorrow.",false,
                "Could you confirm the loaner's plate against the car outside?","Sorry, I read the garage slip upside down. DSE-246, yes. Green sedan. I only picked it up today.",
                "Human with no report. Initially uncertain plate, then corrects it consistently with visible loaner. A plate mistake alone is not a universal Mimic rule.");
            a[7].claimedPlate="DSE-264";a[7].plateAnswer="DSE-264, I think. It's a loaner; I haven't learned it yet.";
            Report(a[0],"Blood trail at Pine Cut turnout","Pine Cut Road / logging turnoff","October 27 · 21:42","County road patrol / submitted camera image",
                "A man was seen beside a burgundy sedan with its trunk open. Blood dripped onto the turnout beneath a covered load; a warning triangle stood behind the vehicle. Wildlife dispatch confirms a deer collision was called in from this stretch of road.","01_pine_cut");
            Report(a[1],"After-hours access at Marlow Fuel","Marlow Fuel / pump three","October 27 · 21:10","Passing motorist / roadside camera",
                "A man in work clothes opened the service panel of a locked dispenser after closing. A white van and a tool case were beside pump three. The station sign lists an after-hours maintenance number.","02_marlow_fuel");
            Report(a[2],"Alder Bridge closed after washout","Alder Bridge / east approach","October 27 · 22:05","County road crew / closure update",
                "The bridge has been impassable since 18:00: water removed the center span. A blue sedan and its driver were photographed at the locked east barrier at 22:05. The posted detour adds forty miles.","03_alder_bridge");
            Report(a[3],"Driver tries wrong sedan at bowling lot","County Nine Bowl / south parking row","October 27 · 21:27","Night attendant / compact digital camera",
                "A man tried the door of blue-gray sedan LMP-525 before checking its rear plate. A second similar sedan, LMP-552, stood one bay over. No damage or forced entry was reported.","04_bowling_lot");
            foreach(var c in a)EditorUtility.SetDirty(c);AssetDatabase.SaveAssets();return a;
        }
        static void Report(GuestCase c,string headline,string location,string time,string source,string description,string image)
        {c.hasReport=true;c.report=new RoadWatchReport{headline=headline,location=location,eventTime=time,source=source,description=description,photograph=AssetDatabase.LoadAssetAtPath<Texture2D>(Content+"/Photos/"+image+".png")};}
    }
}
