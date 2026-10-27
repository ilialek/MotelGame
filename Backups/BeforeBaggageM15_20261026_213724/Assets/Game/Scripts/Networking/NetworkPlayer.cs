using Unity.Netcode;
using MotelGame.Characters;
using MotelGame.Interactions;
using UnityEngine;

namespace MotelGame.Networking
{
    [RequireComponent(typeof(NetworkObject), typeof(CharacterController), typeof(FirstPersonController))]
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        [SerializeField] Camera playerCamera;
        [SerializeField] AudioListener playerListener;
        [SerializeField] Renderer body; // Legacy placeholder for older setup tools.
        [SerializeField] CharacterVisuals characterVisuals;
        public bool BodyVisible => characterVisuals != null ? characterVisuals.BodyVisible : body.enabled;
        FirstPersonController movement;
        CharacterController capsule;
        PlayerInteractor interactor;

        void Awake()
        {
            movement = GetComponent<FirstPersonController>();
            capsule = GetComponent<CharacterController>();
            interactor = GetComponent<PlayerInteractor>();
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
            if (interactor != null) interactor.enabled = local;
            playerCamera.enabled = local;
            playerListener.enabled = local;
            if (body != null) body.enabled = characterVisuals == null && !local;
            if (characterVisuals != null) characterVisuals.SetVisible(!local);
        }
    }
}
