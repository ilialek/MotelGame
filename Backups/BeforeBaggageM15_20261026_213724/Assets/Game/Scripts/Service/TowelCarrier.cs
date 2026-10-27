using System;
using Unity.Netcode;
using UnityEngine;

namespace MotelGame.Service
{
    public enum ServiceItem : byte { None, CleanTowel, GarbageBag, ThermalDecoy, Firearm }
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
        readonly NetworkVariable<double> stunnedUntil=new(0,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
        public bool Incapacitated => IsSpawned && NetworkManager.ServerTime.Time < stunnedUntil.Value;
        internal bool TryCarryEquipment(ServiceItem item)
        {
            if(!IsServer||!IsSpawned||HasItem||Incapacitated||(item!=ServiceItem.ThermalDecoy&&item!=ServiceItem.Firearm))return false;
            carrying.Value=new CarrySnapshot{Item=item};return true;
        }
        internal void ReleaseEquipment(ServiceItem item) { if(IsServer&&State.Item==item)ClearOnServer(); }
        internal void StunOnServer(float seconds){if(IsServer&&IsSpawned)stunnedUntil.Value=NetworkManager.ServerTime.Time+seconds;}
        internal void ClearStunOnServer(){if(IsServer&&IsSpawned)stunnedUntil.Value=0;}
        void Update(){if(IsOwner)GetComponent<FirstPersonController>().TemporarilyIncapacitated=Incapacitated;}
        public CarrySnapshot State => carrying.Value;
        public bool HasItem => IsSpawned && State.Item != ServiceItem.None;
        public bool HasTowel => IsSpawned && State.Item == ServiceItem.CleanTowel;
        public bool HasBag => IsSpawned && State.Item == ServiceItem.GarbageBag;
        public string ItemName => State.Item == ServiceItem.ThermalDecoy ? "Thermal Decoy" : State.Item == ServiceItem.Firearm ? "Pistol" : HasBag ? "Garbage Bag" : HasTowel ? "Clean Towel" : "nothing";
        public ServiceRoom BagRoom => HasBag && State.Room.TryGet(out var obj, NetworkManager) ? obj.GetComponent<ServiceRoom>() : null;
        public bool VisibleTowel => firstPersonTowel.activeSelf || thirdPersonTowel.activeSelf;
        public bool VisibleBag => firstPersonBag.activeSelf || thirdPersonBag.activeSelf;
        public bool ConfigurationValid => firstPersonTowel != null && thirdPersonTowel != null && firstPersonBag != null && thirdPersonBag != null;
        public override void OnNetworkSpawn() { carrying.OnValueChanged += Changed; ApplyVisuals(); }
        public override void OnNetworkDespawn()
        {
            GetComponent<FirstPersonController>().TemporarilyIncapacitated=false;
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
            if(IsOwner&&Incapacitated){GUI.color=new Color(.8f,.1f,.06f,.55f);GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);GUI.color=Color.white;GUI.Box(new Rect(Screen.width/2-150,Screen.height/2-50,300,30),"STAGGERED — regain your footing");}
            if (!IsOwner || !HasBag || GetComponent<MotelGame.Interactions.PlayerInteractor>().MenuOpen) return;
            GUI.Box(new Rect(12, Screen.height - 140, 330, 28), $"Bag / Room {BagRoom?.RoomNumber} → Outdoor dumpster [E]");
        }
    }
}
