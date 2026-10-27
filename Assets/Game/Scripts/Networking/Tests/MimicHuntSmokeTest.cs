#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using MotelGame.Horror;
using MotelGame.Service;
using MotelGame.Interactions;
namespace MotelGame.Networking.Tests
{
    [DefaultExecutionOrder(250)]
    public sealed class MimicHuntSmokeTest:MonoBehaviour
    {
        string role,mode,dir;DevelopmentSession session;MimicHunt hunt;NetworkObject own;PlayerInteractor user;
        readonly List<string> checks=new(),errors=new();
        Vector3 previous;uint revision;float previousAt;bool sampled;float ductDistance;
        bool Host=>role=="host"||role=="solo";bool Solo=>role=="solo";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Launch()
        {
            var role=OnlineIdentity.Argument("-motel-mimic-test");if(role==null)return;
            var t=new GameObject("M14SmokeTest").AddComponent<MimicHuntSmokeTest>();t.role=role;t.mode=OnlineIdentity.Argument("-motel-mimic-mode")??"success";t.dir=OnlineIdentity.Argument("-motel-mimic-results");
        }
        void Awake()=>Application.logMessageReceived+=Log;
        void Log(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(text);}
        void Require(bool value,string why){if(!value)throw new Exception(why);}
        void Mark(string name)=>File.WriteAllText(Path.Combine(dir,name+".signal"),"ready");bool Has(string name)=>File.Exists(Path.Combine(dir,name+".signal"));
        IEnumerator Wait(Func<bool> condition,string step,float seconds=100)
        {File.WriteAllText(Path.Combine(dir,role+".progress"),step);float end=Time.realtimeSinceStartup+seconds;while(!condition()){Require(Time.realtimeSinceStartup<end,"Timeout "+step+" at="+(own!=null?own.transform.position.ToString():"no player")+" mimic="+hunt?.State.Phase+" hp="+hunt?.State.Health+" shift="+hunt?.Shift.State.Phase+"/"+hunt?.Shift.State.Number+" feedback="+user?.Feedback);yield return null;}}
        IEnumerator Barrier(string name){if(Solo)yield break;Mark(name+"-"+role);yield return Wait(()=>Has(name+"-host")&&Has(name+"-client"),name,300);}
        IEnumerator Start()
        {
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0)
            {
                object n=null;bool more=false;Exception failure=null;try{more=stack.Peek().MoveNext();if(more)n=stack.Peek().Current;}catch(Exception e){failure=e;}
                if(failure!=null){Finish(false,failure.ToString());yield break;}
                if(!more){stack.Pop();continue;}if(n is IEnumerator sub)stack.Push(sub);else yield return n;
            }
            Finish(errors.Count==0,"Completed; no critical runtime logs.");
        }
        void LateUpdate()
        {
            if(hunt==null||!hunt.IsSpawned||!hunt.State.Active){sampled=false;return;}
            if(sampled&&Host&&revision==hunt.State.Revision)
            {
                float d=Vector3.Distance(previous,hunt.transform.position);float dt=Time.realtimeSinceStartup-previousAt;
                if(d>Mathf.Max(.15f,dt*2.1f))errors.Add("Noncontinuous Mimic motion: "+d+" / "+dt);
                if(hunt.State.Phase==MimicPhase.MovingThroughDucts)ductDistance+=d;
            }
            previous=hunt.transform.position;revision=hunt.State.Revision;previousAt=Time.realtimeSinceStartup;sampled=true;
        }
        IEnumerator Connect()
        {
            Require(session.StartSession(Host),"NGO startup failed");
            yield return Wait(()=>session.Manager.LocalClient?.PlayerObject!=null,"owned player");
            own=session.Manager.LocalClient.PlayerObject;user=own.GetComponent<PlayerInteractor>();
        }
        IEnumerator Run()
        {
            yield return null;Directory.CreateDirectory(dir);session=FindFirstObjectByType<DevelopmentSession>();hunt=FindFirstObjectByType<MimicHunt>();session.TransportSettings.Port=(ushort)(18810+(mode=="failure"?1:mode=="evacuation"?2:Solo?3:0));session.TransportSettings.Address="127.0.0.1";
            yield return Connect();
            if(role=="late")
            {
                yield return Wait(()=>hunt.State.Active&&hunt.Decoy.State.Placed,"late threat and decoy");
                Require(hunt.State.Health>0&&hunt.Firearm.State.Held,"Late equipment or health");
                Require(hunt.Shift.Reception.Guests.All(g=>!g.Agent.enabled),"Client NPC simulation enabled");
                checks.Add("Late join reconstructed active threat, decoy placement, firearm ownership and guest state.");Mark("late-checked");yield break;
            }
            yield return Barrier("connected");
            Require(own.GetComponent<FirstPersonController>().enabled&&own.GetComponentInChildren<Camera>().enabled,"Owner camera/input missing");
            yield return Walk(Host?new Vector3(13.3f,0,7.5f):new Vector3(5.5f,0,7));
            yield return Barrier("arrival-route-clear");
            if(Host)Require(hunt.PrepareQuickTestOnServer(),"Debug prepare refused");
            yield return Wait(()=>hunt.Shift.IsRunning&&hunt.Shift.Reception.Guests.Count(g=>g.Phase==GuestPhase.InRoom)>=2,"two guests physically reach rooms",150);
            checks.Add("Normal arrival/check-in/NavMesh/door routes produced two occupied rooms.");
            var victim=hunt.Nodes[0].room.Occupant;var carrier=hunt.Nodes[1].room.Occupant;Require(victim!=null&&carrier!=null,"Expected configured first two rooms");
            if(Host)
            {
                yield return Walk(new Vector3(11.9f,0,4.9f));var computer=FindFirstObjectByType<ReceptionComputer>();
                Use(computer.ScreenCollider.GetComponent<ComputerInteraction>(),computer.ScreenCollider.bounds.center);
                yield return Wait(()=>computer.Focused,"Reception computer focus");
                var cctv=FindFirstObjectByType<VentSurveillance>();cctv.Select(0);yield return new WaitForSecondsRealtime(.15f);CaptureFeed(cctv,"cctv-1");Require(cctv.CurrentFeed!=null,"CCTV RT absent");
                cctv.Select(1);yield return new WaitForSecondsRealtime(.15f);CaptureFeed(cctv,"cctv-2");Require(cctv.Selected==1&&computer.Occupied,"CCTV replaced computer lease");
                if(mode!="failure"){computer.CloseLocal();yield return Wait(()=>!computer.LocalOpen,"close CCTV workstation");}checks.Add("Reception camera focus, occupancy, both CCTV targets and first-person return.");
                if(mode!="failure")yield return Walk(new Vector3(13.3f,0,8.4f));
            }
            yield return Barrier("computer");
            if(mode=="success"&&!Solo)yield return EquipmentRace();
            if(mode=="failure")
            {
                if(Host)
                {
                    Require(hunt.ActivateOnServer(9,carrier),"Activate failed");
                    var cctv=FindFirstObjectByType<VentSurveillance>();float end=Time.realtimeSinceStartup+1;
                    while(Time.realtimeSinceStartup<end){cctv.Select(1);yield return null;}
                    CaptureFeed(cctv,"cctv-creature");
                    Require(GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(cctv.cameras[1]),hunt.Body.bounds),"Mimic outside junction camera");
                    checks.Add("Active creature physically crossed camera 2 field of view; captured actual RenderTexture.");
                    FindFirstObjectByType<ReceptionComputer>().CloseLocal();
                }
                yield return Wait(()=>victim.Phase==GuestPhase.Dead,"ignored warnings kill eligible guest",110);
                Require(carrier.Alive&&hunt.Shift.State.GuestDeaths==1&&hunt.Nodes[0].room.State.Status==RoomStatus.Dirty,"Wrong victim or stale room");
                checks.Add("Mimic traversed connected ducts, warned/emerged and killed one eligible guest; carrier survives, source room and request cleaned up.");
            }
            else if(mode=="evacuation")
            {
                if(Host)
                {
                    yield return Enter(0);yield return Walk(new Vector3(19.7f,0,13.65f));
                    Require(hunt.ActivateOnServer(0,carrier),"Activate evacuation");
                    yield return Wait(()=>hunt.State.Phase==MimicPhase.ApproachingTarget||hunt.State.Phase==MimicPhase.ApproachingBait,"warning");
                    Use(victim.GetComponent<GuestEvacuation>(),victim.Body.bounds.center);
                    // Same visit repeated: no duplicate evacuation or count.
                    for(int i=0;i<5;i++)hunt.EvacuateRpc(new NetworkObjectReference(victim.NetworkObject),victim.State.Visit,hunt.State.Revision);
                    yield return Walk(new Vector3(20,0,14.3f));
                }
                yield return Wait(()=>victim.Phase==GuestPhase.Evacuated,"physical evacuation to safe lobby",100);
                Require(hunt.Shift.State.Evacuations==1&&victim.Alive&&hunt.Shift.State.GuestDeaths==0,"Evacuation outcome");
                checks.Add("Threatened guest exited through real door, reached lobby, vacated room and was counted once.");
                if(Host)
                {
                    hunt.ResetOnServer();yield return Leave(0);yield return Take(hunt.Firearm);yield return Enter(0);yield return Walk(new Vector3(20,0,14.2f));
                    Require(hunt.ActivateOnServer(0,carrier),"Player bait activate");
                }
                yield return Wait(()=>hunt.State.Phase==MimicPhase.Emerging,"real player heat lures from grille",50);
                if(Host)
                {
                    yield return Wait(()=>hunt.State.Phase==MimicPhase.Exposed,"exposed for failed ambush");yield return Shoot();
                    yield return Wait(()=>own.GetComponent<TowelCarrier>().Incapacitated,"player hit and stagger",30);
                    Require(!hunt.Firearm.State.Held,"Attack did not drop weapon");
                }
                yield return Wait(()=>hunt.State.Phase==MimicPhase.Retreating,"missed ambush retreats",25);
                yield return Wait(()=>hunt.State.Phase==MimicPhase.Dormant,"continuous retreat finishes",30);
                Require(hunt.State.Health==66,"Retreat lost damage state");checks.Add("Player bait, one valid hit, physical attack/stagger/equipment drop and continuous retreat preserving 66 HP.");
            }
            else
            {
                if(Solo)
                {
                    yield return Take(hunt.Firearm);yield return Enter(0);yield return Walk(new Vector3(20,0,14.2f));
                    hunt.Firearm.PlaceRpc(new Vector3(20,0,14),hunt.Firearm.State.Revision);yield return Wait(()=>!hunt.Firearm.State.Held,"stage solo pistol");yield return Leave(0);
                }
                if(Host)
                {
                    yield return Take(hunt.Decoy);yield return Enter(0);yield return Walk(new Vector3(20,0,Solo?13.3f:14.2f));
                    hunt.Decoy.PlaceRpc(Solo?new Vector3(20.3f,0,13.5f):new Vector3(19.6f,0,14.25f),hunt.Decoy.State.Revision);yield return Wait(()=>hunt.Decoy.IsHeating,"place decoy");
                    Require(!hunt.DecoyRelevantAt(3)&&hunt.GraphDistance(3,0)>5.5f,"Wrong vent summons decoy");
                    if(Solo){Use(hunt.Firearm,hunt.Firearm.PickupCollider.bounds.center);yield return Wait(()=>hunt.Firearm.LocallyHeld,"solo gun pickup");}
                    else yield return Leave(0);
                    Mark("bait-ready");
                }
                if(!Host)
                {yield return Wait(()=>Has("bait-ready"),"host stages bait");yield return Take(hunt.Firearm);yield return Enter(0);yield return Walk(new Vector3(20,0,14.2f));}
                yield return Barrier("ambush-ready");
                if(Host){Require(hunt.ActivateOnServer(9,carrier),"Activate lure");yield return new WaitForSecondsRealtime(.2f);Require(hunt.State.Phase==MimicPhase.MovingThroughDucts&&hunt.DebugTarget==victim.GuestName,"Distant decoy attracted across graph range");Mark("late");}
                if(!Host||Solo)
                {
                    yield return Wait(()=>hunt.State.Active,"activation replicated");
                    Require(hunt.transform.position.y>3,"Wall-shot test requires creature above ceiling");
                    yield return Shoot();yield return new WaitForSecondsRealtime(.6f);
                    Require(hunt.State.Health==100&&hunt.State.Hits==0,"Shot penetrated solid room ceiling");
                    checks.Add("Server raycast rejected a shot through solid ceiling toward the duct creature.");
                }
                if(!Solo)yield return Wait(()=>Has("late-checked"),"late observer",35);
                yield return Wait(()=>hunt.State.Phase==MimicPhase.Exposed,"thermal lure emerges",60);
                own.GetComponentInChildren<Camera>().transform.LookAt(hunt.Body.bounds.center);
                if(!Application.isBatchMode)ScreenCapture.CaptureScreenshot(Path.Combine(dir,role+"-emergence.png"));
                if(!Host||Solo)
                {
                    for(int i=0;i<3;i++){yield return Shoot();yield return new WaitForSecondsRealtime(.65f);}
                }
                yield return Wait(()=>hunt.State.Phase==MimicPhase.Dead,"three shots eliminate Mimic",15);
                Require(hunt.State.Health==0&&hunt.Shift.State.MimicsEliminated==1&&victim.Alive&&carrier.Alive,"Kill outcome mismatch");
                if(!Application.isBatchMode){own.GetComponentInChildren<Camera>().transform.LookAt(hunt.transform.position+Vector3.up*.45f);ScreenCapture.CaptureScreenshot(Path.Combine(dir,role+"-death.png"));yield return new WaitForSecondsRealtime(.3f);}
                checks.Add("Local-range decoy, visible emergence, 3 server hits, permanent death and one shared elimination; both guests survive.");
                if(!Host&&!Solo)
                {session.Disconnect();yield return Wait(()=>!session.Manager.IsListening&&!session.Manager.ShutdownInProgress,"disconnect gun owner");Mark("owner-left");yield return new WaitForSecondsRealtime(2);yield return Connect();yield return Walk(new Vector3(5.5f,0,7));Mark("rejoined");}
                else if(!Solo)
                {yield return Wait(()=>Has("owner-left")&&!hunt.Firearm.State.Held,"disconnect releases pistol");yield return Wait(()=>Has("rejoined"),"client rejoin");checks.Add("Disconnected firearm owner released single equipment object; client rejoined dead-creature state.");}
            }
            if(mode=="success")
            {
                if(Solo)yield return Leave(0);
                if(Host)
                {
                    victim.ExtendDebugStay(hunt.NetworkManager.ServerTime.Time+.5);
                    yield return Walk(new Vector3(11.9f,0,4.9f));
                    var computer=FindFirstObjectByType<ReceptionComputer>();Use(computer.ScreenCollider.GetComponent<ComputerInteraction>(),computer.ScreenCollider.bounds.center);
                    yield return Wait(()=>computer.Focused,"ordinary checkout computer focus");
                    yield return Wait(()=>hunt.Shift.Reception.CheckoutNpc==victim,"surviving guest returns for checkout",70);
                    hunt.Shift.Reception.RequestCheckoutRpc(0,hunt.Nodes[0].room.State.Cycle,new NetworkObjectReference(victim.NetworkObject),victim.State.Visit,computer.Lease);
                }
                yield return Wait(()=>hunt.Shift.State.CheckedOut==1&&victim.Phase==GuestPhase.Absent,"ordinary checkout and physical departure",100);
                Require(hunt.State.Phase==MimicPhase.Dead&&hunt.Nodes[0].room.State.Status==RoomStatus.Dirty&&!hunt.Nodes[0].room.State.BedMade,"Post-hunt work or turnover failed");
                if(Host){FindFirstObjectByType<ReceptionComputer>().CloseLocal();yield return new WaitForSecondsRealtime(.5f);}
                checks.Add("After elimination: surviving guest returned through real door, checked out at leased Reception computer, physically left and reset room for cleaning.");
            }
            yield return Barrier("outcome");
            if(Host)hunt.Shift.EndForHuntDebug();
            yield return Wait(()=>hunt.Shift.State.Phase==ShiftPhase.Finished,"end-of-shift summary");
            Require(!hunt.State.Active&&!hunt.Decoy.State.Held&&!hunt.Firearm.State.Held&&hunt.Shift.Reception.Guests.All(g=>g.Phase==GuestPhase.Absent),"Shift left active hunt or guests");
            Require(hunt.Shift.SummaryText.Contains("Guest deaths:")&&hunt.Shift.SummaryText.Contains("Mimics eliminated:"),"Hunt missing from summary");
            yield return Barrier("summary-observed");
            if(Host)Require(hunt.Shift.DebugStartHuntShift(90),"Restart refused");
            yield return Wait(()=>hunt.Shift.IsRunning&&hunt.Shift.State.Number==2,"new shift");
            Require(!hunt.State.Active&&hunt.Shift.State.GuestDeaths==0&&hunt.Shift.State.MimicsEliminated==0&&hunt.Shift.State.Evacuations==0,"Restart retained old hunt");
            checks.Add("Shift summary, threat/equipment/guest reset and next-shift counters verified.");
            yield return Barrier("done");
        }
        IEnumerator EquipmentRace()
        {
            yield return Walk(new Vector3(12.8f,0,8.4f));yield return Walk(new Vector3(12.8f,0,4.6f));yield return Walk(Host?new Vector3(17.1f,0,4.9f):new Vector3(17.3f,0,5.7f));
            uint revision=hunt.Decoy.State.Revision;
            yield return Barrier("equipment-race");
            hunt.Decoy.TakeRpc(revision);hunt.Decoy.TakeRpc(revision);
            yield return Wait(()=>hunt.Decoy.State.Held,"simultaneous decoy claim");yield return new WaitForSecondsRealtime(.6f);
            Require(FindObjectsByType<TowelCarrier>(FindObjectsSortMode.None).Count(c=>c.IsSpawned&&c.State.Item==ServiceItem.ThermalDecoy)==1,"Simultaneous pickup duplicated carry slot");
            if(!hunt.Decoy.LocallyHeld)hunt.Decoy.PlaceRpc(new Vector3(17.2f,0,4.9f),hunt.Decoy.State.Revision);
            yield return Barrier("unauthorized-place");yield return new WaitForSecondsRealtime(.5f);Require(hunt.Decoy.State.Held,"Nonowner placed equipment");
            if(hunt.Decoy.LocallyHeld)hunt.Decoy.PlaceRpc(new Vector3(17.2f,0,4.9f),hunt.Decoy.State.Revision);
            yield return Wait(()=>hunt.Decoy.IsHeating,"winner releases decoy");yield return Barrier("race-released");
            if(Host)hunt.Decoy.ResetOnServer();yield return Wait(()=>!hunt.Decoy.State.Held&&!hunt.Decoy.State.Placed,"race station reset");
            checks.Add("Simultaneous pickup and repeated stale requests produced one owner; nonowner placement rejected, carry slots released.");
            yield return Walk(new Vector3(12.8f,0,4.6f));yield return Walk(new Vector3(12.8f,0,8.4f));yield return Walk(Host?new Vector3(13.3f,0,8.4f):new Vector3(5.5f,0,7));yield return Barrier("race-done");
        }
        void CaptureFeed(VentSurveillance cctv,string name)
        {
            if(Application.isBatchMode)return;
            var old=RenderTexture.active;RenderTexture.active=cctv.CurrentFeed;var tex=new Texture2D(cctv.CurrentFeed.width,cctv.CurrentFeed.height,TextureFormat.RGB24,false);
            tex.ReadPixels(new Rect(0,0,tex.width,tex.height),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(dir,name+".png"),tex.EncodeToPNG());Destroy(tex);RenderTexture.active=old;
        }
        IEnumerator Shoot()
        {
            var cam=own.GetComponentInChildren<Camera>();Vector3 d=hunt.Body.bounds.center-cam.transform.position;
            Vector3 flat=new Vector3(d.x,0,d.z);own.transform.rotation=Quaternion.LookRotation(flat);cam.transform.rotation=Quaternion.LookRotation(d);yield return new WaitForSecondsRealtime(.2f);
            cam.transform.LookAt(hunt.Body.bounds.center);hunt.Firearm.FireLocal();
            uint sequence=(uint)typeof(HuntEquipment).GetField("localSequence",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(hunt.Firearm);
            for(int i=0;i<4;i++)hunt.Firearm.FireRpc(cam.transform.forward,hunt.Firearm.State.Revision,sequence);
        }
        IEnumerator Take(HuntEquipment equipment)
        {
            yield return Walk(new Vector3(12.8f,0,8.4f));yield return Walk(new Vector3(12.8f,0,4.6f));yield return Walk(new Vector3(17.1f,0,4.9f));
            Use(equipment,equipment.PickupCollider.bounds.center);yield return Wait(()=>equipment.LocallyHeld,"take "+equipment.Item);
            yield return Walk(new Vector3(12.8f,0,4.6f));yield return Walk(new Vector3(12.8f,0,8.4f));
        }
        IEnumerator Enter(int i)
        {
            var room=hunt.Nodes[i].room;yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(room.DoorOutside.position);
            yield return Wait(()=>room.Door.CanInteract,"door available");yield return new WaitForSecondsRealtime(.4f);if(!room.Door.IsOpen)Use(room.Door,room.Door.InteractionPoint);
            yield return Wait(()=>room.Door.IsOpen&&!room.Door.IsMoving,"open room");yield return Walk(new Vector3(20,0,12.65f));yield return Walk(new Vector3(19,0,12.65f));
            if(room.Door.IsOpen)Use(room.Door,room.Door.InteractionPoint);yield return Wait(()=>!room.Door.IsMoving&&!room.Door.IsOpen,"close leaf clear of work");yield return Walk(new Vector3(20,0,12.65f));
        }
        IEnumerator Leave(int i)
        {
            var room=hunt.Nodes[i].room;yield return Walk(new Vector3(20,0,12.65f));yield return Walk(new Vector3(19,0,12.65f));
            yield return Wait(()=>room.Door.CanInteract,"leave available");if(!room.Door.IsOpen)Use(room.Door,room.Door.InteractionPoint);yield return Wait(()=>room.Door.IsOpen&&!room.Door.IsMoving,"leave opens");
            yield return Walk(new Vector3(20,0,12.65f));yield return Walk(room.DoorOutside.position);yield return Walk(new Vector3(22.2f,0,8.4f));yield return Walk(new Vector3(12.8f,0,8.4f));
        }
        void Use(IInteractable target,Vector3 aim)
        {
            own.GetComponentInChildren<Camera>().transform.LookAt(aim);Physics.SyncTransforms();Require(user.FindTarget()==target,"Interaction ray misses "+target+" at "+own.transform.position+" found "+user.FindTarget());Require(user.TryInteract(),"Interaction unavailable");
        }
        IEnumerator Walk(Vector3 target)
        {
            File.WriteAllText(Path.Combine(dir,role+".progress"),"walk "+target);
            var path=new NavMeshPath();Require(NavMesh.CalculatePath(own.transform.position,target,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete,"No player path "+target);
            var cc=own.GetComponent<CharacterController>();
            foreach(var corner in path.corners.Skip(1).Concat(new[]{target}))
            {
                float end=Time.realtimeSinceStartup+30;
                while(true){var d=corner-own.transform.position;d.y=0;if(d.magnitude<.1f)break;Require(Time.realtimeSinceStartup<end,"Player blocked "+corner+" at "+own.transform.position);cc.Move(Vector3.ClampMagnitude(d,4*Time.deltaTime)+Vector3.down*2*Time.deltaTime);yield return null;}
            }
            yield return new WaitForSecondsRealtime(.35f);
        }
        void Finish(bool pass,string reason)
        {File.WriteAllText(Path.Combine(dir,role+".txt"),(pass?"PASS ":"FAIL ")+role+" / "+mode+"\n"+string.Join("\n",checks)+"\n"+reason+"\nDuct travel: "+ductDistance+"m\n"+string.Join("\n",errors));Application.Quit(pass?0:1);}
        void OnDestroy()=>Application.logMessageReceived-=Log;
    }
}
#endif

