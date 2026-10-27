using System.Linq;
using UnityEngine;
using MotelGame.Service;
using MotelGame.Horror;

namespace MotelGame.Inspection
{
    public enum HuntTestMode
    {
        Normal,
        Inspection,
        Complete,
        HuntOnly
    }

    public sealed class HuntScenario : MonoBehaviour
    {
        public static HuntScenario Instance { get; private set; }

        public BaggageInspection inspection;
        public MimicHunt hunt;
        public HuntTestMode Mode { get; private set; }

        bool startedHunt;
        readonly System.Collections.Generic.HashSet<GuestLifecycle> extended = new();
        public string Description => Mode switch
        {
            HuntTestMode.Inspection => $"INSPECTION PRACTICE · 12-minute shift\n{inspection.CaseCount} authored cases; manual admission. Short stays and automatic checkout/turnover. No creature release in this isolated mode.",
            HuntTestMode.Complete => "COMPLETE GAMEPLAY · 10-minute shift\nFour arrivals, manual inspection/admission. One carrier in the sequence. Refusal prevents the hunt; no replacement is spawned.",
            HuntTestMode.HuntOnly => "HUNT ONLY · 10-minute shift\nThree guests walk to prepared rooms automatically. Hunt starts when all three arrive. One protected carrier, two potential victims.",
            _ => "ORDINARY SHIFT · 15 minutes\nUse Reception to start the shift. Arrivals require baggage registration."
        };
        void Awake() => Instance = this;
        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void OrdinaryOnServer()
        {
            if (hunt.IsServer)
            {
                Mode = HuntTestMode.Normal;
                startedHunt = false;
                extended.Clear();
            }
        }

        public void Begin(HuntTestMode mode)
        {
            if (!hunt.IsHost || !hunt.IsSpawned)
                return;
            if (inspection.Shift.IsRunning)
                inspection.Shift.EndForHuntDebug();
            Mode = mode;
            startedHunt = false;
            extended.Clear();
            inspection.Shift.DebugStartScenario(mode == HuntTestMode.Inspection ? 720 : 600, mode == HuntTestMode.Inspection ? inspection.CaseCount : mode == HuntTestMode.HuntOnly ? 3 : 4, mode == HuntTestMode.Inspection ? 10 : 16);
            foreach (var room in inspection.Reception.Rooms)
                room.PrepareForHuntDebug();
        }

        public void ResetScenario() => Begin(Mode == HuntTestMode.Normal ? HuntTestMode.Complete : Mode);
        void Update()
        {
            if (!hunt.IsServer || !hunt.IsSpawned || !inspection.Shift.IsRunning || Mode == HuntTestMode.Normal)
                return;
            var reception = inspection.Reception;
            foreach (var guest in reception.Guests)
                if (guest.Phase == GuestPhase.InRoom && extended.Add(guest))
                    guest.ExtendDebugStay(hunt.NetworkManager.ServerTime.Time + (Mode == HuntTestMode.Inspection ? 30 : 600));
                else if (guest.Phase == GuestPhase.Absent)
                    extended.Remove(guest);
            if (Mode == HuntTestMode.Inspection)
            {
                var checkout = reception.CheckoutNpc;
                if (checkout != null)
                    checkout.Room.TryCheckout(checkout.Room.State.Cycle, checkout);
                foreach (var room in reception.Rooms)
                    if (room.State.Status == RoomStatus.Dirty && !room.State.Reserved)
                        room.PrepareForHuntDebug();
            }

            if (Mode != HuntTestMode.HuntOnly)
                return;
            var waiting = reception.WaitingNpc;
            var ready = reception.Rooms.FirstOrDefault(r => r.AvailableForCheckIn);
            if (waiting != null && ready != null && waiting.TryCheckIn(ready, ready.State.Cycle))
                inspection.RecordDecisionOnServer(waiting, true);
            if (!startedHunt && reception.Guests.Count(g => g.Phase == GuestPhase.InRoom) >= 3)
                startedHunt = hunt.ActivateOnServer(0, reception.Rooms[0].Occupant);
        }
    }
}
