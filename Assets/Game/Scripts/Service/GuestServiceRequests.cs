using System;
using Unity.Netcode;
using UnityEngine;
using MotelGame.Interactions;

namespace MotelGame.Service
{
    public enum GuestRequestPhase : byte { NoRequest, Ringing, PendingDelivery, Completed }
    public struct GuestRequestSnapshot : INetworkSerializable, IEquatable<GuestRequestSnapshot>
    {
        public GuestRequestPhase Phase;
        public uint Cycle;
        public uint RequestId;
        public NetworkObjectReference SourceRoom;
        public double CallAt;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Phase); serializer.SerializeValue(ref Cycle);
            serializer.SerializeValue(ref RequestId); serializer.SerializeValue(ref SourceRoom); serializer.SerializeValue(ref CallAt);
        }
        public bool Equals(GuestRequestSnapshot other) => Phase==other.Phase && Cycle==other.Cycle &&
            RequestId==other.RequestId && SourceRoom.Equals(other.SourceRoom) && CallAt==other.CallAt;
    }

    // One shared phone/service request. Per-stay eligibility belongs to each room snapshot.
    [RequireComponent(typeof(NetworkObject))]
    public sealed class GuestServiceRequests : NetworkBehaviour
    {
        [SerializeField] ServiceRoom[] rooms;
        [SerializeField, Min(1)] float callDelay = 15;
        [SerializeField, Min(1)] float betweenRequestsDelay = 8;
        [SerializeField] GuestServiceInteraction phone;
        [SerializeField] GuestServiceInteraction towelSupply;
        [SerializeField] AudioSource ringtone;
        [SerializeField] TextMesh phoneLabel;
        readonly NetworkVariable<GuestRequestSnapshot> snapshot = new NetworkVariable<GuestRequestSnapshot>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        double nextSelection;
        int nextRoom;

        public ServiceRoom[] Rooms => rooms;
        public ServiceRoom Room => IsSpawned && Phase!=GuestRequestPhase.NoRequest && State.SourceRoom.TryGet(out var obj,NetworkManager)
            ? obj.GetComponent<ServiceRoom>() : null;
        public GuestRequestSnapshot State => snapshot.Value;
        public GuestRequestPhase Phase => State.Phase;
        public float CallDelay => callDelay;
        public float BetweenRequestsDelay => betweenRequestsDelay;
        public GuestServiceInteraction Phone => phone;
        public GuestServiceInteraction TowelSupply => towelSupply;
        public GuestServiceInteraction Delivery => Room == null ? null : Room.Delivery;
        public AudioSource Ringtone => ringtone;
        public TowelCarrier LocalCarrier => NetworkManager == null ? null :
            NetworkManager.LocalClient?.PlayerObject?.GetComponent<TowelCarrier>();

        public override void OnNetworkSpawn()
        {
            snapshot.OnValueChanged+=Changed;
            foreach(var room in rooms)room.StateChanged+=RoomChanged;
            if(IsServer){snapshot.Value=default;nextSelection=0;nextRoom=0;}
            ApplyPresentation();
        }
        public override void OnNetworkDespawn()
        {
            snapshot.OnValueChanged-=Changed;
            foreach(var room in rooms)room.StateChanged-=RoomChanged;
            ringtone.Stop();phoneLabel.text="PHONE";
        }
        void OnDisable(){if(ringtone!=null)ringtone.Stop();}
        void RoomChanged(RoomSnapshot before,RoomSnapshot after)
        {if(IsServer && IsSpawned)ValidateSource();}
        void ValidateSource()
        {
            if(Phase==GuestRequestPhase.NoRequest)return;
            var source=Room;
            if(source!=null && source.IsSpawned && source.State.Status==RoomStatus.Occupied && source.State.Cycle==State.Cycle)return;
            // Only checkout of the request's own room cancels it; unrelated rooms cannot
            // clear the shared phone or carried towels.
            ClearTowels();snapshot.Value=new GuestRequestSnapshot{RequestId=State.RequestId};
            nextSelection=NetworkManager.ServerTime.Time+betweenRequestsDelay;
        }
        void Update()
        {
            if(!IsSpawned)return;
            if(IsServer)
            {
                ValidateSource();double now=NetworkManager.ServerTime.Time;
                foreach(var room in rooms)
                    if(room.IsSpawned && room.Occupant!=null && room.Occupant.IsInRoomFor(room))
                        room.ScheduleTowelRequest(now+callDelay);
                if(Phase!=GuestRequestPhase.Ringing && Phase!=GuestRequestPhase.PendingDelivery && now>=nextSelection)
                {
                    // Bounded round-robin selection, no request queue or parallel ringing lines.
                    for(int i=0;i<rooms.Length;i++)
                    {
                        int index=(nextRoom+i)%rooms.Length;var candidate=rooms[index];
                        if(!candidate.IsSpawned || candidate.State.Status!=RoomStatus.Occupied || candidate.State.TowelRequested ||
                            candidate.State.RequestEligibleAt<=0 || now<candidate.State.RequestEligibleAt ||
                            candidate.Occupant==null || !candidate.Occupant.IsInRoomFor(candidate))continue;
                        candidate.MarkTowelRequested();
                        snapshot.Value=new GuestRequestSnapshot{Phase=GuestRequestPhase.Ringing,Cycle=candidate.State.Cycle,
                            RequestId=State.RequestId+1,SourceRoom=new NetworkObjectReference(candidate.NetworkObject)};
                        nextRoom=(index+1)%rooms.Length;break;
                    }
                }
            }
            if(Phase==GuestRequestPhase.Ringing)
                phoneLabel.color=Mathf.FloorToInt((float)NetworkManager.ServerTime.Time*2)%2==0?Color.white:new Color(1,.75f,.25f);
        }
        void ClearTowels()
        {foreach(var client in NetworkManager.ConnectedClientsList)client.PlayerObject?.GetComponent<TowelCarrier>()?.SetOnServer(false);}
        bool CurrentRequest(uint requestId) => requestId==State.RequestId && Room!=null && Room.IsSpawned &&
            Room.State.Status==RoomStatus.Occupied && Room.State.Cycle==State.Cycle;
        TowelCarrier SenderCarrier(ulong sender) => NetworkManager.ConnectedClients.TryGetValue(sender,out var client)?
            client.PlayerObject?.GetComponent<TowelCarrier>():null;
        bool Reach(ulong sender,GuestServiceInteraction target) => target!=null && ServiceInteractionValidation.CanReach(NetworkManager,sender,target.TargetCollider);
        void Reply(ulong sender,string message)=>FeedbackRpc(message,RpcTarget.Single(sender,RpcTargetUse.Temp));

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void AnswerRpc(uint requestId,RpcParams rpcParams=default)
        {
            if(!IsServer || !IsSpawned)return;
            ulong sender=rpcParams.Receive.SenderClientId;
            if(!CurrentRequest(requestId) || Phase!=GuestRequestPhase.Ringing)Reply(sender,"No incoming call. Your team may have already answered.");
            else if(!Reach(sender,phone))Reply(sender,"Approach the Reception phone.");
            else {var next=State;next.Phase=GuestRequestPhase.PendingDelivery;snapshot.Value=next;Reply(sender,$"Room {Room.RoomNumber}: Could you bring me a clean towel?");}
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void TakeTowelRpc(uint requestId,RpcParams rpcParams=default)
        {
            if(!IsServer || !IsSpawned)return;
            ulong sender=rpcParams.Receive.SenderClientId;var carrier=SenderCarrier(sender);
            if(!CurrentRequest(requestId) || Phase!=GuestRequestPhase.PendingDelivery)Reply(sender,"No pending towel request.");
            else if(carrier==null || carrier.HasTowel)Reply(sender,"You can carry one towel at a time.");
            else if(!Reach(sender,towelSupply))Reply(sender,"Use the towel shelf in Utility.");
            else {carrier.SetOnServer(true);Reply(sender,$"Clean towel collected — deliver to Room {Room.RoomNumber}.");}
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void DeliverRpc(NetworkObjectReference destination,uint requestId,RpcParams rpcParams=default)
        {
            if(!IsServer || !IsSpawned)return;
            ulong sender=rpcParams.Receive.SenderClientId;var carrier=SenderCarrier(sender);
            if(!CurrentRequest(requestId) || Phase!=GuestRequestPhase.PendingDelivery)Reply(sender,"This request is no longer pending.");
            else if(!destination.TryGet(out var target,NetworkManager) || target!=Room.NetworkObject)Reply(sender,"That is not the requested room.");
            else if(carrier==null || !carrier.HasTowel)Reply(sender,"Collect a clean towel in Utility first.");
            else if(!Reach(sender,Delivery))Reply(sender,"Use the delivery point beside the requested room.");
            else
            {
                var next=State;next.Phase=GuestRequestPhase.Completed;snapshot.Value=next;
                ClearTowels();nextSelection=NetworkManager.ServerTime.Time+betweenRequestsDelay;
                Reply(sender,$"Room {Room.RoomNumber} — clean towel delivered. Request complete.");
            }
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void FeedbackRpc(string message,RpcParams rpcParams=default)=>NetworkManager.LocalClient?.PlayerObject?.GetComponent<PlayerInteractor>()?.ShowFeedback(message);
        void Changed(GuestRequestSnapshot before,GuestRequestSnapshot after)=>ApplyPresentation();
        void ApplyPresentation()
        {
            bool ringing=IsSpawned && Phase==GuestRequestPhase.Ringing;
            if(ringing && !ringtone.isPlaying)ringtone.Play();else if(!ringing)ringtone.Stop();
            phoneLabel.text=ringing?$"PHONE RINGING\nRoom {Room?.RoomNumber} — [E] Answer":"PHONE\nReception";
            phoneLabel.color=new Color(.9f,.86f,.7f);
        }
        void OnGUI()
        {
            var carrier=LocalCarrier;
            if(!IsSpawned || carrier==null || Room==null || carrier.GetComponent<PlayerInteractor>().MenuOpen)return;
            float width=Mathf.Min(345,Screen.width-24);
            GUILayout.BeginArea(new Rect(Screen.width-width-12,12,width,145),"GUEST REQUEST",GUI.skin.window);
            GUILayout.Label($"Room {Room.RoomNumber} — Clean Towel — {Phase}");
            if(Phase==GuestRequestPhase.Ringing)GUILayout.Label("Phone ringing at Reception\nApproach the phone and press E.");
            else if(Phase==GuestRequestPhase.PendingDelivery)
                GUILayout.Label(carrier.HasTowel?$"Carrying: Clean Towel\nDeliver beside Room {Room.RoomNumber} [E].":"Collect a towel in Utility [E].\nUtility entrance is behind Reception.");
            else GUILayout.Label("Delivered. The guest remains checked in.");
            GUILayout.EndArea();
        }
        public bool ConfigurationValid()
        {
            if(rooms==null || rooms.Length==0 || phone==null || towelSupply==null || phone.Service!=this || towelSupply.Service!=this ||
                phone.Action!=GuestServiceAction.AnswerPhone || towelSupply.Action!=GuestServiceAction.TakeTowel ||
                phone.TargetCollider==null || towelSupply.TargetCollider==null || ringtone==null || ringtone.clip==null || !ringtone.loop || phoneLabel==null)return false;
            for(int i=0;i<rooms.Length;i++)
            {
                var room=rooms[i];if(room==null || room.Delivery==null || room.Delivery.Service!=this || room.Delivery.Room!=room ||
                    room.Delivery.Action!=GuestServiceAction.DeliverTowel || room.Delivery.TargetCollider==null)return false;
                for(int j=0;j<i;j++)if(rooms[j]==room)return false;
            }
            return true;
        }
    }
}
