using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using MotelGame.Interactions;

namespace MotelGame.Service
{
    public struct ComputerSeat : INetworkSerializable,IEquatable<ComputerSeat>
    {
        public bool Occupied;public ulong User;public uint Lease;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T:IReaderWriter
        {s.SerializeValue(ref Occupied);s.SerializeValue(ref User);s.SerializeValue(ref Lease);}
        public bool Equals(ComputerSeat v)=>Occupied==v.Occupied&&User==v.User&&Lease==v.Lease;
    }
    [DefaultExecutionOrder(100),RequireComponent(typeof(NetworkObject))]
    public sealed class ReceptionComputer : NetworkBehaviour
    {
        [SerializeField] bool surveillanceOnly;
        public bool SurveillanceOnly => surveillanceOnly;
        [SerializeField] ReceptionDesk reception;
        [SerializeField] NightShiftController shift;
        [SerializeField] Collider screen;
        [SerializeField] Transform focus;
        [SerializeField,Min(.05f)] float transitionSeconds=.3f;
        readonly NetworkVariable<ComputerSeat> seat=new(default,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
        PlayerInteractor localUser;
        FirstPersonController movement;
        Camera view;
        Vector3 savedPosition,fromPosition;
        Quaternion savedRotation,fromRotation;
        float transitionStart, savedFieldOfView, fromFieldOfView;
        bool closing;
        Vector2 scroll;
        bool cctv, baggage;
        int registrationTab;
        bool guestView;
        GuestLifecycle seenGuest;uint seenVisit;
        MotelGame.Registration.RegistrationPanel registration;
        Vector2 baggageScroll;
        ServiceRoom relocationSource;
        Texture2D background,buttonBackground,buttonHighlight;
        GUIStyle labelStyle,buttonStyle,boxStyle,titleStyle;
        public Collider ScreenCollider=>screen;
        public Transform Focus=>focus;
        public uint Lease=>seat.Value.Lease;
        public bool Occupied=>seat.Value.Occupied;
        public ulong User=>seat.Value.User;
        public bool LocalOpen=>localUser!=null;
        public bool Focused=>LocalOpen&&!closing&&Time.unscaledTime-transitionStart>=transitionSeconds;
        public bool Available=>IsSpawned&&!Occupied;
        public void RequestUse(){if(IsSpawned)UseRpc(Lease);}
        bool NearWorkstation(ulong sender)
        {
            if(!NetworkManager.ConnectedClients.TryGetValue(sender,out var c)||c.PlayerObject==null||!c.PlayerObject.IsSpawned)return false;
            Vector3 physicalEye=c.PlayerObject.transform.position+Vector3.up*c.PlayerObject.GetComponent<FirstPersonController>().EyeHeight;
            // The configured viewing side keeps entry behind the desk, outside NPC circulation.
            return Vector3.Distance(physicalEye,screen.ClosestPoint(physicalEye))<=PlayerInteractor.MaxDistance&&
                Vector3.Dot(c.PlayerObject.transform.position-screen.bounds.center,-focus.forward)>.2f;
        }
        public bool Authorizes(ulong sender,uint lease)=>IsServer&&IsSpawned&&Occupied&&User==sender&&Lease==lease&&NearWorkstation(sender);
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        public void UseRpc(uint expectedLease,RpcParams rpcParams=default)
        {
            if(!IsServer||!IsSpawned||Occupied||Lease!=expectedLease)return;
            ulong sender=rpcParams.Receive.SenderClientId;
            if(!NetworkManager.ConnectedClients.TryGetValue(sender,out var actor)||actor.PlayerObject==null||actor.PlayerObject.GetComponent<TowelCarrier>()?.Incapacitated==true)return;
            if(!NearWorkstation(sender)||!ServiceInteractionValidation.CanReach(NetworkManager,sender,screen))return;
            seat.Value=new ComputerSeat{Occupied=true,User=sender,Lease=Lease+1};
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        public void ReleaseRpc(uint lease,RpcParams rpcParams=default)
        {if(IsServer&&Occupied&&User==rpcParams.Receive.SenderClientId&&Lease==lease)ReleaseOnServer();}
        internal void ReleaseOnServer(){if(IsServer&&IsSpawned)seat.Value=new ComputerSeat{Lease=Lease+1};}
        public override void OnNetworkSpawn()
        {seat.OnValueChanged+=Changed;if(IsServer){seat.Value=default;NetworkManager.OnClientDisconnectCallback+=Disconnected;}}
        protected override void OnNetworkSessionSynchronized(){if(Occupied&&User==NetworkManager.LocalClientId)OpenLocal();}
        public override void OnNetworkDespawn()
        {seat.OnValueChanged-=Changed;if(NetworkManager!=null)NetworkManager.OnClientDisconnectCallback-=Disconnected;RestoreImmediately(false);}
        void OnDisable()=>RestoreImmediately(false);
        void Disconnected(ulong id){if(Occupied&&User==id)ReleaseOnServer();}
        void Changed(ComputerSeat before,ComputerSeat after)
        {
            if(after.Occupied&&after.User==NetworkManager.LocalClientId)OpenLocal();
            else if(LocalOpen)BeginClose();
        }
        void Awake()=>registration=GetComponent<MotelGame.Registration.RegistrationPanel>();
        void OpenLocal()
        {
            if(LocalOpen)return;
            var player=NetworkManager.LocalClient?.PlayerObject;if(player==null)return;
            var user=player.GetComponent<PlayerInteractor>();if(!user.IsOwner)return;
            localUser=user;movement=player.GetComponent<FirstPersonController>();view=player.GetComponentInChildren<Camera>(true);
            savedPosition=view.transform.localPosition;savedRotation=view.transform.localRotation;
            savedFieldOfView=fromFieldOfView=view.fieldOfView;
            fromPosition=view.transform.position;fromRotation=view.transform.rotation;transitionStart=Time.unscaledTime;closing=false;
            if(registration!=null)registrationTab=1;
            user.MenuOpen=true;movement.InputSuspended=true;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        }
        public void CloseLocal()
        {
            if(!LocalOpen||closing)return;
            if(IsSpawned&&Occupied&&User==NetworkManager.LocalClientId)ReleaseRpc(Lease);
            BeginClose();
        }
        void BeginClose()
        {
            if(!LocalOpen||closing)return;
            closing=true;fromFieldOfView=view.fieldOfView;fromPosition=view.transform.position;fromRotation=view.transform.rotation;transitionStart=Time.unscaledTime;
        }
        void Update()
        {
            if(IsServer&&IsSpawned&&Occupied&&!NearWorkstation(User))ReleaseOnServer();
            if(!LocalOpen)return;
            var arriving=reception.WaitingNpc;
            if(arriving!=null&&(arriving!=seenGuest||arriving.State.Visit!=seenVisit))
            {seenGuest=arriving;seenVisit=arriving.State.Visit;if(registration!=null)registrationTab=1;}
            if(!localUser.IsSpawned||!localUser.IsOwner){RestoreImmediately(false);return;}
            if(!closing&&Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame){if(registration!=null&&registration.Enlarged)registration.ClosePhotograph();else if(guestView)guestView=false;else CloseLocal();}
        }
        void LateUpdate()
        {
            if(!LocalOpen||view==null)return;
            movement.InputSuspended=true;localUser.MenuOpen=true;
            float t=Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.unscaledTime-transitionStart)/transitionSeconds));
            var parent=view.transform.parent;
            Vector3 target=focus.position;Quaternion direction=focus.rotation;float fieldOfView=savedFieldOfView;
            if(!guestView&&TryGetDisplayFrame(out var center,out var normal,out var up,out var size))
            {
                // Look squarely at the physical screen. Keep the same monitor framing at any aspect ratio.
                float distance=Mathf.Max(.15f,Vector3.Distance(focus.position,center));
                target=center+normal*distance;direction=Quaternion.LookRotation(-normal,up);
                float halfExtent=Mathf.Max(size.y,size.x/Mathf.Max(.01f,view.aspect))*.5f;
                // Frame the monitor slightly above screen centre, leaving a subtitle band below it.
                target-=up*(halfExtent*.10f/.86f);
                fieldOfView=2*Mathf.Atan(halfExtent/(distance*.86f))*Mathf.Rad2Deg;
            }
            if(guestView&&reception.WaitingNpc!=null){target=parent.TransformPoint(savedPosition);direction=Quaternion.LookRotation(reception.WaitingNpc.transform.position+Vector3.up*1.55f-target);}
            view.transform.SetPositionAndRotation(Vector3.Lerp(fromPosition,closing?parent.TransformPoint(savedPosition):target,t),
                Quaternion.Slerp(fromRotation,closing?parent.rotation*savedRotation:direction,t));
            view.fieldOfView=Mathf.Lerp(fromFieldOfView,closing?savedFieldOfView:fieldOfView,t);
            if(closing&&t>=1)RestoreImmediately(true);
            else{Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        }
        void RestoreImmediately(bool capture)
        {
            if(view!=null){view.transform.localPosition=savedPosition;view.transform.localRotation=savedRotation;view.fieldOfView=savedFieldOfView;}
            if(localUser!=null)localUser.MenuOpen=false;
            if(movement!=null){movement.InputSuspended=false;if(capture)movement.ResumeAfterComputer();}
            guestView=false;registration?.ClosePhotograph();localUser=null;view=null;movement=null;closing=false;relocationSource=null;
        }
        void Styles()
        {
            if(background!=null)return;
            background=new Texture2D(1,1);background.SetPixel(0,0,new Color(.035f,.06f,.055f));background.Apply();
            labelStyle=new GUIStyle(GUI.skin.label){fontSize=15,wordWrap=true};labelStyle.normal.textColor=new Color(.73f,.85f,.7f);
            titleStyle=new GUIStyle(labelStyle){fontSize=20,fontStyle=FontStyle.Bold};
            buttonStyle=ComputerUiStyles.Button;
            boxStyle=new GUIStyle(GUI.skin.box){padding=new RectOffset(18,18,14,14)};boxStyle.normal.background=background;
#if UNITY_EDITOR
            boxStyle.normal.scaledBackgrounds=Array.Empty<Texture2D>();
#endif
        }
        const float DisplayWidth=800,DisplayHeight=640;

        bool TryGetDisplayFrame(out Vector3 center,out Vector3 normal,out Vector3 up,out Vector2 size)
        {
            center=normal=up=default;size=default;
            if(!(screen is BoxCollider box)||focus==null)return false;
            var t=box.transform;
            Vector3 x=t.TransformVector(Vector3.right*box.size.x);
            Vector3 y=t.TransformVector(Vector3.up*box.size.y);
            Vector3 z=t.TransformVector(Vector3.forward*box.size.z);
            // The thin collider axis is the screen depth; the other two are its visible face.
            Vector3 depth,width,height;
            if(x.magnitude<y.magnitude&&x.magnitude<z.magnitude){depth=x;width=z;height=y;}
            else if(z.magnitude<y.magnitude){depth=z;width=x;height=y;}
            else{depth=y;width=x;height=z;}
            if(width.magnitude<=0||height.magnitude<=0||depth.magnitude<=0)return false;
            center=t.TransformPoint(box.center);normal=depth.normalized;
            if(Vector3.Dot(focus.position-center,normal)<0)normal=-normal;
            center+=normal*depth.magnitude*.5f;up=height.normalized;size=new Vector2(width.magnitude,height.magnitude);
            return true;
        }

        bool TryGetDisplayRect(out Rect rect)
        {
            rect=default;
            if(view==null||!TryGetDisplayFrame(out var center,out var normal,out var up,out var size))return false;
            // Only display the interface once the camera is normal to the screen, never over an angled bezel.
            if(Vector3.Dot(view.transform.forward,-normal)<.9999f)return false;
            Vector3 right=Vector3.Cross(up,-normal);
            var bottomLeft=view.WorldToScreenPoint(center-right*size.x*.5f-up*size.y*.5f);
            var topRight=view.WorldToScreenPoint(center+right*size.x*.5f+up*size.y*.5f);
            if(bottomLeft.z<=view.nearClipPlane||topRight.z<=view.nearClipPlane)return false;
            rect=Rect.MinMaxRect(bottomLeft.x,Screen.height-topRight.y,topRight.x,Screen.height-bottomLeft.y);
            // Inset within the existing glass surface; never draw over the CRT housing.
            float inset=Mathf.Min(rect.width,rect.height)*.015f;
            rect=new Rect(rect.x+inset,rect.y+inset,rect.width-2*inset,rect.height-2*inset);
            return rect.width>1&&rect.height>1;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal static Action<ReceptionComputer> BeforeGuiForVerification;
#endif
        void OnGUI()
        {
            if(!Focused||!IsSpawned)return;
            Styles();
            Matrix4x4 previousMatrix=GUI.matrix;
            Color previousColor=GUI.color,previousContent=GUI.contentColor,previousBackground=GUI.backgroundColor;
            bool previousEnabled=GUI.enabled;
            try
            {
                GUI.color=GUI.contentColor=GUI.backgroundColor=Color.white;
                if(guestView&&reception.WaitingNpc!=null)
                {
                    // This is the return hint while looking away, not the computer's screen content.
                    float hintScale=Mathf.Min(Screen.width/800f,Screen.height/600f);
                    GUI.matrix=Matrix4x4.Scale(new Vector3(hintScale,hintScale,1));
                    if(GUI.Button(new Rect((Screen.width/hintScale-230)/2,Screen.height/hintScale-54,230,34),"COMPUTER VIEW [Esc]",buttonStyle))guestView=false;
                    return;
                }
                guestView=false;
                if(!TryGetDisplayRect(out var display))return;
                GUI.matrix=Matrix4x4.identity;
                GUI.DrawTexture(display,background);
                float scale=Mathf.Min(display.width/DisplayWidth,display.height/DisplayHeight);
                Vector2 origin=display.center-new Vector2(DisplayWidth,DisplayHeight)*scale*.5f;
                GUI.matrix=Matrix4x4.TRS(new Vector3(origin.x,origin.y,0),Quaternion.identity,new Vector3(scale,scale,1));
                // One transform for drawing and pointer input; all tabs and photograph popups share this clip.
                GUI.BeginGroup(new Rect(0,0,DisplayWidth,DisplayHeight));
                try{DrawComputer(DisplayWidth,DisplayHeight);}
                finally{GUI.EndGroup();}
            }
            finally
            {
                GUI.matrix=previousMatrix;GUI.enabled=previousEnabled;
                GUI.color=previousColor;GUI.contentColor=previousContent;GUI.backgroundColor=previousBackground;
            }
        }
        void DrawComputer(float w,float h)
        {
            registration?.BeginFrame();
            GUILayout.BeginArea(new Rect(0,0,w,h),boxStyle);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            BeforeGuiForVerification?.Invoke(this);
#endif
            GUILayout.Label("MOTEL / NIGHT DESK",titleStyle);
            GUILayout.Label($"{shift.ClockText}     {shift.State.Phase}     IN {shift.State.CheckedIn}   OUT {shift.State.CheckedOut}",labelStyle);
            var hunt=MotelGame.Horror.MimicHunt.Instance;
            if(hunt!=null&&!string.IsNullOrEmpty(hunt.Complaint))GUILayout.Label("GUEST REPORT / "+hunt.Complaint,labelStyle);
            var surveillance=FindFirstObjectByType<MotelGame.Horror.VentSurveillance>();
            if(registration!=null)
            {
                GUI.enabled=!registration.Enlarged;
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("RESERVATIONS",buttonStyle))registrationTab=1;
                if(GUILayout.Button("PARKING CAMERA",buttonStyle))registrationTab=3;
                if(GUILayout.Button("ROADWATCH",buttonStyle))registrationTab=2;
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if(!surveillanceOnly&&GUILayout.Button("Room operations / Shift",buttonStyle,GUILayout.Height(23)))registrationTab=0;
                if(GUILayout.Button("Underground activity",buttonStyle,GUILayout.Height(23)))registrationTab=4;
                GUI.enabled=!registration.Enlarged&&reception.WaitingNpc!=null;
                if(GUILayout.Button("Guest view",buttonStyle,GUILayout.Height(23)))guestView=true;
                GUI.enabled=!registration.Enlarged;GUILayout.EndHorizontal();
                if(registrationTab==1&&reception.WaitingNpc==null&&!surveillanceOnly)registrationTab=0;
                if(registrationTab!=0)
                {
                    if(registrationTab==1)registration.DrawReservations(w-40);
                    else if(registrationTab==2)registration.DrawRoadWatch(w-40);
                    else if(registrationTab==3)surveillance?.DrawParking(w-40,h-180);
                    else surveillance?.DrawVents(w-40,h-180);
                    if(GUILayout.Button("Return to motel [Esc]",buttonStyle))CloseLocal();
                    GUILayout.EndArea();GUI.enabled=true;registration.DrawPhotographOverlay(w,h);registration.DrawTooltip(w,h);return;
                }
            }
            else if(surveillanceOnly)
            {
                GUILayout.Label("VENTILATION MONITORING",titleStyle);
                if(surveillance!=null)surveillance.DrawFeeds(w-40,h-180);
                if(GUILayout.Button("Return to motel [Esc]",buttonStyle))CloseLocal();
                GUILayout.EndArea();return;
            }
            if(registration==null){GUILayout.BeginHorizontal();
            if(GUILayout.Button("Reception",buttonStyle)){cctv=false;baggage=false;}
            if(surveillance!=null&&GUILayout.Button("Vent CCTV",buttonStyle)){cctv=true;baggage=false;}
            var inspection=MotelGame.Inspection.BaggageInspection.Instance;
            if(inspection!=null&&GUILayout.Button("Inspection summary",buttonStyle)){baggage=true;cctv=false;}
            GUILayout.EndHorizontal();
            if(baggage&&inspection!=null){baggageScroll=GUILayout.BeginScrollView(baggageScroll);inspection.GetComponent<MotelGame.Inspection.AnalyzerPanel>().DrawComputerReview();GUILayout.EndScrollView();if(GUILayout.Button("Return to motel [Esc]",buttonStyle))CloseLocal();GUILayout.EndArea();return;}
            if(cctv&&surveillance!=null){surveillance.DrawFeeds(w-40,h-195);if(GUILayout.Button("Return to motel [Esc]",buttonStyle))CloseLocal();GUILayout.EndArea();return;}
            }
            scroll=GUILayout.BeginScrollView(scroll);
            if(!shift.IsRunning)
            {
                GUILayout.Label(shift.State.Phase==ShiftPhase.Finished?shift.SummaryText:"Start the 15-minute shift. Register arrivals, compare parked vehicles and check RoadWatch when needed.\nArrival and service workload adapts to staff count.",labelStyle);
                if(GUILayout.Button(shift.State.Phase==ShiftPhase.Finished?"Start Next Shift":"Start Shift",buttonStyle))shift.StartShiftRpc(shift.State.Number,Lease);
            }
            else
            {
                var incoming=reception.WaitingNpc;var outgoing=reception.CheckoutNpc;
                GUILayout.Label("RECEPTION — "+(outgoing!=null?$"{outgoing.GuestName} / Room {outgoing.Room.RoomNumber} — returning key":
                    incoming!=null?incoming.GuestName+" — ready for registration":"Waiting for the next customer to reach the desk"),labelStyle);
                GUILayout.Label($"Queue: {reception.QueueCount}/3",labelStyle);
                if(incoming!=null&&registration!=null&&GUILayout.Button("Open current reservation / Admit or Refuse",buttonStyle))registrationTab=1;
                if(outgoing!=null&&GUILayout.Button($"Accept key / Check out Room {outgoing.Room.RoomNumber}",buttonStyle))
                    reception.Checkout(Array.IndexOf(reception.Rooms,outgoing.Room));
                GUILayout.Space(8);GUILayout.Label("ROOMS / REGISTERED GUESTS",titleStyle);
                foreach(var room in reception.Rooms)
                {
                    GUILayout.Label($"{room.RoomNumber}   {room.AvailabilityText}   {((room.Occupant??room.ReservedGuest)==null?"—":(room.Occupant??room.ReservedGuest).GuestName)}",labelStyle);
                    if (room.State.Status == RoomStatus.Occupied && room.State.Faulted)
                    {
                        GUI.enabled = room.CanRelocate;
                        if (GUILayout.Button("Relocate Guest — Room " + room.RoomNumber, buttonStyle)) relocationSource = room;
                        GUI.enabled = true;
                        if (!room.CanRelocate) GUILayout.Label("Guest is already moving or waiting for checkout.", labelStyle);
                    }
                    if(incoming!=null&&registration==null)
                    {GUI.enabled=reception.CanRegister(room);if(GUILayout.Button("Assign Room "+room.RoomNumber,buttonStyle))reception.SelectRoom(Array.IndexOf(reception.Rooms,room));GUI.enabled=true;}
                }
                if (relocationSource != null)
                {
                    GUILayout.Space(8);
                    GUILayout.Label("MOVE FROM " + relocationSource.RoomNumber + " — choose destination", titleStyle);
                    bool any = false;
                    foreach (var destination in reception.Rooms)
                    {
                        if (destination == relocationSource) continue;
                        bool eligible = relocationSource.CanRelocate && destination.AvailableForCheckIn; any |= eligible;
                        GUI.enabled = eligible;
                        if (GUILayout.Button($"Room {destination.RoomNumber} — {destination.AvailabilityText}", buttonStyle))
                        { reception.Relocate(Array.IndexOf(reception.Rooms, relocationSource), Array.IndexOf(reception.Rooms, destination)); relocationSource = null; break; }
                        GUI.enabled = true;
                    }
                    GUI.enabled = true;
                    if (!any) GUILayout.Label("No eligible destination. Prepare a powered room or repair the source breaker.", labelStyle);
                    if (GUILayout.Button("Cancel relocation selection", buttonStyle)) relocationSource = null;
                }
            }
            GUILayout.EndScrollView();
            if(localUser.FeedbackVisible)GUILayout.Label(localUser.Feedback,labelStyle);
            if(GUILayout.Button("Return to motel [Esc]",buttonStyle))CloseLocal();
            GUILayout.EndArea();
        }
        public override void OnDestroy(){if(background!=null)Destroy(background);if(buttonBackground!=null)Destroy(buttonBackground);if(buttonHighlight!=null)Destroy(buttonHighlight);base.OnDestroy();}
        public bool ConfigurationValid()=>reception!=null&&shift!=null&&screen!=null&&focus!=null&&transitionSeconds>0;
    }
}
