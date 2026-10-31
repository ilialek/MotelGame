using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using MotelGame.Service;
using MotelGame.Interactions;

namespace MotelGame.Inspection
{
    // Local presentation only. Both workstations read the same server-owned inspection record.
    public sealed class AnalyzerPanel : MonoBehaviour
    {
        BaggageInspection analyzer;
        PlayerInteractor user;
        Vector2 scroll;
        GUIStyle title, text, button, frame;
        Texture2D background;
        public bool LocalOpen => user != null;
        public static readonly AnalyzerTest[] PrimaryTests = { AnalyzerTest.Weight, AnalyzerTest.Metal, AnalyzerTest.Pulse };
        static readonly string[] Questions = { "What did you bring?", "Can you explain the weight?", "What metal is inside?", "Is anything loose or powered?", "What caused that response?" };
        void Awake() => analyzer = GetComponent<BaggageInspection>();
        public void OpenLocal()
        {
            if (LocalOpen) return;
            var player = analyzer.NetworkManager.LocalClient?.PlayerObject;
            if (player == null || player.GetComponent<PlayerInteractor>().MenuOpen) return;
            user = player.GetComponent<PlayerInteractor>();
            user.MenuOpen = true;
            player.GetComponent<FirstPersonController>().InputSuspended = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        public void CloseLocal(bool capture = true)
        {
            if (!LocalOpen) return;
            if (analyzer.IsSpawned && analyzer.OwnsControls) analyzer.ReleaseRpc(analyzer.Seat.Lease);
            user.MenuOpen = false;
            var movement = user.GetComponent<FirstPersonController>();
            movement.InputSuspended = false;
            if (capture) movement.ResumeAfterComputer();
            user = null;
        }
        void OnDisable() => CloseLocal(false);
        void Update()
        {
            if (!LocalOpen) return;
            if (!user.IsSpawned || !analyzer.IsSpawned || !analyzer.Shift.IsRunning || user.GetComponent<TowelCarrier>().Incapacitated || Vector3.Distance(user.transform.position, analyzer.Controls.bounds.center) > 3.8f)
            { CloseLocal(); return; }
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true) CloseLocal();
        }
        void LateUpdate()
        {
            if (!LocalOpen) return;
            user.MenuOpen = true; user.GetComponent<FirstPersonController>().InputSuspended = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        void Styles()
        {
            if (background != null) return;
            background = new Texture2D(1, 1);
            background.SetPixel(0, 0, new Color(.025f, .045f, .04f, .97f)); background.Apply();
            frame = new GUIStyle(GUI.skin.box) { padding = new RectOffset(18, 18, 14, 14) };
            frame.normal.background = background;
            text = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            text.normal.textColor = new Color(.79f, .87f, .76f);
            title = new GUIStyle(text) { fontSize = 19, fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { fontSize = 14, fixedHeight = 32, wordWrap = true };
        }
        public static string TestName(AnalyzerTest test) => test switch
        { AnalyzerTest.Weight => "WEIGH", AnalyzerTest.Metal => "METAL RESPONSE", AnalyzerTest.Pulse => "CONTROLLED PULSE", _ => "ADVANCED TEST" };
        public static string PulseDescription(float amplitude, float delay, float decay, float before, float after)
        {
            if (amplitude < .1f) return "Almost no movement; a brief contact sound.";
            string strength = amplitude < 1 ? "Small movement" : "Clear movement";
            string onset = delay < .2f ? "immediately" : "after a short delay";
            string duration = decay < 1 ? "settles quickly" : decay < 2.5f ? "continues briefly" : "continues for several seconds";
            float displacement = after - before;
            string motion = displacement < -.15f ? "moves back against the push" : displacement > .15f ? "shifts with the push" : "returns near its starting position";
            return $"{strength} {onset}; {duration}, {motion}.";
        }
        string Reading(AnalyzerTest test)
        {
            if (!analyzer.MatchesWaitingGuest || !analyzer.Knows(test)) return "Not tested";
            var s = analyzer.State;
            return test switch
            {
                AnalyzerTest.Weight => $"{s.Mass:0.0} kg including the bag",
                AnalyzerTest.Metal => $"{s.Metal:0} / 100 — " + (s.Metal < 10 ? "low" : s.Metal < 35 ? "moderate" : "strong") + " metallic response",
                _ => PulseDescription(s.Amplitude, s.Delay, s.Decay, s.BeforePulse, s.AfterPulse)
            };
        }
        void OnGUI()
        {
            if (!LocalOpen) return;
            Styles();
            float width = Mathf.Min(740, Screen.width - 24), height = Mathf.Min(700, Screen.height - 24);
            GUILayout.BeginArea(new Rect(Screen.width - width - 12, 12, width, height), frame);
            GUILayout.Label("BAGGAGE ANALYZER", title);
            GUILayout.Label(analyzer.OwnsControls ? "Your controls · shared record" : "Read-only observer · another employee has the controls", text);
            if (!analyzer.Seat.Occupied && GUILayout.Button("TAKE FREE CONTROLS", button)) analyzer.RequestUse();
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("Ask what they brought → load the bag → choose tests → decide at Reception. Weight and the first answer are required; extra tests are your choice.", text);
            var guest = analyzer.Reception.WaitingNpc;
            GUILayout.Label(guest == null ? "No arriving guest at Reception." : guest.GuestName + " · closed " + guest.GetComponent<GuestBaggage>().State.Shell, title);
            DrawQuestion(BaggageQuestion.Contents);
            if (analyzer.MatchesWaitingGuest)
                GUILayout.Label("GUEST: “" + analyzer.Statement(BaggageQuestion.Contents) + "”", text);
            GUI.enabled = analyzer.OwnsControls && guest != null && guest.GetComponent<GuestBaggage>().State.Phase == BaggagePhase.WithGuest;
            if (GUILayout.Button("LOAD THIS GUEST'S BAG", button)) analyzer.PlaceBagRpc(new NetworkObjectReference(guest.NetworkObject), guest.State.Visit);
            GUI.enabled = true;
            bool ready = analyzer.OwnsControls && analyzer.MatchesWaitingGuest && analyzer.Current.State.Phase == BaggagePhase.OnAnalyzer && !analyzer.State.Busy;
            if (analyzer.MatchesWaitingGuest && analyzer.State.Busy) GUILayout.Label("RUNNING: " + TestName(analyzer.State.Test) + "…", title);
            for (int i = 0; i < PrimaryTests.Length; i++)
            {
                var test = PrimaryTests[i];
                GUILayout.Space(7);
                GUILayout.Label(TestName(test) + " — " + (i == 0 ? "Total mass, including the bag." : i == 1 ? "Approximate metallic content; not a list of objects." : "A repeatable gentle push. Listen and watch how the load responds."), text);
                GUILayout.Label(Reading(test), title);
                GUI.enabled = ready;
                if (GUILayout.Button((analyzer.MatchesWaitingGuest && analyzer.Knows(test) ? "DONE · repeat " : "RUN ") + TestName(test), button))
                    analyzer.TestRpc(test, analyzer.State.Revision, analyzer.State.Visit, analyzer.Seat.Lease);
                GUI.enabled = true;
                var question = i == 0 ? BaggageQuestion.Weight : i == 1 ? BaggageQuestion.Metal : BaggageQuestion.Response;
                if (analyzer.MatchesWaitingGuest && analyzer.WasAsked(question)) GUILayout.Label("GUEST: “" + analyzer.Statement(question) + "”", text);
                else DrawQuestion(question);
            }
            GUILayout.Space(8);
            if (analyzer.MatchesWaitingGuest && analyzer.WasAsked(BaggageQuestion.Packing)) GUILayout.Label("PACKING: “" + analyzer.Statement(BaggageQuestion.Packing) + "”", text);
            else DrawQuestion(BaggageQuestion.Packing);
            GUILayout.Label("COMPARE, DON'T GUESS: heavy is not dangerous by itself. Metal can come from tools or fittings. Water, springs and powered equipment can move. Compare the response with the guest's explanation.", text);
            if (analyzer.MatchesWaitingGuest && analyzer.History.Count > 0)
            {
                GUILayout.Space(8); GUILayout.Label("MEASUREMENT HISTORY", title);
                foreach (var h in analyzer.History)
                    GUILayout.Label(TestName(h.Test) + " · " + h.Result, text);
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("RETURN TO MOTEL [ESC] · decisions at Reception", button)) CloseLocal();
            GUILayout.EndArea();
        }
        // This view intentionally has no questioning or measurement controls.
        public void DrawComputerReview()
        {
            Styles();
            var guest = analyzer.Reception.WaitingNpc;
            if (guest == null) { GUILayout.Label("No arriving guest to register.", text); return; }
            GUILayout.Label("INSPECTION SUMMARY — " + guest.GuestName, title);
            GUILayout.Label("Read-only · questions and measurements at the physical analyzer beside this desk.", text);
            if (!analyzer.MatchesWaitingGuest) GUILayout.Label("This guest has not been inspected yet.", text);
            else
            {
                GUILayout.Label("GUEST: “" + analyzer.Statement(BaggageQuestion.Contents) + "”", text);
                foreach (var test in PrimaryTests) GUILayout.Label(TestName(test) + ": " + Reading(test), text);
                foreach (var line in analyzer.Statements)
                    if (line.Question != (byte)BaggageQuestion.Contents) GUILayout.Label(Questions[line.Question] + " “" + line.Answer + "”", text);
                if (analyzer.State.Busy) GUILayout.Label("A test is running. Wait for its result before admission.", text);
            }
            GUILayout.Space(8);
            GUILayout.Label(analyzer.CanAdmit(guest) ? "Record available. Admission is your team's decision." : "Required before admission: contents answer + weighing at the analyzer.", text);
            foreach (var room in analyzer.Reception.Rooms)
            {
                GUI.enabled = analyzer.Reception.CanRegister(room);
                if (GUILayout.Button("Admit → Room " + room.RoomNumber + " / " + room.AvailabilityText, button)) analyzer.Reception.SelectRoom(Array.IndexOf(analyzer.Reception.Rooms, room));
            }
            GUI.enabled = true;
            if (GUILayout.Button("REFUSE ACCOMMODATION", button)) analyzer.Reception.RefuseWaitingGuest();
            GUILayout.Label("The guest leaves with their bag. Refusing an ordinary guest loses a stay; justified refusals carry no business penalty.", text);
        }
        void DrawQuestion(BaggageQuestion question)
        {
            if (analyzer.MatchesWaitingGuest && analyzer.WasAsked(question)) return;
            bool available = analyzer.QuestionAvailable(question);
            GUI.enabled = analyzer.OwnsControls && analyzer.Reception.WaitingNpc != null && available;
            if (GUILayout.Button(Questions[(int)question] + (available ? "" : " · after the matching test"), button))
            {
                var guest = analyzer.Reception.WaitingNpc;
                analyzer.AskRpc(new NetworkObjectReference(guest.NetworkObject), guest.State.Visit, question, analyzer.Seat.Lease, false);
            }
            GUI.enabled = true;
        }
        void OnDestroy() { if (background != null) Destroy(background); }
    }
}
