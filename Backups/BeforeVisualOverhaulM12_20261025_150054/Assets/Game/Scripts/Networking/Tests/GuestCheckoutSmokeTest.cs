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
    public sealed class GuestCheckoutSmokeTest : MonoBehaviour
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
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-motel-checkout-test");if(i<0)return;
            var test=new GameObject("GuestCheckoutSmokeTest").AddComponent<GuestCheckoutSmokeTest>();
            test.role=args[i+1];test.directory=args[Array.IndexOf(args,"-motel-checkout-results")+1];
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
            if(guests.Count(g=>g.Phase==GuestPhase.Arriving||g.Phase==GuestPhase.WaitingForCheckIn||g.Phase==GuestPhase.ReturningForCheckout||g.Phase==GuestPhase.WaitingForCheckout)>1)fatal="Reception slot duplicated";
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
            yield return null;session=FindFirstObjectByType<DevelopmentSession>();session.TransportSettings.Address="127.0.0.1";session.TransportSettings.Port=17789;
            Require(session.StartSession(role=="host"),"Start failed");
            yield return Wait(()=>session.Manager.IsConnectedClient&&Players().Length>=2,"connected");
            local=Players().Single(p=>p.IsOwner);interactor=local.GetComponent<PlayerInteractor>();carrier=local.GetComponent<TowelCarrier>();
            rooms=FindObjectsByType<ServiceRoom>(FindObjectsSortMode.None).OrderBy(r=>r.RoomNumber).ToArray();guests=FindObjectsByType<GuestLifecycle>(FindObjectsSortMode.None);
            reception=FindFirstObjectByType<ReceptionDesk>();requests=FindFirstObjectByType<GuestServiceRequests>();
            yield return Wait(()=>rooms.All(r=>r.IsSpawned)&&guests.All(g=>g.IsSpawned)&&requests.IsSpawned,"network scene");
            Require(rooms.Length==4&&guests.Length==5,"Scene pool counts");
            Require(guests.All(g=>g.OwnerClientId==NetworkManager.ServerClientId),"Guest ownership");
            if(role.StartsWith("observer-"))
            {
                var expected=role=="observer-returning"?GuestPhase.ReturningForCheckout:role=="observer-waiting"?GuestPhase.WaitingForCheckout:GuestPhase.Leaving;
                yield return Wait(()=>guests.Any(g=>g.Phase==expected && g.Room==rooms[1]),"late "+expected,12);
                var guest=guests.Single(g=>g.Phase==expected&&g.Room==rooms[1]);
                Require(guest.Visible&&!guest.Agent.enabled&&guest.State.StayEndsAt>0,"Late guest pose/presentation");
                Require(rooms[1].State.Status==(expected==GuestPhase.Leaving?RoomStatus.Dirty:RoomStatus.Occupied),"Late room state");
                if(expected==GuestPhase.WaitingForCheckout)
                {
                    Require(Vector3.Distance(guest.transform.position,guest.ReceptionPoint.position)<.3f,"Late Reception pose");
                    Require(guest.GetComponentsInChildren<TextMesh>().Any(t=>t.text.Contains("Room 102 — Ready to check out")),"Late checkout intent label missing room number");
                }
                else Require(Vector3.Distance(guest.transform.position,guest.ArrivalPoint.position)>2,"Late pose incorrectly reset to arrival");
                if(expected==GuestPhase.Leaving)
                    yield return Wait(()=>requests.Room!=rooms[2]&&Players().All(x=>!x.GetComponent<TowelCarrier>().HasTowel),"late next guest cancellation",10);
                else
                {
                    Require(requests.Phase==GuestRequestPhase.PendingDelivery && requests.Room==rooms[2],"Unrelated late request state");
                    yield return Wait(()=>Players().Count(x=>x.GetComponent<TowelCarrier>().HasTowel)==2,"late carried towels",10);
                }
                checks.Add("Late join "+expected+": exact guest/room/request state, correct physical pose, visible model, remote NavMeshAgent disabled; carried towels match current request lifecycle.");
                Mark(role+"-observed");
            }
            else
            {
                yield return Walk(new Vector3(7,0,5));
                // Inspector-equivalent tuning on the host before registration only. No phase,
                // position, deadline or room snapshot is injected by this test.
                for(int i=0;i<4;i++)yield return Clean(i);
                yield return Register(0,900);var original0=rooms[0].Occupant;
                yield return Register(1,90);var original1=rooms[1].Occupant;
                float thirdDuration=(float)(original1.State.StayEndsAt-session.Manager.ServerTime.Time+5);
                yield return Register(2,thirdDuration);var original2=rooms[2].Occupant;
                Require(rooms.Take(3).All(r=>r.State.Status==RoomStatus.Occupied&&r.Occupant.IsInRoomFor(r)),"Three simultaneous occupants");
                checks.Add("Three distinct physical guests occupy101/102/103; deadlines start only at InRoom.");
                yield return AnswerAndCollect(0,false);yield return Deliver(0,false);
                yield return AnswerAndCollect(1,true);yield return Deliver(1,true);
                yield return AnswerAndCollect(2,true);uint cancelled=requests.State.RequestId;
                yield return Wait(()=>original1.CheckoutDue&&original2.CheckoutDue&&reception.GuestWaiting,"two stays expire behind arriving guest",240);
                Require(original1.Phase==GuestPhase.InRoom&&original2.Phase==GuestPhase.InRoom,"Arrival/return contention broke slot reservation");
                checks.Add("Arrival owns Reception while two stays expire. Both ready guests wait inside assigned Occupied rooms; pending103 is unaffected.");
                yield return Register(3,900,false); // Frees existing arrival slot for overdue returns.
                yield return Wait(()=>original1.Phase==GuestPhase.ReturningForCheckout,"102 begins return",20);
                Require(original2.Phase==GuestPhase.InRoom&&rooms[1].State.Status==RoomStatus.Occupied&&!rooms[1].CanClean(-1),"Return changed room or stole slot");
                if(role=="host")yield return Observe("returning");
                yield return Barrier("return-observed");
                yield return Wait(()=>original1.Phase==GuestPhase.WaitingForCheckout,"102 at desk",70);
                if(role=="host")yield return Observe("waiting");
                yield return Barrier("waiting-observed");
                // Attempt valid checkout remotely, then invalid identities/cycles at the desk.
                var exact=new NetworkObjectReference(original1.NetworkObject);uint visit=original1.State.Visit;
                reception.RequestCheckoutRpc(1,0,exact,visit);yield return new WaitForSecondsRealtime(.5f);
                Require(rooms[1].State.Status==RoomStatus.Occupied,"Remote checkout accepted");
                yield return AtReception();OpenReception();
                reception.RequestCheckoutRpc(0,rooms[0].State.Cycle,new NetworkObjectReference(original0.NetworkObject),original0.State.Visit);
                reception.RequestCheckoutRpc(1,0,default,visit);reception.RequestCheckoutRpc(1,0,exact,visit+1);reception.RequestCheckoutRpc(1,99,exact,visit);
                yield return new WaitForSecondsRealtime(.5f);Require(rooms.Take(3).All(r=>r.State.Status==RoomStatus.Occupied),"Invalid checkout mutated state");
                reception.CloseMenu();yield return ProcessCheckout(1,"client");
                Require(rooms[0].Occupant==original0&&rooms[2].Occupant==original2,"102 checkout changed other occupants");
                if(role=="host")yield return Observe("leaving");
                yield return Barrier("leaving-observed");
                checks.Add("Client checkout102: nonexistent guest, stale visit/cycle, remote reach and other occupied room rejected; only102 becomes Dirty; key return resets all cleaning. Late join covered all three checkout phases.");
                yield return Wait(()=>original2.Phase==GuestPhase.ReturningForCheckout||original2.Phase==GuestPhase.WaitingForCheckout,"next expired stay obtains slot",30);
                yield return Wait(()=>requests.Room!=rooms[2]&&Players().All(x=>!x.GetComponent<TowelCarrier>().HasTowel&&!x.GetComponent<TowelCarrier>().VisibleTowel),"103 cancellation and towels",5);
                requests.AnswerRpc(cancelled);requests.TakeTowelRpc(cancelled);requests.DeliverRpc(new NetworkObjectReference(rooms[2].NetworkObject),cancelled);
                yield return ProcessCheckout(2,"both");
                yield return Wait(()=>departed.Contains(original1)&&departed.Contains(original2),"physical departures102/103",50);
                checks.Add("Oldest expired stay wins, then103 returns independently. Its unfinished request and both towels cancel on return; stale service RPCs rejected. Concurrent Host/Client checkout resets103 once.");
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-motel-checkout-initial-only")>=0)
                {
                    checks.Add("Targeted final-build regression: physical returns/checkouts, exact late-join checkout label, three observer phases, request cancellation, independent rooms and concurrent checkout.");
                    yield return Barrier("targeted-done");
                    if(role=="host"){checks.Add("NPC travel: "+walkedDistance.ToString("F1")+"m");yield return Wait(()=>Players().Length==1,"client shutdown");}
                    session.Disconnect();yield return Wait(()=>!session.Manager.IsListening&&!session.Manager.ShutdownInProgress,"shutdown",10);
                    yield break;
                }
                // Second102 cycle: normal service, disconnect while waiting, Host accepts key.
                yield return Clean(1);yield return Register(1,180);var second=rooms[1].Occupant;
                yield return Clean(2);yield return Register(2,900);
                yield return AnswerAndCollect(3,false);yield return Deliver(3,false);
                yield return AnswerAndCollect(1,false);yield return Deliver(1,false);
                yield return Wait(()=>requests.Phase==GuestRequestPhase.Ringing&&requests.Room==rooms[2],"next independent103 request",30);
                uint independentRequest=requests.State.RequestId;
                yield return Wait(()=>second.Phase==GuestPhase.WaitingForCheckout,"second102 waiting",220);
                Require(requests.Phase==GuestRequestPhase.Ringing&&requests.Room==rooms[2]&&requests.State.RequestId==independentRequest,"Return cancelled another room request");
                yield return Barrier("before-disconnect");
                if(role=="client")
                {
                    session.Disconnect();yield return Wait(()=>!session.Manager.IsListening&&!session.Manager.ShutdownInProgress,"client disconnect",15);
                    previous.Clear();Mark("client-disconnected");yield return Wait(()=>Marked("host-checkout-after-disconnect"),"host checkout without client",40);
                    Require(session.StartSession(false),"Reconnect failed");yield return Wait(()=>session.Manager.IsConnectedClient&&Players().Length==2,"reconnect",30);
                    local=Players().Single(x=>x.IsOwner);interactor=local.GetComponent<PlayerInteractor>();carrier=local.GetComponent<TowelCarrier>();
                    yield return Wait(()=>rooms[1].IsSpawned&&rooms[1].State.Cycle==2&&rooms[1].State.Status==RoomStatus.Dirty,"reconnected room snapshot");
                    yield return Walk(new Vector3(7,0,5));
                }
                else
                {
                    yield return Wait(()=>Marked("client-disconnected")&&Players().Length==1,"server observes disconnect",20);
                    Require(second.Phase==GuestPhase.WaitingForCheckout&&rooms[1].State.Status==RoomStatus.Occupied,"Disconnect mutated checkout");
                    yield return AtReception();OpenReception();reception.Checkout(1);reception.CloseMenu();
                    yield return Wait(()=>rooms[1].State.Cycle==2&&second.Phase==GuestPhase.Leaving,"remaining Host checkout");
                    Mark("host-checkout-after-disconnect");yield return Walk(new Vector3(7,0,5));
                }
                yield return Barrier("reconnected");
                checks.Add("Second full102 service cycle: unrelated103 request survives return; Client disconnects at WaitingForCheckout, Host checks out, Client reconnects to Dirty cycle2.");
                // Third102 cycle uses the actual default120; initial and second cycles also
                // completed full cleaning/service. A reused NPC's old identity is stale.
                yield return Clean(1);yield return Register(1,120);var third=rooms[1].Occupant;
                yield return AnswerAndCollect(2,false);yield return Deliver(2,false);
                yield return AnswerAndCollect(1,false);yield return Deliver(1,false);
                yield return Wait(()=>third.Phase==GuestPhase.InRoom&&requests.Phase==GuestRequestPhase.Completed,"third102 serviced");
                yield return Wait(()=>third.Phase==GuestPhase.WaitingForCheckout,"default120 checkout",180);
                Require(requests.Phase==GuestRequestPhase.Completed&&requests.Room==rooms[1],"Own Completed request cleared during return");
                yield return AtReception();
                reception.RequestCheckoutRpc(1,rooms[1].State.Cycle,exact,visit);yield return new WaitForSecondsRealtime(.5f);
                Require(rooms[1].State.Status==RoomStatus.Occupied,"Old guest snapshot checked out new stay");
                uint thirdVisit=third.State.Visit;
                yield return ProcessCheckout(1,"both");yield return Wait(()=>third.Phase==GuestPhase.Absent||third.State.Visit!=thirdVisit,"third physical departure",50);
                Require(rooms[1].State.Cycle==3&&rooms[0].State.Status==RoomStatus.Occupied&&rooms[3].State.Status==RoomStatus.Occupied,"Final independent cycles");
                Require(guests.Length==5&&guests.Count(g=>g.CanCheckIn)<=1,"Pool grew");
                checks.Add("Three102 turnover cycles with physical stays, service and checkout, including default120 seconds and Completed request retained during return; stale old identity cannot evict replacement. Cleaning resets/reuse verified; bounded guest pool.");
                Require(fatal==null,fatal);yield return Barrier("done");
                if(role=="host")
                {Require(walkedDistance>200,"Insufficient NPC travel");checks.Add("Sampled NPC travel "+walkedDistance.ToString("F1")+"m: no teleport, solid/NPC penetration, duplicate assignment or service slot.");yield return Wait(()=>Players().Length==1,"client shutdown");}
            }
            session.Disconnect();yield return Wait(()=>!session.Manager.IsListening&&!session.Manager.ShutdownInProgress,"shutdown",10);checks.Add("Disconnect clean.");
        }
        IEnumerator Observe(string phase)
        {
            Mark("observe-"+phase);yield return Wait(()=>Marked("observer-"+phase+"-observed"),"late observer "+phase,20);
            yield return Wait(()=>Players().Length==2,"late observer disconnected",15);
        }
        IEnumerator Register(int index,float duration,bool waitForRoom=true)
        {
            yield return Wait(()=>reception.GuestWaiting,"registration waiting");
            if(role=="host")typeof(GuestLifecycle).GetField("stayDuration",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(reception.WaitingNpc,duration);
            yield return CheckIn(index);var guest=rooms[index].Occupant;
            Require(guest.State.StayEndsAt==0&&guest.Phase==GuestPhase.WalkingToRoom,"Timer started before physical arrival");
            if(waitForRoom)
            {
                yield return Wait(()=>guest.IsInRoomFor(rooms[index]),"physical room arrival "+index,75);
                double remaining=guest.State.StayEndsAt-session.Manager.ServerTime.Time;
                if(role=="host")Require(Math.Abs(remaining-duration)<1.5,"Stay duration not applied after arrival");
            }
        }
        IEnumerator ProcessCheckout(int index,string actor)
        {
            var room=rooms[index];var guest=room.Occupant;uint cycle=room.State.Cycle,visit=guest.State.Visit;
            yield return Wait(()=>guest.Phase==GuestPhase.WaitingForCheckout,"checkout waiting "+room.RoomNumber,85);
            yield return AtReception();OpenReception();
            Require(reception.CheckoutNpc==guest&&!reception.GuestWaiting,"Checkout/check-in intent ambiguity");
            var reference=new NetworkObjectReference(guest.NetworkObject);
            yield return Barrier("checkout-"+index+"-"+cycle);
            if(actor=="both"||actor==role)for(int i=0;i<5;i++)reception.RequestCheckoutRpc(index,cycle,reference,visit);
            reception.CloseMenu();
            yield return Wait(()=>room.State.Status==RoomStatus.Dirty&&room.State.Cycle==cycle+1,"checkout "+room.RoomNumber);
            Require(!room.State.BedMade&&room.TrashRemovedCount==0&&!room.State.TowelRequested&&room.State.RequestEligibleAt==0&&room.Occupant==null,"Room reset incomplete");
            Require(room.Bed.CanInteract&&room.Trash.All(t=>t.CanInteract&&t.TargetCollider.enabled),"Cleaning visual reset");
            yield return Walk(new Vector3(7,0,5));
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
