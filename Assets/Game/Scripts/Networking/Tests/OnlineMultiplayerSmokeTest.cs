#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MotelGame.Service;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace MotelGame.Networking.Tests
{
    // Explicit opt-in standalone verification; signals coordinate actors, never gameplay state.
    public sealed class OnlineMultiplayerSmokeTest : MonoBehaviour
    {
        string role, directory;
        OnlineSessionController online;
        DevelopmentSession local;
        readonly List<string> checks = new(), errors = new();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            string role = OnlineIdentity.Argument("-motel-online-smoke-test");
            if (role == null) return;
            var t = new GameObject("OnlineMultiplayerSmokeTest").AddComponent<OnlineMultiplayerSmokeTest>();
            t.role = role; t.directory = OnlineIdentity.Argument("-motel-online-results");
        }
        void Awake() => Application.logMessageReceived += Log;
        void Log(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
        void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        void Mark(string name, string text = "ready") => File.WriteAllText(Path.Combine(directory, name + ".signal"), text);
        bool Marked(string name) => File.Exists(Path.Combine(directory, name + ".signal"));
        IEnumerator Wait(Func<bool> condition, string step, float seconds = 100)
        {
            File.WriteAllText(Path.Combine(directory, role + ".progress"), step);
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition()) { Require(Time.realtimeSinceStartup < end, "Timeout " + step + "; " + online?.Status + "; " + online?.Error); yield return null; }
        }
        IEnumerator TaskDone(Task task, string step) { yield return Wait(() => task.IsCompleted, step); Require(!task.IsFaulted, step + " fault"); }
        IEnumerator Start()
        {
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0)
            {
                object next = null; bool more = false; Exception error = null;
                try { more = stack.Peek().MoveNext(); if (more) next = stack.Peek().Current; } catch (Exception e) { error = e; }
                if (error != null) { Finish(false, error.ToString()); yield break; }
                if (!more) { stack.Pop(); continue; }
                if (next is IEnumerator nested) stack.Push(nested); else yield return next;
            }
            Finish(errors.Count == 0, "No critical runtime errors.");
        }
        IEnumerator Join(string name)
        {
            yield return Wait(() => Marked(name), "invite " + name);
            yield return TaskDone(online.JoinLobbyAsync(File.ReadAllText(Path.Combine(directory, name + ".signal"))), "join " + name);
            Require(online.Session != null, "Join failed: " + online.Error);
            Require(local.Manager.GetComponent<UnityTransport>().Protocol == UnityTransport.ProtocolType.RelayUnityTransport, "Direct transport used");
        }
        IEnumerator Run()
        {
            yield return null;
            online = FindFirstObjectByType<OnlineSessionController>(); local = FindFirstObjectByType<DevelopmentSession>();
            yield return Wait(() => !online.Busy, "automatic authentication");
            if (role == "duplicate")
            {
                Require(!online.Authenticated && online.Error.Contains("profile"), "A duplicate local profile was allowed to authenticate");
                checks.Add("Second process with same explicit profile refused before authentication"); yield break;
            }
            Require(online.Authenticated, online.Error);
            Mark(role + "-identity", online.PlayerId);
            if (!Application.isBatchMode) ScreenCapture.CaptureScreenshot(Path.Combine(directory, role + "-menu.png"));
            checks.Add("Anonymous authentication / persistent separate profile " + online.Profile);
            if (role.StartsWith("auto"))
            {
                yield return Wait(() => Marked("auto1-identity") && Marked("auto2-identity"), "automatic profile isolation");
                Require(File.ReadAllText(Path.Combine(directory, "auto1-identity.signal")) != File.ReadAllText(Path.Combine(directory, "auto2-identity.signal")), "Default launchers shared the same identity");
                Mark(role + "-checked"); yield return Wait(() => Marked("auto1-checked") && Marked("auto2-checked"), "both profiles checked");
                checks.Add("Two unconfigured launches acquired separate persistent profiles and player IDs"); yield break;
            }
            if (role == "invalid")
            {
                yield return TaskDone(online.JoinLobbyAsync("!invalid!"), "invalid syntax");
                Require(online.Session == null && !online.Busy && online.Error.Contains("invite code"), "Invalid code UX");
                yield return TaskDone(online.JoinLobbyAsync("ZZZZZZ"), "unknown code");
                Require(online.Session == null && !online.Busy && !string.IsNullOrEmpty(online.Error), "Unknown code UX");
                yield return TaskDone(online.CreateLobbyAsync(), "retry after failed join");
                Require(online.Session != null, "Retry failed");
                yield return TaskDone(online.LeaveLobbyAsync(), "leave retry lobby");
                var creating = online.CreateLobbyAsync(); online.Cancel(); yield return TaskDone(creating, "cancel creation");
                Require(!online.Busy && online.Session == null && !local.Manager.IsListening, "Cancellation leaked session");
                yield return TaskDone(online.CreateLobbyAsync(), "retry after cancellation"); Require(online.Session != null, "Cancellation retry failed");
                yield return TaskDone(online.LeaveLobbyAsync(), "cleanup");
                checks.Add("Invalid/unknown code, retry, cancellation, recreation"); yield break;
            }
            if (role == "incompatible")
            {
                yield return Wait(() => Marked("code-one"), "invite");
                local.Manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes("{\"build\":\"old-test-build\",\"player\":\"" + online.PlayerId + "\"}");
                var joining = MultiplayerService.Instance.JoinSessionByCodeAsync(File.ReadAllText(Path.Combine(directory, "code-one.signal")));
                yield return Wait(() => joining.IsCompleted, "incompatible NGO approval");
                Require(joining.IsFaulted && local.Manager.DisconnectReason.Contains("Incompatible build"), "Incompatible build not rejected clearly");
                Mark("incompatible-checked"); checks.Add("Actual Relay join rejected by host for incompatible version"); yield break;
            }
            if (role == "fifth")
            {
                yield return Wait(() => Marked("full"), "four-player lobby");
                yield return TaskDone(online.JoinLobbyAsync(File.ReadAllText(Path.Combine(directory, "code-one.signal"))), "fifth join");
                Require(online.Session == null && online.Error.Contains("full"), "Fifth player not rejected: " + online.Error);
                Mark("fifth-checked"); checks.Add("Fifth identity rejected with full-lobby message"); yield break;
            }
            if (role == "host")
            {
                var first = online.CreateLobbyAsync(); var duplicate = online.CreateLobbyAsync();
                yield return TaskDone(first, "create private Relay lobby"); yield return TaskDone(duplicate, "duplicate create");
                Require(online.Session != null && online.Session.IsPrivate && online.Session.MaxPlayers == 4, "Private/capacity config");
                Require(local.Manager.LocalClient.PlayerObject == null, "Player spawned before Start Game");
                Mark("code-one", online.Session.Code);
                yield return Wait(() => Marked("incompatible-checked"), "incompatible approval check"); Mark("allow-clients");
                yield return Wait(() => online.Session.PlayerCount == 4 && online.CanStartGame, "four independent members");
                Require(online.Session.Players.Select(p => p.Id).Distinct().Count() == 4, "Duplicate authentication identity");
                Require(FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None).Length == 0, "Lobby spawned players");
                Require(FindFirstObjectByType<NightShiftController>().State.Phase == ShiftPhase.WaitingToStart, "Lobby started shift");
                if (!Application.isBatchMode) ScreenCapture.CaptureScreenshot(Path.Combine(directory, role + "-lobby.png"));
                Mark("full"); yield return Wait(() => Marked("fifth-checked"), "capacity checked");
                Require(online.StartGame() && !online.StartGame(), "Host Start Game idempotency");
            }
            else
            {
                yield return Wait(() => Marked("allow-clients"), "host ready"); yield return Join("code-one");
                Require(!online.StartGame(), "Client started gameplay");
            }
            yield return Wait(() => online.InGame && FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None).Length == 4, "four spawned players");
            CheckOwnership(); checks.Add("Four Relay players; host-only Start Game; correct camera/input ownership");
            var own = local.Manager.LocalClient.PlayerObject;
            Vector3 from = own.transform.position;
            // V4's scanner occupies the forward side of one spawn; the regression runner selects clear lobby floor.
            var movement = OnlineIdentity.Argument("-motel-online-direction") == "back" ? Vector3.back : Vector3.forward;
            float end = Time.realtimeSinceStartup + .5f;
            while (Time.realtimeSinceStartup < end) { own.GetComponent<CharacterController>().Move(movement * Time.deltaTime * 2); yield return null; }
            Require(Vector3.Distance(from, own.transform.position) > .7f, "Owner did not move");
            Mark("moved-" + role, own.OwnerClientId + "|" + JsonUtility.ToJson(own.transform.position));
            yield return Wait(() => new[] { "host", "client1", "client2", "client3" }.All(r => Marked("moved-" + r)), "all owners moved");
            yield return new WaitForSecondsRealtime(1);
            foreach (string actor in new[] { "host", "client1", "client2", "client3" })
            {
                string[] data = File.ReadAllText(Path.Combine(directory, "moved-" + actor + ".signal")).Split('|');
                var player = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None).Single(p => p.OwnerClientId == ulong.Parse(data[0]));
                Require(Vector3.Distance(player.transform.position, JsonUtility.FromJson<Vector3>(data[1])) < .15f, "Movement not replicated: " + actor);
            }
            checks.Add("All four owner movements replicated to this process"); Mark("movement-checked-" + role);
            yield return Wait(() => new[] { "host", "client1", "client2", "client3" }.All(r => Marked("movement-checked-" + r)), "movement checks");
            if (role == "client2")
            {
                string identity = online.PlayerId;
                yield return TaskDone(online.LeaveLobbyAsync(), "leave active game"); Mark("client-left");
                yield return Wait(() => Marked("slot-free"), "slot cleanup"); yield return Join("code-one");
                yield return Wait(() => online.InGame, "late rejoin spawn");
                Require(online.PlayerId == identity, "Rejoin lost cached identity"); Mark("client-rejoined");
            }
            else if (role == "host")
            {
                yield return Wait(() => Marked("client-left") && online.Session.PlayerCount == 3 && local.Manager.ConnectedClients.Count == 3, "disconnected member cleanup");
                Mark("slot-free"); yield return Wait(() => Marked("client-rejoined") && local.Manager.ConnectedClients.Count == 4, "same-identity rejoin"); Mark("rejoin-complete");
            }
            yield return Wait(() => Marked("rejoin-complete"), "rejoin complete");
            checks.Add("Client leave/rejoin; current game retained; four players restored");
            if (role == "host") { yield return TaskDone(online.LeaveLobbyAsync(), "host graceful leave"); Mark("host-left"); }
            else yield return Wait(() => online.Session == null && !online.OnlineMode && !online.Busy && !local.Manager.IsListening, "host departure returns to menu", 55);
            Mark("left-" + role); checks.Add("Graceful host departure returns everyone to menu");
            yield return Wait(() => new[] { "host", "client1", "client2", "client3" }.All(r => Marked("left-" + r)), "all back at menu");
            if (role == "host")
            {
                yield return TaskDone(online.CreateLobbyAsync(), "create second session without restart"); Require(online.Session != null, online.Error); Mark("code-two", online.Session.Code);
                yield return Wait(() => online.Session.PlayerCount == 4 && online.CanStartGame, "second lobby ready"); Require(online.StartGame(), "Second Start Game");
            }
            else yield return Join("code-two");
            yield return Wait(() => online.InGame, "second-session spawn"); CheckOwnership();
            Require(FindFirstObjectByType<NightShiftController>().State.Phase == ShiftPhase.WaitingToStart, "New session retained old shift");
            Mark("second-ready-" + role); checks.Add("New session created/joined after shutdown without restarting application");
            if (role == "host")
            {
                yield return Wait(() => new[] { "host", "client1", "client2", "client3" }.All(r => Marked("second-ready-" + r)), "all second-session players");
                File.WriteAllText(Path.Combine(directory, "host-before-crash.txt"), string.Join("\n", checks)); Mark("kill-host");
                yield return Wait(() => false, "runner terminates host process", 60);
            }
            else
            {
                yield return Wait(() => online.Session == null && !online.OnlineMode && !online.Busy && !local.Manager.IsListening, "abrupt host loss returns to menu", 60);
                Require(!online.InGame, "Owned player survived host loss"); checks.Add("Abrupt host termination detected; player cleaned up; menu recovered");
            }
        }
        void CheckOwnership()
        {
            foreach (var player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
            {
                bool own = player.IsOwner;
                Require(player.GetComponent<FirstPersonController>().enabled == own && player.GetComponent<CharacterController>().enabled == own, "Remote input/capsule active");
                Require(player.GetComponentInChildren<Camera>(true).enabled == own && player.GetComponentInChildren<AudioListener>(true).enabled == own && player.BodyVisible != own, "Camera/listener/body ownership");
            }
        }
        void Finish(bool pass, string reason)
        {
            File.WriteAllText(Path.Combine(directory, role + ".txt"), (pass ? "PASS " : "FAIL ") + role + "\n" + string.Join("\n", checks) + "\n" + reason + "\n" + string.Join("\n", errors));
            Application.Quit(pass ? 0 : 1);
        }
        void OnDestroy() => Application.logMessageReceived -= Log;
    }
}
#endif
