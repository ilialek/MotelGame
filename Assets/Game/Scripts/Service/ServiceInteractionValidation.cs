using Unity.Netcode;
using UnityEngine;
using MotelGame.Interactions;

namespace MotelGame.Service
{
    internal static class ServiceInteractionValidation
    {
        // Position comes from the server's copy of the sender's player, never the RPC payload.
        internal static bool CanReach(NetworkManager manager, ulong sender, Collider target)
        {
            if (target == null || !target.enabled || !target.gameObject.activeInHierarchy ||
                !manager.ConnectedClients.TryGetValue(sender, out var client) ||
                client.PlayerObject == null || !client.PlayerObject.IsSpawned) return false;
            if(client.PlayerObject.GetComponent<TowelCarrier>()?.Incapacitated==true) return false;
            var interactor = client.PlayerObject.GetComponent<PlayerInteractor>();
            if (interactor == null) return false;
            Physics.SyncTransforms();
            Vector3 eye = interactor.EyePosition;
            Vector3 point = target.ClosestPoint(eye);
            if ((eye - point).sqrMagnitude > PlayerInteractor.MaxDistance * PlayerInteractor.MaxDistance) return false;
            Vector3 delta = point - eye;
            foreach (var hit in Physics.RaycastAll(eye, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider != target && !hit.collider.transform.IsChildOf(client.PlayerObject.transform)) return false;
            return true;
        }
    }
}
