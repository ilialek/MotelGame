#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Unity.Netcode;
using MotelGame.Service;
using MotelGame.Registration;
using MotelGame.Networking;

// Opt-in development actor: resize both physical workstations while motion/parking tabs are open.
[DefaultExecutionOrder(-200)]
public sealed class UndergroundUiVerification : MonoBehaviour
{
    string output;
    Vector2 hoverScreen;
    bool hovering;
    // Synthetic events stay inside this opt-in development Player; no OS cursor/input changes.
    void InjectGuiEvent(ReceptionComputer station)
    {
        if(!hovering||Event.current.type!=EventType.Repaint)return;
        var args=new object[]{default(Rect)};
        if(!(bool)typeof(ReceptionComputer).GetMethod("TryGetDisplayRect",Private).Invoke(station,args))return;
        Rect rect=(Rect)args[0];float scale=Mathf.Min(rect.width/800,rect.height/640);
        Vector2 origin=rect.center-new Vector2(800,640)*scale*.5f;
        // GUIClip resets event coordinates on matrix/group changes. Inject after the final area is open.
        Event.current.mousePosition=(hoverScreen-origin)/scale;
    }

    readonly List<string> checks = new(), errors = new();
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        string path = OnlineIdentity.Argument("-motel-underground-ui-test");
        if (path == null)
            return;
        new GameObject("UndergroundUiVerification").AddComponent<UndergroundUiVerification>().output = path;
    }

    void Awake(){Application.logMessageReceived += Log;ReceptionComputer.BeforeGuiForVerification=InjectGuiEvent;}
    void Log(string text, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(text);
    }

    static void Need(bool ok, string why)
    {
        if (!ok)
            throw new Exception(why);
    }

    static void Set(object o, string field, object value) => o.GetType().GetField(field, Private).SetValue(o, value);
    IEnumerator Wait(Func<bool> ready, string label, float seconds = 120)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!ready())
        {
            Need(Time.realtimeSinceStartup < end, "Timeout: " + label);
            yield return null;
        }
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(output);
        var stack = new Stack<IEnumerator>();
        stack.Push(Run());
        while (stack.Count > 0)
        {
            bool more = false;
            object next = null;
            Exception error = null;
            try
            {
                more = stack.Peek().MoveNext();
                if (more)
                    next = stack.Peek().Current;
            }
            catch (Exception e)
            {
                error = e;
            }

            if (error != null)
            {
                errors.Add(error.ToString());
                break;
            }

            if (!more)
            {
                stack.Pop();
                continue;
            }

            if (next is IEnumerator sub)
                stack.Push(sub);
            else
                yield return next;
        }

        File.WriteAllText(Path.Combine(output, "results.txt"), (errors.Count == 0 ? "PASS" : "FAIL") + "\n" + string.Join("\n", checks) + "\n" + string.Join("\n", errors));
        Application.Quit(errors.Count == 0 ? 0 : 1);
    }

    IEnumerator Run()
    {
        var session = FindFirstObjectByType<DevelopmentSession>();
        var online = FindFirstObjectByType<OnlineSessionController>();
        yield return Wait(() => !online.Busy, "authentication");
        session.TransportSettings.Port = 19101;
        Need(session.StartSession(true), "Start local host");
        yield return Wait(() => session.Manager.LocalClient?.PlayerObject != null, "local player");
        var player = session.Manager.LocalClient.PlayerObject;
        var view = player.GetComponentInChildren<Camera>();
        float initialFov = view.fieldOfView;
        var firstStation = FindObjectsByType<ReceptionComputer>(FindObjectsSortMode.None).First(c => !c.SurveillanceOnly);
        var initialController = player.GetComponent<CharacterController>();
        initialController.enabled = false;
        var initialPosition = firstStation.Focus.position;
        initialPosition.y = .03f;
        player.transform.position = initialPosition;
        initialController.enabled = true;
        var shift = FindFirstObjectByType<NightShiftController>();
        Need(shift.DebugStartScenario(1800, 1, 30), "UI-only shift");
        var desk=FindFirstObjectByType<RegistrationDesk>();
        Set(desk,"agenda",new Queue<GuestCase>(new[]{desk.Cases.First(c=>c.Supports(RegistrationQuestion.BookingName))}));
        foreach(var room in FindObjectsByType<ServiceRoom>(FindObjectsSortMode.None))room.PrepareForHuntDebug();
        yield return Wait(()=>desk.Waiting!=null,"real guest at reception",180);
        var hunt = MotelGame.Horror.MimicHunt.Instance;
        typeof(MotelGame.Horror.MimicHunt).GetMethod("Signal", Private).Invoke(hunt, new object[]{1,false});
        foreach (var station in FindObjectsByType<ReceptionComputer>(FindObjectsSortMode.None).OrderBy(c => c.SurveillanceOnly))
        {
            string prefix = station.SurveillanceOnly ? "cctv" : "reception";
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            var position = station.Focus.position;
            position.y = .03f;
            player.transform.position = position;
            controller.enabled = true;
            yield return null;
            station.RequestUse();
            yield return Wait(() => station.Focused, "computer open");
            var panel = station.GetComponent<RegistrationPanel>();
            foreach (var size in new[]{new Vector2Int(800, 600), new Vector2Int(1280, 720), new Vector2Int(1920, 1200), new Vector2Int(2560, 1080)})
            {
                Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(.6f);
                var args = new object[]{default(Rect)};
                Need((bool)typeof(ReceptionComputer).GetMethod("TryGetDisplayRect", Private).Invoke(station, args), "display rect invalid");
                Rect rect = (Rect)args[0];
                Need(rect.x >= 0 && rect.y >= 0 && rect.xMax <= Screen.width && rect.yMax <= Screen.height, "display outside game view");
                Need(Mathf.Abs(Vector3.Dot(view.transform.up, station.ScreenCollider.transform.up) - 1) < .0001f, "screen tilt");
                var corners = new[]{new Vector2(rect.x, rect.y), new Vector2(rect.xMax, rect.y), new Vector2(rect.x, rect.yMax), new Vector2(rect.xMax, rect.yMax)};
                foreach (var p in corners)
                {
                    var ray = view.ScreenPointToRay(new Vector3(p.x, Screen.height - p.y, 0));
                    Need(station.ScreenCollider.Raycast(ray, out var hit, 3), "UI corner escapes physical screen");
                }

                Need(rect.yMax<Screen.height*.90f,"No subtitle band below monitor");
                checks.Add(prefix + " requested=" + size + " actual=" + Screen.width + "x" + Screen.height + " physical screen contains all UI corners; rect=" + rect);
                yield return Wait(()=>!desk.Waiting.Speaking,"previous speech finished");
                Set(station,"registrationTab",1);hovering=true;
                float uiScale=Mathf.Min(rect.width/800,rect.height/640);var origin=rect.center-new Vector2(800,640)*uiScale*.5f;
                string hint="";
                for(float y=145;y<=260;y+=4)
                {
                    hoverScreen=origin+new Vector2(530,y)*uiScale;
                    yield return new WaitForEndOfFrame();yield return null;
                    hint=(string)typeof(RegistrationPanel).GetField("tooltip",Private).GetValue(panel);
                    if(hint=="Ask about first name")break;
                }
                if(hint!="Ask about first name")
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(output,"hover-diagnostic.png"));
                    yield return new WaitForSecondsRealtime(.3f);
                }
                Need(hint=="Ask about first name","Reservation field hover tooltip not reachable through display transform");
                ScreenCapture.CaptureScreenshot(Path.Combine(output,prefix+"-"+Screen.width+"x"+Screen.height+"-tooltip.png"));
                yield return new WaitForSecondsRealtime(.2f);
                // Native mouse-button ownership cannot be synthesized by replacing a Repaint event.
                // Exercise the exact field action and its server RPC; physical mouse clicks remain a manual check.
                var ask=typeof(RegistrationPanel).GetMethod("AskOrRead",Private);
                var questionArgs=new object[]{desk.Waiting,RegistrationQuestion.BookingName,-1};
                ask.Invoke(panel,questionArgs);
                yield return Wait(()=>desk.Waiting.Speaking,"field action starts replicated dialogue",5);
                desk.Waiting.LatestSpeech(out var spoken);int lineCount=desk.Waiting.Statements.Count;
                ask.Invoke(panel,questionArgs);yield return new WaitForSecondsRealtime(.3f);
                desk.Waiting.LatestSpeech(out var repeated);
                Need(repeated.SpokenAt==spoken.SpokenAt&&lineCount==desk.Waiting.Statements.Count,"Repeated field action overlapped dialogue");
                hovering=false;
                checks.Add(prefix+" "+size+" synthetic local hover + actual field action/RPC; physical projection checked; repeated action ignored during reply. OS mouse clicks not automated");
                yield return new WaitForSecondsRealtime(1.5f);
                foreach (int tab in new[]{1,2,3,4})
                {
                    Set(station, "registrationTab", tab);
                    yield return new WaitForSecondsRealtime(.15f);
                    ScreenCapture.CaptureScreenshot(Path.Combine(output, prefix + "-" + Screen.width + "x" + Screen.height + "-tab" + tab + ".png"));
                    yield return new WaitForSecondsRealtime(.2f);
                }
            }

            station.CloseLocal();
            yield return Wait(() => !station.LocalOpen, "close");
            Need(Mathf.Abs(view.fieldOfView - initialFov) < .01f, "Exit FOV restore");
            checks.Add(prefix + " exit restores original FOV");
        }

        session.Disconnect();
        yield return Wait(() => !session.Manager.IsListening, "disconnect");
        checks.Add("Local host shutdown; critical logs: " + errors.Count);
    }

    void OnDestroy(){Application.logMessageReceived -= Log;ReceptionComputer.BeforeGuiForVerification=null;}
}
#endif

