using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MotelGame.Networking
{
    [DefaultExecutionOrder(-100)]
    public sealed class DevelopmentSession : MonoBehaviour
    {
        [SerializeField] NetworkManager manager;
        [SerializeField] LocalTransportSettings transportSettings;
        [SerializeField] Camera lobbyCamera;
        [SerializeField] Vector3[] spawnPositions = {
            new Vector3(6, .05f, 2), new Vector3(8, .05f, 2),
            new Vector3(6, .05f, 4), new Vector3(8, .05f, 4) };
        readonly Dictionary<ulong, int> slots = new Dictionary<ulong, int>();
        readonly Rect panel = new Rect(12, 12, 330, 255);
        string status = "Disconnected";
        string portText;
        bool starting;
        public NetworkManager Manager => manager;
        public LocalTransportSettings TransportSettings => transportSettings;

        void Awake()
        {
            Application.runInBackground = true;
            portText = transportSettings.Port.ToString();
            manager.ConnectionApprovalCallback = Approve;
            manager.OnClientConnectedCallback += Connected;
            manager.OnClientDisconnectCallback += Disconnected;
            manager.OnTransportFailure += TransportFailed;
            manager.OnServerStopped += ServerStopped;
            manager.OnClientStopped += ClientStopped;
        }

        public bool StartSession(bool host)
        {
            if (starting || manager.IsListening || manager.ShutdownInProgress) return false;
            try
            {
                transportSettings.Configure(host);
                slots.Clear();
                starting = true;
                status = host ? "Starting host…" : "Connecting…";
                bool success = host ? manager.StartHost() : manager.StartClient();
                if (!success) { starting = false; status = "Could not start session. Check Console/port."; }
                return success;
            }
            catch (Exception e)
            {
                starting = false;
                status = e.Message;
                if (manager.IsListening) manager.Shutdown();
                return false;
            }
        }

        public void Disconnect()
        {
            status = "Disconnecting…";
            starting = false;
            manager.Shutdown();
        }

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            int free = -1;
            for (int i = 0; i < Mathf.Min(4, spawnPositions.Length); i++)
                if (!slots.ContainsValue(i)) { free = i; break; }
            response.Approved = free >= 0;
            response.CreatePlayerObject = free >= 0;
            response.Pending = false;
            if (free < 0) { response.Reason = "Session is full (4 players)."; return; }
            slots[request.ClientNetworkId] = free;
            response.Position = spawnPositions[free];
            response.Rotation = Quaternion.identity;
        }

        void Connected(ulong id)
        {
            if (id != manager.LocalClientId) return;
            starting = false;
            status = manager.IsHost ? "Host connected" : "Client connected";
        }
        void Disconnected(ulong id)
        {
            slots.Remove(id);
            if (id != manager.LocalClientId) return;
            starting = false;
            status = string.IsNullOrEmpty(manager.DisconnectReason) ? "Disconnected" : manager.DisconnectReason;
        }
        void TransportFailed() { starting = false; status = "Transport failed. Check address/port and Console."; }
        void ServerStopped(bool wasHost) { slots.Clear(); starting = false; status = "Disconnected"; }
        void ClientStopped(bool wasHost) { starting = false; if (status == "Disconnecting…") status = "Disconnected"; }

        void Update()
        {
            var local = manager.LocalClient?.PlayerObject;
            bool spawned = local != null && local.IsSpawned;
            lobbyCamera.enabled = !spawned;
            if (!spawned) return;
            var movement = local.GetComponent<FirstPersonController>();
            var mouse = Mouse.current;
            Vector2 point = mouse == null ? Vector2.zero : mouse.position.ReadValue();
            point.y = Screen.height - point.y;
            // Prevent a menu click from also recapturing the FPS cursor in the same frame.
            movement.InputSuspended = local.GetComponent<MotelGame.Interactions.PlayerInteractor>().MenuOpen || Cursor.lockState != CursorLockMode.Locked && panel.Contains(point);
        }

        void OnGUI()
        {
            var localInteractor = manager.LocalClient?.PlayerObject?.GetComponent<MotelGame.Interactions.PlayerInteractor>();
            if (localInteractor != null && localInteractor.MenuOpen) return;
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                GUI.Label(new Rect(12, 12, 430, 30), status + " | Esc: connection menu");
                return;
            }
            GUILayout.BeginArea(panel, "Local Multiplayer — Development", GUI.skin.window);
            GUILayout.Label(status);
            GUI.enabled = !starting && !manager.IsListening && !manager.ShutdownInProgress;
            GUILayout.Label("Host IP (localhost: 127.0.0.1)");
            transportSettings.Address = GUILayout.TextField(transportSettings.Address);
            GUILayout.Label("UDP port");
            portText = GUILayout.TextField(portText);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start Host")) StartFromUI(true);
            if (GUILayout.Button("Start Client")) StartFromUI(false);
            GUILayout.EndHorizontal();
            GUI.enabled = (starting || manager.IsListening) && !manager.ShutdownInProgress;
            if (GUILayout.Button("Disconnect")) Disconnect();
            GUI.enabled = true;
            GUILayout.Label("WASD · Mouse · Left Shift · Esc\nClick outside this panel to capture cursor.");
            GUILayout.EndArea();
        }
        void StartFromUI(bool host)
        {
            if (!ushort.TryParse(portText, out ushort port) || port == 0) { status = "Invalid port."; return; }
            transportSettings.Port = port;
            StartSession(host);
        }
        void OnDestroy()
        {
            if (manager == null) return;
            manager.ConnectionApprovalCallback = null;
            manager.OnClientConnectedCallback -= Connected;
            manager.OnClientDisconnectCallback -= Disconnected;
            manager.OnTransportFailure -= TransportFailed;
            manager.OnServerStopped -= ServerStopped;
            manager.OnClientStopped -= ClientStopped;
        }
    }
}
