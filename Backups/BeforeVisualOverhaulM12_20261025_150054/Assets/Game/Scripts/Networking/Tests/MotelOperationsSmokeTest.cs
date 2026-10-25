#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Unity.Netcode;
using MotelGame.Interactions;
using MotelGame.Service;

namespace MotelGame.Networking.Tests
{
    // Explicit Development test only. Configuration is tuned before a night; all gameplay
    // changes use normal raycasts/RPCs. File signals coordinate test actors, never game state.
    [DefaultExecutionOrder(200)]
    public sealed class MotelOperationsSmokeTest : MonoBehaviour
    {
        string role, directory, fatal;
        DevelopmentSession session;
        NetworkPlayer local;
        PlayerInteractor interactor;
        ServiceRoom[] rooms;
        GuestLifecycle[] guests;
        ReceptionDesk reception;
        ReceptionComputer computer;
        NightShiftController shift;
        GuestServiceRequests requests;
        StaffTaskBoard board;
        GarbageDumpster dumpster;
        TowelCarrier Carrier => local.GetComponent<TowelCarrier>();
        bool EdgesMode => Environment.GetCommandLineArgs().Contains("-motel-operations-edges");
        bool NightMode => Environment.GetCommandLineArgs().Contains("-motel-operations-night");
        readonly List<string> checks=new(),errors=new();
        readonly HashSet<string> contested=new();
        readonly Dictionary<GuestLifecycle,(Vector3 p,uint visit,GuestPhase phase)> positions=new();
        int maximumQueue;float distance;float nextDiagnostic;bool joining; readonly HashSet<string> captures=new();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-motel-operations-test");if(i<0)return;
            var t=new GameObject("MotelOperationsSmokeTest").AddComponent<MotelOperationsSmokeTest>();t.role=args[i+1];t.directory=args[Array.IndexOf(args,"-motel-operations-results")+1];
        }
        void Awake()=>Application.logMessageReceived+=Log;
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}
        static void Require(bool b,string m){if(!b)throw new Exception(m);}
        static void Configure(object o,string field,object value)=>o.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,value);
        void Mark(string n)=>File.WriteAllText(Path.Combine(directory,n+".signal"),"ready");
        bool Marked(string n)=>File.Exists(Path.Combine(directory,n+".signal"));
        IEnumerator Barrier(string n){Mark(n+"-"+role);yield return Wait(()=>Marked(n+"-host")&&Marked(n+"-client"),n,1200);}
        IEnumerator Wait(Func<bool> c,string n,float seconds=120)
        {
            File.WriteAllText(Path.Combine(directory,role+".progress"),n);float end=Time.realtimeSinceStartup+seconds;
            while(!c()){Require(fatal==null,fatal);Require(Time.realtimeSinceStartup<end,"Timeout "+n+"; "+Diagnostic());yield return null;}
        }
        string Diagnostic()=>"player="+(local==null?"none":local.transform.position.ToString())+" shift="+(shift==null?"none":shift.State.Phase+" "+shift.ClockText)+
            " rooms="+(rooms==null?"":string.Join(";",rooms.Select(r=>r.RoomNumber+":"+r.State.Status+" bed="+r.State.BedMade+" trash="+r.TrashRemovedCount)))+" feedback="+interactor?.Feedback+" request="+(requests==null?"none":requests.State.RequestId+" "+requests.RequestName+" "+requests.Phase+" step="+requests.State.RepairStep)+"; "+
            (guests==null?"":string.Join("; ",guests.Select(g=>g.GuestName+" "+g.Phase+" q="+g.State.QueuePosition+" pos="+g.transform.position)));
        IEnumerator Start()
        {
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0)
            {
                object n=null;bool more=false;Exception failure=null;
                try{more=stack.Peek().MoveNext();if(more)n=stack.Peek().Current;}catch(Exception e){failure=e;}
                if(failure!=null){Finish(false,failure.ToString());yield break;}
                if(!more){stack.Pop();continue;}
                if(n is IEnumerator nested)stack.Push(nested);else yield return n;
            }
            Finish(errors.Count==0,"No critical runtime logs.");
        }
        void Bind()
        {
            dumpster = FindFirstObjectByType<GarbageDumpster>(); positions.Clear();
            local=session.Manager.LocalClient.PlayerObject.GetComponent<NetworkPlayer>();interactor=local.GetComponent<PlayerInteractor>();
            rooms=FindObjectsByType<ServiceRoom>(FindObjectsSortMode.None).OrderBy(r=>r.RoomNumber).ToArray();guests=FindObjectsByType<GuestLifecycle>(FindObjectsSortMode.None);
            reception=FindFirstObjectByType<ReceptionDesk>();computer=FindFirstObjectByType<ReceptionComputer>();shift=FindFirstObjectByType<NightShiftController>();requests=FindFirstObjectByType<GuestServiceRequests>();board=FindFirstObjectByType<StaffTaskBoard>();
        }
        IEnumerator Connect()
        {
            Require(session.StartSession(role=="host"),"Connection start");
            yield return Wait(()=>session.Manager.IsConnectedClient&&session.Manager.LocalClient.PlayerObject!=null,"connection");Bind();
            yield return Wait(()=>shift.IsSpawned&&computer.IsSpawned&&rooms.All(r=>r.IsSpawned)&&guests.All(g=>g.IsSpawned),"scene synchronized");joining=false;
        }
        void Update()
        {
            if (guests == null || joining || shift == null || !shift.IsSpawned) return;
            if (Time.unscaledTime >= nextDiagnostic)
            { nextDiagnostic = Time.unscaledTime + 1; File.WriteAllText(Path.Combine(directory, role + ".state"), Diagnostic() + " bags=" + string.Join(";", rooms.Select(r => r.RoomNumber + ":" + r.State.Bag + "/fault=" + r.State.Faulted + "/reserved=" + r.State.Reserved)) + " stats=" + shift.SummaryText); }
            if (rooms.Count(r => r.State.Faulted) > 1) fatal = "More than one electrical fault";
            if (shift.State.CheckedOut > shift.State.CheckedIn || shift.State.ElectricalRepairs > shift.State.ElectricalFaults) fatal = "Duplicate statistics";
            if(!Application.isBatchMode)
            {
                string image=computer.Focused?"computer":Carrier.HasBag?"carried-bag":rooms.Any(r=>r.State.Faulted)?"power-fault":shift.State.Phase==ShiftPhase.Finished?"summary":null;
                if(image!=null&&captures.Add(image))ScreenCapture.CaptureScreenshot(Path.Combine(directory,role+"-"+image+".png"));
            }
            if (role != "host") return;
            foreach (var guest in guests)
            {
                if (positions.TryGetValue(guest, out var prev) && prev.visit == guest.State.Visit && prev.phase != GuestPhase.Absent && guest.Phase != GuestPhase.Absent)
                { float d = Vector3.Distance(prev.p, guest.transform.position); if (d > 1.6f * Time.unscaledDeltaTime + .7f) fatal = "NPC route teleport"; distance += d; }
                positions[guest] = (guest.transform.position, guest.State.Visit, guest.Phase);
            }
        }
        IEnumerator Run()
        {
            yield return null;
            session=FindFirstObjectByType<DevelopmentSession>();session.TransportSettings.Address="127.0.0.1";session.TransportSettings.Port=(ushort)(EdgesMode?17793:NightMode?17792:17791);
            yield return Connect();
            if(role.StartsWith("observer")) { yield return Observe(); yield break; }
            if(EdgesMode) { yield return ContestedOperations(); yield break; }
            yield return Barrier("connected");
            if(role=="host")
            {
                Configure(shift,"durationSeconds",NightMode?900f:600f);
                Configure(shift,"electricalSeed",311);
                if(!NightMode)
                {
                    Configure(shift,"electricalFaultCount",new Vector2Int(0,0));Configure(shift,"arrivalCount",new Vector2Int(2,2));
                    Configure(shift,"firstArrivalDelay",60f);Configure(shift,"lastArrivalFraction",.5f);Configure(reception,"arrivalPatience",450f);
                    Configure(requests,"televisionChance",0f);Configure(requests,"callDelay",5f);Configure(requests,"betweenRequestsDelay",2f);
                    foreach(var guest in guests)Configure(guest,"stayDuration",180f);
                }
                UnityEngine.Random.InitState(4511);
            }
            yield return StartNight(0);
            if(NightMode)
            {
                if(role=="host")yield return Operate();else yield return Work();
            }
            else yield return FocusedScenario();
            yield return Wait(()=>shift.State.Phase==ShiftPhase.Finished,"night finished",950);
            yield return new WaitForSecondsRealtime(1);
            Require(fatal==null,fatal);Require(shift.State.TrashDisposed>=3&&shift.State.CheckedIn>=1&&shift.State.CheckedOut>=1,"Insufficient integrated service cycles");
            Require(shift.State.ElectricalRepairs>=1&&(NightMode||shift.State.Relocations>=1),"Missing maintenance/relocation coverage: "+shift.SummaryText);
            if(NightMode)Require(shift.State.Towels>=1&&shift.State.Repairs>=1&&shift.State.CheckedOut>=2,"Missing full-night services");
            CheckReset();
            checks.Add("Night complete: "+shift.SummaryText.Replace('\n',';'));
            if(role=="host")File.WriteAllText(Path.Combine(directory,"summary-one.txt"),shift.SummaryText);
            yield return Barrier("first-finished");
            Require(File.ReadAllText(Path.Combine(directory,"summary-one.txt"))==shift.SummaryText,"Shared summary differs");
            if(role=="host") {Configure(shift,"durationSeconds",180f);Configure(shift,"arrivalCount",new Vector2Int(2,2));Configure(shift,"firstArrivalDelay",20f);Configure(shift,"lastArrivalFraction",.5f);Configure(shift,"electricalFaultCount",new Vector2Int(0,0));foreach(var g in guests)Configure(g,"stayDuration",45f);}
            // All workers return from their final location before restarting; no character teleport.
            if(local.transform.position.z<0)yield return FromDumpster();else if(local.transform.position.x>14)yield return BackToLobby();
            yield return StartNight(1);
            Require(rooms.All(r=>!r.State.Faulted&&!r.State.Reserved&&r.State.Bag==GarbagePhase.Collecting),"Restart retained M11 state");
            if(role=="host")yield return Operate();else yield return Work();
            yield return Wait(()=>shift.State.Phase==ShiftPhase.Finished,"second finished",220);
            yield return new WaitForSecondsRealtime(.6f);CheckReset();
            Require(shift.State.CheckedIn>=1&&shift.State.CheckedOut>=1&&shift.State.TrashDisposed>=2,"Second night not playable");
            checks.Add("Second night, same connections: "+shift.SummaryText.Replace('\n',';'));
            yield return Barrier("complete");
        }
        void CheckReset()
        {
            Require(!Carrier.HasItem&&requests.Phase==GuestRequestPhase.NoRequest&&!computer.Occupied,"Temporary state retained");
            Require(rooms.All(r=>r.State.Status==RoomStatus.Dirty&&!r.State.Faulted&&!r.State.Reserved&&r.State.Bag==GarbagePhase.Collecting&&r.ControlledLights.All(l=>l.enabled)),"Room reset");
            Require(guests.All(g=>g.Phase==GuestPhase.Absent&&!g.Visible),"Guest reset");
        }
        IEnumerator StartNight(uint previous)
        {
            if(role=="host")
            {
                yield return ToComputer();yield return OpenComputer();uint lease=computer.Lease;
                for(int i=0;i<4;i++)shift.StartShiftRpc(previous,lease);
            }
            yield return Wait(()=>shift.IsRunning&&shift.State.Number==previous+1&&!computer.LocalOpen,"start night");
            yield return Barrier("start-"+previous);
        }
        IEnumerator ContestedOperations()
        {
            yield return Barrier("connected");
            if(role=="host")
            {
                Configure(shift,"durationSeconds",240f);Configure(shift,"electricalFaultCount",new Vector2Int(0,0));
                Configure(shift,"arrivalCount",new Vector2Int(1,1));Configure(shift,"firstArrivalDelay",300f);
            }
            yield return StartNight(0);
            if(role=="host")
            {
                yield return Collect(0);yield return EnterRoom(0);yield return Walk(new Vector3(20,0,14.4f));Mark("contest-bag-ready");
                yield return Wait(()=>Marked("contest-client-entered"),"client enters contest room");
                yield return Walk(new Vector3(20,0,13.4f));Mark("contest-host-placed");
            }
            else
            {
                yield return Wait(()=>Marked("contest-bag-ready"),"host prepares contested bag");yield return EnterRoom(0);Mark("contest-client-entered");
                yield return Wait(()=>Marked("contest-host-placed"),"host moves clear");yield return Walk(new Vector3(20,0,14.4f));
            }
            yield return Barrier("contest-positioned");yield return new WaitForSecondsRealtime(.4f);
            if(role=="host")Require(session.Manager.ConnectedClientsList.All(c=>ServiceInteractionValidation.CanReach(session.Manager,c.ClientId,rooms[0].BagPickup.TargetCollider)),"Both contenders must pass real server reach validation");
            yield return Barrier("contest-pickup");
            for(int i=0;i<10;i++)rooms[0].PickUpBagRpc(rooms[0].State.Cycle);
            yield return Wait(()=>rooms[0].State.Bag==GarbagePhase.Carried,"one contender carries bag");yield return new WaitForSecondsRealtime(.4f);
            Require(FindObjectsByType<TowelCarrier>(FindObjectsSortMode.None).Count(c=>c.HasBag)==1,"Concurrent pickup duplicated bag");
            if(role=="host") {yield return Walk(new Vector3(20,0,14.4f));Mark("contest-exit-clear");yield return Wait(()=>Marked("contest-client-out"),"client exits contest room");if(rooms[0].Door.IsOpen)Use(rooms[0].Door,rooms[0].Door.InteractionPoint);yield return Wait(()=>!rooms[0].Door.IsOpen&&!rooms[0].Door.IsMoving,"clear leaf");yield return LeaveRoom(0);}
            else {yield return Wait(()=>Marked("contest-exit-clear"),"host clears sweep");yield return LeaveRoom(0);Mark("contest-client-out");}
            yield return Barrier("contest-out");
            yield return Walk(new Vector3(9.5f,0,8.4f));yield return Walk(new Vector3(8,0,2));yield return Walk(new Vector3(8,0,-3.1f));
            yield return Walk(new Vector3(16.8f,0,role=="host"?-3.1f:-4f));yield return Barrier("dumpster-positioned");yield return new WaitForSecondsRealtime(.4f);
            if(role=="host")Require(session.Manager.ConnectedClientsList.All(c=>ServiceInteractionValidation.CanReach(session.Manager,c.ClientId,dumpster.TargetCollider)),"Both disposal contenders must be in reach");
            yield return Barrier("disposal-contest");
            for(int i=0;i<10;i++)dumpster.DisposeRpc(new NetworkObjectReference(rooms[0].NetworkObject),rooms[0].State.Cycle);
            yield return Wait(()=>rooms[0].State.Status==RoomStatus.Ready,"one disposal transaction");yield return FromDumpster();
            yield return Barrier("contest-disposed");Require(rooms[0].State.Status==RoomStatus.Ready&&shift.State.TrashDisposed==1,"Concurrent pickup/disposal counted twice");
            if(role=="host")Require(shift.TryGenerateFault(rooms[1]),"Contested breaker fault");
            yield return Wait(()=>rooms[1].State.Faulted,"breaker fault replicated");
            yield return Walk(new Vector3(12.8f,0,8.4f));yield return Walk(new Vector3(12.8f,0,4.6f));yield return Walk(new Vector3(16.8f,0,4.6f));
            yield return Walk(new Vector3(role=="host"?16.1f:17.3f,0,3.2f));yield return Barrier("breaker-positioned");yield return new WaitForSecondsRealtime(.4f);
            if(role=="host")Require(session.Manager.ConnectedClientsList.All(c=>ServiceInteractionValidation.CanReach(session.Manager,c.ClientId,rooms[1].Breaker.TargetCollider)),"Both breaker contenders must be in reach");
            yield return Barrier("breaker-contest");uint fault=rooms[1].State.FaultId;
            for(int i=0;i<10;i++)rooms[1].ResetBreakerRpc(fault);
            yield return Wait(()=>!rooms[1].State.Faulted,"contested repair");yield return new WaitForSecondsRealtime(.4f);
            Require(shift.State.ElectricalRepairs==1&&rooms[1].ControlledLights.All(l=>l.enabled),"Contested repair duplicated completion");
            yield return Walk(new Vector3(16.8f,0,4.6f));yield return Walk(new Vector3(12.8f,0,4.6f));yield return Walk(new Vector3(12.8f,0,8.4f));yield return Walk(new Vector3(13.3f,0,7.5f));
            if(role=="client") {yield return Collect(3);yield return TakeAvailableBag(3);Mark("closing-with-bag");}
            else {yield return Wait(()=>Marked("closing-with-bag"),"client holds bag at closing",100);Require(shift.TryGenerateFault(rooms[2]),"Closing fault");}
            yield return Barrier("closing-state");
            yield return Wait(()=>rooms[3].State.Bag==GarbagePhase.Carried&&rooms[2].State.Faulted,"closing state replicated",5);
            uint oldCycle=rooms[3].State.Cycle;uint oldFault=rooms[2].State.FaultId;
            Require(rooms[3].State.Bag==GarbagePhase.Carried&&rooms[2].State.Faulted,"No active cleanup state at closing");
            yield return Wait(()=>shift.State.Phase==ShiftPhase.Finished,"close with active bag/fault",220);yield return new WaitForSecondsRealtime(.7f);CheckReset();
            if(role=="host")Configure(shift,"durationSeconds",60f);
            yield return StartNight(1);
            rooms[3].PickUpBagRpc(oldCycle);rooms[2].ResetBreakerRpc(oldFault);dumpster.DisposeRpc(new NetworkObjectReference(rooms[3].NetworkObject),oldCycle);
            yield return new WaitForSecondsRealtime(.5f);
            Require(rooms.All(r=>r.State.Bag==GarbagePhase.Collecting&&!r.State.Faulted&&!r.State.Reserved)&&!Carrier.HasItem&&shift.State.TrashDisposed==0&&shift.State.ElectricalRepairs==0,"Stale actions crossed shift boundary");
            if(role=="client") {yield return ToComputer();yield return OpenComputer();Mark("edge-client-at-computer");}
            yield return Wait(()=>Marked("edge-client-at-computer")&&computer.Occupied,"client owns workstation before disconnect",20);
            yield return Barrier("edge-workstation");
            if(role=="client")
            {
                joining=true;session.Disconnect();yield return Wait(()=>!session.Manager.IsListening&&!session.Manager.ShutdownInProgress,"disconnect workstation owner");
                yield return Wait(()=>Marked("edge-workstation-recovered"),"host takes released workstation");yield return Connect();
            }
            else
            {
                yield return Wait(()=>!computer.Occupied,"workstation lease released on disconnect",10);yield return OpenComputer();
                Require(computer.Focused,"Remaining worker could not reopen workstation");computer.CloseLocal();yield return Wait(()=>!computer.LocalOpen,"restore first person after recovery");Mark("edge-workstation-recovered");
            }
            checks.Add("Computer owner's disconnect released the lease; host reopened it and client reconnected with normal owner camera/input.");
            checks.Add("Both clients in validated range: 20 competing pickup RPCs produce one carrier; 20 competing disposal RPCs produce one disposal; 20 competing breaker RPCs produce one repair. Closing clears carried bag + live fault; next shift rejects stale pickup/disposal/breaker IDs.");
            yield return Barrier("edges-complete");
        }
        IEnumerator Observe()
        {
            yield return new WaitForSecondsRealtime(.7f);
            if(role=="observer-bag")
            {
                var carrying=FindObjectsByType<TowelCarrier>(FindObjectsSortMode.None).Single(c=>c.HasBag);
                Require(carrying.VisibleBag&&carrying.BagRoom==rooms[0]&&rooms[0].State.Bag==GarbagePhase.Carried,"Late bag state/visual");
            }
            else if(role=="observer-fault")
            {
                Require(rooms[0].State.Faulted&&rooms[0].ControlledLights.All(l=>!l.enabled)&&requests.State.Kind==GuestRequestKind.ElectricalMaintenance&&requests.Phase==GuestRequestPhase.Ringing,"Late outage/request");
                Require(board.DisplayText.Contains("POWER FAULT"),"Late board");
            }
            else if(role=="observer-relocation")
            {
                var guest=guests.Single(g=>g.Phase==GuestPhase.Relocating);
                Require(guest.RelocationDestination==rooms[1]&&rooms[1].State.Reserved&&!rooms[1].AvailableForCheckIn&&rooms[0].State.Faulted,"Late reservation/relocation");
                Require(rooms[0].ControlledLights.All(l=>!l.enabled)&&requests.Phase==GuestRequestPhase.NoRequest,"Late old-room maintenance/cancellation");
            }
            Require(guests.All(g=>!g.Agent.enabled),"Client simulated NPC");
            checks.Add("Late join synchronized "+role+"; remote NPC agents disabled.");Mark(role+"-checked");
        }
        IEnumerator FocusedScenario()
        {
            // All fault triggers below use the real server's validated operation. Only their
            // timing/room selection is diagnostic; the full-night mode uses its seeded agenda.
            if(role=="host")
            {
                yield return Collect(0);yield return BackToLobby();Mark("bag-created");
                yield return Wait(()=>rooms[0].State.Bag==GarbagePhase.Carried,"client takes shared bag");
                yield return Wait(()=>Marked("observer-bag-checked"),"bag observer",45);Mark("disconnect-bag");
                yield return Wait(()=>rooms[0].State.Bag==GarbagePhase.AwaitingPickup,"disconnect returns bag",15);Mark("bag-recovered");
                yield return Wait(()=>Marked("preparation-done"),"client preparation",240);
                Require(rooms[0].State.Status==RoomStatus.Ready&&rooms[1].State.Status==RoomStatus.Ready&&rooms[2].State.Bag==GarbagePhase.AwaitingPickup,"Disposal gated readiness");
                Require(shift.TryGenerateFault(rooms[1]),"Empty fault trigger");yield return ToComputer();yield return OpenComputer();
                yield return Wait(()=>reception.WaitingNpc!=null,"arrival for blocked-room registration",180);
                var g=reception.WaitingNpc;reception.RequestCheckInRpc(1,rooms[1].State.Cycle,new NetworkObjectReference(g.NetworkObject),g.State.Visit,computer.Lease);
                yield return new WaitForSecondsRealtime(.5f);Require(!rooms[1].AvailableForCheckIn&&rooms[1].State.Status==RoomStatus.Ready,"Faulty empty room admitted guest");Mark("empty-fault-checked");
                yield return Wait(()=>!rooms[1].State.Faulted,"client repairs empty room");reception.SelectRoom(0);
                yield return Wait(()=>rooms[0].Occupant!=null&&rooms[0].Occupant.IsInRoomFor(rooms[0]),"first guest in room");Mark("guest-in-room");
                yield return Wait(()=>Marked("towel-conflict-tested"),"towel/bag conflict and delivery",120);
                Require(shift.TryGenerateFault(rooms[0]),"Occupied fault trigger");
                yield return Wait(()=>requests.State.Kind==GuestRequestKind.ElectricalMaintenance&&requests.Phase==GuestRequestPhase.Ringing,"electrical call");Mark("observe-fault");
                yield return Wait(()=>Marked("observer-fault-checked"),"fault observer",45);Mark("repair-unanswered");
                yield return Wait(()=>!rooms[0].State.Faulted,"unanswered fault repaired",60);
                Require(requests.Phase==GuestRequestPhase.Completed,"Unanswered repaired call stuck");
                yield return Wait(()=>Marked("unanswered-checked"),"client observes repaired call",40);
                yield return new WaitForSecondsRealtime(2.5f);Require(shift.TryGenerateFault(rooms[0]),"Relocation fault trigger");
                yield return Wait(()=>requests.State.Kind==GuestRequestKind.ElectricalMaintenance&&requests.Phase==GuestRequestPhase.Ringing,"relocation maintenance call");
                computer.CloseLocal();yield return Wait(()=>!computer.LocalOpen,"release for client relocation");
                yield return Walk(new Vector3(12.8f,0,8.4f));yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(rooms[0].DoorOutside.position);
                Mark("holding-exit");
                yield return Wait(()=>rooms[1].State.Reserved,"client reservation",180);
                var resident=rooms[0].Occupant;
                if(resident!=null)reception.RequestRelocateRpc(0,1,rooms[0].State.Cycle,rooms[1].State.Cycle,rooms[0].State.FaultId,new NetworkObjectReference(resident.NetworkObject),resident.State.Visit,computer.Lease);
                yield return Wait(()=>Marked("observer-relocation-checked"),"relocation observer",45);
                yield return Walk(new Vector3(22.4f,0,15.1f));yield return new WaitForSecondsRealtime(6);yield return BackToLobby();Mark("exit-cleared");
                yield return Wait(()=>Marked("relocation-checked"),"client verifies relocation",90);
                yield return Breaker(0);yield return Collect(0);yield return PickBag(0);yield return DisposeBag();Mark("source-restored");
                checks.Add("Shared collection; disconnect bag recovery; empty-room check-in refusal; occupied outage; source turnover after physical relocation.");
                yield return ToComputer();yield return OpenComputer();
                yield return Operate();
            }
            else
            {
                yield return Wait(()=>Marked("bag-created"),"host collects first room");
                yield return TakeAvailableBag(0);Require(Carrier.VisibleBag,"Local carried bag visual");Mark("observe-bag");
                yield return Wait(()=>Marked("disconnect-bag"),"disconnect after observer");
                joining=true;session.Disconnect();yield return Wait(()=>!session.Manager.IsListening&&!session.Manager.ShutdownInProgress,"client disconnect");
                yield return Wait(()=>Marked("bag-recovered"),"server recovers bag");yield return Connect();
                yield return TakeAvailableBag(0);yield return DisposeBag();
                yield return Clean(1);yield return Collect(2);yield return BackToLobby();Mark("preparation-done");
                yield return Wait(()=>Marked("empty-fault-checked"),"fault check-in rejection");
                uint id=rooms[1].State.FaultId;rooms[1].ResetBreakerRpc(id);yield return new WaitForSecondsRealtime(.3f);Require(rooms[1].State.Faulted,"Remote breaker accepted");
                yield return Breaker(1);yield return Wait(()=>Marked("guest-in-room"),"guest ready for towel");
                yield return Wait(()=>requests.Phase==GuestRequestPhase.Ringing&&requests.State.Kind==GuestRequestKind.CleanTowel,"towel call",40);
                yield return Phone();Use(requests.Phone,requests.Phone.AimPoint);yield return Wait(()=>requests.Phase==GuestRequestPhase.PendingDelivery,"answer towel");
                yield return Supply();Use(requests.TowelSupply,requests.TowelSupply.AimPoint);yield return Wait(()=>Carrier.HasTowel,"take towel");yield return FromSupply();
                yield return EnterRoom(2);yield return Walk(Map(new Vector3(20f,0,14.4f),2));
                Require(!rooms[2].BagPickup.CanInteract&&rooms[2].BagPickup.Prompt.Contains("Clean Towel"),"Carry conflict prompt");
                rooms[2].PickUpBagRpc(rooms[2].State.Cycle);yield return new WaitForSecondsRealtime(.4f);
                Require(Carrier.HasTowel&&rooms[2].State.Bag==GarbagePhase.AwaitingPickup,"Bag replaced towel");yield return LeaveRoom(2);
                yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(new Vector3(22.2f,0,rooms[0].Delivery.AimPoint.z));
                Use(rooms[0].Delivery,rooms[0].Delivery.AimPoint);yield return Wait(()=>!Carrier.HasTowel,"towel delivered");yield return BackToLobby();Mark("towel-conflict-tested");
                yield return TakeAvailableBag(2);yield return DisposeBag();
                yield return Wait(()=>Marked("repair-unanswered"),"repair unanswered electrical request");yield return Breaker(0);
                Require(requests.Phase==GuestRequestPhase.Completed,"Repair before answer did not resolve call");Mark("unanswered-checked");
                yield return Wait(()=>Marked("holding-exit"),"host holds guest exit");yield return ToComputer();yield return OpenComputer();
                var guest=rooms[0].Occupant;double deadline=guest.State.StayEndsAt;
                yield return Wait(()=>session.Manager.ServerTime.Time>=deadline-6,"relocate near original checkout deadline",180);
                Require(guest.IsInRoomFor(rooms[0]),"Guest already returning before relocation");uint sourceCycle=rooms[0].State.Cycle;uint targetCycle=rooms[1].State.Cycle;
                for(int i=0;i<8;i++)reception.RequestRelocateRpc(0,1,sourceCycle,targetCycle,rooms[0].State.FaultId,new NetworkObjectReference(guest.NetworkObject),guest.State.Visit,computer.Lease);
                yield return Wait(()=>rooms[1].State.Reserved,"destination reserved");
                Require(!rooms[1].AvailableForCheckIn,"Reserved room available");
                if(reception.WaitingNpc!=null)reception.RequestCheckInRpc(1,targetCycle,new NetworkObjectReference(reception.WaitingNpc.NetworkObject),reception.WaitingNpc.State.Visit,computer.Lease);
                reception.RequestCheckoutRpc(0,sourceCycle,new NetworkObjectReference(guest.NetworkObject),guest.State.Visit,computer.Lease);
                yield return new WaitForSecondsRealtime(.4f);Require(guest.Phase==GuestPhase.Relocating&&rooms[1].State.Reserved,"Concurrent requests broke relocation");Mark("observe-relocation");
                yield return Wait(()=>Marked("exit-cleared"),"host clears route");
                yield return Wait(()=>guest.Room==rooms[1]&&guest.Phase!=GuestPhase.Relocating,"physical move completed",60);
                Require(guest.State.StayEndsAt==deadline&&shift.State.Relocations==1&&rooms[0].State.Status==RoomStatus.Dirty&&rooms[0].State.Faulted&&!rooms[1].State.Reserved,"Relocation states/deadline");
                Require(requests.Phase==GuestRequestPhase.NoRequest&&shift.State.CheckedIn==1&&shift.State.CheckedOut==0,"Relocation request/counters");
                float checkoutDeadline=Time.realtimeSinceStartup+90;
                while(reception.CheckoutNpc!=guest)
                {
                    Require(Time.realtimeSinceStartup<checkoutDeadline,"Overdue guest did not reach checkout");
                    if(reception.WaitingNpc!=null&&rooms[2].AvailableForCheckIn)reception.SelectRoom(2);
                    yield return new WaitForSecondsRealtime(.2f);
                }
                reception.Checkout(1);
                yield return Wait(()=>rooms[1].State.Status==RoomStatus.Dirty,"checkout relocated guest");
                Require(shift.State.CheckedOut==1,"Duplicate checkout credit");computer.CloseLocal();yield return Wait(()=>!computer.LocalOpen,"leave computer");Mark("relocation-checked");
                checks.Add("Towel/bag conflict; before-answer repair; duplicate relocation; destination check-in and checkout conflict; late join; original deadline expired during real walk; exactly one checkout.");
                yield return Wait(()=>Marked("source-restored"),"source repaired and cleaned",120);
                yield return Work();
            }
        }
        IEnumerator Operate()
        {
            if(!computer.LocalOpen){yield return ToComputer();yield return OpenComputer();}
            while(shift.IsRunning)
            {
                if(NightMode)Configure(requests,"televisionChance",shift.State.Repairs==0?1f:shift.State.Towels==0?0f:.5f);
                var source=rooms.FirstOrDefault(r=>r.CanRelocate);var target=rooms.FirstOrDefault(r=>r.AvailableForCheckIn);
                if(source!=null&&target!=null&&shift.State.Relocations==0)
                    reception.Relocate(Array.IndexOf(rooms,source),Array.IndexOf(rooms,target));
                else if(reception.CheckoutNpc!=null)reception.Checkout(Array.IndexOf(rooms,reception.CheckoutNpc.Room));
                else if(reception.WaitingNpc!=null&&target!=null)reception.SelectRoom(Array.IndexOf(rooms,target));
                yield return new WaitForSecondsRealtime(.2f);
            }
        }
        IEnumerator Work()
        {
            while(shift.IsRunning)
            {
                var fault=rooms.FirstOrDefault(r=>r.State.Faulted);
                if(fault!=null)
                {
                    // Give the operator a short chance to choose relocation; do not fix under its click.
                    if(fault.CanRelocate&&rooms.Any(r=>r.AvailableForCheckIn)&&shift.State.Relocations==0)yield return new WaitForSecondsRealtime(3);
                    yield return Breaker(Array.IndexOf(rooms,fault));continue;
                }
                if(requests.Phase==GuestRequestPhase.Ringing)
                {yield return Phone();if(requests.Phone.CanInteract)Use(requests.Phone,requests.Phone.AimPoint);yield return new WaitForSecondsRealtime(.3f);}
                if(requests.Phase==GuestRequestPhase.PendingDelivery)
                {
                    var room=requests.Room;int i=Array.IndexOf(rooms,room);uint id=requests.State.RequestId;
                    if(requests.State.Kind==GuestRequestKind.CleanTowel)
                    {
                        yield return Supply();if(requests.TowelSupply.CanInteract)Use(requests.TowelSupply,requests.TowelSupply.AimPoint);yield return FromSupply();
                        yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(new Vector3(22.2f,0,room.Delivery.AimPoint.z));
                        if(room.Delivery.CanInteract)Use(room.Delivery,room.Delivery.AimPoint);yield return new WaitForSecondsRealtime(.3f);yield return BackToLobby();
                    }
                    else if(requests.State.Kind==GuestRequestKind.RepairTelevision)
                    {
                        yield return EnterRoom(i);yield return Walk(Map(new Vector3(20,0,14.4f),i));
                        if(room.Television.Controls[0].CanInteract)Use(room.Television.Controls[0],room.Television.Controls[0].AimPoint);
                        yield return new WaitForSecondsRealtime(.4f);
                        if(room.Television.Controls[1].CanInteract)Use(room.Television.Controls[1],room.Television.Controls[1].AimPoint);
                        yield return new WaitForSecondsRealtime(.4f);yield return LeaveRoom(i);
                    }
                    else yield return new WaitForSecondsRealtime(.2f);
                }
                else
                {
                    int dirty=Array.FindIndex(rooms,r=>r.State.Status==RoomStatus.Dirty);
                    if(dirty>=0)yield return Clean(dirty);else yield return new WaitForSecondsRealtime(.5f);
                }
            }
        }
        IEnumerator Collect(int i)
        {
            yield return EnterRoom(i);yield return Walk(Map(new Vector3(18.5f,0,13.2f),i));
            if(rooms[i].Bed.CanInteract)Use(rooms[i].Bed,rooms[i].Bed.AimPoint);
            yield return Walk(Map(new Vector3(18.8f,0,12.65f),i));if(rooms[i].Trash[0].CanInteract)Use(rooms[i].Trash[0],rooms[i].Trash[0].AimPoint);
            yield return Walk(Map(new Vector3(18f,0,12.2f),i));yield return Walk(Map(new Vector3(17.7f,0,12.2f),i));
            if(rooms[i].Trash[1].CanInteract)Use(rooms[i].Trash[1],rooms[i].Trash[1].AimPoint);
            yield return Wait(()=>rooms[i].State.Bag!=GarbagePhase.Collecting||!shift.IsRunning,"filled bag",8);
            yield return Walk(Map(new Vector3(18.1f,0,12.4f),i));
            if(shift.IsRunning)Require(rooms[i].State.Status==RoomStatus.Dirty,"Room Ready before disposal");
            yield return LeaveRoom(i);
        }
        IEnumerator TakeAvailableBag(int i)
        {
            yield return EnterRoom(i);if(i==3)yield return Walk(new Vector3(24.4f,0,19.55f));yield return Walk(Map(new Vector3(20f,0,14.4f),i));
            yield return PickBagAt(i);yield return LeaveRoom(i);
        }
        IEnumerator PickBag(int i) { yield return TakeAvailableBag(i); }
        IEnumerator PickBagAt(int i)
        {
            if(!shift.IsRunning)yield break;
            Use(rooms[i].BagPickup,rooms[i].BagPickup.AimPoint);
            for(int n=0;n<8;n++)rooms[i].PickUpBagRpc(rooms[i].State.Cycle);
            yield return Wait(()=>Carrier.HasBag,"bag picked up",8);
            Require(Carrier.BagRoom==rooms[i]&&rooms[i].State.Bag==GarbagePhase.Carried,"Bag identity");
        }
        IEnumerator Clean(int i)
        {
            if(rooms[i].State.Bag==GarbagePhase.Collecting)yield return Collect(i);
            if(shift.IsRunning&&rooms[i].State.Bag==GarbagePhase.AwaitingPickup) {yield return TakeAvailableBag(i);yield return DisposeBag();}
        }
        IEnumerator DisposeBag()
        {
            if(!Carrier.HasBag)yield break;
            var room=Carrier.BagRoom;uint cycle=Carrier.State.Cycle;var reference=Carrier.State.Room;
            dumpster.DisposeRpc(reference,cycle);yield return new WaitForSecondsRealtime(.3f);Require(Carrier.HasBag,"Remote disposal accepted");
            yield return Walk(new Vector3(9.5f,0,8.4f));yield return Walk(new Vector3(8,0,2));yield return Walk(new Vector3(8,0,-3.1f));yield return Walk(new Vector3(16.8f,0,-3.1f));
            if(shift.IsRunning&&Carrier.HasBag)
            {
                Use(dumpster,dumpster.AimPoint);for(int n=0;n<8;n++)dumpster.DisposeRpc(reference,cycle);
                yield return Wait(()=>!Carrier.HasBag,"disposed",8);
                Require(room.State.Status==RoomStatus.Ready&&room.State.Bag==GarbagePhase.Disposed,"Disposal did not complete room");
            }
            yield return FromDumpster();
        }
        IEnumerator FromDumpster(){yield return Walk(new Vector3(8,0,-3.1f));yield return Walk(new Vector3(8,0,2));yield return Walk(new Vector3(9.5f,0,8.4f));yield return Walk(new Vector3(13.3f,0,8.4f));yield return Walk(new Vector3(13.3f,0,7.5f));}
        IEnumerator Breaker(int i)
        {
            yield return Walk(new Vector3(12.8f,0,8.4f));yield return Walk(new Vector3(12.8f,0,4.6f));yield return Walk(new Vector3(16.8f,0,4.6f));yield return Walk(new Vector3(16.7f,0,3.2f));
            uint id=rooms[i].State.FaultId;
            if(shift.IsRunning&&rooms[i].State.Faulted)
            {
                var wrong=rooms[(i+1)%4];wrong.ResetBreakerRpc(wrong.State.FaultId);rooms[i].ResetBreakerRpc(id-1);
                yield return new WaitForSecondsRealtime(.3f);Require(rooms[i].State.Faulted,"Wrong/stale breaker repaired fault");
                Use(rooms[i].Breaker,rooms[i].Breaker.AimPoint);for(int n=0;n<8;n++)rooms[i].ResetBreakerRpc(id);
                yield return Wait(()=>!rooms[i].State.Faulted,"correct breaker repaired");Require(rooms[i].ControlledLights.All(l=>l.enabled),"Power light restoration");
            }
            yield return Walk(new Vector3(16.8f,0,4.6f));yield return Walk(new Vector3(12.8f,0,4.6f));yield return Walk(new Vector3(12.8f,0,8.4f));yield return Walk(new Vector3(13.3f,0,7.5f));
        }
        IEnumerator OpenComputer()
        {
            if(computer.LocalOpen){yield return Wait(()=>computer.Focused,"focused");yield break;}
            var cam=local.GetComponentInChildren<Camera>(true);var root=local.transform.position;
            Use(computer.ScreenCollider.GetComponent<ComputerInteraction>(),computer.ScreenCollider.bounds.center);
            yield return Wait(()=>computer.Focused&&Vector3.Distance(cam.transform.position,computer.Focus.position)<.02f,"computer focuses after LateUpdate",8);
            Require(interactor.MenuOpen&&local.GetComponent<FirstPersonController>().InputSuspended,"Computer did not suspend input");
            Require(Vector3.Distance(cam.transform.position,computer.Focus.position)<.02f&&Vector3.Distance(local.transform.position,root)<.08f,"Focus transform/root position: camera="+Vector3.Distance(cam.transform.position,computer.Focus.position)+" root="+Vector3.Distance(local.transform.position,root));
        }
        IEnumerator ToComputer()
        {
            if(local.transform.position.x<11.4f){yield return Walk(new Vector3(9.5f,0,8.4f));yield return Walk(new Vector3(13.3f,0,8.4f));}
            yield return Walk(new Vector3(13.3f,0,7.5f));yield return Walk(new Vector3(12.8f,0,role=="host"?4.9f:5.7f));yield return Walk(new Vector3(role=="host"?11.9f:12.1f,0,role=="host"?4.9f:5.7f));
        }
        IEnumerator Phone(){yield return ToComputer();yield return Walk(new Vector3(11.8f,0,5.75f));yield return new WaitForSecondsRealtime(.3f);}
        Vector3 Map(Vector3 p,int i){if(i%2==1)p.x=44.4f-p.x;if(i>=2)p.z+=5.7f;return p;}
        IEnumerator EnterRoom(int i)
        {
            if(local.transform.position.x<11.4f)yield return Walk(new Vector3(9.5f,0,8.4f));
            yield return Walk(new Vector3(12.8f,0,8.4f));yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(rooms[i].DoorOutside.position);
            if(!rooms[i].Door.IsOpen)Use(rooms[i].Door,rooms[i].Door.InteractionPoint);
            yield return Wait(()=>rooms[i].Door.IsOpen&&!rooms[i].Door.IsMoving,"door opens");
            // Upper east doorway is 1.2m north of the lower-row furniture offset.
            // Stay north of its open leaf; occupied visits can reach the TV without closing it.
            if(i==3)
            {
                if(rooms[i].State.Status==RoomStatus.Occupied)
                {yield return Walk(new Vector3(24.4f,0,19.55f));yield return Walk(new Vector3(24.4f,0,20.1f));}
                else
                {
                    yield return Walk(new Vector3(25.5f,0,19.5f));
                    if(rooms[i].Door.IsOpen)Use(rooms[i].Door,rooms[i].Door.InteractionPoint);
                    yield return Wait(()=>!rooms[i].Door.IsOpen&&!rooms[i].Door.IsMoving,"close upper east door from clear side");
                }
                yield break;
            }
            var inner=rooms[i].DoorInside.position;inner.x=Map(new Vector3(20,0,0),i).x;yield return Walk(inner);
            float south=i>=2?13f:12.65f;
            yield return Walk(Map(new Vector3(20,0,south),i));yield return Walk(Map(new Vector3(19,0,south),i));
            if(rooms[i].Door.IsOpen)Use(rooms[i].Door,rooms[i].Door.InteractionPoint);
            yield return Wait(()=>!rooms[i].Door.IsOpen&&!rooms[i].Door.IsMoving,"close door clear of room work");
            yield return Walk(Map(new Vector3(20f,0,south),i));
        }
        IEnumerator LeaveRoom(int i)
        {
            if(i==3)
            {
                if(local.transform.position.z>19.6f)yield return Walk(new Vector3(24.4f,0,19.55f));
                if(!rooms[i].Door.IsOpen)
                {
                    yield return Walk(new Vector3(25.5f,0,19.5f));Use(rooms[i].Door,rooms[i].Door.InteractionPoint);
                    yield return Wait(()=>rooms[i].Door.IsOpen&&!rooms[i].Door.IsMoving,"leave upper east room");
                }
                yield return Walk(new Vector3(24.4f,0,19.55f));yield return Walk(rooms[i].DoorOutside.position);yield return BackToLobby();yield break;
            }
            float south=i>=2?13f:12.65f;
            yield return Walk(Map(new Vector3(20f,0,south),i));yield return Walk(Map(new Vector3(19,0,south),i));
            if(!rooms[i].Door.IsOpen)Use(rooms[i].Door,rooms[i].Door.InteractionPoint);
            yield return Wait(()=>rooms[i].Door.IsOpen&&!rooms[i].Door.IsMoving,"leave room");
            yield return Walk(Map(new Vector3(20,0,south),i));
            var inner=rooms[i].DoorInside.position;inner.x=Map(new Vector3(20,0,0),i).x;yield return Walk(inner);
            yield return Walk(rooms[i].DoorOutside.position);yield return BackToLobby();
        }
        IEnumerator BackToLobby(){yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(new Vector3(13.3f,0,8.4f));yield return Walk(new Vector3(13.3f,0,7.5f));}
        IEnumerator Supply(){yield return Walk(new Vector3(12.8f,0,8.4f));yield return Walk(new Vector3(12.8f,0,4.6f));yield return Walk(new Vector3(16.8f,0,4.6f));yield return Walk(new Vector3(17,0,5.15f));}
        IEnumerator FromSupply(){yield return Walk(new Vector3(16.8f,0,4.6f));yield return Walk(new Vector3(12.8f,0,4.6f));yield return Walk(new Vector3(12.8f,0,8.4f));}
        void Use(IInteractable expected,Vector3 p)
        {
            var camera=local.GetComponentInChildren<Camera>(true);camera.transform.LookAt(p);Physics.SyncTransforms();var found=interactor.FindTarget();
            Require(found==expected,"Ray miss expected="+expected+" found="+found+" at="+local.transform.position+" aim="+p+" hits="+string.Join(",",Physics.RaycastAll(camera.transform.position,camera.transform.forward,2.5f).OrderBy(h=>h.distance).Select(h=>h.collider.name+":"+h.distance)));
            Require(interactor.TryInteract(),"Interaction unavailable");
        }
        IEnumerator Walk(Vector3 target)
        {
            File.WriteAllText(Path.Combine(directory,role+".progress"),"Walk "+target); float end=Time.realtimeSinceStartup+30;var cc=local.GetComponent<CharacterController>();
            float progressedAt=Time.realtimeSinceStartup;Vector3 progressPosition=local.transform.position;int yields=0;
            while(true)
            {
                Vector3 d=target-local.transform.position;d.y=0;if(d.magnitude<.07f)break;
                Require(Time.realtimeSinceStartup<end,"Walk blocked toward "+target+" at "+local.transform.position);
                if(Vector3.Distance(local.transform.position,progressPosition)>.1f)
                {progressPosition=local.transform.position;progressedAt=Time.realtimeSinceStartup;}
                // At the lobby/corridor junction, a scripted actor must yield to crossing
                // guests like a human would. Walk back into the open lobby, never teleport.
                bool junction=local.transform.position.x>11.5f&&local.transform.position.x<14.2f&&local.transform.position.z>7.5f&&local.transform.position.z<9.5f;
                if(junction&&Time.realtimeSinceStartup-progressedAt>1.5f&&yields++<2)
                {
                    float asideUntil=Time.realtimeSinceStartup+.6f;
                    while(Time.realtimeSinceStartup<asideUntil)
                    {cc.Move(Vector3.back*2*Time.deltaTime+Vector3.down*2*Time.deltaTime);yield return null;}
                    yield return new WaitForSecondsRealtime(1);
                    progressPosition=local.transform.position;progressedAt=Time.realtimeSinceStartup;
                    continue;
                }
                cc.Move(Vector3.ClampMagnitude(d,4*Time.deltaTime)+Vector3.down*2*Time.deltaTime);yield return null;
            }
            yield return new WaitForSecondsRealtime(.2f);
        }
        void Finish(bool pass,string reason)
        {File.WriteAllText(Path.Combine(directory,role+".txt"),(pass?"PASS ":"FAIL ")+role+"\n"+string.Join("\n",checks)+"\n"+reason+"\nNPC distance: "+distance+"m; max queue="+maximumQueue+"\n"+string.Join("\n",errors));Application.Quit(pass?0:1);}
        void OnDestroy()=>Application.logMessageReceived-=Log;
    }
}
#endif
