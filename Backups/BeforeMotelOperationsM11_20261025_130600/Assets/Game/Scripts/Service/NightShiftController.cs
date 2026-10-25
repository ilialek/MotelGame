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
        public int ArrivalTarget, Arrived, CheckedIn, CheckedOut, Unserved, Towels, Repairs, MissedRequests, Prepared, OccupiedAtClose;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T:IReaderWriter
        {
            s.SerializeValue(ref Phase);s.SerializeValue(ref Number);s.SerializeValue(ref StartedAt);s.SerializeValue(ref Duration);
            s.SerializeValue(ref ArrivalTarget);s.SerializeValue(ref Arrived);s.SerializeValue(ref CheckedIn);s.SerializeValue(ref CheckedOut);
            s.SerializeValue(ref Unserved);s.SerializeValue(ref Towels);s.SerializeValue(ref Repairs);s.SerializeValue(ref MissedRequests);
            s.SerializeValue(ref Prepared);s.SerializeValue(ref OccupiedAtClose);
        }
        public bool Equals(ShiftSnapshot v)=>Phase==v.Phase&&Number==v.Number&&StartedAt==v.StartedAt&&Duration==v.Duration&&
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
            if(before.Status==RoomStatus.Ready&&after.Status==RoomStatus.Occupied)v.CheckedIn++;
            if(before.Status==RoomStatus.Occupied&&after.Status==RoomStatus.Dirty)v.CheckedOut++;
            snapshot.Value=v;
        }
        internal void RecordArrival(){if(!IsServer||!ArrivalDue)return;var v=State;v.Arrived++;snapshot.Value=v;}
        internal void RecordUnserved(){if(!IsServer||!IsRunning)return;var v=State;v.Unserved++;snapshot.Value=v;}
        internal void RecordRequest(bool completed,GuestRequestKind kind)
        {
            if(!IsServer||!IsRunning)return;var v=State;
            if(!completed)v.MissedRequests++;else if(kind==GuestRequestKind.CleanTowel)v.Towels++;else v.Repairs++;
            snapshot.Value=v;
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        public void StartShiftRpc(uint expectedNumber,uint lease,RpcParams rpcParams=default)
        {
            if(!IsServer||!IsSpawned||IsRunning||State.Number!=expectedNumber||!computer.Authorizes(rpcParams.Receive.SenderClientId,lease))return;
            // Reset while not Running: room events cannot inflate the next night's statistics.
            requests.ResetOnServer(false);reception.ResetOnServer();
            foreach(var room in reception.Rooms)room.ResetOnServer();
            computer.ReleaseOnServer();
            var v=new ShiftSnapshot{Phase=ShiftPhase.Running,Number=State.Number+1,StartedAt=NetworkManager.ServerTime.Time,
                Duration=Mathf.Max(30,durationSeconds),ArrivalTarget=UnityEngine.Random.Range(Mathf.Max(1,arrivalCount.x),Mathf.Max(arrivalCount.x,arrivalCount.y)+1)};
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
            snapshot.Value=v;
        }
        void Update()
        {
            if(!IsSpawned)return;
            if(IsServer&&IsRunning&&Elapsed>=State.Duration)FinishShift();
            if(clockLabel!=null)clockLabel.text="NIGHT SHIFT\n"+ClockText+"\n"+State.Phase;
        }
        void FinishShift()
        {
            // Cancel exactly once before freezing counters; quiesce routes without destroying NGO identities.
            requests.ResetOnServer(true);
            foreach(var g in reception.Guests)if(g.Phase==GuestPhase.Arriving||g.Phase==GuestPhase.WaitingForCheckIn)RecordUnserved();
            var v=State;v.OccupiedAtClose=Array.FindAll(reception.Rooms,r=>r.State.Status==RoomStatus.Occupied).Length;
            v.Phase=ShiftPhase.Finished;snapshot.Value=v;
            // Closing may catch an NPC in a narrow doorway. Retire the existing pool
            // and reset rooms after freezing totals, so no stopped capsule traps a player.
            reception.ResetOnServer();
            foreach(var room in reception.Rooms)room.ResetOnServer();
            computer.ReleaseOnServer();
        }
        public string SummaryText=>$"NIGHT {State.Number} COMPLETE — 06:00\n\nGuests checked in: {State.CheckedIn}\nGuests checked out: {State.CheckedOut}\nLeft without service: {State.Unserved}\nTowels delivered: {State.Towels}\nTV repairs: {State.Repairs}\nMissed / cancelled requests: {State.MissedRequests}\nRooms prepared: {State.Prepared}\nOccupied at closing: {State.OccupiedAtClose}";
        void OnGUI()
        {
            if(!IsSpawned||NetworkManager.LocalClient?.PlayerObject==null)return;
            var i=NetworkManager.LocalClient.PlayerObject.GetComponent<MotelGame.Interactions.PlayerInteractor>();if(i.MenuOpen)return;
            if(State.Phase==ShiftPhase.Finished)
            {
                float w=Mathf.Min(350,Screen.width-24);GUILayout.BeginArea(new Rect(Screen.width-w-12,45,w,300),GUI.skin.box);
                GUILayout.Label(SummaryText);GUILayout.Space(8);GUILayout.Label("Reception computer [E] → Start Next Shift");GUILayout.EndArea();
            }
            else GUI.Box(new Rect(12,Screen.height-108,240,28),IsRunning?$"NIGHT {State.Number}   {ClockText}":"22:00 — Start shift at the computer");
        }
        public bool ConfigurationValid()=>durationSeconds>=30&&arrivalCount.x>=1&&arrivalCount.y>=arrivalCount.x&&arrivalCount.y<=32&&
            lastArrivalFraction>0&&lastArrivalFraction<1&&reception!=null&&computer!=null&&requests!=null&&clockLabel!=null;
    }
}
