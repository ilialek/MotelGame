using System;
using System.Linq;
using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using MotelGame.Service;
using MotelGame.Horror;
using MotelGame.Interactions;

namespace MotelGame.Inspection
{
    public struct InspectionRecord : INetworkSerializable, IEquatable<InspectionRecord>
    {
        public NetworkObjectReference Guest;
        public uint Visit, Revision, Known, Asked;
        public bool HasGuest, Busy;
        public AnalyzerTest Test;
        public int Tilt, LastTilt;
        public bool Pulsed;
        public double StartedAt;
        public float Mass, Metal, Balance, BeforePulse, AfterPulse, Delay, Decay, Amplitude;
        public void NetworkSerialize<T>(BufferSerializer<T> s)
            where T : IReaderWriter
        {
            s.SerializeValue(ref Guest);
            s.SerializeValue(ref Visit);
            s.SerializeValue(ref Revision);
            s.SerializeValue(ref Known);
            s.SerializeValue(ref Asked);
            s.SerializeValue(ref HasGuest);
            s.SerializeValue(ref Busy);
            s.SerializeValue(ref Test);
            s.SerializeValue(ref Tilt);
            s.SerializeValue(ref LastTilt);
            s.SerializeValue(ref Pulsed);
            s.SerializeValue(ref StartedAt);
            s.SerializeValue(ref Mass);
            s.SerializeValue(ref Metal);
            s.SerializeValue(ref Balance);
            s.SerializeValue(ref BeforePulse);
            s.SerializeValue(ref AfterPulse);
            s.SerializeValue(ref Delay);
            s.SerializeValue(ref Decay);
            s.SerializeValue(ref Amplitude);
        }

        public bool Equals(InspectionRecord r) => Guest.Equals(r.Guest) && Visit == r.Visit && Revision == r.Revision && Known == r.Known && Asked == r.Asked && HasGuest == r.HasGuest && Busy == r.Busy && Test == r.Test && Tilt == r.Tilt && LastTilt == r.LastTilt && Pulsed == r.Pulsed && StartedAt == r.StartedAt && Mass == r.Mass && Metal == r.Metal && Balance == r.Balance && BeforePulse == r.BeforePulse && AfterPulse == r.AfterPulse && Delay == r.Delay && Decay == r.Decay && Amplitude == r.Amplitude;
    }

    public struct InspectionStatement : INetworkSerializable, IEquatable<InspectionStatement>
    {
        public byte Question;
        public FixedString512Bytes Answer;
        public void NetworkSerialize<T>(BufferSerializer<T> s)
            where T : IReaderWriter
        {
            s.SerializeValue(ref Question);
            s.SerializeValue(ref Answer);
        }

        public bool Equals(InspectionStatement s) => Question == s.Question && Answer.Equals(s.Answer);
    }

    [RequireComponent(typeof(NetworkObject))]
    public sealed class BaggageInspection : NetworkBehaviour, IInteractable
    {
        public static BaggageInspection Instance { get; private set; }

        public ReceptionDesk Reception;
        public ReceptionComputer Computer;
        public NightShiftController Shift;
        public MimicHunt Hunt;
        public Transform Platform;
        public Transform Tray;
        public Collider Controls;
        public TextMesh WorldReadout;
        public Transform[] RoomBagSpots;
        [SerializeField]
        BaggageCase[] cases;
        [SerializeField]
        BaggageCase[] inspectionPracticeCases, completeScenarioCases, huntOnlyCases;
        [SerializeField, Min(.2f)]
        float measurementSeconds = 2.2f;
        public AudioSource Mechanism;
        public AudioClip MotorSound, DryResponse, MetallicResponse;
        readonly NetworkVariable<InspectionRecord> record = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<ComputerSeat> seat = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<FixedString512Bytes> debrief = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkList<InspectionStatement> Statements;
        BaggageCase[] deck, ordinaryCases;
        int nextCase, admitted, refused, legitimateAdmitted, legitimateRefused, carriersRefused, carriersAdmitted, legitimateArrivals;
        public InspectionRecord State => record.Value;
        public ComputerSeat Seat => seat.Value;
        public string Debrief => debrief.Value.ToString();
        public float MeasurementSeconds => measurementSeconds;
        public int CaseCount => cases.Length;
        public GuestBaggage Current => State.HasGuest && State.Guest.TryGet(out var obj, NetworkManager) ? obj.GetComponent<GuestBaggage>() : null;
        public bool CanInteract => IsSpawned && Shift.IsRunning;
        public string Prompt => Seat.Occupied ? "Review shared baggage readings" : "Use baggage analyzer";
        public bool OwnsControls => IsSpawned && Seat.Occupied && Seat.User == NetworkManager.LocalClientId;
        public bool AllowInfiltration => HuntScenario.Instance == null || HuntScenario.Instance.Mode != HuntTestMode.Inspection;
        public bool Knows(AnalyzerTest test) => (State.Known & (1u << (int)test)) != 0;
        double Now => NetworkManager.ServerTime.Time;
        void Awake()
        {
            Instance = this;
            Statements = new NetworkList<InspectionStatement>();
        }

        public override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                ResetOnServer();
                NetworkManager.OnClientDisconnectCallback += Disconnected;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= Disconnected;
            StopAllCoroutines();
            if (Mechanism != null)
                Mechanism.Stop();
            GetComponent<AnalyzerPanel>()?.CloseLocal(false);
        }

        void Disconnected(ulong client)
        {
            if (Seat.Occupied && Seat.User == client)
                ReleaseSeat();
        }

        public void ResetOnServer()
        {
            if (!IsServer || !IsSpawned)
                return;
            record.Value = new InspectionRecord{Revision = State.Revision + 1};
            Statements.Clear();
            ReleaseSeat();
            debrief.Value = default;
            admitted = refused = legitimateAdmitted = legitimateRefused = carriersRefused = carriersAdmitted = legitimateArrivals = nextCase = 0;
            ordinaryCases = cases.Where(c => !c.containsMimic).ToArray();
            var mode = HuntScenario.Instance != null ? HuntScenario.Instance.Mode : HuntTestMode.Normal;
            if (mode == HuntTestMode.Inspection)
                deck = inspectionPracticeCases;
            else if (mode == HuntTestMode.Complete)
                deck = completeScenarioCases;
            else if (mode == HuntTestMode.HuntOnly)
                deck = huntOnlyCases;
            else
            {
                var innocent = ordinaryCases.OrderBy(_ => UnityEngine.Random.value).ToList();
                var dangerous = cases.Where(c => c.containsMimic).ToArray();
                innocent.Insert(UnityEngine.Random.Range(0, Mathf.Min(4, innocent.Count + 1)), dangerous[UnityEngine.Random.Range(0, dangerous.Length)]);
                deck = innocent.ToArray();
            }
        }

        public BaggageCase AssignCaseOnServer()
        {
            if (!IsServer)
                throw new InvalidOperationException("Only server assigns cases");
            // A normal shift contains at most one authored carrier; successful refusal never creates a replacement.
            var c = nextCase < deck.Length ? deck[nextCase++] : ordinaryCases[nextCase++ % ordinaryCases.Length];
            if (!c.containsMimic)
                legitimateArrivals++;
            return c;
        }

        public Vector3 RoomBagPosition(ServiceRoom room) => RoomBagSpots[Array.IndexOf(Reception.Rooms, room)].position;
        public bool ReleaseMimicOnServer(GuestBaggage bag)
        {
            if (!IsServer || !AllowInfiltration || !bag.AdmittedOnServer || bag.ServerCase == null || !bag.ServerCase.containsMimic || bag.Guest.Phase != GuestPhase.InRoom)
                return false;
            int node = Array.FindIndex(Hunt.Nodes, n => n.room == bag.Guest.Room);
            return node >= 0 && Hunt.BeginInfiltrationOnServer(node, bag.Guest, bag.State.RoomPosition);
        }

        public bool CanAdmit(GuestLifecycle guest) => guest != null && Current != null && Current.Guest == guest && State.Visit == guest.State.Visit && !State.Busy && Current.State.Phase == BaggagePhase.OnAnalyzer && (State.Asked & 1) != 0 && Knows(AnalyzerTest.Weight);
        public void RecordDecisionOnServer(GuestLifecycle guest, bool accepted)
        {
            if (!IsServer)
                return;
            var bag = guest.GetComponent<GuestBaggage>();
            var c = bag.ServerCase;
            if (accepted)
            {
                admitted++;
                if (c.containsMimic)
                    carriersAdmitted++;
                else
                    legitimateAdmitted++;
            }
            else
            {
                refused++;
                if (c.containsMimic)
                    carriersRefused++;
                else
                    legitimateRefused++;
            }

            bag.DecideOnServer(accepted);
        }

        public void FinishShiftOnServer()
        {
            if (!IsServer)
                return;
            // Ground truth is debrief information, never a live verdict or replicated case id.
            debrief.Value = new FixedString512Bytes($"Baggage decisions: {admitted} admitted / {refused} refused\nLegitimate guests admitted: {legitimateAdmitted}\nLegitimate stays refused: {legitimateRefused}\nCarriers refused with baggage: {carriersRefused}\nCarriers admitted: {carriersAdmitted}\n" + (legitimateArrivals > 0 && legitimateAdmitted == 0 ? "SERVICE FAILED — no legitimate guests accommodated." : "Safety refusals carry no business penalty; legitimate refusals are lost stays."));
            ReleaseSeat();
            var v = State;
            v.Busy = false;
            v.Revision++;
            record.Value = v;
        }

        bool MatchesFront(NetworkObjectReference obj, uint visit, out GuestBaggage bag)
        {
            bag = null;
            if (!IsServer || !Shift.IsRunning || !obj.TryGet(out var target, NetworkManager))
                return false;
            bag = target.GetComponent<GuestBaggage>();
            return bag != null && bag.Guest == Reception.WaitingNpc && bag.Guest.State.Visit == visit && bag.State.Visit == visit;
        }

        bool Near(ulong id) => ServiceInteractionValidation.CanReach(NetworkManager, id, Controls);
        bool Operator(ulong id, uint lease) => Seat.Occupied && Seat.User == id && Seat.Lease == lease && Near(id);
        bool EnsureRecord(GuestBaggage bag)
        {
            if (Current == bag && State.Visit == bag.State.Visit)
                return true;
            if (Current != null && (Current.State.Phase == BaggagePhase.ToAnalyzer || Current.State.Phase == BaggagePhase.OnAnalyzer || Current.State.Phase == BaggagePhase.Returning))
                return false;
            record.Value = new InspectionRecord{HasGuest = true, Guest = new NetworkObjectReference(bag.NetworkObject), Visit = bag.State.Visit, Revision = State.Revision + 1};
            Statements.Clear();
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void PlaceBagRpc(NetworkObjectReference guest, uint visit, RpcParams rpc = default)
        {
            if (!MatchesFront(guest, visit, out var bag) || !(Near(rpc.Receive.SenderClientId) || ServiceInteractionValidation.CanReach(NetworkManager, rpc.Receive.SenderClientId, bag.handle)) || !EnsureRecord(bag))
                return;
            if (bag.PlaceOnAnalyzer())
                Reception.TouchInspection(bag.Guest);
        }

        public void Interact() => RequestUse();
        public void RequestUse() => UseRpc(Seat.Lease);
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void UseRpc(uint lease, RpcParams rpc = default)
        {
            if (!IsServer || !Shift.IsRunning || !Near(rpc.Receive.SenderClientId))
                return;
            if (!Seat.Occupied && Seat.Lease == lease)
                seat.Value = new ComputerSeat{Occupied = true, User = rpc.Receive.SenderClientId, Lease = Seat.Lease + 1};
            OpenPanelRpc(RpcTarget.Single(rpc.Receive.SenderClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void OpenPanelRpc(RpcParams rpc = default) => GetComponent<AnalyzerPanel>().OpenLocal();
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ReleaseRpc(uint lease, RpcParams rpc = default)
        {
            if (IsServer && Seat.Occupied && Seat.User == rpc.Receive.SenderClientId && Seat.Lease == lease)
                ReleaseSeat();
        }

        void ReleaseSeat()
        {
            if (IsServer)
                seat.Value = new ComputerSeat{Lease = Seat.Lease + 1};
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void AskRpc(NetworkObjectReference guest, uint visit, BaggageQuestion question, uint lease, bool atComputer, RpcParams rpc = default)
        {
            ulong id = rpc.Receive.SenderClientId;
            if (!Enum.IsDefined(typeof(BaggageQuestion), question) || !MatchesFront(guest, visit, out var bag) || !(atComputer ? Computer.Authorizes(id, lease) : Operator(id, lease)) || !EnsureRecord(bag))
                return;
            if ((State.Asked & (1u << (int)question)) != 0)
                return;
            if (question == BaggageQuestion.Weight && !Knows(AnalyzerTest.Weight) || question == BaggageQuestion.Metal && !Knows(AnalyzerTest.Metal) || question == BaggageQuestion.Response && !Knows(AnalyzerTest.Pulse))
                return;
            Statements.Add(new InspectionStatement{Question = (byte)question, Answer = new FixedString512Bytes(bag.ServerCase.Answer(question))});
            var v = State;
            v.Asked |= 1u << (int)question;
            record.Value = v;
            Reception.TouchInspection(bag.Guest);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void TestRpc(AnalyzerTest test, uint revision, uint visit, uint lease, RpcParams rpc = default)
        {
            if (!IsServer || !Enum.IsDefined(typeof(AnalyzerTest), test) || !Operator(rpc.Receive.SenderClientId, lease) || State.Revision != revision || State.Visit != visit || State.Busy || Current == null || Current.Guest != Reception.WaitingNpc || Current.State.Phase != BaggagePhase.OnAnalyzer)
                return;
            var v = State;
            v.Test = test;
            v.Busy = true;
            v.StartedAt = Now;
            v.Revision++;
            record.Value = v;
            Reception.TouchInspection(Current.Guest);
            MotorSoundRpc();
            if (test == AnalyzerTest.Pulse)
            {
                var c = Current.ServerCase;
                ResponseSoundRpc(c.Metal >= 25, c.ResponseAmplitude, c.Delay, c.Decay, State.Visit);
            }
        }

        void CompleteTest()
        {
            var c = Current.ServerCase;
            var v = State;
            switch (v.Test)
            {
                case AnalyzerTest.Weight:
                    v.Mass = c.Mass;
                    break;
                case AnalyzerTest.Metal:
                    v.Metal = c.Metal;
                    break;
                case AnalyzerTest.TiltLeft:
                    v.Tilt = v.LastTilt = -1;
                    v.Pulsed = false;
                    break;
                case AnalyzerTest.TiltRight:
                    v.Tilt = v.LastTilt = 1;
                    v.Pulsed = false;
                    break;
                case AnalyzerTest.Level:
                    v.Tilt = 0;
                    v.Pulsed = false;
                    break;
                case AnalyzerTest.Pulse:
                    v.BeforePulse = c.Balance(v.Tilt, v.LastTilt, v.Pulsed);
                    v.Pulsed = true;
                    v.AfterPulse = c.Balance(v.Tilt, v.LastTilt, true);
                    v.Delay = c.Delay;
                    v.Decay = c.Decay;
                    v.Amplitude = c.ResponseAmplitude;
                    break;
            }

            if (v.Test >= AnalyzerTest.Balance)
            {
                v.Balance = c.Balance(v.Tilt, v.LastTilt, v.Pulsed);
                v.Known |= 1u << (int)AnalyzerTest.Balance;
            }

            v.Known |= 1u << (int)v.Test;
            v.Busy = false;
            v.Revision++;
            record.Value = v;
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        void MotorSoundRpc()
        {
            Mechanism.pitch = 1;
            Mechanism.PlayOneShot(MotorSound, .35f);
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        void ResponseSoundRpc(bool metallic, float amplitude, float delay, float decay, uint visit)
        {
            StartCoroutine(ResponseSound(metallic, amplitude, delay, decay, visit));
        }

        IEnumerator ResponseSound(bool metallic, float amplitude, float delay, float decay, uint visit)
        {
            yield return new WaitForSeconds(Mathf.Max(.02f, delay));
            if (!IsSpawned || !Shift.IsRunning || State.Visit != visit)
                yield break;
            // Onset and duration follow the measured model; the same timbres apply to innocent and carrier loads.
            var clip = metallic ? MetallicResponse : DryResponse;
            Mechanism.pitch = Mathf.Clamp(clip.length / Mathf.Max(.15f, decay), .3f, 3);
            Mechanism.PlayOneShot(clip, Mathf.Clamp(.12f + amplitude * .12f, .12f, .7f));
        }

        void Update()
        {
            if (!IsSpawned)
                return;
            if (IsServer)
            {
                if (Seat.Occupied && !Near(Seat.User))
                    ReleaseSeat();
                if (State.Busy)
                {
                    if (Current == null || Current.Guest != Reception.WaitingNpc || Current.State.Visit != State.Visit)
                    {
                        var v = State;
                        v.Busy = false;
                        v.Revision++;
                        record.Value = v;
                    }
                    else if (Now - State.StartedAt >= (State.Test == AnalyzerTest.Pulse ? Mathf.Max(measurementSeconds, Current.ServerCase.Delay + Current.ServerCase.Decay + .15f) : measurementSeconds))
                        CompleteTest();
                }
            }

            Tray.localRotation = Quaternion.Slerp(Tray.localRotation, Quaternion.Euler(0, 0, -State.Tilt * 12), Time.deltaTime * 4);
            WorldReadout.text = "MOTEL / LOAD ANALYZER\n" + (Current == null ? "PLACE BAG ON PLATFORM" : Current.Guest.GuestName + " / " + Current.State.Shell) + "\n" + (State.Busy ? "MEASURING..." : "READY") + "\nMASS  " + (Knows(AnalyzerTest.Weight) ? State.Mass.ToString("0.0") + " kg" : "--") + "\nMETAL " + (Knows(AnalyzerTest.Metal) ? State.Metal.ToString("0") + " / 100" : "--") + "\nBALANCE " + (Knows(AnalyzerTest.Balance) ? State.Balance.ToString("+0.0;-0.0;0.0") + " cm" : "--");
        }

        public bool ConfigurationValid() => Reception != null && Computer != null && Shift != null && Hunt != null && Platform != null && Controls != null && cases != null && cases.Length >= 12 && cases.All(c => c != null && c.Valid(out _)) && cases.Any(c => c.containsMimic) && cases.Any(c => !c.containsMimic) && inspectionPracticeCases != null && inspectionPracticeCases.Length == cases.Length && inspectionPracticeCases.All(c => cases.Contains(c)) && completeScenarioCases != null && completeScenarioCases.Length == 4 && completeScenarioCases.All(c => cases.Contains(c)) && completeScenarioCases.Count(c => c.containsMimic) == 1 && huntOnlyCases != null && huntOnlyCases.Length == 3 && huntOnlyCases.All(c => c != null && !c.containsMimic) && Tray != null && WorldReadout != null && MotorSound != null && DryResponse != null && MetallicResponse != null && RoomBagSpots != null && RoomBagSpots.Length == 4 && RoomBagSpots.All(p => p != null) && Mechanism != null;
    }
}
