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
    public sealed class MultipleGuestsSmokeTest : MonoBehaviour
    {
        string role, directory, fatal;
        DevelopmentSession session;
        NetworkPlayer local;
        PlayerInteractor interactor;
        ServiceRoom[] rooms;
        GuestLifecycle[] guests;
        ReceptionDesk reception;
        GuestServiceRequests requests;
        TowelCarrier carrier;
        readonly List<string> checks=new List<string>(), errors=new List<string>();
        readonly Dictionary<GuestLifecycle,(Vector3 p,GuestPhase phase,uint visit)> previous=new();
        readonly HashSet<GuestLifecycle> departed=new();
        float nextSample, walkedDistance;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-motel-multiple-test");if(i<0)return;
            var test=new GameObject("MultipleGuestsSmokeTest").AddComponent<MultipleGuestsSmokeTest>();
            test.role=args[i+1];test.directory=args[Array.IndexOf(args,"-motel-multiple-results")+1];
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
        {
            File.WriteAllText(Path.Combine(directory,role+".progress"),label);float end=Time.realtimeSinceStartup+timeout;
            while(!condition())
            {
                Require(fatal==null,fatal);
                Require(Time.realtimeSinceStartup<end,"Timeout: "+label+"; "+Diagnostic());yield return null;
            }
        }
        string Diagnostic()=>"local="+(local==null?"none":local.transform.position.ToString())+"; "+
            (guests==null?"":string.Join("; ",guests.Select(g=>g.GuestName+" "+g.Phase+" room="+g.Room?.RoomNumber+" pos="+g.transform.position+" blocked="+g.State.Blocked)));
        bool Marked(string name)=>File.Exists(Path.Combine(directory,name+".signal"));
        void Mark(string name){File.WriteAllText(Path.Combine(directory,name+".signal"),"ready");}
        IEnumerator Barrier(string name)
        {Mark(name+"-"+(role=="host"?"host":"client"));yield return Wait(()=>Marked(name+"-host")&&Marked(name+"-client"),name);}
        static void Require(bool yes,string message){if(!yes)throw new Exception(message);}
        NetworkPlayer[] Players()=>session.Manager.SpawnManager.SpawnedObjectsList.Where(n=>n.IsPlayerObject).Select(n=>n.GetComponent<NetworkPlayer>()).ToArray();

        void Update()
        {
            if(guests==null)return;
            foreach(var g in guests)
            {
                if(!g.IsSpawned)continue;
                if(previous.TryGetValue(g,out var prev))
                {
                    if(prev.visit==g.State.Visit && prev.phase!=GuestPhase.Absent && g.Phase!=GuestPhase.Absent)
                    {
                        float delta=Vector3.Distance(prev.p,g.transform.position);
                        if(delta>1.6f*Time.unscaledDeltaTime+.7f)fatal="NPC route teleport: "+g.GuestName+" "+delta;
                        if(role=="host")walkedDistance+=delta;
                    }
                    if(prev.phase==GuestPhase.Leaving && g.Phase==GuestPhase.Absent)
                    {
                        if(Vector3.Distance(g.transform.position,g.DeparturePoint.position)>(role=="host"?.3f:.8f))fatal="Premature departure hide: "+g.GuestName+" at "+g.transform.position;
                        departed.Add(g);
                    }
                }
                previous[g]=(g.transform.position,g.Phase,g.State.Visit);
            }
            if(role!="host"||Time.unscaledTime<nextSample)return;nextSample=Time.unscaledTime+.1f;
            if(guests.Count(g=>g.Phase==GuestPhase.Arriving||g.Phase==GuestPhase.WaitingForCheckIn)>1)fatal="Reception slot duplicated";
            if(rooms.Where(r=>r.State.Status==RoomStatus.Occupied).Select(r=>r.Occupant).Distinct().Count()!=rooms.Count(r=>r.State.Status==RoomStatus.Occupied))fatal="Guest assigned twice";
            Physics.SyncTransforms();
            foreach(var g in guests.Where(g=>g.Visible))
            foreach(var c in Physics.OverlapCapsule(g.transform.position+Vector3.up*.35f,g.transform.position+Vector3.up*1.5f,.27f,~0,QueryTriggerInteraction.Ignore))
            {
                if(c.transform.IsChildOf(g.transform)||c.GetComponentInParent<NetworkPlayer>()!=null)continue;
                if(Physics.ComputePenetration(g.Body,g.transform.position,g.transform.rotation,c,c.transform.position,c.transform.rotation,out _,out float depth)&&depth>.055f)
                    fatal="NPC collision: "+g.GuestName+" with "+c.name+" depth="+depth+" at "+g.transform.position;
            }
        }
        IEnumerator Run()
        {
            yield return null;session=FindFirstObjectByType<DevelopmentSession>();session.TransportSettings.Address="127.0.0.1";session.TransportSettings.Port=17783;
            Require(session.StartSession(role=="host"),"Start failed");
            yield return Wait(()=>session.Manager.IsConnectedClient&&Players().Length>=2,"connected");
            local=Players().Single(p=>p.IsOwner);interactor=local.GetComponent<PlayerInteractor>();carrier=local.GetComponent<TowelCarrier>();
            rooms=FindObjectsByType<ServiceRoom>(FindObjectsSortMode.None).OrderBy(r=>r.RoomNumber).ToArray();guests=FindObjectsByType<GuestLifecycle>(FindObjectsSortMode.None);
            reception=FindFirstObjectByType<ReceptionDesk>();requests=FindFirstObjectByType<GuestServiceRequests>();
            yield return Wait(()=>rooms.All(r=>r.IsSpawned)&&guests.All(g=>g.IsSpawned)&&requests.IsSpawned,"network scene");
            Require(rooms.Length==4&&guests.Length==5,"Scene pool counts");
            Require(guests.All(g=>g.OwnerClientId==NetworkManager.ServerClientId),"Guest ownership");
            if(role=="observer")
            {
                yield return Wait(()=>rooms.All(r=>r.State.Status==RoomStatus.Occupied&&r.Occupant!=null)&&requests.Phase==GuestRequestPhase.PendingDelivery,"late state",15);
                Require(requests.Room==rooms[1]&&guests.Count(g=>g.Phase==GuestPhase.InRoom)==4&&guests.Count(g=>g.CanCheckIn)==1,"Late guest/request assignment");
                Require(guests.All(g=>g.Visible&&!g.Agent.enabled)&&rooms.All(r=>r.State.BedMade&&r.TrashRemovedCount==2),"Late visuals/agents/cleaning");
                yield return Wait(()=>Players().Count(p=>p.GetComponent<TowelCarrier>().HasTowel&&p.GetComponent<TowelCarrier>().VisibleTowel)==2,"late towel visuals",10);
                Require(rooms.All(r=>Vector3.Distance(r.Occupant.transform.position,r.GuestIdle.position)<.25f),"Late positions");
                checks.Add("Late join: four occupied rooms, five NPCs, exact assignments/poses, cleaning state, pending Room102 request and two carried towel visuals; remote agents disabled.");Mark("observed");
            }
            else
            {
                yield return Walk(new Vector3(7,0,5));
                var original=new GuestLifecycle[4];
                for(int i=0;i<4;i++)
                {
                    var unaffected=rooms[0].State;
                    yield return Clean(i);
                    if(i==1)Require(rooms[0].State.Status==unaffected.Status&&rooms[0].State.Cycle==unaffected.Cycle&&rooms[0].State.Guest.Equals(unaffected.Guest)&&rooms[0].State.BedMade==unaffected.BedMade,"Cleaning102 altered101");
                    yield return CheckIn(i);original[i]=rooms[i].Occupant;
                    yield return Wait(()=>rooms[i].Occupant.IsInRoomFor(rooms[i]),"NPC reaches "+rooms[i].RoomNumber,70);
                    if(i==2)Require(rooms.Take(3).All(r=>r.State.Status==RoomStatus.Occupied&&r.Occupant.IsInRoomFor(r)&&r.Occupant.Visible),"Three simultaneous occupants");
                }
                checks.Add("Independent cleaning and concurrent registration: three required rooms plus104 occupied by four distinct physical guests.");
                yield return Wait(()=>reception.GuestWaiting,"fifth waits while full",40);
                Require(rooms.All(r=>r.State.Status==RoomStatus.Occupied)&&!rooms.Any(r=>reception.CanRegister(r)),"Full motel evicted/accepted guest");
                yield return AnswerAndCollect(0,false);yield return Deliver(0,false);
                yield return AnswerAndCollect(1,true);
                if(role=="host"){Mark("observe");yield return Wait(()=>Marked("observed"),"late observer",35);yield return Wait(()=>Players().Length==2,"observer disconnect",15);}
                yield return Barrier("observer-gone");
                uint pendingId=requests.State.RequestId;
                yield return Checkout(3);
                Require(requests.Phase==GuestRequestPhase.PendingDelivery&&requests.Room==rooms[1]&&requests.State.RequestId==pendingId&&carrier.HasTowel,"Unrelated checkout cancelled102");
                yield return Deliver(1,true);
                yield return Checkout(1);
                yield return Wait(()=>departed.Contains(original[1])&&departed.Contains(original[3]),"102/104 physical departures",80);
                Require(rooms[0].Occupant==original[0]&&rooms[2].Occupant==original[2]&&original[0].Phase==GuestPhase.InRoom&&original[2].Phase==GuestPhase.InRoom,"Independent checkout changed101/103");
                checks.Add("Room102 request: wrong101 rejected, duplicate correct deliveries complete once; checkout104 preserves102 call/carry. Specific concurrent checkout102 resets only102 and guest walks out.");
                yield return Clean(1);yield return CheckIn(1);var replacement=rooms[1].Occupant;
                Require(replacement!=original[1],"Room102 did not get a different guest");
                yield return Wait(()=>replacement.IsInRoomFor(rooms[1]),"reuse102 physical arrival",70);
                yield return Clean(3);
                // Hold the103 request while another guest starts toward104, then send103
                // in the opposite direction through the shared corridor.
                yield return AnswerAndCollect(2,true);
                yield return CheckIn(3);var crossing=rooms[3].Occupant;
                Require(crossing.Phase==GuestPhase.WalkingToRoom,"Expected newly walking104");
                yield return Checkout(2);
                yield return Wait(()=>requests.Phase==GuestRequestPhase.NoRequest&&Players().All(p=>!p.GetComponent<TowelCarrier>().HasTowel),"source checkout cancellation",5);
                requests.AnswerRpc(pendingId);requests.TakeTowelRpc(pendingId);requests.DeliverRpc(new NetworkObjectReference(rooms[1].NetworkObject),pendingId);
                yield return Wait(()=>crossing.IsInRoomFor(rooms[3])&&departed.Contains(original[2]),"opposing NPC routes finish",85);
                checks.Add("Source103 checkout cancels only its request and both towels; stale prior request rejected; departing103 and arriving104 traverse shared routes without deadlock.");
                yield return AnswerAndCollect(1,false);yield return Deliver(1,false);yield return Checkout(1);
                yield return Wait(()=>departed.Contains(replacement),"second102 departure",80);
                Require(rooms[1].State.Cycle==2,"Room102 occupancy cycles");
                yield return AnswerAndCollect(3,false);yield return Deliver(3,false);yield return Checkout(3);yield return Checkout(0);
                yield return Wait(()=>guests.All(g=>g.Phase!=GuestPhase.Leaving),"departures finish",80);
                yield return Clean(1);yield return CheckIn(1);var early=rooms[1].Occupant;uint visit=early.State.Visit;
                yield return Checkout(1);
                Require(early.Phase==GuestPhase.Leaving,"Checkout during navigation did not redirect");
                yield return Wait(()=>early.Phase==GuestPhase.Absent||early.State.Visit!=visit,"early departure",60);
                yield return new WaitForSecondsRealtime(16);
                Require(requests.Phase==GuestRequestPhase.NoRequest&&rooms.All(r=>r.State.Status==RoomStatus.Dirty),"Stale request after early checkout");
                Require(guests.Length==5&&guests.Count(g=>g.CanCheckIn)<=1,"Pool accumulated");
                checks.Add("Two complete102 occupancy/service/checkout cycles, all four room requests, full cleaning reset, later checkout while walking, bounded pool and one reception slot.");
                Require(fatal==null,fatal);
                yield return Barrier("done");
                if(role=="host")
                {Require(walkedDistance>250,"Insufficient NPC travel");checks.Add("Sampled all NPC routes: "+walkedDistance.ToString("F1")+"m; no teleport, solid/NPC penetration, duplicate assignment or waiting slot.");yield return Wait(()=>Players().Length==1,"client disconnect");Require(rooms.All(r=>r.State.Status==RoomStatus.Dirty),"Disconnect mutated rooms");}
            }
            session.Disconnect();yield return Wait(()=>!session.Manager.IsListening&&!session.Manager.ShutdownInProgress,"shutdown",10);checks.Add("Disconnect clean.");
        }
        Vector3 Map(Vector3 p,int index){if(index%2==1)p.x=44.4f-p.x;if(index>=2)p.z+=5.7f;return p;}
        IEnumerator Clean(int index)
        {
            var room=rooms[index];var door=room.Door;string key=room.RoomNumber+"-"+room.State.Cycle;
            Require(room.State.Status==RoomStatus.Dirty,"Room not Dirty");
            yield return Walk(new Vector3(9.5f,0,8.4f));yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(room.DoorOutside.position);
            if(role=="host"&&!door.IsOpen)Use(door,door.InteractionPoint);
            yield return Wait(()=>door.IsOpen&&!door.IsMoving,"door open "+key);yield return Walk(room.DoorInside.position);
            bool bedWorker=role=="host";
            yield return Walk(Map(bedWorker?new Vector3(18.5f,0,13.2f):new Vector3(18.8f,0,12.65f),index));
            yield return Barrier("clean-start-"+key);
            if(bedWorker)Use(room.Bed,room.Bed.AimPoint);else Use(room.Trash[0],room.Trash[0].AimPoint);
            yield return Wait(()=>room.State.BedMade&&room.IsCleaned(0),"partial cleaning "+key);Require(room.State.Status==RoomStatus.Dirty,"Premature Ready");
            yield return Barrier("partial-"+key);
            if(!bedWorker){yield return Walk(Map(new Vector3(18,0,12.2f),index));yield return Walk(Map(new Vector3(17.7f,0,12.2f),index));Use(room.Trash[1],room.Trash[1].AimPoint);}
            yield return Wait(()=>room.State.Status==RoomStatus.Ready,"Ready "+key);yield return Barrier("ready-"+key);
            yield return Walk(Map(new Vector3(18.1f,0,12.4f),index));yield return Walk(room.DoorInside.position);yield return Walk(room.DoorOutside.position);
            yield return Barrier("outside-"+key);if(role=="host"&&door.IsOpen)Use(door,door.InteractionPoint);
            yield return Wait(()=>!door.IsOpen&&!door.IsMoving,"closed "+key);yield return BackToLobby();
        }
        IEnumerator CheckIn(int index)
        {
            yield return Wait(()=>reception.GuestWaiting,"waiting for registration");yield return AtReception();OpenReception();
            var guest=reception.WaitingNpc;var expected=new NetworkObjectReference(guest.NetworkObject);uint visit=guest.State.Visit,cycle=rooms[index].State.Cycle;
            Require(reception.CanRegister(rooms[index]),"Ready room not registerable");yield return Barrier("checkin-"+index+"-"+cycle);
            for(int i=0;i<5;i++)reception.RequestCheckInRpc(index,cycle,expected,visit);
            yield return Wait(()=>rooms[index].State.Status==RoomStatus.Occupied,"assigned room" );
            Require(rooms[index].Occupant==guest&&guest.State.RoomCycle==cycle,"Wrong assignment");reception.CloseMenu();
            yield return Walk(new Vector3(7,0,5));
        }
        IEnumerator Checkout(int index)
        {
            var room=rooms[index];uint cycle=room.State.Cycle;
            yield return AtReception();OpenReception();yield return Barrier("checkout-"+index+"-"+cycle);
            for(int i=0;i<5;i++)reception.Checkout(index);reception.CloseMenu();
            yield return Wait(()=>room.State.Status==RoomStatus.Dirty&&room.State.Cycle==cycle+1,"checkout "+room.RoomNumber);
            Require(!room.State.BedMade&&room.TrashRemovedCount==0&&!room.State.TowelRequested&&room.State.RequestEligibleAt==0&&room.Occupant==null,"Room reset incomplete");
            Require(room.Bed.CanInteract&&room.Trash.All(t=>t.CanInteract&&t.TargetCollider.enabled),"Cleaning visual reset");
            yield return Walk(new Vector3(7,0,5));
        }
        IEnumerator AnswerAndCollect(int index,bool both)
        {
            yield return Wait(()=>requests.Phase==GuestRequestPhase.Ringing&&requests.Room==rooms[index],"phone room "+rooms[index].RoomNumber,70);
            uint id=requests.State.RequestId;
            yield return AtReception();yield return Walk(new Vector3(9.5f,0,role=="host"?5.3f:6.15f));yield return Barrier("answer-"+id);
            if(role=="host")Use(requests.Phone,requests.Phone.AimPoint);
            yield return Wait(()=>requests.Phase==GuestRequestPhase.PendingDelivery,"answered "+id);
            requests.AnswerRpc(id);
            if(both||role=="client")
            {yield return ToSupply();Use(requests.TowelSupply,requests.TowelSupply.AimPoint);yield return Wait(()=>carrier.HasTowel&&carrier.VisibleTowel,"towel "+id);requests.TakeTowelRpc(id);yield return FromSupply();}
            yield return Barrier("collected-"+id);
        }
        IEnumerator Deliver(int index,bool wrong)
        {
            uint id=requests.State.RequestId;
            if(wrong)
            {
                yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(new Vector3(22.2f,0,rooms[0].Delivery.AimPoint.z));
                requests.DeliverRpc(new NetworkObjectReference(rooms[0].NetworkObject),id);yield return new WaitForSecondsRealtime(.5f);
                Require(requests.Phase==GuestRequestPhase.PendingDelivery&&carrier.HasTowel,"Wrong room accepted");
            }
            if(wrong||role=="client")
            {
                if(!wrong)yield return Walk(new Vector3(22.2f,0,8.4f));
                yield return Walk(new Vector3(22.2f,0,rooms[index].Delivery.AimPoint.z));
            }
            yield return Barrier("delivery-"+id);
            if(!wrong&&role=="client")Use(rooms[index].Delivery,rooms[index].Delivery.AimPoint);
            if(wrong||role=="client")for(int i=0;i<5;i++)requests.DeliverRpc(new NetworkObjectReference(rooms[index].NetworkObject),id);
            yield return Wait(()=>requests.Phase==GuestRequestPhase.Completed&&requests.State.RequestId==id,"delivered "+id);
            yield return Wait(()=>Players().All(p=>!p.GetComponent<TowelCarrier>().HasTowel&&!p.GetComponent<TowelCarrier>().VisibleTowel),"towels consumed");
            if(wrong||role=="client")yield return BackToLobby();
        }
        IEnumerator BackToLobby(){yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(new Vector3(9.5f,0,8.4f));yield return Walk(new Vector3(7,0,5));}
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
