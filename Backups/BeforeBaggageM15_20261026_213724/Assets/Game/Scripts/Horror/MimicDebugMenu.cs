using UnityEngine;
using UnityEngine.InputSystem;
using MotelGame.Service;
using MotelGame.Interactions;

namespace MotelGame.Horror
{
    public sealed class MimicDebugMenu : MonoBehaviour
    {
        public MimicHunt hunt;
        bool open;
        int carrierIndex, nodeIndex;
        Vector2 scroll;
        void Update()
        {
            if (!hunt.IsSpawned || !hunt.IsHost || hunt.NetworkManager.LocalClient?.PlayerObject == null)
            {
                open = false;
                return;
            }

            if (Keyboard.current?.f8Key.wasPressedThisFrame == true)
            {
                if (FindFirstObjectByType<ReceptionComputer>().LocalOpen)
                    return;
                open = !open;
                var player = hunt.NetworkManager.LocalClient.PlayerObject;
                player.GetComponent<PlayerInteractor>().MenuOpen = open;
                if (open)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else
                    player.GetComponent<FirstPersonController>().ResumeAfterComputer();
            }
        }

        void OnGUI()
        {
            if (!hunt.IsSpawned || !hunt.IsHost || hunt.NetworkManager.LocalClient?.PlayerObject == null)
                return;
            if (!open)
            {
                GUI.Label(new Rect(Screen.width - 165, 12, 160, 24), "F8 · M14 debug (host)");
                return;
            }

            GUILayout.BeginArea(new Rect(15, 15, 440, Mathf.Min(Screen.height - 30, 560)), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("DEBUG — M14 MIMIC HUNT / HOST ONLY");
            GUILayout.Label("Quick setup starts a short shift, prepares rooms and auto-registers two real walking guests. Wait for both to enter rooms.");
            if (GUILayout.Button("Prepare quick playtest (8-minute shift)"))
                hunt.PrepareQuickTestOnServer();
            var guests = hunt.Shift.Reception.Guests;
            for (int i = 0; i < guests.Length; i++)
                if (GUILayout.Button((i == carrierIndex ? "> " : "") + guests[i].GuestName + " — " + guests[i].Phase))
                    carrierIndex = i;
            GUILayout.Label("Designated carrier is excluded from hunting. Other guests remain vulnerable.");
            for (int i = 0; i < 4; i++)
                if (GUILayout.Button((i == nodeIndex ? "> " : "") + "Activate at Room " + hunt.Nodes[i].room.RoomNumber + " vent"))
                    nodeIndex = i;
            GUI.enabled = !hunt.State.Active && guests[carrierIndex].Alive && hunt.Shift.IsRunning;
            if (GUILayout.Button("ACTIVATE MIMIC"))
                hunt.ActivateOnServer(nodeIndex, guests[carrierIndex]);
            GUI.enabled = true;
            if (GUILayout.Button("Force harmless fan complaint"))
                hunt.HarmlessComplaintOnServer();
            if (GUILayout.Button("Reset threat + return equipment (keep shift)"))
                hunt.ResetOnServer();
            if (GUILayout.Button("Finish current shift (debug)"))
                hunt.Shift.EndForHuntDebug();
            GUILayout.Label($"Active {hunt.State.Active} / {hunt.State.Phase}\nNode {hunt.State.Node} → {hunt.DebugNextNode} / HP {hunt.State.Health}\nCarrier {hunt.DebugCarrier?.GuestName} / target {hunt.DebugTarget}");
            GUILayout.Label("E: inspect / take / evacuate. G: place equipment. LMB: fire. Reception → Vent CCTV. Stage pistol beside the vent before placing bait for solo play.");
            GUILayout.EndScrollView();
            if (GUILayout.Button("Return to motel [F8]"))
            {
                open = false;
                var p = hunt.NetworkManager.LocalClient.PlayerObject;
                p.GetComponent<PlayerInteractor>().MenuOpen = false;
                p.GetComponent<FirstPersonController>().ResumeAfterComputer();
            }

            GUILayout.EndArea();
        }
    }
}
