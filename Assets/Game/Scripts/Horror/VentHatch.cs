using System;
using Unity.Netcode;
using UnityEngine;
using MotelGame.Interactions;
using MotelGame.Networking;
using MotelGame.Service;

namespace MotelGame.Horror
{
    // Keeps the existing scene script identity; this is now a manually traversed wall grille.
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class VentHatch : NetworkBehaviour, IInteractable
    {
        public ServiceRoom room;
        public Transform upper, lower, hinge;
        public BoxCollider leaf;
        public Transform[] route;
        [SerializeField]
        float duration = .55f;
        public struct GrilleState : INetworkSerializable, IEquatable<GrilleState>
        {
            public bool Open;
            public uint Revision;
            public double StartedAt;
            public float FromAngle;
            public void NetworkSerialize<T>(BufferSerializer<T> s)
                where T : IReaderWriter
            {
                s.SerializeValue(ref Open);
                s.SerializeValue(ref Revision);
                s.SerializeValue(ref StartedAt);
                s.SerializeValue(ref FromAngle);
            }

            public bool Equals(GrilleState o) => Open == o.Open && Revision == o.Revision && StartedAt == o.StartedAt && FromAngle == o.FromAngle;
        }

        readonly NetworkVariable<GrilleState> state = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public GrilleState State => state.Value;
        public bool IsMoving => IsSpawned && State.StartedAt > 0 && NetworkManager.ServerTime.Time < State.StartedAt + duration;
        public bool CanInteract => IsSpawned && !IsMoving;
        public string Prompt => IsMoving ? "Vent grille moving" : State.Open ? "Close vent grille" : "Open vent grille — crouch to enter [Left Ctrl]";
        public float CurrentAngle { get; private set; }

        readonly NetworkVariable<bool> blocking = new(false,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
        PassageBlocker blocker;
        public bool PassageBlocked => blocker != null && blocker.Enabled;
        public override void OnNetworkSpawn(){hinge.localRotation=Quaternion.identity;blocker=new PassageBlocker(transform,leaf,NetworkManager);ApplyPose();}
        public override void OnNetworkDespawn()
        {
            blocker?.Dispose();blocker=null;
            if (hinge != null)
                hinge.localRotation = Quaternion.identity;
        }

        void Update()
        {
            if (!IsSpawned)
                return;
            ApplyPose();

        }

        void ApplyPose()
        {
            float t = State.StartedAt <= 0 ? 1 : Mathf.Clamp01((float)((NetworkManager.ServerTime.Time - State.StartedAt) / duration));
            t = t * t * (3 - 2 * t);
            CurrentAngle = Mathf.Lerp(State.FromAngle, State.Open ? 90 : 0, t);
            hinge.localRotation = Quaternion.Euler(CurrentAngle, 0, 0);
            if(IsServer)blocking.Value=!State.Open && !IsMoving && blocker!=null && blocker.CanBlock;
            blocker?.SetClosed(blocking.Value);
            Physics.SyncTransforms();
        }

        public void Interact()
        {
            if (CanInteract)
                UseRpc(State.Revision);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void UseRpc(uint revision, RpcParams rpc = default)
        {
            if (!IsServer || !CanInteract || revision != State.Revision || !(ServiceInteractionValidation.CanReach(NetworkManager, rpc.Receive.SenderClientId, leaf) || ServiceInteractionValidation.CanReach(NetworkManager, rpc.Receive.SenderClientId, leaf, useCenter: true)))
                return;
            SetOpen(!State.Open);
        }

        void SetOpen(bool open)
        {
            state.Value = new GrilleState{Open = open, Revision = State.Revision + 1, StartedAt = NetworkManager.ServerTime.Time, FromAngle = CurrentAngle};
            ApplyPose();
        }

    }
}
