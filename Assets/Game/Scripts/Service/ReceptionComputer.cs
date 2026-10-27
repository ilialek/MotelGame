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
        float transitionStart;
        bool closing;
        Vector2 scroll;
        bool cctv, baggage;
        Vector2 baggageScroll;
        ServiceRoom relocationSource;
        Texture2D background;
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
        void OpenLocal()
        {
            if(LocalOpen)return;
            var player=NetworkManager.LocalClient?.PlayerObject;if(player==null)return;
            var user=player.GetComponent<PlayerInteractor>();if(!user.IsOwner)return;
            localUser=user;movement=player.GetComponent<FirstPersonController>();view=player.GetComponentInChildren<Camera>(true);
            savedPosition=view.transform.localPosition;savedRotation=view.transform.localRotation;
            fromPosition=view.transform.position;fromRotation=view.transform.rotation;transitionStart=Time.unscaledTime;closing=false;
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
            closing=true;fromPosition=view.transform.position;fromRotation=view.transform.rotation;transitionStart=Time.unscaledTime;
        }
        void Update()
        {
            if(IsServer&&IsSpawned&&Occupied&&!NearWorkstation(User))ReleaseOnServer();
            if(!LocalOpen)return;
            if(!localUser.IsSpawned||!localUser.IsOwner){RestoreImmediately(false);return;}
            if(!closing&&Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame)CloseLocal();
        }
        void LateUpdate()
        {
            if(!LocalOpen||view==null)return;
            movement.InputSuspended=true;localUser.MenuOpen=true;
            float t=Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.unscaledTime-transitionStart)/transitionSeconds));
            var parent=view.transform.parent;
            view.transform.SetPositionAndRotation(Vector3.Lerp(fromPosition,closing?parent.TransformPoint(savedPosition):focus.position,t),
                Quaternion.Slerp(fromRotation,closing?parent.rotation*savedRotation:focus.rotation,t));
            if(closing&&t>=1)RestoreImmediately(true);
            else{Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        }
        void RestoreImmediately(bool capture)
        {
            if(view!=null){view.transform.localPosition=savedPosition;view.transform.localRotation=savedRotation;}
            if(localUser!=null)localUser.MenuOpen=false;
            if(movement!=null){movement.InputSuspended=false;if(capture)movement.ResumeAfterComputer();}
            localUser=null;view=null;movement=null;closing=false;relocationSource=null;
        }
        void Styles()
        {
            if(background!=null)return;
            background=new Texture2D(1,1);background.SetPixel(0,0,new Color(.035f,.06f,.055f));background.Apply();
            labelStyle=new GUIStyle(GUI.skin.label){fontSize=15,wordWrap=true};labelStyle.normal.textColor=new Color(.73f,.85f,.7f);
            titleStyle=new GUIStyle(labelStyle){fontSize=20,fontStyle=FontStyle.Bold};
            buttonStyle=new GUIStyle(GUI.skin.button){fontSize=15,fixedHeight=30};buttonStyle.normal.textColor=new Color(.9f,.82f,.57f);
            boxStyle=new GUIStyle(GUI.skin.box){padding=new RectOffset(18,18,14,14)};boxStyle.normal.background=background;
        }
        void OnGUI()
        {
            if(!Focused||!IsSpawned)return;Styles();
            float w=Mathf.Min(670,Screen.width-32),h=Mathf.Min(525,Screen.height-32);
            GUILayout.BeginArea(new Rect((Screen.width-w)/2,(Screen.height-h)/2,w,h),boxStyle);
            GUILayout.Label("MOTEL / NIGHT DESK",titleStyle);
            GUILayout.Label($"{shift.ClockText}     {shift.State.Phase}     IN {shift.State.CheckedIn}   OUT {shift.State.CheckedOut}",labelStyle);
            var hunt=MotelGame.Horror.MimicHunt.Instance;
            if(hunt!=null&&!string.IsNullOrEmpty(hunt.Complaint))GUILayout.Label("GUEST REPORT / "+hunt.Complaint,labelStyle);
            var surveillance=FindFirstObjectByType<MotelGame.Horror.VentSurveillance>();
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Reception",buttonStyle)){cctv=false;baggage=false;}
            if(surveillance!=null&&GUILayout.Button("Vent CCTV",buttonStyle)){cctv=true;baggage=false;}
            var inspection=MotelGame.Inspection.BaggageInspection.Instance;
            if(inspection!=null&&GUILayout.Button("Baggage",buttonStyle)){baggage=true;cctv=false;}
            GUILayout.EndHorizontal();
            if(baggage&&inspection!=null){baggageScroll=GUILayout.BeginScrollView(baggageScroll);inspection.GetComponent<MotelGame.Inspection.AnalyzerPanel>().DrawComputerReview();GUILayout.EndScrollView();if(GUILayout.Button("Return to motel [Esc]",buttonStyle))CloseLocal();GUILayout.EndArea();return;}
            if(cctv&&surveillance!=null){surveillance.DrawFeeds(w-40,h-195);if(GUILayout.Button("Return to motel [Esc]",buttonStyle))CloseLocal();GUILayout.EndArea();return;}
            scroll=GUILayout.BeginScrollView(scroll);
            if(!shift.IsRunning)
            {
                GUILayout.Label(shift.State.Phase==ShiftPhase.Finished?shift.SummaryText:"Prepare the motel together. Guests arrive after the shift starts.\n22:00 – 06:00",labelStyle);
                if(GUILayout.Button(shift.State.Phase==ShiftPhase.Finished?"Start Next Shift":"Start Shift",buttonStyle))shift.StartShiftRpc(shift.State.Number,Lease);
            }
            else
            {
                var incoming=reception.WaitingNpc;var outgoing=reception.CheckoutNpc;
                GUILayout.Label("RECEPTION — "+(outgoing!=null?$"{outgoing.GuestName} / Room {outgoing.Room.RoomNumber} — returning key":
                    incoming!=null?incoming.GuestName+" — inspect baggage before room assignment":"Waiting for the next customer to reach the desk"),labelStyle);
                GUILayout.Label($"Queue: {reception.QueueCount}/3",labelStyle);
                if(incoming!=null&&inspection!=null){if(GUILayout.Button("Inspect / review baggage and decide",buttonStyle)){baggage=true;cctv=false;}}
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
                    if(incoming!=null)
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
        public override void OnDestroy(){if(background!=null)Destroy(background);base.OnDestroy();}
        public bool ConfigurationValid()=>reception!=null&&shift!=null&&screen!=null&&focus!=null&&transitionSeconds>0;
    }
}
