using UnityEngine;
using MotelGame.Service;
using MotelGame.Interactions;

namespace MotelGame.Horror
{
    // Authored physical graph. Junctions have no room/mouth; room branches have an actual grille.
    public sealed class VentNode : MonoBehaviour, IInteractable
    {
        public int index;
        public bool underground;
        public int sector;
        public Transform hiddenAnchor,hiddenBend;
        public BoxCollider roomVolume;
        public VentNode[] neighbours;
        public ServiceRoom room;
        public Transform mouth, landing, grille;
        public Collider inspection;
        public bool looseFan;
        public Transform fan;
        public AudioSource sound;
        public AudioClip rattle;
        float nextRattle;
        public bool CanInteract => room != null;
        public string Prompt => "Inspect ventilation";
        public void Interact()
        {
            var manager = Unity.Netcode.NetworkManager.Singleton;
            manager.LocalClient?.PlayerObject?.GetComponent<PlayerInteractor>()?.ShowFeedback(looseFan ? "Loose fan bearing: regular rattle, visibly wobbling rotor. This sound has an ordinary cause." : "Look through the grille and listen. An empty duct here says nothing about the other branches.");
        }

        void Update()
        {
            if(underground)return;
            var hunt = MimicHunt.Instance;
            bool open = hunt != null && hunt.IsSpawned && hunt.State.Node == index && hunt.State.Active && (hunt.State.Phase == MimicPhase.Infiltrating || hunt.State.Phase == MimicPhase.Emerging || hunt.State.Phase == MimicPhase.Exposed || hunt.State.Phase == MimicPhase.Attacking || hunt.State.Phase == MimicPhase.Retreating || hunt.State.Phase == MimicPhase.Dead);
            if (grille != null)
                grille.localRotation = Quaternion.RotateTowards(grille.localRotation, Quaternion.Euler(open ? -105 : 0, 0, 0), Time.deltaTime * 90);
            if (!looseFan || hunt == null || !hunt.Shift.IsRunning)
                return;
            if (fan != null)
                fan.localRotation = Quaternion.Euler(0, 0, Time.time * 210 + Mathf.Sin(Time.time * 8) * 12);
            if (Time.time >= nextRattle)
            {
                nextRattle = Time.time + 6;
                sound.PlayOneShot(rattle, .36f);
            }
        }
    }
}
