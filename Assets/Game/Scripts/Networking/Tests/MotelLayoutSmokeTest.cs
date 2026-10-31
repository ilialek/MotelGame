#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using MotelGame.Service;
using MotelGame.Interactions;
using MotelGame.Inspection;
using MotelGame.Horror;

namespace MotelGame.Networking.Tests
{
    // Explicit development-only command-line actors. They walk colliding owner controllers and
    // use the real raycast/RPC pipeline. Underground hunt safety cases additionally use explicit
    // controlled positioning fixtures; underground route tests always traverse physical geometry.
    [DefaultExecutionOrder(10000)] // After MimicHunt (250), including its LateUpdate movement.
    public sealed partial class MotelLayoutSmokeTest : MonoBehaviour
    {
        string role,mode,dir;bool Host=>role=="host";
        DevelopmentSession session;NetworkObject own;PlayerInteractor user;Camera view;
        BaggageInspection scanner;ReceptionComputer computer,cctv;ReceptionDesk reception;
        NightShiftController shift;GuestServiceRequests requests;MimicHunt hunt;
        readonly List<string> checks=new(),errors=new();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            var role=OnlineIdentity.Argument("-motel-layout-test");if(role==null)return;
            var t=new GameObject("V4ValidationActor").AddComponent<MotelLayoutSmokeTest>();t.role=role;t.mode=OnlineIdentity.Argument("-motel-layout-mode")??"routes";t.dir=OnlineIdentity.Argument("-motel-layout-results");
        }
        void Awake()=>Application.logMessageReceived+=Log;
        void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(text);}
        void Need(bool condition,string why){if(!condition)throw new Exception(why);}
        void Progress(string text)=>File.WriteAllText(Path.Combine(dir,role+".progress"),text);
        void Mark(string name)=>File.WriteAllText(Path.Combine(dir,name+".signal"),"ready");
        bool Has(string name)=>File.Exists(Path.Combine(dir,name+".signal"));
        IEnumerator Wait(Func<bool> condition,string step,float seconds=100)
        {Progress(step);float end=Time.realtimeSinceStartup+seconds;while(!condition()){Need(Time.realtimeSinceStartup<end,"Timeout "+step+" pos="+(own!=null?own.transform.position.ToString():"none")+" feedback="+(user!=null?user.Feedback:"")+" hunt="+(hunt!=null?hunt.State.Phase.ToString():""));yield return null;}}
        IEnumerator Barrier(string name){Mark(name+"-"+role);yield return Wait(()=>Has(name+"-host")&&Has(name+"-client"),name,600);}
        IEnumerator Start()
        {
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0)
            {
                object next=null;bool more=false;Exception failure=null;
                try{more=stack.Peek().MoveNext();if(more)next=stack.Peek().Current;}catch(Exception e){failure=e;}
                if(failure!=null){Finish(false,failure.ToString());yield break;}
                if(!more){stack.Pop();continue;}if(next is IEnumerator sub)stack.Push(sub);else yield return next;
            }
            Finish(errors.Count==0,"Completed with "+errors.Count+" critical runtime logs.");
        }
        // Observe after movement and network interpolation; Update order otherwise compares
        // the previous frame's large movement with this frame's short elapsed time after a hitch.
        void LateUpdate(){if(mode!=null&&mode.StartsWith("underground"))ObserveUnderground();ObserveSlug();}
        float nextState;
        void Update()
        {
            if(mode==null||!mode.StartsWith("underground"))ObserveHunt();
            if(reception==null||own==null||Time.realtimeSinceStartup<nextState)return;nextState=Time.realtimeSinceStartup+2;
            File.WriteAllText(Path.Combine(dir,role+".state"),"Player "+own.transform.position+"\n"+string.Join("\n",reception.Guests.Select(g=>g.name+" "+g.Phase+" at "+g.transform.position+" blocked="+g.State.Blocked+" car="+(g.GetComponent<MotelGame.Registration.GuestVehicleJourney>()?.State.Phase.ToString()??"none")+" carBlocked="+(g.GetComponent<MotelGame.Registration.GuestVehicleJourney>()?.State.Blocked.ToString()??"none")))+"\n"+string.Join("\n",reception.Rooms.Select(r=>r.RoomNumber+" "+r.State.Status+" cycle="+r.State.Cycle+" bag="+r.State.Bag))+"\nMimic "+hunt.State.Phase+" node="+hunt.State.Node+" HP="+hunt.State.Health+" position="+hunt.transform.position+"\nRequest "+requests.Room?.RoomNumber+" "+requests.Phase+" "+requests.State.Kind);
        }
        IEnumerator Run()
        {
            yield return null;Directory.CreateDirectory(dir);
            registration=FindFirstObjectByType<MotelGame.Registration.RegistrationDesk>();scanner=FindFirstObjectByType<BaggageInspection>();
            reception=FindFirstObjectByType<ReceptionDesk>();computer=FindObjectsByType<ReceptionComputer>(FindObjectsSortMode.None).Single(c=>!c.SurveillanceOnly);shift=FindFirstObjectByType<NightShiftController>();hunt=FindFirstObjectByType<MimicHunt>();
            cctv=FindObjectsByType<ReceptionComputer>(FindObjectsSortMode.None).Single(c=>c.SurveillanceOnly);requests=FindFirstObjectByType<GuestServiceRequests>();
            session=FindFirstObjectByType<DevelopmentSession>();session.TransportSettings.Port=(ushort)(19050+(mode=="underground-hunt"?9:mode=="registration"?7:mode=="reveal"?8:mode=="solo"?5:mode=="inspection"?6:mode=="service"?1:mode=="hunt"?2:mode=="failure"?3:mode=="refusal"?4:0));session.TransportSettings.Address="127.0.0.1";
            bool relay=Environment.GetCommandLineArgs().Contains("-motel-online");var online=FindFirstObjectByType<OnlineSessionController>();
            if(relay)
            {
                yield return Wait(()=>!online.Busy,"Unity authentication",60);Need(online.Authenticated,"Authentication: "+online.Error);
                if(Host)
                {
                    var task=online.CreateLobbyAsync();yield return Wait(()=>task.IsCompleted,"create private Relay lobby",60);Need(online.Session!=null,"Relay host: "+online.Error);
                    File.WriteAllText(Path.Combine(dir,"relay-code.txt"),online.Session.Code);
                    yield return Wait(()=>session.Manager.ConnectedClients.Count>=2&&online.CanStartGame,"two Relay clients ready",100);Need(online.StartGame(),"Start online game");
                }
                else
                {
                    yield return Wait(()=>File.Exists(Path.Combine(dir,"relay-code.txt")),"private invite",100);
                    var task=online.JoinLobbyAsync(File.ReadAllText(Path.Combine(dir,"relay-code.txt")));yield return Wait(()=>task.IsCompleted,"join private Relay lobby",60);Need(online.Session!=null,"Relay join: "+online.Error);
                }
                Need(session.Manager.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>().Protocol==Unity.Netcode.Transports.UTP.UnityTransport.ProtocolType.RelayUnityTransport,"Not using Relay");checks.Add("Real private lobby and Unity Relay transport.");
            }
            else Need(session.StartSession(Host),"Session startup");
            yield return Wait(()=>session.Manager.LocalClient?.PlayerObject!=null,"owned player");
            own=session.Manager.LocalClient.PlayerObject;user=own.GetComponent<PlayerInteractor>();view=own.GetComponentInChildren<Camera>();
            if(mode=="underground-routes"){yield return UndergroundRoutes();yield break;}
            if(mode=="underground-hunt"){yield return UndergroundHunt();yield break;}
            if(mode=="solo"){yield return SoloShift();yield break;}
            if(mode=="inspection"){yield return InspectionTeam();yield break;}
            if(role=="late"&&mode=="vehicles"){yield return VehicleLate();yield break;}
            if(role=="late"&&(mode=="registration"||mode=="reveal")){yield return RegistrationLate();yield break;}
            if(role=="late")
            {
                if(mode=="hunt"||mode=="failure")yield return Wait(()=>hunt.State.Active,"late active threat");else yield return Wait(()=>reception.Rooms.All(r=>r.IsSpawned&&r.State.Status==RoomStatus.Ready),"late six ready rooms");
                yield return Wait(()=>File.Exists(Path.Combine(dir,"late-doors.txt"))&&DoorSnapshot()==File.ReadAllText(Path.Combine(dir,"late-doors.txt")),"late synchronized door poses");
                Need(reception.Guests.All(g=>!g.Agent.enabled),"Remote NPC navigation enabled");checks.Add("Late join: current six-room/door/threat state and server-only NPC simulation restored.");Mark("late-checked");yield break;
            }
            yield return Barrier("connected");
            var players=FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);Need(players.Length==2,"Two spawned players");
            foreach(var p in players){Need(p.GetComponent<FirstPersonController>().enabled==p.IsOwner,"Movement ownership");Need(p.GetComponentInChildren<Camera>(true).enabled==p.IsOwner,"Camera ownership");Need(p.BodyVisible!=p.IsOwner,"Remote representation");}
            checks.Add("Two real NGO instances: independent owner input/view and visible remote body.");
            if(mode=="vehicles")yield return Vehicles();else if(mode=="registration")yield return RegistrationCases();else if(mode=="reveal")yield return RegistrationReveal();else if(mode=="routes")yield return Routes();else if(mode=="service")yield return Service();else if(mode=="refusal")yield return Refusal();else yield return Hunt();
            yield return Barrier("done");
            if(!Host){if(relay){var task=online.LeaveLobbyAsync();yield return Wait(()=>task.IsCompleted,"leave Relay client");}else session.Disconnect();yield return Wait(()=>!session.Manager.IsListening,"client disconnect");Mark("disconnected");}
            else{yield return Wait(()=>Has("disconnected"),"disconnect signal");yield return Wait(()=>session.Manager.ConnectedClients.Count==1,"server cleanup");Need(!cctv.Occupied,"Disconnected workstation lease not released");if(relay){var task=online.LeaveLobbyAsync();yield return Wait(()=>task.IsCompleted,"leave Relay host");}else session.Disconnect();}
            checks.Add("Client disconnect and host cleanup completed.");
        }
        string DoorSnapshot()=>string.Join("\n",FindObjectsByType<NetworkDoor>(FindObjectsSortMode.None).OrderBy(d=>d.name).Select(d=>d.name+"="+d.IsOpen+"/"+d.IsSpawned));
        void RecordLateDoors()=>File.WriteAllText(Path.Combine(dir,"late-doors.txt"),DoorSnapshot());
        IEnumerator Routes()
        {
            if(Host)Need(shift.DebugStartScenario(3600,0,16),"Start controlled preparation shift");
            yield return Wait(()=>shift.IsRunning,"shift start");
            yield return OpenComputer(Host?computer:cctv);yield return Barrier("both-workstations");
            Need(computer.Occupied&&cctv.Occupied&&computer.User!=cctv.User,"Independent registration and CCTV leases");
            if(!Host){var feeds=FindFirstObjectByType<VentSurveillance>();for(int i=0;i<3;i++){feeds.Select(i);yield return new WaitForSecondsRealtime(.1f);}}
            if(!Application.isBatchMode){ScreenCapture.CaptureScreenshot(Path.Combine(dir,role+"-workstation.png"));yield return new WaitForSecondsRealtime(.4f);}
            (Host?computer:cctv).CloseLocal();yield return Wait(()=>!computer.LocalOpen&&!cctv.LocalOpen,"leave workstation");
            checks.Add("Registration and separate CCTV station occupied simultaneously by different players; three feeds selectable.");
            foreach(int i in Host?new[]{0,1,2}:new[]{5,4,3})
            {
                var room=reception.Rooms[i];yield return Walk(room.DoorOutside.position,1);yield return Walk(room.DoorInside.position,1|(1<<room.NavigationArea));
                yield return Clean(room);
                var d=room.CourtyardDoor;var center=d.transform.TransformPoint(new Vector3(0,0,-.51f));
                yield return Walk(center-d.transform.right*.95f);yield return Walk(center+d.transform.right*.95f);
                checks.Add("Room "+room.RoomNumber+": both entrances traversed, bed/trash cleared, filled bag carried out through courtyard.");
                var dumpster=FindFirstObjectByType<GarbageDumpster>();yield return Approach(dumpster.TargetCollider);Use(dumpster,dumpster.AimPoint);
                yield return Wait(()=>room.State.Bag==GarbagePhase.Disposed&&room.State.Status==RoomStatus.Ready,"room ready "+room.RoomNumber);
            }
            yield return Barrier("all-ready");yield return Wait(()=>reception.Rooms.All(r=>r.State.Status==RoomStatus.Ready),"Six room readiness synchronized");
            yield return Walk(new Vector3(21.7f,0,23.6f));
            // Both maintenance exits and the direct storage-to-lobby door are exercised through real colliders.
            yield return Walk(new Vector3(25,0,23.2f));yield return Walk(new Vector3(26,0,29));
            yield return Walk(new Vector3(22.8f,0,-1));yield return Walk(new Vector3(27,0,-1));yield return Walk(new Vector3(27,0,-5.5f));
            checks.Add("Maintenance through-route, backyard, exterior Storage entrance and direct Lobby entrance walked.");
            if(Host){RecordLateDoors();Mark("late");}yield return Wait(()=>Has("late-checked"),"late observer",120);yield return Barrier("late-observed");
        }
        IEnumerator Clean(ServiceRoom room)
        {
            yield return Approach(room.Bed.TargetCollider);Use(room.Bed,room.Bed.AimPoint);room.RequestCleanRpc(-1,room.State.Cycle);
            yield return Wait(()=>room.State.BedMade,"bed made");
            foreach(var trash in room.Trash){yield return Approach(trash.TargetCollider);Use(trash,trash.AimPoint);}
            yield return Wait(()=>room.State.Bag==GarbagePhase.AwaitingPickup&&room.BagPickup.TargetCollider.enabled,"filled bag and pickup collider");
            yield return Approach(room.BagPickup.TargetCollider);Use(room.BagPickup,room.BagPickup.AimPoint);
            yield return Wait(()=>own.GetComponent<TowelCarrier>().HasBag,"carrying room bag");
        }
        IEnumerator Refusal()
        {
            if(Host)
            {
                Field(requests,"callDelay",100000f);Field(shift,"electricalFaultCount",Vector2Int.zero);
                Need(shift.DebugStartScenario(600,1,3),"Start carrier refusal fixture");
                Field(scanner,"deck",((BaggageCase[])Get(scanner,"cases")).Where(c=>c.containsMimic).Take(1).ToArray());
                foreach(var r in reception.Rooms)r.PrepareForHuntDebug();
            }
            yield return Wait(()=>shift.IsRunning,"refusal shift");
            if(Host)yield return OpenComputer(computer);else yield return OpenScanner();
            yield return Barrier("refusal-stations");yield return Wait(()=>reception.WaitingNpc!=null,"carrier arrived");
            var guest=reception.WaitingNpc;uint visit=guest.State.Visit;
            if(!Host)
            {
                scanner.PlaceBagRpc(new NetworkObjectReference(guest.NetworkObject),visit);
                yield return Wait(()=>scanner.Current!=null&&scanner.Current.Guest==guest&&scanner.Current.State.Phase==BaggagePhase.OnAnalyzer,"refused carrier bag on tray");
                foreach(var test in AnalyzerPanel.PrimaryTests)yield return Measure(test);
                foreach(BaggageQuestion q in Enum.GetValues(typeof(BaggageQuestion)))
                {scanner.AskRpc(new NetworkObjectReference(guest.NetworkObject),visit,q,scanner.Seat.Lease,false);yield return new WaitForSecondsRealtime(.2f);}
                yield return Wait(()=>scanner.Statements.Count==5,"five shared answers / follow-up");Mark("refusal-evidence");
            }
            yield return Wait(()=>Has("refusal-evidence")&&scanner.Statements.Count==5&&scanner.Knows(AnalyzerTest.Pulse),"complete shared inspection");
            if(Host)
            {
                reception.RequestRefuseRpc(new NetworkObjectReference(guest.NetworkObject),visit,computer.Lease);
                reception.RequestRefuseRpc(new NetworkObjectReference(guest.NetworkObject),visit,computer.Lease);
            }
            yield return Wait(()=>guest.Phase==GuestPhase.Leaving,"carrier refused");
            if(Host)computer.CloseLocal();else scanner.GetComponent<AnalyzerPanel>().CloseLocal();
            yield return Wait(()=>guest.Phase==GuestPhase.Absent,"refused carrier physically left motel",90);
            yield return new WaitForSecondsRealtime(5);
            Need(!hunt.State.Active&&shift.State.CheckedIn==0&&reception.Rooms.All(r=>r.State.Status==RoomStatus.Ready),"Refusal caused admission or threat");
            checks.Add("Three primary tests and five answers replicated; duplicate refusal applied once; carrier departed with no admission or infiltration.");
        }

        IEnumerator Service()
        {
            if(Host)
            {
                // Accelerated deterministic fixture: six ordinary bags, no random electrical interruptions.
                // All decisions, cleaning, delivery and repair still go through the normal player RPCs.
                Field(requests,"callDelay",100000f);Field(requests,"betweenRequestsDelay",.1f);Field(requests,"timingVariation",0f);
                Field(shift,"electricalFaultCount",Vector2Int.zero);Need(shift.DebugStartScenario(2400,6,3),"Service shift startup");
                foreach(var guest in reception.Guests)Field(guest,"stayDuration",900f);
                if(registration!=null)Field(registration,"agenda",new Queue<MotelGame.Registration.GuestCase>(Enumerable.Range(0,6).Select(i=>registration.Cases.Where(c=>!c.disguisedMimic).ToArray()[i%4])));else{var cases=(BaggageCase[])Get(scanner,"cases");Field(scanner,"deck",cases.Where(c=>!c.containsMimic).ToArray());}
                foreach(var r in reception.Rooms)r.PrepareForHuntDebug();
            }
            yield return Wait(()=>shift.IsRunning,"service shift");
            yield return Register(0,6,"initial");
            yield return Wait(()=>reception.Rooms.All(r=>r.Occupant!=null&&r.Occupant.Phase==GuestPhase.InRoom),"all six NPCs physically in rooms",180);
            if(Host)foreach(var g in reception.Guests.Where(g=>g.Phase==GuestPhase.InRoom))g.ExtendDebugStay(session.Manager.ServerTime.Time+2000);
            checks.Add("Six guests registered through shared registration + computer and walked to their own corridor entrances, including 105 and 106.");
            for(int i=0;i<6;i++)yield return Request(reception.Rooms[i],i%2==0?GuestRequestKind.CleanTowel:GuestRequestKind.RepairTelevision,"request-"+i);
            var faultRoom=reception.Rooms[4];
            if(Host){Need(shift.TryGenerateFault(faultRoom),"Generate controlled power fault 105");Field(requests,"nextSelection",0d);}
            yield return Wait(()=>requests.Room==faultRoom&&requests.Phase==GuestRequestPhase.Ringing,"105 electrical report");
            if(Host){yield return Approach(requests.Phone.TargetCollider);Use(requests.Phone,requests.Phone.AimPoint);}
            yield return Wait(()=>requests.Phase==GuestRequestPhase.PendingDelivery,"electrical call accepted");
            if(!Host){yield return Approach(faultRoom.Breaker.TargetCollider);Use(faultRoom.Breaker,faultRoom.Breaker.AimPoint);}
            yield return Wait(()=>!faultRoom.State.Faulted&&shift.State.ElectricalRepairs==1,"Maintenance breaker 105 repaired");
            checks.Add("Room 105 power failure, real phone answer, correct breaker repair in Maintenance, synchronized restoration.");
            for(int i=0;i<6;i++)yield return Turnover(i,"turnover-"+i);
            if(Host){RecordLateDoors();Mark("late");}yield return Wait(()=>Has("late-checked"),"late six prepared rooms",120);yield return Barrier("late-six");
            if(Host)
            {
                ((List<double>)Get(shift,"arrivalTimes")).Add(session.Manager.ServerTime.Time+.1);
                var v=Snapshot<ShiftSnapshot>(shift);var state=v.Value;state.ArrivalTarget++;v.Value=state;
                if(registration!=null)Field(registration,"agenda",new Queue<MotelGame.Registration.GuestCase>(Enumerable.Range(0,6).Select(i=>registration.Cases.Where(c=>!c.disguisedMimic).ToArray()[i%4])));
            }
            yield return Register(0,1,"repeat");yield return Wait(()=>reception.Rooms[0].Occupant!=null&&reception.Rooms[0].Occupant.Phase==GuestPhase.InRoom,"repeat guest in 101",120);
            yield return Request(reception.Rooms[0],GuestRequestKind.RepairTelevision,"repeat-tv");
            yield return Turnover(0,"repeat-turnover");
            Need(shift.State.CheckedIn==7&&shift.State.CheckedOut==7&&shift.State.TrashDisposed==7,"Repeatable service counters");
            checks.Add("Seven check-ins, seven checkouts, seven complete clean/dispose cycles. Room 101 completed two cycles without scene/session restart.");
            if(Host)shift.EndForHuntDebug();yield return Wait(()=>shift.State.Phase==ShiftPhase.Finished,"shift summary");
            Need(reception.Rooms.All(r=>r.State.Status==RoomStatus.Dirty&&!r.State.BedMade&&r.State.Bag==GarbagePhase.Collecting),"Finish/reset dirty visuals");
            if(Host){yield return OpenComputer(computer);shift.StartShiftRpc(shift.State.Number,computer.Lease);}
            yield return Wait(()=>shift.IsRunning,"ordinary next shift");
            Need(reception.Guests.All(g=>g.Phase==GuestPhase.Absent)&&!hunt.State.Active&&(scanner==null||!scanner.Seat.Occupied),"Next shift reset");
            if(Host){computer.CloseLocal();yield return Wait(()=>!computer.LocalOpen,"release restart computer");}
            checks.Add("Shift summary and normal next-shift RPC reset guests, registration, threat, room cleaning and workstation lease.");
        }
        static object Get(object obj,string name)=>obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);
        static NetworkVariable<T> Snapshot<T>(object obj) where T:unmanaged=> (NetworkVariable<T>)Get(obj,"snapshot");
        IEnumerator OpenScanner()
        {
            // Approach the physical control face; the CRT housing correctly blocks side access.
            var stand=scanner.Controls.bounds.center-scanner.Controls.transform.forward*1.65f;stand.y=0;
            yield return Walk(stand);Use(scanner,scanner.Controls.bounds.center);
            yield return Wait(()=>scanner.OwnsControls&&scanner.GetComponent<AnalyzerPanel>().LocalOpen,"scanner lease");
        }
        IEnumerator Measure(AnalyzerTest test)
        {uint rev=scanner.State.Revision;scanner.TestRpc(test,rev,scanner.State.Visit,scanner.Seat.Lease);scanner.TestRpc(test,rev,scanner.State.Visit,scanner.Seat.Lease);yield return Wait(()=>scanner.State.Revision>rev&&!scanner.State.Busy,"measurement "+test,15);Need(scanner.Knows(test),"Reading committed");}
        IEnumerator Register(int start,int count,string tag)
        {
            if(registration!=null){yield return RegisterNew(start,count,tag);yield break;}
            if(Host)yield return OpenComputer(computer);else yield return OpenScanner();
            yield return Barrier(tag+"-stations");
            for(int i=start;i<start+count;i++)
            {
                yield return Wait(()=>reception.WaitingNpc!=null,"waiting guest "+tag+"/"+i,180);var g=reception.WaitingNpc;uint visit=g.State.Visit;
                if(!Host)
                {
                    scanner.PlaceBagRpc(new NetworkObjectReference(g.NetworkObject),visit);
                    yield return Wait(()=>scanner.Current!=null&&scanner.Current.Guest==g&&scanner.Current.State.Phase==BaggagePhase.OnAnalyzer,"bag physically on tray");
                    yield return Measure(AnalyzerTest.Weight);
                    scanner.AskRpc(new NetworkObjectReference(g.NetworkObject),visit,BaggageQuestion.Contents,scanner.Seat.Lease,false);
                    yield return Wait(()=>scanner.CanAdmit(g),"shared admission evidence");Mark(tag+"-scanned-"+i);
                }
                yield return Wait(()=>Has(tag+"-scanned-"+i)&&scanner.CanAdmit(g),"shared scanner record");
                if(Host)
                {
                    var r=reception.Rooms[i];uint cycle=r.State.Cycle;
                    reception.RequestCheckInRpc(i,cycle,new NetworkObjectReference(g.NetworkObject),visit,computer.Lease);
                    reception.RequestCheckInRpc(i,cycle,new NetworkObjectReference(g.NetworkObject),visit,computer.Lease);
                }
                yield return Wait(()=>g.Phase==GuestPhase.WalkingToRoom||g.Phase==GuestPhase.InRoom,"atomic admission");
                Need(g.Room==reception.Rooms[i],"Assigned correct room");
                yield return Barrier(tag+"-assigned-"+i);
            }
            if(Host){computer.CloseLocal();yield return Wait(()=>!computer.LocalOpen,"computer closed");}
            else{scanner.GetComponent<AnalyzerPanel>().CloseLocal();yield return Wait(()=>!scanner.OwnsControls,"scanner released");}
            yield return Barrier(tag+"-closed");
        }
        IEnumerator Request(ServiceRoom room,GuestRequestKind kind,string tag)
        {
            if(Host)
            {
                requests.ResetOnServer(false);Field(requests,"televisionChance",kind==GuestRequestKind.RepairTelevision?1f:0f);Field(requests,"nextSelection",0d);
                var v=Snapshot<RoomSnapshot>(room);var state=v.Value;state.RequestEligibleAt=session.Manager.ServerTime.Time-.1;v.Value=state;
            }
            yield return Wait(()=>requests.Room==room&&requests.Phase==GuestRequestPhase.Ringing,"call "+tag);
            Need(requests.State.Kind==kind,"Correct generated request kind");
            if(Host){yield return Approach(requests.Phone.TargetCollider);Use(requests.Phone,requests.Phone.AimPoint);}
            yield return Wait(()=>requests.Phase==GuestRequestPhase.PendingDelivery,"answered "+tag);
            if(!Host)
            {
                if(kind==GuestRequestKind.CleanTowel)
                {
                    yield return Approach(requests.TowelSupply.TargetCollider);Use(requests.TowelSupply,requests.TowelSupply.AimPoint);
                    yield return Wait(()=>own.GetComponent<TowelCarrier>().HasTowel,"towel carried");
                    yield return Walk(room.DoorOutside.position);yield return Approach(room.Delivery.TargetCollider);Use(room.Delivery,room.Delivery.AimPoint);
                }
                else
                {
                    yield return Walk(room.DoorOutside.position);yield return Walk(room.DoorInside.position);
                    foreach(var control in room.Television.Controls)
                    {yield return Approach(control.TargetCollider);Use(control,control.AimPoint);yield return Wait(()=>requests.State.RepairStep>control.Step,"TV step "+control.Step);}
                    yield return Walk(room.DoorInside.position);yield return Walk(room.DoorOutside.position);
                    yield return Walk(room.DoorOutside.position+Vector3.right*1.4f-room.DoorOutside.forward*1.1f);
                }
            }
            yield return Wait(()=>requests.Phase==GuestRequestPhase.Completed,"completed "+tag,180);
            checks.Add("Room "+room.RoomNumber+": "+kind+" answered and completed through real interactions by two employees.");yield return Barrier(tag+"-complete");
        }
        IEnumerator Turnover(int index,string tag)
        {
            var room=reception.Rooms[index];var g=room.Occupant;Need(g!=null,"Missing checkout occupant");uint cycle=room.State.Cycle;
            if(Host){g.ExtendDebugStay(session.Manager.ServerTime.Time+.1);yield return OpenComputer(computer);}
            yield return Wait(()=>reception.CheckoutNpc==g,"guest physical return to reception "+tag,180);
            yield return Barrier(tag+"-checkout-observed");
            if(Host){reception.RequestCheckoutRpc(index,cycle,new NetworkObjectReference(g.NetworkObject),g.State.Visit,computer.Lease);reception.RequestCheckoutRpc(index,cycle,new NetworkObjectReference(g.NetworkObject),g.State.Visit,computer.Lease);}
            yield return Wait(()=>room.State.Status==RoomStatus.Dirty&&room.State.Cycle>cycle,"checkout resets room");
            Need(!room.State.BedMade&&room.State.RemovedTrash==0&&room.State.Bag==GarbagePhase.Collecting&&!room.State.TowelRequested,"Complete turnover reset");
            if(Host){computer.CloseLocal();yield return Wait(()=>!computer.LocalOpen,"leave checkout computer");}
            else
            {
                yield return Walk(room.DoorOutside.position);yield return Walk(room.DoorInside.position);yield return Clean(room);
                var d=room.CourtyardDoor;var center=d.transform.TransformPoint(new Vector3(0,0,-.51f));yield return Walk(center-d.transform.right*.95f);yield return Walk(center+d.transform.right*.95f);
                var dumpster=FindFirstObjectByType<GarbageDumpster>();yield return Approach(dumpster.TargetCollider);Use(dumpster,dumpster.AimPoint);
            }
            yield return Wait(()=>room.State.Status==RoomStatus.Ready&&g.Phase==GuestPhase.Absent,"ready after physical disposal / departure "+tag,180);
            checks.Add("Room "+room.RoomNumber+": checkout at Reception, departure, bed/trash/bag reset and physical re-preparation.");yield return Barrier(tag+"-done");
        }

        bool sawInfiltration,sawExploration,sawRetreat;float lastMotionAt,ductDistance;Vector3 lastMimic;uint motionRevision;
        void ObserveHunt()
        {
            if(hunt==null||!hunt.IsSpawned||!hunt.State.Active){lastMotionAt=0;return;}
            if(hunt.State.Phase==MimicPhase.Infiltrating)sawInfiltration=true;
            if(hunt.State.Phase==MimicPhase.Exploring)sawExploration=true;
            if(hunt.State.Phase==MimicPhase.Retreating)sawRetreat=true;
            if(Host&&lastMotionAt>0&&motionRevision==hunt.State.Revision)
            {
                float d=Vector3.Distance(lastMimic,hunt.transform.position),dt=Time.realtimeSinceStartup-lastMotionAt;
                if(d>dt*3+.3f)errors.Add("Discontinuous mimic movement "+d+" / "+dt);
                if(hunt.State.Phase==MimicPhase.Exploring||hunt.State.Phase==MimicPhase.MovingThroughDucts)ductDistance+=d;
            }
            lastMimic=hunt.transform.position;lastMotionAt=Time.realtimeSinceStartup;motionRevision=hunt.State.Revision;
        }
        IEnumerator Hunt()
        {
            if(Host)
            {
                Field(requests,"callDelay",100000f);Field(shift,"electricalFaultCount",Vector2Int.zero);HuntScenario.Instance.Begin(HuntTestMode.Complete);
                var arrivals=(List<double>)Get(shift,"arrivalTimes");for(int i=0;i<arrivals.Count;i++)arrivals[i]=session.Manager.ServerTime.Time+1+i*3;
            }
            yield return Wait(()=>shift.IsRunning,"complete baggage/hunt scenario");
            yield return Register(0,6,"hunt-registration");
            yield return Wait(()=>reception.Rooms.All(r=>r.Occupant!=null&&r.Occupant.Phase==GuestPhase.InRoom),"six guests in actual rooms",180);
            var carrier=reception.Rooms[2].Occupant;
            if(Host)foreach(var g in reception.Guests.Where(g=>g.Phase==GuestPhase.InRoom))g.ExtendDebugStay(session.Manager.ServerTime.Time+1200);
            yield return Wait(()=>hunt.State.Active&&sawInfiltration&&sawExploration,"physical baggage release / vent exploration",180);
            Need(carrier.GetComponent<GuestBaggage>().State.Phase==BaggagePhase.Released,"Carrier bag released the creature");
            checks.Add("Actual carrier baggage opened in Room 103; creature crawled to that room's vent and explored the new connected graph.");
            if(Host){yield return OpenComputer(cctv);RecordLateDoors();Mark("late");}
            yield return Wait(()=>Has("late-checked"),"late active-hunt observer",120);
            if(Host)
            {
                var feeds=FindFirstObjectByType<VentSurveillance>();for(int i=0;i<3;i++){feeds.Select(i);yield return new WaitForSecondsRealtime(.3f);CaptureFeed(feeds,"cctv-"+(i+1));}
                cctv.CloseLocal();yield return Wait(()=>!cctv.LocalOpen,"leave CCTV");
            }
            yield return Barrier("observed-hunt");
            if(mode=="failure")
            {
                yield return Walk(Host?new Vector3(23,0,-12):new Vector3(25,0,-12));
                yield return Wait(()=>shift.State.GuestDeaths>0,"unopposed hunt kills a non-carrier",300);
                Need(carrier.Alive&&carrier.Phase!=GuestPhase.Dead,"Protected carrier attacked");
                Need(reception.Guests.Any(g=>g.Phase==GuestPhase.Dead&&!g.Body.enabled),"Dead guest state/physical collision");
                checks.Add("Unopposed hunt produced a synchronized guest death; the admitted carrier remained protected.");
                yield break;
            }
            if(Host)hunt.ResetOnServer();yield return Wait(()=>!hunt.State.Active,"controlled combat fixture reset");
            // Combat fixture starts only after the real baggage release/exploration has been observed.
            var room=reception.Rooms[3];var node=hunt.Nodes.Single(n=>n.room==room);var guest=room.Occupant;
            if(!Host)
            {
                yield return Walk(room.DoorOutside.position);yield return Walk(room.DoorInside.position);yield return Approach(guest.Body);
                Use(guest.GetComponent<GuestEvacuation>(),guest.Body.bounds.center);
                yield return Walk(room.DoorInside.position);yield return Walk(room.DoorOutside.position);
                yield return Walk(new Vector3(-1.6f,0,25)); // Clear the guest's door waypoint before waiting for evacuation.
            }
            yield return Wait(()=>guest.Phase==GuestPhase.Evacuated,"104 evacuation reaches its lobby waiting point",180);
            Need(room.State.Status==RoomStatus.Dirty&&!guest.State.Assigned,"Evacuation vacates room");
            checks.Add("Room 104 guest evacuated through corridor to separate lobby wait point, freeing the room.");
            var equipment=Host?hunt.Firearm:hunt.Decoy;yield return Approach(equipment.PickupCollider);Use(equipment,equipment.PickupCollider.bounds.center);
            yield return Wait(()=>equipment.LocallyHeld,"pickup emergency equipment in Storage");
            yield return Walk(room.DoorOutside.position);yield return Walk(room.DoorInside.position);
            var side=Vector3.Cross(Vector3.up,node.transform.forward);var shooter=node.landing.position+node.transform.forward*1.3f+side*.45f;shooter.y=0;
            if(Host)yield return Walk(shooter);
            else
            {
                var spot=node.landing.position+node.transform.forward*.35f;spot.y=0;
                yield return Walk(spot+node.transform.forward*.9f);hunt.Decoy.PlaceRpc(spot,hunt.Decoy.State.Revision);
                yield return Wait(()=>hunt.Decoy.IsHeating,"physical thermal decoy placed");yield return Walk(room.DoorInside.position);yield return Walk(room.DoorOutside.position);
            }
            yield return Barrier("ambush-staged");
            if(Host)Need(hunt.ActivateOnServer(node.index,carrier),"Activate controlled combat at room 104 vent");
            yield return Wait(()=>hunt.State.Phase==MimicPhase.Exposed&&hunt.State.Node==node.index,"heat lures visible creature",100);
            if(Host)
            {
                yield return Shoot();yield return Wait(()=>hunt.State.Health==66,"first valid hit");
                yield return Walk(room.DoorInside.position);yield return Walk(room.DoorOutside.position);
            }
            yield return Wait(()=>sawRetreat&&hunt.State.Phase!=MimicPhase.Retreating&&hunt.State.Health==66,"retreat preserves wounded HP",90);
            checks.Add("Thermal decoy caused physical emergence; first shot replicated 66 HP; retreat preserved damage.");
            yield return Barrier("retreat-checked");
            // Heat is local: after retreat the creature may legitimately leave this branch.
            // Preserve that behavior. Verify lethal combat in a separate explicit debug fixture.
            if(Host)hunt.ResetOnServer();yield return Wait(()=>!hunt.State.Active,"prepare lethal combat fixture");
            if(Host)
            {
                // The normal debug reset also returns equipment to Storage.
                yield return Wait(()=>hunt.Firearm.PickupCollider.enabled,"pistol returned to Storage");
                yield return Approach(hunt.Firearm.PickupCollider);Use(hunt.Firearm,hunt.Firearm.PickupCollider.bounds.center);
                yield return Wait(()=>hunt.Firearm.LocallyHeld,"reacquire pistol after fixture reset");
                yield return Walk(room.DoorInside.position);yield return Walk(shooter);
            }
            yield return Barrier("lethal-combat-staged");
            if(Host)Need(hunt.ActivateOnServer(node.index,carrier),"Activate independent lethal combat fixture");
            yield return Wait(()=>hunt.State.Phase==MimicPhase.Exposed&&hunt.State.Node==node.index,"lethal combat emergence",100);
            if(Host){yield return Shoot();yield return new WaitForSecondsRealtime(.65f);yield return Shoot();yield return new WaitForSecondsRealtime(.65f);yield return Shoot();}
            yield return Wait(()=>hunt.State.Phase==MimicPhase.Dead&&shift.State.MimicsEliminated==1,"three shots eliminate mimic exactly once",40);
            Need(carrier.Alive&&shift.State.GuestDeaths==0,"Controlled hunt harmed a guest");
            checks.Add("Separate lethal combat fixture: three server-validated shots killed the mimic once; carrier protected; guest service remains running.");
            if(Host){yield return Walk(room.DoorInside.position);yield return Walk(room.DoorOutside.position);}
            yield return Barrier("hunt-cleared");
            if(Host)HuntScenario.Instance.ResetScenario();yield return Wait(()=>!hunt.State.Active&&shift.State.GuestDeaths==0&&shift.State.MimicsEliminated==0,"scenario reset after hunt");
            Need(!hunt.Firearm.State.Held&&!hunt.Decoy.State.Held&&!hunt.Decoy.IsHeating,"Equipment reset");
            checks.Add("Scenario restart reset threat, equipment, baggage and guest states while both players stayed connected.");
        }
        IEnumerator Shoot()
        {
            Need(hunt.Firearm.LocallyHeld,"Pistol ownership");var direction=hunt.Body.bounds.center-view.transform.position;
            own.transform.rotation=Quaternion.LookRotation(new Vector3(direction.x,0,direction.z));view.transform.rotation=Quaternion.LookRotation(direction);
            yield return new WaitForSecondsRealtime(.2f);view.transform.LookAt(hunt.Body.bounds.center);hunt.Firearm.FireLocal();yield return new WaitForSecondsRealtime(.25f);
            if(!Application.isBatchMode){ScreenCapture.CaptureScreenshot(Path.Combine(dir,role+"-hunt.png"));yield return null;}
        }
        void CaptureFeed(VentSurveillance surveillance,string name)
        {
            if(Application.isBatchMode)return;var previous=RenderTexture.active;RenderTexture.active=surveillance.CurrentFeed;
            var tex=new Texture2D(surveillance.CurrentFeed.width,surveillance.CurrentFeed.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,tex.width,tex.height),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(dir,name+".png"),tex.EncodeToPNG());Destroy(tex);RenderTexture.active=previous;
        }

        static void Field(object obj,string name,object value)=>obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);
        IEnumerator OpenComputer(ReceptionComputer station)
        {
            // Use the authored employee side; a clear ray from the guest side is intentionally unauthorized.
            yield return Walk(station.Focus.position);
            yield return new WaitForSecondsRealtime(.3f);
            Use(station.ScreenCollider.GetComponent<ComputerInteraction>(),station.ScreenCollider.bounds.center);yield return Wait(()=>station.Focused,"workstation focused");
        }
        bool RayHits(Collider collider,Vector3 eye,Vector3 target)
        {
            var d=target-eye;
            var hit=Physics.RaycastAll(eye,d.normalized,d.magnitude+.03f,~0,QueryTriggerInteraction.Collide)
                .Where(h=>!h.collider.transform.IsChildOf(own.transform)&&(!h.collider.isTrigger||h.collider.GetComponentInParent<IInteractable>()!=null))
                .OrderBy(h=>h.distance).FirstOrDefault();
            var intended=collider.GetComponentInParent<IInteractable>();
            return hit.collider==collider||hit.collider!=null&&intended!=null&&hit.collider.GetComponentInParent<IInteractable>()==intended;
        }
        IEnumerator Approach(Collider collider,NetworkDoor door=null)
        {
            Need(collider!=null,"Missing interaction collider");var center=collider.bounds.center;var choices=new List<(Vector3 p,float distance)>();
            for(int radius=0;radius<3;radius++)for(int i=0;i<16;i++)
            {
                float a=i*Mathf.PI/8;var p=new Vector3(center.x+Mathf.Cos(a)*(.85f+radius*.4f),0,center.z+Mathf.Sin(a)*(.85f+radius*.4f));
                if(!NavMesh.SamplePosition(p,out var hit,.2f,NavMesh.AllAreas)||!Clear(hit.position))continue;p=hit.position;
                if(door!=null)
                {
                    var local=door.transform.InverseTransformPoint(p+Vector3.up*.9f);
                    var side=door.transform.InverseTransformPoint(own.transform.position).z;
                    if(local.z*side<0)continue;
                }
                if(!RayHits(collider,p+Vector3.up*1.65f,center))continue;
                var path=new NavMeshPath();if(!NavMesh.CalculatePath(own.transform.position,p,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)continue;
                float distance=0;for(int k=1;k<path.corners.Length;k++)distance+=Vector3.Distance(path.corners[k-1],path.corners[k]);choices.Add((p,distance));
            }
            Need(choices.Count>0,"No reachable interaction position for "+collider.name+" at "+center);
            yield return Walk(choices.OrderBy(v=>v.distance).First().p,NavMesh.AllAreas,door);
        }
        void Use(IInteractable target,Vector3 aim)
        {view.transform.LookAt(aim);Physics.SyncTransforms();Need(ReferenceEquals(user.FindTarget(),target),"Ray misses "+target+" at "+own.transform.position+" found "+user.FindTarget()+" ray="+string.Join(";",Physics.RaycastAll(view.transform.position,view.transform.forward,PlayerInteractor.MaxDistance,~0,QueryTriggerInteraction.Ignore).Where(h=>!h.collider.transform.IsChildOf(own.transform)).OrderBy(h=>h.distance).Select(h=>h.collider.name+"/"+h.distance)));Need(user.TryInteract(),"Interaction unavailable: "+target);}
        bool Clear(Vector3 p)=>!Physics.OverlapCapsule(p+Vector3.up*.36f,p+Vector3.up*1.44f,.29f,~0,QueryTriggerInteraction.Ignore).Any(c=>!c.transform.IsChildOf(own.transform));
        IEnumerator Walk(Vector3 target,int mask=NavMesh.AllAreas,NetworkDoor ignoredDoor=null)
        {
            target.y=0;Progress("walk "+target);var path=new NavMeshPath();
            Need(NavMesh.CalculatePath(own.transform.position,target,mask,path)&&path.status==NavMeshPathStatus.PathComplete,"No player route "+target+" mask="+mask);
            var cc=own.GetComponent<CharacterController>();
            foreach(var corner in path.corners.Skip(1).Concat(new[]{target}))
            {
                float end=Time.realtimeSinceStartup+50;var detours=new Queue<Vector3>();float nextAvoid=0;
                while(true)
                {
                    var goal=detours.Count>0?detours.Peek():corner;var delta=goal-own.transform.position;delta.y=0;
                    if(delta.magnitude<.2f){if(detours.Count>0){detours.Dequeue();continue;}break;}
                    Need(Time.realtimeSinceStartup<end,"Player blocked before "+corner+" at "+own.transform.position);
                    var hits=Physics.CapsuleCastAll(own.transform.position+Vector3.up*.35f,own.transform.position+Vector3.up*1.45f,.29f,delta.normalized,Mathf.Min(1.4f,delta.magnitude),~0,QueryTriggerInteraction.Ignore).Where(h=>!h.collider.transform.IsChildOf(own.transform)).ToArray();
                    var door=hits.Select(h=>h.collider.GetComponentInParent<NetworkDoor>()).FirstOrDefault(d=>d!=null&&d!=ignoredDoor&&!d.IsOpen);
                    if(door!=null)
                    {
                        yield return Approach(door.GetComponentsInChildren<BoxCollider>().First(c=>c.enabled),door);
                        yield return new WaitForSecondsRealtime(.35f); // Replicate the player's safe standing position before server sweep validation.
                        yield return Wait(()=>door.CanInteract||door.IsOpen,"door available "+door.name,40);
                        if(!door.IsOpen)Use(door,door.InteractionPoint);
                        yield return Wait(()=>door.IsOpen&&!door.IsMoving,"open route door "+door.name,40);
                        yield return new WaitForSecondsRealtime(.25f); // Let the stationary open leaf carve its actual footprint.
                        yield return Walk(target,mask);yield break;
                    }
                    if(detours.Count==0&&Time.realtimeSinceStartup>nextAvoid&&hits.Any(h=>h.collider.GetComponentInParent<GuestLifecycle>()!=null))
                    {
                        nextAvoid=Time.realtimeSinceStartup+.5f;
                        foreach(float sign in new[]{1f,-1f})
                        {
                            var across=own.transform.position+Vector3.Cross(Vector3.up,delta.normalized)*sign*.95f;var past=across+delta.normalized*1.5f;
                            if(!Clear(across)||!Clear(past)||NavMesh.Raycast(own.transform.position,across,out _,mask)||NavMesh.Raycast(across,past,out _,mask))continue;
                            detours.Enqueue(across);detours.Enqueue(past);break;
                        }
                        if(detours.Count>0)continue;
                    }
                    cc.Move(Vector3.ClampMagnitude(delta,4*Time.deltaTime)+Vector3.down*2*Time.deltaTime);yield return null;
                }
            }
            yield return new WaitForSecondsRealtime(.2f);
        }
        void Finish(bool pass,string why){ReportSlug();pass=pass&&errors.Count==0;File.WriteAllText(Path.Combine(dir,role+".txt"),(pass?"PASS ":"FAIL ")+role+" / "+mode+"\n"+string.Join("\n",checks)+"\n"+why+"\n"+string.Join("\n",errors));Application.Quit(pass?0:1);}
        void OnDestroy()=>Application.logMessageReceived-=Log;
    }
}
#endif
