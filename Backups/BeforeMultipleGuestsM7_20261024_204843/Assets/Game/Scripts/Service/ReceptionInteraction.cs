using UnityEngine;
using MotelGame.Interactions;

namespace MotelGame.Service
{
    // Reuses the existing counter collider and interaction pipeline.
    public sealed class ReceptionInteraction : MonoBehaviour, IInteractable
    {
        [SerializeField] ReceptionDesk reception;
        public bool CanInteract => reception != null && reception.IsSpawned;
        public string Prompt => "Reception — room service";
        public void Interact() { if (CanInteract) reception.OpenMenu(); }
    }
}
