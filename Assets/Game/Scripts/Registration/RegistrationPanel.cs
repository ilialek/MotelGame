using System;
using UnityEngine;
using Unity.Netcode;
using MotelGame.Service;

namespace MotelGame.Registration
{
    // Local presentation on the existing computer. All questions and decisions use its server lease.
    public sealed class RegistrationPanel : MonoBehaviour
    {
        ReceptionComputer station;
        string plateInput="";
        RegisteredGuest displayed;
        uint visit;
        int roomIndex;
        string tooltip;
        Rect tooltipField;
        Vector2 tooltipMouse;
        float nextQuestion;
        Vector2 reportScroll;
        GUIStyle body,title,paperText,paperTitle,paper,field,button,small,subtitle,plateStyle;
        Texture2D paperTexture,controlHighlight;
        bool enlarged;
        public bool Enlarged=>enlarged;
        RegistrationDesk Desk=>RegistrationDesk.Instance;
        void Awake()=>station=GetComponent<ReceptionComputer>();
        void Styles()
        {
            if(body!=null)return;
            body=new GUIStyle(GUI.skin.label){fontSize=14,wordWrap=true};body.normal.textColor=new Color(.8f,.86f,.77f);
            title=new GUIStyle(body){fontSize=18,fontStyle=FontStyle.Bold};small=new GUIStyle(body){fontSize=12};
            paperText=new GUIStyle(body){fontSize=14};paperText.normal.textColor=new Color(.1f,.14f,.15f);
            paperTitle=new GUIStyle(paperText){fontSize=16,fontStyle=FontStyle.Bold};
            paperTexture=new Texture2D(1,1);paperTexture.SetPixel(0,0,new Color(.76f,.78f,.71f));paperTexture.Apply();
            paper=new GUIStyle(GUI.skin.box){padding=new RectOffset(12,12,8,8)};paper.normal.background=paperTexture;
#if UNITY_EDITOR
            paper.normal.scaledBackgrounds=Array.Empty<Texture2D>();
#endif
            controlHighlight=new Texture2D(1,1);controlHighlight.SetPixel(0,0,new Color(.86f,.88f,.81f));controlHighlight.Apply();
            field=new GUIStyle(GUI.skin.button){fontSize=14,alignment=TextAnchor.MiddleLeft,wordWrap=true,padding=new RectOffset(8,8,4,4)};
            button=new GUIStyle(GUI.skin.button){fontSize=13,fixedHeight=28,wordWrap=true};
            plateStyle=new GUIStyle(GUI.skin.textField){fontSize=15};
            // A fixed dark foreground needs a fixed light background in Editor and Player, at every DPI.
            foreach(var style in new[]{field,button,plateStyle})
            {
                style.border=new RectOffset();
                foreach(var state in new[]{style.normal,style.hover,style.active,style.focused,
                    style.onNormal,style.onHover,style.onActive,style.onFocused})
                {
                    state.textColor=new Color(.08f,.12f,.11f);state.background=controlHighlight;
    #if UNITY_EDITOR
                state.scaledBackgrounds=Array.Empty<Texture2D>();
#endif
                }
                style.normal.background=paperTexture;
            }
            button=ComputerUiStyles.Button;field=new GUIStyle(button){alignment=TextAnchor.MiddleLeft,fixedHeight=0};
            subtitle=new GUIStyle(body){fontSize=13,padding=new RectOffset(8,8,5,5)};
        }
        void ObserveGuest()
        {
            var guest=Desk.Waiting;
            if(guest!=displayed||guest!=null&&guest.State.Visit!=visit)
            {
                displayed=guest;visit=guest==null?0:guest.State.Visit;tooltip=null;plateInput="";reportScroll=Vector2.zero;enlarged=false;
                roomIndex=0;
                if(guest!=null)
                {int requested=Array.FindIndex(Desk.Reception.Rooms,r=>r.RoomNumber==guest.State.RequestedRoom.ToString());if(requested>=0)roomIndex=requested;}
            }
        }
        public void DrawRegistration()=>DrawReservations(720);
        public void DrawReservations(float width)
        {
            Styles();ObserveGuest();if(enlarged){GUILayout.FlexibleSpace();return;}
            var r=displayed;
            if(r==null){GUILayout.Label("Waiting for a guest at the counter. Documents appear here automatically.",body);GUILayout.FlexibleSpace();return;}
            GUILayout.BeginHorizontal(GUILayout.Height(238));
            GUILayout.BeginVertical(paper,GUILayout.Width(width*.38f),GUILayout.Height(238));
            GUILayout.Label("BRIAR COUNTY",paperTitle);GUILayout.Label("DRIVER'S LICENSE",paperText);
            var portrait=r.Presentation?.portrait;
            var rect=GUILayoutUtility.GetRect(108,108,GUILayout.ExpandWidth(false));if(portrait!=null)GUI.DrawTexture(rect,portrait,ScaleMode.ScaleToFit,false);
            GUILayout.Label("First name: "+r.State.FirstName,paperText);GUILayout.Label("Last name: "+r.State.LastName,paperText);GUILayout.Label("Date of birth: "+r.State.DateOfBirth,paperText);
            GUILayout.EndVertical();
            GUILayout.BeginVertical(paper,GUILayout.Height(238));GUILayout.Label(r.State.Reserved?"RESERVATION RECORD":"WALK-IN REGISTRATION",paperTitle);
            if(r.State.Reserved)
            {
                BookingField(r,"First name: "+r.State.BookingFirstName,RegistrationQuestion.BookingName,26,"Ask about first name");
                BookingField(r,"Last name: "+r.State.BookingLastName,RegistrationQuestion.BookingName,26,"Ask about last name");
                GUILayout.Label("Check-in: "+r.State.CheckInDate,paperText);
                BookingField(r,"Expected guests: "+r.State.ExpectedGuests,RegistrationQuestion.GuestCount,28,"Ask about guests");
                GUILayout.Label("Requested room: "+r.State.RequestedRoom,paperText);
                if(!r.State.BookingNote.IsEmpty)BookingField(r,"Note: "+r.State.BookingNote,RegistrationQuestion.BookingNote,48,"Ask about reservation note");
            }
            else{GUILayout.Label("No advance reservation.\nA ready room may be offered.",paperText);GUILayout.FlexibleSpace();}
            GUILayout.EndVertical();GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();GUILayout.Label("PARKING SPACE: "+r.ParkingCode,body);
            if(GUILayout.Button(r.State.ParkingKnown?"REPEAT PARKING ANSWER":"ASK WHERE THEY PARKED",button,GUILayout.Width(225)))AskOrRead(r,RegistrationQuestion.ParkingSpace);
            GUILayout.EndHorizontal();
            DrawDecision(r);
        }
        void BookingField(RegisteredGuest guest,string label,RegistrationQuestion question,float height,string hint)
        {
            if(guest.Presentation!=null&&guest.Presentation.Supports(question))
            {
                // These are also document data: keep text readable during a reply.
                bool busy=guest.Speaking||Time.unscaledTime<nextQuestion;
                if(GUILayout.Button(label,field,GUILayout.MinHeight(height))&&!busy)AskOrRead(guest,question);
                var rect=GUILayoutUtility.GetLastRect();
                if(rect.Contains(Event.current.mousePosition)){tooltip=guest.Speaking?"Guest is speaking…":hint;tooltipField=rect;tooltipMouse=Event.current.mousePosition;}
            }
            else GUILayout.Label(label,paperText,GUILayout.MinHeight(height));
        }
        public void BeginFrame()=>tooltip=null;
        public void DrawTooltip(float width,float height)
        {
            if(enlarged||string.IsNullOrEmpty(tooltip))return;
            var style=ComputerUiStyles.Tooltip;
            Vector2 size=style.CalcSize(new GUIContent(tooltip));var mouse=tooltipMouse;
            float x=Mathf.Clamp(mouse.x+14,4,width-size.x-4), y=mouse.y+20;
            if(y+size.y>height-4)y=mouse.y-size.y-10;
            var rect=new Rect(x,y,size.x,size.y);
            if(rect.Overlaps(tooltipField))rect.y=Mathf.Clamp(tooltipField.yMax+4,4,height-size.y-4);
            GUI.Label(rect,tooltip,style);
        }
        void DrawDecision(RegisteredGuest guest)
        {
            if(station.SurveillanceOnly){GUILayout.Label("Investigation is shared. Assign rooms or refuse at the main Reception computer.",small);return;}
            GUILayout.BeginHorizontal();GUILayout.Label("Room",body,GUILayout.Width(38));
            roomIndex=GUILayout.SelectionGrid(roomIndex,Array.ConvertAll(Desk.Reception.Rooms,r=>r.RoomNumber),6,button,GUILayout.Width(265),GUILayout.Height(28));
            var room=Desk.Reception.Rooms[roomIndex];GUILayout.Label(room.AvailabilityText,small,GUILayout.Width(95));
            GUI.enabled=Desk.Reception.CanRegister(room);
            if(GUILayout.Button("ADMIT",button))Desk.Reception.SelectRoom(roomIndex);
            GUI.enabled=true;if(GUILayout.Button("REFUSE",button))Desk.Reception.RefuseWaitingGuest();GUILayout.EndHorizontal();
        }
        void AskOrRead(RegisteredGuest guest,RegistrationQuestion question,int report=-1)
        {
            if(guest.Speaking||Time.unscaledTime<nextQuestion)return;
            nextQuestion=Time.unscaledTime+.5f;
            Desk.AskRpc(new NetworkObjectReference(guest.NetworkObject),guest.State.Visit,question,new NetworkObjectReference(station.NetworkObject),station.Lease,report,Desk.Lookup.Revision);
        }
        void Search()=>Desk.SearchRpc(plateInput,Desk.Lookup.Revision,new NetworkObjectReference(station.NetworkObject),station.Lease);
        public void DrawRoadWatch(float width)
        {
            Styles();ObserveGuest();if(enlarged){GUILayout.FlexibleSpace();return;}
            GUILayout.BeginVertical(paper);GUILayout.Label("RoadWatch / County Incident Desk",paperTitle);
            GUILayout.BeginHorizontal();GUILayout.Label("License plate",paperText,GUILayout.Width(96));
            plateInput=GUILayout.TextField(plateInput,20,plateStyle,GUILayout.Height(28));GUI.enabled=RegistrationDesk.NormalizePlate(plateInput).Length>0;
            if(GUILayout.Button("SEARCH",button,GUILayout.Width(95)))Search();
            GUI.enabled=true;GUILayout.EndHorizontal();
            GUILayout.Label("Read the plate from Parking Camera, then enter it here.",paperText);
            reportScroll=GUILayout.BeginScrollView(reportScroll);
            var result=Desk.Lookup;
            if(!result.Completed)GUILayout.Label("Enter a vehicle plate to search local incident reports.",paperText);
            else if(result.Report<0)GUILayout.Label("NO MATCHING REPORTS FOUND\nPlate: "+result.Plate,paperTitle);
            else if(result.Report<Desk.Cases.Length)
            {
                var c=Desk.Cases[result.Report];var report=c.report;
                GUILayout.BeginHorizontal();GUILayout.BeginVertical();GUILayout.Label(report.headline,paperTitle);
                GUILayout.Label(report.location+"\n"+report.eventTime+"\nSource: "+report.source,paperText);
                GUILayout.Label(report.description,paperText);GUILayout.Label("Matching plate: "+result.Plate,paperText);GUILayout.EndVertical();
                GUILayout.BeginVertical(GUILayout.Width(260));
                var rect=GUILayoutUtility.GetRect(256,171,GUILayout.ExpandWidth(false));
                if(report.photograph!=null){GUI.DrawTexture(rect,report.photograph,ScaleMode.ScaleToFit,false);if(GUI.Button(rect,GUIContent.none,GUIStyle.none))enlarged=true;}
                if(GUILayout.Button("OPEN PHOTOGRAPH",button))enlarged=true;
                GUILayout.EndVertical();GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            if(displayed!=null&&result.Completed&&result.Report>=0&&result.Report<Desk.Cases.Length&&GUILayout.Button("ASK ABOUT REPORT",button))AskOrRead(displayed,RegistrationQuestion.FollowUp,result.Report);
            GUILayout.EndVertical();
        }
        public void DrawPhotographOverlay(float screenWidth,float screenHeight)
        {
            if(!enlarged)return;Styles();var result=Desk.Lookup;
            if(result.Report<0||result.Report>=Desk.Cases.Length){enlarged=false;return;}
            var photo=Desk.Cases[result.Report].report.photograph;if(photo==null){enlarged=false;return;}
            GUI.color=new Color(.01f,.015f,.02f,.99f);GUI.DrawTexture(new Rect(0,0,screenWidth,screenHeight),Texture2D.whiteTexture);GUI.color=Color.white;
            float scale=Mathf.Min((screenWidth-24f)/photo.width,(screenHeight-65f)/photo.height);
            GUI.DrawTexture(new Rect((screenWidth-photo.width*scale)/2,(screenHeight-photo.height*scale)/2-12,photo.width*scale,photo.height*scale),photo,ScaleMode.StretchToFill,false);
            if(GUI.Button(new Rect((screenWidth-260)/2,screenHeight-43,260,30),"CLOSE PHOTOGRAPH",button))enlarged=false;
        }
        public void ClosePhotograph()=>enlarged=false;
        void OnDestroy(){if(paperTexture!=null)Destroy(paperTexture);if(controlHighlight!=null)Destroy(controlHighlight);}
    }
}
