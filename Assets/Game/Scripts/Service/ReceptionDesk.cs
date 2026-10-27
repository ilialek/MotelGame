using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using MotelGame.Interactions;

namespace MotelGame.Service
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ReceptionDesk : NetworkBehaviour
    {
        [SerializeField] ServiceRoom[] rooms;
        [SerializeField] GuestLifecycle[] guests;
        [SerializeField] GuestServiceRequests serviceRequests;
        [SerializeField] NightShiftController shift;
        [SerializeField] ReceptionComputer computer;
        [SerializeField] Transform[] queuePoints;
        [SerializeField, Min(1)] float arrivalPatience=120;
        readonly List<GuestLifecycle> queue=new();
        readonly Dictionary<GuestLifecycle,double> waitingSince=new();
        GuestLifecycle clearingGuest;
        int nextGuest;
        public Transform[] QueuePoints=>queuePoints;
        public int QueueCount=>System.Array.FindAll(guests,g=>g.IsSpawned&&g.State.QueuePosition>0).Length;
        [SerializeField] Collider counter;
        [SerializeField] GameObject waitingGuest;
        [SerializeField] TextMesh guestLabel;
        public ServiceRoom[] Rooms => rooms;
        public Collider Counter => counter;
        public GameObject WaitingGuest => waitingGuest;
        public bool MenuOpen => computer!=null&&computer.LocalOpen;
        public GuestLifecycle[] Guests => guests;
        public ServiceRoom OccupiedRoom => System.Array.Find(rooms,r=>r.IsSpawned && r.State.Status==RoomStatus.Occupied);
        public GuestLifecycle WaitingNpc => System.Array.Find(guests,g=>g.IsSpawned && g.State.QueuePosition==1 && g.AtQueuePosition && g.CanCheckIn);
        public GuestLifecycle CheckoutNpc => System.Array.Find(guests,g=>g.IsSpawned && g.State.QueuePosition==1 && g.AtQueuePosition && g.CanCheckoutFor(g.Room));
        public bool GuestWaiting => IsSpawned && WaitingNpc != null;
        public bool CanRegister(ServiceRoom room) => GuestWaiting && System.Array.IndexOf(rooms,room)>=0 && room.AvailableForCheckIn && (MotelGame.Inspection.BaggageInspection.Instance==null || MotelGame.Inspection.BaggageInspection.Instance.CanAdmit(WaitingNpc));
        public override void OnNetworkSpawn()
        {
            queue.Clear();waitingSince.Clear();clearingGuest=null;nextGuest=0;
            waitingGuest.SetActive(false);guestLabel.gameObject.SetActive(false);
        }
        void Update(){if(IsSpawned&&IsServer&&shift.IsRunning)ScheduleReception();}
        static bool Queued(GuestLifecycle guest)=>guest.Phase==GuestPhase.Arriving||guest.Phase==GuestPhase.WaitingForCheckIn||
            guest.Phase==GuestPhase.ReturningForCheckout||guest.Phase==GuestPhase.WaitingForCheckout;
        void UpdatePositions()
        {
            for(int i=0;i<queue.Count;i++)
                if(queue[i].State.QueuePosition!=i+1)queue[i].SetQueuePosition(queuePoints[i],(byte)(i+1));
        }
        void ScheduleReception()
        {
            double now=NetworkManager.ServerTime.Time;
            for(int i=queue.Count-1;i>=0;i--)
            {
                var guest=queue[i];
                if(!Queued(guest))
                {
                    if(i==0)clearingGuest=guest;
                    waitingSince.Remove(guest);queue.RemoveAt(i);continue;
                }
                if(!guest.State.Assigned&&guest.Phase==GuestPhase.WaitingForCheckIn)
                {
                    if(!waitingSince.ContainsKey(guest))waitingSince.Add(guest,now);
                    else if(now-waitingSince[guest]>=arrivalPatience)
                    {
                        guest.BeginLeaving();shift.RecordUnserved();
                        if(i==0)clearingGuest=guest;
                        waitingSince.Remove(guest);queue.RemoveAt(i);
                    }
                }
            }
            // Do not advance a follower into the departing customer's capsule.
            if(clearingGuest!=null&&clearingGuest.Visible&&Vector3.Distance(clearingGuest.transform.position,queuePoints[0].position)<1.05f)return;
            clearingGuest=null;
            bool full=!System.Array.Exists(rooms,r=>r.IsSpawned&&r.State.Status!=RoomStatus.Occupied&&!r.State.Reserved&&!r.State.Faulted);
            GuestLifecycle due=null;
            foreach(var guest in guests)if(guest.CheckoutDue&&(due==null||guest.State.StayEndsAt<due.State.StayEndsAt))due=guest;
            if(queue.Count<queuePoints.Length&&due!=null)
            {
                // With no vacant room, accepting a key must remain possible. Arrivals wait
                // in the two side positions while the returner physically walks to the front.
                int index=full&&queue.Count>0&&!queue[0].State.Assigned?0:queue.Count;
                queue.Insert(index,due);UpdatePositions();
                if(due.TryBeginCheckoutReturn())serviceRequests.ValidateSource();
                else queue.Remove(due);
            }
            if(full&&queue.Count>1&&!queue[0].State.Assigned)
            {
                int returning=queue.FindIndex(g=>g.State.Assigned);
                if(returning>0){var g=queue[returning];queue.RemoveAt(returning);queue.Insert(0,g);}
            }
            UpdatePositions();
            if(full||queue.Count>=queuePoints.Length||!shift.ArrivalDue)return;
            for(int i=0;i<guests.Length;i++)
            {
                int index=(nextGuest+i)%guests.Length;var guest=guests[index];
                if(guest.Phase!=GuestPhase.Absent)continue;
                guest.SetQueuePosition(queuePoints[queue.Count],(byte)(queue.Count+1));
                if(guest.StartArrival())
                {queue.Add(guest);nextGuest=(index+1)%guests.Length;shift.RecordArrival();return;}
                guest.SetQueuePosition(null,0);
            }
        }
        internal void TouchInspection(GuestLifecycle guest) { if(IsServer&&waitingSince.ContainsKey(guest))waitingSince[guest]=NetworkManager.ServerTime.Time; }
        internal void ResetOnServer()
        {
            if(!IsServer)return;queue.Clear();waitingSince.Clear();clearingGuest=null;nextGuest=0;
            foreach(var g in guests)g.ResetOnServer();
        }
        // Kept for existing interaction callers; the old counter UI no longer owns input/state.
        public void OpenMenu(){if(computer!=null)computer.RequestUse();}
        public void CloseMenu(){if(computer!=null)computer.CloseLocal();}

        public void SelectRoom(int index)
        {
            var waiting=WaitingNpc;
            if (index >= 0 && index < rooms.Length && waiting!=null)
                RequestCheckInRpc(index, rooms[index].State.Cycle,new NetworkObjectReference(waiting.NetworkObject),waiting.State.Visit,computer.Lease);
        }
        public void Checkout(int index)
        {
            var guest=CheckoutNpc;
            if (index >= 0 && index < rooms.Length && guest!=null && guest.Room==rooms[index])
                RequestCheckoutRpc(index, rooms[index].State.Cycle,new NetworkObjectReference(guest.NetworkObject),guest.State.Visit,computer.Lease);
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestCheckInRpc(int index, uint cycle, NetworkObjectReference expectedGuest = default, uint visit = 0, uint lease = 0, RpcParams rpcParams = default)
        {
            if (!IsServer || !IsSpawned) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            string message;
            if (!shift.IsRunning || !computer.Authorizes(sender,lease)) message = "Use the Reception computer during the shift.";
            else if (index < 0 || index >= rooms.Length) message = "Invalid room.";
            else if (!GuestWaiting || !expectedGuest.TryGet(out var selected,NetworkManager) || selected!=WaitingNpc.NetworkObject || WaitingNpc.State.Visit!=visit) message = "Waiting guest changed. Select again.";
            else if (MotelGame.Inspection.BaggageInspection.Instance!=null&&!MotelGame.Inspection.BaggageInspection.Instance.CanAdmit(WaitingNpc)) message = "Ask about the contents and weigh this guest's bag before admission.";
            else if (!WaitingNpc.TryCheckIn(rooms[index],cycle)) message = "Room unavailable. Refresh your selection.";
            else { MotelGame.Inspection.BaggageInspection.Instance?.RecordDecisionOnServer(rooms[index].Occupant,true); message = $"Guest checked into Room {rooms[index].RoomNumber}."; }
            FeedbackRpc(message, RpcTarget.Single(sender, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestCheckoutRpc(int index, uint cycle, NetworkObjectReference expectedGuest = default, uint visit = 0, uint lease = 0, RpcParams rpcParams = default)
        {
            if (!IsServer || !IsSpawned) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            string message;
            if (!shift.IsRunning || !computer.Authorizes(sender,lease)) message = "Use the Reception computer during the shift.";
            else if (index < 0 || index >= rooms.Length || CheckoutNpc==null ||
                !expectedGuest.TryGet(out var selected,NetworkManager) || selected!=CheckoutNpc.NetworkObject ||
                CheckoutNpc.State.Visit!=visit || !rooms[index].TryCheckout(cycle,CheckoutNpc))
                message = "Checkout unavailable. Guest or room changed; select the waiting guest again.";
            else message = $"Key returned — Room {rooms[index].RoomNumber} is Dirty. Guest departing.";
            FeedbackRpc(message, RpcTarget.Single(sender, RpcTargetUse.Temp));
        }
        public void RefuseWaitingGuest()
        {
            var guest=WaitingNpc;
            if(guest!=null)RequestRefuseRpc(new NetworkObjectReference(guest.NetworkObject),guest.State.Visit,computer.Lease);
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        public void RequestRefuseRpc(NetworkObjectReference expectedGuest,uint visit,uint lease,RpcParams rpc=default)
        {
            if(!IsServer||!IsSpawned||!shift.IsRunning||!computer.Authorizes(rpc.Receive.SenderClientId,lease)||
                !expectedGuest.TryGet(out var obj,NetworkManager)||WaitingNpc==null||obj!=WaitingNpc.NetworkObject||WaitingNpc.State.Visit!=visit)return;
            var guest=WaitingNpc;
            MotelGame.Inspection.BaggageInspection.Instance?.RecordDecisionOnServer(guest,false);
            guest.BeginLeaving();
            FeedbackRpc("Accommodation refused. The guest leaves with their baggage.",RpcTarget.Single(rpc.Receive.SenderClientId,RpcTargetUse.Temp));
        }
        public void Relocate(int sourceIndex, int destinationIndex)
        {
            var source = rooms[sourceIndex]; var guest = source.Occupant;
            if (guest != null) RequestRelocateRpc(sourceIndex, destinationIndex, source.State.Cycle, rooms[destinationIndex].State.Cycle,
                source.State.FaultId, new NetworkObjectReference(guest.NetworkObject), guest.State.Visit, computer.Lease);
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestRelocateRpc(int sourceIndex, int destinationIndex, uint sourceCycle, uint destinationCycle, uint faultId,
            NetworkObjectReference expectedGuest, uint visit, uint lease, RpcParams rpcParams = default)
        {
            if (!IsServer || !IsSpawned) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            string message;
            if (!shift.IsRunning || !computer.Authorizes(sender, lease)) message = "Use the Reception computer during the shift.";
            else if (sourceIndex < 0 || sourceIndex >= rooms.Length || destinationIndex < 0 || destinationIndex >= rooms.Length || sourceIndex == destinationIndex)
                message = "Select a different Ready room.";
            else
            {
                var source = rooms[sourceIndex]; var guest = source.Occupant;
                if (guest == null || !expectedGuest.TryGet(out var obj, NetworkManager) || obj != guest.NetworkObject || guest.State.Visit != visit ||
                    !guest.TryRelocate(rooms[destinationIndex], sourceCycle, destinationCycle, faultId))
                    message = "Relocation unavailable — guest, power fault or destination changed.";
                else
                {
                    serviceRequests.CancelForRelocation(source);
                    message = $"Guest relocating to Room {rooms[destinationIndex].RoomNumber}. Room {source.RoomNumber} still needs power repair and turnover.";
                }
            }
            FeedbackRpc(message, RpcTarget.Single(sender, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void FeedbackRpc(string message, RpcParams rpcParams = default)
        {
            NetworkManager.LocalClient?.PlayerObject?.GetComponent<PlayerInteractor>()?.ShowFeedback(message);
        }
        public bool ConfigurationValid()
        {
            if (shift==null||computer==null||queuePoints==null||queuePoints.Length!=3||System.Array.Exists(queuePoints,p=>p==null))return false;
            if (serviceRequests == null || rooms == null || rooms.Length == 0 || guests==null || guests.Length!=rooms.Length+1 || counter == null || waitingGuest == null || guestLabel == null) return false;
            for (int i = 0; i < rooms.Length; i++)
            {
                if (rooms[i] == null) return false;
                for (int j = 0; j < i; j++) if (rooms[j] == rooms[i] || rooms[j].RoomNumber == rooms[i].RoomNumber) return false;
            }
            for(int i=0;i<guests.Length;i++)
            {
                if(guests[i]==null)return false;
                for(int j=0;j<i;j++)if(guests[i]==guests[j])return false;
            }
            return true;
        }
    }
}
