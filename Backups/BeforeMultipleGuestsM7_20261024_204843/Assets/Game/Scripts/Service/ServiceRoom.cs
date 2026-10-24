using System;
using Unity.Netcode;
using UnityEngine;
using MotelGame.Interactions;

namespace MotelGame.Service
{
    public enum RoomStatus : byte { Dirty, Ready, Occupied }

    // One atomic snapshot: status and visuals can never describe different service cycles.
    public struct RoomSnapshot : INetworkSerializable, IEquatable<RoomSnapshot>
    {
        public RoomStatus Status;
        public bool BedMade;
        public uint RemovedTrash;
        public uint Cycle;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Status);
            serializer.SerializeValue(ref BedMade);
            serializer.SerializeValue(ref RemovedTrash);
            serializer.SerializeValue(ref Cycle);
        }
        public bool Equals(RoomSnapshot other) => Status == other.Status && BedMade == other.BedMade &&
            RemovedTrash == other.RemovedTrash && Cycle == other.Cycle;
    }

    [RequireComponent(typeof(NetworkObject))]
    public sealed class ServiceRoom : NetworkBehaviour
    {
        [SerializeField] string roomNumber = "101";
        [SerializeField] CleaningSpot bed;
        [SerializeField] CleaningSpot[] trash;
        [SerializeField] TextMesh[] statusSigns;
        [SerializeField] GameObject guestInRoom;
        [SerializeField] GuestLifecycle physicalGuest;
        readonly NetworkVariable<RoomSnapshot> snapshot = new NetworkVariable<RoomSnapshot>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public event Action<RoomSnapshot, RoomSnapshot> StateChanged;
        public string RoomNumber => roomNumber;
        public RoomSnapshot State => snapshot.Value;
        public CleaningSpot Bed => bed;
        public CleaningSpot[] Trash => trash;
        public GameObject GuestInRoom => guestInRoom;
        public int TrashRemovedCount
        {
            get { int n = 0; for (int i = 0; i < trash.Length; i++) if (IsCleaned(i)) n++; return n; }
        }
        uint AllTrashMask => (1u << trash.Length) - 1u;
        // -1 identifies the bed; non-negative indices are configured trash spots.
        public bool IsCleaned(int index) => index == -1 ? State.BedMade :
            index >= 0 && index < trash.Length && (State.RemovedTrash & (1u << index)) != 0;
        public bool CanClean(int index) => IsSpawned && State.Status == RoomStatus.Dirty && !IsCleaned(index);

        public override void OnNetworkSpawn()
        {
            if (IsServer) snapshot.Value = new RoomSnapshot { Status = RoomStatus.Dirty };
            snapshot.OnValueChanged += Changed;
            ApplyVisuals();
        }
        public override void OnNetworkDespawn() => snapshot.OnValueChanged -= Changed;
        void Changed(RoomSnapshot before, RoomSnapshot after)
        {
            ApplyVisuals();
            StateChanged?.Invoke(before, after);
        }
        void ApplyVisuals()
        {
            bed.ApplyCleaned(State.BedMade);
            for (int i = 0; i < trash.Length; i++) trash[i].ApplyCleaned(IsCleaned(i));
            guestInRoom.SetActive(physicalGuest == null && IsSpawned && State.Status == RoomStatus.Occupied);
            string label = $"ROOM {roomNumber} — {State.Status.ToString().ToUpperInvariant()}\n" +
                $"Bed: {(State.BedMade ? "made" : "unmade")}   Trash: {TrashRemovedCount}/{trash.Length}";
            foreach (var sign in statusSigns) sign.text = label;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestCleanRpc(int index, uint cycle, RpcParams rpcParams = default)
        {
            if (!IsServer || !IsSpawned) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            CleaningSpot spot = index == -1 ? bed : index >= 0 && index < trash.Length ? trash[index] : null;
            string message;
            if (spot == null) message = "Invalid cleaning target.";
            else if (cycle != State.Cycle) message = "Room changed. Please interact again.";
            else if (State.Status != RoomStatus.Dirty) message = "Room is not available for cleaning.";
            else if (IsCleaned(index)) message = "Already cleaned by your team.";
            else if (!ServiceInteractionValidation.CanReach(NetworkManager, sender, spot.TargetCollider))
                message = "Move closer and keep the object in view.";
            else
            {
                var next = State;
                if (index == -1) next.BedMade = true; else next.RemovedTrash |= 1u << index;
                if (next.BedMade && next.RemovedTrash == AllTrashMask) next.Status = RoomStatus.Ready;
                snapshot.Value = next;
                message = next.Status == RoomStatus.Ready ? $"Room {roomNumber} is Ready for check-in." :
                    index == -1 ? "Bed made." : "Trash removed.";
            }
            FeedbackRpc(message, RpcTarget.Single(sender, RpcTargetUse.Temp));
        }

        // Called only by the validated reception transaction on the server.
        internal bool TryCheckIn(uint cycle)
        {
            if (!IsServer || !IsSpawned || State.Cycle != cycle || State.Status != RoomStatus.Ready) return false;
            var next = State; next.Status = RoomStatus.Occupied; snapshot.Value = next; return true;
        }
        internal bool TryCheckout(uint cycle)
        {
            if (!IsServer || !IsSpawned || State.Cycle != cycle || State.Status != RoomStatus.Occupied) return false;
            snapshot.Value = new RoomSnapshot { Status = RoomStatus.Dirty, Cycle = State.Cycle + 1 };
            return true;
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void FeedbackRpc(string message, RpcParams rpcParams = default)
        {
            NetworkManager.LocalClient?.PlayerObject?.GetComponent<PlayerInteractor>()?.ShowFeedback(message);
        }

        public bool ConfigurationValid(out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(roomNumber) || bed == null || trash == null || trash.Length < 1 || trash.Length > 31 ||
                guestInRoom == null || statusSigns == null || statusSigns.Length == 0) error = "Missing room configuration (1–31 trash objects required).";
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
