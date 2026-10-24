using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using MotelGame.Interactions;

namespace MotelGame.Service
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ReceptionDesk : NetworkBehaviour
    {
        [SerializeField] ServiceRoom[] rooms;
        [SerializeField] Collider counter;
        [SerializeField] GameObject waitingGuest;
        [SerializeField] TextMesh guestLabel;
        PlayerInteractor menuOwner;
        public ServiceRoom[] Rooms => rooms;
        public Collider Counter => counter;
        public GameObject WaitingGuest => waitingGuest;
        public bool MenuOpen => menuOwner != null;
        // M4 has one guest. Derive their location from the authoritative room state so
        // occupancy and guest status cannot drift into separate network-variable states.
        public ServiceRoom OccupiedRoom
        {
            get { foreach (var room in rooms) if (room.IsSpawned && room.State.Status == RoomStatus.Occupied) return room; return null; }
        }
        public bool GuestWaiting => IsSpawned && OccupiedRoom == null;
        public bool CanRegister(ServiceRoom room) => GuestWaiting && room.IsSpawned && room.State.Status == RoomStatus.Ready;

        void Update()
        {
            bool waiting = GuestWaiting;
            if (waitingGuest.activeSelf != waiting) waitingGuest.SetActive(waiting);
            guestLabel.gameObject.SetActive(waiting);
            guestLabel.text = "GUEST WAITING\nRegister at Reception";
            if (menuOwner == null) return;
            if (!IsSpawned || !menuOwner.IsSpawned || !menuOwner.IsOwner ||
                Vector3.Distance(menuOwner.EyePosition, counter.ClosestPoint(menuOwner.EyePosition)) > PlayerInteractor.MaxDistance ||
                (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)) CloseMenu();
        }
        public void OpenMenu()
        {
            if (!IsSpawned) return;
            var player = NetworkManager.LocalClient?.PlayerObject;
            var interactor = player == null ? null : player.GetComponent<PlayerInteractor>();
            if (interactor == null || !interactor.IsOwner || !interactor.enabled ||
                Vector3.Distance(interactor.EyePosition, counter.ClosestPoint(interactor.EyePosition)) > PlayerInteractor.MaxDistance) return;
            menuOwner = interactor; menuOwner.MenuOpen = true;
            player.GetComponent<FirstPersonController>().InputSuspended = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        public void CloseMenu()
        {
            if (menuOwner != null) menuOwner.MenuOpen = false;
            menuOwner = null;
            // Keep the cursor released; a click outside the connection panel resumes FPS input.
        }
        public override void OnNetworkDespawn() => CloseMenu();
        void OnDisable() => CloseMenu();

        public void SelectRoom(int index)
        {
            if (index >= 0 && index < rooms.Length) RequestCheckInRpc(index, rooms[index].State.Cycle);
        }
        public void Checkout(int index)
        {
            if (index >= 0 && index < rooms.Length) RequestCheckoutRpc(index, rooms[index].State.Cycle);
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestCheckInRpc(int index, uint cycle, RpcParams rpcParams = default)
        {
            if (!IsServer || !IsSpawned) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            string message;
            if (!ServiceInteractionValidation.CanReach(NetworkManager, sender, counter)) message = "Use the Reception desk nearby.";
            else if (index < 0 || index >= rooms.Length) message = "Invalid room.";
            else if (!GuestWaiting) message = "Guest is already checked in.";
            else if (!rooms[index].TryCheckIn(cycle)) message = "Room unavailable. Refresh your selection.";
            else message = $"Guest checked into Room {rooms[index].RoomNumber}.";
            FeedbackRpc(message, RpcTarget.Single(sender, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestCheckoutRpc(int index, uint cycle, RpcParams rpcParams = default)
        {
            if (!IsServer || !IsSpawned) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            string message;
            if (!ServiceInteractionValidation.CanReach(NetworkManager, sender, counter)) message = "Use the Reception desk nearby.";
            else if (index < 0 || index >= rooms.Length || rooms[index] != OccupiedRoom || !rooms[index].TryCheckout(cycle))
                message = "Checkout unavailable. The room has already changed.";
            else message = $"Room {rooms[index].RoomNumber}: checkout complete. New guest waiting; clean the room again.";
            FeedbackRpc(message, RpcTarget.Single(sender, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void FeedbackRpc(string message, RpcParams rpcParams = default)
        {
            NetworkManager.LocalClient?.PlayerObject?.GetComponent<PlayerInteractor>()?.ShowFeedback(message);
        }
        void OnGUI()
        {
            if (menuOwner == null || !menuOwner.IsOwner || !IsSpawned) return;
            float width = Mathf.Min(480, Screen.width - 24);
            GUILayout.BeginArea(new Rect((Screen.width - width) / 2, Mathf.Max(12, (Screen.height - 330) / 2), width, 330),
                "RECEPTION", GUI.skin.window);
            GUILayout.Label(GuestWaiting ? "Guest waiting — select a Ready room." : $"Guest checked in — Room {OccupiedRoom?.RoomNumber}");
            foreach (var room in rooms)
            {
                int index = System.Array.IndexOf(rooms, room);
                GUILayout.Space(10);
                GUILayout.Label($"Room {room.RoomNumber} — {room.State.Status}");
                GUILayout.Label($"Bed: {(room.State.BedMade ? "made" : "unmade")} | Trash: {room.TrashRemovedCount}/{room.Trash.Length}");
                GUI.enabled = CanRegister(room);
                if (GUILayout.Button($"Check in guest → Room {room.RoomNumber}")) SelectRoom(index);
                GUI.enabled = room.IsSpawned && room == OccupiedRoom;
                if (GUILayout.Button($"TEST: Check out Room {room.RoomNumber}")) Checkout(index);
                GUI.enabled = true;
            }
            GUILayout.Space(10);
            GUILayout.Label("Prototype: guest relocates instantly.\nCheckout resets all cleaning and provides the next waiting guest.");
            if (GUILayout.Button("Close (Esc)")) CloseMenu();
            GUILayout.EndArea();
        }
        public bool ConfigurationValid()
        {
            if (rooms == null || rooms.Length == 0 || counter == null || waitingGuest == null || guestLabel == null) return false;
            for (int i = 0; i < rooms.Length; i++)
            {
                if (rooms[i] == null) return false;
                for (int j = 0; j < i; j++) if (rooms[j] == rooms[i] || rooms[j].RoomNumber == rooms[i].RoomNumber) return false;
            }
            return true;
        }
    }
}
