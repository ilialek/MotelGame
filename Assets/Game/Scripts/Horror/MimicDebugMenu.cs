using UnityEngine;
using UnityEngine.InputSystem;
using MotelGame.Service;
using MotelGame.Interactions;
using MotelGame.Inspection;

namespace MotelGame.Horror
{
    public sealed class MimicDebugMenu : MonoBehaviour
    {
        public MimicHunt hunt;
        bool open, advanced;
        int carrierIndex, nodeIndex;
        Vector2 scroll;
        GUIStyle help;
        void Line(string text) => GUILayout.Label(text, help);
        void Close()
        {
            open = false;
            var player = hunt.NetworkManager.LocalClient?.PlayerObject;
            if (player == null)
                return;
            player.GetComponent<PlayerInteractor>().MenuOpen = false;
            player.GetComponent<FirstPersonController>().ResumeAfterComputer();
        }

        void Update()
        {
            if (!hunt.IsSpawned || !hunt.IsHost || hunt.NetworkManager.LocalClient?.PlayerObject == null)
            {
                open = false;
                return;
            }

            if (open && Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                Close();
                return;
            }
            if (Keyboard.current?.f8Key.wasPressedThisFrame != true)
                return;
            if (open)
            {
                Close();
                return;
            }

            if (FindFirstObjectByType<ReceptionComputer>().LocalOpen || FindFirstObjectByType<AnalyzerPanel>().LocalOpen)
                return;
            open = true;
            hunt.NetworkManager.LocalClient.PlayerObject.GetComponent<PlayerInteractor>().MenuOpen = true;
        }

        void LateUpdate()
        {
            if (!open)
                return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            hunt.NetworkManager.LocalClient.PlayerObject.GetComponent<PlayerInteractor>().MenuOpen = true;
        }

        void OnGUI()
        {
            if (!hunt.IsSpawned || !hunt.IsHost || hunt.NetworkManager.LocalClient?.PlayerObject == null)
                return;
            if (!open)
            {
                GUI.Label(new Rect(Screen.width - 180, 12, 175, 24), "F8 · Test scenarios (host)");
                return;
            }

            help ??= new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            var scenario = HuntScenario.Instance;
            GUILayout.BeginArea(new Rect(15, 15, Mathf.Min(570, Screen.width - 30), Screen.height - 30), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            Line("MOTEL PLAYTEST SCENARIOS · HOST ONLY");
            Line("Each scenario starts a fresh shift, prepares rooms and returns equipment. Players stay connected. Ordinary shifts remain 15 minutes.");
            GUILayout.Space(10);
            if (GUILayout.Button("A · Inspection practice — 12 minutes", GUILayout.Height(32)))
                scenario.Begin(HuntTestMode.Inspection);
            Line($"Question guests and measure {scenario.inspection.CaseCount} cases. Decide manually. Short stays, automatic checkout/cleaning; no creature release in this practice mode.");
            if (GUILayout.Button("B · Complete gameplay — 10 minutes", GUILayout.Height(32)))
                scenario.Begin(HuntTestMode.Complete);
            Line("Inspect and admit four arrivals yourself. One carrier. Refusing that carrier prevents infiltration. Accepted baggage can start the hunt.");
            if (GUILayout.Button("C · Hunt only — 10 minutes", GUILayout.Height(32)))
                scenario.Begin(HuntTestMode.HuntOnly);
            Line("Three guests walk to rooms automatically. Hunt begins when all arrive: one protected carrier and two potential victims. No inspection setup needed.");
            GUILayout.Space(12);
            Line(scenario.Description);
            int remaining = Mathf.CeilToInt((float)System.Math.Max(0, hunt.Shift.State.Duration - hunt.Shift.Elapsed));
            int activeGuests = 0;
            foreach (var guest in hunt.Shift.Reception.Guests)
                if (guest.Alive)
                    activeGuests++;
            Line($"Shift: {hunt.Shift.State.Phase} · remaining {remaining / 60:00}:{remaining % 60:00} · active guests: {activeGuests}");
            Line($"Threat: {(hunt.State.Active ? hunt.State.Phase.ToString() : "inactive")} · eligible victims: {hunt.ValidVictims}\nInitial exploration: {hunt.InitialExplorationSeconds:0}s. Heat bait can interrupt it immediately.");
            if (GUILayout.Button("Reset current scenario — clear baggage, threat and equipment"))
                scenario.ResetScenario();
            if (GUILayout.Button("Finish shift and inspect results"))
                hunt.Shift.EndForHuntDebug();
            GUILayout.Space(10);
            advanced = GUILayout.Toggle(advanced, "Advanced diagnostics — reveal host-only ground truth");
            if (advanced)
            {
                var guests = hunt.Shift.Reception.Guests;
                for (int i = 0; i < guests.Length; i++)
                {
                    var bag = guests[i].GetComponent<GuestBaggage>();
                    if (GUILayout.Button((i == carrierIndex ? "> " : "") + guests[i].GuestName + " / " + guests[i].Phase + " / " + (bag.ServerCase == null ? "no bag" : bag.ServerCase.name)))
                        carrierIndex = i;
                }

                for (int i = 0; i < 4; i++)
                    if (GUILayout.Button((i == nodeIndex ? "> " : "") + "Manual start: Room " + hunt.Nodes[i].room.RoomNumber))
                        nodeIndex = i;
                GUI.enabled = !hunt.State.Active && guests[carrierIndex].Alive && hunt.Shift.IsRunning;
                if (GUILayout.Button("Manual activation — selected guest protected"))
                    hunt.ActivateOnServer(nodeIndex, guests[carrierIndex]);
                GUI.enabled = true;
                if (GUILayout.Button("Harmless fan complaint — Room 104"))
                    hunt.HarmlessComplaintOnServer();
                if (GUILayout.Button("Reset threat and equipment only"))
                    hunt.ResetOnServer();
                Line($"Node {hunt.State.Node} → {hunt.DebugNextNode} / HP {hunt.State.Health}\nCarrier {hunt.DebugCarrier?.GuestName} / target {hunt.DebugTarget}");
            }

            GUILayout.Space(8);
            Line("E: use / take / evacuate · G: place equipment · LMB: fire\nReception → Vent CCTV. Solo: place the pistol beside your ambush position before deploying bait, then pick it up again.");
            GUILayout.EndScrollView();
            if (GUILayout.Button("Return to motel [F8 / Esc]", GUILayout.Height(30)))
                Close();
            GUILayout.EndArea();
        }
    }
}
