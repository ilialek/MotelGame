using System;
using Unity.Netcode;
using UnityEngine;

namespace MotelGame.Interactions
{
    [DefaultExecutionOrder(-50)] // Update collider pose before the local CharacterController moves.
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkDoor : NetworkBehaviour, IInteractable
    {
        [SerializeField] Transform hinge;
        [SerializeField] BoxCollider leaf;
        [SerializeField] float openAngle = 90f;
        [SerializeField, Min(.1f)] float animationDuration = .65f;
        // Conservative local-space sweep bounds for this prefab, before player capsule expansion.
        [SerializeField] Vector3 sweepCenter = new Vector3(-.51f, 1.1f, -.51f);
        [SerializeField] Vector3 sweepSize = new Vector3(1.12f, 2.2f, 1.12f);

        public struct DoorState : INetworkSerializable, IEquatable<DoorState>
        {
            public bool Open;
            public bool GuestPassage;
            public double StartedAt;
            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            { serializer.SerializeValue(ref Open); serializer.SerializeValue(ref GuestPassage); serializer.SerializeValue(ref StartedAt); }
            public bool Equals(DoorState other) => Open == other.Open && GuestPassage == other.GuestPassage && StartedAt == other.StartedAt;
        }
        readonly NetworkVariable<DoorState> state = new NetworkVariable<DoorState>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public bool IsOpen => state.Value.Open;
        public double TransitionStartedAt => state.Value.StartedAt;
        public bool IsMoving => IsSpawned && state.Value.StartedAt > 0 &&
            NetworkManager.ServerTime.Time < state.Value.StartedAt + animationDuration;
        Collider passageGuest;
        bool closeAfterGuest;
        public bool GuestPassage => state.Value.GuestPassage;
        public bool CanInteract => IsSpawned && !IsMoving && !GuestPassage;
        public string Prompt => GuestPassage ? "Guest using door" : IsMoving ? "Door moving" : IsOpen ? "Close door" : "Open door";
        public float CurrentAngle { get; private set; }
        public Vector3 InteractionPoint => leaf.bounds.center;

        public override void OnNetworkSpawn()
        {
            if (IsServer) { var next=state.Value; next.GuestPassage=false; state.Value=next; }
            ApplyPose();
        }
        void Update()
        {
            if (!IsSpawned) return;
            ApplyPose();
            if (IsServer && closeAfterGuest && !IsMoving && !GuestPassage && !SweepOccupied())
            { closeAfterGuest=false; SetOpen(false); passageGuest=null; }
        }

        void ApplyPose()
        {
            var value = state.Value;
            float t = value.StartedAt <= 0 ? 1 : Mathf.Clamp01((float)
                ((NetworkManager.ServerTime.Time - value.StartedAt) / animationDuration));
            t = t * t * (3 - 2 * t);
            CurrentAngle = Mathf.Lerp(value.Open ? 0 : openAngle, value.Open ? openAngle : 0, t);
            hinge.localRotation = Quaternion.Euler(0, CurrentAngle, 0);
            // One moving primitive door: keep raycasts/CharacterController collision current locally.
            Physics.SyncTransforms();
        }

        public void Interact() { if (CanInteract) RequestToggleRpc(); }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestToggleRpc(RpcParams rpcParams = default)
        {
            if (!IsSpawned || !IsServer || IsMoving || GuestPassage) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!NetworkManager.ConnectedClients.TryGetValue(sender, out var client) ||
                client.PlayerObject == null || !client.PlayerObject.IsSpawned) return;
            var interactor = client.PlayerObject.GetComponent<PlayerInteractor>();
            if (interactor == null) return;
            ApplyPose();
            Vector3 eye = interactor.EyePosition;
            Vector3 closest = leaf.ClosestPoint(eye);
            if ((closest - eye).sqrMagnitude > PlayerInteractor.MaxDistance * PlayerInteractor.MaxDistance) return;
            // The server uses its own player position and geometry, never a client-supplied distance/id.
            if (Physics.Linecast(eye, closest, out var hit, ~0, QueryTriggerInteraction.Ignore) &&
                !hit.collider.transform.IsChildOf(transform)) return;
            if (SweepOccupied()) return;
            closeAfterGuest=false;
            SetOpen(!IsOpen);
        }

        void SetOpen(bool open)
        {
            if (IsOpen==open) return;
            var next=state.Value; next.Open=open; next.StartedAt=NetworkManager.ServerTime.Time; state.Value=next; ApplyPose();
        }
        // Server-only passage reservation. Players keep the same RPC/state/animation mechanism;
        // closing is temporarily unavailable while the guest crosses the physical doorway.
        public bool TryBeginGuestPassage(Collider guest)
        {
            if (!IsServer || !IsSpawned || guest==null || !guest.enabled ||
                Vector3.Distance(guest.bounds.center,transform.position)>3.5f || (passageGuest!=null && passageGuest!=guest)) return false;
            passageGuest=guest; closeAfterGuest=false;
            var next=state.Value; next.GuestPassage=true; state.Value=next;
            if (!IsMoving && !IsOpen && !SweepOccupied()) SetOpen(true);
            return true;
        }
        public void EndGuestPassage(Collider guest)
        {
            if (!IsServer || !IsSpawned || passageGuest!=guest) return;
            var next=state.Value; next.GuestPassage=false; state.Value=next;
            closeAfterGuest=true;
        }
        public override void OnNetworkDespawn() { passageGuest=null; closeAfterGuest=false; }

        bool SweepOccupied()
        {
            // Remote CharacterControllers are disabled, so test connected player positions directly.
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                var capsule = client.PlayerObject.GetComponent<CharacterController>();
                if (capsule == null) continue;
                Vector3 center = transform.InverseTransformPoint(client.PlayerObject.transform.TransformPoint(capsule.center));
                Vector3 half = sweepSize * .5f;
                if (Mathf.Abs(center.x - sweepCenter.x) < half.x + capsule.radius &&
                    Mathf.Abs(center.z - sweepCenter.z) < half.z + capsule.radius &&
                    Mathf.Abs(center.y - sweepCenter.y) < half.y + capsule.height * .5f) return true;
            }
            if (passageGuest != null)
            {
                Vector3 c=transform.InverseTransformPoint(passageGuest.bounds.center);
                Vector3 h=sweepSize*.5f, e=passageGuest.bounds.extents;
                if (Mathf.Abs(c.x-sweepCenter.x)<h.x+e.x && Mathf.Abs(c.z-sweepCenter.z)<h.z+e.z &&
                    Mathf.Abs(c.y-sweepCenter.y)<h.y+e.y) return true;
            }
            return false;
        }
    }
}
