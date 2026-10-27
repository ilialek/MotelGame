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
    public sealed class ServiceLoopSmokeTest : MonoBehaviour
    {
        string role, directory;
        DevelopmentSession session;
        NetworkPlayer local;
        PlayerInteractor interactor;
        ServiceRoom room;
        ReceptionDesk reception;
        NetworkDoor door;
        readonly List<string> checks = new List<string>(), errors = new List<string>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-motel-service-test");if(i<0)return;
            var test=new GameObject("ServiceLoopSmokeTest").AddComponent<ServiceLoopSmokeTest>();
            test.role=args[i+1];test.directory=args[Array.IndexOf(args,"-motel-service-results")+1];
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
        {float end=Time.realtimeSinceStartup+timeout;while(!condition()){Require(Time.realtimeSinceStartup<end,"Timeout: "+label);yield return null;}}
        bool Marked(string name)=>File.Exists(Path.Combine(directory,name+".signal"));
        void Mark(string name){File.WriteAllText(Path.Combine(directory,name+".signal"),"ready");}
        IEnumerator Barrier(string name)
        {Mark(name+"-"+role);yield return Wait(()=>Marked(name+"-host")&&Marked(name+"-client"),name);}
        static void Require(bool yes,string message){if(!yes)throw new Exception(message);}
        NetworkPlayer[] Players()=>session.Manager.SpawnManager.SpawnedObjectsList.Where(n=>n.IsPlayerObject).Select(n=>n.GetComponent<NetworkPlayer>()).ToArray();
        IEnumerator Run()
        {
            yield return null;
            session=FindFirstObjectByType<DevelopmentSession>();Require(session!=null,"Session missing");
            session.TransportSettings.Address="127.0.0.1";session.TransportSettings.Port=17779;
            Require(session.StartSession(role=="host"),"Start failed");
            yield return Wait(()=>session.Manager.IsConnectedClient && Players().Length==2,"two players");
            local=Players().Single(p=>p.IsOwner);interactor=local.GetComponent<PlayerInteractor>();
            room=FindFirstObjectByType<ServiceRoom>();reception=FindFirstObjectByType<ReceptionDesk>();door=FindFirstObjectByType<NetworkDoor>();
            Require(room!=null && room.IsSpawned && reception!=null && reception.IsSpawned && door!=null && door.IsSpawned,"Scene NetworkObjects missing");
            var remote=Players().Single(p=>!p.IsOwner);
            Require(interactor.enabled && !remote.GetComponent<PlayerInteractor>().enabled && remote.GetComponent<PlayerInteractor>().FindTarget()==null,"Remote input/raycast isolation");
            Require(local.GetComponent<FirstPersonController>().enabled && !remote.GetComponent<FirstPersonController>().enabled,"Movement ownership");
            Require(local.GetComponentInChildren<Camera>(true).enabled && !remote.GetComponentInChildren<Camera>(true).enabled,"Camera ownership");
            checks.Add("Host/Client connected; ownership, camera and remote raycast isolation verified.");
            if(role=="late")yield return Late();
            else
            {
                Require(room.State.Status==RoomStatus.Dirty && !room.State.BedMade && room.TrashRemovedCount==0,"Initial Dirty state");
                if(role=="client")
                {
                    room.RequestCleanRpc(-1,0);yield return new WaitForSecondsRealtime(.5f);
                    Require(!room.State.BedMade,"Distant clean RPC accepted");
                    yield return AtReception();OpenReception();reception.SelectRoom(0);
                    yield return new WaitForSecondsRealtime(.5f);
                    Require(room.State.Status==RoomStatus.Dirty && reception.GuestWaiting && !reception.CanRegister(room),"Dirty check-in accepted");
                    reception.RequestCheckInRpc(999,0);reception.RequestCheckoutRpc(0,0);room.RequestCleanRpc(999,0);
                    yield return new WaitForSecondsRealtime(.5f);
                    Require(room.State.Status==RoomStatus.Dirty && room.State.Cycle==0,"Malformed/invalid action altered state");
                    reception.CloseMenu();Mark("invalid-checked");
                    checks.Add("Rejected distant cleaning, Dirty check-in, invalid room/cleaning index and checkout before occupancy.");
                }
                else yield return Wait(()=>Marked("invalid-checked"),"invalid checks");
                for(uint cycle=0;cycle<2;cycle++)yield return Cycle(cycle);
            }
            session.Disconnect();yield return Wait(()=>!session.Manager.IsListening&&!session.Manager.ShutdownInProgress,"shutdown",10);
            checks.Add("Disconnect completed.");
        }
        IEnumerator Cycle(uint cycle)
        {
            yield return EnterRoom(cycle);
            bool bedWorker=(cycle==0)==(role=="host");
            yield return Walk(bedWorker?new Vector3(18.5f,0,13.2f):new Vector3(18.8f,0,12.65f));
            yield return Barrier("clean-start-"+cycle);
            if(bedWorker)Use(room.Bed,room.Bed.AimPoint);
            else
            {
                Use(room.Trash[0],room.Trash[0].AimPoint);
                // Compete for the same initially dirty bed while the other peer is making it.
                room.RequestCleanRpc(-1,cycle);
            }
            yield return Wait(()=>room.State.BedMade && room.IsCleaned(0),"parallel bed/trash");
            Require(room.State.Status==RoomStatus.Dirty,"Ready before all trash removed");
            AssertBedding(true);
            // Both peers repeat the same completed action; it must remain idempotent.
            for(int i=0;i<8;i++)room.RequestCleanRpc(-1,cycle);
            yield return new WaitForSecondsRealtime(.3f);
            Require(room.State.Status==RoomStatus.Dirty && room.TrashRemovedCount==1,"Duplicate cleaning altered progress");
            Require(!room.Bed.CanInteract && !room.Trash[0].TargetCollider.enabled && room.Trash[1].TargetCollider.enabled,"Cleaning visual/collider mismatch");
            yield return Barrier("partial-"+cycle);
            if(!bedWorker)
            {
                yield return Walk(new Vector3(18.0f,0,12.2f));
                yield return Walk(new Vector3(17.7f,0,12.2f));Use(room.Trash[1],room.Trash[1].AimPoint);
            }
            yield return Wait(()=>room.State.Status==RoomStatus.Ready,"Ready after both tasks");
            Require(room.State.BedMade && room.TrashRemovedCount==room.Trash.Length,"Incomplete Ready");
            checks.Add($"Cycle {cycle+1}: parallel actual raycast cleaning; duplicates harmless; Ready only after bed + all trash.");
            yield return Barrier("ready-"+cycle);
            // Distant registration from inside the room must also be rejected.
            reception.SelectRoom(0);yield return new WaitForSecondsRealtime(.3f);
            Require(room.State.Status==RoomStatus.Ready,"Distant registration accepted");
            yield return Barrier("distant-register-"+cycle);
            yield return Walk(new Vector3(18.1f,0,12.4f));yield return Walk(new Vector3(19.1f,0,12.65f));
            yield return Walk(new Vector3(22.2f,0,12.65f));yield return Walk(new Vector3(22.2f,0,8.4f));
            yield return Walk(new Vector3(9.5f,0,8.4f));yield return AtReception();OpenReception();
            Require(reception.CanRegister(room),"Ready room not selectable");
            yield return Barrier("checkin-"+cycle);
            for(int i=0;i<8;i++)reception.SelectRoom(0);
            yield return Wait(()=>room.State.Status==RoomStatus.Occupied,"check-in");yield return new WaitForSecondsRealtime(.4f);
            Require(!reception.GuestWaiting && !reception.WaitingGuest.activeSelf && room.GuestInRoom.activeSelf && !reception.CanRegister(room),"Guest/availability mismatch");
            room.RequestCleanRpc(-1,cycle);reception.SelectRoom(0);yield return new WaitForSecondsRealtime(.3f);
            Require(room.State.Status==RoomStatus.Occupied && room.State.Cycle==cycle,"Occupied room accepted invalid action");
            checks.Add($"Cycle {cycle+1}: reception raycast/menu, competing check-ins, synchronized guest occupancy; invalid cleaning/re-registration rejected.");
            if(cycle==0)
            {
                yield return Barrier("checkout-0");
                for(int i=0;i<8;i++)reception.Checkout(0);
                yield return Wait(()=>room.State.Cycle==1,"checkout reset");yield return null;
                AssertReset(1);room.RequestCleanRpc(-1,0);reception.RequestCheckInRpc(0,0);reception.RequestCheckoutRpc(0,0);
                yield return new WaitForSecondsRealtime(.4f);AssertReset(1);
                checks.Add("First checkout atomically reset bed/trash/guest; duplicate checkout and previous-cycle RPCs rejected.");
                reception.CloseMenu();yield return Barrier("reset-0");
            }
            else if(role=="client"){reception.CloseMenu();Mark("client-done");}
            else
            {
                yield return Wait(()=>Players().Length==1,"client disconnect before late join");Mark("ready-late");
                yield return Wait(()=>Marked("late-observed"),"late join occupancy");
                yield return Wait(()=>room.State.Cycle==2,"late client checkout");yield return null;AssertReset(2);
                checks.Add("Second checkout reset all interactions; late client received Occupied state and performed checkout.");
                yield return Wait(()=>Players().Length==1,"late disconnect");reception.CloseMenu();
            }
        }
        IEnumerator Late()
        {
            yield return null;
            Require(room.State.Cycle==1 && room.State.Status==RoomStatus.Occupied && room.State.BedMade && room.TrashRemovedCount==room.Trash.Length,"Late snapshot wrong");
            Require(!room.Trash[0].TargetCollider.enabled && !room.Trash[1].TargetCollider.enabled && room.GuestInRoom.activeSelf && !reception.WaitingGuest.activeSelf,"Late visuals wrong");
            AssertBedding(true);
            checks.Add("Late join received Occupied cycle, made bed, removed trash colliders, and correct guest location.");Mark("late-observed");
            yield return AtReception();OpenReception();reception.Checkout(0);
            yield return Wait(()=>room.State.Cycle==2,"second checkout");yield return null;AssertReset(2);
            checks.Add("Late client's reception checkout completed second full cycle; all cleaning interactions and visuals reset.");
            reception.CloseMenu();
        }
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
