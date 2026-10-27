using System;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;
using MotelGame.Interactions;

namespace MotelGame.Service
{
    public enum GuestPhase : byte { Absent, Arriving, WaitingForCheckIn, WalkingToRoom, InRoom, Leaving, ReturningForCheckout, WaitingForCheckout, Relocating, Evacuating, Evacuated, Dead }
    public struct GuestSnapshot : INetworkSerializable, IEquatable<GuestSnapshot>
    {
        public GuestPhase Phase;
        public uint Visit;
        public byte QueuePosition;
        public uint RoomCycle;
        public double StayEndsAt;
        public bool Assigned;
        public bool Blocked;
        public NetworkObjectReference Room;
        public NetworkObjectReference RelocationTarget;
        public bool RelocationRequestUsed;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref RelocationTarget); s.SerializeValue(ref RelocationRequestUsed); s.SerializeValue(ref QueuePosition); s.SerializeValue(ref Phase); s.SerializeValue(ref Visit); s.SerializeValue(ref RoomCycle);
            s.SerializeValue(ref StayEndsAt); s.SerializeValue(ref Assigned); s.SerializeValue(ref Blocked); s.SerializeValue(ref Room);
        }
        public bool Equals(GuestSnapshot other) => RelocationTarget.Equals(other.RelocationTarget) && RelocationRequestUsed==other.RelocationRequestUsed && QueuePosition==other.QueuePosition && Phase==other.Phase && Visit==other.Visit && RoomCycle==other.RoomCycle &&
            StayEndsAt==other.StayEndsAt && Assigned==other.Assigned && Blocked==other.Blocked && Room.Equals(other.Room);
    }

    [RequireComponent(typeof(NetworkObject), typeof(NetworkTransform), typeof(NavMeshAgent))]
    public sealed class GuestLifecycle : NetworkBehaviour
    {
        ServiceRoom assignedRoom;
        ServiceRoom room => Room;
        NetworkDoor door => room == null ? null : room.Door;
        Transform doorOutside => room.DoorOutside;
        Transform doorInside => room.DoorInside;
        Transform roomIdlePoint => room.GuestIdle;
        [SerializeField, Min(1), Tooltip("Real seconds in the room before requesting Reception checkout.")] float stayDuration = 120;
        [SerializeField] Transform arrivalPoint;
        [SerializeField] Transform receptionPoint;
        [SerializeField] Transform departurePoint;
        [SerializeField] GameObject visual;
        [SerializeField] CapsuleCollider body;
        [SerializeField] string guestName = "Guest A";
        [SerializeField] Color shirtColor = new Color(.2f,.4f,.5f);
        [SerializeField] Renderer shirt;
        [SerializeField, Min(0)] int shirtMaterialIndex;
        [SerializeField] TextMesh nameLabel;
        readonly NetworkVariable<GuestSnapshot> snapshot = new NetworkVariable<GuestSnapshot>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        NavMeshAgent agent;
        NetworkTransform networkTransform;
        // Door crossings are explicit route stages, not a separate door state machine.
        enum RouteStep { None, Reception, Outside, Enter, Idle, Inside, Exit, Depart, Safety }
        [SerializeField] Transform evacuationPoint;
        Vector3 visualPosition; Quaternion visualRotation;
        public Transform EvacuationPoint => evacuationPoint;
        public bool Alive => Phase != GuestPhase.Absent && Phase != GuestPhase.Dead;
        RouteStep step;
        Transform destination;
        Transform queueTarget;
        public bool AtQueuePosition => step==RouteStep.None && (Phase==GuestPhase.WaitingForCheckIn||Phase==GuestPhase.WaitingForCheckout);
        double nextPathAttempt, baggagePauseUntil;
        Vector3 lastPosition, progressPosition;
        float lastProgress;
        bool hasPassage, warned;

        public float StayDuration => stayDuration;
        public bool CheckoutDue => IsSpawned && Phase==GuestPhase.InRoom && State.StayEndsAt>0 && NetworkManager.ServerTime.Time>=State.StayEndsAt;
        public bool CanCheckoutFor(ServiceRoom target) => IsSpawned && Phase==GuestPhase.WaitingForCheckout && target!=null &&
            target==room && target.State.Status==RoomStatus.Occupied && target.State.Cycle==State.RoomCycle && target.Occupant==this;
        public GuestSnapshot State => snapshot.Value;
        public GuestPhase Phase => State.Phase;
        public ServiceRoom Room => IsSpawned && State.Assigned && State.Room.TryGet(out var obj,NetworkManager)
            ? obj.GetComponent<ServiceRoom>() : null;
        public ServiceRoom RelocationDestination => IsSpawned && Phase == GuestPhase.Relocating && State.RelocationTarget.TryGet(out var obj, NetworkManager) ? obj.GetComponent<ServiceRoom>() : null;
        public string GuestName => guestName;
        public NavMeshAgent Agent => agent;
        public CapsuleCollider Body => body;
        public bool Visible => visual.activeSelf;
        public Transform ArrivalPoint => arrivalPoint;
        public Transform ReceptionPoint => receptionPoint;
        public Transform DoorOutside => doorOutside;
        public Transform DoorInside => doorInside;
        public Transform RoomIdlePoint => roomIdlePoint;
        public Transform DeparturePoint => departurePoint;
        public bool CanCheckIn => IsSpawned && Phase == GuestPhase.WaitingForCheckIn && !State.Assigned;
        public bool IsInRoomFor(ServiceRoom target) => IsSpawned && Phase==GuestPhase.InRoom && State.Assigned &&
            target!=null && target==room && State.RoomCycle==target.State.Cycle && target.Occupant==this;
        public string StatusText => State.Blocked ? "Guest route blocked — clear the way" : Phase switch
        {
            GuestPhase.Dead => "Guest deceased",
            GuestPhase.Evacuating => "Guest evacuating to lobby",
            GuestPhase.Evacuated => "Guest evacuated — waiting in lobby",
            GuestPhase.Absent => "Next guest arriving soon",
            GuestPhase.Arriving => "Guest approaching Reception",
            GuestPhase.WaitingForCheckIn => "Guest waiting — select a Ready room",
            GuestPhase.WalkingToRoom => $"Guest walking to Room {room?.RoomNumber}",
            GuestPhase.Relocating => $"Guest relocating to Room {RelocationDestination?.RoomNumber}",
            GuestPhase.InRoom => $"Guest in Room {room?.RoomNumber}",
            GuestPhase.ReturningForCheckout => $"Room {room?.RoomNumber} — returning for checkout",
            GuestPhase.WaitingForCheckout => $"Room {room?.RoomNumber} — Ready to check out",
            _ => "Guest leaving the motel"
        };
        void Awake()
        {
            visualPosition=visual.transform.localPosition; visualRotation=visual.transform.localRotation;
            agent = GetComponent<NavMeshAgent>(); networkTransform = GetComponent<NetworkTransform>();
            agent.enabled = false; visual.SetActive(false); body.enabled = false; ApplyIdentity();
        }
        public override void OnNetworkSpawn()
        {
            snapshot.OnValueChanged += Changed;
            if (IsServer)
            {
                snapshot.Value = default;
                step = RouteStep.None; destination = null; hasPassage = false;
            }
            ApplyPresentation(); ApplyIdentity();
        }
        // Room may be spawned after this guest during late-join synchronization.
        protected override void OnNetworkSessionSynchronized() => ApplyIdentity();
        public override void OnNetworkDespawn()
        {
            snapshot.OnValueChanged -= Changed;
            if (assignedRoom != null) assignedRoom.StateChanged -= RoomChanged;
            if (IsServer && hasPassage && door != null && door.IsSpawned) door.EndGuestPassage(body);
            agent.enabled = false; visual.SetActive(false); body.enabled = false;
        }
        void Changed(GuestSnapshot before, GuestSnapshot after) { ApplyPresentation(); ApplyIdentity(); }
        public void ApplyIdentity()
        {
            if (shirt != null)
            {
                var color = new MaterialPropertyBlock(); color.SetColor("_BaseColor",shirtColor); color.SetColor("_Color",shirtColor);
                shirt.SetPropertyBlock(color, shirtMaterialIndex);
            }
            if (nameLabel != null) nameLabel.text = guestName + (State.Visit > 0 ? " · " + State.Visit : "") +
                (Phase==GuestPhase.Dead ? "\nDECEASED" : Phase==GuestPhase.Evacuated ? "\nEVACUATED" : Phase==GuestPhase.WaitingForCheckout ? $"\nRoom {room?.RoomNumber} — Ready to check out" : "");
        }
        void ApplyPresentation()
        {
            bool present = IsSpawned && Phase != GuestPhase.Absent;
            visual.SetActive(present); body.enabled = present && Phase != GuestPhase.Dead;
            visual.transform.localRotation = Phase == GuestPhase.Dead ? Quaternion.Euler(0,0,88) : visualRotation;
            visual.transform.localPosition = Phase == GuestPhase.Dead ? visualPosition + Vector3.up*.12f : visualPosition;
            var character=visual.GetComponentInChildren<MotelGame.Characters.CharacterVisuals>(true);
            if(character!=null && character.Animator!=null) character.Animator.enabled=Phase!=GuestPhase.Dead;
            // Only the server simulates navigation. NGO's server-authoritative NetworkTransform
            // drives clients, including the initial pose on late join.
            if (!IsServer) agent.enabled = false;
        }
        internal bool TryCheckIn(ServiceRoom target, uint cycle)
        {
            if (!IsServer || !CanCheckIn || target==null || target.State.Cycle!=cycle || !target.AvailableForCheckIn) return false;
            if (!target.TryCheckIn(cycle,this)) return false;
            assignedRoom=target; assignedRoom.StateChanged += RoomChanged;
            var value = State; value.Phase=GuestPhase.WalkingToRoom; value.Assigned=true;
            value.Room = new NetworkObjectReference(target.NetworkObject); value.RoomCycle=cycle; value.QueuePosition=0; value.Blocked=false; snapshot.Value=value;
            SetRoute(RouteStep.Outside,doorOutside);
            return true;
        }
        // Reservation, source departure and destination arrival are serialized on the server.
        // During exit Room remains the source so the existing door route uses the right door.
        internal bool TryRelocate(ServiceRoom target, uint sourceCycle, uint destinationCycle, uint faultId)
        {
            var source = room;
            if (!IsServer || source == null || !source.CanRelocate || !IsInRoomFor(source) || source.State.Cycle != sourceCycle ||
                source.State.FaultId != faultId || target == source || !target.TryReserve(this, destinationCycle)) return false;
            var v = State; v.Phase = GuestPhase.Relocating; v.Blocked = false;
            v.RelocationTarget = new NetworkObjectReference(target.NetworkObject); v.RelocationRequestUsed = source.State.TowelRequested;
            snapshot.Value = v;
            SetRoute(RouteStep.Inside, doorInside);
            return true;
        }
        void ContinueRelocation()
        {
            var target = RelocationDestination;
            if (target == null || !target.State.Reserved || !target.State.Reservation.Equals(new NetworkObjectReference(NetworkObject)))
            { ReportBlocked("Relocation reservation missing."); return; }
            var source = assignedRoom;
            source.StateChanged -= RoomChanged;
            // Unsubscribe before vacating: ordinary checkout listeners must not route this guest to the parking lot.
            source.VacateForRelocation(this);
            assignedRoom = target; assignedRoom.StateChanged += RoomChanged;
            var v = State; v.Room = new NetworkObjectReference(target.NetworkObject); v.RoomCycle = target.State.Cycle; snapshot.Value = v;
            SetRoute(RouteStep.Outside, doorOutside);
        }
        void RoomChanged(RoomSnapshot before, RoomSnapshot after)
        {
            if (!IsServer || !IsSpawned || !State.Assigned || Phase==GuestPhase.Leaving || Phase==GuestPhase.Absent || Phase==GuestPhase.Relocating || Phase==GuestPhase.Evacuating || Phase==GuestPhase.Evacuated || Phase==GuestPhase.Dead) return;
            if (after.Status!=RoomStatus.Occupied || after.Cycle!=State.RoomCycle) BeginLeaving();
        }
        // Reception reserves its single service position before allowing a guest to return.
        internal bool TryBeginCheckoutReturn()
        {
            if (!IsServer || !CheckoutDue || !IsInRoomFor(room)) return false;
            SetPhase(GuestPhase.ReturningForCheckout);
            SetRoute(RouteStep.Inside,doorInside);
            return true;
        }
        internal void PauseForBaggage(float seconds)
        { if(IsServer){ baggagePauseUntil=NetworkManager.ServerTime.Time+seconds; if(agent.enabled&&agent.isOnNavMesh)agent.isStopped=true; } }
        internal void BeginLeaving()
        {
            // Room state can change to Dirty only through validated checkout at Reception.
            var leaving=State;leaving.Phase=GuestPhase.Leaving;leaving.QueuePosition=0;leaving.Blocked=false;snapshot.Value=leaving;
            SetRoute(RouteStep.Depart,departurePoint);
        }
        internal void SetQueuePosition(Transform point,byte position)
        {
            if(!IsServer)return;
            queueTarget=point;var v=State;v.QueuePosition=position;snapshot.Value=v;
            if(point!=null&&(step==RouteStep.Reception||Phase==GuestPhase.WaitingForCheckIn||Phase==GuestPhase.WaitingForCheckout))
            {SetPhase(State.Assigned?GuestPhase.ReturningForCheckout:GuestPhase.Arriving);SetRoute(RouteStep.Reception,point);}
        }
        internal void ExtendDebugStay(double until) { if(IsServer&&Phase==GuestPhase.InRoom){var v=State;v.StayEndsAt=until;snapshot.Value=v;} }
        internal bool EvacuateOnServer()
        {
            if(!IsServer || !IsSpawned || Phase!=GuestPhase.InRoom || evacuationPoint==null) return false;
            SetPhase(GuestPhase.Evacuating); SetRoute(RouteStep.Inside,doorInside);
            FindFirstObjectByType<GuestServiceRequests>()?.ValidateSource(); return true;
        }
        void VacateEmergencyRoom()
        {
            if(assignedRoom!=null) { assignedRoom.StateChanged-=RoomChanged; assignedRoom.VacateForRelocation(this); }
            assignedRoom=null;
            var v=State; v.Assigned=false; v.Room=default; v.QueuePosition=0; v.StayEndsAt=0; snapshot.Value=v;
            FindFirstObjectByType<GuestServiceRequests>()?.ValidateSource();
        }
        internal bool KillOnServer()
        {
            if(!IsServer || !IsSpawned || !Alive) return false;
            StopOnServer(); RelocationDestination?.CancelReservation(this); SetPhase(GuestPhase.Dead);
            VacateEmergencyRoom(); agent.enabled=false;
            FindFirstObjectByType<NightShiftController>()?.RecordGuestDeath(); return true;
        }
        internal void StopOnServer()
        {
            if(!IsServer)return;
            ReleasePassage();step=RouteStep.None;destination=null;
            if(agent.enabled&&agent.isOnNavMesh){agent.isStopped=true;agent.ResetPath();}
        }
        internal void ResetOnServer()
        {
            if(!IsServer)return;StopOnServer();baggagePauseUntil=0;GetComponent<MotelGame.Inspection.GuestBaggage>()?.ResetOnServer();
            RelocationDestination?.CancelReservation(this);
            if(assignedRoom!=null)assignedRoom.StateChanged-=RoomChanged;
            assignedRoom=null;queueTarget=null;agent.enabled=false;
            snapshot.Value=new GuestSnapshot{Visit=State.Visit+1};
        }
        void SetPhase(GuestPhase phase)
        { var value=State; value.Phase=phase; value.Blocked=false; snapshot.Value=value; }
        void SetBlocked(bool value)
        { if (State.Blocked==value) return; var next=State; next.Blocked=value; snapshot.Value=next; }
        void SetRoute(RouteStep route, Transform target)
        {
            step=route; destination=target; nextPathAttempt=0; lastProgress=Time.unscaledTime;
            warned=false; lastPosition=transform.position; progressPosition=transform.position;
            if (agent.enabled && agent.isOnNavMesh) { agent.isStopped=false; agent.ResetPath(); }
        }
        void Update()
        {
            if (!IsSpawned || !IsServer) return;
            if (Phase==GuestPhase.Absent) return;
            if(NetworkManager.ServerTime.Time<baggagePauseUntil){if(agent.enabled&&agent.isOnNavMesh)agent.isStopped=true;return;}
            if (step==RouteStep.None) return;
            if (!agent.enabled || !agent.isOnNavMesh) { ReportBlocked("NPC is not on its configured NavMesh."); return; }
            // A NavMesh path alone does not collide with a moving door or remote players.
            // Sweep actual travel and pause for obstructions; never teleport through them.
            Vector3 travel=transform.position-lastPosition; travel.y=0;
            if (travel.sqrMagnitude>0 && Obstructed(lastPosition,travel))
            {
                transform.position=lastPosition; agent.nextPosition=lastPosition; agent.isStopped=true;
            }
            else if (Vector3.Distance(transform.position,progressPosition)>.03f)
            { progressPosition=transform.position; lastProgress=Time.unscaledTime; warned=false; SetBlocked(false); }
            lastPosition=transform.position;
            if (agent.isStopped)
            {
                Vector3 ahead=agent.steeringTarget-transform.position; ahead.y=0;
                if (Obstructed(transform.position,Vector3.ClampMagnitude(ahead,.35f)))
                { ReportBlocked("Guest waiting for an obstruction to clear."); return; }
                agent.isStopped=false; lastProgress=Time.unscaledTime; SetBlocked(false);
            }
            if (!agent.pathPending && (!agent.hasPath || agent.pathStatus!=NavMeshPathStatus.PathComplete))
            {
                if (Vector3.Distance(transform.position,destination.position)>.18f && NetworkManager.ServerTime.Time>=nextPathAttempt)
                {
                    nextPathAttempt=NetworkManager.ServerTime.Time+1;
                    var path=new NavMeshPath();
                    if (!agent.CalculatePath(destination.position,path) || path.status!=NavMeshPathStatus.PathComplete)
                    { ReportBlocked("No complete path to "+destination.name); return; }
                    agent.SetPath(path);
                }
            }
            if (!agent.pathPending && Vector3.Distance(transform.position,destination.position)<.18f)
            {
                agent.isStopped=true; agent.ResetPath(); Reached(); return;
            }
            if (Time.unscaledTime-lastProgress>8) ReportBlocked("Guest cannot make progress toward "+destination.name);
        }
        // Reception is the sole arrival scheduler. Fixed, configured instances bound the pool.
        internal bool StartArrival()
        {
            if (!IsServer || !IsSpawned || Phase!=GuestPhase.Absent) return false;
            if (assignedRoom != null) assignedRoom.StateChanged -= RoomChanged;
            assignedRoom=null;
            foreach (var other in FindObjectsByType<GuestLifecycle>(FindObjectsSortMode.None))
                if (other!=this && other.Visible && Vector3.Distance(other.transform.position,arrivalPoint.position)<1.2f) return false;
            if (!NavMesh.SamplePosition(arrivalPoint.position,out var hit,.35f,NavMesh.AllAreas))
            { ReportBlocked("Arrival point is off NavMesh."); return false; }
            // This reposition is only a new, hidden arrival after departure; routes never Warp.
            networkTransform.Teleport(hit.position,arrivalPoint.rotation,Vector3.one);
            agent.enabled=true;
            if (!agent.Warp(hit.position)) { agent.enabled=false; ReportBlocked("Cannot place arriving guest."); return false; }
            var value=new GuestSnapshot { Phase=GuestPhase.Arriving,Visit=State.Visit+1, QueuePosition=State.QueuePosition }; snapshot.Value=value;
            GetComponent<MotelGame.Inspection.GuestBaggage>()?.BeginVisitOnServer();
            SetRoute(RouteStep.Reception,queueTarget==null?receptionPoint:queueTarget); return true;
        }
        void Reached()
        {
            switch (step)
            {
                case RouteStep.Reception:
                    SetPhase(State.Assigned ? GuestPhase.WaitingForCheckout : GuestPhase.WaitingForCheckIn);
                    StopAt(queueTarget==null?receptionPoint:queueTarget); break;
                case RouteStep.Outside:
                    if (!OpenPassage()) return;
                    SetRoute(RouteStep.Enter,doorInside); break;
                case RouteStep.Enter:
                    ReleasePassage(); SetRoute(RouteStep.Idle,roomIdlePoint); break;
                case RouteStep.Idle:
                    // Deadline is replicated for observation, but only the server/coordinator uses it.
                    var arrived=State; arrived.Phase=GuestPhase.InRoom; arrived.Blocked=false;
                    if (Phase == GuestPhase.Relocating)
                    {
                        if (!room.CompleteRelocation(this, State.RoomCycle, State.RelocationRequestUsed)) { ReportBlocked("Cannot complete relocation."); return; }
                        arrived.RelocationTarget = default; arrived.RelocationRequestUsed = false;
                        // Keep original StayEndsAt, including if it elapsed while walking.
                    }
                    else arrived.StayEndsAt=NetworkManager.ServerTime.Time+Mathf.Max(1,stayDuration);
                    snapshot.Value=arrived; StopAt(roomIdlePoint); break;
                case RouteStep.Inside:
                    if (!OpenPassage()) return;
                    SetRoute(RouteStep.Exit,doorOutside); break;
                case RouteStep.Exit:
                    ReleasePassage();
                    if (Phase == GuestPhase.Evacuating) { VacateEmergencyRoom(); SetRoute(RouteStep.Safety,evacuationPoint); }
                    else if (Phase == GuestPhase.Relocating) ContinueRelocation();
                    else SetRoute(RouteStep.Reception,queueTarget==null?receptionPoint:queueTarget); break;
                case RouteStep.Safety:
                    SetPhase(GuestPhase.Evacuated); StopAt(evacuationPoint);
                    FindFirstObjectByType<NightShiftController>()?.RecordEvacuation(); break;
                case RouteStep.Depart:
                    ReleasePassage(); SetPhase(GuestPhase.Absent); agent.enabled=false;
                    step=RouteStep.None; destination=null;
                    if(assignedRoom!=null)assignedRoom.StateChanged-=RoomChanged; assignedRoom=null; break;
            }
        }
        bool OpenPassage()
        {
            hasPassage=door.TryBeginGuestPassage(body);
            if (!hasPassage || !door.IsOpen || door.IsMoving)
            { if (Time.unscaledTime-lastProgress>8) ReportBlocked("Room door is obstructed."); return false; }
            return true;
        }
        void ReleasePassage()
        { if (hasPassage) door.EndGuestPassage(body); hasPassage=false; }
        void StopAt(Transform point)
        { step=RouteStep.None; destination=null; transform.rotation=point.rotation; agent.isStopped=true; }
        void ReportBlocked(string reason)
        {
            SetBlocked(true);
            if (warned) return;
            warned=true; Debug.LogWarning("Guest navigation: "+reason,this);
        }
        bool Obstructed(Vector3 origin, Vector3 travel)
        {
            float distance=travel.magnitude; if (distance<=Mathf.Epsilon) return false;
            Vector3 direction=travel/distance;
            Vector3 low=origin+Vector3.up*(body.radius+.06f), high=origin+Vector3.up*(body.height-body.radius);
            foreach (var hit in Physics.CapsuleCastAll(low,high,body.radius,direction,distance+.025f,~0,QueryTriggerInteraction.Ignore))
                // NavMeshAgent local avoidance handles other agents. Stopping both on a
                // capsule sweep would prevent them from steering around each other.
                if (!hit.collider.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<GuestLifecycle>()==null) return true;
            // Remote player CharacterControllers are disabled on the host. Check their replicated
            // centers explicitly as the existing NetworkDoor does.
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                var player=client.PlayerObject; if (player==null) continue;
                Vector3 to=player.transform.position-origin; if (Mathf.Abs(to.y)>body.height) continue; to.y=0;
                float along=Mathf.Clamp(Vector3.Dot(to,direction),0,distance+.1f);
                if ((to-direction*along).sqrMagnitude<.6f*.6f) return true;
            }
            return false;
        }
        public bool ConfigurationValid() => stayDuration>=1 && !float.IsInfinity(stayDuration) && arrivalPoint!=null && receptionPoint!=null && departurePoint!=null && visual!=null && body!=null && shirt!=null && nameLabel!=null;
    }
}
