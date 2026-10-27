using UnityEngine;
using UnityEngine.InputSystem;
using MotelGame.Interactions;

namespace MotelGame.Networking
{
    [RequireComponent(typeof(OnlineSessionController))]
    public sealed class OnlineSessionMenu : MonoBehaviour
    {
        OnlineSessionController online;
        DevelopmentSession local;
        string code = "";
        GUIStyle title, text, button, codeStyle;
        Rect Panel => online.InGame ? new Rect(12, 12, 390, 560) : new Rect((Screen.width - 440) / 2f, Mathf.Max(10, (Screen.height - 548) / 2f), 440, 548);
        public bool ContainsMouse
        {
            get
            {
                if (Cursor.lockState == CursorLockMode.Locked || online.LanMenu) return false;
                if (!online.InGame) return true;
                Vector2 point = Mouse.current?.position.ReadValue() ?? Vector2.zero;
                point.y = Screen.height - point.y;
                return Panel.Contains(point);
            }
        }
        void Awake() { online = GetComponent<OnlineSessionController>(); local = GetComponent<DevelopmentSession>(); }
        void Update()
        {
            if (!online.InGame && !local.Manager.IsListening || online.OnlineMode && !online.InGame)
            { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
        void OnGUI()
        {
            if (online.LanMenu) return;
            if (local.Manager.LocalClient?.PlayerObject?.GetComponent<PlayerInteractor>().MenuOpen == true) return;
            EnsureStyles();
            if (online.InGame && Cursor.lockState == CursorLockMode.Locked)
            {
                GUI.Label(new Rect(12, 12, 470, 28), "ONLINE · " + (online.IsHost ? "HOST" : "CLIENT") + " · Esc: lobby menu", text);
                return;
            }
            Color old = GUI.color;
            GUI.color = new Color(.06f, .075f, .085f, .98f); GUI.DrawTexture(Panel, Texture2D.whiteTexture); GUI.color = old;
            GUILayout.BeginArea(new Rect(Panel.x + 22, Panel.y + 18, Panel.width - 44, Panel.height - 36));
            GUILayout.Label("MOTEL GAME", title);
            GUILayout.Label("NIGHT SHIFT / ONLINE PLAY", text);
            GUILayout.Space(12);
            GUILayout.Label(online.Status, text);
            if (!string.IsNullOrEmpty(online.Error))
            { GUI.contentColor = new Color(1, .65f, .55f); GUILayout.Label(online.Error, text); GUI.contentColor = Color.white; }
            GUILayout.Space(10);
            if (online.Session != null)
            {
                GUILayout.Label("PRIVATE LOBBY", text);
                GUILayout.Label(online.Session.Code, codeStyle);
                if (GUILayout.Button("Copy Code", button)) GUIUtility.systemCopyBuffer = online.Session.Code;
                GUILayout.Label("Players: " + online.Session.PlayerCount + "/" + OnlineSessionController.Capacity, text);
                int i = 0;
                foreach (var player in online.Session.Players)
                    GUILayout.Label((++i) + ". " + (player.Id == online.Session.Host ? "Host" : "Player " + i) + (player.Id == online.PlayerId ? " (you)" : ""), text);
                GUILayout.Space(8);
                if (!online.InGame)
                {
                    if (online.IsHost)
                    { GUI.enabled = online.CanStartGame; if (GUILayout.Button("Start Game", button)) online.StartGame(); GUI.enabled = true; }
                    else GUILayout.Label("Waiting for the host to start…", text);
                }
                else if (GUILayout.Button("Resume Game", button)) { local.Manager.LocalClient.PlayerObject.GetComponent<FirstPersonController>().ResumeAfterComputer(); }
                if (GUILayout.Button(online.IsHost ? "End Session" : "Leave Lobby", button)) _ = online.LeaveLobbyAsync();
            }
            else if (online.Busy)
            {
                GUILayout.Label("Please wait…", text);
                if (GUILayout.Button("Cancel", button)) online.Cancel();
            }
            else if (!online.Authenticated)
            {
                if (GUILayout.Button("Retry Connection", button)) _ = online.AuthenticateAsync();
            }
            else
            {
                if (GUILayout.Button("Create Lobby", button)) _ = online.CreateLobbyAsync();
                GUILayout.Space(8);
                GUILayout.Label("Invite code", text);
                code = GUILayout.TextField(code, 32, GUILayout.Height(30));
                if (GUILayout.Button("Join Lobby", button)) _ = online.JoinLobbyAsync(code);
            }
            GUILayout.FlexibleSpace();
            if (online.Session == null && !online.Busy && !local.Manager.IsListening && GUILayout.Button("Local / LAN Debug", button)) online.LanMenu = true;
            if (GUILayout.Button("Quit", button)) _ = online.QuitAsync();
            GUILayout.Label(OnlineSessionController.BuildVersion, text);
            GUILayout.EndArea(); GUI.enabled = true;
        }
        void EnsureStyles()
        {
            if (title != null) return;
            text = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, normal = { textColor = new Color(.86f, .86f, .78f) } };
            title = new GUIStyle(text) { fontSize = 26, fontStyle = FontStyle.Bold };
            codeStyle = new GUIStyle(title) { fontSize = 25, alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button) { fontSize = 15, fixedHeight = 32, margin = new RectOffset(0, 0, 3, 3) };
        }
    }
}
