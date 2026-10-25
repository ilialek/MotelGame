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
        [SerializeField] GuestLifecycle[] guests;
        [SerializeField] GuestServiceRequests serviceRequests;
        [SerializeField, Min(0)] float firstArrivalDelay = 3;
        [SerializeField, Min(1)] float arrivalInterval = 6;
        double nextArrival;
        int nextGuest;
        Vector2 scroll;
        [SerializeField] Collider counter;
        [SerializeField] GameObject waitingGuest;
        [SerializeField] TextMesh guestLabel;
        PlayerInteractor menuOwner;
        public ServiceRoom[] Rooms => rooms;
        public Collider Counter => counter;
        public GameObject WaitingGuest => waitingGuest;
        public bool MenuOpen => menuOwner != null;
        public GuestLifecycle[] Guests => guests;
        public ServiceRoom OccupiedRoom => System.Array.Find(rooms,r=>r.IsSpawned && r.State.Status==RoomStatus.Occupied);
        public GuestLifecycle WaitingNpc => System.Array.Find(guests,g=>g.IsSpawned && g.CanCheckIn);
        public GuestLifecycle CheckoutNpc => System.Array.Find(guests,g=>g.IsSpawned && g.CanCheckoutFor(g.Room));
        public bool GuestWaiting => IsSpawned && WaitingNpc != null;
        public bool CanRegister(ServiceRoom room) => GuestWaiting && System.Array.IndexOf(rooms,room)>=0 && room.AvailableForCheckIn;
        public override void OnNetworkSpawn()
        {
            if(IsServer){nextArrival=NetworkManager.ServerTime.Time+firstArrivalDelay;nextGuest=0;}
            waitingGuest.SetActive(false);guestLabel.gameObject.SetActive(false);
        }
        void Update()
        {
            if (IsSpawned && IsServer) ScheduleReception();
            if (menuOwner == null) return;
            if (!IsSpawned || !menuOwner.IsSpawned || !menuOwner.IsOwner ||
                Vector3.Distance(menuOwner.EyePosition, counter.ClosestPoint(menuOwner.EyePosition)) > PlayerInteractor.MaxDistance ||
                (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)) CloseMenu();
        }
        void ScheduleReception()
        {
            double now=NetworkManager.ServerTime.Time;
            foreach(var guest in guests)
                if(guest.IsSpawned && (guest.Phase==GuestPhase.Arriving || guest.Phase==GuestPhase.WaitingForCheckIn ||
                    guest.Phase==GuestPhase.ReturningForCheckout || guest.Phase==GuestPhase.WaitingForCheckout ||
                    ((guest.Phase==GuestPhase.WalkingToRoom || guest.Phase==GuestPhase.Leaving) &&
                        Vector3.Distance(guest.transform.position,guest.ReceptionPoint.position)<2)))
                {nextArrival=now+arrivalInterval;return;}
            // Oldest expired stay goes first. Four bounded rooms need no separate queue.
            GuestLifecycle due=null;
            foreach(var guest in guests)
                if(guest.CheckoutDue && (due==null || guest.State.StayEndsAt<due.State.StayEndsAt)) due=guest;
            if(due!=null && due.TryBeginCheckoutReturn())
            {
                serviceRequests.ValidateSource(); // Cancel its unfinished call/towels in this transaction.
                nextArrival=now+arrivalInterval;return;
            }
            // A fifth arrival cannot wait here while all rooms are occupied: it would
            // prevent the only operation that can free a room from reaching the desk.
            if(!System.Array.Exists(rooms,r=>r.IsSpawned && r.State.Status!=RoomStatus.Occupied))return;
            if(now<nextArrival)return;
            for(int i=0;i<guests.Length;i++)
            {
                int index=(nextGuest+i)%guests.Length;
                if(guests[index].StartArrival()){nextGuest=(index+1)%guests.Length;nextArrival=now+arrivalInterval;return;}
            }
            nextArrival=now+1;
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
            var waiting=WaitingNpc;
            if (index >= 0 && index < rooms.Length && waiting!=null)
                RequestCheckInRpc(index, rooms[index].State.Cycle,new NetworkObjectReference(waiting.NetworkObject),waiting.State.Visit);
        }
        public void Checkout(int index)
        {
            var guest=CheckoutNpc;
            if (index >= 0 && index < rooms.Length && guest!=null && guest.Room==rooms[index])
                RequestCheckoutRpc(index, rooms[index].State.Cycle,new NetworkObjectReference(guest.NetworkObject),guest.State.Visit);
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestCheckInRpc(int index, uint cycle, NetworkObjectReference expectedGuest = default, uint visit = 0, RpcParams rpcParams = default)
        {
            if (!IsServer || !IsSpawned) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            string message;
            if (!ServiceInteractionValidation.CanReach(NetworkManager, sender, counter)) message = "Use the Reception desk nearby.";
            else if (index < 0 || index >= rooms.Length) message = "Invalid room.";
            else if (!GuestWaiting || !expectedGuest.TryGet(out var selected,NetworkManager) || selected!=WaitingNpc.NetworkObject || WaitingNpc.State.Visit!=visit) message = "Waiting guest changed. Select again.";
            else if (!WaitingNpc.TryCheckIn(rooms[index],cycle)) message = "Room unavailable. Refresh your selection.";
            else message = $"Guest checked into Room {rooms[index].RoomNumber}.";
            FeedbackRpc(message, RpcTarget.Single(sender, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestCheckoutRpc(int index, uint cycle, NetworkObjectReference expectedGuest = default, uint visit = 0, RpcParams rpcParams = default)
        {
            if (!IsServer || !IsSpawned) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            string message;
            if (!ServiceInteractionValidation.CanReach(NetworkManager, sender, counter)) message = "Use the Reception desk nearby.";
            else if (index < 0 || index >= rooms.Length || CheckoutNpc==null ||
                !expectedGuest.TryGet(out var selected,NetworkManager) || selected!=CheckoutNpc.NetworkObject ||
                CheckoutNpc.State.Visit!=visit || !rooms[index].TryCheckout(cycle,CheckoutNpc))
                message = "Checkout unavailable. Guest or room changed; select the waiting guest again.";
            else message = $"Key returned — Room {rooms[index].RoomNumber} is Dirty. Guest departing.";
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
            float height=Mathf.Min(600,Screen.height-24);
            GUILayout.BeginArea(new Rect((Screen.width - width) / 2, (Screen.height-height)/2, width, height),
                "RECEPTION", GUI.skin.window);
            var checkout=CheckoutNpc;
            GUILayout.Label(checkout!=null ? checkout.GuestName+" — Room "+checkout.Room.RoomNumber+" — Ready to check out" :
                GuestWaiting ? WaitingNpc.GuestName + " waiting to check in — choose a Ready room" : "No guest waiting at Reception");
            if(checkout!=null)
            {
                var room=checkout.Room;int index=System.Array.IndexOf(rooms,room);
                if(GUILayout.Button($"Check out — Room {room.RoomNumber} / {checkout.GuestName}"))
                    RequestCheckoutRpc(index,room.State.Cycle,new NetworkObjectReference(checkout.NetworkObject),checkout.State.Visit);
            }
            scroll=GUILayout.BeginScrollView(scroll);
            foreach (var room in rooms)
            {
                int index = System.Array.IndexOf(rooms, room);
                GUILayout.Space(10);
                GUILayout.Label($"Room {room.RoomNumber} — {room.State.Status}" + (room.Occupant!=null ? " — "+room.Occupant.GuestName : ""));
                GUILayout.Label($"Bed: {(room.State.BedMade ? "made" : "unmade")} | Trash: {room.TrashRemovedCount}/{room.Trash.Length}");
                GUI.enabled = CanRegister(room);
                if (GUILayout.Button($"Check in guest → Room {room.RoomNumber}")) SelectRoom(index);
                GUI.enabled = true;
            }
            GUILayout.EndScrollView();
            GUILayout.Space(10);
            GUILayout.Label("Guests return here when their stay ends.\nAccept the key before cleaning their room.");
            if (GUILayout.Button("Close (Esc)")) CloseMenu();
            GUILayout.EndArea();
        }
        public bool ConfigurationValid()
        {
            if (serviceRequests == null || rooms == null || rooms.Length == 0 || guests==null || guests.Length!=rooms.Length+1 || counter == null || waitingGuest == null || guestLabel == null) return false;
            for (int i = 0; i < rooms.Length; i++)
            {
                if (rooms[i] == null) return false;
                for (int j = 0; j < i; j++) if (rooms[j] == rooms[i] || rooms[j].RoomNumber == rooms[i].RoomNumber) return false;
            }
            for(int i=0;i<guests.Length;i++)
            {
                if(guests[i]==null)return false;
                for(int j=0;j<i;j++)if(guests[i]==guests[j])return false;
            }
            return true;
        }
    }
}
