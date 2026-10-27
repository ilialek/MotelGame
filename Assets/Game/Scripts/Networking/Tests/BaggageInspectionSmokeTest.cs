#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using MotelGame.Horror;
using MotelGame.Inspection;
using MotelGame.Service;
using MotelGame.Interactions;

namespace MotelGame.Networking.Tests
{
    [DefaultExecutionOrder(250)]
    public sealed class BaggageInspectionSmokeTest : MonoBehaviour
    {
        string role, mode, dir;
        DevelopmentSession session;
        MimicHunt hunt;
        NetworkObject own;
        PlayerInteractor user;
        readonly List<string> checks = new(), errors = new();
        Vector3 previous;
        uint revision;
        float previousAt;
        bool sampled;
        float ductDistance;
        bool Host => role == "host" || role == "solo";
        bool Solo => role == "solo";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            var role = OnlineIdentity.Argument("-motel-baggage-test");
            if (role == null)
                return;
            var t = new GameObject("M15SmokeTest").AddComponent<BaggageInspectionSmokeTest>();
            t.role = role;
            t.mode = OnlineIdentity.Argument("-motel-baggage-mode") ?? "success";
            t.dir = OnlineIdentity.Argument("-motel-baggage-results");
        }

        void Awake() => Application.logMessageReceived += Log;
        void Log(string text, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(text);
        }

        void Require(bool value, string why)
        {
            if (!value)
                throw new Exception(why);
        }

        void Mark(string name) => File.WriteAllText(Path.Combine(dir, name + ".signal"), "ready");
        bool Has(string name) => File.Exists(Path.Combine(dir, name + ".signal"));
        IEnumerator Wait(Func<bool> condition, string step, float seconds = 100)
        {
            File.WriteAllText(Path.Combine(dir, role + ".progress"), step);
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                Require(Time.realtimeSinceStartup < end, "Timeout " + step + " at=" + (own != null ? own.transform.position.ToString() : "no player") + " mimic=" + hunt?.State.Phase + " hp=" + hunt?.State.Health + " shift=" + hunt?.Shift.State.Phase + "/" + hunt?.Shift.State.Number + " feedback=" + user?.Feedback);
                yield return null;
            }
        }

        IEnumerator Barrier(string name)
        {
            if (Solo)
                yield break;
            Mark(name + "-" + role);
            yield return Wait(() => Has(name + "-host") && Has(name + "-client"), name, 300);
        }

        IEnumerator Start()
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(Run());
            while (stack.Count > 0)
            {
                object n = null;
                bool more = false;
                Exception failure = null;
                try
                {
                    more = stack.Peek().MoveNext();
                    if (more)
                        n = stack.Peek().Current;
                }
                catch (Exception e)
                {
                    failure = e;
                }

                if (failure != null)
                {
                    Finish(false, failure.ToString());
                    yield break;
                }

                if (!more)
                {
                    stack.Pop();
                    continue;
                }

                if (n is IEnumerator sub)
                    stack.Push(sub);
                else
                    yield return n;
            }

            Finish(errors.Count == 0, "Completed; no critical runtime logs.");
        }

        float stateAt;
        MimicPhase lastPhase;
        int lastDeaths;
        void LateUpdate()
        {
            if (scanner != null && scanner.IsSpawned && Host && Time.realtimeSinceStartup > stateAt)
            {
                stateAt = Time.realtimeSinceStartup + 2;
                File.WriteAllText(Path.Combine(dir, role + ".state"), string.Join("\n", scanner.Reception.Guests.Select(g => g.GuestName + " " + g.Phase + " at " + g.transform.position + " blocked=" + g.State.Blocked + " bag=" + g.GetComponent<GuestBaggage>().State.Phase)) + "\nHunt " + hunt.State.Phase + " at " + hunt.transform.position + " age " + hunt.HuntAge);
            }

            if (hunt == null || !hunt.IsSpawned || !hunt.State.Active)
            {
                sampled = false;
                return;
            }

            if (Host && (lastPhase != hunt.State.Phase || lastDeaths != hunt.Shift.State.GuestDeaths))
            {
                lastPhase = hunt.State.Phase;
                lastDeaths = hunt.Shift.State.GuestDeaths;
                File.AppendAllText(Path.Combine(dir, "hunt.timeline"), $"{hunt.HuntAge:0.00}s {lastPhase} node {hunt.State.Node} hp {hunt.State.Health} deaths {lastDeaths} at {hunt.transform.position}\n");
            }

            if (sampled && Host && revision == hunt.State.Revision)
            {
                float d = Vector3.Distance(previous, hunt.transform.position);
                float dt = Time.realtimeSinceStartup - previousAt;
                if (d > Mathf.Max(.15f, dt * 2.1f))
                    errors.Add("Noncontinuous Mimic motion: " + d + " / " + dt);
                if (hunt.State.Phase == MimicPhase.MovingThroughDucts || hunt.State.Phase == MimicPhase.Exploring)
                    ductDistance += d;
                if (hunt.State.Phase == MimicPhase.Infiltrating)
                {
                    sawInfiltration = true;
                    infiltrationDistance += d;
                }

                if (hunt.State.Phase == MimicPhase.Exploring)
                    sawExploration = true;
            }

            previous = hunt.transform.position;
            revision = hunt.State.Revision;
            previousAt = Time.realtimeSinceStartup;
            sampled = true;
        }

        IEnumerator Connect()
        {
            Require(session.StartSession(Host), "NGO startup failed");
            yield return Wait(() => session.Manager.LocalClient?.PlayerObject != null, "owned player");
            own = session.Manager.LocalClient.PlayerObject;
            user = own.GetComponent<PlayerInteractor>();
        }

        BaggageInspection scanner;
        ReceptionComputer computer;
        HuntScenario scenario;
        GuestLifecycle carrierGuest;
        bool sawInfiltration, sawExploration;
        float infiltrationDistance;
        IEnumerator Run()
        {
            yield return null;
            Directory.CreateDirectory(dir);
            session = FindFirstObjectByType<DevelopmentSession>();
            hunt = FindFirstObjectByType<MimicHunt>();
            scanner = FindFirstObjectByType<BaggageInspection>();
            computer = scanner.Computer;
            scenario = HuntScenario.Instance;
            session.TransportSettings.Port = (ushort)(18920 + (mode == "refuse" ? 1 : mode == "failure" ? 2 : mode == "edges" ? 4 : mode == "practice" ? 5 : mode == "hunt-only" ? 6 : Solo ? 3 : 0));
            session.TransportSettings.Address = "127.0.0.1";
            yield return Connect();
            if (role.StartsWith("late"))
            {
                if (role == "late-inspection")
                {
                    yield return Wait(() => scanner.Current != null && scanner.Knows(AnalyzerTest.Weight) && scanner.Statements.Count >= 3, "late shared inspection");
                    Require(scanner.Current.State.Phase == BaggagePhase.OnAnalyzer && scanner.Current.visual.gameObject.activeSelf, "Late bag absent");
                    Require(scanner.Current.ServerCase == null && !scanner.OwnsControls, "Private case or control exposed to observer");
                    checks.Add("Late join reconstructed shell, platform, revealed statements, readings and occupied instrument lease; no case truth on client.");
                }
                else
                {
                    yield return Wait(() => hunt.State.Active, "late active hunt");
                    Require(hunt.State.Health > 0 && scanner.Reception.Guests.All(g => !g.Agent.enabled), "Late authoritative state invalid");
                    checks.Add("Late join after infiltration reconstructed active creature and server-simulated guests.");
                }

                Mark(role + "-checked");
                yield break;
            }

            yield return Barrier("connected");
            if (mode == "edges")
            {
                yield return Edges();
                yield break;
            }

            if (mode == "practice")
            {
                yield return Practice();
                yield break;
            }

            if (mode == "hunt-only")
            {
                yield return HuntOnly();
                yield break;
            }

            yield return Walk(Host ? new Vector3(8.2f, 0, 2.7f) : new Vector3(9.6f, 0, 2.8f));
            if (Host)
                scenario.Begin(HuntTestMode.Complete);
            yield return Wait(() => scanner.Shift.IsRunning, "complete scenario started");
            yield return Barrier("started");
            uint oldLease = scanner.Seat.Lease;
            scanner.UseRpc(oldLease);
            scanner.UseRpc(oldLease);
            yield return Wait(() => scanner.Seat.Occupied, "competing instrument requests");
            yield return new WaitForSecondsRealtime(.6f);
            Require(scanner.Seat.Lease == oldLease + 1, "Lease advanced twice for stale repeated requests");
            yield return Barrier("lease-race");
            scanner.GetComponent<AnalyzerPanel>().CloseLocal();
            if (scanner.OwnsControls)
                scanner.ReleaseRpc(scanner.Seat.Lease);
            yield return Barrier("lease-released");
            yield return Wait(() => !scanner.Seat.Occupied, "operator release");
            checks.Add("Two simultaneous/repeated analyzer claims granted exactly one lease; release worked.");
            if (Host && !Solo)
                yield return OpenComputer();
            if (!Host || Solo)
                yield return OpenScanner();
            for (int i = 0; i < (mode == "inspection" ? 1 : 4); i++)
            {
                yield return Wait(() => scanner.Reception.WaitingNpc != null, "arrival " + i, 100);
                var guest = scanner.Reception.WaitingNpc;
                uint visit = guest.State.Visit;
                var bag = guest.GetComponent<GuestBaggage>();
                if (i == 2)
                    carrierGuest = guest;
                Require(bag.State.Visit == visit && bag.State.Phase == BaggagePhase.WithGuest, "Arriving guest missing associated baggage");
                if (Host && !Solo)
                {
                    scanner.AskRpc(new NetworkObjectReference(guest.NetworkObject), visit, BaggageQuestion.Contents, computer.Lease, true);
                    // Remote instrument operation is not available from the computer's location or lease.
                    scanner.TestRpc(AnalyzerTest.Weight, scanner.State.Revision, visit, scanner.Seat.Lease);
                }

                if (!Host || Solo)
                {
                    if (Solo)
                        scanner.AskRpc(new NetworkObjectReference(guest.NetworkObject), visit, BaggageQuestion.Contents, scanner.Seat.Lease, false);
                    scanner.PlaceBagRpc(new NetworkObjectReference(guest.NetworkObject), visit);
                    scanner.PlaceBagRpc(new NetworkObjectReference(guest.NetworkObject), visit);
                    yield return Wait(() => bag.State.Phase == BaggagePhase.OnAnalyzer, "physical bag transfer " + i);
                    yield return Measure(AnalyzerTest.Weight);
                    float mass = scanner.State.Mass;
                    yield return Measure(AnalyzerTest.Weight);
                    Require(Mathf.Approximately(mass, scanner.State.Mass), "Repeated mass changed");
                    Require(!scanner.Knows(AnalyzerTest.Metal) && scanner.State.Metal == 0, "Unmeasured metal response was replicated");
                    yield return Measure(AnalyzerTest.Metal);
                    yield return Measure(AnalyzerTest.Balance);
                    yield return Measure(AnalyzerTest.TiltLeft);
                    float left = scanner.State.Balance;
                    yield return Measure(AnalyzerTest.TiltLeft);
                    Require(Mathf.Approximately(left, scanner.State.Balance), "Repeated tilt changed");
                    yield return Measure(AnalyzerTest.Level);
                    yield return Measure(AnalyzerTest.TiltRight);
                    yield return Measure(AnalyzerTest.Pulse);
                    float after = scanner.State.AfterPulse;
                    yield return Measure(AnalyzerTest.Pulse);
                    Require(Mathf.Approximately(after, scanner.State.AfterPulse), "Repeated settled pulse changed");
                    foreach (BaggageQuestion q in Enum.GetValues(typeof(BaggageQuestion)))
                        scanner.AskRpc(new NetworkObjectReference(guest.NetworkObject), visit, q, scanner.Seat.Lease, false);
                    yield return Wait(() => scanner.Statements.Count == 5 && scanner.CanAdmit(guest), "all statements and admission gate");
                    if (!Application.isBatchMode && i == 2)
                    {
                        ScreenCapture.CaptureScreenshot(Path.Combine(dir, role + "-analyzer.png"));
                        yield return new WaitForSecondsRealtime(.3f);
                    }

                    if (i == 2)
                        Require(scanner.State.Mass > 10 && scanner.State.Mass < 15 && scanner.State.Metal > 10 && scanner.State.Balance < 0, "Plausible gimbal case lost mass/metal/counter-motion evidence");
                    Mark("scanned-" + i);
                }

                yield return Wait(() => Has("scanned-" + i), "other employee's shared readings " + i);
                Require(scanner.Statements.Count == 5 && scanner.Knows(AnalyzerTest.Pulse), "Shared record not replicated");
                checks.Add("Guest " + i + ": physical transfer, all 7 tests, repeated weight/tilt/pulse, 5 contextual statements and shared admission gate.");
                if (i == 0 && !Solo)
                {
                    if (Host)
                        Mark("late-inspection");
                    yield return Wait(() => Has("late-inspection-checked"), "late inspection observer", 60);
                }

                if (Host)
                {
                    if (Solo)
                    {
                        scanner.GetComponent<AnalyzerPanel>().CloseLocal();
                        yield return OpenComputer();
                    }

                    bool reject = mode == "refuse" && i >= 2;
                    var obj = new NetworkObjectReference(guest.NetworkObject);
                    if (reject)
                    {
                        scanner.Reception.RequestRefuseRpc(obj, visit, computer.Lease);
                        scanner.Reception.RequestRefuseRpc(obj, visit, computer.Lease);
                    }
                    else
                    {
                        uint cycle = scanner.Reception.Rooms[i].State.Cycle;
                        scanner.Reception.RequestCheckInRpc(i, cycle, obj, visit, computer.Lease);
                        scanner.Reception.RequestCheckInRpc(i, cycle, obj, visit, computer.Lease);
                        scanner.Reception.RequestRefuseRpc(obj, visit, computer.Lease);
                    }

                    yield return Wait(() => guest.Phase != GuestPhase.WaitingForCheckIn, "atomic decision " + i);
                    Mark("decided-" + i);
                    if (Solo)
                    {
                        computer.CloseLocal();
                        yield return Wait(() => !computer.LocalOpen, "return from computer");
                        yield return OpenScanner();
                    }
                }

                yield return Wait(() => Has("decided-" + i) && bag.State.Phase != BaggagePhase.OnAnalyzer && bag.State.Phase != BaggagePhase.ToAnalyzer, "decision and bag return " + i);
                if (!Host)
                {
                    // Competing decision from an employee without the computer lease cannot change the result.
                    scanner.Reception.RequestRefuseRpc(new NetworkObjectReference(guest.NetworkObject), visit, computer.Lease);
                }

                yield return Barrier("guest-done-" + i);
            }

            scanner.GetComponent<AnalyzerPanel>().CloseLocal();
            computer.CloseLocal();
            yield return Wait(() => !computer.LocalOpen, "close registration");
            yield return Walk(Host ? new Vector3(13.3f, 0, 7.3f) : new Vector3(5.5f, 0, 7));
            if (mode == "inspection")
            {
                yield return Barrier("inspection-complete");
                if (Host) scanner.Shift.EndForHuntDebug();
                yield return Wait(() => scanner.Shift.State.Phase == ShiftPhase.Finished, "focused inspection complete");
                checks.Add("Focused final-build regression: all measurements and pulse sound RPC, shared data, contextual answers, admission and late join.");
                yield return Barrier("done");
                yield break;
            }
            yield return Wait(() => scanner.Reception.Guests.Count(g => g.Phase == GuestPhase.InRoom) >= (mode == "refuse" ? 2 : 4), "admitted guests physically enter rooms", 90);
            if (mode == "refuse")
            {
                yield return Wait(() => carrierGuest.Phase == GuestPhase.Absent, "refused carrier physically leaves with bag");
                yield return new WaitForSecondsRealtime(25);
                Require(!hunt.State.Active && scanner.Shift.State.CheckedIn == 2, "Refusal created hidden hunt or duplicate admission");
                checks.Add("Only carrier refused after physical evidence; departure completed, no hunt spawned. Innocent false refusal also recorded; two legitimate stays continue.");
            }
            else
            {
                yield return Wait(() => hunt.State.Active && hunt.State.Phase != MimicPhase.Infiltrating, "physical bag-to-vent infiltration", 60);
                Require(carrierGuest.Room == scanner.Reception.Rooms[2] && carrierGuest.GetComponent<GuestBaggage>().State.Phase == BaggagePhase.Released, "Wrong carrier or empty bag missing");
                if (Host)
                {
                    Require(sawInfiltration && infiltrationDistance > 1.5f, "Physical infiltration not observed");
                    Mark("late-hunt");
                }

                checks.Add("Admitted carrier reached Room 103 with same bag; creature emerged and traversed room-to-vent route continuously.");
                if (!Solo)
                    yield return Wait(() => Has("late-hunt-checked"), "late hunt observer", 60);
                if (mode == "failure")
                {
                    yield return Wait(() => scanner.Shift.State.GuestDeaths > 0, "ignored investigation window leads to guest death", 210);
                    if (Host)
                        Require(hunt.HuntAge >= 100, "First unprovoked kill too early: " + hunt.HuntAge);
                    Require(carrierGuest.Alive && scanner.Shift.State.GuestDeaths == 1, "Carrier killed or excess deaths");
                    if (Host)
                        Require(hunt.ValidVictims >= 2, "No remaining victims after first death");
                    checks.Add("Ignored threat killed eligible guest after >=100s of hunting; protected carrier alive and further valid victims remain.");
                }
                else
                    yield return Ambush();
            }

            if (mode != "failure")
                yield return Checkout();
            yield return Barrier("outcome");
            if (Host)
                scanner.Shift.EndForHuntDebug();
            yield return Wait(() => scanner.Shift.State.Phase == ShiftPhase.Finished, "shift debrief");
            Require(!hunt.State.Active && scanner.Reception.Guests.All(g => g.Phase == GuestPhase.Absent) && FindObjectsByType<GuestBaggage>(FindObjectsSortMode.None).All(b => b.State.Phase == BaggagePhase.Hidden), "Closing left threat or baggage");
            Require(scanner.Debrief.Contains(mode == "refuse" ? "Carriers refused with baggage: 1" : "Carriers admitted: 1"), "Wrong safety debrief");
            if (mode == "refuse")
                Require(scanner.Debrief.Contains("Legitimate stays refused: 1"), "False refusal missing");
            checks.Add("End-of-shift business and safety counts, bag/creature/equipment/lease cleanup verified.");
            yield return Barrier("summary");
            if (Host)
                scenario.Begin(HuntTestMode.Complete);
            yield return Wait(() => scanner.Shift.IsRunning && scanner.Shift.State.Number == 2, "second shift");
            Require(!hunt.State.Active && !scanner.Seat.Occupied && scanner.Statements.Count == 0 && scanner.Debrief == "" && scanner.Shift.State.GuestDeaths == 0, "Second shift retained state");
            if (Host && !Solo)
                yield return OpenComputer();
            if (!Host || Solo)
                yield return OpenScanner();
            yield return Wait(() => scanner.Reception.WaitingNpc != null, "second-shift fresh arriving bag");
            var fresh = scanner.Reception.WaitingNpc;
            if (!Host || Solo)
            {
                scanner.AskRpc(new NetworkObjectReference(fresh.NetworkObject), fresh.State.Visit, BaggageQuestion.Contents, scanner.Seat.Lease, false);
                scanner.PlaceBagRpc(new NetworkObjectReference(fresh.NetworkObject), fresh.State.Visit);
                yield return Wait(() => scanner.Current == fresh.GetComponent<GuestBaggage>() && scanner.Current.State.Phase == BaggagePhase.OnAnalyzer, "second platform");
                yield return Measure(AnalyzerTest.Weight);
            }

            yield return Wait(() => scanner.CanAdmit(fresh), "second inspection complete");
            yield return Barrier("second-ready");
            if (Host)
            {
                if (Solo)
                {
                    scanner.GetComponent<AnalyzerPanel>().CloseLocal();
                    yield return OpenComputer();
                }

                scanner.Reception.RequestCheckInRpc(0, scanner.Reception.Rooms[0].State.Cycle, new NetworkObjectReference(fresh.NetworkObject), fresh.State.Visit, computer.Lease);
            }

            yield return Wait(() => fresh.Phase == GuestPhase.WalkingToRoom, "second admission");
            scanner.GetComponent<AnalyzerPanel>().CloseLocal();
            computer.CloseLocal();
            yield return Wait(() => !computer.LocalOpen, "second registration close");
            yield return Walk(Host ? new Vector3(13.3f, 0, 7.3f) : new Vector3(5.5f, 0, 7));
            yield return Wait(() => fresh.Phase == GuestPhase.InRoom, "second stay begins", 100);
            yield return Checkout();
            yield return Barrier("second-cycle");
            if (Host)
                scanner.Shift.EndForHuntDebug();
            yield return Wait(() => scanner.Shift.State.Phase == ShiftPhase.Finished, "second completed shift");
            Require(scanner.Shift.State.CheckedIn == 1 && scanner.Shift.State.CheckedOut == 1, "Second service cycle counted incorrectly");
            checks.Add("Second shift completed another arrival/inspection/admission/stay/checkout/departure/room-turnover cycle in the same connected session.");
            yield return Barrier("second-summary");
            if (Host)
                scenario.Begin(HuntTestMode.Complete);
            yield return Wait(() => scanner.Shift.IsRunning && scanner.Shift.State.Number == 3, "reset stress night");
            if (Host && !Solo)
                yield return OpenComputer();
            if (!Host || Solo)
                yield return OpenScanner();
            yield return Wait(() => scanner.Reception.WaitingNpc != null, "reset stress bag arrival");
            var pending = scanner.Reception.WaitingNpc;
            if (!Host || Solo)
            {
                scanner.PlaceBagRpc(new NetworkObjectReference(pending.NetworkObject), pending.State.Visit);
                yield return Wait(() => scanner.Current == pending.GetComponent<GuestBaggage>() && scanner.Current.State.Phase == BaggagePhase.OnAnalyzer, "pending platform");
                scanner.TestRpc(AnalyzerTest.Weight, scanner.State.Revision, scanner.State.Visit, scanner.Seat.Lease);
            }

            yield return Wait(() => scanner.State.Busy, "measurement in progress before reset");
            yield return Barrier("busy-reset");
            if (Host)
                scenario.ResetScenario();
            yield return Wait(() => scanner.Shift.State.Number == 4 && !scanner.State.Busy, "reset unresolved bag and active lease");
            Require(!scanner.Seat.Occupied && scanner.Statements.Count == 0 && !hunt.State.Active, "Reset left instrument locked");
            checks.Add("Resetting during an unfinished test clears measurement, lease and old visit data.");
            yield return Barrier("done");
        }

        IEnumerator Practice()
        {
            Require(Solo, "Practice actor uses one real host player");
            scenario.Begin(HuntTestMode.Inspection);
            yield return OpenScanner();
            var seen = new HashSet<string>();
            for (int i = 0; i < 14; i++)
            {
                yield return Wait(() => scanner.Reception.WaitingNpc != null, "practice case " + i, 100);
                var g = scanner.Reception.WaitingNpc;
                var bag = g.GetComponent<GuestBaggage>();
                var definition = bag.ServerCase;
                Require(seen.Add(definition.name), "Practice repeated a case before deck exhaustion");
                scanner.AskRpc(new NetworkObjectReference(g.NetworkObject), g.State.Visit, BaggageQuestion.Contents, scanner.Seat.Lease, false);
                scanner.PlaceBagRpc(new NetworkObjectReference(g.NetworkObject), g.State.Visit);
                yield return Wait(() => bag.State.Phase == BaggagePhase.OnAnalyzer, "practice transfer");
                foreach (var test in new[]{AnalyzerTest.Weight, AnalyzerTest.Metal, AnalyzerTest.TiltLeft, AnalyzerTest.Level, AnalyzerTest.Pulse})
                    yield return Measure(test);
                Require(Mathf.Abs(scanner.State.Mass - definition.Mass) < .001f && Mathf.Abs(scanner.State.Metal - definition.Metal) < .001f, "Instrument disagrees with case physics");
                foreach (BaggageQuestion q in Enum.GetValues(typeof(BaggageQuestion)))
                    scanner.AskRpc(new NetworkObjectReference(g.NetworkObject), g.State.Visit, q, scanner.Seat.Lease, false);
                yield return Wait(() => scanner.Statements.Count == 5 && scanner.CanAdmit(g), "practice contextual questions");
                checks.Add(definition.name + ": kg=" + scanner.State.Mass.ToString("0.0") + " metal=" + scanner.State.Metal.ToString("0.0") + " balance=" + scanner.State.Balance.ToString("0.00") + " 5 answers");
                scanner.GetComponent<AnalyzerPanel>().CloseLocal();
                yield return OpenComputer();
                if (i == 0 || i == 2 || i == 5 || i == 9)
                {
                    int index = Array.FindIndex(scanner.Reception.Rooms, r => r.AvailableForCheckIn);
                    Require(index >= 0, "Practice room turnover failed");
                    scanner.Reception.RequestCheckInRpc(index, scanner.Reception.Rooms[index].State.Cycle, new NetworkObjectReference(g.NetworkObject), g.State.Visit, computer.Lease);
                }
                else
                    scanner.Reception.RequestRefuseRpc(new NetworkObjectReference(g.NetworkObject), g.State.Visit, computer.Lease);
                yield return Wait(() => g.Phase != GuestPhase.WaitingForCheckIn, "practice decision");
                computer.CloseLocal();
                yield return Wait(() => !computer.LocalOpen, "practice close computer");
                yield return OpenScanner();
                Require(!hunt.State.Active, "Inspection-only practice released a creature");
            }

            Require(scanner.Shift.State.CheckedOut >= 2, "Practice automatic checkout/turnover did not repeat");
            Require(FindObjectsByType<GuestBaggage>(FindObjectsSortMode.None).Length == 5, "Practice duplicated bags");
            checks.Add("All 14 authored cases measured and questioned through real interaction/RPCs; multiple admissions, innocent refusals, carriers; isolated mode suppressed release and repeated automatic checkout/turnover.");
            scanner.Shift.EndForHuntDebug();
            yield return Wait(() => scanner.Shift.State.Phase == ShiftPhase.Finished, "practice end");
            Require(scanner.Reception.Guests.All(g => g.Phase == GuestPhase.Absent) && !scanner.Seat.Occupied, "Practice reset retained baggage owner");
        }

        IEnumerator HuntOnly()
        {
            yield return Walk(Host ? new Vector3(13.3f, 0, 7.3f) : new Vector3(5.5f, 0, 7));
            if (Host)
                scenario.Begin(HuntTestMode.HuntOnly);
            yield return Wait(() => hunt.State.Active && scanner.Reception.Guests.Count(g => g.Phase == GuestPhase.InRoom) >= 3, "three automatic walking guests", 150);
            if (Host)
                Require(hunt.ValidVictims == 2 && hunt.DebugCarrier == scanner.Reception.Rooms[0].Occupant, "Hunt-only carrier/victim setup");
            yield return new WaitForSecondsRealtime(20);
            Require(scanner.Shift.State.GuestDeaths == 0, "Hunt-only killed guest during initial investigation window");
            if (Host)
            {
                hunt.HarmlessComplaintOnServer();
                Require(hunt.State.Health == 100, "Harmless complaint affected threat");
            }

            if (Host && !Application.isBatchMode)
            {
                var menu = FindFirstObjectByType<MimicDebugMenu>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(MimicDebugMenu).GetField("open", flags).SetValue(menu, true);
                yield return new WaitForSecondsRealtime(.3f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "f8-scenarios.png"));
                yield return new WaitForSecondsRealtime(.3f);
                typeof(MimicDebugMenu).GetMethod("Close", flags).Invoke(menu, null);
            }

            checks.Add("One-click hunt-only scenario produced three real walking guests, two eligible victims and protected carrier; no kill in initial 20s. Harmless fan complaint did not mutate threat.");
            yield return Barrier("hunt-only-ready");
            if (Host)
                scenario.ResetScenario();
            yield return Wait(() => scanner.Shift.State.Number == 2 && !hunt.State.Active, "full hunt-only scenario reset");
            Require(!hunt.Decoy.State.Held && !hunt.Decoy.State.Placed && !hunt.Firearm.State.Held && FindObjectsByType<HuntEquipment>(FindObjectsSortMode.None).Length == 2, "Reset duplicated equipment");
            yield return Wait(() => hunt.State.Active && scanner.Reception.Guests.Count(g => g.Phase == GuestPhase.InRoom) >= 3, "repeat automatic hunt setup", 150);
            if (Host)
                Require(hunt.ValidVictims == 2, "Reset lost victim population");
            checks.Add("Reset Scenario cleared active hunt and equipment, then independently rebuilt another 3-guest hunt.");
            yield return Barrier("repeat-hunt-ready");
            yield return EvacuationAndRetreat();
            if (Host)
            {
                hunt.ResetOnServer();
                hunt.HarmlessComplaintOnServer();
                var room = scanner.Reception.Rooms[3];
                var vent = hunt.Nodes[3];
                yield return Walk(new Vector3(22.2f, 0, 8.4f));
                yield return Walk(room.DoorOutside.position);
                yield return Wait(() => room.Door.CanInteract, "fan room door");
                if (!room.Door.IsOpen)
                    Use(room.Door, room.Door.InteractionPoint);
                yield return Wait(() => room.Door.IsOpen && !room.Door.IsMoving, "fan room open");
                yield return Walk(room.DoorInside.position);
                if (room.Door.IsOpen)
                    Use(room.Door, room.Door.InteractionPoint);
                yield return Wait(() => !room.Door.IsMoving && !room.Door.IsOpen, "fan door closed");
                yield return Walk(new Vector3(24.4f, 0, 20));
                Use(vent, vent.inspection.bounds.center);
                Require(user.Feedback.Contains("ordinary cause") && !hunt.State.Active, "Fan inspection became a threat");
                Mark("fan-inspected");
            }

            yield return Wait(() => Has("fan-inspected"), "ordinary fan inspection");
            Require(!hunt.State.Active, "False complaint spawned a Mimic");
            checks.Add("With threat reset, actual E interaction at Room 104 identified ordinary loose fan; no Mimic activated.");
            yield return Barrier("done");
        }

        IEnumerator EvacuationAndRetreat()
        {
            var victim = scanner.Reception.Rooms[0].Occupant;
            var carrier = scanner.Reception.Rooms[1].Occupant;
            var remaining = scanner.Reception.Rooms[2].Occupant;
            // Isolated regression uses the same explicit host-only manual activation as Advanced diagnostics.
            // The current three-guest fixture has physically placed bags, unlike the retired M14 quick fixture.
            if (Host)
                hunt.ResetOnServer();
            yield return Wait(() => !hunt.State.Active, "prepare evacuation regression");
            yield return Barrier("evacuation-prepared");
            if (Host)
            {
                yield return Enter(0);
                yield return Walk(new Vector3(19.7f, 0, 13.65f));
                Require(hunt.ActivateOnServer(0, carrier), "Activate evacuation regression");
                yield return Wait(() => hunt.State.Phase == MimicPhase.ApproachingTarget || hunt.State.Phase == MimicPhase.ApproachingBait, "evacuation warning");
                Use(victim.GetComponent<GuestEvacuation>(), victim.Body.bounds.center);
                for (int i = 0; i < 5; i++)
                    hunt.EvacuateRpc(new NetworkObjectReference(victim.NetworkObject), victim.State.Visit, hunt.State.Revision);
                yield return Walk(new Vector3(20, 0, 14.3f));
            }

            yield return Wait(() => victim.Phase == GuestPhase.Evacuated, "physical evacuation to lobby", 100);
            Require(victim.Alive && carrier.Alive && scanner.Shift.State.Evacuations == 1 && scanner.Shift.State.GuestDeaths == 0, "Evacuation outcomes");
            checks.Add("Threatened guest physically exited through the door to safety; repeated requests counted once, room vacated, carrier protected.");
            yield return Barrier("evacuated");
            if (Host)
                hunt.ResetOnServer();
            yield return Wait(() => !hunt.State.Active, "reset before player-heat activation");
            yield return Barrier("player-heat-prepared");
            if (Host)
            {
                yield return Leave(0);
                yield return Take(hunt.Firearm);
                yield return Enter(0);
                yield return Walk(new Vector3(20, 0, 14.2f));
                Require(hunt.ActivateOnServer(0, carrier), "Activate player-heat regression");
            }

            yield return Wait(() => hunt.State.Phase == MimicPhase.Emerging, "player heat lures creature", 80);
            if (Host)
            {
                yield return Wait(() => hunt.State.Phase == MimicPhase.Exposed, "failed ambush exposed");
                yield return Shoot();
                yield return Wait(() => own.GetComponent<TowelCarrier>().Incapacitated, "attack staggers player", 30);
                Require(!hunt.Firearm.State.Held, "Player attack did not drop firearm");
            }

            yield return Wait(() => hunt.State.Phase == MimicPhase.Retreating, "failed ambush retreat", 35);
            yield return Wait(() => hunt.State.Phase == MimicPhase.Dormant, "continuous retreat into vent", 35);
            Require(hunt.State.Health == 66, "Retreat reset damage");
            checks.Add("Player heat caused emergence; one authoritative hit, physical attack/stagger and weapon drop, continuous retreat with 66 HP retained.");
            yield return Barrier("retreat-observed");
            if (Host)
                yield return Leave(0);
            yield return Wait(() => hunt.State.Phase == MimicPhase.ApproachingTarget, "hunt resumes against remaining eligible guest", 150);
            if (Host)
                Require(hunt.DebugTarget == remaining.GuestName && hunt.ValidVictims == 1, "Retargeted evacuated guest or protected carrier");
            Require(hunt.State.Health == 66 && carrier.Alive && victim.Alive, "Resumed hunt lost preserved state");
            checks.Add("After retreat, the same wounded creature resumed its connected route toward the remaining eligible guest, excluding carrier and evacuated guest.");
            yield return Barrier("retarget-observed");
        }

        IEnumerator Edges()
        {
            if (Host)
                scenario.Begin(HuntTestMode.Complete);
            yield return Wait(() => scanner.Shift.IsRunning, "edge scenario");
            if (Host)
                yield return Walk(new Vector3(13.3f, 0, 7.3f));
            else
                yield return OpenScanner();
            yield return Wait(() => scanner.Reception.WaitingNpc != null, "edge arriving guest");
            var guest = scanner.Reception.WaitingNpc;
            if (!Host)
            {
                scanner.PlaceBagRpc(new NetworkObjectReference(guest.NetworkObject), guest.State.Visit);
                scanner.AskRpc(new NetworkObjectReference(guest.NetworkObject), guest.State.Visit, BaggageQuestion.Contents, scanner.Seat.Lease, false);
                yield return Wait(() => scanner.Current != null && scanner.Current.State.Phase == BaggagePhase.OnAnalyzer, "edge bag on platform");
                scanner.TestRpc(AnalyzerTest.Weight, scanner.State.Revision, scanner.State.Visit, scanner.Seat.Lease);
            }

            yield return Wait(() => scanner.State.Busy, "disconnect during measurement");
            if (!Host)
                yield return Reconnect("instrument");
            else
            {
                yield return Wait(() => Has("instrument-left") && !scanner.Seat.Occupied, "disconnected operator lease released");
                yield return Wait(() => scanner.Knows(AnalyzerTest.Weight) && !scanner.State.Busy, "host completes unattended physical measurement");
                yield return Wait(() => Has("instrument-rejoined"), "operator rejoined");
            }

            yield return Barrier("instrument-restored");
            Require(scanner.Knows(AnalyzerTest.Weight) && scanner.Statements.Count == 1 && scanner.Current.State.Phase == BaggagePhase.OnAnalyzer, "Shared inspection lost after reconnect");
            checks.Add("Analyzer operator disconnected mid-measurement: host completed test, released lease and restored same bag/readings/statements on rejoin.");
            if (!Host)
                yield return OpenComputer();
            else
                yield return OpenScanner();
            yield return Wait(() => computer.Occupied, "client computer ownership");
            yield return Barrier("computer-owner-established");
            if (!Host)
                yield return Reconnect("computer");
            else
            {
                yield return Wait(() => Has("computer-left") && !computer.Occupied, "computer disconnect releases lease");
                yield return Wait(() => Has("computer-rejoined"), "computer owner rejoined");
            }

            yield return Barrier("computer-restored");
            checks.Add("Computer owner disconnected while another employee used analyzer; computer released, shared record and other lease survived.");
            if (Host)
            {
                scanner.GetComponent<AnalyzerPanel>().CloseLocal();
                yield return OpenComputer();
                scanner.Reception.RequestCheckInRpc(0, scanner.Reception.Rooms[0].State.Cycle, new NetworkObjectReference(guest.NetworkObject), guest.State.Visit, computer.Lease);
                computer.CloseLocal();
                yield return Wait(() => !computer.LocalOpen, "close after recovered admission");
                yield return Walk(new Vector3(13.3f, 0, 7.3f));
            }

            yield return Wait(() => guest.Phase == GuestPhase.InRoom, "registration remains usable after both disconnects", 100);
            yield return Barrier("admitted-after-disconnects");
            if (Host)
                scanner.Shift.EndForHuntDebug();
            yield return Wait(() => scanner.Shift.State.Phase == ShiftPhase.Finished, "edge night complete");
            yield return Barrier("edge-summary");
            if (Host)
                scenario.Begin(HuntTestMode.Complete);
            yield return Wait(() => scanner.Shift.State.Number == 2 && scanner.Shift.IsRunning, "refuse-all business test");
            if (Host)
                yield return OpenComputer();
            else
                yield return OpenScanner();
            yield return Wait(() => scanner.Reception.WaitingNpc != null, "refuse-all arrival");
            var innocent = scanner.Reception.WaitingNpc;
            if (!Host)
            {
                scanner.PlaceBagRpc(new NetworkObjectReference(innocent.NetworkObject), innocent.State.Visit);
                yield return Wait(() => scanner.Current == innocent.GetComponent<GuestBaggage>() && scanner.Current.State.Phase == BaggagePhase.OnAnalyzer, "refusal busy bag");
                scanner.TestRpc(AnalyzerTest.Weight, scanner.State.Revision, innocent.State.Visit, scanner.Seat.Lease);
            }

            yield return Wait(() => scanner.State.Busy, "refusal during unfinished test");
            yield return Barrier("refusal-busy");
            if (Host)
                scanner.Reception.RequestRefuseRpc(new NetworkObjectReference(innocent.NetworkObject), innocent.State.Visit, computer.Lease);
            yield return Wait(() => innocent.Phase == GuestPhase.Leaving && !scanner.State.Busy, "ordinary refusal cancels unfinished test");
            yield return Barrier("ordinary-refused");
            if (Host)
                scanner.Shift.EndForHuntDebug();
            yield return Wait(() => scanner.Shift.State.Phase == ShiftPhase.Finished, "business summary");
            Require(scanner.Debrief.Contains("SERVICE FAILED") && scanner.Debrief.Contains("Legitimate stays refused: 1") && !hunt.State.Active, "Refusing all guests rewarded");
            checks.Add("Refusing all legitimate arrivals produces explicit SERVICE FAILED, one lost stay, and no invented threat.");
            yield return Barrier("business-checked");
            if (Host)
            {
                var randomState = UnityEngine.Random.state;
                var positions = new HashSet<int>();
                scenario.OrdinaryOnServer();
                for (int trial = 0; trial < 32; trial++)
                {
                    UnityEngine.Random.InitState(1500 + trial);
                    scanner.ResetOnServer();
                    int carriers = 0;
                    for (int arrival = 0; arrival < 18; arrival++)
                        if (scanner.AssignCaseOnServer().containsMimic)
                        {
                            carriers++;
                            positions.Add(arrival);
                            Require(arrival < 4, "Carrier scheduled outside initial population");
                        }

                    Require(carriers == 1, "Normal shift added replacement/multiple carriers");
                }

                UnityEngine.Random.state = randomState;
                Require(positions.Count > 1, "Normal carrier always in predictable queue position");
                scanner.ResetOnServer();
                Mark("normal-decks-checked");
            }

            yield return Wait(() => Has("normal-decks-checked"), "normal deck validation");
            checks.Add("32 seeded normal-shift decks: exactly one carrier across 18 assignments, varied initial position, no replacement after the original case.");
            yield return Barrier("done");
        }

        IEnumerator Reconnect(string tag)
        {
            session.Disconnect();
            yield return Wait(() => !session.Manager.IsListening && !session.Manager.ShutdownInProgress, tag + " shutdown");
            Mark(tag + "-left");
            yield return new WaitForSecondsRealtime(2);
            yield return Connect();
            yield return Walk(new Vector3(5.5f, 0, 7));
            Mark(tag + "-rejoined");
        }

        IEnumerator OpenScanner()
        {
            if (own.transform.position.x > 10)
                yield return Walk(new Vector3(12.8f, 0, 8.4f));
            if (own.transform.position.z > 4.5f)
            {
                yield return Walk(new Vector3(5, 0, 8));
                yield return Walk(new Vector3(5, 0, 3));
            }

            yield return Walk(new Vector3(9.5f, 0, 2.8f));
            Use(scanner, scanner.Controls.bounds.center);
            yield return Wait(() => scanner.OwnsControls && scanner.GetComponent<AnalyzerPanel>().LocalOpen, "analyzer operator");
        }

        IEnumerator OpenComputer()
        {
            if (own.transform.position.x < 10 && own.transform.position.z < 5.5f)
            {
                yield return Walk(new Vector3(5, 0, 3));
                yield return Walk(new Vector3(5, 0, 8));
            }

            yield return Walk(new Vector3(12.8f, 0, 8.4f));
            yield return Walk(new Vector3(11.9f, 0, 4.9f));
            Use(computer.ScreenCollider.GetComponent<ComputerInteraction>(), computer.ScreenCollider.bounds.center);
            yield return Wait(() => computer.Focused, "computer owner focused");
        }

        IEnumerator Measure(AnalyzerTest test)
        {
            uint rev = scanner.State.Revision;
            scanner.TestRpc(test, rev, scanner.State.Visit, scanner.Seat.Lease);
            scanner.TestRpc(test, rev, scanner.State.Visit, scanner.Seat.Lease);
            yield return Wait(() => scanner.State.Revision > rev && !scanner.State.Busy, "measurement " + test, 12);
            Require(scanner.Knows(test), "Test did not commit " + test);
        }

        IEnumerator Ambush()
        {
            // Stage the firearm before enabling heat. The bait is allowed to interrupt exploration immediately.
            if (!Host)
            {
                yield return Take(hunt.Firearm);
                yield return Walk(new Vector3(22.2f, 0, 10.5f));
                Mark("gun-ready");
            }

            if (Host && !Solo)
                yield return Wait(() => Has("gun-ready"), "partner prepared firearm");
            if (Solo)
            {
                yield return Take(hunt.Firearm);
                yield return Enter(0);
                yield return Walk(new Vector3(20, 0, 14.2f));
                hunt.Firearm.PlaceRpc(new Vector3(20, 0, 14), hunt.Firearm.State.Revision);
                yield return Wait(() => !hunt.Firearm.State.Held, "stage solo pistol");
                yield return Leave(0);
            }

            if (Host)
            {
                yield return Take(hunt.Decoy);
                yield return Enter(0);
                yield return Walk(new Vector3(20, 0, Solo ? 13.3f : 14.2f));
                hunt.Decoy.PlaceRpc(Solo ? new Vector3(20.3f, 0, 13.5f) : new Vector3(19.6f, 0, 14.25f), hunt.Decoy.State.Revision);
                yield return Wait(() => hunt.Decoy.IsHeating, "place real heat decoy");
                if (Solo)
                {
                    Use(hunt.Firearm, hunt.Firearm.PickupCollider.bounds.center);
                    yield return Wait(() => hunt.Firearm.LocallyHeld, "retrieve staged pistol");
                }
                else
                    yield return Leave(0);
                Mark("bait-ready");
            }

            if (!Host)
            {
                yield return Wait(() => Has("bait-ready"), "partner stages decoy");
                yield return Enter(0);
                yield return Walk(new Vector3(20, 0, 14.2f));
            }

            if (Host && !Solo)
            {
                yield return OpenComputer();
                var cctv = FindFirstObjectByType<VentSurveillance>();
                cctv.Select(0);
                yield return new WaitForSecondsRealtime(.2f);
                CaptureFeed(cctv, "vent-camera-1");
                cctv.Select(1);
                yield return new WaitForSecondsRealtime(.2f);
                CaptureFeed(cctv, "vent-camera-2");
                computer.CloseLocal();
            }

            if (!Host || Solo)
            {
                yield return Wait(() => hunt.State.Phase == MimicPhase.Exposed && hunt.State.Node == 0, "lured creature visible", 100);
                if (!Application.isBatchMode)
                {
                    own.GetComponentInChildren<Camera>().transform.LookAt(hunt.Body.bounds.center);
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, role + "-emergence.png"));
                }

                for (int i = 0; i < 3; i++)
                {
                    yield return Shoot();
                    yield return new WaitForSecondsRealtime(.65f);
                }
            }

            yield return Wait(() => hunt.State.Phase == MimicPhase.Dead, "three valid shots kill creature", 130);
            Require(scanner.Shift.State.MimicsEliminated == 1 && scanner.Shift.State.GuestDeaths == 0 && carrierGuest.Alive, "Ambush did not preserve guests");
            checks.Add("Full arrival → questioning → measurement → admission → physical infiltration → local thermal bait → 3 server-authoritative hits; no guest deaths.");
            if (!Host || Solo)
            {
                yield return Leave(0);
                yield return Walk(new Vector3(5.5f, 0, 7));
            }

            yield return Barrier("ambush-cleared");
        }

        IEnumerator Checkout()
        {
            yield return new WaitForSecondsRealtime(.3f); // Let scenario stay-duration setup observe the new InRoom state first.
            var guest = scanner.Reception.Rooms[0].Occupant;
            if (Host)
            {
                guest.ExtendDebugStay(hunt.NetworkManager.ServerTime.Time + .5);
                yield return OpenComputer();
                float checkoutEnd = Time.realtimeSinceStartup + 100;
                while (scanner.Reception.CheckoutNpc != guest)
                {
                    Require(Time.realtimeSinceStartup < checkoutEnd, "Checkout queue timeout");
                    if (scanner.Reception.WaitingNpc != null)
                        scanner.Reception.RefuseWaitingGuest();
                    yield return null;
                }

                scanner.Reception.RequestCheckoutRpc(0, guest.Room.State.Cycle, new NetworkObjectReference(guest.NetworkObject), guest.State.Visit, computer.Lease);
            }

            yield return Wait(() => guest.Phase == GuestPhase.Absent && scanner.Shift.State.CheckedOut == 1, "physical checkout departure with baggage", 100);
            Require(scanner.Reception.Rooms[0].State.Status == RoomStatus.Dirty && !scanner.Reception.Rooms[0].State.BedMade, "Checkout did not reset cleaning");
            checks.Add("Post-inspection normal checkout and physical departure reset room bed/dirty state.");
            if (Host)
            {
                computer.CloseLocal();
                yield return Wait(() => !computer.LocalOpen, "leave computer");
            }
        }

        IEnumerator EquipmentRace()
        {
            yield return Walk(new Vector3(12.8f, 0, 8.4f));
            yield return Walk(new Vector3(12.8f, 0, 4.6f));
            yield return Walk(Host ? new Vector3(17.1f, 0, 4.9f) : new Vector3(17.3f, 0, 5.7f));
            uint revision = hunt.Decoy.State.Revision;
            yield return Barrier("equipment-race");
            hunt.Decoy.TakeRpc(revision);
            hunt.Decoy.TakeRpc(revision);
            yield return Wait(() => hunt.Decoy.State.Held, "simultaneous decoy claim");
            yield return new WaitForSecondsRealtime(.6f);
            Require(FindObjectsByType<TowelCarrier>(FindObjectsSortMode.None).Count(c => c.IsSpawned && c.State.Item == ServiceItem.ThermalDecoy) == 1, "Simultaneous pickup duplicated carry slot");
            if (!hunt.Decoy.LocallyHeld)
                hunt.Decoy.PlaceRpc(new Vector3(17.2f, 0, 4.9f), hunt.Decoy.State.Revision);
            yield return Barrier("unauthorized-place");
            yield return new WaitForSecondsRealtime(.5f);
            Require(hunt.Decoy.State.Held, "Nonowner placed equipment");
            if (hunt.Decoy.LocallyHeld)
                hunt.Decoy.PlaceRpc(new Vector3(17.2f, 0, 4.9f), hunt.Decoy.State.Revision);
            yield return Wait(() => hunt.Decoy.IsHeating, "winner releases decoy");
            yield return Barrier("race-released");
            if (Host)
                hunt.Decoy.ResetOnServer();
            yield return Wait(() => !hunt.Decoy.State.Held && !hunt.Decoy.State.Placed, "race station reset");
            checks.Add("Simultaneous pickup and repeated stale requests produced one owner; nonowner placement rejected, carry slots released.");
            yield return Walk(new Vector3(12.8f, 0, 4.6f));
            yield return Walk(new Vector3(12.8f, 0, 8.4f));
            yield return Walk(Host ? new Vector3(13.3f, 0, 7.3f) : new Vector3(5.5f, 0, 7));
            yield return Barrier("race-done");
        }

        void CaptureFeed(VentSurveillance cctv, string name)
        {
            if (Application.isBatchMode)
                return;
            var old = RenderTexture.active;
            RenderTexture.active = cctv.CurrentFeed;
            var tex = new Texture2D(cctv.CurrentFeed.width, cctv.CurrentFeed.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            RenderTexture.active = old;
        }

        IEnumerator Shoot()
        {
            var cam = own.GetComponentInChildren<Camera>();
            Vector3 d = hunt.Body.bounds.center - cam.transform.position;
            Vector3 flat = new Vector3(d.x, 0, d.z);
            own.transform.rotation = Quaternion.LookRotation(flat);
            cam.transform.rotation = Quaternion.LookRotation(d);
            yield return new WaitForSecondsRealtime(.2f);
            cam.transform.LookAt(hunt.Body.bounds.center);
            hunt.Firearm.FireLocal();
            uint sequence = (uint)typeof(HuntEquipment).GetField("localSequence", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(hunt.Firearm);
            for (int i = 0; i < 4; i++)
                hunt.Firearm.FireRpc(cam.transform.forward, hunt.Firearm.State.Revision, sequence);
        }

        IEnumerator Take(HuntEquipment equipment)
        {
            yield return Walk(new Vector3(12.8f, 0, 8.4f));
            yield return Walk(new Vector3(12.8f, 0, 4.6f));
            yield return Walk(new Vector3(17.1f, 0, 4.9f));
            Use(equipment, equipment.PickupCollider.bounds.center);
            yield return Wait(() => equipment.LocallyHeld, "take " + equipment.Item);
            yield return Walk(new Vector3(12.8f, 0, 4.6f));
            yield return Walk(new Vector3(12.8f, 0, 8.4f));
        }

        IEnumerator Enter(int i)
        {
            var room = hunt.Nodes[i].room;
            yield return Walk(new Vector3(22.2f, 0, 8.4f));
            yield return Walk(room.DoorOutside.position);
            yield return Wait(() => room.Door.CanInteract, "door available");
            yield return new WaitForSecondsRealtime(.4f);
            if (!room.Door.IsOpen)
                Use(room.Door, room.Door.InteractionPoint);
            yield return Wait(() => room.Door.IsOpen && !room.Door.IsMoving, "open room");
            yield return Walk(new Vector3(20, 0, 12.65f));
            yield return Walk(new Vector3(19, 0, 12.65f));
            if (room.Door.IsOpen)
                Use(room.Door, room.Door.InteractionPoint);
            yield return Wait(() => !room.Door.IsMoving && !room.Door.IsOpen, "close leaf clear of work");
            yield return Walk(new Vector3(20, 0, 12.65f));
        }

        IEnumerator Leave(int i)
        {
            var room = hunt.Nodes[i].room;
            yield return Walk(new Vector3(20, 0, 12.65f));
            yield return Walk(new Vector3(19, 0, 12.65f));
            yield return Wait(() => room.Door.CanInteract, "leave available");
            if (!room.Door.IsOpen)
                Use(room.Door, room.Door.InteractionPoint);
            yield return Wait(() => room.Door.IsOpen && !room.Door.IsMoving, "leave opens");
            yield return Walk(new Vector3(20, 0, 12.65f));
            yield return Walk(room.DoorOutside.position);
            yield return Walk(new Vector3(22.2f, 0, 8.4f));
            yield return Walk(new Vector3(12.8f, 0, 8.4f));
        }

        void Use(IInteractable target, Vector3 aim)
        {
            own.GetComponentInChildren<Camera>().transform.LookAt(aim);
            Physics.SyncTransforms();
            Require(user.FindTarget() == target, "Interaction ray misses " + target + " at " + own.transform.position + " found " + user.FindTarget());
            Require(user.TryInteract(), "Interaction unavailable");
        }

        IEnumerator Walk(Vector3 target)
        {
            File.WriteAllText(Path.Combine(dir, role + ".progress"), "walk " + target);
            var path = new NavMeshPath();
            Require(NavMesh.CalculatePath(own.transform.position, target, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete, "No player path " + target);
            var cc = own.GetComponent<CharacterController>();
            foreach (var corner in path.corners.Skip(1).Concat(new[]{target}))
            {
                float end = Time.realtimeSinceStartup + 30;
                var detours = new Queue<Vector3>();
                float nextAvoid = 0;
                while (true)
                {
                    Vector3 goal = detours.Count > 0 ? detours.Peek() : corner;
                    var d = goal - own.transform.position;
                    d.y = 0;
                    if (d.magnitude < .1f)
                    {
                        if (detours.Count > 0)
                        {
                            detours.Dequeue();
                            continue;
                        }

                        break;
                    }

                    Require(Time.realtimeSinceStartup < end, "Player blocked " + corner + " at " + own.transform.position);
                    // A test actor steers around people using the same colliding CharacterController; no teleports or disabled collisions.
                    if (detours.Count == 0 && Time.realtimeSinceStartup > nextAvoid)
                    {
                        nextAvoid = Time.realtimeSinceStartup + .5f;
                        Vector3 direction = d.normalized;
                        bool person = Physics.CapsuleCastAll(own.transform.position + Vector3.up * .35f, own.transform.position + Vector3.up * 1.45f, .28f, direction, Mathf.Min(.85f, d.magnitude), ~0, QueryTriggerInteraction.Ignore).Any(h => !h.collider.transform.IsChildOf(own.transform) && h.collider.GetComponentInParent<GuestLifecycle>() != null);
                        if (person)
                        {
                            foreach (float side in new[]{1f, -1f})
                            {
                                Vector3 across = own.transform.position + Vector3.Cross(Vector3.up, direction) * side * .95f;
                                Vector3 past = across + direction * 1.6f;
                                if (!ClearStep(across) || !ClearStep(past) || NavMesh.Raycast(own.transform.position, across, out _, NavMesh.AllAreas) || NavMesh.Raycast(across, past, out _, NavMesh.AllAreas))
                                    continue;
                                detours.Enqueue(across);
                                detours.Enqueue(past);
                                File.AppendAllText(Path.Combine(dir, role + ".walking"), "Walk around guest at " + own.transform.position + " via " + across + " / " + past + "\n");
                                break;
                            }

                            if (detours.Count > 0)
                                continue;
                        }
                    }

                    cc.Move(Vector3.ClampMagnitude(d, 4 * Time.deltaTime) + Vector3.down * 2 * Time.deltaTime);
                    yield return null;
                }
            }

            yield return new WaitForSecondsRealtime(.35f);
        }

        bool ClearStep(Vector3 p)
        {
            if (!NavMesh.SamplePosition(p, out var hit, .15f, NavMesh.AllAreas) || Vector3.Distance(hit.position, p) > .15f)
                return false;
            return !Physics.OverlapCapsule(p + Vector3.up * .35f, p + Vector3.up * 1.45f, .29f, ~0, QueryTriggerInteraction.Ignore).Any(c => !c.transform.IsChildOf(own.transform));
        }

        void Finish(bool pass, string reason)
        {
            File.WriteAllText(Path.Combine(dir, role + ".txt"), (pass ? "PASS " : "FAIL ") + role + " / " + mode + "\n" + string.Join("\n", checks) + "\n" + reason + "\nInfiltration travel: " + infiltrationDistance + "m; exploration seen: " + sawExploration + "\nDuct travel: " + ductDistance + "m\n" + string.Join("\n", errors));
            Application.Quit(pass ? 0 : 1);
        }

        void OnDestroy() => Application.logMessageReceived -= Log;
    }
}
#endif
