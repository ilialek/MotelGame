using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;
using MotelGame.Networking;

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

        [SerializeField, Min(.7f)] float crouchedHeight = .9f;
        [SerializeField, Min(.1f)] float crouchedEyeHeight = .78f;
        [SerializeField, Min(.1f)] float crouchedSpeed = 1.7f;
        float standingHeight, standingStepOffset;
        bool crouched;
        float nextPostureCheck, nextPostureRequest;
        public bool Crouched => crouched;
        public float CrouchedHeight => crouchedHeight;
        public float StandingHeight => standingHeight;
        public void RequestCrouch(bool value)
        {
            if(value == crouched || Time.unscaledTime < nextPostureRequest || !value && !CanStand())return;
            nextPostureRequest=Time.unscaledTime+.4f;
            var net=GetComponent<NetworkPlayer>();
            if(net!=null&&net.IsSpawned){if(net.IsOwner)net.RequestCrouchRpc(value);}
            else ApplyCrouch(value);
        }
        public bool CanStand()
        {
            var foot=transform.position;
            return !Physics.OverlapCapsule(foot+Vector3.up*(controller.radius+.04f),foot+Vector3.up*(standingHeight-controller.radius),
                controller.radius*.95f,~0,QueryTriggerInteraction.Ignore).Any(c=>!c.transform.IsChildOf(transform));
        }
        public void ApplyCrouch(bool value)
        {
            crouched=value;nextPostureRequest=0;float height=value?crouchedHeight:standingHeight;
            controller.height=height;controller.center=Vector3.up*(height*.5f);
            controller.stepOffset=Mathf.Min(standingStepOffset,height*.25f);
            var eye=playerCamera.transform.localPosition;eye.y=EyeHeight;playerCamera.transform.localPosition=eye;
        }
        // Menus may suspend local input without disabling gravity.
        public bool InputSuspended { get; set; }
        public bool TemporarilyIncapacitated { get; set; }
        public float EyeHeight => crouched ? crouchedEyeHeight : eyeHeight;

        CharacterController controller;
        float verticalSpeed;
        float pitch;
        bool cursorCaptured;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            standingHeight=controller.height;standingStepOffset=controller.stepOffset;
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
            float speed = crouched ? crouchedSpeed : walkingSpeed;
            if (!InputSuspended && !TemporarilyIncapacitated && cursorCaptured && Application.isFocused)
            {
                if (keyboard != null)
                {
                    UpdatePosture(keyboard.leftCtrlKey.isPressed);
                    move.x = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
                    move.y = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
                    speed=crouched?crouchedSpeed:allowSprint&&keyboard.leftShiftKey.isPressed?sprintSpeed:walkingSpeed;
                }
                if (mouse != null && !justCaptured)
                {
                    Vector2 look = mouse.delta.ReadValue() * mouseSensitivity;
                    transform.Rotate(0, look.x, 0);
                    pitch = Mathf.Clamp(pitch - look.y, -pitchLimit, pitchLimit);
                    playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
                }
            }
            MovePlayer(move, speed);
        }

        void UpdatePosture(bool held)
        {
            // Local headroom checks do not send rejected requests every frame.
            if(held!=crouched && Time.unscaledTime>=nextPostureCheck)
            {nextPostureCheck=Time.unscaledTime+.1f;RequestCrouch(held);}
        }

        // Shared by normal input and the development collision test; no scripted traversal.
        void MovePlayer(Vector2 move, float speed)
        {
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
