using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using Unity.Services.Lobbies;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace MotelGame.Networking
{
    [RequireComponent(typeof(DevelopmentSession))]
    public sealed class OnlineSessionController : MonoBehaviour
    {
        public const string BuildVersion = "motel-m15-1";
        public const int Capacity = 4;
        const int OperationSeconds = 45;
        readonly OnlineIdentity identity = new();
        readonly Dictionary<ulong, string> identities = new();
        readonly HashSet<string> removing = new();
        DevelopmentSession local;
        CancellationTokenSource operation;
        Task activeTask, leaveTask;
        bool closing, destroyed, quitAllowed, gameStarted;
        string originalHost;
        public ISession Session { get; private set; }
        public bool Busy { get; private set; }
        public bool OnlineMode { get; private set; }
        public bool Authenticated => identity.Ready;
        public bool InGame => local.Manager.LocalClient?.PlayerObject != null;
        public bool IsHost => Session != null && Session.Host == identity.PlayerId && local.Manager.IsHost;
        public string Status { get; private set; } = "Connecting to Unity services…";
        public string Error { get; private set; } = "";
        public string Profile => identity.Profile;
        public string PlayerId => identity.PlayerId;
        public bool LanMenu { get; set; }
        public bool CanStartGame => IsHost && !Busy && !gameStarted && local.Manager.ConnectedClients.Count == Session.PlayerCount;
        [Serializable] sealed class Handshake { public string build; public string player; }

        void Awake()
        {
            local = GetComponent<DevelopmentSession>();
            local.Manager.OnClientDisconnectCallback += OnDisconnected;
            local.Manager.OnTransportFailure += OnTransportFailure;
            Application.wantsToQuit += WantsToQuit;
        }
        async void Start()
        {
            // Existing explicit LAN regression runners must remain independent of UGS availability.
            bool test = Environment.GetCommandLineArgs().Any(a => a.StartsWith("-motel-") && a.EndsWith("-test"));
            if (test && !Environment.GetCommandLineArgs().Contains("-motel-online")) { LanMenu = true; return; }
            await AuthenticateAsync();
        }
        public Task AuthenticateAsync() => RunAsync(async token =>
        {
            Status = "Signing in…";
            await AwaitAuthenticationAsync(token);
            token.ThrowIfCancellationRequested();
            Status = "Online ready";
        });
        public Task CreateLobbyAsync() => RunAsync(async token =>
        {
            await AwaitAuthenticationAsync(token);
            token.ThrowIfCancellationRequested();
            PrepareConnection();
            Status = "Creating private lobby and Relay connection…";
            var options = new SessionOptions
            {
                Name = "MOTEL GAME — Private Lobby", MaxPlayers = Capacity, IsPrivate = true,
                SessionProperties = new Dictionary<string, SessionProperty> { ["build"] = new(BuildVersion, VisibilityPropertyOptions.Member) }
            }.WithRelayNetwork().WithNetworkOptions(new NetworkOptions { RelayProtocol = RelayProtocol.DTLS });
            Session = await MultiplayerService.Instance.CreateSessionAsync(options, token);
            FinishConnection(token);
        });
        public Task JoinLobbyAsync(string code) => RunAsync(async token =>
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length < 4 || code.Length > 16 || code.Any(c => !(c >= 'A' && c <= 'Z' || c >= '0' && c <= '9')))
                throw new ArgumentException("Enter the invite code shared by the host.");
            await AwaitAuthenticationAsync(token);
            token.ThrowIfCancellationRequested();
            PrepareConnection();
            Status = "Joining private lobby through Relay…";
            var options = new JoinSessionOptions().WithNetworkOptions(new NetworkOptions { RelayProtocol = RelayProtocol.DTLS });
            Session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code, options, token);
            FinishConnection(token);
        });
        async Task AwaitAuthenticationAsync(CancellationToken token)
        {
            var authentication = identity.InitializeAsync();
            if (await Task.WhenAny(authentication, Task.Delay(Timeout.Infinite, token)) != authentication)
            { _ = ObserveCleanupAsync(authentication); token.ThrowIfCancellationRequested(); }
            await authentication;
        }
        void PrepareConnection()
        {
            if (Session != null || local.Manager.IsListening || local.Manager.ShutdownInProgress)
                throw new InvalidOperationException("Leave the current session first.");
            OnlineMode = true; LanMenu = false; gameStarted = false;
            identities.Clear(); removing.Clear(); local.ResetSpawnSlots();
            local.Manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Handshake { build = BuildVersion, player = identity.PlayerId }));
        }
        void FinishConnection(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!Session.Properties.TryGetValue("build", out var version) || version.Value != BuildVersion)
                throw new InvalidOperationException("Incompatible build. All players need the same game version.");
            if (!local.Manager.IsConnectedClient || local.Manager.GetComponent<UnityTransport>().Protocol != UnityTransport.ProtocolType.RelayUnityTransport)
                throw new InvalidOperationException("Relay did not establish the game connection.");
            originalHost = Session.Host;
            Session.Deleted += OnSessionLost;
            Session.RemovedFromSession += OnSessionLost;
            Session.SessionHostChanged += OnHostChanged;
            Status = "Connected through Unity Relay";
            Debug.Log("[M13] Private Relay session connected; players=" + Session.PlayerCount + "; host=" + IsHost);
        }
        async Task RunAsync(Func<CancellationToken, Task> action)
        {
            if (Busy || closing || Session != null || local.Manager.IsListening) return;
            Busy = true; Error = "";
            operation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            operation.CancelAfter(TimeSpan.FromSeconds(OperationSeconds));
            try { activeTask = action(operation.Token); await activeTask; }
            catch (Exception e)
            {
                if (destroyed) return;
                Error = e is OperationCanceledException ? "Connection cancelled or timed out. Check your internet and retry." : PlayerError(e);
                string disconnect = local.Manager.DisconnectReason;
                if (!string.IsNullOrEmpty(disconnect) && (disconnect.StartsWith("Incompatible build") || disconnect.StartsWith("Session is full") || disconnect.StartsWith("This player"))) Error = disconnect;
                LogFailure("connect", e);
                await CleanupAsync();
                Status = "Not connected";
            }
            finally { activeTask = null; operation?.Dispose(); operation = null; Busy = false; }
        }
        public void Cancel() { Status = "Cancelling connection…"; operation?.Cancel(); }
        public bool Approve(ulong clientId, byte[] payload, out string reason)
        {
            reason = "";
            if (!OnlineMode) return true;
            Handshake h = null;
            try { if (payload != null && payload.Length <= 512) h = JsonUtility.FromJson<Handshake>(Encoding.UTF8.GetString(payload)); } catch (Exception) { }
            if (h == null || h.build != BuildVersion)
            { reason = "Incompatible build. All players need the same game version."; return false; }
            if (string.IsNullOrEmpty(h.player) || removing.Contains(h.player) || identities.Any(p => p.Key != clientId && p.Value == h.player))
            { reason = "This player is already connected or still disconnecting. Retry in a moment."; return false; }
            identities[clientId] = h.player;
            return true;
        }
        public bool ShouldSpawnPlayer => !OnlineMode || gameStarted;
        public bool StartGame()
        {
            if (!CanStartGame) return false;
            gameStarted = true;
            local.SpawnWaitingPlayers();
            Status = "In game — start the night at the reception computer";
            return true;
        }
        public Task LeaveLobbyAsync(string message = "Left lobby")
        {
            if (closing) return leaveTask ?? Task.CompletedTask;
            closing = true; Busy = true;
            leaveTask = LeaveCoreAsync(message);
            return leaveTask;
        }
        async Task LeaveCoreAsync(string message)
        {
            operation?.Cancel();
            if (activeTask != null) { try { await activeTask; } catch (Exception) { } }
            try { await CleanupAsync(); Status = message; }
            finally { closing = false; Busy = false; }
        }
        async Task CleanupAsync()
        {
            var session = Session;
            Session = null;
            if (session != null)
            {
                session.Deleted -= OnSessionLost;
                session.RemovedFromSession -= OnSessionLost;
                session.SessionHostChanged -= OnHostChanged;
                try
                {
                    Task cleanup = session.IsHost ? session.AsHost().DeleteAsync() : session.LeaveAsync();
                    if (await Task.WhenAny(cleanup, Task.Delay(8000)) == cleanup) await cleanup;
                    else { LogFailure("cleanup timeout", new TimeoutException()); _ = ObserveCleanupAsync(cleanup); }
                }
                catch (Exception e) { LogFailure("cleanup", e); }
            }
            if (local != null && local.Manager != null && local.Manager.IsListening) local.Manager.Shutdown();
            float shutdownDeadline = Time.realtimeSinceStartup + 5;
            while (!destroyed && local != null && local.Manager != null && local.Manager.ShutdownInProgress && Time.realtimeSinceStartup < shutdownDeadline) await Task.Yield();
            if (local != null && local.Manager.ShutdownInProgress) Error = "Network shutdown did not finish. Please restart the game.";
            OnlineMode = false; gameStarted = false; identities.Clear(); removing.Clear();
            if (local != null) { local.ResetSpawnSlots(); local.Manager.NetworkConfig.ConnectionData = Array.Empty<byte>(); }
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        static async Task ObserveCleanupAsync(Task task) { try { await task; } catch (Exception e) { LogFailure("late cleanup", e); } }
        void OnSessionLost() { if (!closing && !Busy && OnlineMode) _ = LeaveLobbyAsync("The host ended the session. Create or join another lobby."); }
        void OnHostChanged(string host) { if (host != originalHost) OnSessionLost(); }
        void OnTransportFailure() { if (!closing && !Busy && OnlineMode) _ = LeaveLobbyAsync("Connection lost. Check your internet and rejoin using the code."); }
        void OnDisconnected(ulong id)
        {
            if (!OnlineMode || closing) return;
            if (!local.Manager.IsServer)
            {
                if (!Busy) _ = LeaveLobbyAsync("Host disconnected or connection lost. Return to a new lobby.");
                return;
            }
            if (identities.Remove(id, out string player) && Session != null && player != identity.PlayerId)
                _ = RemoveDisconnectedPlayerAsync(player, Session);
        }
        async Task RemoveDisconnectedPlayerAsync(string player, ISession session)
        {
            removing.Add(player);
            try { if (session.IsHost && session.HasPlayer(player)) await session.AsHost().RemovePlayerAsync(player); }
            catch (Exception e) { LogFailure("remove disconnected member", e); }
            finally { removing.Remove(player); }
        }
        bool WantsToQuit()
        {
            if (quitAllowed || !OnlineMode && !Busy) return true;
            _ = QuitAsync(); return false;
        }
        public async Task QuitAsync()
        {
            await LeaveLobbyAsync(); quitAllowed = true; Application.Quit();
        }
        void OnDestroy()
        {
            destroyed = true; operation?.Cancel(); identity.Dispose(); Application.wantsToQuit -= WantsToQuit;
            if (local == null || local.Manager == null) return;
            local.Manager.OnClientDisconnectCallback -= OnDisconnected;
            local.Manager.OnTransportFailure -= OnTransportFailure;
        }
        static string PlayerError(Exception e)
        {
            for (Exception current = e; current != null; current = current.InnerException)
            {
                if (current is LobbyServiceException lobby)
                    switch (lobby.Reason)
                    {
                        case LobbyExceptionReason.LobbyFull: return "Lobby is full (4 players).";
                        case LobbyExceptionReason.LobbyNotFound:
                        case LobbyExceptionReason.InvalidJoinCode: return "Invite code is invalid or this lobby has expired.";
                        case LobbyExceptionReason.RateLimited: return "Too many attempts. Wait a few seconds and retry.";
                        case LobbyExceptionReason.Forbidden:
                        case LobbyExceptionReason.PaymentRequired: return "Online services are unavailable for this project. Contact the host.";
                    }
                if (current is SessionException session)
                {
                    // MPS 2.4.1 discards LobbyFull/InvalidJoinCode's typed reason and inner exception.
                    // Classify only known service phrases; never display the raw service payload.
                    string message = session.Message.ToLowerInvariant();
                    if (message.Contains("full")) return "Lobby is full (4 players).";
                    if (message.Contains("invalid") && message.Contains("code")) return "Invite code is invalid or this lobby has expired.";
                    if (session.Error == SessionError.SessionNotFound || session.Error == SessionError.SessionDeleted)
                        return "This lobby no longer exists. Ask the host for a new code.";
                    if (session.Error == SessionError.RateLimitExceeded) return "Too many attempts. Wait a few seconds and retry.";
                    if (session.Error == SessionError.NetworkSetupFailed || session.Error == SessionError.NetworkManagerStartFailed)
                        return "Relay could not connect to the host. Check your internet and game version, then retry.";
                }
            }
            // Only our own known errors are shown verbatim. Never expose HTTP/auth response bodies.
            if (e is ArgumentException || e is InvalidOperationException) return e.Message;
            if (e is RequestFailedException) return "Could not sign in to Unity services. Check your internet and retry.";
            return "Could not connect to the lobby. Check the code, internet connection and game version, then retry.";
        }
        static void LogFailure(string step, Exception e)
        {
            var diagnostics = new List<string>();
            for (var current = e; current != null; current = current.InnerException)
                diagnostics.Add(current.GetType().Name + (current is RequestFailedException r ? " code=" + r.ErrorCode : current is SessionException s ? " code=" + s.Error : ""));
            Debug.LogWarning("[M13] " + step + ": " + string.Join(" -> ", diagnostics));
        }
    }
}
