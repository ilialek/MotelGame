using System;
using Unity.Netcode;
using UnityEngine;
using MotelGame.Service;

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
        [SerializeField] UnityEngine.AI.NavMeshObstacle openLeafObstacle;
        public bool IsOpen => state.Value.Open;
        public double TransitionStartedAt => state.Value.StartedAt;
        public bool IsMoving => IsSpawned && state.Value.StartedAt > 0 &&
            NetworkManager.ServerTime.Time < state.Value.StartedAt + animationDuration;
        readonly NetworkVariable<bool> blocking = new(false,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
        PassageBlocker blocker;
        public bool PassageBlocked => blocker != null && blocker.Enabled;
        Collider passageGuest;
        bool closeAfterGuest;
        [SerializeField] bool autoCloseAfterPlayer;
        double autoCloseAt;
        public bool GuestPassage => state.Value.GuestPassage;
        public bool CanInteract => IsSpawned && !IsMoving && !GuestPassage;
        public string Prompt => GuestPassage ? "Guest using door" : IsMoving ? "Door moving" : IsOpen ? "Close door" : "Open door";
        public float CurrentAngle { get; private set; }
        public Vector3 InteractionPoint => leaf.bounds.center;

        public override void OnNetworkSpawn()
        {
            hinge.localRotation=Quaternion.identity;
            blocker = new PassageBlocker(transform, leaf, NetworkManager);
            if (IsServer) { var next=state.Value; next.GuestPassage=false; state.Value=next; }
            ApplyPose();
        }
        void Update()
        {
            if (!IsSpawned) return;
            ApplyPose();
            if(IsServer&&autoCloseAfterPlayer&&IsOpen&&!GuestPassage&&!IsMoving&&NetworkManager.ServerTime.Time>=autoCloseAt)SetOpen(false);
            if (IsServer && closeAfterGuest && !IsMoving && !GuestPassage)
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
            if(IsServer)blocking.Value=!value.Open && !IsMoving && blocker!=null && blocker.CanBlock;
            blocker?.SetClosed(blocking.Value);
            // Optional in authored scenes with through-routes. Closed doors remain planned
            // as explicit guest crossings; the animated leaf remains query-only.
            if(openLeafObstacle!=null)openLeafObstacle.enabled=false;
            // One moving primitive door: keep raycasts/CharacterController collision current locally.
            Physics.SyncTransforms();
        }

        public void Interact() { if (CanInteract) RequestToggleRpc(); }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestToggleRpc(RpcParams rpcParams = default)
        {
            if (!IsSpawned || !IsServer || IsMoving || GuestPassage) return;
            if(!ServiceInteractionValidation.CanReach(NetworkManager,rpcParams.Receive.SenderClientId,leaf))return;
            closeAfterGuest=false;
            SetOpen(!IsOpen);
            if(IsOpen)autoCloseAt=NetworkManager.ServerTime.Time+3;
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
            if (!IsMoving && !IsOpen) SetOpen(true);
            return true;
        }
        public void EndGuestPassage(Collider guest)
        {
            if (!IsServer || !IsSpawned || passageGuest!=guest) return;
            var next=state.Value; next.GuestPassage=false; state.Value=next;
            closeAfterGuest=true;
        }
        public override void OnNetworkDespawn() { blocker?.Dispose();blocker=null;passageGuest=null; closeAfterGuest=false;if(openLeafObstacle!=null)openLeafObstacle.enabled=false; }

    }
}
