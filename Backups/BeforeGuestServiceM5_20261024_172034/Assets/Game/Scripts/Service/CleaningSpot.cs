using UnityEngine;
using MotelGame.Interactions;

namespace MotelGame.Service
{
    // Scene configuration only. Shared state belongs to ServiceRoom, not to each visual.
    public sealed class CleaningSpot : MonoBehaviour, IInteractable
    {
        [SerializeField] ServiceRoom room;
        [SerializeField] int index = -1;
        [SerializeField] Collider targetCollider;
        [SerializeField] GameObject dirtyVisual;
        [SerializeField] GameObject cleanVisual;
        public Collider TargetCollider => targetCollider;
        public Vector3 AimPoint => targetCollider.bounds.center;
        public bool CanInteract => room != null && room.CanClean(index);
        public string Prompt => room.State.Status == RoomStatus.Occupied ? "Occupied — cleaning unavailable" :
            room.IsCleaned(index) ? (index == -1 ? "Bed made" : "Trash removed") : index == -1 ? "Make Bed" : "Remove Trash";
        public void Interact() { if (CanInteract) room.RequestCleanRpc(index, room.State.Cycle); }
        public void ApplyCleaned(bool cleaned)
        {
            dirtyVisual.SetActive(!cleaned);
            if (cleanVisual != null) cleanVisual.SetActive(cleaned);
            // The bed keeps its existing collision. Removed trash must stop intercepting raycasts.
            if (index >= 0) targetCollider.enabled = !cleaned;
        }
        public bool Matches(ServiceRoom owner, int expectedIndex) => room == owner && index == expectedIndex &&
            targetCollider != null && dirtyVisual != null && (index >= 0 || cleanVisual != null);
    }
}
