#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace MotelGame.Networking.Tests
{
    // Development-build-only integration test. Dormant unless explicitly requested on the command line.
    // Drives each process's OWN capsule; remote observations come exclusively through NGO/UTP.
    public sealed class LocalMultiplayerSmokeTest : MonoBehaviour
    {
        string role, reportPath;
        readonly List<string> errors=new List<string>();
        readonly List<string> checks=new List<string>();
        bool done;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            string[] args=Environment.GetCommandLineArgs();
            int index=Array.IndexOf(args,"-motel-smoke");
            if(index<0 || index+1>=args.Length)return;
            var go=new GameObject("LocalMultiplayerSmokeTest");DontDestroyOnLoad(go);
            var test=go.AddComponent<LocalMultiplayerSmokeTest>();test.role=args[index+1];
            index=Array.IndexOf(args,"-motel-smoke-report");
            test.reportPath=index>=0 && index+1<args.Length ? args[index+1] : Path.Combine(Application.persistentDataPath,"multiplayer-smoke-"+test.role+".txt");
        }
        void Awake() { Application.logMessageReceived+=Log; }
        void Log(string message,string stack,LogType type)
        { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)errors.Add(message); }
        IEnumerator Start()
        {
            yield return null;
            var session=FindFirstObjectByType<DevelopmentSession>();
            if(session==null || (role!="host" && role!="client")) { Finish(false,"Missing session / invalid role");yield break; }
            session.TransportSettings.Address="127.0.0.1";
            session.TransportSettings.Port=17777;
            if(!session.StartSession(role=="host")) { Finish(false,"Session failed to start");yield break; }
            var manager=session.Manager;
            float deadline=Time.realtimeSinceStartup+90;
            while(Players(manager).Length!=2)
            {
                if(Time.realtimeSinceStartup>deadline) { Finish(false,"Timeout waiting for two players");yield break; }
                yield return null;
            }
            yield return null;yield return null;
            var players=Players(manager);
            var local=players.Single(p=>p.IsOwner);var remote=players.Single(p=>!p.IsOwner);
            if(Vector3.Distance(local.transform.position,remote.transform.position)<1)
            { Finish(false,"Spawn positions overlap");yield break; }
            if(!CorrectOwnership(local,true) || !CorrectOwnership(remote,false))
            { Finish(false,"Owner/remote component isolation failed");yield break; }
            if(FindObjectsByType<Camera>(FindObjectsSortMode.None).Count(c=>c.enabled)!=1 ||
               FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(l=>l.enabled)!=1)
            { Finish(false,"Multiple active cameras/listeners");yield break; }
            checks.Add("Two distinct player objects and lobby spawn points; one local owner.");
            checks.Add("Owner input/camera/listener/capsule enabled; remote input/camera/listener/capsule disabled; only remote body visible.");
            Vector3 before=remote.transform.position;Quaternion rotation=remote.transform.rotation;
            float start=Time.realtimeSinceStartup;float maxMove=0,maxRotation=0;
            while(Time.realtimeSinceStartup-start<8)
            {
                float elapsed=Time.realtimeSinceStartup-start;
                if(elapsed<4)
                {
                    local.GetComponent<CharacterController>().Move(Vector3.forward*(.8f*Time.deltaTime));
                    local.transform.Rotate(0,20*Time.deltaTime,0);
                }
                maxMove=Mathf.Max(maxMove,Vector3.Distance(before,remote.transform.position));
                maxRotation=Mathf.Max(maxRotation,Quaternion.Angle(rotation,remote.transform.rotation));
                if(!CorrectOwnership(local,true)||!CorrectOwnership(remote,false))
                { Finish(false,"Ownership isolation changed while moving");yield break; }
                yield return null;
            }
            if(maxMove<1 || maxRotation<20) { Finish(false,"Remote transform not synchronized: "+maxMove+"m / "+maxRotation+"deg");yield break; }
            checks.Add("Observed remote movement via NGO: "+maxMove.ToString("F2")+"m and yaw "+maxRotation.ToString("F1")+"deg.");
            if(role=="client")
            {
                yield return new WaitForSecondsRealtime(2);
                session.Disconnect();
            }
            else
            {
                deadline=Time.realtimeSinceStartup+20;
                while(Players(manager).Length!=1)
                {
                    if(Time.realtimeSinceStartup>deadline){Finish(false,"Remote disconnect did not despawn player");yield break;}
                    yield return null;
                }
                checks.Add("Client disconnect despawned remote player on host.");
                session.Disconnect();
            }
            deadline=Time.realtimeSinceStartup+10;
            while(manager.IsListening || manager.ShutdownInProgress)
            {
                if(Time.realtimeSinceStartup>deadline){Finish(false,"Shutdown timed out");yield break;}
                yield return null;
            }
            yield return null;
            if(FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None).Length!=0)
            { Finish(false,"Player objects remained after shutdown");yield break; }
            checks.Add("Disconnect completed and network player objects were removed.");
            Finish(errors.Count==0, errors.Count==0 ? "No critical error logs." : "Critical logs captured.");
        }
        static NetworkPlayer[] Players(NetworkManager manager)
        {
            if(manager==null || manager.SpawnManager==null)return Array.Empty<NetworkPlayer>();
            return manager.SpawnManager.SpawnedObjectsList.Where(o=>o.IsPlayerObject).Select(o=>o.GetComponent<NetworkPlayer>()).Where(p=>p!=null).ToArray();
        }
        static bool CorrectOwnership(NetworkPlayer player,bool local)
        {
            return player.IsOwner==local && player.GetComponent<FirstPersonController>().enabled==local &&
                player.GetComponent<CharacterController>().enabled==local && player.GetComponentInChildren<Camera>(true).enabled==local &&
                player.GetComponentInChildren<AudioListener>(true).enabled==local && player.transform.Find("RemoteBody").GetComponent<Renderer>().enabled!=local;
        }
        void Finish(bool passed,string reason)
        {
            if(done)return;done=true;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
            File.WriteAllText(reportPath,(passed?"PASS":"FAIL")+" "+role+" "+DateTime.UtcNow.ToString("O")+"\n"+string.Join("\n",checks)+"\n"+reason+"\n"+string.Join("\n",errors)+"\n");
            Application.Quit(passed?0:1);
        }
        void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
#endif
