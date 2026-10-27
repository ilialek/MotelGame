using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace MotelGame.Service
{
    public enum ShiftPhase : byte { WaitingToStart, Running, Finished }
    public struct ShiftSnapshot : INetworkSerializable, IEquatable<ShiftSnapshot>
    {
        public ShiftPhase Phase;
        public uint Number;
        public double StartedAt, Duration;
        public int GuestDeaths, MimicsEliminated, Evacuations;
        public int TrashDisposed, ElectricalFaults, ElectricalRepairs, Relocations;
        public int ArrivalTarget, Arrived, CheckedIn, CheckedOut, Unserved, Towels, Repairs, MissedRequests, Prepared, OccupiedAtClose;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T:IReaderWriter
        {
            s.SerializeValue(ref GuestDeaths);s.SerializeValue(ref MimicsEliminated);s.SerializeValue(ref Evacuations);
            s.SerializeValue(ref TrashDisposed);s.SerializeValue(ref ElectricalFaults);s.SerializeValue(ref ElectricalRepairs);s.SerializeValue(ref Relocations);
            s.SerializeValue(ref Phase);s.SerializeValue(ref Number);s.SerializeValue(ref StartedAt);s.SerializeValue(ref Duration);
            s.SerializeValue(ref ArrivalTarget);s.SerializeValue(ref Arrived);s.SerializeValue(ref CheckedIn);s.SerializeValue(ref CheckedOut);
            s.SerializeValue(ref Unserved);s.SerializeValue(ref Towels);s.SerializeValue(ref Repairs);s.SerializeValue(ref MissedRequests);
            s.SerializeValue(ref Prepared);s.SerializeValue(ref OccupiedAtClose);
        }
        public bool Equals(ShiftSnapshot v)=>GuestDeaths==v.GuestDeaths&&MimicsEliminated==v.MimicsEliminated&&Evacuations==v.Evacuations&&TrashDisposed==v.TrashDisposed&&ElectricalFaults==v.ElectricalFaults&&ElectricalRepairs==v.ElectricalRepairs&&Relocations==v.Relocations&&Phase==v.Phase&&Number==v.Number&&StartedAt==v.StartedAt&&Duration==v.Duration&&
            ArrivalTarget==v.ArrivalTarget&&Arrived==v.Arrived&&CheckedIn==v.CheckedIn&&CheckedOut==v.CheckedOut&&Unserved==v.Unserved&&
            Towels==v.Towels&&Repairs==v.Repairs&&MissedRequests==v.MissedRequests&&Prepared==v.Prepared&&OccupiedAtClose==v.OccupiedAtClose;
    }

    // Owns the night, its bounded arrival agenda and counters. Room/guest/request state stays in its existing owners.
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NightShiftController : NetworkBehaviour
    {
        [SerializeField, Min(30)] float durationSeconds=900;
        [SerializeField] Vector2Int arrivalCount=new Vector2Int(6,8);
        [SerializeField, Min(0)] float firstArrivalDelay=45;
        [SerializeField, Range(.1f,.95f)] float lastArrivalFraction=.8f;
        [SerializeField, Tooltip("Relative early / middle / late arrival density.")] Vector3 workloadWeights=new Vector3(2,3,2);
        [SerializeField] Vector2Int electricalFaultCount = new Vector2Int(1, 2);
        [SerializeField, Tooltip("Fault window as fractions of the shift; no new faults outside this window.")] Vector2 faultWindow = new Vector2(.3f, .72f);
        [SerializeField, Tooltip("0 selects a new seed each shift; nonzero repeats the electrical agenda deterministically.")] int electricalSeed;
        readonly List<double> faultTimes = new();
        System.Random faultRandom;
        int nextFault;
        double nextFaultAllowedAt;
        [SerializeField] ReceptionDesk reception;
        [SerializeField] ReceptionComputer computer;
        [SerializeField] GuestServiceRequests requests;
        [SerializeField] TextMesh clockLabel;
        readonly NetworkVariable<ShiftSnapshot> snapshot=new(default,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
        readonly List<double> arrivalTimes=new();
        public ShiftSnapshot State=>snapshot.Value;
        public bool IsRunning=>IsSpawned&&State.Phase==ShiftPhase.Running;
        public ReceptionDesk Reception=>reception;
        public double Elapsed=>State.Phase==ShiftPhase.WaitingToStart?0:State.Phase==ShiftPhase.Finished?State.Duration:
            Math.Clamp(NetworkManager.ServerTime.Time-State.StartedAt,0,State.Duration);
        public float Progress=>State.Duration>0?(float)(Elapsed/State.Duration):0;
        public string ClockText { get { int minutes=22*60+Mathf.FloorToInt(Progress*480);return $"{minutes/60%24:00}:{minutes%60:00}"; } }
        public bool ArrivalDue=>IsRunning&&State.Arrived<arrivalTimes.Count&&Progress<=lastArrivalFraction&&NetworkManager.ServerTime.Time>=arrivalTimes[State.Arrived];
        public override void OnNetworkSpawn()
        {
            if(IsServer)snapshot.Value=new ShiftSnapshot{Duration=durationSeconds};
            foreach(var room in reception.Rooms)room.StateChanged+=RoomChanged;
        }
        public override void OnNetworkDespawn(){foreach(var room in reception.Rooms)room.StateChanged-=RoomChanged;arrivalTimes.Clear();}
        void RoomChanged(RoomSnapshot before,RoomSnapshot after)
        {
            if(!IsServer||!IsRunning)return;
            var v=State;
            if(before.Status==RoomStatus.Dirty&&after.Status==RoomStatus.Ready)v.Prepared++;
            snapshot.Value=v;
        }
        internal void RecordGuestDeath(){if(IsServer&&IsRunning){var v=State;v.GuestDeaths++;snapshot.Value=v;}}
        internal void RecordMimicKill(){if(IsServer&&IsRunning){var v=State;v.MimicsEliminated++;snapshot.Value=v;}}
        internal void RecordEvacuation(){if(IsServer&&IsRunning){var v=State;v.Evacuations++;snapshot.Value=v;}}
        // Host-only M14 setup uses the same reset, arrival agenda and counters as normal reception.
        internal void EndForHuntDebug(){if(IsServer&&IsRunning)FinishShift();}
        internal bool DebugStartHuntShift(float seconds=480)
        {
            if(!IsServer||!IsSpawned||IsRunning)return false;
            BeginShift(seconds,true); return true;
        }
        internal void RecordCheckIn() { if(IsServer&&IsRunning){var v=State;v.CheckedIn++;snapshot.Value=v;} }
        internal void RecordCheckout() { if(IsServer&&IsRunning){var v=State;v.CheckedOut++;snapshot.Value=v;} }
        internal void RecordDisposal() { if(IsServer&&IsRunning){var v=State;v.TrashDisposed++;snapshot.Value=v;} }
        internal void RecordElectricalRepair() { if(IsServer&&IsRunning){var v=State;v.ElectricalRepairs++;snapshot.Value=v;nextFaultAllowedAt=NetworkManager.ServerTime.Time+Math.Min(45,State.Duration*.05);} }
        internal void RecordRelocation() { if(IsServer&&IsRunning){var v=State;v.Relocations++;snapshot.Value=v;} }
        internal void RecordArrival(){if(!IsServer||!ArrivalDue)return;var v=State;v.Arrived++;snapshot.Value=v;}
        internal void RecordUnserved(){if(!IsServer||!IsRunning)return;var v=State;v.Unserved++;snapshot.Value=v;}
        internal void RecordRequest(bool completed,GuestRequestKind kind)
        {
            if(!IsServer||!IsRunning)return;var v=State;
            if(!completed)v.MissedRequests++;else if(kind==GuestRequestKind.CleanTowel)v.Towels++;else if(kind==GuestRequestKind.RepairTelevision)v.Repairs++;
            snapshot.Value=v;
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        public void StartShiftRpc(uint expectedNumber,uint lease,RpcParams rpcParams=default)
        {
            if(!IsServer||!IsSpawned||IsRunning||State.Number!=expectedNumber||!computer.Authorizes(rpcParams.Receive.SenderClientId,lease))return;
            BeginShift(durationSeconds,false);
        }
        void BeginShift(float seconds,bool debug)
        {
            FindFirstObjectByType<MotelGame.Horror.MimicHunt>()?.ResetOnServer();
            // Reset while not Running: room events cannot inflate the next night's statistics.
            requests.ResetOnServer(false);reception.ResetOnServer();
            foreach(var room in reception.Rooms)room.ResetOnServer();
            computer.ReleaseOnServer();
            var v=new ShiftSnapshot{Phase=ShiftPhase.Running,Number=State.Number+1,StartedAt=NetworkManager.ServerTime.Time,
                Duration=Mathf.Max(30,seconds),ArrivalTarget=UnityEngine.Random.Range(Mathf.Max(1,arrivalCount.x),Mathf.Max(arrivalCount.x,arrivalCount.y)+1)};
            arrivalTimes.Clear();
            // Stratified random timings keep natural gaps without an unbounded arrival stream.
            Vector3 w=new Vector3(Mathf.Max(.01f,workloadWeights.x),Mathf.Max(.01f,workloadWeights.y),Mathf.Max(.01f,workloadWeights.z));
            float total=w.x+w.y+w.z;
            for(int i=0;i<v.ArrivalTarget;i++)
            {
                float u=(i+UnityEngine.Random.Range(.2f,.8f))/v.ArrivalTarget*total;
                float f=u<w.x?u/w.x*.3f:u<w.x+w.y?.3f+(u-w.x)/w.y*.35f:.65f+(u-w.x-w.y)/w.z*.35f;
                double offset=Mathf.Lerp(Mathf.Min(firstArrivalDelay,(float)v.Duration*lastArrivalFraction*.5f),(float)v.Duration*lastArrivalFraction,f);
                arrivalTimes.Add(v.StartedAt+offset);
            }
            if(debug) { arrivalTimes.Clear(); v.ArrivalTarget=2; arrivalTimes.Add(v.StartedAt+1);arrivalTimes.Add(v.StartedAt+8); }
            snapshot.Value=v;
            BuildElectricalAgenda();
        }
        void BuildElectricalAgenda()
        {
            faultTimes.Clear(); nextFault = 0; nextFaultAllowedAt = 0;
            faultRandom = new System.Random(electricalSeed == 0 ? UnityEngine.Random.Range(1, int.MaxValue) : electricalSeed);
            int count = faultRandom.Next(Mathf.Max(0, electricalFaultCount.x), Mathf.Max(electricalFaultCount.x, electricalFaultCount.y) + 1);
            for (int i = 0; i < count; i++)
            {
                float fraction = Mathf.Lerp(faultWindow.x, faultWindow.y, (i + .2f + (float)faultRandom.NextDouble() * .6f) / count);
                faultTimes.Add(State.StartedAt + State.Duration * fraction);
            }
        }
        void ScheduleElectricalFault()
        {
            if (nextFault >= faultTimes.Count || Progress > faultWindow.y || NetworkManager.ServerTime.Time < faultTimes[nextFault] || NetworkManager.ServerTime.Time < nextFaultAllowedAt ||
                Array.Exists(reception.Rooms, r => r.State.Faulted)) return;
            // Reserved rooms cannot change availability under a guest already walking there.
            var candidates = Array.FindAll(reception.Rooms, r => !r.State.Reserved &&
                (r.State.Status != RoomStatus.Occupied || (r.Occupant != null && r.Occupant.IsInRoomFor(r))));
            if (candidates.Length == 0) return;
            // Prefer an occupied room to expose the repair/relocation decision, while empty rooms can still fail.
            var occupied = Array.FindAll(candidates, r => r.State.Status == RoomStatus.Occupied);
            var choice = occupied.Length > 0 && faultRandom.NextDouble() < .75 ? occupied : candidates;
            if (TryGenerateFault(choice[faultRandom.Next(choice.Length)])) nextFault++;
        }
        internal bool TryGenerateFault(ServiceRoom room)
        {
            if (!IsServer || !IsRunning || Array.IndexOf(reception.Rooms, room) < 0 || Array.Exists(reception.Rooms, r => r.State.Faulted) || !room.TryFaultOnServer()) return false;
            var v = State; v.ElectricalFaults++; snapshot.Value = v; return true;
        }
        void Update()
        {
            if(!IsSpawned)return;
            if(IsServer&&IsRunning) { if(Elapsed>=State.Duration)FinishShift(); else ScheduleElectricalFault(); }
            if(clockLabel!=null)clockLabel.text="NIGHT SHIFT\n"+ClockText+"\n"+State.Phase;
        }
        void FinishShift()
        {
            // Cancel exactly once before freezing counters; quiesce routes without destroying NGO identities.
            FindFirstObjectByType<MotelGame.Horror.MimicHunt>()?.ResetOnServer();
            requests.ResetOnServer(true);
            foreach(var g in reception.Guests)if(g.Phase==GuestPhase.Arriving||g.Phase==GuestPhase.WaitingForCheckIn)RecordUnserved();
            var v=State;v.OccupiedAtClose=Array.FindAll(reception.Guests,g=>g.State.Assigned&&g.Phase!=GuestPhase.Absent&&g.Phase!=GuestPhase.Leaving).Length;
            v.Phase=ShiftPhase.Finished;snapshot.Value=v;
            // Closing may catch an NPC in a narrow doorway. Retire the existing pool
            // and reset rooms after freezing totals, so no stopped capsule traps a player.
            reception.ResetOnServer();
            foreach(var room in reception.Rooms)room.ResetOnServer();
            computer.ReleaseOnServer();
        }
        public string SummaryText=>$"NIGHT {State.Number} COMPLETE — 06:00\n\nGuests checked in: {State.CheckedIn}\nGuests checked out: {State.CheckedOut}\nLeft without service: {State.Unserved}\nTowels delivered: {State.Towels}\nTV repairs: {State.Repairs}\nMissed / cancelled requests: {State.MissedRequests}\nRooms prepared: {State.Prepared}\nTrash bags disposed: {State.TrashDisposed}\nElectrical repairs: {State.ElectricalRepairs}/{State.ElectricalFaults}\nGuest relocations: {State.Relocations}\nOccupied at closing: {State.OccupiedAtClose}\nGuest deaths: {State.GuestDeaths}\nGuests evacuated: {State.Evacuations}\nMimics eliminated: {State.MimicsEliminated}";
        void OnGUI()
        {
            if(!IsSpawned||NetworkManager.LocalClient?.PlayerObject==null)return;
            var i=NetworkManager.LocalClient.PlayerObject.GetComponent<MotelGame.Interactions.PlayerInteractor>();if(i.MenuOpen)return;
            if(State.Phase==ShiftPhase.Finished)
            {
                float w=Mathf.Min(350,Screen.width-24);GUILayout.BeginArea(new Rect(Screen.width-w-12,45,w,Mathf.Min(Screen.height-65,440)),GUI.skin.box);
                GUILayout.Label(SummaryText);GUILayout.Space(8);GUILayout.Label("Reception computer [E] → Start Next Shift");GUILayout.EndArea();
            }
            else GUI.Box(new Rect(12,Screen.height-108,240,28),IsRunning?$"NIGHT {State.Number}   {ClockText}":"22:00 — Start shift at the computer");
        }
        public bool ConfigurationValid()=>electricalFaultCount.x>=0&&electricalFaultCount.y>=electricalFaultCount.x&&electricalFaultCount.y<=4&&faultWindow.x>0&&faultWindow.y>faultWindow.x&&faultWindow.y<1&&durationSeconds>=30&&arrivalCount.x>=1&&arrivalCount.y>=arrivalCount.x&&arrivalCount.y<=32&&
            lastArrivalFraction>0&&lastArrivalFraction<1&&reception!=null&&computer!=null&&requests!=null&&clockLabel!=null;
    }
}
