using System;
using Unity.Netcode;
using UnityEngine;
using MotelGame.Interactions;

namespace MotelGame.Service
{
    public enum GarbagePhase : byte { Collecting, AwaitingPickup, Carried, Disposed }
    public enum RoomStatus : byte { Dirty, Ready, Occupied }

    // One atomic snapshot: status and visuals can never describe different service cycles.
    public struct RoomSnapshot : INetworkSerializable, IEquatable<RoomSnapshot>
    {
        public RoomStatus Status;
        public GarbagePhase Bag;
        public ulong BagCarrier;
        public bool Faulted, ElectricalReported, Reserved;
        public uint FaultId;
        public NetworkObjectReference Reservation;
        public bool BedMade;
        public uint RemovedTrash;
        public uint Cycle;
        public NetworkObjectReference Guest;
        public bool TowelRequested;
        public double RequestEligibleAt;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Bag); serializer.SerializeValue(ref BagCarrier);
            serializer.SerializeValue(ref Faulted); serializer.SerializeValue(ref FaultId);
            serializer.SerializeValue(ref ElectricalReported); serializer.SerializeValue(ref Reserved); serializer.SerializeValue(ref Reservation);
            serializer.SerializeValue(ref Status);
            serializer.SerializeValue(ref BedMade);
            serializer.SerializeValue(ref RemovedTrash);
            serializer.SerializeValue(ref Cycle);
            serializer.SerializeValue(ref Guest);
            serializer.SerializeValue(ref TowelRequested);
            serializer.SerializeValue(ref RequestEligibleAt);
        }
        public bool Equals(RoomSnapshot other) => Bag == other.Bag && BagCarrier == other.BagCarrier && Faulted == other.Faulted && FaultId == other.FaultId &&
            ElectricalReported == other.ElectricalReported && Reserved == other.Reserved && Reservation.Equals(other.Reservation) && Status == other.Status && BedMade == other.BedMade &&
            RemovedTrash == other.RemovedTrash && Cycle == other.Cycle && Guest.Equals(other.Guest) &&
            TowelRequested == other.TowelRequested && RequestEligibleAt == other.RequestEligibleAt;
    }

    [RequireComponent(typeof(NetworkObject))]
    public sealed class ServiceRoom : NetworkBehaviour
    {
        [SerializeField] NightShiftController shift;
        [SerializeField] RoomOperationsInteraction bagPickup;
        [SerializeField] RoomOperationsInteraction breaker;
        [SerializeField] Light[] controlledLights;
        public RoomOperationsInteraction BagPickup => bagPickup;
        public RoomOperationsInteraction Breaker => breaker;
        public Light[] ControlledLights => controlledLights;
        public bool AcceptingWork => IsSpawned && (shift == null || shift.IsRunning);
        public bool CanRelocate => AcceptingWork && State.Faulted && Occupant != null && Occupant.IsInRoomFor(this);
        public string AvailabilityText => State.Reserved ? "Reserved — guest relocating" : State.Faulted ? "POWER FAULT — unavailable" : State.Status.ToString();
        [SerializeField] string roomNumber = "101";
        [SerializeField] CleaningSpot bed;
        [SerializeField] CleaningSpot[] trash;
        [SerializeField] TextMesh[] statusSigns;
        [SerializeField] GameObject guestInRoom;
        [SerializeField] NetworkDoor door;
        [SerializeField] Transform doorOutside;
        [SerializeField] Transform doorInside;
        [SerializeField] Transform guestIdle;
        [SerializeField] GuestServiceInteraction delivery;
        [SerializeField] RoomTelevision television;
        public RoomTelevision Television=>television;
        readonly NetworkVariable<RoomSnapshot> snapshot = new NetworkVariable<RoomSnapshot>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public event Action<RoomSnapshot, RoomSnapshot> StateChanged;
        public string RoomNumber => roomNumber;
        public RoomSnapshot State => snapshot.Value;
        public CleaningSpot Bed => bed;
        public CleaningSpot[] Trash => trash;
        public GameObject GuestInRoom => guestInRoom;
        public NetworkDoor Door => door;
        public Transform DoorOutside => doorOutside;
        public Transform DoorInside => doorInside;
        public Transform GuestIdle => guestIdle;
        public GuestServiceInteraction Delivery => delivery;
        public GuestLifecycle Occupant => IsSpawned && State.Status == RoomStatus.Occupied &&
            State.Guest.TryGet(out var obj, NetworkManager) ? obj.GetComponent<GuestLifecycle>() : null;
        public GuestLifecycle ReservedGuest => IsSpawned && State.Reserved && State.Reservation.TryGet(out var obj, NetworkManager) ? obj.GetComponent<GuestLifecycle>() : null;
        // Only a fully cleaned, powered, unreserved room is available for the next stay.
        public bool AvailableForCheckIn => AcceptingWork && State.Status == RoomStatus.Ready && !State.Faulted && !State.Reserved;
        public int TrashRemovedCount
        {
            get { int n = 0; for (int i = 0; i < trash.Length; i++) if (IsCleaned(i)) n++; return n; }
        }
        uint AllTrashMask => (1u << trash.Length) - 1u;
        // -1 identifies the bed; non-negative indices are configured trash spots.
        public bool IsCleaned(int index) => index == -1 ? State.BedMade :
            index >= 0 && index < trash.Length && (State.RemovedTrash & (1u << index)) != 0;
        public bool CanClean(int index) => (shift==null||shift.IsRunning) && IsSpawned && State.Status == RoomStatus.Dirty && !IsCleaned(index);

        public override void OnNetworkSpawn()
        {
            if (IsServer) { snapshot.Value = new RoomSnapshot { Status = RoomStatus.Dirty }; NetworkManager.OnClientDisconnectCallback += Disconnected; }
            snapshot.OnValueChanged += Changed;
            ApplyVisuals();
        }
        public override void OnNetworkDespawn()
        {
            snapshot.OnValueChanged -= Changed;
            if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= Disconnected;
            if (controlledLights != null) foreach (var light in controlledLights) if (light != null) light.enabled = true;
        }
        void Disconnected(ulong id)
        {
            if (!IsServer || !IsSpawned || State.Bag != GarbagePhase.Carried || State.BagCarrier != id) return;
            var v = State; v.Bag = GarbagePhase.AwaitingPickup; v.BagCarrier = 0; snapshot.Value = v;
        }
        void Changed(RoomSnapshot before, RoomSnapshot after)
        {
            ApplyVisuals();
            StateChanged?.Invoke(before, after);
        }
        void ApplyVisuals()
        {
            if (controlledLights != null) foreach (var light in controlledLights) if (light != null) light.enabled = !State.Faulted;
            bed.ApplyCleaned(State.BedMade);
            for (int i = 0; i < trash.Length; i++) trash[i].ApplyCleaned(IsCleaned(i));
            if (guestInRoom != null) guestInRoom.SetActive(false);
            string label = $"ROOM {roomNumber} — {State.Status.ToString().ToUpperInvariant()}\n" +
                $"Bed: {(State.BedMade ? "made" : "unmade")}   Trash: {TrashRemovedCount}/{trash.Length}";
            label += $"\nTrash bag: {State.Bag}" + (State.Faulted ? " / POWER FAULT" : "") + (State.Reserved ? " / RESERVED" : "");
            foreach (var sign in statusSigns) sign.text = label;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestCleanRpc(int index, uint cycle, RpcParams rpcParams = default)
        {
            if (!IsServer || !IsSpawned) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            CleaningSpot spot = index == -1 ? bed : index >= 0 && index < trash.Length ? trash[index] : null;
            string message;
            if (shift!=null&&!shift.IsRunning) message = "Start a night shift at the Reception computer.";
            else if (spot == null) message = "Invalid cleaning target.";
            else if (cycle != State.Cycle) message = "Room changed. Please interact again.";
            else if (State.Status != RoomStatus.Dirty) message = "Room is not available for cleaning.";
            else if (IsCleaned(index)) message = "Already cleaned by your team.";
            else if (!ServiceInteractionValidation.CanReach(NetworkManager, sender, spot.TargetCollider))
                message = "Move closer and keep the object in view.";
            else
            {
                var next = State;
                if (index == -1) next.BedMade = true; else next.RemovedTrash |= 1u << index;
                if (next.RemovedTrash == AllTrashMask && next.Bag == GarbagePhase.Collecting) next.Bag = GarbagePhase.AwaitingPickup;
                if (next.BedMade && next.Bag == GarbagePhase.Disposed) next.Status = RoomStatus.Ready;
                snapshot.Value = next;
                message = next.Status == RoomStatus.Ready ? $"Room {roomNumber} is Ready for check-in." :
                    index == -1 ? "Bed made." : "Trash collected — take the filled bag to the dumpster.";
            }
            FeedbackRpc(message, RpcTarget.Single(sender, RpcTargetUse.Temp));
        }

        // Called only by the validated reception transaction on the server.
        internal bool TryCheckIn(uint cycle, GuestLifecycle guest)
        {
            if ((shift!=null&&!shift.IsRunning) || !IsServer || !IsSpawned || guest == null || !guest.IsSpawned || !guest.CanCheckIn ||
                State.Cycle != cycle || !AvailableForCheckIn) return false;
            var next = State; next.Status = RoomStatus.Occupied;
            next.Guest = new NetworkObjectReference(guest.NetworkObject); snapshot.Value = next; shift?.RecordCheckIn(); return true;
        }
        internal void ScheduleTowelRequest(double at)
        {
            if (!IsServer || State.Status != RoomStatus.Occupied || State.TowelRequested || State.RequestEligibleAt > 0) return;
            var next = State; next.RequestEligibleAt = at; snapshot.Value = next;
        }
        internal void MarkTowelRequested()
        {
            if (!IsServer || State.Status != RoomStatus.Occupied) return;
            var next = State; next.TowelRequested = true; snapshot.Value = next;
        }
        internal bool TryCheckout(uint cycle, GuestLifecycle guest)
        {
            if ((shift!=null&&!shift.IsRunning) || !IsServer || !IsSpawned || State.Cycle != cycle || State.Status != RoomStatus.Occupied ||
                guest == null || !guest.CanCheckoutFor(this)) return false;
            snapshot.Value = NextDirty(true);
            shift?.RecordCheckout();
            return true;
        }
        RoomSnapshot NextDirty(bool preserveFault) => new RoomSnapshot
        {
            Status = RoomStatus.Dirty, Cycle = State.Cycle + 1,
            Faulted = preserveFault && State.Faulted, FaultId = preserveFault ? State.FaultId : State.FaultId + 1
        };
        internal void PrepareForHuntDebug()
        { if(IsServer&&AcceptingWork&&State.Status!=RoomStatus.Occupied) snapshot.Value=new RoomSnapshot{Status=RoomStatus.Ready,Cycle=State.Cycle+1,BedMade=true,RemovedTrash=AllTrashMask,Bag=GarbagePhase.Disposed}; }
        internal void ResetOnServer()
        {
            if (!IsServer || !IsSpawned) return;
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                var carrier = client.PlayerObject?.GetComponent<TowelCarrier>();
                if (carrier != null && carrier.BagRoom == this) carrier.ClearOnServer();
            }
            snapshot.Value = NextDirty(false);
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void PickUpBagRpc(uint cycle, RpcParams rpcParams = default)
        {
            if (!IsServer || !IsSpawned) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            var carrier = NetworkManager.ConnectedClients.TryGetValue(sender, out var client) ? client.PlayerObject?.GetComponent<TowelCarrier>() : null;
            string message;
            if (!AcceptingWork || cycle != State.Cycle || State.Status != RoomStatus.Dirty || State.Bag != GarbagePhase.AwaitingPickup)
                message = "No filled bag available for this cleaning cycle.";
            else if (carrier == null || carrier.HasItem) message = "Already carrying a service item — finish that delivery first.";
            else if (!ServiceInteractionValidation.CanReach(NetworkManager, sender, bagPickup.TargetCollider)) message = "Approach the filled bag.";
            else if (!carrier.TryCarryBag(this)) message = "Your hands are occupied.";
            else
            {
                var v = State; v.Bag = GarbagePhase.Carried; v.BagCarrier = sender; snapshot.Value = v;
                message = $"Carrying Room {roomNumber} trash — take it to the outdoor dumpster.";
            }
            FeedbackRpc(message, RpcTarget.Single(sender, RpcTargetUse.Temp));
        }
        internal bool TryDisposeBag(TowelCarrier carrier, uint cycle)
        {
            if (!IsServer || !AcceptingWork || State.Cycle != cycle || State.Status != RoomStatus.Dirty ||
                State.Bag != GarbagePhase.Carried || State.BagCarrier != carrier.OwnerClientId || carrier.BagRoom != this || carrier.State.Cycle != cycle) return false;
            carrier.ClearOnServer();
            var v = State; v.Bag = GarbagePhase.Disposed; v.BagCarrier = 0;
            if (v.BedMade && v.RemovedTrash == AllTrashMask) v.Status = RoomStatus.Ready;
            snapshot.Value = v; shift?.RecordDisposal(); return true;
        }
        internal bool TryFaultOnServer()
        {
            if (!IsServer || !AcceptingWork || State.Faulted || State.Reserved) return false;
            var v = State; v.Faulted = true; v.FaultId++; v.ElectricalReported = false; snapshot.Value = v; return true;
        }
        internal void MarkElectricalReported()
        { if (IsServer) { var v = State; v.ElectricalReported = true; snapshot.Value = v; } }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ResetBreakerRpc(uint faultId, RpcParams rpcParams = default)
        {
            if (!IsServer || !IsSpawned) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            string message;
            if (!AcceptingWork || !State.Faulted || State.FaultId != faultId) message = "This breaker is already OK or the fault changed.";
            else if (!ServiceInteractionValidation.CanReach(NetworkManager, sender, breaker.TargetCollider)) message = "Approach the correct breaker in Utility.";
            else
            {
                var v = State; v.Faulted = false; snapshot.Value = v; shift?.RecordElectricalRepair();
                message = $"Room {roomNumber} — power restored.";
            }
            FeedbackRpc(message, RpcTarget.Single(sender, RpcTargetUse.Temp));
        }
        internal bool TryReserve(GuestLifecycle guest, uint cycle)
        {
            if (!IsServer || !AvailableForCheckIn || State.Cycle != cycle || guest == null) return false;
            var v = State; v.Reserved = true; v.Reservation = new NetworkObjectReference(guest.NetworkObject); snapshot.Value = v; return true;
        }
        internal void CancelReservation(GuestLifecycle guest)
        {
            if (!IsServer || !State.Reserved || !State.Reservation.Equals(new NetworkObjectReference(guest.NetworkObject))) return;
            var v = State; v.Reserved = false; v.Reservation = default; snapshot.Value = v;
        }
        internal bool CompleteRelocation(GuestLifecycle guest, uint cycle, bool requestUsed)
        {
            if (!IsServer || !AcceptingWork || !State.Reserved || State.Cycle != cycle || State.Status != RoomStatus.Ready ||
                !State.Reservation.Equals(new NetworkObjectReference(guest.NetworkObject))) return false;
            var v = State; v.Reserved = false; v.Reservation = default; v.Status = RoomStatus.Occupied;
            v.Guest = new NetworkObjectReference(guest.NetworkObject); v.TowelRequested = requestUsed; snapshot.Value = v;
            shift?.RecordRelocation(); return true;
        }
        internal void VacateForRelocation(GuestLifecycle guest)
        {
            if (IsServer && State.Status == RoomStatus.Occupied && Occupant == guest) snapshot.Value = NextDirty(true);
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void FeedbackRpc(string message, RpcParams rpcParams = default)
        {
            NetworkManager.LocalClient?.PlayerObject?.GetComponent<PlayerInteractor>()?.ShowFeedback(message);
        }

        public bool ConfigurationValid(out string error)
        {
            error = null;
            if (bagPickup == null || breaker == null || controlledLights == null || controlledLights.Length == 0 || System.Array.Exists(controlledLights, l => l == null) || string.IsNullOrWhiteSpace(roomNumber) || bed == null || trash == null || trash.Length < 1 || trash.Length > 31 ||
                door == null || doorOutside == null || doorInside == null || guestIdle == null || delivery == null || delivery.Room != this || statusSigns == null || statusSigns.Length == 0) error = "Missing room configuration (1–31 trash objects required).";
            else if (!bed.Matches(this, -1)) error = "Bed reference/index mismatch.";
            else
            {
                for (int i = 0; i < trash.Length; i++)
                    if (trash[i] == null || !trash[i].Matches(this, i)) { error = "Trash reference/index mismatch."; break; }
                foreach (var sign in statusSigns) if (sign == null) error = "Missing status sign.";
            }
            return error == null;
        }
    }
}
