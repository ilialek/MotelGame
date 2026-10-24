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
        readonly RaycastHit[] rayHits = new RaycastHit[32];
        public bool HasPrompt => target != null;
        public bool MenuOpen { get; set; }
        public string Feedback { get; private set; }
        float feedbackUntil;
        public void ShowFeedback(string message) { Feedback = message; feedbackUntil = Time.unscaledTime + 4; }
        public Vector3 EyePosition => playerCamera.transform.position;

        void Awake() => movement = GetComponent<FirstPersonController>();

        void Update()
        {
            target = null;
            if (!IsSpawned || !IsOwner || !Application.isFocused ||
                Cursor.lockState != CursorLockMode.Locked || movement.InputSuspended || MenuOpen) return;
            target = FindFocus();
            if (target != null && target.CanInteract && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                target.Interact();
        }

        // Shared by normal input and integration tests; remote players cannot even raycast.
        public IInteractable FindTarget()
        {
            var focused = FindFocus();
            return focused != null && focused.CanInteract ? focused : null;
        }
        IInteractable FindFocus()
        {
            if (!enabled || !IsSpawned || !IsOwner || MenuOpen || playerCamera == null) return null;
            int count = Physics.RaycastNonAlloc(playerCamera.transform.position, playerCamera.transform.forward,
                rayHits, MaxDistance, ~0, QueryTriggerInteraction.Ignore);
            var hits = rayHits;
            // Rare dense geometry fallback avoids losing an occluding wall when the buffer fills.
            if (count == rayHits.Length)
            {
                hits = Physics.RaycastAll(playerCamera.transform.position, playerCamera.transform.forward,
                    MaxDistance, ~0, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }
            Collider closest = null; float distance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.transform.IsChildOf(transform) || hits[i].distance >= distance) continue;
                closest = hits[i].collider; distance = hits[i].distance;
            }
            return closest == null ? null : closest.GetComponentInParent<IInteractable>();
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
            if (!IsSpawned || !IsOwner) return;
            if (target != null && !MenuOpen && Cursor.lockState == CursorLockMode.Locked)
            {
                string label = target.CanInteract ? "Press E — " + target.Prompt : target.Prompt;
                GUI.Box(new Rect(Screen.width * .5f - 170, Screen.height * .5f + 24, 340, 30), label);
            }
            if (Time.unscaledTime < feedbackUntil)
                GUI.Box(new Rect(16, Screen.height - 72, Screen.width - 32, 56), Feedback);
        }
        void OnDisable() { target = null; MenuOpen = false; }
        public override void OnNetworkDespawn() { target = null; MenuOpen = false; Feedback = null; feedbackUntil = 0; }
    }
}
