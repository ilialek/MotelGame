using System;
using System.Linq;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using MotelGame.Service;
using MotelGame.Horror;

namespace MotelGame.Registration
{
    public struct RoadWatchLookup : INetworkSerializable,IEquatable<RoadWatchLookup>
    {
        public uint Revision;
        public bool Completed;
        public FixedString32Bytes Plate;
        public int Report;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T:IReaderWriter
        {s.SerializeValue(ref Revision);s.SerializeValue(ref Completed);s.SerializeValue(ref Plate);s.SerializeValue(ref Report);}
        public bool Equals(RoadWatchLookup x)=>Revision==x.Revision&&Completed==x.Completed&&Plate.Equals(x.Plate)&&Report==x.Report;
    }

    [RequireComponent(typeof(NetworkObject))]
    public sealed class RegistrationDesk : NetworkBehaviour
    {
        public static RegistrationDesk Instance {get;private set;}
        public ReceptionDesk Reception;
        public NightShiftController Shift;
        public MimicHunt Hunt;
        public ReceptionComputer[] Workstations;
        public GuestCase[] Cases;
        readonly NetworkVariable<RoadWatchLookup> lookup=new(default,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
        readonly NetworkVariable<FixedString512Bytes> debrief=new(default,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
        Queue<GuestCase> agenda=new();
        int admittedHumans, refusedHumans, admittedMimics, refusedMimics;
        RegisteredGuest searchGuest; uint searchVisit;
        void Update()
        {
            if(!IsServer||!IsSpawned)return;
            var front=Waiting;uint visit=front==null?0:front.State.Visit;
            if(front==searchGuest&&visit==searchVisit)return;
            searchGuest=front;searchVisit=visit;lookup.Value=new RoadWatchLookup{Revision=Lookup.Revision+1,Report=-1};
        }
        public RoadWatchLookup Lookup=>lookup.Value;
        public string Debrief=>debrief.Value.ToString();
        public RegisteredGuest Waiting=>Reception.WaitingNpc?.GetComponent<RegisteredGuest>();
        public bool CanAdmit(GuestLifecycle guest)=>guest!=null&&guest.GetComponent<RegisteredGuest>() is RegisteredGuest r&&r.State.Active&&r.State.Visit==guest.State.Visit&&r.State.Decision==RegistrationDecision.Pending&&r.Complete;
        void Awake()=>Instance=this;
        public override void OnDestroy(){if(Instance==this)Instance=null;base.OnDestroy();}
        public override void OnNetworkSpawn(){if(IsServer)ResetOnServer();}
        internal void ResetOnServer()
        {
            if(!IsServer||!IsSpawned)return;
            lookup.Value=new RoadWatchLookup{Revision=Lookup.Revision+1,Report=-1};debrief.Value=default;
            admittedHumans=refusedHumans=admittedMimics=refusedMimics=0;agenda.Clear();
        }
        internal void BeginShiftOnServer(int arrivals)
        {
            if(!IsServer)return;
            agenda=new Queue<GuestCase>(MakeAgenda(arrivals));
        }
        public GuestCase[] MakeAgenda(int count)
        {
            var reports=new Queue<GuestCase>(Cases.Where(c=>c.hasReport).OrderBy(_=>UnityEngine.Random.value));
            var quiet=new Queue<GuestCase>(Cases.Where(c=>!c.hasReport).OrderBy(_=>UnityEngine.Random.value));
            var result=new List<GuestCase>();
            // One report per pair, random order. Odd shifts naturally get floor/ceil half.
            for(int i=0;i<count;)
            {
                bool firstReport=UnityEngine.Random.value<.5f;
                for(int j=0;j<2&&i<count;j++,i++)
                {
                    bool reported=j==0?firstReport:!firstReport;var pool=reported?reports:quiet;
                    if(pool.Count==0)foreach(var c in Cases.Where(c=>c.hasReport==reported).OrderBy(_=>UnityEngine.Random.value))pool.Enqueue(c);
                    result.Add(pool.Dequeue());
                }
            }
            return result.ToArray();
        }
        internal GuestCase TakeNextCaseOnServer()
        {
            if(!IsServer)throw new InvalidOperationException("Only the server assigns identities");
            if(agenda.Count==0)BeginShiftOnServer(Cases.Length);
            return agenda.Dequeue();
        }
        public static string NormalizePlate(string text)
        {
            if(string.IsNullOrWhiteSpace(text)||text.Length>32)return "";
            text=text.ToUpperInvariant();
            if(text.Any(c=>!(c>='A'&&c<='Z'||c>='0'&&c<='9'||c==' '||c=='-')))return "";
            string normalized=new string(text.Where(c=>c!=' '&&c!='-').ToArray());
            return normalized.Length>=3&&normalized.Length<=12?normalized:"";
        }
        bool Authorized(ulong sender,NetworkObjectReference station,uint lease)
        {
            if(!IsServer||!IsSpawned||!Shift.IsRunning||!station.TryGet(out var obj,NetworkManager))return false;
            var computer=obj.GetComponent<ReceptionComputer>();
            return computer!=null&&Array.IndexOf(Workstations,computer)>=0&&computer.Authorizes(sender,lease);
        }
        bool Matches(NetworkObjectReference expected,uint visit,out RegisteredGuest record)
        {
            record=Waiting;
            return record!=null&&record.State.Visit==visit&&expected.TryGet(out var obj,NetworkManager)&&obj==record.NetworkObject;
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        public void AskRpc(NetworkObjectReference guest,uint visit,RegistrationQuestion question,NetworkObjectReference workstation,uint lease,int report=-1,uint lookupRevision=0,RpcParams rpc=default)
        {
            if(question==RegistrationQuestion.FollowUp&&(Lookup.Revision!=lookupRevision||!Lookup.Completed||Lookup.Report!=report))return;
            if(Authorized(rpc.Receive.SenderClientId,workstation,lease)&&Matches(guest,visit,out var record)&&record.AskOnServer(question,report))Reception.TouchInspection(record.Guest);
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        public void SearchRpc(string plate,uint expectedRevision,NetworkObjectReference workstation,uint lease,RpcParams rpc=default)
        {
            if(!Authorized(rpc.Receive.SenderClientId,workstation,lease)||Lookup.Revision!=expectedRevision)return;
            string normalized=NormalizePlate(plate);if(normalized.Length==0)return;
            int index=Array.FindIndex(Cases,c=>c.hasReport&&NormalizePlate(c.actualPlate)==normalized);
            lookup.Value=new RoadWatchLookup{Completed=true,Revision=Lookup.Revision+1,Plate=new FixedString32Bytes(normalized),Report=index};
            if(index>=0)foreach(var guest in Reception.Guests)guest.GetComponent<RegisteredGuest>().ReportFoundOnServer(Cases[index]);
        }
        internal void RecordDecisionOnServer(GuestLifecycle guest,bool admit)
        {
            if(!IsServer)return;
            var r=guest.GetComponent<RegisteredGuest>();
            if(!r.DecideOnServer(admit,admit?int.Parse(guest.Room.RoomNumber):0))return;
            if(r.IsMimicOnServer){if(admit)admittedMimics++;else refusedMimics++;}
            else if(admit)admittedHumans++;else refusedHumans++;
        }
        internal void FinishShiftOnServer()
        {
            if(!IsServer)return;
            debrief.Value=new FixedString512Bytes($"Registration decisions\nHuman guests admitted: {admittedHumans}\nHuman stays refused: {refusedHumans}\nDisguised Mimics admitted: {admittedMimics}\nDisguised Mimics refused: {refusedMimics}\nRefused human guests are lost stays. No automatic verdict was supplied during registration.");
        }
        public bool ConfigurationValid()=>Reception!=null&&Shift!=null&&Hunt!=null&&Cases!=null&&Cases.Length==6&&Cases.All(c=>c!=null&&c.Valid(out _))&&Cases.Count(c=>c.hasReport)==3&&Cases.Select(c=>NormalizePlate(c.actualPlate)).Distinct().Count()==6&&Workstations!=null&&Workstations.Length==2&&Workstations.All(c=>c!=null)&&Reception.Guests.All(g=>g.GetComponent<RegisteredGuest>()!=null);
    }
}
