using System;
using Unity.Netcode;
using UnityEngine;
using MotelGame.Service;
using MotelGame.Interactions;

namespace MotelGame.Inspection
{
    public enum BaggagePhase : byte
    {
        Hidden,
        WithGuest,
        ToAnalyzer,
        OnAnalyzer,
        Returning,
        PlacingInRoom,
        InRoom,
        Opening,
        Released
    }

    public struct BaggageSnapshot : INetworkSerializable, IEquatable<BaggageSnapshot>
    {
        public uint Visit, Revision;
        public BagShell Shell;
        public BaggagePhase Phase;
        public Vector3 From, RoomPosition, Color;
        public double Since;
        public void NetworkSerialize<T>(BufferSerializer<T> s)
            where T : IReaderWriter
        {
            s.SerializeValue(ref Visit);
            s.SerializeValue(ref Revision);
            s.SerializeValue(ref Shell);
            s.SerializeValue(ref Phase);
            s.SerializeValue(ref From);
            s.SerializeValue(ref RoomPosition);
            s.SerializeValue(ref Color);
            s.SerializeValue(ref Since);
        }

        public bool Equals(BaggageSnapshot b) => Visit == b.Visit && Revision == b.Revision && Shell == b.Shell && Phase == b.Phase && From == b.From && RoomPosition == b.RoomPosition && Color == b.Color && Since == b.Since;
    }

    public sealed class GuestBaggage : NetworkBehaviour
    {
        public Transform visual;
        public GameObject[] shells;
        public Transform[] lids;
        public Renderer[] caseRenderers;
        public Collider handle;
        public TextMesh identity;
        readonly NetworkVariable<BaggageSnapshot> snapshot = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        BaggageCase actualCase;
        bool admitted, released;
        GuestLifecycle guest;
        MaterialPropertyBlock color;
        public GuestLifecycle Guest => guest;
        public BaggageSnapshot State => snapshot.Value;
        public BaggageCase ServerCase => IsServer ? actualCase : null;
        public bool AdmittedOnServer => IsServer && admitted;
        double Now => NetworkManager.ServerTime.Time;
        Vector3 HandPosition => guest.transform.TransformPoint(new Vector3(.42f, .36f, -.12f));
        void Awake()
        {
            guest = GetComponent<GuestLifecycle>();
            color = new MaterialPropertyBlock();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                ResetOnServer();
            Apply();
        }

        public override void OnNetworkDespawn()
        {
            visual.gameObject.SetActive(false);
            handle.enabled = false;
        }

        public void BeginVisitOnServer()
        {
            if (!IsServer || !IsSpawned)
                return;
            actualCase = BaggageInspection.Instance.AssignCaseOnServer();
            admitted = released = false;
            snapshot.Value = new BaggageSnapshot{Visit = guest.State.Visit, Revision = State.Revision + 1, Shell = actualCase.shell, Color = new Vector3(actualCase.color.r, actualCase.color.g, actualCase.color.b), Phase = BaggagePhase.WithGuest, Since = Now};
        }

        public void ResetOnServer()
        {
            if (IsServer)
            {
                actualCase = null;
                admitted = released = false;
                snapshot.Value = new BaggageSnapshot{Revision = State.Revision + 1};
            }
        }

        void Phase(BaggagePhase phase)
        {
            var v = State;
            v.From = visual.position;
            v.Phase = phase;
            v.Since = Now;
            v.Revision++;
            snapshot.Value = v;
        }

        public bool PlaceOnAnalyzer()
        {
            if (!IsServer || guest != BaggageInspection.Instance.Reception.WaitingNpc || State.Phase != BaggagePhase.WithGuest)
                return false;
            Phase(BaggagePhase.ToAnalyzer);
            return true;
        }

        public void DecideOnServer(bool accepted)
        {
            if (!IsServer)
                return;
            admitted = accepted;
            if (State.Phase == BaggagePhase.OnAnalyzer || State.Phase == BaggagePhase.ToAnalyzer)
            {
                Phase(BaggagePhase.Returning);
                guest.PauseForBaggage(1.5f);
            }
        }

        void Update()
        {
            if (!IsSpawned || !IsServer)
                return;
            if (guest.Phase == GuestPhase.Absent)
            {
                if (State.Phase != BaggagePhase.Hidden)
                    ResetOnServer();
                return;
            }

            if (State.Phase == BaggagePhase.Hidden)
                return;
            if (State.Phase == BaggagePhase.ToAnalyzer && Now - State.Since >= 1.4)
                Phase(BaggagePhase.OnAnalyzer);
            if (State.Phase == BaggagePhase.Returning && Now - State.Since >= 1.4)
                Phase(BaggagePhase.WithGuest);
            if (admitted && guest.Phase == GuestPhase.InRoom && State.Phase == BaggagePhase.WithGuest)
            {
                var v = State;
                v.RoomPosition = BaggageInspection.Instance.RoomBagPosition(guest.Room);
                snapshot.Value = v;
                Phase(BaggagePhase.PlacingInRoom);
            }

            if (State.Phase == BaggagePhase.PlacingInRoom && Now - State.Since >= 1.2)
                Phase(released ? BaggagePhase.Released : BaggagePhase.InRoom);
            if (State.Phase == BaggagePhase.InRoom && Now - State.Since >= 8 && actualCase != null && actualCase.containsMimic && !released && BaggageInspection.Instance.AllowInfiltration && guest.Phase == GuestPhase.InRoom)
            {
                if (BaggageInspection.Instance.ReleaseMimicOnServer(this))
                {
                    released = true;
                    Phase(BaggagePhase.Opening);
                }
            }

            if (State.Phase == BaggagePhase.Opening && Now - State.Since >= 3)
                Phase(BaggagePhase.Released);
            // A departing/relocating guest visibly retrieves this same shell. No second luggage object.
            if ((guest.Phase == GuestPhase.ReturningForCheckout || guest.Phase == GuestPhase.Relocating || guest.Phase == GuestPhase.Leaving || guest.Phase == GuestPhase.Evacuating) && (State.Phase == BaggagePhase.InRoom || State.Phase == BaggagePhase.Released))
                Phase(BaggagePhase.Returning);
            if (guest.Phase == GuestPhase.Leaving && (State.Phase == BaggagePhase.OnAnalyzer || State.Phase == BaggagePhase.ToAnalyzer))
                Phase(BaggagePhase.Returning);
        }

        void LateUpdate()
        {
            if (IsSpawned)
                Apply();
        }

        void Apply()
        {
            bool visible = State.Phase != BaggagePhase.Hidden && guest.Phase != GuestPhase.Absent;
            visual.gameObject.SetActive(visible);
            handle.enabled = visible && (State.Phase == BaggagePhase.WithGuest || State.Phase == BaggagePhase.OnAnalyzer || State.Phase == BaggagePhase.InRoom);
            if (!visible)
                return;
            for (int i = 0; i < shells.Length; i++)
                shells[i].SetActive(i == (int)State.Shell);
            color.SetColor("_BaseColor", new Color(State.Color.x, State.Color.y, State.Color.z));
            foreach (var r in caseRenderers)
                r.SetPropertyBlock(color);
            float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01((float)(Now - State.Since) / 1.4f));
            Vector3 at = HandPosition;
            Quaternion rotation = guest.transform.rotation;
            if (State.Phase == BaggagePhase.ToAnalyzer)
            {
                at = Vector3.Lerp(State.From, BaggageInspection.Instance.Platform.position, t);
                at.y += Mathf.Sin(t * Mathf.PI) * .55f;
                rotation = BaggageInspection.Instance.Platform.rotation;
            }
            else if (State.Phase == BaggagePhase.OnAnalyzer)
            {
                at = BaggageInspection.Instance.Platform.position;
                rotation = BaggageInspection.Instance.Platform.rotation;
            }
            else if (State.Phase == BaggagePhase.Returning)
            {
                at = Vector3.Lerp(State.From, HandPosition, t);
                at.y += Mathf.Sin(t * Mathf.PI) * .55f;
            }
            else if (State.Phase == BaggagePhase.PlacingInRoom)
                at = Vector3.Lerp(State.From, State.RoomPosition, t);
            else if (State.Phase == BaggagePhase.InRoom || State.Phase == BaggagePhase.Opening || State.Phase == BaggagePhase.Released)
                at = State.RoomPosition;
            visual.SetPositionAndRotation(at, rotation);
            float lidAngle = State.Phase == BaggagePhase.Opening ? -75 * Mathf.SmoothStep(0, 1, Mathf.Clamp01((float)(Now - State.Since))) : State.Phase == BaggagePhase.Released ? -75 : 0;
            foreach (var lid in lids)
                lid.localRotation = Quaternion.Euler(lidAngle, 0, 0);
            identity.text = guest.GuestName + " / " + State.Shell.ToString().ToUpperInvariant();
        }
    }
}
