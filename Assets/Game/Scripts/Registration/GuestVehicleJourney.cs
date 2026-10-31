using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using MotelGame.Service;

namespace MotelGame.Registration
{
    public enum VehiclePhase : byte { Hidden, Arriving, DriverExiting, Parked, Boarding, Reversing, Leaving }
    public struct VehicleMotion : INetworkSerializable,IEquatable<VehicleMotion>
    {
        public VehiclePhase Phase;
        public float Distance;
        public double ChangedAt, SampledAt;
        public uint Revision;
        public bool Blocked;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T:IReaderWriter
        {s.SerializeValue(ref Phase);s.SerializeValue(ref Distance);s.SerializeValue(ref ChangedAt);s.SerializeValue(ref SampledAt);s.SerializeValue(ref Revision);s.SerializeValue(ref Blocked);}
        public bool Equals(VehicleMotion v)=>Phase==v.Phase&&Distance==v.Distance&&ChangedAt==v.ChangedAt&&SampledAt==v.SampledAt&&Revision==v.Revision&&Blocked==v.Blocked;
    }

    // Kinematic authored travel, on the existing NPC NetworkObject. Only the server advances it.
    // Clients reconstruct the same path from replicated progress; no streamed cameras or driving AI.
    public sealed class GuestVehicleJourney : NetworkBehaviour
    {
        public ParkingSlot slot;
        public GuestVehicleView vehicle;
        [Min(.2f)] public float driveSpeed=3;
        readonly NetworkVariable<VehicleMotion> motion=new(default,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
        GuestLifecycle guest;
        RegisteredGuest record;
        NavMeshObstacle obstacle;
        BoxCollider body;
        GuestVehicleJourney[] journeys;
        ParkingSlot[] spaces;
        float distance,displayDistance,nextPublish,door;
        VehiclePhase renderedPhase;
        Transform[] doors;
        public VehicleMotion State=>motion.Value;
        public bool Moving=>State.Phase==VehiclePhase.Arriving||State.Phase==VehiclePhase.Reversing||State.Phase==VehiclePhase.Leaving;
        public bool OccupiesLane=>Moving||State.Phase==VehiclePhase.DriverExiting;
        public bool Parked=>State.Phase==VehiclePhase.Parked;
        double Now=>NetworkManager.ServerTime.Time;
        void Awake()
        {
            guest=GetComponent<GuestLifecycle>();record=GetComponent<RegisteredGuest>();
            body=vehicle.GetComponent<BoxCollider>();obstacle=vehicle.GetComponent<NavMeshObstacle>();
            doors=vehicle.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="DriverDoorHinge").ToArray();
        }
        public override void OnNetworkSpawn()
        {
            journeys=FindObjectsByType<GuestVehicleJourney>(FindObjectsSortMode.None);
            spaces=FindObjectsByType<ParkingSlot>(FindObjectsSortMode.None).OrderBy(p=>p.number).ToArray();
            if(IsServer)ResetOnServer();Apply(true);
        }
        protected override void OnNetworkSessionSynchronized()=>Apply(true);
        public override void OnNetworkDespawn(){vehicle.Show(false);obstacle.enabled=false;}
        internal void ResetOnServer()
        {
            if(!IsServer||!IsSpawned)return;
            distance=0;motion.Value=new VehicleMotion{Revision=State.Revision+1,ChangedAt=Now,SampledAt=Now};Apply(true);
        }
        internal bool ReserveRandomSlotOnServer()
        {
            if(!IsServer||!IsSpawned||State.Phase!=VehiclePhase.Hidden)return false;
            var free=spaces.Where(p=>p.number>0&&p.number<=255&&p.driverPoint!=null&&p.arrival.Length>2&&p.reverse.Length>2&&p.departure.Length>=2 &&
                !journeys.Any(j=>j!=this&&j.IsSpawned&&j.record.State.ParkingSlot==p.number)).ToArray();
            if(free.Length==0)return false;
            slot=free[UnityEngine.Random.Range(0,free.Length)];return true;
        }
        bool ResolveSlot()
        {
            if(record.State.ParkingSlot==0)return false;
            if(slot==null||slot.number!=record.State.ParkingSlot)slot=Array.Find(spaces,p=>p.number==record.State.ParkingSlot);
            return slot!=null;
        }
        bool LaneFree=>!journeys.Any(j=>j!=this&&j.IsSpawned&&j.OccupiesLane);
        Vector3[] Route=>State.Phase==VehiclePhase.Reversing?slot.reverse:State.Phase==VehiclePhase.Leaving?slot.departure:slot.arrival;
        float Speed=>State.Phase==VehiclePhase.Reversing?1.6f:driveSpeed;
        void Phase(VehiclePhase phase)
        {
            distance=0;motion.Value=new VehicleMotion{Phase=phase,ChangedAt=Now,SampledAt=Now,Revision=State.Revision+1};Apply(true);
        }
        internal void BeginDepartureOnServer()
        {if(IsServer&&Parked)Phase(VehiclePhase.Boarding);}
        void Update()
        {
            if(!IsSpawned)return;
            if(State.Phase!=VehiclePhase.Hidden||guest.Phase==GuestPhase.DrivingToMotel){if(!ResolveSlot())return;}
            if(IsServer)
            {
                if(State.Phase==VehiclePhase.Hidden&&guest.Phase==GuestPhase.DrivingToMotel&&LaneFree)
                {
                    ParkingSlot.Sample(slot.arrival,0,false,out var p,out var q);
                    if(Clear(p,q))Phase(VehiclePhase.Arriving);
                }
                else if(Moving)
                {
                    float next=Mathf.Min(ParkingSlot.Length(Route),distance+Speed*Mathf.Min(Time.deltaTime,.1f));
                    ParkingSlot.Sample(Route,next,State.Phase==VehiclePhase.Reversing,out var p,out var q);
                    bool blocked=!Clear(p,q);if(!blocked)distance=next;
                    if(Time.unscaledTime>=nextPublish||blocked!=State.Blocked)
                    {nextPublish=Time.unscaledTime+.08f;var v=State;v.Distance=distance;v.SampledAt=Now;v.Blocked=blocked;motion.Value=v;}
                    if(!blocked&&distance>=ParkingSlot.Length(Route)-.001f)
                    {
                        if(State.Phase==VehiclePhase.Arriving)Phase(VehiclePhase.DriverExiting);
                        else if(State.Phase==VehiclePhase.Reversing)Phase(VehiclePhase.Leaving);
                        else{Phase(VehiclePhase.Hidden);record.ReleaseParkingOnServer();guest.CompleteVehicleDepartureOnServer();}
                    }
                }
                else if(State.Phase==VehiclePhase.DriverExiting&&Now-State.ChangedAt>=1.3&&ExitClear())
                {if(guest.CompleteCarArrivalOnServer())Phase(VehiclePhase.Parked);}
                else if(State.Phase==VehiclePhase.Boarding)
                {
                    if(guest.Phase==GuestPhase.Dead)Phase(VehiclePhase.Parked);
                    else
                    {
                        if(Now-State.ChangedAt>=.9)guest.HideForCarDepartureOnServer();
                        if(Now-State.ChangedAt>=2&&LaneFree)Phase(VehiclePhase.Reversing);
                    }
                }
            }
            Apply(false);
        }
        bool ExitClear()
        {
            Vector3 p=slot.driverPoint.position;
            return !Physics.OverlapCapsule(p+Vector3.up*.36f,p+Vector3.up*1.45f,.31f,~0,QueryTriggerInteraction.Ignore)
                .Any(c=>!c.transform.IsChildOf(guest.transform)&&!c.transform.IsChildOf(vehicle.transform)) &&
                !NetworkManager.ConnectedClientsList.Any(c=>c.PlayerObject!=null&&(c.PlayerObject.transform.position-p).sqrMagnitude<.75f*.75f);
        }
        bool Clear(Vector3 position,Quaternion rotation)
        {
            Vector3 center=position+rotation*body.center,half=body.size*.5f;
            foreach(var collider in Physics.OverlapBox(center,half-Vector3.one*.015f,rotation,~0,QueryTriggerInteraction.Ignore))
                if(!collider.transform.IsChildOf(vehicle.transform)&&!collider.transform.IsChildOf(guest.transform))return false;
            foreach(var client in NetworkManager.ConnectedClientsList)
            {
                if(client.PlayerObject==null)continue;
                var local=Quaternion.Inverse(rotation)*(client.PlayerObject.transform.position+Vector3.up*.9f-center);
                if(Mathf.Abs(local.x)<half.x+.4f&&Mathf.Abs(local.z)<half.z+.4f&&Mathf.Abs(local.y)<half.y+.9f)return false;
            }
            return true;
        }
        void Apply(bool snap)
        {
            if(!IsSpawned)return;
            vehicle.Apply(record.State,State.Phase!=VehiclePhase.Hidden);
            if(State.Phase==VehiclePhase.Hidden){obstacle.enabled=false;door=0;foreach(var hinge in doors)hinge.localRotation=Quaternion.identity;return;}
            if(!ResolveSlot()){vehicle.Show(false);obstacle.enabled=false;return;}
            bool stationary=!Moving;
            obstacle.enabled=stationary;obstacle.carving=stationary;
            Vector3 position;Quaternion rotation;
            if(stationary){position=slot.arrival[slot.arrival.Length-1];rotation=Quaternion.identity;}
            else
            {
                float target=IsServer?distance:State.Distance+(State.Blocked?0:(float)Math.Clamp(Now-State.SampledAt,0,.08)*Speed);
                if(snap||renderedPhase!=State.Phase)displayDistance=target;else displayDistance=Mathf.MoveTowards(displayDistance,target,Time.deltaTime*Speed*1.5f);
                ParkingSlot.Sample(Route,displayDistance,State.Phase==VehiclePhase.Reversing,out position,out rotation);
            }
            vehicle.transform.SetPositionAndRotation(position,rotation);renderedPhase=State.Phase;
            bool open=State.Phase==VehiclePhase.DriverExiting || State.Phase==VehiclePhase.Boarding&&Now-State.ChangedAt<1.1;
            door=snap?(open?1:0):Mathf.MoveTowards(door,open?1:0,Time.deltaTime*2.5f);
            foreach(var hinge in doors)hinge.localRotation=Quaternion.Euler(0,70*door,0);
        }
        public bool ConfigurationValid()=>slot!=null&&slot.driverPoint!=null&&slot.arrival.Length>2&&slot.reverse.Length>2&&slot.departure.Length>=2&&vehicle!=null&&driveSpeed>0;
    }
}
