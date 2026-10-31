using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using MotelGame.Service;
using MotelGame.Characters;
using MotelGame.Horror;

namespace MotelGame.Registration
{
    public struct GuestRegistrationState : INetworkSerializable, IEquatable<GuestRegistrationState>
    {
        public uint Visit, Revision, Asked;
        public bool Active, ReservationKnown, Reserved, ReportSeen;
        public byte Appearance, Vehicle;
        public Vector3 Clothing, Paint;
        public FixedString64Bytes Name;
        public FixedString32Bytes ActualPlate, ClaimedPlate;
        public FixedString128Bytes ReservationEntry, FollowUpPrompt;
        public int AssignedRoom;
        public byte ParkingSlot, ExpectedGuests;
        public bool ParkingKnown;
        public FixedString32Bytes FirstName, LastName, DateOfBirth, BookingFirstName, BookingLastName, CheckInDate, RequestedRoom;
        public FixedString512Bytes BookingNote;
        public RegistrationDecision Decision;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T:IReaderWriter
        {
            s.SerializeValue(ref Visit);s.SerializeValue(ref Revision);s.SerializeValue(ref Asked);s.SerializeValue(ref Active);
            s.SerializeValue(ref ReservationKnown);s.SerializeValue(ref Reserved);s.SerializeValue(ref ReportSeen);
            s.SerializeValue(ref Appearance);s.SerializeValue(ref Vehicle);s.SerializeValue(ref Clothing);s.SerializeValue(ref Paint);
            s.SerializeValue(ref Name);s.SerializeValue(ref ActualPlate);s.SerializeValue(ref ClaimedPlate);s.SerializeValue(ref ReservationEntry);
            s.SerializeValue(ref ParkingSlot);s.SerializeValue(ref ParkingKnown);s.SerializeValue(ref ExpectedGuests);
            s.SerializeValue(ref FirstName);s.SerializeValue(ref LastName);s.SerializeValue(ref DateOfBirth);
            s.SerializeValue(ref BookingFirstName);s.SerializeValue(ref BookingLastName);s.SerializeValue(ref CheckInDate);
            s.SerializeValue(ref RequestedRoom);s.SerializeValue(ref BookingNote);
            s.SerializeValue(ref FollowUpPrompt);s.SerializeValue(ref AssignedRoom);s.SerializeValue(ref Decision);
        }
        public bool Equals(GuestRegistrationState x)=>ParkingSlot==x.ParkingSlot&&ParkingKnown==x.ParkingKnown&&ExpectedGuests==x.ExpectedGuests&&FirstName.Equals(x.FirstName)&&LastName.Equals(x.LastName)&&DateOfBirth.Equals(x.DateOfBirth)&&BookingFirstName.Equals(x.BookingFirstName)&&BookingLastName.Equals(x.BookingLastName)&&CheckInDate.Equals(x.CheckInDate)&&RequestedRoom.Equals(x.RequestedRoom)&&BookingNote.Equals(x.BookingNote)&&Visit==x.Visit&&Revision==x.Revision&&Asked==x.Asked&&Active==x.Active&&ReservationKnown==x.ReservationKnown&&Reserved==x.Reserved&&ReportSeen==x.ReportSeen&&Appearance==x.Appearance&&Vehicle==x.Vehicle&&Clothing==x.Clothing&&Paint==x.Paint&&Name.Equals(x.Name)&&ActualPlate.Equals(x.ActualPlate)&&ClaimedPlate.Equals(x.ClaimedPlate)&&ReservationEntry.Equals(x.ReservationEntry)&&FollowUpPrompt.Equals(x.FollowUpPrompt)&&AssignedRoom==x.AssignedRoom&&Decision==x.Decision;
    }
    public struct GuestStatement : INetworkSerializable,IEquatable<GuestStatement>
    {
        public RegistrationQuestion Question;
        public FixedString512Bytes Answer;
        public FixedString128Bytes Prompt;
        public int Report;
        public double SpokenAt;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T:IReaderWriter {s.SerializeValue(ref Question);s.SerializeValue(ref Answer);s.SerializeValue(ref Prompt);s.SerializeValue(ref Report);s.SerializeValue(ref SpokenAt);}
        public bool Equals(GuestStatement x)=>Question==x.Question&&Answer.Equals(x.Answer)&&Prompt.Equals(x.Prompt)&&Report==x.Report&&SpokenAt==x.SpokenAt;
    }

    // One persistent NPC slot, one server-assigned visit. No replicated truth flag or case index.
    public sealed class RegisteredGuest : NetworkBehaviour
    {
        public CharacterVisuals character;
        public GuestVehicleView vehicle;
        readonly NetworkVariable<GuestRegistrationState> state=new(default,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
        public NetworkList<GuestStatement> Statements;
        GuestLifecycle guest;
        GuestCase actualCase;
        double revealAt;
        public bool AwaitingMimicActivation=>IsMimicOnServer&&State.Decision==RegistrationDecision.Admitted;
        internal void MarkMissingOnServer(){if(IsServer){var v=State;v.Decision=RegistrationDecision.Missing;v.Revision++;state.Value=v;}}
        public GuestRegistrationState State=>state.Value;
        public GuestLifecycle Guest=>guest;
        public GuestCase ServerCase=>IsServer?actualCase:null;
        public bool IsMimicOnServer=>IsServer&&actualCase!=null&&actualCase.disguisedMimic;
        public string PublicName=>State.Name.IsEmpty?"Arriving guest":State.Name.ToString();
        // Investigation is optional: admission may be decided as soon as the guest reaches the desk.
        public bool Complete=>State.Active;
        public GuestCase Presentation=>Array.Find(RegistrationDesk.Instance.Cases,c=>c.guestName==PublicName);
        public string ParkingCode=>State.ParkingKnown?"P"+State.ParkingSlot.ToString("00"):"NOT REGISTERED";
        public bool Asked(RegistrationQuestion q)=>(State.Asked&(1u<<(int)q))!=0;
        public string Statement(RegistrationQuestion q)
        {foreach(var line in Statements)if(line.Question==q)return line.Answer.ToString();return "Not asked yet";}
        void Awake(){guest=GetComponent<GuestLifecycle>();Statements=new NetworkList<GuestStatement>();}
        public override void OnNetworkSpawn()
        {
            state.OnValueChanged+=Changed;
            if(IsServer)ResetOnServer();
            Apply();
        }
        protected override void OnNetworkSessionSynchronized()=>Apply();
        public override void OnNetworkDespawn(){state.OnValueChanged-=Changed;vehicle?.Show(false);}
        void Changed(GuestRegistrationState before,GuestRegistrationState after)=>Apply();
        void Apply()
        {
            if(!State.Active)return;
            character?.ApplyGuestAppearance(State.Appearance,new Color(State.Clothing.x,State.Clothing.y,State.Clothing.z));
            guest?.ApplyIdentity();
        }
        internal void BeginVisitOnServer()
        {
            if(!IsServer||!IsSpawned)return;
            actualCase=RegistrationDesk.Instance.TakeNextCaseOnServer();revealAt=0;Statements.Clear();
            var c=actualCase;
            state.Value=new GuestRegistrationState{Active=true,Visit=guest.State.Visit,Revision=State.Revision+1,
                Appearance=(byte)c.appearance,Vehicle=(byte)c.vehicleKind,Clothing=new Vector3(c.clothing.r,c.clothing.g,c.clothing.b),
                Paint=new Vector3(c.vehicleColor.r,c.vehicleColor.g,c.vehicleColor.b),ActualPlate=new FixedString32Bytes(c.actualPlate),
                Name=new FixedString64Bytes(c.guestName),FirstName=new FixedString32Bytes(c.firstName),LastName=new FixedString32Bytes(c.lastName),DateOfBirth=new FixedString32Bytes(c.dateOfBirth),
                Reserved=c.hasReservation,ReservationKnown=true,BookingFirstName=new FixedString32Bytes(c.bookingFirstName),BookingLastName=new FixedString32Bytes(c.bookingLastName),
                CheckInDate=new FixedString32Bytes(c.checkInDate),RequestedRoom=new FixedString32Bytes(c.requestedRoom),ExpectedGuests=(byte)c.expectedGuests,BookingNote=new FixedString512Bytes(c.bookingNote),
                ParkingSlot=(byte)(GetComponent<GuestVehicleJourney>()?.slot?.number ?? 0)};
        }
        internal void ReleaseParkingOnServer()
        {
            if(!IsServer)return;
            var v=State;v.ParkingSlot=0;v.ParkingKnown=false;v.Revision++;state.Value=v;
        }
        public static float SpeechDuration(GuestStatement line)=>Mathf.Clamp(2+line.Answer.Length*.04f,4,10);
        public bool LatestSpeech(out GuestStatement line)
        {
            line=default;bool found=false;
            foreach(var candidate in Statements)if(!found||candidate.SpokenAt>line.SpokenAt){line=candidate;found=true;}
            return found;
        }
        public bool Speaking=>IsSpawned&&LatestSpeech(out var line)&&NetworkManager.ServerTime.Time<line.SpokenAt+SpeechDuration(line);
        internal void ResetOnServer()
        {
            if(!IsServer||!IsSpawned)return;
            actualCase=null;revealAt=0;Statements.Clear();state.Value=new GuestRegistrationState{Revision=State.Revision+1};
        }
        public bool AskedReport(int index)
        {foreach(var line in Statements)if(line.Question==RegistrationQuestion.FollowUp&&line.Report==index)return true;return false;}
        internal bool AskOnServer(RegistrationQuestion q,int report=-1)
        {
            if(!IsServer||actualCase==null||State.Decision!=RegistrationDecision.Pending||Speaking)return false;
            // Replays update the same replicated line; concurrent clicks cannot overlap a reply.
            for(int i=0;i<Statements.Count;i++)
                if(Statements[i].Question==q&&(q!=RegistrationQuestion.FollowUp||Statements[i].Report==report))
                {var line=Statements[i];line.SpokenAt=NetworkManager.ServerTime.Time;Statements[i]=line;return true;}
            string prompt,answer;
            if(q==RegistrationQuestion.FollowUp)
            {
                var cases=RegistrationDesk.Instance.Cases;
                if(report<0||report>=cases.Length||!cases[report].hasReport||AskedReport(report))return false;
                prompt=cases[report].followUpQuestion;
                answer=cases[report]==actualCase?actualCase.followUpAnswer:"I don't recognize that incident. That isn't my vehicle.";
            }
            else
            {
                if(!actualCase.Supports(q)||Asked(q))return false;
                prompt=actualCase.Question(q);
                answer=q==RegistrationQuestion.ParkingSpace?"I'm in space "+State.ParkingSlot+". The car is parked there.":actualCase.Answer(q);
            }
            var v=State;v.Asked|=1u<<(int)q;v.Revision++;
            if(q==RegistrationQuestion.ParkingSpace)v.ParkingKnown=true;
            Statements.Add(new GuestStatement{Question=q,Prompt=new FixedString128Bytes(prompt),Answer=new FixedString512Bytes(answer),Report=report,SpokenAt=NetworkManager.ServerTime.Time});
            state.Value=v;return true;
        }
        internal void ReportFoundOnServer(GuestCase reportedCase)
        {
            if(!IsServer||actualCase!=reportedCase||State.ReportSeen)return;
            var v=State;v.ReportSeen=true;v.FollowUpPrompt=new FixedString128Bytes(actualCase.followUpQuestion);v.Revision++;state.Value=v;
        }
        internal bool DecideOnServer(bool admit,int room)
        {
            if(!IsServer||actualCase==null||State.Decision!=RegistrationDecision.Pending)return false;
            var v=State;v.Decision=admit?RegistrationDecision.Admitted:RegistrationDecision.Refused;v.AssignedRoom=room;v.Revision++;state.Value=v;return true;
        }
        void Update()
        {
            if(!IsServer||!IsSpawned||!IsMimicOnServer||State.Decision!=RegistrationDecision.Admitted||guest.Phase!=GuestPhase.InRoom||!RegistrationDesk.Instance.Shift.IsRunning)return;
            double now=NetworkManager.ServerTime.Time;
            if(revealAt<=0)revealAt=now+actualCase.disguiseSeconds;
            if(now<revealAt)return;
            RegistrationDesk.Instance.Hunt.QueueGuestOnServer(guest);
        }
        void LateUpdate()
        {
            if(!IsSpawned)return;
            if(GetComponent<GuestVehicleJourney>()==null)vehicle?.Apply(State,State.Active&&guest.Phase!=GuestPhase.Absent&&State.Visit==guest.State.Visit);
        }
    }
}
