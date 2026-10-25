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
    public sealed class NightShiftSmokeTest : MonoBehaviour
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
        readonly List<string> checks=new(),errors=new();
        readonly HashSet<string> contested=new();
        readonly Dictionary<GuestLifecycle,(Vector3 p,uint visit,GuestPhase phase)> positions=new();
        int maximumQueue;float distance;float nextDiagnostic;bool joining;bool capturedComputer,capturedRooms,capturedHud,capturedRepair,capturedSummary;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-motel-night-test");if(i<0)return;
            var t=new GameObject("NightShiftSmokeTest").AddComponent<NightShiftSmokeTest>();t.role=args[i+1];t.directory=args[Array.IndexOf(args,"-motel-night-results")+1];
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
            if(guests==null||joining||shift==null||!shift.IsSpawned)return;
            if(!Application.isBatchMode&&computer.Focused)
            {
                if(!capturedComputer){ScreenCapture.CaptureScreenshot(Path.Combine(directory,role+"-computer.png"));capturedComputer=true;}
                if(!capturedRooms&&shift.State.CheckedIn>0){ScreenCapture.CaptureScreenshot(Path.Combine(directory,role+"-rooms.png"));capturedRooms=true;}
            }
            if(!Application.isBatchMode&&!computer.LocalOpen)
            {
                if(!capturedHud&&requests.Phase==GuestRequestPhase.Ringing)
                {ScreenCapture.CaptureScreenshot(Path.Combine(directory,role+"-phone-hud.png"));capturedHud=true;}
                if(!capturedRepair&&requests.State.RepairStep==1&&requests.Phase==GuestRequestPhase.PendingDelivery)
                {ScreenCapture.CaptureScreenshot(Path.Combine(directory,role+"-repair.png"));capturedRepair=true;}
                if(!capturedSummary&&shift.State.Phase==ShiftPhase.Finished)
                {ScreenCapture.CaptureScreenshot(Path.Combine(directory,role+"-summary.png"));capturedSummary=true;}
            }
            if(Time.unscaledTime>=nextDiagnostic){nextDiagnostic=Time.unscaledTime+1;File.WriteAllText(Path.Combine(directory,role+".state"),Diagnostic());}
            maximumQueue=Math.Max(maximumQueue,reception.QueueCount);
            if(role=="client"&&shift.IsRunning&&computer.Occupied&&computer.User!=session.Manager.LocalClientId)
            {
                var checkin=reception.WaitingNpc;var checkout=reception.CheckoutNpc;
                if(checkin!=null&&contested.Add("in-"+checkin.NetworkObjectId+"-"+checkin.State.Visit))
                {
                    int index=Array.FindIndex(rooms,r=>r.AvailableForCheckIn);
                    if(index>=0)reception.RequestCheckInRpc(index,rooms[index].State.Cycle,new NetworkObjectReference(checkin.NetworkObject),checkin.State.Visit,computer.Lease);
                }
                if(checkout!=null&&contested.Add("out-"+checkout.NetworkObjectId+"-"+checkout.State.Visit))
                {
                    int index=Array.IndexOf(rooms,checkout.Room);
                    reception.RequestCheckoutRpc(index,rooms[index].State.Cycle,new NetworkObjectReference(checkout.NetworkObject),checkout.State.Visit,computer.Lease);
                }
            }
            if(shift.State.CheckedIn>shift.State.Arrived||shift.State.CheckedOut>shift.State.CheckedIn||shift.State.Towels+shift.State.Repairs>shift.State.CheckedIn||shift.State.Prepared>rooms.Length+shift.State.CheckedOut)fatal="Duplicate statistics credit";
            if(role!="host")return;
            var queued=guests.Where(g=>g.State.QueuePosition>0&&g.Phase!=GuestPhase.Absent).ToArray();
            if(queued.Length>3||queued.Select(g=>g.State.QueuePosition).Distinct().Count()!=queued.Length)fatal="Duplicate queue reservation";
            if(rooms.Where(r=>r.State.Status==RoomStatus.Occupied).Select(r=>r.Occupant).Distinct().Count()!=rooms.Count(r=>r.State.Status==RoomStatus.Occupied))fatal="Duplicate occupant";
            foreach(var guest in guests)
            {
                if(positions.TryGetValue(guest,out var prev)&&prev.visit==guest.State.Visit&&prev.phase!=GuestPhase.Absent&&guest.Phase!=GuestPhase.Absent)
                {float d=Vector3.Distance(prev.p,guest.transform.position);if(d>1.6f*Time.unscaledDeltaTime+.7f)fatal="NPC route teleport";distance+=d;}
                positions[guest]=(guest.transform.position,guest.State.Visit,guest.Phase);
            }
        }
        IEnumerator Run()
        {
            yield return null;session=FindFirstObjectByType<DevelopmentSession>();session.TransportSettings.Address="127.0.0.1";session.TransportSettings.Port=17790;
            yield return Connect();
            if(role=="observer")
            {
                yield return Wait(()=>requests.Phase==GuestRequestPhase.PendingDelivery&&requests.State.Kind==GuestRequestKind.RepairTelevision&&requests.State.RepairStep==1,"late repair",15);
                yield return new WaitForSecondsRealtime(.4f);
                Require(shift.IsRunning&&shift.State.Number==1&&shift.Progress>0&&shift.Progress<1,"Late clock/shift");
                Require(requests.Room.State.Status==RoomStatus.Occupied&&requests.Room.Occupant.IsInRoomFor(requests.Room),"Late occupancy");
                Require(requests.Room.Television.Broken&&requests.Room.Television.DisplayText.Contains("NO SIGNAL"),"Late television presentation");
                Require(board.DisplayText.Contains("TV Repair")&&board.DisplayText.Contains("Reset TV"),"Late board");
                Require(guests.All(g=>!g.Agent.enabled),"Client simulated navigation");
                checks.Add("Late join during repair stage1: clock, room/guest, queue snapshot, TV and staff board synchronized; agents disabled.");Mark("observer-checked");yield break;
            }
            yield return Barrier("connected");
            yield return new WaitForSecondsRealtime(3);
            Require(shift.State.Phase==ShiftPhase.WaitingToStart&&shift.ClockText=="22:00"&&guests.All(g=>g.Phase==GuestPhase.Absent),"Automatic pre-shift arrivals");
            rooms[0].RequestCleanRpc(-1,rooms[0].State.Cycle);yield return new WaitForSecondsRealtime(.3f);Require(!rooms[0].State.BedMade,"Pre-shift task accepted");
            yield return ToComputer();yield return Barrier("at-computer");
            if(role=="host")yield return OpenComputer();
            yield return Wait(()=>computer.Occupied,"host occupies");yield return Barrier("host-focused");
            if(role=="client")
            {
                var camera=local.GetComponentInChildren<Camera>(true);var before=camera.transform.localPosition;
                computer.UseRpc(computer.Lease);shift.StartShiftRpc(shift.State.Number,computer.Lease);
                yield return new WaitForSecondsRealtime(.5f);
                Require(!computer.LocalOpen&&!interactor.MenuOpen&&camera.transform.localPosition==before,"Remote camera/input interfered");
                Require(shift.State.Phase==ShiftPhase.WaitingToStart,"Non-operator started night");
            }
            yield return Barrier("busy-checked");if(role=="host")computer.CloseLocal();
            yield return Wait(()=>!computer.Occupied&&!computer.LocalOpen,"released");yield return Barrier("released");
            if(role=="client")yield return OpenComputer();yield return Wait(()=>computer.Occupied,"client occupies");yield return Barrier("client-seat");
            if(role=="client")
            {
                joining=true;session.Disconnect();yield return Wait(()=>!session.Manager.IsListening&&!session.Manager.ShutdownInProgress,"client disconnected");
                yield return Wait(()=>Marked("disconnect-released"),"server releases disconnected user");yield return Connect();yield return ToComputer();
            }
            else{yield return Wait(()=>!computer.Occupied,"release on disconnect");Mark("disconnect-released");}
            yield return Barrier("reconnected");
            checks.Add("Exclusive workstation, remote camera/input isolation, unauthorized start refusal, disconnect release and reconnect.");
            float duration=900;var args=Environment.GetCommandLineArgs();int durationIndex=Array.IndexOf(args,"-motel-night-seconds");if(durationIndex>=0)duration=float.Parse(args[durationIndex+1]);
            if(role=="host")
            {
                Configure(shift,"durationSeconds",duration);
                if(duration<900)Configure(reception,"arrivalPatience",duration);
                // Faster diagnostics only; the final 900s run keeps default arrival timing/density.
                if(duration<900){Configure(shift,"firstArrivalDelay",5f);Configure(shift,"lastArrivalFraction",.4f);}
                Configure(requests,"televisionChance",1f);UnityEngine.Random.InitState(4510);
            }
            yield return StartNight(0);
            if(role=="client")
            {for(int i=0;i<4;i++)yield return Clean(i);yield return BackToLobby();Mark("initial-rooms-ready");}
            else
            {
                yield return OpenComputer();Vector3 p=local.transform.position;
                yield return Wait(()=>Marked("initial-rooms-ready"),"client cleans while host uses workstation",180);
                Require(Vector3.Distance(local.transform.position,p)<.08f&&computer.Focused,"Focused camera moved character");
                computer.CloseLocal();yield return Wait(()=>!computer.LocalOpen,"restore camera");
            }
            yield return Barrier("prepared");
            if(duration<900)
            {
                yield return Wait(()=>reception.QueueCount==3&&guests.Count(g=>g.Phase==GuestPhase.WaitingForCheckIn)==3,"three guests physically waiting",duration*.65f);
                yield return Wait(()=>guests.Where(g=>g.State.QueuePosition>0).All(g=>Vector3.Distance(g.transform.position,reception.QueuePoints[g.State.QueuePosition-1].position)<.3f),"queue transforms settle",5);
                checks.Add("Three distinct physical queue positions and four Ready rooms; camera focus leaves worker gameplay running.");
            }
            else
            {
                yield return Wait(()=>reception.GuestWaiting,"default-paced first arrival",180);
                checks.Add("Default 900s clock, 45s first-delay bound, 80% arrival cutoff and 120s patience; no artificial initial queue hold.");
            }
            yield return Barrier("queue-three");
            if(role=="host")yield return Operate();else yield return Work();
            yield return Wait(()=>shift.State.Phase==ShiftPhase.Finished,"first night closing",duration+30);
            yield return new WaitForSecondsRealtime(.7f);
            Require(shift.ClockText=="06:00"&&shift.State.CheckedIn>=3&&shift.State.CheckedOut>=2&&shift.State.Prepared>=5,"First summary insufficient: "+shift.SummaryText);
            Require(shift.State.Repairs>=1&&shift.State.Towels>=1&&shift.State.MissedRequests>=1,"Missing service coverage: "+shift.SummaryText);
            Require(!computer.Occupied&&requests.Phase==GuestRequestPhase.NoRequest&&!local.GetComponent<TowelCarrier>().HasTowel,"Closing temporary state");
            Require(guests.All(g=>g.Phase==GuestPhase.Absent&&!g.Visible)&&rooms.All(r=>r.State.Status==RoomStatus.Dirty&&r.Occupant==null)&&reception.QueueCount==0,"Closing pool/rooms/queue cleanup");
            var frozen=guests.Select(g=>g.transform.position).ToArray();yield return new WaitForSecondsRealtime(1);
            Require(guests.Select((g,i)=>Vector3.Distance(g.transform.position,frozen[i])<.03f).All(b=>b),"NPC continued moving after close");
            checks.Add("Full "+duration+"s shift: "+shift.SummaryText.Replace('\n',';')+". Routes stopped, calls/towels/terminal released.");
            uint[] cycles=rooms.Select(r=>r.State.Cycle).ToArray();uint oldRequest=requests.State.RequestId;
            if(role=="host")File.WriteAllText(Path.Combine(directory,"summary-one.txt"),shift.SummaryText);
            yield return Barrier("first-finished");Require(File.ReadAllText(Path.Combine(directory,"summary-one.txt"))==shift.SummaryText,"Summary replicas disagree");
            checks.Add($"Arrivals {shift.State.Arrived}/{shift.State.ArrivalTarget}; shared summary identical. Concurrent non-operator check-in/out requests: {contested.Count}.");
            yield return ToComputer();
            if(role=="host")
            {
                Configure(shift,"durationSeconds",180f);Configure(shift,"arrivalCount",new Vector2Int(3,3));Configure(shift,"firstArrivalDelay",5f);Configure(shift,"lastArrivalFraction",.65f);
                Configure(reception,"arrivalPatience",120f);foreach(var g in guests)Configure(g,"stayDuration",45f);Configure(requests,"televisionChance",.5f);
            }
            yield return StartNight(1);
            Require(rooms.Select((r,i)=>r.State.Cycle==cycles[i]+1&&r.State.Status==RoomStatus.Dirty&&!r.State.BedMade&&r.TrashRemovedCount==0&&r.Occupant==null).All(b=>b),"Next night room reset");
            Require(guests.All(g=>g.Phase==GuestPhase.Absent)&&reception.QueueCount==0&&requests.State.RequestId>oldRequest&&!computer.Occupied,"Stale next-night objects");
            if(role=="host")yield return Operate();else yield return Work();
            yield return Wait(()=>shift.State.Phase==ShiftPhase.Finished,"second night closing",210);
            Require(shift.State.Number==2&&shift.State.CheckedIn>=2&&shift.State.CheckedOut>=1&&shift.State.Prepared>=2,"Second shift not playable: "+shift.SummaryText);
            checks.Add("Second 180s shift without reconnect/restart: "+shift.SummaryText.Replace('\n',';')+". Old cycles/items/calls/queue/seat absent.");
            yield return Barrier("all-complete");Require(fatal==null,fatal);
        }
        IEnumerator StartNight(uint number)
        {
            if(role=="host")yield return OpenComputer();yield return Wait(()=>computer.Occupied,"start workstation");yield return Barrier("start-"+number);
            uint lease=computer.Lease;
            for(int i=0;i<5;i++)shift.StartShiftRpc(number,lease);
            yield return Wait(()=>shift.IsRunning&&shift.State.Number==number+1,"night started");yield return Wait(()=>!computer.LocalOpen,"start releases camera");
            yield return new WaitForSecondsRealtime(.5f);Require(shift.State.Number==number+1&&shift.State.CheckedIn==0,"Duplicate night start");
            checks.Add("Concurrent/repeated shift start -> exactly one transition, no client bypass.");
        }
        IEnumerator Operate()
        {
            yield return OpenComputer();
            while(shift.IsRunning)
            {
                if(shift.State.Number==1&&Marked("repair-stage-one")&&!Marked("repair-both-done"))
                {
                    computer.CloseLocal();yield return Wait(()=>!computer.LocalOpen,"leave computer for cooperative repair");
                    var room=requests.Room;yield return EnterRoom(Array.IndexOf(rooms,room));
                    yield return Barrier("repair-entry-clear");
                    yield return Walk(Map(new Vector3(20f,0,13.4f),Array.IndexOf(rooms,room)));
                    yield return Barrier("repair-final");uint id=requests.State.RequestId;
                    for(int i=0;i<5;i++)requests.RepairRpc(new NetworkObjectReference(room.NetworkObject),1,id);
                    yield return Wait(()=>requests.Phase==GuestRequestPhase.Completed,"simultaneous repair complete");
                    Require(shift.State.Repairs==1,"Repair counted twice");Mark("repair-both-done");Configure(requests,"televisionChance",0f);
                    yield return Walk(Map(new Vector3(20,0,14.4f),Array.IndexOf(rooms,room)));Mark("repair-host-clear");
                    yield return Wait(()=>Marked("repair-client-out"),"client clears door before host leaves");
                    // The worker leaves the inward-opening leaf across this approach lane.
                    // Close it from the clear TV side before following the usual exit route.
                    if(room.Door.IsOpen)Use(room.Door,room.Door.InteractionPoint);
                    yield return Wait(()=>!room.Door.IsOpen&&!room.Door.IsMoving,"clear inward door leaf before host exit");
                    yield return LeaveRoom(Array.IndexOf(rooms,room));yield return ToComputer();yield return OpenComputer();
                }
                var outgoing=reception.CheckoutNpc;
                if(outgoing!=null)
                {
                    int index=Array.IndexOf(rooms,outgoing.Room);uint cycle=rooms[index].State.Cycle;
                    var guest=new NetworkObjectReference(outgoing.NetworkObject);uint visit=outgoing.State.Visit;
                    reception.RequestCheckoutRpc(index,cycle,guest,visit+1,computer.Lease);Require(rooms[index].State.Status==RoomStatus.Occupied,"Stale visit checkout accepted");
                    for(int i=0;i<5;i++)reception.RequestCheckoutRpc(index,cycle,guest,visit,computer.Lease);
                }
                else if(reception.WaitingNpc!=null)
                {
                    int index=Array.FindIndex(rooms,r=>r.AvailableForCheckIn);
                    if(index>=0)
                    {
                        var g=reception.WaitingNpc;uint cycle=rooms[index].State.Cycle;
                        for(int i=0;i<5;i++)reception.RequestCheckInRpc(index,cycle,new NetworkObjectReference(g.NetworkObject),g.State.Visit,computer.Lease);
                    }
                }
                if(shift.State.Towels>0&&shift.State.Repairs>0)Configure(requests,"televisionChance",.5f);
                yield return new WaitForSecondsRealtime(.15f);
            }
        }
        IEnumerator Work()
        {
            while(shift.IsRunning)
            {
                if(requests.Phase==GuestRequestPhase.Ringing)
                {
                    yield return Phone();if(!shift.IsRunning)break;
                    if(requests.Phone.CanInteract)Use(requests.Phone,requests.Phone.AimPoint);
                    yield return new WaitForSecondsRealtime(.3f);
                }
                if(requests.Phase==GuestRequestPhase.PendingDelivery&&shift.IsRunning)
                {
                    uint id=requests.State.RequestId;var room=requests.Room;int index=Array.IndexOf(rooms,room);
                    if(shift.State.Number==1&&shift.State.Towels>0&&shift.State.Repairs>0&&shift.State.MissedRequests==0)
                    {
                        if(requests.State.Kind==GuestRequestKind.CleanTowel){yield return Supply();if(requests.TowelSupply.CanInteract)Use(requests.TowelSupply,requests.TowelSupply.AimPoint);yield return FromSupply();}
                        yield return BackToLobby();
                        yield return Wait(()=>requests.State.RequestId!=id||requests.Phase!=GuestRequestPhase.PendingDelivery||!shift.IsRunning,"unfinished request cancelled on return",160);
                        Require(!local.GetComponent<TowelCarrier>().HasTowel,"Cancelled request retained towel");
                        requests.RepairRpc(new NetworkObjectReference(room.NetworkObject),1,id);requests.DeliverRpc(new NetworkObjectReference(room.NetworkObject),id);
                        checks.Add("Return/checkout cancelled pending request; stale repair/delivery rejected and towel cleared.");continue;
                    }
                    if(requests.State.Kind==GuestRequestKind.CleanTowel)
                    {
                        yield return Supply();if(requests.TowelSupply.CanInteract)Use(requests.TowelSupply,requests.TowelSupply.AimPoint);
                        yield return FromSupply();yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(new Vector3(22.2f,0,room.Delivery.AimPoint.z));
                        if(requests.State.RequestId==id&&requests.Phase==GuestRequestPhase.PendingDelivery&&shift.IsRunning)
                        {
                            requests.DeliverRpc(new NetworkObjectReference(rooms[(index+1)%4].NetworkObject),id);
                            yield return new WaitForSecondsRealtime(.2f);
                            if(requests.State.RequestId!=id||requests.Phase!=GuestRequestPhase.PendingDelivery){yield return BackToLobby();continue;}
                            Use(room.Delivery,room.Delivery.AimPoint);for(int n=0;n<4;n++)requests.DeliverRpc(new NetworkObjectReference(room.NetworkObject),id);
                            yield return Wait(()=>requests.Phase!=GuestRequestPhase.PendingDelivery,"towel completion");
                            checks.Add("Towel pickup/delivery through raycast, wrong room and duplicate validation.");
                        }
                        yield return BackToLobby();
                    }
                    else
                    {
                        yield return EnterRoom(index);yield return Walk(Map(new Vector3(20f,0,14.4f),index));
                        yield return RepairTv(index,room,id);
                        if(shift.State.Number==1&&!Marked("repair-client-out"))yield return Wait(()=>Marked("repair-host-clear"),"host clears door sweep");
                        yield return LeaveRoom(index);Mark("repair-client-out");
                    }
                }
                else
                {
                    int dirty=Array.FindIndex(rooms,r=>r.State.Status==RoomStatus.Dirty);
                    if(dirty>=0)yield return Clean(dirty);else yield return new WaitForSecondsRealtime(.3f);
                }
            }
        }
        bool RepairActive(ServiceRoom room,uint id)=>shift.IsRunning&&requests.State.RequestId==id&&requests.Room==room&&requests.State.Kind==GuestRequestKind.RepairTelevision&&requests.Phase==GuestRequestPhase.PendingDelivery;
        IEnumerator RepairTv(int index,ServiceRoom room,uint id)
        {
            if(!RepairActive(room,id))yield break;
            var tv=room.Television;
            requests.RepairRpc(new NetworkObjectReference(rooms[(index+1)%4].NetworkObject),0,id);
            requests.RepairRpc(new NetworkObjectReference(room.NetworkObject),1,id);
            yield return new WaitForSecondsRealtime(.3f);if(!RepairActive(room,id))yield break;
            Require(requests.State.RepairStep==0,"Wrong room/out-of-order repair accepted");
            Use(tv.Controls[0],tv.Controls[0].AimPoint);
            yield return Wait(()=>requests.State.RepairStep==1||!RepairActive(room,id),"cable accepted or request cancelled",8);
            if(!RepairActive(room,id)){checks.Add("TV request cancelled during the visit; obsolete controls unavailable.");yield break;}
            if(shift.State.Number==1&&!Marked("repair-stage-one"))
            {
                Mark("repair-stage-one");Mark("observe-repair");float south=index>=2?13f:12.65f;
                yield return Walk(Map(new Vector3(20,0,south),index));yield return Walk(Map(new Vector3(19,0,south),index));
                yield return Wait(()=>Marked("observer-checked"),"late observer",35);yield return Barrier("repair-entry-clear");
                yield return Walk(Map(new Vector3(20,0,south),index));yield return Walk(Map(new Vector3(20,0,14.4f),index));
                yield return Barrier("repair-final");
            }
            if(!RepairActive(room,id))yield break;
            if(tv.Controls[1].CanInteract)Use(tv.Controls[1],tv.Controls[1].AimPoint);
            for(int n=0;n<5;n++)requests.RepairRpc(new NetworkObjectReference(room.NetworkObject),1,id);
            yield return Wait(()=>!RepairActive(room,id),"TV completed or cancelled",8);
            if(requests.State.RequestId!=id||requests.Phase!=GuestRequestPhase.Completed)yield break;
            yield return new WaitForSecondsRealtime(.4f);Require(!tv.Broken&&tv.DisplayText.Contains("CHANNEL 03"),"TV display reset");
            Require(board.DisplayText.Contains("Completed"),"Board not completed");
            checks.Add("Physical TV two-step repair, wrong room/order rejection, simultaneous completion, completed screen and shared board.");
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
            yield return Walk(new Vector3(12.8f,0,8.4f));yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(rooms[i].DoorOutside.position);
            if(!rooms[i].Door.IsOpen)Use(rooms[i].Door,rooms[i].Door.InteractionPoint);
            yield return Wait(()=>rooms[i].Door.IsOpen&&!rooms[i].Door.IsMoving,"door opens");
            if(rooms[i].State.Status==RoomStatus.Occupied)
            {
                var inner=rooms[i].DoorInside.position;inner.x=Map(new Vector3(20,0,0),i).x;yield return Walk(inner);
                float south=i>=2?13f:12.65f;
                yield return Walk(Map(new Vector3(20,0,south),i));yield return Walk(Map(new Vector3(19,0,south),i));
                if(rooms[i].Door.IsOpen)Use(rooms[i].Door,rooms[i].Door.InteractionPoint);
                yield return Wait(()=>!rooms[i].Door.IsOpen&&!rooms[i].Door.IsMoving,"close door clear of television access");
                yield return Walk(Map(new Vector3(20f,0,south),i));
            }
            else yield return Walk(rooms[i].DoorInside.position);
        }
        IEnumerator LeaveRoom(int i)
        {
            if(rooms[i].State.Status==RoomStatus.Occupied)
            {
                float south=i>=2?13f:12.65f;
                yield return Walk(Map(new Vector3(20f,0,south),i));yield return Walk(Map(new Vector3(19,0,south),i));
                if(!rooms[i].Door.IsOpen)Use(rooms[i].Door,rooms[i].Door.InteractionPoint);
                yield return Wait(()=>rooms[i].Door.IsOpen&&!rooms[i].Door.IsMoving,"leave occupied room");
                yield return Walk(Map(new Vector3(20,0,south),i));
                var inner=rooms[i].DoorInside.position;inner.x=Map(new Vector3(20,0,0),i).x;yield return Walk(inner);
            }
            else yield return Walk(rooms[i].DoorInside.position);
            yield return Walk(rooms[i].DoorOutside.position);yield return BackToLobby();
        }
        IEnumerator BackToLobby(){yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(new Vector3(13.3f,0,8.4f));yield return Walk(new Vector3(13.3f,0,7.5f));}
        IEnumerator Clean(int i)
        {
            yield return EnterRoom(i);yield return Walk(Map(new Vector3(18.5f,0,13.2f),i));
            if(rooms[i].Bed.CanInteract)Use(rooms[i].Bed,rooms[i].Bed.AimPoint);
            yield return Walk(Map(new Vector3(18.8f,0,12.65f),i));if(rooms[i].Trash[0].CanInteract)Use(rooms[i].Trash[0],rooms[i].Trash[0].AimPoint);
            yield return Walk(Map(new Vector3(18f,0,12.2f),i));yield return Walk(Map(new Vector3(17.7f,0,12.2f),i));
            if(rooms[i].Trash[1].CanInteract)Use(rooms[i].Trash[1],rooms[i].Trash[1].AimPoint);
            yield return Wait(()=>rooms[i].State.Status!=RoomStatus.Dirty||!shift.IsRunning,"room cleaned "+i,8);
            yield return Walk(Map(new Vector3(18.1f,0,12.4f),i));yield return LeaveRoom(i);
        }
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
            float end=Time.realtimeSinceStartup+22;var cc=local.GetComponent<CharacterController>();
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
