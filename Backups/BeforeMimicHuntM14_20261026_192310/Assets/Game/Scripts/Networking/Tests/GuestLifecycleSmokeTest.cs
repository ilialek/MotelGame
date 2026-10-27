#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using MotelGame.Interactions;
using MotelGame.Service;

namespace MotelGame.Networking.Tests
{
    // Explicit development integration test. Barriers coordinate steps only; all gameplay
    // changes use the same interaction/RPC paths as normal play and observations use NGO.
    [DefaultExecutionOrder(100)]
    public sealed class GuestLifecycleSmokeTest : MonoBehaviour
    {
        string role, directory;
        bool edgeCases;
        DevelopmentSession session;
        NetworkPlayer local;
        PlayerInteractor interactor;
        ServiceRoom room;
        GuestServiceRequests requests;
        TowelCarrier carrier;
        GuestLifecycle guest;
        string fatal;
        Vector3 previousPosition;
        GuestPhase previousPhase;
        uint previousVisit;
        float nextSample;
        float walkedDistance;
        readonly HashSet<GuestPhase> observedPhases=new HashSet<GuestPhase>();
        ReceptionDesk reception;
        NetworkDoor door;
        readonly List<string> checks = new List<string>(), errors = new List<string>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-motel-lifecycle-test");if(i<0)return;
            var test=new GameObject("GuestLifecycleSmokeTest").AddComponent<GuestLifecycleSmokeTest>();
            test.edgeCases=Array.IndexOf(args,"-motel-lifecycle-edge")>=0;
            test.role=args[i+1];test.directory=args[Array.IndexOf(args,"-motel-lifecycle-results")+1];
        }
        void Awake(){Application.logMessageReceived+=Log;}
        void Log(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}
        IEnumerator Start()
        {
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0)
            {
                object next=null;bool more=false;Exception failure=null;
                try{more=stack.Peek().MoveNext();if(more)next=stack.Peek().Current;}catch(Exception e){failure=e;}
                if(failure!=null){Finish(false,failure.ToString());yield break;}
                if(!more){stack.Pop();continue;}
                if(next is IEnumerator nested)stack.Push(nested);else yield return next;
            }
            Finish(errors.Count==0,"Completed without critical runtime logs.");
        }
        IEnumerator Wait(Func<bool> condition,string label,float timeout=100)
        {File.WriteAllText(Path.Combine(directory,role+".progress"),label);float end=Time.realtimeSinceStartup+timeout;while(!condition()){Require(fatal==null,fatal);Require(Time.realtimeSinceStartup<end,"Timeout: "+label+"; guest="+(guest==null?"none":guest.Phase+" at "+guest.transform.position+" blocked="+guest.State.Blocked)+"; local="+(local==null?"none":local.transform.position.ToString()));yield return null;}}
        bool Marked(string name)=>File.Exists(Path.Combine(directory,name+".signal"));
        void Mark(string name){File.WriteAllText(Path.Combine(directory,name+".signal"),"ready");}
        IEnumerator Barrier(string name)
        {Mark(name+"-"+(role=="host"?"host":"client"));yield return Wait(()=>Marked(name+"-host")&&Marked(name+"-client"),name);}
        static void Require(bool yes,string message){if(!yes)throw new Exception(message);}
        NetworkPlayer[] Players()=>session.Manager.SpawnManager.SpawnedObjectsList.Where(n=>n.IsPlayerObject).Select(n=>n.GetComponent<NetworkPlayer>()).ToArray();
        void Update()
        {
            if(guest==null || !guest.IsSpawned)return;
            observedPhases.Add(guest.Phase);
            if(previousVisit==guest.State.Visit && previousPhase!=GuestPhase.Absent && guest.Phase!=GuestPhase.Absent)
            {
                float delta=Vector3.Distance(previousPosition,guest.transform.position);
                if(delta>1.6f*Time.unscaledDeltaTime+.7f)fatal="NPC teleported during active route: "+delta;
                if(role=="host")walkedDistance+=delta;
            }
            previousPosition=guest.transform.position;previousPhase=guest.Phase;previousVisit=guest.State.Visit;
            if(role!="host" || guest.Phase==GuestPhase.Absent || Time.unscaledTime<nextSample)return;
            nextSample=Time.unscaledTime+.1f;Physics.SyncTransforms();
            Vector3 p=guest.transform.position;
            foreach(var c in Physics.OverlapCapsule(p+Vector3.up*.35f,p+Vector3.up*1.5f,.27f,~0,QueryTriggerInteraction.Ignore))
            {
                if(c.transform.IsChildOf(guest.transform)||c.GetComponentInParent<NetworkPlayer>()!=null)continue;
                if(Physics.ComputePenetration(guest.Body,p,guest.transform.rotation,c,c.transform.position,c.transform.rotation,out _,out float depth)&&depth>.055f)
                    fatal="NPC penetrated solid geometry: "+c.name+" depth="+depth+" at "+p;
            }
            if(guest.Phase==GuestPhase.WalkingToRoom && (requests.Phase!=GuestRequestPhase.NoRequest||requests.State.CallAt!=0))fatal="M5 call started before InRoom";
        }
        IEnumerator Run()
        {
            yield return null;session=FindFirstObjectByType<DevelopmentSession>();
            session.TransportSettings.Address="127.0.0.1";session.TransportSettings.Port=(ushort)(edgeCases?17782:17781);
            Require(session.StartSession(role=="host"),"Start failed");
            yield return Wait(()=>session.Manager.IsConnectedClient && Players().Length>=2,"connected");
            local=Players().Single(p=>p.IsOwner);interactor=local.GetComponent<PlayerInteractor>();carrier=local.GetComponent<TowelCarrier>();
            room=FindFirstObjectByType<ServiceRoom>();reception=FindFirstObjectByType<ReceptionDesk>();door=FindFirstObjectByType<NetworkDoor>();requests=FindFirstObjectByType<GuestServiceRequests>();guest=FindFirstObjectByType<GuestLifecycle>();
            yield return Wait(()=>guest!=null&&guest.IsSpawned&&room.IsSpawned&&requests.IsSpawned,"scene network objects");
            Require(FindObjectsByType<GuestLifecycle>(FindObjectsSortMode.None).Length==1,"Duplicate guest");
            Require(guest.NetworkObject.OwnerClientId==NetworkManager.ServerClientId,"Guest client-owned");
            Require(guest.Agent.enabled==(role=="host"&&guest.Phase!=GuestPhase.Absent),"NavMeshAgent runs on client");
            if(role.StartsWith("observe-"))
            {
                GuestPhase expected=(GuestPhase)Enum.Parse(typeof(GuestPhase),role.Substring(8));
                yield return Wait(()=>guest.Phase==expected,"late snapshot "+expected,15);
                Require(guest.Visible&&!guest.Agent.enabled&&!room.GuestInRoom.activeSelf&&!reception.WaitingGuest.activeSelf,"Late visuals / old ghosts");
                if(expected!=GuestPhase.WaitingForCheckIn)Require(guest.State.Assigned && guest.State.Room.TryGet(out var assigned,session.Manager)&&assigned==room.NetworkObject,"Late assigned room");
                if(expected==GuestPhase.WalkingToRoom||expected==GuestPhase.Leaving)
                {
                    Vector3 before=guest.transform.position;yield return new WaitForSecondsRealtime(1.3f);
                    Require(Vector3.Distance(before,guest.transform.position)>.3f,"Late observer does not see NPC moving");
                }
                if(expected==GuestPhase.InRoom)
                {
                    Require(requests.Phase==GuestRequestPhase.PendingDelivery,"Late M5 state");
                    Require(Players().Any(p=>p.GetComponent<TowelCarrier>().HasTowel&&p.GetComponent<TowelCarrier>().VisibleTowel),"Late towel visual");
                }
                checks.Add("Late join "+expected+": one server-owned NPC, correct assigned room/pose, remote agent disabled, no old guest visuals.");
                Mark("observed-"+expected);
            }
            else
            {
                yield return Walk(new Vector3(7,0,5));
                Require(!reception.CanRegister(room),"Registration before arrival/Ready allowed");
                if(role=="host")
                {
                    yield return Wait(()=>guest.Phase==GuestPhase.Arriving,"physical arrival");
                    Require(guest.transform.position.z<-5,"Guest appeared at reception");
                }
                yield return Wait(()=>guest.Phase==GuestPhase.WaitingForCheckIn && Vector3.Distance(guest.transform.position,guest.ReceptionPoint.position)<.22f,"guest reaches reception",45);
                Require(Vector3.Distance(guest.transform.position,guest.ReceptionPoint.position)<.22f,"Waiting pose wrong");
                if(!edgeCases && role=="host")yield return Observe(GuestPhase.WaitingForCheckIn);
                yield return Barrier("initial-arrival");
                if(edgeCases)yield return EdgeCases();
                else
                {
                    for(uint cycle=0;cycle<2;cycle++)yield return FullCycle(cycle);
                    yield return MovementCheckout(2);
                }
                Require(fatal==null,fatal);
                if(role=="host")
                {
                    Require(walkedDistance>(edgeCases?70:120),"Insufficient physical NPC travel: "+walkedDistance);
                    Require(observedPhases.Count==6,"Missing lifecycle phases");
                    checks.Add("Physical NPC travel: "+walkedDistance.ToString("F1")+" m, all six phases; sampled solid collisions and continuity without route teleports.");
                    yield return Wait(()=>Marked("client-finished"),"client finished");
                    yield return Wait(()=>Players().Length==1,"client disconnect");
                }
                else Mark("client-finished");
            }
            session.Disconnect();yield return Wait(()=>!session.Manager.IsListening&&!session.Manager.ShutdownInProgress,"shutdown",10);
            checks.Add("Disconnect completed.");
        }
        IEnumerator Observe(GuestPhase phase)
        {
            Mark("observe-"+phase);yield return Wait(()=>Marked("observed-"+phase),"observer "+phase,25);
            yield return Wait(()=>Players().Length==2,"observer disconnect",15);
        }
        IEnumerator FullCycle(uint cycle)
        {
            yield return PrepareAndCheckIn(cycle);
            if(role=="host"&&cycle==0)yield return Observe(GuestPhase.WalkingToRoom);
            yield return Wait(()=>guest.Phase==GuestPhase.InRoom && Vector3.Distance(guest.transform.position,guest.RoomIdlePoint.position)<.23f,"physical room arrival",60);
            Require(Vector3.Distance(guest.transform.position,guest.RoomIdlePoint.position)<.23f,"Guest not inside room");
            yield return Wait(()=>!door.IsOpen&&!door.IsMoving,"guest closes room door",15);
            yield return Wait(()=>requests.State.CallAt>0,"M5 delay after InRoom");
            double deadline=requests.State.CallAt;
            Require(deadline-session.Manager.ServerTime.Time>10,"M5 delay began before room arrival");
            yield return Wait(()=>requests.Phase==GuestRequestPhase.Ringing,"M5 phone rings",25);
            Require(session.Manager.ServerTime.Time+.2>=deadline,"Early phone call");
            yield return AtReception();yield return Walk(new Vector3(9.5f,0,role=="host"?5.3f:6.15f));
            bool answerer=cycle==0?role=="host":role=="client";
            yield return Barrier("answer-"+cycle);
            if(answerer)Use(requests.Phone,requests.Phone.AimPoint);
            yield return Wait(()=>requests.Phase==GuestRequestPhase.PendingDelivery,"M5 shared request");
            for(int i=0;i<6;i++)requests.AnswerRpc(cycle);
            bool deliverer=!answerer;
            if(deliverer)
            {
                yield return ToSupply();Use(requests.TowelSupply,requests.TowelSupply.AimPoint);
                yield return Wait(()=>carrier.HasTowel&&carrier.VisibleTowel,"M5 carry");
                requests.TakeTowelRpc(cycle);requests.DeliverRpc(new NetworkObjectReference(reception.NetworkObject),cycle);
            }
            yield return Wait(()=>Players().Any(p=>p.GetComponent<TowelCarrier>().HasTowel),"shared towel");
            if(cycle==1)
            {
                if(role=="host")yield return Observe(GuestPhase.InRoom);
                yield return Barrier("inroom-observed");
            }
            if(deliverer)
            {
                yield return FromSupply();yield return ToDelivery();Use(requests.Delivery,requests.Delivery.AimPoint);
                for(int i=0;i<6;i++)requests.DeliverRpc(new NetworkObjectReference(room.NetworkObject),cycle);
            }
            yield return Wait(()=>requests.Phase==GuestRequestPhase.Completed,"M5 delivery complete");
            Require(room.State.Status==RoomStatus.Occupied&&guest.Phase==GuestPhase.InRoom,"Delivery changed occupancy/lifecycle");
            yield return Wait(()=>Players().All(p=>!p.GetComponent<TowelCarrier>().HasTowel),"towels consumed");
            if(deliverer){yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(new Vector3(9.5f,0,8.4f));}
            yield return AtReception();OpenReception();yield return Barrier("checkout-"+cycle);
            for(int i=0;i<6;i++)reception.Checkout(0);reception.CloseMenu();
            yield return Wait(()=>guest.Phase==GuestPhase.Leaving&&room.State.Cycle==cycle+1,"physical departure begins");AssertReset(cycle+1);
            yield return Walk(new Vector3(7,0,5));
            if(role=="host"&&cycle==0)yield return Observe(GuestPhase.Leaving);
            yield return Wait(()=>guest.Phase==GuestPhase.Absent && Vector3.Distance(guest.transform.position,guest.DeparturePoint.position)<.25f,"guest reaches parking exit",65);
            Require(Vector3.Distance(guest.transform.position,guest.DeparturePoint.position)<.25f&&!guest.Visible&&!guest.Body.enabled,"Premature removal / wrong departure point");
            checks.Add($"Cycle {cycle}: physical arrival/check-in/door crossing/InRoom, 15s M5 call, cooperative delivery, checkout resets M4/M5, guest walks out before hiding.");
            yield return Wait(()=>guest.Phase==GuestPhase.WaitingForCheckIn,"next guest arrives",40);
            Require(guest.State.Visit==cycle+2&&FindObjectsByType<GuestLifecycle>(FindObjectsSortMode.None).Length==1,"Guest accumulation / visit counter");
            yield return Barrier("next-"+cycle);
        }
        IEnumerator EdgeCases()
        {
            // A real connected player blocks the approach. No extra geometry or test-only
            // navigation state is injected; clearing the path must resume the existing agent.
            yield return PrepareAndCheckIn(0);
            if(role=="host")
            {
                yield return Walk(new Vector3(9.5f,0,8.4f));yield return Walk(new Vector3(22.2f,0,8.4f));
                yield return Walk(guest.DoorOutside.position);
            }
            yield return Barrier("door-blocker-ready");
            yield return Wait(()=>guest.State.Blocked&&Vector3.Distance(guest.transform.position,guest.DoorOutside.position)<1.2f,"player obstructs NPC approach",25);
            // Let the client's interpolated pose reach the server's stopped pose first.
            yield return new WaitForSecondsRealtime(.5f);
            Vector3 stopped=guest.transform.position;yield return new WaitForSecondsRealtime(2);
            Require(Vector3.Distance(stopped,guest.transform.position)<.15f && guest.Phase==GuestPhase.WalkingToRoom,"Blocked guest moved: "+stopped+" -> "+guest.transform.position+" phase="+guest.Phase);
            yield return Barrier("obstruction-confirmed");
            if(role=="host")yield return Walk(new Vector3(22.2f,0,15.1f));
            else {yield return AtReception();OpenReception();}
            yield return Wait(()=>door.GuestPassage&&door.IsOpen&&!door.IsMoving&&guest.transform.position.x<20.5f,"NPC crosses reserved doorway",30);
            // A close request cannot interrupt the guest's reservation.
            for(int i=0;i<8;i++)door.RequestToggleRpc();
            if(role=="client") {reception.Checkout(0);reception.CloseMenu();}
            yield return Wait(()=>guest.Phase==GuestPhase.Leaving&&room.State.Cycle==1,"checkout halfway through doorway",10);
            AssertReset(1);
            if(role=="host") {yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(new Vector3(9.5f,0,8.4f));}
            yield return Walk(new Vector3(7,0,5));
            yield return Wait(()=>guest.Phase==GuestPhase.Absent,"interrupted entry reaches parking",65);
            yield return Wait(()=>guest.Phase==GuestPhase.WaitingForCheckIn,"next guest after interrupted door entry",40);
            Require(!door.GuestPassage&&!door.IsOpen&&!door.IsMoving,"Door reservation or open state leaked after departure");
            AssertReset(1);checks.Add("Player obstruction pauses/resumes NPC; client checkout midway through reserved doorway reverses safely; door closes and next guest arrives.");
            yield return Barrier("door-interruption-complete");

            yield return PrepareAndCheckIn(1);
            yield return Wait(()=>guest.Phase==GuestPhase.InRoom,"guest in room before pending cancellation",60);
            yield return Wait(()=>requests.Phase==GuestRequestPhase.Ringing,"call after physical arrival",25);
            yield return AtReception();yield return Walk(new Vector3(9.5f,0,role=="host"?5.3f:6.15f));
            if(role=="host")Use(requests.Phone,requests.Phone.AimPoint);
            yield return Wait(()=>requests.Phase==GuestRequestPhase.PendingDelivery,"pending request to cancel");
            yield return ToSupply();Use(requests.TowelSupply,requests.TowelSupply.AimPoint);
            yield return Wait(()=>carrier.HasTowel&&Players().All(p=>p.GetComponent<TowelCarrier>().HasTowel),"both players carry towels");
            yield return FromSupply();yield return AtReception();OpenReception();yield return Barrier("pending-checkout");
            for(int i=0;i<8;i++)reception.Checkout(0);reception.CloseMenu();
            yield return Wait(()=>guest.Phase==GuestPhase.Leaving&&room.State.Cycle==2,"pending request checkout");
            yield return Wait(()=>Players().All(p=>!p.GetComponent<TowelCarrier>().HasTowel&&!p.GetComponent<TowelCarrier>().VisibleTowel),"checkout removes both towels");
            AssertReset(2);requests.AnswerRpc(1);requests.TakeTowelRpc(1);requests.DeliverRpc(new NetworkObjectReference(room.NetworkObject),1);
            yield return Walk(new Vector3(7,0,5));
            yield return Wait(()=>guest.Phase==GuestPhase.Absent,"pending cancellation physical departure",65);
            yield return Wait(()=>guest.Phase==GuestPhase.WaitingForCheckIn,"next guest after cancelled request",40);
            AssertReset(2);checks.Add("Checkout during PendingDelivery clears request and both carried towel visuals, rejects stale RPCs, guest leaves physically and next arrival is consistent.");
            yield return Barrier("edge-complete");
        }
        IEnumerator MovementCheckout(uint cycle)
        {
            yield return PrepareAndCheckIn(cycle);
            Require(guest.Phase==GuestPhase.WalkingToRoom,"Expected walking guest");
            yield return AtReception();OpenReception();yield return Barrier("early-checkout");
            if(role=="client")reception.Checkout(0);reception.CloseMenu();
            yield return Wait(()=>guest.Phase==GuestPhase.Leaving&&room.State.Cycle==cycle+1,"checkout redirects walking guest");
            AssertReset(cycle+1);yield return Walk(new Vector3(7,0,5));
            yield return new WaitForSecondsRealtime(requests.CallDelay+1);
            Require(requests.Phase==GuestRequestPhase.NoRequest&&requests.State.CallAt==0,"Stale phone after early checkout");
            yield return Wait(()=>guest.Phase==GuestPhase.Absent||guest.State.Visit>3,"early checkout departure",50);
            yield return Wait(()=>guest.Phase==GuestPhase.WaitingForCheckIn,"recovery guest arrives",45);
            AssertReset(cycle+1);checks.Add("Client checkout while walking redirects guest to exit; no delayed phone, reset consistent, next guest waits normally.");
            yield return Barrier("early-done");
        }
        IEnumerator PrepareAndCheckIn(uint cycle)
        {
            yield return EnterRoom(cycle);
            bool bedWorker=(cycle%2==0)==(role=="host");
            yield return Walk(bedWorker?new Vector3(18.5f,0,13.2f):new Vector3(18.8f,0,12.65f));
            yield return Barrier("clean-start-"+cycle);
            if(bedWorker)Use(room.Bed,room.Bed.AimPoint);else Use(room.Trash[0],room.Trash[0].AimPoint);
            yield return Wait(()=>room.State.BedMade&&room.IsCleaned(0),"M4 partial cleaning");Require(room.State.Status==RoomStatus.Dirty,"Premature Ready");
            yield return Barrier("partial-"+cycle);
            if(!bedWorker){yield return Walk(new Vector3(18,0,12.2f));yield return Walk(new Vector3(17.7f,0,12.2f));Use(room.Trash[1],room.Trash[1].AimPoint);}
            yield return Wait(()=>room.State.Status==RoomStatus.Ready,"M4 Ready");yield return Barrier("ready-"+cycle);
            yield return Walk(new Vector3(18.1f,0,12.4f));yield return Walk(new Vector3(19.1f,0,12.65f));yield return Walk(new Vector3(22.2f,0,12.65f));
            yield return Barrier("outside-door-"+cycle);
            if(role=="host"&&door.IsOpen)Use(door,door.InteractionPoint);
            yield return Wait(()=>!door.IsOpen&&!door.IsMoving,"players leave closed door");
            yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(new Vector3(9.5f,0,8.4f));yield return AtReception();OpenReception();
            Require(reception.CanRegister(room),"Physical waiting guest not registerable");yield return Barrier("checkin-"+cycle);
            if(cycle==0){if(role=="client")reception.SelectRoom(0);}else for(int i=0;i<8;i++)reception.SelectRoom(0);
            yield return Wait(()=>room.State.Status==RoomStatus.Occupied&&guest.Phase==GuestPhase.WalkingToRoom,"M4 check-in assigns physical guest");
            Require(guest.State.Assigned&&guest.State.RoomCycle==cycle,"Assignment mismatch");
            Require(!reception.GuestWaiting&&!reception.CanRegister(room)&&!room.GuestInRoom.activeSelf&&!reception.WaitingGuest.activeSelf,"Duplicate registration/ghost placeholder");
            Require(requests.Phase==GuestRequestPhase.NoRequest&&requests.State.CallAt==0,"M5 started on check-in instead of arrival");
            reception.CloseMenu();yield return Walk(new Vector3(7,0,5));
        }
        void AssertReset(uint cycle)
        {
            Require(room.State.Cycle==cycle&&room.State.Status==RoomStatus.Dirty&&!room.State.BedMade&&room.TrashRemovedCount==0,"M4 reset snapshot");
            Require(room.Bed.CanInteract&&room.Trash.All(t=>t.CanInteract&&t.TargetCollider.enabled),"M4 reset interactions");
            Require(requests.Phase==GuestRequestPhase.NoRequest&&requests.State.CallAt==0&&!carrier.HasTowel,"M5 reset");
        }
        IEnumerator ToSupply()
        {
            yield return Walk(new Vector3(9.5f,0,8.4f));yield return Walk(new Vector3(12.8f,0,8.4f));
            yield return Walk(new Vector3(12.8f,0,4.6f));yield return Walk(new Vector3(16.8f,0,4.6f));yield return Walk(new Vector3(17,0,5.15f));
        }
        IEnumerator FromSupply()
        {
            yield return Walk(new Vector3(16.8f,0,4.6f));yield return Walk(new Vector3(12.8f,0,4.6f));
            yield return Walk(new Vector3(12.8f,0,8.4f));yield return Walk(new Vector3(9.5f,0,8.4f));
        }
        IEnumerator ToDelivery(){yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(new Vector3(22.2f,0,13.75f));}
        IEnumerator EnterRoom(uint cycle)
        {
            yield return Walk(new Vector3(9.5f,0,8.4f));yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(new Vector3(22.2f,0,12.65f));
            if(role=="host" && !door.IsOpen){Use(door,door.InteractionPoint);yield return Wait(()=>door.IsOpen&&!door.IsMoving,"door open");}
            else yield return Wait(()=>door.IsOpen&&!door.IsMoving,"host door open");
            yield return Walk(new Vector3(19.1f,0,12.65f));
        }
        IEnumerator AtReception(){yield return Walk(new Vector3(9.5f,0,role=="host"?5.0f:6.4f));yield return new WaitForSecondsRealtime(.35f);}
        void OpenReception()
        {
            var target=reception.Counter.GetComponent<ReceptionInteraction>();Use(target,reception.Counter.bounds.center);
            Require(reception.MenuOpen && interactor.MenuOpen && local.GetComponent<FirstPersonController>().InputSuspended,"Reception modal input not suspended");
        }
        void Use(IInteractable expected,Vector3 point)
        {
            var camera = local.GetComponentInChildren<Camera>(true);
            camera.transform.LookAt(point);
            var found = interactor.FindTarget();
            if (found != expected)
            {
                Physics.SyncTransforms();
                string hits = string.Join(", ", Physics.RaycastAll(camera.transform.position, camera.transform.forward, 2.5f).OrderBy(h=>h.distance).Select(h=>h.collider.name+":"+h.distance));
                throw new Exception($"Ray miss. Expected={expected}, found={found}, can={expected.CanInteract}, enabled={interactor.enabled}, owner={interactor.IsOwner}, modal={interactor.MenuOpen}, eye={camera.transform.position}, point={point}, hits={hits}");
            }
            Require(interactor.TryInteract(),"Interaction not available");
        }
        IEnumerator Walk(Vector3 target)
        {
            float end=Time.realtimeSinceStartup+18;var cc=local.GetComponent<CharacterController>();
            while(true)
            {
                Vector3 d=target-local.transform.position;d.y=0;if(d.magnitude<.06f)break;
                Require(Time.realtimeSinceStartup<end,"Walk blocked toward "+target+" at "+local.transform.position);
                cc.Move(Vector3.ClampMagnitude(d,4*Time.deltaTime)+Vector3.down*2*Time.deltaTime);yield return null;
            }
            yield return new WaitForSecondsRealtime(.2f);
        }
        void Finish(bool pass,string reason)
        {File.WriteAllText(Path.Combine(directory,role+".txt"),(pass?"PASS":"FAIL")+" "+role+"\n"+string.Join("\n",checks)+"\n"+reason+"\n"+string.Join("\n",errors));Application.Quit(pass?0:1);}
        void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
#endif
