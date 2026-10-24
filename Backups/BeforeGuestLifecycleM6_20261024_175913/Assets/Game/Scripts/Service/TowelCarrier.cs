using Unity.Netcode;
using UnityEngine;

namespace MotelGame.Service
{
    // One server-owned item flag, not an inventory. Visuals need no NetworkObjects.
    [DisallowMultipleComponent]
    public sealed class TowelCarrier : NetworkBehaviour
    {
        [SerializeField] GameObject firstPersonTowel;
        [SerializeField] GameObject thirdPersonTowel;
        readonly NetworkVariable<bool> carrying = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public bool HasTowel => IsSpawned && carrying.Value;
        public bool VisibleTowel => firstPersonTowel.activeSelf || thirdPersonTowel.activeSelf;
        public bool ConfigurationValid => firstPersonTowel != null && thirdPersonTowel != null;

        public override void OnNetworkSpawn()
        {
            carrying.OnValueChanged += Changed;
            ApplyVisuals();
        }
        public override void OnNetworkDespawn()
        {
            carrying.OnValueChanged -= Changed;
            firstPersonTowel.SetActive(false);
            thirdPersonTowel.SetActive(false);
        }
        public override void OnGainedOwnership() => ApplyVisuals();
        public override void OnLostOwnership() => ApplyVisuals();
        void Changed(bool before, bool after) => ApplyVisuals();
        void ApplyVisuals()
        {
            firstPersonTowel.SetActive(HasTowel && IsOwner);
            thirdPersonTowel.SetActive(HasTowel && !IsOwner);
        }
        internal void SetOnServer(bool value)
        {
            if (IsServer && IsSpawned) carrying.Value = value;
        }
    }
}
