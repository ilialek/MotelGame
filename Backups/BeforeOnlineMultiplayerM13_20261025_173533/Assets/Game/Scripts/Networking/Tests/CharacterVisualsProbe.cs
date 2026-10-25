#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using MotelGame.Characters;
using MotelGame.Service;
namespace MotelGame.Networking.Tests
{
    // Opt-in observer used by both the focused locomotion test and the complete M7 regression.
    public sealed class CharacterVisualsProbe : MonoBehaviour
    {
        string path;float nextWrite;
        readonly HashSet<string> observations=new HashSet<string>();
        readonly HashSet<string> failures=new HashSet<string>();
        readonly Dictionary<int,Quaternion> poses=new Dictionary<int,Quaternion>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-motel-character-report");
            if(i<0||i+1>=args.Length)return;
            var p=new GameObject("CharacterVisualsProbe").AddComponent<CharacterVisualsProbe>();p.path=args[i+1];DontDestroyOnLoad(p.gameObject);
        }
        void Check(bool okay,string message){if(!okay&&failures.Add(message))Debug.LogError("M8 visuals: "+message);}
        void LateUpdate()
        {
            foreach(var p in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None).Where(p=>p.IsSpawned))
            {
                var v=p.GetComponentInChildren<CharacterVisuals>(true);
                Check(v!=null&&v.ConfigurationValid(),"Player visual configuration");if(v==null)continue;
                Check(p.BodyVisible!=p.IsOwner,"Owner-only body hiding");
                Check(!p.transform.Find("RemoteBody").GetComponent<Renderer>().enabled,"Legacy capsule visible");
                Check(p.GetComponentInChildren<Camera>(true).enabled==p.IsOwner,"Camera ownership");
                Observe(v,p.IsOwner?"local-player":"remote-player");
                var towel=p.GetComponent<TowelCarrier>();
                if(towel.HasTowel)
                {
                    var s=p.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="CarriedTowel");
                    var f=p.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="FirstPersonTowel");
                    Check(s.gameObject.activeSelf!=p.IsOwner&&f.gameObject.activeSelf==p.IsOwner,"Towel ownership");
                    Check(s.parent.name=="Palm.R","Towel hand attachment");
                    Check(Vector3.Distance(s.position,s.parent.position)<.3f,"Towel detached from hand");
                    observations.Add(p.IsOwner?"first-person-towel":"remote-hand-towel");
                }
            }
            var guests=FindObjectsByType<GuestLifecycle>(FindObjectsSortMode.None).Where(g=>g.IsSpawned&&g.Visible).ToArray();
            if(guests.Length>=4)observations.Add("four-simultaneous-guests");
            foreach(var g in guests)
            {
                var v=g.GetComponentInChildren<CharacterVisuals>();Check(v!=null&&v.ConfigurationValid()&&v.BodyVisible,"Guest model visibility");
                if(v!=null){Observe(v,"guest");observations.Add("appearance-"+v.Appearance.name);observations.Add("guest-phase-"+g.Phase);}
                Check(!g.transform.Find("HumanoidPlaceholder").gameObject.activeInHierarchy,"Guest primitive visible");
            }
            if(Time.unscaledTime>nextWrite){nextWrite=Time.unscaledTime+2;Write();}
        }
        void Observe(CharacterVisuals v,string prefix)
        {
            var a=v.Animator;if(a==null)return;
            Check(!a.applyRootMotion&&a.avatar.isValid,"Invalid rig or root motion");
            var state=a.GetCurrentAnimatorStateInfo(0);
            if(!a.IsInTransition(0))
            {
                foreach(string name in new[]{"Idle","Walk","Run"})if(state.IsName(name))observations.Add(prefix+"-"+name);
            }
            var bone=v.Appearance.GetComponentsInChildren<Transform>().First(t=>t.name=="LowerLeg.L");int id=v.GetInstanceID();
            if(poses.TryGetValue(id,out var previous)&&v.MovementSpeed>.4f&&Quaternion.Angle(previous,bone.localRotation)>.5f)observations.Add(prefix+"-animated-leg");
            poses[id]=bone.localRotation;
        }
        void Write(){if(string.IsNullOrEmpty(path))return;Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,(failures.Count==0?"PASS":"FAIL")+" configuration checks\n"+string.Join("\n",observations.OrderBy(x=>x))+"\n"+string.Join("\n",failures));}
        void OnApplicationQuit()=>Write();
    }
}
#endif
