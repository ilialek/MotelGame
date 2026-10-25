using Unity.Netcode;
using UnityEngine;
using MotelGame.Interactions;
namespace MotelGame.Service
{
    public sealed class TelevisionControl : MonoBehaviour,IInteractable
    {
        [SerializeField] RoomTelevision television;
        [SerializeField,Range(0,1)] byte step;
        [SerializeField] Collider targetCollider;
        public RoomTelevision Television=>television;
        public byte Step=>step;
        public Collider TargetCollider=>targetCollider;
        public Vector3 AimPoint=>targetCollider.bounds.center;
        public bool CanInteract=>television!=null&&television.Requests.AcceptingWork&&television.Broken&&
            television.Requests.Phase==GuestRequestPhase.PendingDelivery&&television.Requests.State.RepairStep==step;
        public string Prompt=>CanInteract?(step==0?"Connect TV power cable":"Reset TV tuning"):
            television!=null&&television.Broken?"TV fault — answer the call / check cable first":"Television — working";
        public void Interact(){if(CanInteract)television.Requests.RepairRpc(new NetworkObjectReference(television.Room.NetworkObject),step,television.Requests.State.RequestId);}
    }
}
