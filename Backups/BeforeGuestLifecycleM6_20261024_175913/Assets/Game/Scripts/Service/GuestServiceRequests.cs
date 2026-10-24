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
        // Server deadline. Zero means no scheduled call; Completed never schedules another.
        public double CallAt;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref Cycle);
            serializer.SerializeValue(ref CallAt);
        }
        public bool Equals(GuestRequestSnapshot other) => Phase == other.Phase && Cycle == other.Cycle && CallAt == other.CallAt;
    }

    [RequireComponent(typeof(NetworkObject))]
    public sealed class GuestServiceRequests : NetworkBehaviour
    {
        [SerializeField] ServiceRoom room;
        [SerializeField, Min(1)] float callDelay = 15;
        [SerializeField] GuestServiceInteraction phone;
        [SerializeField] GuestServiceInteraction towelSupply;
        [SerializeField] GuestServiceInteraction delivery;
        [SerializeField] AudioSource ringtone;
        [SerializeField] TextMesh phoneLabel;
        readonly NetworkVariable<GuestRequestSnapshot> snapshot = new NetworkVariable<GuestRequestSnapshot>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public ServiceRoom Room => room;
        public GuestRequestSnapshot State => snapshot.Value;
        public GuestRequestPhase Phase => State.Phase;
        public float CallDelay => callDelay;
        public GuestServiceInteraction Phone => phone;
        public GuestServiceInteraction TowelSupply => towelSupply;
        public GuestServiceInteraction Delivery => delivery;
        public AudioSource Ringtone => ringtone;
        public TowelCarrier LocalCarrier => NetworkManager == null ? null :
            NetworkManager.LocalClient?.PlayerObject?.GetComponent<TowelCarrier>();

        public override void OnNetworkSpawn()
        {
            snapshot.OnValueChanged += Changed;
            room.StateChanged += RoomChanged;
            if (IsServer) SynchronizeRoom();
            ApplyPresentation();
        }
        public override void OnNetworkDespawn()
        {
            snapshot.OnValueChanged -= Changed;
            room.StateChanged -= RoomChanged;
            ringtone.Stop();
            phoneLabel.text = "PHONE";
        }
        void OnDisable() { if (ringtone != null) ringtone.Stop(); }
        void RoomChanged(RoomSnapshot before, RoomSnapshot after)
        {
            // Checkout cancellation is immediate within the M4 server transaction.
            if (IsServer && IsSpawned) SynchronizeRoom();
        }
        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer)
            {
                // Also handles scene NetworkObject spawn order, without relying on Start order.
                SynchronizeRoom();
                if (State.Phase == GuestRequestPhase.NoRequest && State.CallAt > 0 &&
                    NetworkManager.ServerTime.Time >= State.CallAt)
                {
                    var next = State; next.Phase = GuestRequestPhase.Ringing; next.CallAt = 0; snapshot.Value = next;
                }
            }
            if (Phase == GuestRequestPhase.Ringing)
                phoneLabel.color = Mathf.FloorToInt((float)NetworkManager.ServerTime.Time * 2) % 2 == 0 ? Color.white : new Color(1,.75f,.25f);
        }
        void SynchronizeRoom()
        {
            if (!room.IsSpawned) return;
            var current = State;
            if (room.State.Status != RoomStatus.Occupied || current.Cycle != room.State.Cycle)
            {
                var reset = new GuestRequestSnapshot { Cycle = room.State.Cycle };
                if (!current.Equals(reset)) ClearTowels();
                current = reset;
            }
            if (room.State.Status == RoomStatus.Occupied && current.Phase == GuestRequestPhase.NoRequest && current.CallAt == 0)
                current.CallAt = NetworkManager.ServerTime.Time + Mathf.Max(1, callDelay);
            if (!current.Equals(State)) snapshot.Value = current;
        }
        void ClearTowels()
        {
            foreach (var client in NetworkManager.ConnectedClientsList)
                client.PlayerObject?.GetComponent<TowelCarrier>()?.SetOnServer(false);
        }
        bool CurrentCycle(uint cycle) => room.IsSpawned && room.State.Status == RoomStatus.Occupied &&
            room.State.Cycle == cycle && State.Cycle == cycle;
        TowelCarrier SenderCarrier(ulong sender) => NetworkManager.ConnectedClients.TryGetValue(sender, out var client) ?
            client.PlayerObject?.GetComponent<TowelCarrier>() : null;
        bool Reach(ulong sender, GuestServiceInteraction target) =>
            ServiceInteractionValidation.CanReach(NetworkManager, sender, target.TargetCollider);
        void Reply(ulong sender, string message) => FeedbackRpc(message, RpcTarget.Single(sender, RpcTargetUse.Temp));

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void AnswerRpc(uint cycle, RpcParams rpcParams = default)
        {
            if (!IsServer || !IsSpawned) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!CurrentCycle(cycle) || Phase != GuestRequestPhase.Ringing)
                Reply(sender, "No incoming call. Your team may have already answered.");
            else if (!Reach(sender, phone)) Reply(sender, "Approach the Reception phone.");
            else
            {
                var next = State; next.Phase = GuestRequestPhase.PendingDelivery; snapshot.Value = next;
                Reply(sender, $"Room {room.RoomNumber}: Could you bring me a clean towel?");
            }
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void TakeTowelRpc(uint cycle, RpcParams rpcParams = default)
        {
            if (!IsServer || !IsSpawned) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            var carrier = SenderCarrier(sender);
            if (!CurrentCycle(cycle) || Phase != GuestRequestPhase.PendingDelivery) Reply(sender, "No pending towel request.");
            else if (carrier == null || carrier.HasTowel) Reply(sender, "You can carry one towel at a time.");
            else if (!Reach(sender, towelSupply)) Reply(sender, "Use the towel shelf in Utility.");
            else { carrier.SetOnServer(true); Reply(sender, $"Clean towel collected — deliver to Room {room.RoomNumber}."); }
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void DeliverRpc(NetworkObjectReference destination, uint cycle, RpcParams rpcParams = default)
        {
            if (!IsServer || !IsSpawned) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            var carrier = SenderCarrier(sender);
            if (!CurrentCycle(cycle) || Phase != GuestRequestPhase.PendingDelivery) Reply(sender, "This request is no longer pending.");
            else if (!destination.TryGet(out var target, NetworkManager) || target != room.NetworkObject || delivery.Room != room)
                Reply(sender, "That is not the requested room.");
            else if (carrier == null || !carrier.HasTowel) Reply(sender, "Collect a clean towel in Utility first.");
            else if (!Reach(sender, delivery)) Reply(sender, "Use the delivery point beside the room door.");
            else
            {
                // Sequential server RPC execution makes competing deliveries complete only once.
                var next = State; next.Phase = GuestRequestPhase.Completed; snapshot.Value = next;
                ClearTowels(); // Any spare towel is returned to supply; no unusable carried item remains.
                Reply(sender, $"Room {room.RoomNumber} — clean towel delivered. Request complete.");
            }
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void FeedbackRpc(string message, RpcParams rpcParams = default) =>
            NetworkManager.LocalClient?.PlayerObject?.GetComponent<PlayerInteractor>()?.ShowFeedback(message);

        void Changed(GuestRequestSnapshot before, GuestRequestSnapshot after) => ApplyPresentation();
        void ApplyPresentation()
        {
            bool ringing = IsSpawned && Phase == GuestRequestPhase.Ringing;
            if (ringing && !ringtone.isPlaying) ringtone.Play();
            else if (!ringing) ringtone.Stop();
            phoneLabel.text = ringing ? "PHONE RINGING\n[E] Answer" : "PHONE\nReception";
            phoneLabel.color = new Color(.9f,.86f,.7f);
        }
        void OnGUI()
        {
            var carrier = LocalCarrier;
            if (!IsSpawned || carrier == null || Phase == GuestRequestPhase.NoRequest ||
                carrier.GetComponent<PlayerInteractor>().MenuOpen) return;
            float width = Mathf.Min(345, Screen.width - 24);
            GUILayout.BeginArea(new Rect(Screen.width - width - 12, 12, width, 145), "GUEST REQUEST", GUI.skin.window);
            if (Phase == GuestRequestPhase.Ringing)
            {
                GUILayout.Label("Phone ringing at Reception");
                GUILayout.Label("Approach the desk phone and press E.");
            }
            else
            {
                GUILayout.Label($"Room {room.RoomNumber} — Clean Towel — {(Phase == GuestRequestPhase.Completed ? "Complete" : "Pending Delivery")}");
                if (Phase == GuestRequestPhase.PendingDelivery)
                {
                    GUILayout.Label($"Room {room.RoomNumber}: Could you bring me a clean towel?");
                    GUILayout.Label(carrier.HasTowel ? "Carrying: Clean Towel\nDeliver beside the guest room door [E]." :
                        "Collect a towel from the Utility shelf [E].\nUtility entrance is behind Reception.");
                }
                else GUILayout.Label("Delivered. The guest remains checked in.");
            }
            GUILayout.EndArea();
        }
        public bool ConfigurationValid()
        {
            return room != null && phone != null && towelSupply != null && delivery != null && delivery.Room == room &&
                phone.Service == this && towelSupply.Service == this && delivery.Service == this &&
                phone.Action == GuestServiceAction.AnswerPhone && towelSupply.Action == GuestServiceAction.TakeTowel &&
                delivery.Action == GuestServiceAction.DeliverTowel && phone.TargetCollider != null &&
                towelSupply.TargetCollider != null && delivery.TargetCollider != null && ringtone != null &&
                ringtone.clip != null && ringtone.loop && phoneLabel != null && callDelay >= 1;
        }
    }
}
