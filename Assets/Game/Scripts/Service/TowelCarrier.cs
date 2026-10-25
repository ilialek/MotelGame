using System;
using Unity.Netcode;
using UnityEngine;

namespace MotelGame.Service
{
    public enum ServiceItem : byte { None, CleanTowel, GarbageBag }
    public struct CarrySnapshot : INetworkSerializable, IEquatable<CarrySnapshot>
    {
        public ServiceItem Item;
        public NetworkObjectReference Room;
        public uint Cycle;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        { s.SerializeValue(ref Item); s.SerializeValue(ref Room); s.SerializeValue(ref Cycle); }
        public bool Equals(CarrySnapshot v) => Item == v.Item && Room.Equals(v.Room) && Cycle == v.Cycle;
    }

    // Existing service carrier, extended to a single towel OR bag. No inventory slots or item physics.
    [DisallowMultipleComponent]
    public sealed class TowelCarrier : NetworkBehaviour
    {
        [SerializeField] GameObject firstPersonTowel;
        [SerializeField] GameObject thirdPersonTowel;
        [SerializeField] GameObject firstPersonBag;
        [SerializeField] GameObject thirdPersonBag;
        readonly NetworkVariable<CarrySnapshot> carrying = new(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public CarrySnapshot State => carrying.Value;
        public bool HasItem => IsSpawned && State.Item != ServiceItem.None;
        public bool HasTowel => IsSpawned && State.Item == ServiceItem.CleanTowel;
        public bool HasBag => IsSpawned && State.Item == ServiceItem.GarbageBag;
        public string ItemName => HasBag ? "Garbage Bag" : HasTowel ? "Clean Towel" : "nothing";
        public ServiceRoom BagRoom => HasBag && State.Room.TryGet(out var obj, NetworkManager) ? obj.GetComponent<ServiceRoom>() : null;
        public bool VisibleTowel => firstPersonTowel.activeSelf || thirdPersonTowel.activeSelf;
        public bool VisibleBag => firstPersonBag.activeSelf || thirdPersonBag.activeSelf;
        public bool ConfigurationValid => firstPersonTowel != null && thirdPersonTowel != null && firstPersonBag != null && thirdPersonBag != null;
        public override void OnNetworkSpawn() { carrying.OnValueChanged += Changed; ApplyVisuals(); }
        public override void OnNetworkDespawn()
        {
            carrying.OnValueChanged -= Changed; firstPersonTowel.SetActive(false); thirdPersonTowel.SetActive(false);
            if (firstPersonBag != null) firstPersonBag.SetActive(false); if (thirdPersonBag != null) thirdPersonBag.SetActive(false);
        }
        public override void OnGainedOwnership() => ApplyVisuals();
        public override void OnLostOwnership() => ApplyVisuals();
        void Changed(CarrySnapshot before, CarrySnapshot after) => ApplyVisuals();
        void ApplyVisuals()
        {
            firstPersonTowel.SetActive(HasTowel && IsOwner);
            thirdPersonTowel.SetActive(HasTowel && !IsOwner);
            if (firstPersonBag != null) firstPersonBag.SetActive(HasBag && IsOwner);
            if (thirdPersonBag != null) thirdPersonBag.SetActive(HasBag && !IsOwner);
        }
        // Request cancellation only clears towels; unrelated bags must survive it.
        internal void SetOnServer(bool value)
        {
            if (!IsServer || !IsSpawned) return;
            if (value && !HasItem) carrying.Value = new CarrySnapshot { Item = ServiceItem.CleanTowel };
            else if (!value && HasTowel) ClearOnServer();
        }
        internal bool TryCarryBag(ServiceRoom room)
        {
            if (!IsServer || !IsSpawned || HasItem) return false;
            carrying.Value = new CarrySnapshot { Item = ServiceItem.GarbageBag, Room = new NetworkObjectReference(room.NetworkObject), Cycle = room.State.Cycle };
            return true;
        }
        internal void ClearOnServer() { if (IsServer && IsSpawned) carrying.Value = default; }
        void OnGUI()
        {
            if (!IsOwner || !HasBag || GetComponent<MotelGame.Interactions.PlayerInteractor>().MenuOpen) return;
            GUI.Box(new Rect(12, Screen.height - 140, 330, 28), $"Bag / Room {BagRoom?.RoomNumber} → Outdoor dumpster [E]");
        }
    }
}
