using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using MotelGame.Service;
using MotelGame.Interactions;

namespace MotelGame.Horror
{
    public struct EquipmentSnapshot : INetworkSerializable, IEquatable<EquipmentSnapshot>
    {
        public bool Held, Placed;
        public ulong Holder;
        public Vector3 Position;
        public uint Revision;
        public void NetworkSerialize<T>(BufferSerializer<T> s)
            where T : IReaderWriter
        {
            s.SerializeValue(ref Held);
            s.SerializeValue(ref Placed);
            s.SerializeValue(ref Holder);
            s.SerializeValue(ref Position);
            s.SerializeValue(ref Revision);
        }

        public bool Equals(EquipmentSnapshot v) => Held == v.Held && Placed == v.Placed && Holder == v.Holder && Position == v.Position && Revision == v.Revision;
    }

    [RequireComponent(typeof(NetworkObject))]
    public sealed class HuntEquipment : NetworkBehaviour, IInteractable
    {
        [SerializeField]
        ServiceItem item;
        [SerializeField]
        Transform home, visual;
        [SerializeField]
        Collider pickup;
        [SerializeField]
        GameObject flash;
        [SerializeField]
        AudioClip gunshot, impact;
        [SerializeField]
        Material impactMaterial;
        [SerializeField, Min(1)]
        int damage = 34;
        [SerializeField, Min(.1f)]
        float shotInterval = .45f;
        readonly NetworkVariable<EquipmentSnapshot> state = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        Renderer[] fins,allRenderers;bool undergroundVisual;
        MaterialPropertyBlock heatColor;
        void Awake()
        {
            allRenderers=visual.GetComponentsInChildren<Renderer>(true);
            fins = visual.GetComponentsInChildren<Renderer>(true).Where(r => r.name == "HeatingFin").ToArray();
            heatColor = new MaterialPropertyBlock();
        }

        double nextShot;
        uint lastSequence, localSequence;
        float flashUntil;
        public EquipmentSnapshot State => state.Value;
        public ServiceItem Item => item;
        public bool IsHeating => IsSpawned && !State.Held && State.Placed && item == ServiceItem.ThermalDecoy;
        public bool LocallyHeld => IsSpawned && State.Held && State.Holder == NetworkManager.LocalClientId;
        public bool CanInteract => IsSpawned && !State.Held && MimicHunt.Instance.Shift.IsRunning;
        public string Prompt => "Take " + (item == ServiceItem.Firearm ? "pistol" : "thermal decoy") + " (one carry slot)";
        public Collider PickupCollider => pickup;
        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                state.Value = new EquipmentSnapshot{Position = home.position};
                NetworkManager.OnClientDisconnectCallback += DropOwnerOnServer;
            }

            Apply();
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= DropOwnerOnServer;
            visual.SetPositionAndRotation(home.position, home.rotation);
            if (flash != null) flash.SetActive(false);
        }

        public void Interact()
        {
            TakeRpc(State.Revision);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void TakeRpc(uint revision, RpcParams rpc = default)
        {
            ulong id = rpc.Receive.SenderClientId;
            if (!IsServer || !CanInteract || State.Revision != revision || !ServiceInteractionValidation.CanReach(NetworkManager, id, pickup))
                return;
            var carrier = NetworkManager.ConnectedClients[id].PlayerObject.GetComponent<TowelCarrier>();
            if (!carrier.TryCarryEquipment(item))
            {
                ReplyRpc("Your hands are occupied. Finish the delivery or place your equipment [G].", RpcTarget.Single(id, RpcTargetUse.Temp));
                return;
            }

            var v = State;
            v.Held = true;
            v.Holder = id;
            v.Placed = false;
            v.Revision++;
            state.Value = v;
            lastSequence = 0;
            nextShot = 0;
        }

        public void PlaceLocal()
        {
            var player = NetworkManager.LocalClient?.PlayerObject;
            if (player == null || !LocallyHeld)
                return;
            var camera = player.GetComponentInChildren<Camera>();
            Vector3 aim = player.transform.position + player.transform.forward * .9f;
            if (Physics.Raycast(camera.transform.position, camera.transform.forward, out var hit, 2.3f, ~0, QueryTriggerInteraction.Ignore) && hit.normal.y > .7f)
                aim = hit.point;
            PlaceRpc(aim, State.Revision);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void PlaceRpc(Vector3 requested, uint revision, RpcParams rpc = default)
        {
            if (!IsServer || !State.Held || State.Holder != rpc.Receive.SenderClientId || State.Revision != revision || !Finite(requested))
                return;
            if (!NetworkManager.ConnectedClients.TryGetValue(State.Holder, out var c) || c.PlayerObject == null || c.PlayerObject.GetComponent<TowelCarrier>().Incapacitated)
                return;
            if (Vector3.Distance(c.PlayerObject.transform.position, requested) > 2.5f)
                return;
            if (!Floor(requested, out var floor))
                return;
            Vector3 eye = c.PlayerObject.transform.position + Vector3.up * c.PlayerObject.GetComponent<FirstPersonController>().EyeHeight;
            if (Physics.RaycastAll(eye, (floor - eye).normalized, Vector3.Distance(eye, floor) - .12f, ~0, QueryTriggerInteraction.Ignore).Any(h => !h.collider.transform.IsChildOf(c.PlayerObject.transform) && !h.collider.transform.IsChildOf(transform)))
                return;
            Release(floor, true);
        }

        bool Floor(Vector3 near, out Vector3 p)
        {
            p = near;
            var hits = Physics.RaycastAll(near + Vector3.up * .2f, Vector3.down, 4, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance);
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<Unity.Netcode.NetworkObject>()?.GetComponent<CharacterController>() != null)
                    continue;
                if (hit.normal.y < .7f)
                    return false;
                p = hit.point + Vector3.up * .035f;
                return true;
            }

            return false;
        }

        public void DropOwnerOnServer(ulong id)
        {
            if (!IsServer || !IsSpawned || !State.Held || State.Holder != id)
                return;
            Vector3 p = visual.position;
            if (NetworkManager.ConnectedClients.TryGetValue(id, out var c) && c.PlayerObject != null)
                p = c.PlayerObject.transform.position;
            if (!Floor(p, out var floor))
                floor = State.Position;
            Release(floor, false);
        }

        void Release(Vector3 at, bool heating)
        {
            if (State.Held && NetworkManager.ConnectedClients.TryGetValue(State.Holder, out var c))
                c.PlayerObject?.GetComponent<TowelCarrier>()?.ReleaseEquipment(item);
            state.Value = new EquipmentSnapshot{Position = at, Placed = heating, Revision = State.Revision + 1};
        }

        public void ResetOnServer()
        {
            if (IsServer && IsSpawned)
                Release(home.position, false);
        }

        static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        public void FireLocal()
        {
            var player = NetworkManager.LocalClient?.PlayerObject;
            if (player == null || !LocallyHeld || item != ServiceItem.Firearm)
                return;
            FireRpc(player.GetComponentInChildren<Camera>().transform.forward, State.Revision, ++localSequence);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void FireRpc(Vector3 direction, uint revision, uint sequence, RpcParams rpc = default)
        {
            ulong id = rpc.Receive.SenderClientId;
            if (!IsServer || item != ServiceItem.Firearm || !State.Held || State.Holder != id || State.Revision != revision || sequence <= lastSequence || NetworkManager.ServerTime.Time < nextShot || !Finite(direction) || direction.sqrMagnitude < .9f || direction.sqrMagnitude > 1.1f)
                return;
            if (!NetworkManager.ConnectedClients.TryGetValue(id, out var c) || c.PlayerObject == null)
                return;
            var carrier = c.PlayerObject.GetComponent<TowelCarrier>();
            var interaction = c.PlayerObject.GetComponent<PlayerInteractor>();
            if (carrier.Incapacitated || carrier.State.Item != item || MimicHunt.Instance.ComputerUsedBy(id))
                return;
            Vector3 horizontal = new Vector3(direction.x, 0, direction.z);
            if (horizontal.sqrMagnitude > .04f && Vector3.Dot(horizontal.normalized, c.PlayerObject.transform.forward) < .45f)
                return;
            direction.Normalize();
            lastSequence = sequence;
            nextShot = NetworkManager.ServerTime.Time + shotInterval;
            Vector3 origin = c.PlayerObject.transform.position + Vector3.up * c.PlayerObject.GetComponent<FirstPersonController>().EyeHeight;
            Physics.SyncTransforms();
            var hits = Physics.RaycastAll(origin, direction, 30, ~0, QueryTriggerInteraction.Ignore).Where(h => !h.collider.transform.IsChildOf(c.PlayerObject.transform) && !h.collider.transform.IsChildOf(transform)).OrderBy(h => h.distance).ToArray();
            Vector3 end = origin + direction * 30;
            bool hurt = false;
            if (hits.Length > 0)
            {
                end = hits[0].point;
                var creature = hits[0].collider.GetComponentInParent<MimicHunt>();
                if (creature != null)
                    hurt = creature.DamageOnServer(damage);
            }

            ShotRpc(origin + direction * .35f, end, hurt, hits.Length > 0);
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        void ShotRpc(Vector3 origin, Vector3 end, bool hurt, bool hit)
        {
            flashUntil = Time.time + .07f;
            AudioSource.PlayClipAtPoint(gunshot, origin, .75f);
            if (hit)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(g.GetComponent<Collider>());
                g.name = "BulletImpact";
                g.transform.position = end;
                g.transform.localScale = Vector3.one * (hurt ? .14f : .07f);
                g.GetComponent<Renderer>().sharedMaterial = impactMaterial;
                Destroy(g, .18f);
                AudioSource.PlayClipAtPoint(impact, end, hurt ? .7f : .3f);
            }

            if (LocallyHeld && hurt)
                NetworkManager.LocalClient.PlayerObject.GetComponent<PlayerInteractor>().ShowFeedback("Hit");
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ReplyRpc(string text, RpcParams rpc = default) => NetworkManager.LocalClient?.PlayerObject?.GetComponent<PlayerInteractor>()?.ShowFeedback(text);
        void Update()
        {
            if (!IsSpawned)
                return;
            if (flash != null)
                flash.SetActive(Time.time < flashUntil);
            if (!LocallyHeld)
                return;
            var player = NetworkManager.LocalClient?.PlayerObject;
            if (player == null || !Application.isFocused || player.GetComponent<TowelCarrier>().Incapacitated || player.GetComponent<PlayerInteractor>().MenuOpen || Cursor.lockState != CursorLockMode.Locked)
                return;
            if (Keyboard.current?.gKey.wasPressedThisFrame == true)
                PlaceLocal();
            if (item == ServiceItem.Firearm && Mouse.current?.leftButton.wasPressedThisFrame == true)
                FireLocal();
        }

        void LateUpdate()
        {
            if (IsSpawned)
                Apply();
        }

        void Apply()
        {
            pickup.enabled = !State.Held;
            bool below=visual.position.y<-.5f;if(below!=undergroundVisual){undergroundVisual=below;foreach(var r in allRenderers)r.renderingLayerMask=below?2u:1u;}
            if (item == ServiceItem.ThermalDecoy)
            {
                heatColor.SetColor("_EmissionColor", IsHeating ? new Color(3, .7f, .06f) : Color.black);
                heatColor.SetColor("_BaseColor", IsHeating ? new Color(1, .3f, .04f) : new Color(.18f, .1f, .08f));
                foreach (var r in fins)
                    r.SetPropertyBlock(heatColor);
            }

            if (State.Held && NetworkManager.SpawnManager.SpawnedObjects.Values.FirstOrDefault(o => o.IsPlayerObject && o.OwnerClientId == State.Holder)is NetworkObject player)
            {
                var camera = player.GetComponentInChildren<Camera>(true).transform;
                Vector3 offset = item == ServiceItem.Firearm ? new Vector3(.18f, -.18f, .3f) : new Vector3(.2f, -.43f, .6f);
                visual.SetPositionAndRotation(camera.TransformPoint(offset), camera.rotation);
            }
            else
                visual.SetPositionAndRotation(State.Position, home.rotation);
        // Collider is on the visual root; it follows the authoritative placed state.
        }

        void OnGUI()
        {
            if (!LocallyHeld || NetworkManager.LocalClient?.PlayerObject?.GetComponent<PlayerInteractor>().MenuOpen == true)
                return;
            var previous=GUI.matrix;float scale=Mathf.Min(Screen.width/900f,Screen.height/600f);
            GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
            try
            {
                float w=Screen.width/scale,h=Screen.height/scale;
                GUI.Box(new Rect(w/2-260,h-48,520,30),item==ServiceItem.Firearm?"PISTOL · LMB fire · G place · F flashlight · Ctrl crouch":"THERMAL DECOY · inactive legacy item");
                if(item==ServiceItem.Firearm&&Cursor.lockState==CursorLockMode.Locked)GUI.Label(new Rect(w/2-4,h/2-10,20,20),"+");
            }
            finally{GUI.matrix=previous;}
        }

        public bool ConfigurationValid() => home != null && visual != null && pickup != null && (item == ServiceItem.ThermalDecoy || gunshot != null && flash != null);
    }
}
