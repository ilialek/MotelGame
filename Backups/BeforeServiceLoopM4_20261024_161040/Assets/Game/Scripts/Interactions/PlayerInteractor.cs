using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MotelGame.Interactions
{
    [DisallowMultipleComponent]
    public sealed class PlayerInteractor : NetworkBehaviour
    {
        public const float MaxDistance = 2.5f;
        [SerializeField] Camera playerCamera;
        FirstPersonController movement;
        IInteractable target;
        public bool HasPrompt => target != null;
        public Vector3 EyePosition => playerCamera.transform.position;

        void Awake() => movement = GetComponent<FirstPersonController>();

        void Update()
        {
            target = null;
            if (!IsSpawned || !IsOwner || !Application.isFocused ||
                Cursor.lockState != CursorLockMode.Locked || movement.InputSuspended) return;
            target = FindTarget();
            if (target != null && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                target.Interact();
        }

        // Shared by normal input and integration tests; remote players cannot even raycast.
        public IInteractable FindTarget()
        {
            if (!enabled || !IsSpawned || !IsOwner || playerCamera == null) return null;
            if (!Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward,
                out var hit, MaxDistance, ~0, QueryTriggerInteraction.Ignore)) return null;
            var interactable = hit.collider.GetComponentInParent<IInteractable>();
            return interactable != null && interactable.CanInteract ? interactable : null;
        }

        public bool TryInteract()
        {
            var interactable = FindTarget();
            if (interactable == null) return false;
            interactable.Interact();
            return true;
        }

        void OnGUI()
        {
            if (!IsSpawned || !IsOwner || target == null || Cursor.lockState != CursorLockMode.Locked) return;
            GUI.Box(new Rect(Screen.width * .5f - 42, Screen.height * .5f + 24, 84, 26), "Press E");
        }
        void OnDisable() => target = null;
        public override void OnNetworkDespawn() => target = null;
    }
}
