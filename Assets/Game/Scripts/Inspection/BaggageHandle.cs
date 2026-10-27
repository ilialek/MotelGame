using UnityEngine;
using Unity.Netcode;
using MotelGame.Interactions;

namespace MotelGame.Inspection
{
    public sealed class BaggageHandle : MonoBehaviour, IInteractable
    {
        public GuestBaggage baggage;
        public bool CanInteract => baggage != null && baggage.IsSpawned && baggage.Guest.CanCheckIn;
        public string Prompt => baggage.State.Phase == BaggagePhase.OnAnalyzer ? "Read baggage analyzer" : "Place " + baggage.Guest.GuestName + "'s bag on analyzer";
        public void Interact()
        {
            if (baggage.State.Phase == BaggagePhase.OnAnalyzer)
                BaggageInspection.Instance.RequestUse();
            else
                BaggageInspection.Instance.PlaceBagRpc(new NetworkObjectReference(baggage.NetworkObject), baggage.State.Visit);
        }
    }
}
