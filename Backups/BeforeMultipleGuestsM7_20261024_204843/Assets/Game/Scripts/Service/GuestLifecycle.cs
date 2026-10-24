using System;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;
using MotelGame.Interactions;

namespace MotelGame.Service
{
    public enum GuestPhase : byte { Absent, Arriving, WaitingForCheckIn, WalkingToRoom, InRoom, Leaving }
    public struct GuestSnapshot : INetworkSerializable, IEquatable<GuestSnapshot>
    {
        public GuestPhase Phase;
        public uint Visit;
        public uint RoomCycle;
        public bool Assigned;
        public bool Blocked;
        public NetworkObjectReference Room;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Phase); s.SerializeValue(ref Visit); s.SerializeValue(ref RoomCycle);
            s.SerializeValue(ref Assigned); s.SerializeValue(ref Blocked); s.SerializeValue(ref Room);
        }
        public bool Equals(GuestSnapshot other) => Phase==other.Phase && Visit==other.Visit && RoomCycle==other.RoomCycle &&
            Assigned==other.Assigned && Blocked==other.Blocked && Room.Equals(other.Room);
    }

    [RequireComponent(typeof(NetworkObject), typeof(NetworkTransform), typeof(NavMeshAgent))]
    public sealed class GuestLifecycle : NetworkBehaviour
    {
        [SerializeField] ServiceRoom room;
        [SerializeField] NetworkDoor door;
        [SerializeField] Transform arrivalPoint;
        [SerializeField] Transform receptionPoint;
        [SerializeField] Transform doorOutside;
        [SerializeField] Transform doorInside;
        [SerializeField] Transform roomIdlePoint;
        [SerializeField] Transform departurePoint;
        [SerializeField] GameObject visual;
        [SerializeField] CapsuleCollider body;
        [SerializeField, Min(0)] float arrivalDelay = 3;
        [SerializeField, Min(1)] float nextGuestDelay = 6;
        readonly NetworkVariable<GuestSnapshot> snapshot = new NetworkVariable<GuestSnapshot>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        NavMeshAgent agent;
        NetworkTransform networkTransform;
        // Door crossings are explicit route stages, not a separate door state machine.
        enum RouteStep { None, Reception, Outside, Enter, Idle, Inside, Exit, Depart }
        RouteStep step;
        Transform destination;
        double nextArrival, nextPathAttempt;
        Vector3 lastPosition, progressPosition;
        float lastProgress;
        bool hasPassage, warned;

        public GuestSnapshot State => snapshot.Value;
        public GuestPhase Phase => State.Phase;
        public ServiceRoom Room => room;
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
            target==room && State.RoomCycle==target.State.Cycle && target.State.Status==RoomStatus.Occupied;
        public string StatusText => State.Blocked ? "Guest route blocked — clear the way" : Phase switch
        {
            GuestPhase.Absent => "Next guest arriving soon",
            GuestPhase.Arriving => "Guest approaching Reception",
            GuestPhase.WaitingForCheckIn => "Guest waiting — select a Ready room",
            GuestPhase.WalkingToRoom => $"Guest walking to Room {room.RoomNumber}",
            GuestPhase.InRoom => $"Guest in Room {room.RoomNumber}",
            _ => "Guest leaving the motel"
        };
        void Awake()
        {
            agent = GetComponent<NavMeshAgent>(); networkTransform = GetComponent<NetworkTransform>();
            agent.enabled = false; visual.SetActive(false); body.enabled = false;
        }
        public override void OnNetworkSpawn()
        {
            snapshot.OnValueChanged += Changed;
            room.StateChanged += RoomChanged;
            if (IsServer)
            {
                snapshot.Value = default;
                nextArrival = NetworkManager.ServerTime.Time + arrivalDelay;
                step = RouteStep.None; destination = null; hasPassage = false;
            }
            ApplyPresentation();
        }
        public override void OnNetworkDespawn()
        {
            snapshot.OnValueChanged -= Changed; room.StateChanged -= RoomChanged;
            if (IsServer && hasPassage && door != null && door.IsSpawned) door.EndGuestPassage(body);
            agent.enabled = false; visual.SetActive(false); body.enabled = false;
        }
        void Changed(GuestSnapshot before, GuestSnapshot after) => ApplyPresentation();
        void ApplyPresentation()
        {
            bool present = IsSpawned && Phase != GuestPhase.Absent;
            visual.SetActive(present); body.enabled = present;
            // Only the server simulates navigation. NGO's server-authoritative NetworkTransform
            // drives clients, including the initial pose on late join.
            if (!IsServer) agent.enabled = false;
        }
        internal bool TryCheckIn(ServiceRoom target, uint cycle)
        {
            if (!IsServer || !CanCheckIn || target!=room || target.State.Cycle!=cycle || target.State.Status!=RoomStatus.Ready) return false;
            if (!target.TryCheckIn(cycle)) return false;
            var value = State; value.Phase=GuestPhase.WalkingToRoom; value.Assigned=true;
            value.Room = new NetworkObjectReference(target.NetworkObject); value.RoomCycle=cycle; value.Blocked=false; snapshot.Value=value;
            SetRoute(RouteStep.Outside,doorOutside);
            return true;
        }
        void RoomChanged(RoomSnapshot before, RoomSnapshot after)
        {
            if (!IsServer || !IsSpawned || !State.Assigned || Phase==GuestPhase.Leaving || Phase==GuestPhase.Absent) return;
            if (after.Status!=RoomStatus.Occupied || after.Cycle!=State.RoomCycle) BeginLeaving();
        }
        void BeginLeaving()
        {
            SetPhase(GuestPhase.Leaving);
            // Checkout can happen halfway across the threshold. Keep the passage reserved
            // until the NPC is safely outside, instead of turning through a closing leaf.
            if (hasPassage) SetRoute(RouteStep.Exit,doorOutside);
            else
            {
                // The lobby also lies behind the door's infinite plane. Use the completed
                // route stage to decide which side of the actual doorway we reached.
                bool inside = step == RouteStep.Idle || step == RouteStep.None;
                SetRoute(inside ? RouteStep.Inside : RouteStep.Depart, inside ? doorInside : departurePoint);
            }
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
            if (Phase==GuestPhase.Absent)
            {
                if (NetworkManager.ServerTime.Time>=nextArrival) Arrive();
                return;
            }
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
        void Arrive()
        {
            if (!NavMesh.SamplePosition(arrivalPoint.position,out var hit,.35f,NavMesh.AllAreas))
            { ReportBlocked("Arrival point is off NavMesh."); nextArrival=NetworkManager.ServerTime.Time+2; return; }
            // This reposition is only a new, hidden arrival after departure; routes never Warp.
            networkTransform.Teleport(hit.position,arrivalPoint.rotation,Vector3.one);
            agent.enabled=true;
            if (!agent.Warp(hit.position)) { agent.enabled=false; ReportBlocked("Cannot place arriving guest."); nextArrival=NetworkManager.ServerTime.Time+2; return; }
            var value=new GuestSnapshot { Phase=GuestPhase.Arriving,Visit=State.Visit+1 }; snapshot.Value=value;
            SetRoute(RouteStep.Reception,receptionPoint);
        }
        void Reached()
        {
            switch (step)
            {
                case RouteStep.Reception:
                    SetPhase(GuestPhase.WaitingForCheckIn); StopAt(receptionPoint); break;
                case RouteStep.Outside:
                    if (!OpenPassage()) return;
                    SetRoute(RouteStep.Enter,doorInside); break;
                case RouteStep.Enter:
                    ReleasePassage(); SetRoute(RouteStep.Idle,roomIdlePoint); break;
                case RouteStep.Idle:
                    SetPhase(GuestPhase.InRoom); StopAt(roomIdlePoint); break;
                case RouteStep.Inside:
                    if (!OpenPassage()) return;
                    SetRoute(RouteStep.Exit,doorOutside); break;
                case RouteStep.Exit:
                    ReleasePassage(); SetRoute(RouteStep.Depart,departurePoint); break;
                case RouteStep.Depart:
                    ReleasePassage(); SetPhase(GuestPhase.Absent); agent.enabled=false;
                    step=RouteStep.None; destination=null; nextArrival=NetworkManager.ServerTime.Time+nextGuestDelay; break;
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
                if (!hit.collider.transform.IsChildOf(transform)) return true;
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
        public bool ConfigurationValid() => room!=null && door!=null && arrivalPoint!=null && receptionPoint!=null &&
            doorOutside!=null && doorInside!=null && roomIdlePoint!=null && departurePoint!=null && visual!=null && body!=null;
    }
}
