using Unity.Netcode;
using UnityEngine;
using MotelGame.Interactions;

namespace MotelGame.Service
{
    public enum GuestServiceAction : byte { AnswerPhone, TakeTowel, DeliverTowel }

    [DisallowMultipleComponent]
    public sealed class GuestServiceInteraction : MonoBehaviour, IInteractable
    {
        [SerializeField] GuestServiceRequests service;
        [SerializeField] GuestServiceAction action;
        [SerializeField] ServiceRoom deliveryRoom;
        [SerializeField] Collider targetCollider;

        public ServiceRoom Room => deliveryRoom;
        public GuestServiceRequests Service => service;
        public GuestServiceAction Action => action;
        public Collider TargetCollider => targetCollider;
        public Vector3 AimPoint => targetCollider.bounds.center;
        TowelCarrier LocalCarrier => service.LocalCarrier;
        public bool CanInteract => service != null && service.IsSpawned && LocalCarrier != null &&
            (action == GuestServiceAction.AnswerPhone ? service.Phase == GuestRequestPhase.Ringing :
            service.Phase == GuestRequestPhase.PendingDelivery &&
            (action == GuestServiceAction.TakeTowel ? !LocalCarrier.HasTowel :
            deliveryRoom == service.Room && LocalCarrier.HasTowel));
        public string Prompt => action == GuestServiceAction.AnswerPhone ?
            (service.Phase == GuestRequestPhase.Ringing ? "Answer Phone" : "Phone — no incoming call") :
            action == GuestServiceAction.TakeTowel ? (LocalCarrier != null && LocalCarrier.HasTowel ? "Already carrying a clean towel" :
            service.Phase == GuestRequestPhase.PendingDelivery ? "Take Clean Towel" : "Clean towels — answer a guest request first") :
            service.Room != deliveryRoom ? $"Room {deliveryRoom.RoomNumber} — no delivery requested" :
            service.Phase == GuestRequestPhase.Completed ? $"Room {deliveryRoom.RoomNumber} — request complete" :
            service.Phase != GuestRequestPhase.PendingDelivery ? $"Room {deliveryRoom.RoomNumber} — no delivery requested" :
            LocalCarrier != null && LocalCarrier.HasTowel ? $"Deliver Towel — Room {deliveryRoom.RoomNumber}" : "Collect a clean towel in Utility";
        public void Interact()
        {
            if (!CanInteract) return;
            uint requestId = service.State.RequestId;
            if (action == GuestServiceAction.AnswerPhone) service.AnswerRpc(requestId);
            else if (action == GuestServiceAction.TakeTowel) service.TakeTowelRpc(requestId);
            else service.DeliverRpc(new NetworkObjectReference(deliveryRoom.NetworkObject), requestId);
        }
    }
}
