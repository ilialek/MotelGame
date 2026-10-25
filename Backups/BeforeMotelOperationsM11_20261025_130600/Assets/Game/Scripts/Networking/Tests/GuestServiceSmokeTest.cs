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
    public sealed class GuestServiceSmokeTest : MonoBehaviour
    {
        string role, directory;
        DevelopmentSession session;
        NetworkPlayer local;
        PlayerInteractor interactor;
        ServiceRoom room;
        GuestServiceRequests requests;
        TowelCarrier carrier;
        ReceptionDesk reception;
        NetworkDoor door;
        readonly List<string> checks = new List<string>(), errors = new List<string>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-motel-guest-test");if(i<0)return;
            var test=new GameObject("GuestServiceSmokeTest").AddComponent<GuestServiceSmokeTest>();
            test.role=args[i+1];test.directory=args[Array.IndexOf(args,"-motel-guest-results")+1];
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
        {File.WriteAllText(Path.Combine(directory,role+".progress"),label);float end=Time.realtimeSinceStartup+timeout;while(!condition()){Require(Time.realtimeSinceStartup<end,"Timeout: "+label);yield return null;}}
        bool Marked(string name)=>File.Exists(Path.Combine(directory,name+".signal"));
        void Mark(string name){File.WriteAllText(Path.Combine(directory,name+".signal"),"ready");}
        IEnumerator Barrier(string name)
        {Mark(name+"-"+(role=="host"?"host":"client"));yield return Wait(()=>Marked(name+"-host")&&Marked(name+"-client"),name);}
        static void Require(bool yes,string message){if(!yes)throw new Exception(message);}
        NetworkPlayer[] Players()=>session.Manager.SpawnManager.SpawnedObjectsList.Where(n=>n.IsPlayerObject).Select(n=>n.GetComponent<NetworkPlayer>()).ToArray();
        IEnumerator Run()
        {
            yield return null;
            session=FindFirstObjectByType<DevelopmentSession>();
            session.TransportSettings.Address="127.0.0.1";session.TransportSettings.Port=17780;
            Require(session.StartSession(role=="host"),"Start failed");
            yield return Wait(()=>session.Manager.IsConnectedClient && Players().Length==2,"two players");
            local=Players().Single(p=>p.IsOwner);interactor=local.GetComponent<PlayerInteractor>();carrier=local.GetComponent<TowelCarrier>();
            room=FindFirstObjectByType<ServiceRoom>();reception=FindFirstObjectByType<ReceptionDesk>();door=FindFirstObjectByType<NetworkDoor>();
            requests=FindFirstObjectByType<GuestServiceRequests>();
            Require(requests!=null && requests.IsSpawned && requests.ConfigurationValid(),"M5 configuration/spawn");
            var remote=Players().Single(p=>!p.IsOwner);
            Require(interactor.enabled && !remote.GetComponent<PlayerInteractor>().enabled && remote.GetComponent<PlayerInteractor>().FindTarget()==null,"Remote interaction ownership");
            Require(local.GetComponentInChildren<Camera>(true).enabled && !remote.GetComponentInChildren<Camera>(true).enabled,"Camera ownership");
            if(role=="late")
            {
                yield return Wait(()=>requests.Phase==GuestRequestPhase.PendingDelivery && remote.GetComponent<TowelCarrier>().HasTowel,"late active snapshot");
                Require(room.State.Cycle==1 && room.State.Status==RoomStatus.Occupied && remote.GetComponent<TowelCarrier>().VisibleTowel,"Late occupancy/carry visual");
                Require(!carrier.HasTowel && !carrier.VisibleTowel,"Late player inherited another player's towel");
                checks.Add("Late join: Pending Delivery + Occupied cycle 1, remote carried towel visible, own hands empty.");
                Mark("late-observed");
                yield return Wait(()=>requests.Phase==GuestRequestPhase.Completed,"late observes delivery");
                yield return FinishCycle(1);
                yield return Cycle(2);yield return Cycle(3);
            }
            else
            {
                Require(room.State.Status==RoomStatus.Dirty && requests.Phase==GuestRequestPhase.NoRequest,"Initial state");
                requests.AnswerRpc(0);requests.TakeTowelRpc(0);requests.DeliverRpc(new NetworkObjectReference(room.NetworkObject),0);
                yield return new WaitForSecondsRealtime(.4f);
                Require(requests.Phase==GuestRequestPhase.NoRequest && !carrier.HasTowel,"Invalid idle actions accepted");
                yield return Cycle(0);yield return Cycle(1);
                if(role=="host"){yield return Cycle(2);yield return Cycle(3);yield return Wait(()=>Players().Length==1,"late disconnect");}
            }
            session.Disconnect();yield return Wait(()=>!session.Manager.IsListening&&!session.Manager.ShutdownInProgress,"shutdown",10);
            checks.Add("Ownership isolation and disconnect verified.");
        }
        IEnumerator Cycle(uint cycle)
        {
            yield return PrepareAndCheckIn(cycle);
            double callAt=requests.State.CallAt;
            yield return Wait(()=>requests.State.CallAt>0 || requests.Phase==GuestRequestPhase.Ringing,"scheduled call");
            if(requests.Phase==GuestRequestPhase.NoRequest)
            {
                Require(requests.State.CallAt-session.Manager.ServerTime.Time<=requests.CallDelay+.2,"Delay configuration");
                requests.AnswerRpc(cycle);requests.TakeTowelRpc(cycle);
                yield return new WaitForSecondsRealtime(.3f);
                Require(!carrier.HasTowel,"Pickup before answer accepted");
            }
            if(cycle==3)
            {
                // Fresh check-in after cancellation must create a new call and keep ringing until checkout.
                yield return Wait(()=>requests.Phase==GuestRequestPhase.Ringing,"fresh call after cancelled cycle",30);
                yield return new WaitForSecondsRealtime(1);
                Require(requests.Phase==GuestRequestPhase.Ringing,"Call stopped without answer");
                yield return FinishCycle(cycle);
                checks.Add("New check-in after cancellation rings normally; checkout cancels ringing and stops audio.");
                yield break;
            }
            yield return Wait(()=>requests.Phase==GuestRequestPhase.Ringing,"phone rings after server delay",30);
            if(callAt>0)Require(session.Manager.ServerTime.Time+.25>=callAt,"Call fired too early");
            requests.TakeTowelRpc(cycle);requests.DeliverRpc(new NetworkObjectReference(room.NetworkObject),cycle);
            yield return new WaitForSecondsRealtime(.3f);Require(requests.Phase==GuestRequestPhase.Ringing && !carrier.HasTowel,"Unanswered request delivered");
            yield return Walk(new Vector3(9.5f,0,role=="host"?5.3f:6.15f));
            yield return Barrier("answer-ready-"+cycle);
            bool answerer=cycle==0?role=="host":role!="host";
            if(cycle==2) { if(requests.Phone.CanInteract) Use(requests.Phone,requests.Phone.AimPoint);requests.AnswerRpc(cycle); }
            else if(answerer)Use(requests.Phone,requests.Phone.AimPoint);
            yield return Wait(()=>requests.Phase==GuestRequestPhase.PendingDelivery,"shared answered request");
            for(int i=0;i<8;i++)requests.AnswerRpc(cycle);
            yield return new WaitForSecondsRealtime(.25f);
            Require(requests.Phase==GuestRequestPhase.PendingDelivery && requests.State.CallAt==0,"Repeated answer changed request");
            Require(!requests.Ringtone.isPlaying,"Ringtone did not stop");
            requests.DeliverRpc(new NetworkObjectReference(room.NetworkObject),cycle);requests.TakeTowelRpc(cycle);
            yield return new WaitForSecondsRealtime(.3f);
            Require(!carrier.HasTowel && requests.Phase==GuestRequestPhase.PendingDelivery,"Empty delivery/distant pickup accepted");
            checks.Add($"Cycle {cycle}: call delay, answer, shared Pending, rejection of idle/ringing/duplicate/distant actions.");
            bool deliverer=cycle==0?role!="host":role=="host";
            if(deliverer || cycle==2)
            {
                yield return ToSupply();Use(requests.TowelSupply,requests.TowelSupply.AimPoint);
                yield return Wait(()=>carrier.HasTowel && carrier.VisibleTowel,"towel pickup and own visual");
                for(int i=0;i<8;i++)requests.TakeTowelRpc(cycle);
                // Send another valid NetworkObject identity to exercise wrong-room rejection.
                requests.DeliverRpc(new NetworkObjectReference(reception.NetworkObject),cycle);
                requests.DeliverRpc(new NetworkObjectReference(room.NetworkObject),cycle);
                yield return new WaitForSecondsRealtime(.3f);
                Require(carrier.HasTowel && requests.Phase==GuestRequestPhase.PendingDelivery,"Wrong-room/distant delivery accepted");
            }
            yield return Wait(()=>Players().Any(p=>p.GetComponent<TowelCarrier>().HasTowel),"shared carry state");
            foreach(var p in Players())Require(p.GetComponent<TowelCarrier>().HasTowel==p.GetComponent<TowelCarrier>().VisibleTowel,"Carry visual mismatch");
            if(cycle==1)
            {
                if(role=="client") { checks.Add("Client answered; host picked up; remote carried towel visible before late replacement.");yield break; }
                yield return Wait(()=>Players().Length==1,"replace client with late join");Mark("ready-late");
                yield return Wait(()=>Marked("late-observed"),"late sees pending and towel");
            }
            if(cycle==2)
            {
                yield return Barrier("both-carry-2");
                yield return FromSupply();
                if(role=="host")
                {
                    yield return AtReception();OpenReception();reception.Checkout(0);reception.CloseMenu();
                }
                yield return Wait(()=>room.State.Cycle==3 && requests.Phase==GuestRequestPhase.NoRequest && !carrier.HasTowel,"checkout cancels pending and clears carry");
                requests.DeliverRpc(new NetworkObjectReference(room.NetworkObject),cycle);requests.TakeTowelRpc(cycle);requests.AnswerRpc(cycle);
                yield return new WaitForSecondsRealtime(.4f);AssertReset(3);
                Require(!carrier.VisibleTowel && Players().All(p=>!p.GetComponent<TowelCarrier>().HasTowel && !p.GetComponent<TowelCarrier>().VisibleTowel),"Stale towels after checkout");
                yield return Barrier("cancelled-2");
                checks.Add("Simultaneous answer/pickup; one towel each; checkout cancels Pending, clears both carry visuals and rejects stale RPCs.");
                yield break;
            }
            if(deliverer)
            {
                yield return FromSupply();yield return ToDelivery();Use(requests.Delivery,requests.Delivery.AimPoint);
                for(int i=0;i<12;i++)requests.DeliverRpc(new NetworkObjectReference(room.NetworkObject),cycle);
            }
            yield return Wait(()=>requests.Phase==GuestRequestPhase.Completed,"delivery completes on both peers");
            yield return new WaitForSecondsRealtime(.4f);
            Require(room.State.Status==RoomStatus.Occupied && !carrier.HasTowel && !carrier.VisibleTowel,"Delivery changed occupancy or left towel");
            Require(Players().All(p=>!p.GetComponent<TowelCarrier>().HasTowel && !p.GetComponent<TowelCarrier>().VisibleTowel),"Remote towel not consumed");
            Require(!requests.Delivery.CanInteract && !requests.TowelSupply.CanInteract,"Completed interactions still available");
            checks.Add($"Cycle {cycle}: {(role=="host"?"host":"client")} observes Completed, room remains Occupied; duplicate delivery harmless, towels consumed.");
            if(deliverer) { yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(new Vector3(9.5f,0,8.4f)); }
            yield return FinishCycle(cycle);
        }
        IEnumerator FinishCycle(uint cycle)
        {
            yield return AtReception();OpenReception();yield return Barrier("checkout-"+cycle);
            for(int i=0;i<5;i++)reception.Checkout(0);
            yield return Wait(()=>room.State.Cycle==cycle+1 && requests.Phase==GuestRequestPhase.NoRequest,"checkout all state reset");
            yield return new WaitForSecondsRealtime(.3f);AssertReset(cycle+1);
            Require(!requests.Ringtone.isPlaying && !carrier.HasTowel && !carrier.VisibleTowel,"Checkout presentation not reset");
            requests.AnswerRpc(cycle);requests.TakeTowelRpc(cycle);requests.DeliverRpc(new NetworkObjectReference(room.NetworkObject),cycle);
            yield return new WaitForSecondsRealtime(.3f);AssertReset(cycle+1);reception.CloseMenu();
            yield return Barrier("reset-"+cycle);
            checks.Add($"Cycle {cycle}: checkout restored Dirty bed/trash/guest, no request/carry; stale requests rejected.");
        }
        IEnumerator PrepareAndCheckIn(uint cycle)
        {
            yield return EnterRoom(cycle);
            bool bedWorker=(cycle%2==0)==(role=="host");
            yield return Walk(bedWorker?new Vector3(18.5f,0,13.2f):new Vector3(18.8f,0,12.65f));
            yield return Barrier("clean-start-"+cycle);
            if(bedWorker)Use(room.Bed,room.Bed.AimPoint);else Use(room.Trash[0],room.Trash[0].AimPoint);
            yield return Wait(()=>room.State.BedMade && room.IsCleaned(0),"M4 partial cleaning");
            Require(room.State.Status==RoomStatus.Dirty,"Premature Ready");yield return Barrier("partial-"+cycle);
            if(!bedWorker){yield return Walk(new Vector3(18,0,12.2f));yield return Walk(new Vector3(17.7f,0,12.2f));Use(room.Trash[1],room.Trash[1].AimPoint);}
            yield return Wait(()=>room.State.Status==RoomStatus.Ready,"M4 Ready");
            yield return Barrier("ready-"+cycle);
            yield return Walk(new Vector3(18.1f,0,12.4f));yield return Walk(new Vector3(19.1f,0,12.65f));
            yield return Walk(new Vector3(22.2f,0,12.65f));yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(new Vector3(9.5f,0,8.4f));
            yield return AtReception();OpenReception();yield return Barrier("checkin-"+cycle);reception.SelectRoom(0);
            yield return Wait(()=>room.State.Status==RoomStatus.Occupied,"M4 check-in");reception.CloseMenu();
            yield return Wait(()=>room.GuestInRoom.activeSelf && !reception.WaitingGuest.activeSelf,"M4 guest presentation");
            Require(room.State.Cycle==cycle && room.GuestInRoom.activeSelf && !reception.WaitingGuest.activeSelf,"M4 guest state");
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
        void AssertBedding(bool made)
        {
            Require(room.transform.Find("MadeBedding").gameObject.activeSelf==made &&
                room.transform.Find("UnmadeBedding").gameObject.activeSelf!=made,"Bed visual state mismatch");
        }
        void AssertReset(uint cycle)
        {
            AssertBedding(false);
            Require(room.State.Cycle==cycle && room.State.Status==RoomStatus.Dirty && !room.State.BedMade && room.TrashRemovedCount==0,"Reset snapshot incorrect");
            Require(room.Bed.CanInteract && room.Trash.All(t=>t.CanInteract&&t.TargetCollider.enabled) && reception.GuestWaiting && reception.WaitingGuest.activeSelf && !room.GuestInRoom.activeSelf,"Reset visuals/interactions incorrect");
        }
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
