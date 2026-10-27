using Unity.Netcode;
using UnityEngine;
using MotelGame.Interactions;
using MotelGame.Service;

namespace MotelGame.Horror
{
    public sealed class GuestEvacuation : MonoBehaviour, IInteractable
    {
        GuestLifecycle guest;
        void Awake() => guest = GetComponent<GuestLifecycle>();
        public bool CanInteract => guest != null && guest.IsSpawned && guest.Phase == GuestPhase.InRoom && MimicHunt.Instance != null && MimicHunt.Instance.Shift.IsRunning;
        public string Prompt => "Evacuate " + guest.GuestName + " to the lobby";
        public void Interact() => MimicHunt.Instance.EvacuateRpc(new NetworkObjectReference(guest.NetworkObject), guest.State.Visit, MimicHunt.Instance.State.Revision);
    }
}
