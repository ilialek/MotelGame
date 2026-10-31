using Unity.Netcode;
using UnityEngine;
using MotelGame.Interactions;

namespace MotelGame.Service
{
    internal static class ServiceInteractionValidation
    {
        // Position comes from the server's copy of the sender's player, never the RPC payload.
        internal static bool CanReach(NetworkManager manager, ulong sender, Collider target, bool useCenter = false)
        {
            if (target == null || !target.enabled || !target.gameObject.activeInHierarchy ||
                !manager.ConnectedClients.TryGetValue(sender, out var client) ||
                client.PlayerObject == null || !client.PlayerObject.IsSpawned) return false;
            if(client.PlayerObject.GetComponent<TowelCarrier>()?.Incapacitated==true) return false;
            var interactor = client.PlayerObject.GetComponent<PlayerInteractor>();
            if (interactor == null) return false;
            Physics.SyncTransforms();
            Vector3 eye = interactor.EyePosition;
            // A recessed grille can expose its centre while the closest upper edge is behind a lintel.
            // Existing callers keep the original nearest-point validation.
            Vector3 point = useCenter ? target.bounds.center : target.ClosestPoint(eye);
            if ((eye - point).sqrMagnitude > PlayerInteractor.MaxDistance * PlayerInteractor.MaxDistance) return false;
            Vector3 delta = point - eye;
            var targetInteraction=target.GetComponentInParent<IInteractable>();
            foreach (var hit in Physics.RaycastAll(eye, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Collide))
            {
                var interaction=hit.collider.GetComponentInParent<IInteractable>();
                if(hit.collider.isTrigger && interaction==null)continue;
                if(hit.collider!=target && !(targetInteraction!=null && interaction==targetInteraction) && !hit.collider.transform.IsChildOf(client.PlayerObject.transform))return false;
            }
            return true;
        }
    }
}
