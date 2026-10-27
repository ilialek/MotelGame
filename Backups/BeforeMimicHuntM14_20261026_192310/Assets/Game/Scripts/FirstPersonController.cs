using UnityEngine;
using UnityEngine.InputSystem;

namespace MotelGame
{
    // Local input only. For a remote player, disable this component and its camera/listener.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [SerializeField] Camera playerCamera;
        [SerializeField, Min(0.1f)] float walkingSpeed = 4f;
        [SerializeField] bool allowSprint = true;
        [SerializeField, Min(0.1f)] float sprintSpeed = 6f;
        [SerializeField, Min(0.1f)] float eyeHeight = 1.65f;
        [SerializeField, Min(0.001f)] float mouseSensitivity = 0.1f;
        [SerializeField, Range(1f, 89f)] float pitchLimit = 85f;
        [SerializeField, Min(0.1f)] float gravity = 20f;

        // Menus may suspend local input without disabling gravity.
        public bool InputSuspended { get; set; }
        public float EyeHeight => eyeHeight;

        CharacterController controller;
        float verticalSpeed;
        float pitch;
        bool cursorCaptured;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (playerCamera == null)
            {
                Debug.LogError("FirstPersonController requires its own player camera.", this);
                enabled = false;
                return;
            }
            // Keep the eye inside the capsule; a short near plane leaves room before walls.
            float top = controller.center.y + controller.height * 0.5f;
            playerCamera.transform.localPosition = new Vector3(0, Mathf.Clamp(eyeHeight, 0.1f, top - 0.1f), 0);
            playerCamera.nearClipPlane = 0.03f;
        }

        void OnEnable()
        {
            verticalSpeed = 0;
            if (Application.isFocused) SetCursorCaptured(true);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            bool released = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            bool justCaptured = false;
            if (released && !InputSuspended) SetCursorCaptured(false);
            else if (!InputSuspended && !cursorCaptured && Application.isFocused && mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                SetCursorCaptured(true);
                justCaptured = true;
            }
            // Also handle cursor releases performed by the Editor/OS.
            if (cursorCaptured && Cursor.lockState != CursorLockMode.Locked) SetCursorCaptured(false);

            Vector2 move = Vector2.zero;
            float speed = walkingSpeed;
            if (!InputSuspended && cursorCaptured && Application.isFocused)
            {
                if (keyboard != null)
                {
                    move.x = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
                    move.y = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
                    if (allowSprint && keyboard.leftShiftKey.isPressed) speed = sprintSpeed;
                }
                if (mouse != null && !justCaptured)
                {
                    Vector2 look = mouse.delta.ReadValue() * mouseSensitivity;
                    transform.Rotate(0, look.x, 0);
                    pitch = Mathf.Clamp(pitch - look.y, -pitchLimit, pitchLimit);
                    playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
                }
            }
            move = Vector2.ClampMagnitude(move, 1); // No faster diagonal movement.
            Vector3 velocity = (transform.right * move.x + transform.forward * move.y) * speed;
            if (controller.isGrounded && verticalSpeed < 0) verticalSpeed = -2f;
            verticalSpeed = Mathf.Max(verticalSpeed - gravity * Time.deltaTime, -50f);
            velocity.y = verticalSpeed;
            CollisionFlags flags = controller.Move(velocity * Time.deltaTime);
            if ((flags & CollisionFlags.Below) != 0 && verticalSpeed < 0) verticalSpeed = -2f;
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0) verticalSpeed = 0;
        }

        public void ResumeAfterComputer() => SetCursorCaptured(Application.isFocused);

        void SetCursorCaptured(bool captured)
        {
            cursorCaptured = captured;
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) SetCursorCaptured(false);
        }

        void OnDisable()
        {
            if (cursorCaptured) SetCursorCaptured(false);
        }
    }
}
