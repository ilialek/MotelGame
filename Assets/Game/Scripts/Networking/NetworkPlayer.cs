using Unity.Netcode;
using UnityEngine;

namespace MotelGame.Networking
{
    [RequireComponent(typeof(NetworkObject), typeof(CharacterController), typeof(FirstPersonController))]
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        [SerializeField] Camera playerCamera;
        [SerializeField] AudioListener playerListener;
        [SerializeField] Renderer body;
        FirstPersonController movement;
        CharacterController capsule;

        void Awake()
        {
            movement = GetComponent<FirstPersonController>();
            capsule = GetComponent<CharacterController>();
            ApplyOwnership(false);
        }

        public override void OnNetworkSpawn() => ApplyOwnership(IsOwner);
        public override void OnGainedOwnership() => ApplyOwnership(IsOwner && IsSpawned);
        public override void OnLostOwnership() => ApplyOwnership(false);
        public override void OnNetworkDespawn() => ApplyOwnership(false);

        void ApplyOwnership(bool local)
        {
            // Remote transforms are driven by NGO; remote capsules do not simulate movement.
            capsule.enabled = local;
            movement.enabled = local;
            playerCamera.enabled = local;
            playerListener.enabled = local;
            body.enabled = !local;
        }
    }
}
