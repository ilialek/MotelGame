using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using MotelGame.Service;
using MotelGame.Interactions;

namespace MotelGame.Inspection
{
    public sealed class AnalyzerPanel : MonoBehaviour
    {
        BaggageInspection analyzer;
        PlayerInteractor user;
        Vector2 scroll;
        GUIStyle title, text, readout, button, frame;
        Texture2D background;
        public bool LocalOpen => user != null;
        static readonly string[] Questions = {"What did you bring?", "Explain the weight", "Tools or electronics?", "How is it packed?", "Explain that response"};
        static readonly string[] Tests = {"WEIGH", "METAL RESPONSE", "READ BALANCE", "TILT LEFT 12°", "TILT RIGHT 12°", "LEVEL PLATFORM", "CONTROLLED PULSE"};
        void Awake() => analyzer = GetComponent<BaggageInspection>();
        public void OpenLocal()
        {
            if (LocalOpen)
                return;
            var player = analyzer.NetworkManager.LocalClient?.PlayerObject;
            if (player == null || player.GetComponent<PlayerInteractor>().MenuOpen)
                return;
            user = player.GetComponent<PlayerInteractor>();
            user.MenuOpen = true;
            player.GetComponent<FirstPersonController>().InputSuspended = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void CloseLocal(bool capture = true)
        {
            if (!LocalOpen)
                return;
            if (analyzer.IsSpawned && analyzer.OwnsControls)
                analyzer.ReleaseRpc(analyzer.Seat.Lease);
            user.MenuOpen = false;
            var movement = user.GetComponent<FirstPersonController>();
            movement.InputSuspended = false;
            if (capture)
                movement.ResumeAfterComputer();
            user = null;
        }

        void OnDisable() => CloseLocal(false);
        void Update()
        {
            if (!LocalOpen)
                return;
            if (!user.IsSpawned || !analyzer.IsSpawned || !analyzer.Shift.IsRunning || user.GetComponent<TowelCarrier>().Incapacitated || Vector3.Distance(user.transform.position, analyzer.Controls.bounds.center) > 3.8f)
            {
                CloseLocal();
                return;
            }

            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                CloseLocal();
        }

        void LateUpdate()
        {
            if (!LocalOpen)
                return;
            user.MenuOpen = true;
            user.GetComponent<FirstPersonController>().InputSuspended = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Styles()
        {
            if (background != null)
                return;
            background = new Texture2D(1, 1);
            background.SetPixel(0, 0, new Color(.025f, .045f, .04f, .97f));
            background.Apply();
            frame = new GUIStyle(GUI.skin.box)
            {padding = new RectOffset(18, 18, 14, 14)};
            frame.normal.background = background;
            text = new GUIStyle(GUI.skin.label)
            {fontSize = 15, wordWrap = true};
            text.normal.textColor = new Color(.79f, .87f, .76f);
            title = new GUIStyle(text)
            {fontSize = 20, fontStyle = FontStyle.Bold};
            readout = new GUIStyle(text)
            {fontSize = 23, fontStyle = FontStyle.Bold};
            readout.normal.textColor = new Color(.94f, .78f, .38f);
            button = new GUIStyle(GUI.skin.button)
            {fontSize = 14, fixedHeight = 31, wordWrap = true};
        }

        void OnGUI()
        {
            if (!LocalOpen)
                return;
            Styles();
            float width = Mathf.Min(760, Screen.width - 24), height = Mathf.Min(680, Screen.height - 24);
            GUILayout.BeginArea(new Rect(Screen.width - width - 12, 12, width, height), frame);
            GUILayout.Label("LOAD ANALYZER / RECEPTION", title);
            GUILayout.Label(analyzer.OwnsControls ? "OPERATOR — physical tests and shared statements" : "OBSERVER — another employee controls the instrument", text);
            if (!analyzer.Seat.Occupied && GUILayout.Button("TAKE FREE CONTROLS", button))
                analyzer.RequestUse();
            scroll = GUILayout.BeginScrollView(scroll);
            var front = analyzer.Reception.WaitingNpc;
            GUILayout.Label(front == null ? "No arriving guest at the desk." : "ARRIVING: " + front.GuestName + " · closed " + front.GetComponent<GuestBaggage>().State.Shell, text);
            GUI.enabled = analyzer.OwnsControls && front != null && front.GetComponent<GuestBaggage>().State.Phase == BaggagePhase.WithGuest;
            if (GUILayout.Button("PLACE THIS GUEST'S BAG ON PLATFORM", button))
                analyzer.PlaceBagRpc(new NetworkObjectReference(front.NetworkObject), front.State.Visit);
            GUI.enabled = true;
            DrawReadings(false);
            bool ready = analyzer.OwnsControls && analyzer.Current != null && analyzer.Current.Guest == front && analyzer.Current.State.Phase == BaggagePhase.OnAnalyzer && !analyzer.State.Busy;
            GUI.enabled = ready;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Tests.Length; i++)
            {
                if (i == 3 || i == 6)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }

                if (GUILayout.Button(Tests[i], button))
                    analyzer.TestRpc((AnalyzerTest)i, analyzer.State.Revision, analyzer.State.Visit, analyzer.Seat.Lease);
            }

            GUILayout.EndHorizontal();
            GUI.enabled = true;
            GUILayout.Space(8);
            DrawQuestions(false);
            GUILayout.Label("REFERENCE: clothing usually gives low metal response. Tools may be heavy and metal-rich. Loose loads settle downhill; liquids oscillate; elastic/motorized mechanisms can return or counterbalance. Interpret readings with the explanation — the instrument does not identify contents.", text);
            GUILayout.Label("Admission and refusal: Reception computer → Baggage / Reception. Contents question + weight are the minimum; additional tests are your decision.", text);
            GUILayout.EndScrollView();
            if (GUILayout.Button("RETURN TO MOTEL [ESC]", button))
                CloseLocal();
            GUILayout.EndArea();
        }

        public void DrawComputerReview()
        {
            Styles();
            GUILayout.Label("CLOSED BAGGAGE / SHARED RECORD", title);
            GUILayout.Label("Use the physical analyzer beside Reception for measurements. You can ask questions and make the decision here.", text);
            DrawReadings(true);
            DrawQuestions(true);
            var guest = analyzer.Reception.WaitingNpc;
            if (guest == null)
                return;
            GUILayout.Space(8);
            GUILayout.Label("ADMISSION DECISION — " + guest.GuestName, title);
            GUILayout.Label(analyzer.CanAdmit(guest) ? "Basic registration complete. Compare the evidence; further tests remain available." : "Before admission: ask contents and weigh this guest's bag on the platform.", text);
            foreach (var room in analyzer.Reception.Rooms)
            {
                GUI.enabled = analyzer.Reception.CanRegister(room);
                if (GUILayout.Button("Admit → Room " + room.RoomNumber + " / " + room.AvailabilityText, button))
                    analyzer.Reception.SelectRoom(System.Array.IndexOf(analyzer.Reception.Rooms, room));
            }

            GUI.enabled = true;
            if (GUILayout.Button("REFUSE ACCOMMODATION — guest leaves with baggage", button))
                analyzer.Reception.RefuseWaitingGuest();
            GUILayout.Label("Refusal is allowed. Lost legitimate stays are recorded; justified safety refusals carry no business penalty. No live verdict is provided.", text);
        }

        void DrawReadings(bool compact)
        {
            var s = analyzer.State;
            var bag = analyzer.Current;
            GUILayout.Label(bag == null ? "NO MEASUREMENTS" : "INSTRUMENT RECORD: " + bag.Guest.GuestName + " · visit " + s.Visit, text);
            if (s.Busy)
                GUILayout.Label("MEASURING / " + Tests[(int)s.Test] + " ...", title);
            var style = compact ? text : readout;
            GUILayout.Label("MASS  " + (analyzer.Knows(AnalyzerTest.Weight) ? s.Mass.ToString("0.0") + " kg (shell included)" : "—") + "     METAL  " + (analyzer.Knows(AnalyzerTest.Metal) ? s.Metal.ToString("0") + " / 100" : "—"), style);
            GUILayout.Label("BALANCE  " + (analyzer.Knows(AnalyzerTest.Balance) ? s.Balance.ToString("+0.0;-0.0;0.0") + " cm · + right / − left" : "—") + "     PLATFORM  " + (s.Tilt == 0 ? "LEVEL" : s.Tilt < 0 ? "LEFT 12°" : "RIGHT 12°"), text);
            if (analyzer.Knows(AnalyzerTest.Pulse))
                GUILayout.Label($"PULSE RESPONSE: balance {s.BeforePulse:+0.0;-0.0;0.0} → {s.AfterPulse:+0.0;-0.0;0.0} cm\nResponse onset {s.Delay:0.00} s · decay {s.Decay:0.0} s · movement amplitude {s.Amplitude:0.0} cm", text);
            GUILayout.Label("Tilt, return level, then pulse to compare displacement and recovery. Each pulse is the same right-directed mechanical stimulus.", text);
        }

        void DrawQuestions(bool atComputer)
        {
            var guest = analyzer.Reception.WaitingNpc;
            bool matching = guest != null && analyzer.Current != null && analyzer.Current.Guest == guest && analyzer.State.Visit == guest.State.Visit;
            GUILayout.Label("GUEST STATEMENTS — separate from measurements", title);
            foreach (var line in analyzer.Statements)
                GUILayout.Label(Questions[line.Question] + "\n“" + line.Answer + "”", text);
            for (int i = 0; i < Questions.Length; i++)
            {
                bool known = matching && (analyzer.State.Asked & (1u << i)) != 0;
                bool available = i == 0 || i == 3 || matching && (i == 1 && analyzer.Knows(AnalyzerTest.Weight) || i == 2 && analyzer.Knows(AnalyzerTest.Metal) || i == 4 && analyzer.Knows(AnalyzerTest.Pulse));
                GUI.enabled = guest != null && (atComputer || analyzer.OwnsControls) && !known && available;
                if (GUILayout.Button((known ? "Asked · " : "Ask · ") + Questions[i], button))
                    analyzer.AskRpc(new NetworkObjectReference(guest.NetworkObject), guest.State.Visit, (BaggageQuestion)i, atComputer ? analyzer.Computer.Lease : analyzer.Seat.Lease, atComputer);
            }

            GUI.enabled = true;
        }

        void OnDestroy()
        {
            if (background != null)
                Destroy(background);
        }
    }
}
