#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using MotelGame.Interactions;

namespace MotelGame.Networking.Tests
{
    // Explicit command-line development test only. File barriers coordinate processes;
    // door state, poses and remote player observations still travel through NGO/UTP.
    public sealed class NetworkDoorSmokeTest : MonoBehaviour
    {
        string role, directory;
        DevelopmentSession session;
        NetworkPlayer local;
        NetworkDoor door;
        readonly List<string> checks = new List<string>(), errors = new List<string>();
        bool intermediate;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-motel-door-test");
            if(i<0)return;
            var test=new GameObject("NetworkDoorSmokeTest").AddComponent<NetworkDoorSmokeTest>();
            test.role=args[i+1];test.directory=args[Array.IndexOf(args,"-motel-door-results")+1];
        }
        void Awake(){Application.logMessageReceived+=Log;}
        void Log(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}
        void Update(){if(door!=null && door.CurrentAngle>5 && door.CurrentAngle<85)intermediate=true;}
        IEnumerator Start()
        {
            // Flatten nested iterators so failures in any stage produce a report.
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0)
            {
                object next=null;bool more=false;Exception failure=null;
                try{more=stack.Peek().MoveNext();if(more)next=stack.Peek().Current;}
                catch(Exception e){failure=e;}
                if(failure!=null){Finish(false,failure.ToString());yield break;}
                if(!more){stack.Pop();continue;}
                if(next is IEnumerator nested)stack.Push(nested);else yield return next;
            }
            Finish(errors.Count==0,"Completed.");
        }
        IEnumerator Wait(Func<bool> condition,string label,float timeout=60)
        {
            float end=Time.realtimeSinceStartup+timeout;
            while(!condition()){Require(Time.realtimeSinceStartup<end,"Timeout: "+label);yield return null;}
        }
        bool Marked(string name)=>File.Exists(Path.Combine(directory,name+".signal"));
        void Mark(string name)=>File.WriteAllText(Path.Combine(directory,name+".signal"),"ready");
        static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
        NetworkPlayer[] Players()=>session.Manager.SpawnManager.SpawnedObjectsList.Where(n=>n.IsPlayerObject).Select(n=>n.GetComponent<NetworkPlayer>()).ToArray();
        IEnumerator Run()
        {
            yield return null;
            session=FindFirstObjectByType<DevelopmentSession>();Require(session!=null,"No session");
            session.TransportSettings.Address="127.0.0.1";session.TransportSettings.Port=17778;
            Require(session.StartSession(role=="host"),"Start failed");
            yield return Wait(()=>session.Manager.IsConnectedClient && Players().Length==2,"two players");
            local=Players().Single(p=>p.IsOwner);
            door=FindFirstObjectByType<NetworkDoor>();Require(door!=null && door.IsSpawned,"Door spawn");
            var remote=Players().Single(p=>!p.IsOwner);
            Require(local.GetComponent<PlayerInteractor>().enabled && !remote.GetComponent<PlayerInteractor>().enabled,"Interactor ownership");
            Require(remote.GetComponent<PlayerInteractor>().FindTarget()==null,"Remote raycast allowed");
            Require(local.GetComponent<FirstPersonController>().enabled && !remote.GetComponent<FirstPersonController>().enabled,"Movement ownership");
            Require(local.GetComponentInChildren<Camera>(true).enabled && !remote.GetComponentInChildren<Camera>(true).enabled,"Camera ownership");
            checks.Add("Two players spawned; owner-only camera, movement and interaction; remote cannot raycast.");
            if(role=="host")yield return Host();
            else if(role=="client")yield return Client();
            else yield return Late();
            session.Disconnect();
            yield return Wait(()=>!session.Manager.IsListening && !session.Manager.ShutdownInProgress,"disconnect",10);
            checks.Add("Disconnect completed.");
        }
        IEnumerator Host()
        {
            yield return Wait(()=>Marked("distant-request"),"distant request");
            yield return new WaitForSecondsRealtime(1);
            Require(!door.IsOpen && door.TransitionStartedAt==0,"Distant request accepted");
            checks.Add("Server rejected distant client's direct RPC.");Mark("distant-denied");
            yield return Approach();
            yield return Blocked();
            yield return Walk(new Vector3(22.2f,0,12.65f));
            yield return Toggle(true);
            Require(intermediate,"Host did not animate");
            yield return Walk(new Vector3(19.1f,0,12.65f));
            checks.Add("Open door permits CharacterController passage.");
            yield return Walk(new Vector3(18.9f,0,13.2f));Mark("host-inside");
            yield return Wait(()=>Marked("client-closed"),"client close");
            Require(!door.IsOpen && door.CurrentAngle<.1f,"Host did not see close");
            checks.Add("Host observed client close through replicated state.");Mark("host-saw-close");
            yield return Wait(()=>Marked("spam-complete"),"spam");
            Require(door.IsOpen && door.CurrentAngle>89.9f,"Spam diverged");
            yield return Wait(()=>Players().Length==1,"first client disconnect");Mark("ready-late");
            yield return Wait(()=>Marked("late-checked"),"late join");
            yield return Wait(()=>Players().Length==1,"late disconnect");
            checks.Add("Remote player moved to guest wing; client disconnect and late join preserved door state.");
        }
        IEnumerator Client()
        {
            Require(!door.IsOpen,"Initial state not closed");door.RequestToggleRpc();Mark("distant-request");
            yield return Wait(()=>Marked("distant-denied"),"distance denial");
            Require(!door.IsOpen,"Client distant request changed state");
            yield return Wait(()=>Marked("host-inside"),"host open/passage");
            Require(door.IsOpen && door.CurrentAngle>89.9f && intermediate,"Client did not observe animated open");
            Require(Players().Single(p=>!p.IsOwner).transform.position.x<20,"Host movement not replicated");
            checks.Add("Observed host movement and smooth door opening over NGO.");
            yield return Approach();yield return Toggle(false);Mark("client-closed");
            yield return Wait(()=>Marked("host-saw-close"),"host sees close");
            yield return Blocked();yield return Walk(new Vector3(22.2f,0,12.65f));
            yield return new WaitForSecondsRealtime(.4f);
            for(int i=0;i<20;i++)door.RequestToggleRpc();
            yield return Wait(()=>door.IsOpen,"spam open",5);
            double started=door.TransitionStartedAt;
            yield return new WaitForSecondsRealtime(1.5f);
            Require(door.IsOpen && door.TransitionStartedAt==started && door.CurrentAngle>89.9f,"Rapid requests caused multiple transitions");
            checks.Add("20 rapid RPCs yielded one transition, stable replicated open state.");Mark("spam-complete");
        }
        IEnumerator Late()
        {
            Require(door.IsOpen && door.CurrentAngle>89.9f,"Late join incorrect state/pose");
            checks.Add("Late join received open state and correct 90-degree collider pose.");
            Mark("late-checked");yield return new WaitForSecondsRealtime(1);
        }
        IEnumerator Approach()
        {
            yield return Walk(new Vector3(7,0,8.4f));
            yield return Walk(new Vector3(22.2f,0,8.4f));
            yield return Walk(new Vector3(22.2f,0,12.65f));
            yield return new WaitForSecondsRealtime(.4f);
        }
        IEnumerator Walk(Vector3 target)
        {
            float end=Time.realtimeSinceStartup+15;var cc=local.GetComponent<CharacterController>();
            while(true)
            {
                Vector3 delta=target-local.transform.position;delta.y=0;if(delta.magnitude<.06f)break;
                Require(Time.realtimeSinceStartup<end,"Walk blocked toward "+target+" at "+local.transform.position);
                cc.Move(Vector3.ClampMagnitude(delta,4*Time.deltaTime)+Vector3.down*2*Time.deltaTime);yield return null;
            }
        }
        IEnumerator Blocked()
        {
            float end=Time.realtimeSinceStartup+1;var cc=local.GetComponent<CharacterController>();
            while(Time.realtimeSinceStartup<end){cc.Move(new Vector3(-4,-2,0)*Time.deltaTime);yield return null;}
            Require(local.transform.position.x>20.8f && local.transform.position.x<21.1f,"Closed door collision failed "+local.transform.position);
            checks.Add("Closed leaf blocks local CharacterController.");
        }
        IEnumerator Toggle(bool open)
        {
            var camera=local.GetComponentInChildren<Camera>(true);
            camera.transform.LookAt(door.InteractionPoint);
            Require(local.GetComponent<PlayerInteractor>().FindTarget()==(IInteractable)door,"Camera ray missed door");
            Require(local.GetComponent<PlayerInteractor>().TryInteract(),"Interaction rejected locally");
            yield return Wait(()=>door.IsOpen==open && !door.IsMoving,"door transition",5);
            Require(Mathf.Abs(door.CurrentAngle-(open?90:0))<.1f,"Final pose");
            checks.Add("Owner camera raycast/RPC "+(open?"opened":"closed")+" door.");
        }
        void Finish(bool pass,string reason)
        {
            File.WriteAllText(Path.Combine(directory,role+".txt"),(pass?"PASS":"FAIL")+" "+role+"\n"+string.Join("\n",checks)+"\n"+reason+"\n"+string.Join("\n",errors));
            Application.Quit(pass?0:1);
        }
        void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
#endif
