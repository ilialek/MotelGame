using UnityEngine;
using MotelGame.Interactions;
namespace MotelGame.Service
{
    public sealed class ComputerInteraction : MonoBehaviour,IInteractable
    {
        [SerializeField] ReceptionComputer computer;
        public ReceptionComputer Computer=>computer;
        public bool CanInteract=>computer!=null&&computer.Available;
        public string Prompt=>computer!=null&&computer.Occupied?"Reception computer — in use":"Use Reception computer";
        public void Interact(){if(CanInteract)computer.RequestUse();}
    }
}
