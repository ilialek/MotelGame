using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using MotelGame.Service;
using MotelGame.Horror;

namespace MotelGame.Networking
{
    public struct ExplorationView : INetworkSerializable, IEquatable<ExplorationView>
    {
        public Vector3 Direction, EyeOffset;
        public float Fov, Aspect;
        public bool Flashlight;
        public void NetworkSerialize<T>(BufferSerializer<T> s)
            where T : IReaderWriter
        {
            s.SerializeValue(ref Direction);
            s.SerializeValue(ref EyeOffset);
            s.SerializeValue(ref Fov);
            s.SerializeValue(ref Aspect);
            s.SerializeValue(ref Flashlight);
        }

        public bool Equals(ExplorationView v) => Direction == v.Direction && EyeOffset == v.EyeOffset && Fov == v.Fov && Aspect == v.Aspect && Flashlight == v.Flashlight;
    }

    public sealed partial class NetworkPlayer
    {
        readonly NetworkVariable<bool> crouched = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<ExplorationView> exploration = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        [SerializeField, Min(0)] float flashlightIntensity = 12;
        [SerializeField, Min(1)] float flashlightRange = 14;
        [SerializeField, Range(20,100)] float flashlightCone = 70;
        [SerializeField, Range(10,90)] float flashlightInnerCone = 42;
        CapsuleCollider remoteCapsule;
        Light flashlight;
        bool localFlash, undergroundVisual;
        float nextPose;
        double lastView;
        Vector3 visualScale;
        public bool Crouched => crouched.Value;
        public bool FlashlightOn => exploration.Value.Flashlight;
        public Vector3 ViewOrigin => IsOwner ? playerCamera.transform.position : transform.position + exploration.Value.EyeOffset;
        public Vector3 ViewDirection => IsOwner ? playerCamera.transform.forward : exploration.Value.Direction;
        public float ViewFov => IsOwner ? playerCamera.fieldOfView : exploration.Value.Fov;
        public float ViewAspect => IsOwner ? playerCamera.aspect : exploration.Value.Aspect;
        public bool ViewFresh => IsOwner || IsServer && NetworkManager.ServerTime.Time - lastView < .5;
        void InitializeExploration()
        {
            gameObject.AddComponent<MotelGame.Registration.GuestDialogueSubtitles>();
            var collision = new GameObject("RemotePlayerCollision");
            collision.transform.SetParent(transform, false);
            remoteCapsule = collision.AddComponent<CapsuleCollider>();
            remoteCapsule.radius = capsule.radius;
            remoteCapsule.height = capsule.height;
            remoteCapsule.center = capsule.center;
            remoteCapsule.enabled = false;
            var torch = new GameObject("Flashlight");
            torch.transform.SetParent(playerCamera.transform, false);
            torch.transform.localPosition = new Vector3(.12f, -.08f, .05f);
            flashlight = torch.AddComponent<Light>();
            flashlight.type = LightType.Spot;
            flashlight.range = flashlightRange;
            flashlight.spotAngle = flashlightCone;
            flashlight.innerSpotAngle = Mathf.Min(flashlightInnerCone,flashlightCone);
            flashlight.intensity = flashlightIntensity;
            flashlight.color = new Color(.92f, .95f, 1);
            flashlight.shadows = LightShadows.Soft;
            flashlight.enabled = false;
            torch.AddComponent<UniversalAdditionalLightData>().renderingLayers = 3;
            if (characterVisuals != null)
                visualScale = characterVisuals.transform.localScale;
        }

        void SpawnExploration()
        {
            crouched.OnValueChanged += PostureChanged;
            if (IsServer)
            {
                crouched.Value = false;
                lastView = 0;
            }

            movement.ApplyCrouch(crouched.Value);
        }

        void DespawnExploration()
        {
            crouched.OnValueChanged -= PostureChanged;
            flashlight.enabled = false;
            localFlash = false;
            remoteCapsule.enabled = false;
        }

        void PostureChanged(bool before, bool after) => movement.ApplyCrouch(after);
        [Rpc(SendTo.Server)]
        public void RequestCrouchRpc(bool value)
        {
            if (!IsServer || !IsSpawned || GetComponent<TowelCarrier>().Incapacitated)
                return;
            if (!value && (!movement.CanStand()))
                return;
            crouched.Value = value;
            movement.ApplyCrouch(value);
        }

        public void ToggleFlashlight()
        {
            if (IsOwner && IsSpawned)
                localFlash = !localFlash;
        }

        [Rpc(SendTo.Server)]
        void ViewRpc(ExplorationView value)
        {
            if (!IsServer || !Finite(value.Direction) || !Finite(value.EyeOffset) || value.Direction.sqrMagnitude < .9f || value.Direction.sqrMagnitude > 1.1f || value.EyeOffset.magnitude > 3 || !float.IsFinite(value.Fov) || !float.IsFinite(value.Aspect))
                return;
            value.Fov = Mathf.Clamp(value.Fov, 1, 120);
            value.Aspect = Mathf.Clamp(value.Aspect, .25f, 4);
            value.Direction.Normalize();
            exploration.Value = value;
            lastView = NetworkManager.ServerTime.Time;
        }

        static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
        void LateUpdate()
        {
            if (!IsSpawned)
                return;
            if (IsOwner)
            {
                if (Application.isFocused && !interactor.MenuOpen && !movement.TemporarilyIncapacitated && Keyboard.current?.fKey.wasPressedThisFrame == true)
                    ToggleFlashlight();
                if (Time.unscaledTime >= nextPose)
                {
                    nextPose = Time.unscaledTime + .05f;
                    ViewRpc(new ExplorationView{Direction = playerCamera.transform.forward, EyeOffset = playerCamera.transform.position - transform.position, Fov = playerCamera.fieldOfView, Aspect = playerCamera.aspect, Flashlight = localFlash});
                }
            }
            else
            {
                var v = exploration.Value;
                playerCamera.transform.position = transform.position + (v.Direction.sqrMagnitude > .9f ? v.EyeOffset : Vector3.up * movement.EyeHeight);
                if (v.Direction.sqrMagnitude > .9f)
                    playerCamera.transform.rotation = Quaternion.LookRotation(v.Direction);
                remoteCapsule.height = capsule.height;
                remoteCapsule.center = capsule.center;
                if (characterVisuals != null)
                    characterVisuals.transform.localScale = new Vector3(visualScale.x, visualScale.y * (Crouched ? .5f : 1), visualScale.z);
            }

            bool below = transform.position.y < -.5f;
            if (below != undergroundVisual && characterVisuals != null)
            {
                undergroundVisual = below;
                foreach (var r in characterVisuals.GetComponentsInChildren<Renderer>(true))
                    r.renderingLayerMask = below ? 2u : 1u;
            }

            flashlight.enabled = IsOwner ? localFlash : exploration.Value.Flashlight;
        }
    }
}
