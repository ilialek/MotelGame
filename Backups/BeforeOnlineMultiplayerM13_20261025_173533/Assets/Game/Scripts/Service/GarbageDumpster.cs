using Unity.Netcode;
using UnityEngine;
using MotelGame.Interactions;

namespace MotelGame.Service
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class GarbageDumpster : NetworkBehaviour, IInteractable
    {
        [SerializeField] NightShiftController shift;
        [SerializeField] Collider targetCollider;
        public Collider TargetCollider => targetCollider;
        public Vector3 AimPoint => targetCollider.bounds.center;
        TowelCarrier LocalCarrier => NetworkManager.LocalClient?.PlayerObject?.GetComponent<TowelCarrier>();
        public bool CanInteract => IsSpawned && shift.IsRunning && LocalCarrier != null && LocalCarrier.HasBag;
        public string Prompt => LocalCarrier != null && LocalCarrier.HasTowel ? "Clean towels cannot be disposed here" : CanInteract ? "Dispose Trash" : "Dumpster — carry a filled garbage bag here";
        public void Interact() { if (CanInteract) DisposeRpc(LocalCarrier.State.Room, LocalCarrier.State.Cycle); }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void DisposeRpc(NetworkObjectReference expectedRoom, uint cycle, RpcParams rpcParams = default)
        {
            if (!IsServer || !IsSpawned) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            var carrier = NetworkManager.ConnectedClients.TryGetValue(sender, out var client) ? client.PlayerObject?.GetComponent<TowelCarrier>() : null;
            string message;
            if (!shift.IsRunning || carrier == null || !carrier.HasBag || !carrier.State.Room.Equals(expectedRoom) || carrier.State.Cycle != cycle)
                message = "No matching garbage bag. Collect a filled bag in a Dirty room.";
            else if (!ServiceInteractionValidation.CanReach(NetworkManager, sender, targetCollider)) message = "Approach the outdoor dumpster.";
            else if (carrier.BagRoom == null || !carrier.BagRoom.TryDisposeBag(carrier, cycle)) message = "This cleaning cycle has changed.";
            else message = "Trash disposed. Room task updated.";
            FeedbackRpc(message, RpcTarget.Single(sender, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void FeedbackRpc(string message, RpcParams rpcParams = default) => NetworkManager.LocalClient?.PlayerObject?.GetComponent<PlayerInteractor>()?.ShowFeedback(message);
        public bool ConfigurationValid => shift != null && targetCollider != null;
    }
}
