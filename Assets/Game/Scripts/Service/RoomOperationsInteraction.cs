using UnityEngine;
using MotelGame.Interactions;

namespace MotelGame.Service
{
    public enum RoomOperation : byte { PickUpBag, ResetBreaker }
    // Scene target only; room owns and validates all shared operations.
    public sealed class RoomOperationsInteraction : MonoBehaviour, IInteractable
    {
        [SerializeField] ServiceRoom room;
        [SerializeField] RoomOperation action;
        [SerializeField] Collider targetCollider;
        [SerializeField] GameObject bagVisual;
        [SerializeField] TextMesh label;
        public ServiceRoom Room => room;
        public RoomOperation Action => action;
        public Collider TargetCollider => targetCollider;
        public Vector3 AimPoint => targetCollider.bounds.center;
        TowelCarrier Carrier => room.NetworkManager.LocalClient?.PlayerObject?.GetComponent<TowelCarrier>();
        public bool CanInteract => room.AcceptingWork && (action == RoomOperation.ResetBreaker ? room.State.Faulted :
            room.State.Bag == GarbagePhase.AwaitingPickup && Carrier != null && !Carrier.HasItem);
        public string Prompt => action == RoomOperation.ResetBreaker ? (room.State.Faulted ? $"Reset Breaker {room.RoomNumber}" : $"Room {room.RoomNumber} — Power OK") :
            Carrier != null && Carrier.HasItem ? $"Carrying {Carrier.ItemName} — finish that delivery first" : $"Pick Up Garbage Bag — Room {room.RoomNumber}";
        public void Interact()
        {
            if (!CanInteract) return;
            if (action == RoomOperation.ResetBreaker) room.ResetBreakerRpc(room.State.FaultId);
            else room.PickUpBagRpc(room.State.Cycle);
        }
        void Update()
        {
            if (action == RoomOperation.PickUpBag)
            {
                bool visible = room.IsSpawned && room.State.Bag == GarbagePhase.AwaitingPickup;
                if (bagVisual != null) bagVisual.SetActive(visible);
                targetCollider.enabled = visible;
            }
            else if (label != null)
            {
                label.text = $"ROOM {room.RoomNumber}\n" + (room.State.Faulted ? "TRIPPED / [E] RESET" : "POWER OK");
                label.color = room.State.Faulted ? new Color(1, .63f, .25f) : new Color(.7f, .86f, .7f);
            }
        }
        public bool ConfigurationValid => room != null && targetCollider != null && (action == RoomOperation.PickUpBag ? bagVisual != null : label != null);
    }
}
