using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using MotelGame.Service;
using MotelGame.Networking;
using MotelGame.Registration;

namespace MotelGame.Horror
{
    // Legacy enum values stay serialized-compatible with preserved scenes/tools; the underground runner uses only the new cycle.
    public enum MimicPhase : byte
    {
        Dormant,
        MovingThroughDucts,
        ApproachingTarget,
        ApproachingBait,
        Emerging,
        Exposed,
        Attacking,
        Retreating,
        Dead,
        Infiltrating,
        Exploring,
        SelectingTarget,
        Hidden,
        PreparingExcursion,
        ActiveHunt,
        HiddenTransit,
        Submerging,
        Defending
    }

    public struct MimicSnapshot : INetworkSerializable, IEquatable<MimicSnapshot>
    {
        public bool Active;
        public MimicPhase Phase;
        public int Node, Health, HiddenDestination;
        public uint Revision, Hits;
        public double Since, Until;
        public void NetworkSerialize<T>(BufferSerializer<T> s)
            where T : IReaderWriter
        {
            s.SerializeValue(ref Active);
            s.SerializeValue(ref Phase);
            s.SerializeValue(ref Node);
            s.SerializeValue(ref Health);
            s.SerializeValue(ref HiddenDestination);
            s.SerializeValue(ref Revision);
            s.SerializeValue(ref Hits);
            s.SerializeValue(ref Since);
            s.SerializeValue(ref Until);
        }

        public bool Equals(MimicSnapshot v) => Active == v.Active && Phase == v.Phase && Node == v.Node && Health == v.Health && HiddenDestination == v.HiddenDestination && Revision == v.Revision && Hits == v.Hits && Since == v.Since && Until == v.Until;
    }

    public struct VentActivity : INetworkSerializable, IEquatable<VentActivity>
    {
        public int Sector;
        public double Until;
        public uint Revision;
        public void NetworkSerialize<T>(BufferSerializer<T> s)
            where T : IReaderWriter
        {
            s.SerializeValue(ref Sector);
            s.SerializeValue(ref Until);
            s.SerializeValue(ref Revision);
        }

        public bool Equals(VentActivity v) => Sector == v.Sector && Until == v.Until && Revision == v.Revision;
    }

    [DefaultExecutionOrder(250), RequireComponent(typeof(NetworkObject), typeof(NetworkTransform))]
    public sealed class MimicHunt : NetworkBehaviour
    {
        public static MimicHunt Instance { get; private set; }

        [SerializeField]
        VentNode[] nodes;
        [SerializeField]
        NightShiftController shift;
        [SerializeField]
        HuntEquipment decoy, firearm;
        [SerializeField]
        GameObject creature;
        [SerializeField]
        Collider body;
        [SerializeField]
        AudioClip scratch, warning, attack, pain, death;
        [SerializeField, Min(1)]
        int maximumHealth = 100;
        [SerializeField, Min(.1f)]
        float ductSpeed = .75f, retreatSpeed = .9f;
        [SerializeField]
        Vector2 excursionInterval = new(45, 75), preparationSeconds = new(15, 20), excursionDuration = new(60, 90), hiddenTransitSeconds = new(3, 6);
        [SerializeField, Min(0)]
        float nextMimicDelay = 10;
        [SerializeField, Min(.1f)]
        float interceptionRadius = 3, defenseRange = 1.25f, defenseWindup = 1.6f, defenseCooldown = 5, defenseStagger = 1.2f;
        [SerializeField, Min(.1f)]
        float sphereRadius = .3f;
        readonly NetworkVariable<MimicSnapshot> state = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<VentActivity> activity = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkList<Vector3> Remains;
        readonly List<GameObject> remainsVisuals = new();
        readonly List<(GuestLifecycle guest, uint visit)> waiting = new();
        readonly List<int> route = new();
        readonly Dictionary<ulong, Vector3> previousPlayers = new();
        GuestLifecycle carrier, target;
        int routeStep, hole, verticalStep;
        double nextActivation, nextPlan, activeUntil, commitAt, nextDefense, nextMotion, nextFalsePositive, nextScratch;
        ulong defendingPlayer;
        bool abortOnEmergence, quickSetup;
        uint presentedHits;
        double Now => NetworkManager.ServerTime.Time;
        public MimicSnapshot State => state.Value;
        public VentNode[] Nodes => nodes;
        public NightShiftController Shift => shift;
        public HuntEquipment Decoy => decoy;
        public HuntEquipment Firearm => firearm;
        public Collider Body => body;
        public GuestLifecycle DebugCarrier => IsServer ? carrier : null;
        public string DebugTarget => IsServer && target != null ? target.GuestName : "None";
        public int DebugNextNode => routeStep < route.Count ? route[routeStep] : -1;
        public int ValidVictims => IsServer ? shift.Reception.Guests.Count(Eligible) : 0;
        public float InitialExplorationSeconds => preparationSeconds.x;
        public double HuntAge => IsServer && State.Active ? Now - State.Since : 0;
        public VentActivity Activity => activity.Value;
        public string Complaint => ""; // Room-specific accusations from the old AI are intentionally removed.
        public bool Visible => IsSpawned && State.Active && (State.Phase == MimicPhase.Emerging || State.Phase == MimicPhase.ActiveHunt || State.Phase == MimicPhase.Retreating || State.Phase == MimicPhase.Submerging || State.Phase == MimicPhase.Defending);
        public string ActivityText => IsSpawned && Now < activity.Value.Until ? "UNDERGROUND ACTIVITY — SECTOR " + (char)('A' + activity.Value.Sector) : "UNDERGROUND ACTIVITY — NO RECENT SIGNAL";
        public bool ComputerUsedBy(ulong id) => FindObjectsByType<ReceptionComputer>(FindObjectsSortMode.None).Any(c => c.Occupied && c.User == id);
        void Awake()
        {
            Instance = this;
            Remains = new NetworkList<Vector3>();
        }

        public override void OnNetworkSpawn()
        {
            state.OnValueChanged += Changed;
            if (IsServer)
            {
                state.Value = new MimicSnapshot{Health = maximumHealth};
                nextFalsePositive = Now + UnityEngine.Random.Range(35, 70);
            }

            Present();
        }

        protected override void OnNetworkSessionSynchronized() => Present();
        public override void OnNetworkDespawn()
        {
            state.OnValueChanged -= Changed;
            creature.SetActive(false);
            body.enabled = false;
            ClearRemains();
            waiting.Clear();
        }

        public override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }

        void Changed(MimicSnapshot before, MimicSnapshot after) => Present();
        void Present()
        {
            bool show = Visible;
            // NGO state and transform snapshots can arrive in different orders. Keep the sphere
            // hidden until its emergence transform reaches the designated opaque pocket.
            if (show && State.Phase == MimicPhase.Emerging)
                show = State.Node >= 0 && State.Node < nodes.Length && nodes[State.Node].hiddenAnchor != null &&
                    Vector3.Distance(transform.position, nodes[State.Node].hiddenAnchor.position) < 2.5f;
            creature.SetActive(show);
            body.enabled = show;
            if (Remains.Count < remainsVisuals.Count)
                ClearRemains();
            while (remainsVisuals.Count < Remains.Count)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                g.name = "EliminatedMimic";
                Destroy(g.GetComponent<Collider>());
                g.transform.position = Remains[remainsVisuals.Count];
                g.transform.localScale = new Vector3(.6f, .28f, .6f);
                var r = g.GetComponent<Renderer>();
                r.sharedMaterial = creature.GetComponentInChildren<Renderer>(true).sharedMaterial;
                r.renderingLayerMask = 2;
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", new Color(.12f, .05f, .09f));
                r.SetPropertyBlock(block);
                remainsVisuals.Add(g);
            }

            if (presentedHits != State.Hits)
            {
                presentedHits = State.Hits;
                creature.transform.localScale = Vector3.one * .85f;
            }

            creature.transform.localScale = Vector3.Lerp(creature.transform.localScale, Vector3.one, Time.deltaTime * 8);
        }

        void ClearRemains()
        {
            foreach (var g in remainsVisuals)
                if (g != null)
                    Destroy(g);
            remainsVisuals.Clear();
        }

        void Phase(MimicPhase p, double until = 0)
        {
            var s = State;
            s.Phase = p;
            s.Since = Now;
            s.Until = until;
            state.Value = s;
        }

        static float RandomSeconds(Vector2 range) => UnityEngine.Random.Range(range.x, range.y);
        internal bool QueueGuestOnServer(GuestLifecycle guest)
        {
            if (!IsServer || !IsSpawned || !shift.IsRunning || guest == null || !guest.IsSpawned || guest.Phase != GuestPhase.InRoom || guest.GetComponent<RegisteredGuest>()?.IsMimicOnServer != true)
                return false;
            if (guest == carrier || waiting.Any(v => v.guest == guest && v.visit == guest.State.Visit))
                return false;
            waiting.Add((guest, guest.State.Visit));
            return true;
        }

        bool ActivateNext()
        {
            while (waiting.Count > 0)
            {
                var entry = waiting[0];
                waiting.RemoveAt(0);
                var g = entry.guest;
                if (g == null || !g.IsSpawned || g.State.Visit != entry.visit || g.Phase != GuestPhase.InRoom || !g.IsInRoomFor(g.Room))
                    continue;
                if (PlayerInside(g.Room) || g.Room.Door.IsOpen || g.Room.CourtyardDoor.IsOpen)
                {
                    waiting.Insert(0, entry);
                    return false;
                }

                if (!g.RevealMimicOnServer())
                    continue;
                g.GetComponent<RegisteredGuest>().MarkMissingOnServer();
                carrier = g;
                target = null;
                route.Clear();
                commitAt = 0;
                state.Value = new MimicSnapshot{Active = true, Health = maximumHealth, Phase = MimicPhase.Hidden, Node = 0, Revision = State.Revision + 1, Since = Now, Until = Now + RandomSeconds(excursionInterval)};
                return true;
            }

            return false;
        }

        public void ResetOnServer()
        {
            if (!IsServer || !IsSpawned)
                return;
            waiting.Clear();
            route.Clear();
            carrier = target = null;
            quickSetup = false;
            nextActivation = commitAt = 0;
            Remains.Clear();
            previousPlayers.Clear();
            state.Value = new MimicSnapshot{Health = maximumHealth, Revision = State.Revision + 1};
            activity.Value = default;
            nextFalsePositive = Now + UnityEngine.Random.Range(35, 70);
            firearm?.ResetOnServer();
            decoy?.ResetOnServer();
            foreach (var c in NetworkManager.ConnectedClientsList)
                c.PlayerObject?.GetComponent<TowelCarrier>()?.ClearStunOnServer();
        }

        bool Eligible(GuestLifecycle g) => g != null && g.IsSpawned && g.Alive && g.Phase == GuestPhase.InRoom && g.Room != null && g.IsInRoomFor(g.Room) && g.GetComponent<RegisteredGuest>()?.IsMimicOnServer != true && nodes.Any(n => n.room == g.Room);
        public float GraphDistance(int from, int to) => ShortestPath(from, to, false, out _);
        float ShortestPath(int from, int to, bool avoidPlayers, out List<int> result)
        {
            result = new();
            if (from < 0 || to < 0 || from >= nodes.Length || to >= nodes.Length)
                return float.PositiveInfinity;
            float[] d = new float[nodes.Length];
            int[] prev = new int[nodes.Length];
            bool[] done = new bool[nodes.Length];
            Array.Fill(d, float.PositiveInfinity);
            Array.Fill(prev, -1);
            d[from] = 0;
            for (int k = 0; k < nodes.Length; k++)
            {
                int u = -1;
                for (int j = 0; j < nodes.Length; j++)
                    if (!done[j] && (u < 0 || d[j] < d[u]))
                        u = j;
                if (u < 0 || float.IsPositiveInfinity(d[u]))
                    break;
                if (u == to)
                    break;
                done[u] = true;
                foreach (var n in nodes[u].neighbours)
                {
                    if (avoidPlayers && !ClearSegment(nodes[u].transform.position, n.transform.position))
                        continue;
                    float value = d[u] + Vector3.Distance(nodes[u].transform.position, n.transform.position);
                    if (value < d[n.index])
                    {
                        d[n.index] = value;
                        prev[n.index] = u;
                    }
                }
            }

            if (float.IsPositiveInfinity(d[to]))
                return d[to];
            for (int at = to; at >= 0; at = prev[at])
            {
                result.Add(at);
                if (at == from)
                    break;
            }

            result.Reverse();
            return d[to];
        }

        bool Plan(int destination, bool avoidPlayers)
        {
            float best = float.PositiveInfinity;
            List<int> chosen = null;
            foreach (var start in nodes.OrderBy(n => (n.transform.position - transform.position).sqrMagnitude))
            {
                float initial = Vector3.Distance(transform.position, start.transform.position);
                if (initial >= best)
                    continue;
                if (initial > .035f && !ClearSegment(transform.position, start.transform.position))
                    continue;
                float cost = ShortestPath(start.index, destination, avoidPlayers, out var path) + initial;
                if (cost < best)
                {
                    best = cost;
                    chosen = path;
                }
            }

            if (chosen == null)
                return false;
            route.Clear();
            route.AddRange(chosen);
            routeStep = 0;
            return true;
        }

        bool ClearSegment(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            foreach (var c in Physics.OverlapSphere(from, sphereRadius * .96f, ~0, QueryTriggerInteraction.Ignore))
                if (!c.transform.IsChildOf(transform))
                    return false;
            if (delta.sqrMagnitude < .0001f)
                return true;
            return !Physics.SphereCastAll(from, sphereRadius * .96f, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore).Any(h => !h.collider.transform.IsChildOf(transform));
        }

        bool Move(Vector3 goal, float speed)
        {
            var next = Vector3.MoveTowards(transform.position, goal, speed * Time.deltaTime);
            if (!ClearSegment(transform.position, next))
                return false;
            var direction = next - transform.position;
            if (direction.sqrMagnitude > 0)
                transform.rotation = Quaternion.LookRotation(direction);
            transform.position = next;
            return true;
        }

        bool Travel(float speed)
        {
            if (routeStep >= route.Count)
                return true;
            int n = route[routeStep];
            if (!Move(nodes[n].transform.position, speed))
                return false;
            if (Vector3.Distance(transform.position, nodes[n].transform.position) < .015f)
            {
                var s = State;
                s.Node = n;
                state.Value = s;
                routeStep++;
            }

            return routeStep >= route.Count;
        }

        void SelectTarget()
        {
            target = shift.Reception.Guests.Where(Eligible).OrderBy(g => GraphDistance(State.Node, Array.FindIndex(nodes, n => n.room == g.Room))).FirstOrDefault();
            if (target == null || !Plan(Array.FindIndex(nodes, n => n.room == target.Room), false))
            {
                BeginRetreat();
                return;
            }
        }

        bool PlayerSees(Vector3 point, float radius = .3f)
        {
            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                var p = c.PlayerObject?.GetComponent<NetworkPlayer>();
                if (p == null)
                    continue;
                if (!p.ViewFresh)
                    return true; // A stale view cannot authorize an unobserved kill or pop-in.
                Vector3 delta = point - p.ViewOrigin;
                float distance = delta.magnitude;
                if (distance < radius)
                    return true;
                // Enclose the entire rectangular frustum, including its corners, in a conservative cone.
                float diagonal = Mathf.Atan(Mathf.Tan(p.ViewFov * Mathf.Deg2Rad * .5f) * Mathf.Sqrt(1 + p.ViewAspect * p.ViewAspect)) * Mathf.Rad2Deg;
                float cone = diagonal + Mathf.Asin(Mathf.Min(1, radius / distance)) * Mathf.Rad2Deg;
                if (Vector3.Angle(p.ViewDirection, delta) > cone)
                    continue;
                if (!Physics.RaycastAll(p.ViewOrigin, delta.normalized, Mathf.Max(0, distance - radius), ~0, QueryTriggerInteraction.Ignore).Any(h => !h.collider.transform.IsChildOf(transform) && h.collider.GetComponentInParent<NetworkPlayer>() == null))
                    return true;
            }

            return false;
        }

        bool PlayerInside(ServiceRoom room)
        {
            var volume = nodes.Single(n => n.room == room).roomVolume;
            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                var p = c.PlayerObject;
                if (p == null)
                    continue;
                var cc = p.GetComponent<CharacterController>();
                Vector3 local = volume.transform.InverseTransformPoint(p.transform.position + Vector3.up * cc.height * .5f) - volume.center;
                var half = volume.size * .5f;
                // Includes the doorway margin, so a player entering on the commit tick protects the room.
                if (Mathf.Abs(local.x) < half.x + cc.radius + .2f && Mathf.Abs(local.z) < half.z + cc.radius + .2f && Mathf.Abs(local.y) < half.y + cc.height * .5f)
                    return true;
            }

            return false;
        }

        internal bool CanCommitGuestDeath(GuestLifecycle guest)
        {
            if (!IsServer || !Eligible(guest))
                return false;
            int node = Array.FindIndex(nodes, n => n.room == guest.Room);
            if (node < 0 || Vector3.Distance(transform.position, nodes[node].transform.position) > .08f || PlayerInside(guest.Room))
                return false;
            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                if (c.PlayerObject == null)
                    continue;
                var p = c.PlayerObject;
                if (p.transform.position.y < -.5f && Vector3.Distance(p.transform.position + Vector3.up * p.GetComponent<CharacterController>().height * .5f, transform.position) < interceptionRadius)
                    return false;
            }

            return !PlayerSees(transform.position);
        }

        void BeginRetreat()
        {
            target = null;
            commitAt = 0;
            route.Clear();
            nextPlan = Now;
            Phase(MimicPhase.Retreating);
        }

        void PlanEscape()
        {
            nextPlan = Now + .5;
            foreach (var n in nodes.Where(n => n.hiddenAnchor != null).OrderBy(n => GraphDistance(State.Node, n.index)))
                if (Plan(n.index, true))
                {
                    hole = n.index;
                    return;
                }

            route.Clear();
            routeStep = 0;
            var close = NetworkManager.ConnectedClientsList.Where(c => c.PlayerObject != null && !c.PlayerObject.GetComponent<TowelCarrier>().Incapacitated).OrderBy(c => Vector3.Distance(c.PlayerObject.transform.position + Vector3.up * .45f, transform.position)).FirstOrDefault();
            if (close != null && Now >= nextDefense && Vector3.Distance(close.PlayerObject.transform.position + Vector3.up * .45f, transform.position) < defenseRange)
            {
                defendingPlayer = close.ClientId;
                Phase(MimicPhase.Defending, Now + defenseWindup);
                SoundRpc(1, transform.position);
            }
        }

        bool ChooseHole()
        {
            var choices = new[]{nodes[hole]}.Concat(nodes.Where(n => n.hiddenAnchor != null && n.index != hole).OrderBy(_ => UnityEngine.Random.value));
            foreach (var n in choices)
            {
                if (PlayerSees(n.transform.position) || PlayerSees(n.hiddenAnchor.position) || !ClearSegment(n.hiddenAnchor.position, n.hiddenBend.position) || !ClearSegment(n.hiddenBend.position, n.transform.position))
                    continue;
                hole = n.index;
                var s = State;
                s.HiddenDestination = hole;
                state.Value = s;
                return true;
            }

            return false;
        }

        [SerializeField] AudioClip activityAlert;
        double nextActivityAlert;
        float activityNoticeUntil;
        public int LocalActivityAlertCount {get;private set;}
        void Signal(int sector,bool emergence=false)
        {
            bool newEpisode=activity.Value.Until<Now;
            activity.Value = new VentActivity{Sector = sector, Until = Now + 9, Revision = activity.Value.Revision + 1};
            if(emergence || newEpisode && Now>=nextActivityAlert)
            {nextActivityAlert=Now+20;ActivityAlertRpc();}
        }
        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        void ActivityAlertRpc()
        {
            LocalActivityAlertCount++;activityNoticeUntil=Time.unscaledTime+5;
            if(activityAlert==null)return;
            var go=new GameObject("UndergroundActivityAlert");var source=go.AddComponent<AudioSource>();
            source.clip=activityAlert;source.spatialBlend=0;source.volume=.6f;source.Play();Destroy(go,activityAlert.length+.1f);
        }
        void OnGUI()
        {
            if(!IsSpawned||Time.unscaledTime>=activityNoticeUntil)return;
            var matrix=GUI.matrix;var color=GUI.color;
            try
            {
                float scale=Mathf.Min(Screen.width/900f,Screen.height/600f);GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));GUI.color=Color.white;
                var style=new GUIStyle(GUI.skin.box){fontSize=17,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
                GUI.Box(new Rect((Screen.width/scale-310)*.5f,12,310,36),"UNDERGROUND ACTIVITY",style);
            }
            finally{GUI.matrix=matrix;GUI.color=color;}
        }

        void DetectMotion()
        {
            if (Now < nextMotion)
                return;
            nextMotion = Now + .7;
            if (State.Phase == MimicPhase.PreparingExcursion)
                Signal(nodes[hole].sector);
            else if (Visible)
                Signal(nodes.OrderBy(n => (n.transform.position - transform.position).sqrMagnitude).First().sector);
            foreach (var c in NetworkManager.ConnectedClientsList)
            {
                var p = c.PlayerObject;
                if (p == null)
                    continue;
                if (previousPlayers.TryGetValue(c.ClientId, out var previous) && p.transform.position.y < -.6f && Vector3.Distance(previous, p.transform.position) > .12f)
                    Signal(nodes.OrderBy(n => (n.transform.position - p.transform.position).sqrMagnitude).First().sector);
                previousPlayers[c.ClientId] = p.transform.position;
            }

            if (Now >= nextFalsePositive)
            {
                Signal(UnityEngine.Random.Range(0, 3));
                nextFalsePositive = Now + UnityEngine.Random.Range(45, 90);
            }
        }

        void LateUpdate()
        {
            if (!IsSpawned)
                return;
            Present();
            if (!IsServer || !shift.IsRunning)
                return;
            Physics.SyncTransforms();
            DetectMotion();
            if (Visible && Now >= nextScratch)
            {
                SoundRpc(0, transform.position);
                nextScratch = Now + UnityEngine.Random.Range(5f, 9f);
            }

            if (quickSetup)
            {
                var waitingGuest = shift.Reception.WaitingNpc;
                var room = shift.Reception.Rooms.FirstOrDefault(r => r.AvailableForCheckIn);
                if (waitingGuest != null && room != null)
                    waitingGuest.TryCheckIn(room, room.State.Cycle);
            }

            if (!State.Active)
            {
                if (Now >= nextActivation)
                    ActivateNext();
                return;
            }

            switch (State.Phase)
            {
                case MimicPhase.Hidden:
                    if (Now >= State.Until)
                    {
                        hole = nodes.Where(n => n.hiddenAnchor != null).OrderBy(_ => UnityEngine.Random.value).First().index;
                        Signal(nodes[hole].sector);
                        SoundRpc(0, nodes[hole].transform.position);
                        Phase(MimicPhase.PreparingExcursion, Now + RandomSeconds(preparationSeconds));
                    }

                    break;
                case MimicPhase.PreparingExcursion:
                    if (Now < State.Until || Now < nextPlan)
                        break;
                    nextPlan = Now + 1;
                    if (!ChooseHole())
                        break;
                    // Both endpoints lie behind an opaque bend; the sphere appears only in this hidden pocket.
                    GetComponent<NetworkTransform>().Teleport(nodes[hole].hiddenAnchor.position, Quaternion.identity, Vector3.one);
                    var s = State;
                    s.Node = hole;
                    state.Value = s;
                    verticalStep = 0;
                    abortOnEmergence = false;
                    Phase(MimicPhase.Emerging);
                    break;
                case MimicPhase.Emerging:
                    var emerge = verticalStep == 0 ? nodes[hole].hiddenBend.position : nodes[hole].transform.position;
                    if (!Move(emerge, .7f))
                    {
                        verticalStep = verticalStep == 0 ? 1 : 0;
                        Phase(MimicPhase.Submerging);
                        break;
                    }

                    if (Vector3.Distance(transform.position, emerge) < .015f && ++verticalStep == 2)
                    {
                        if (abortOnEmergence)
                            BeginRetreat();
                        else
                        {
                            activeUntil = Now + RandomSeconds(excursionDuration);
                            Phase(MimicPhase.ActiveHunt, activeUntil);
                            Signal(nodes[hole].sector,true);
                            SelectTarget();
                        }
                    }

                    break;
                case MimicPhase.ActiveHunt:
                    if (Now >= activeUntil)
                    {
                        BeginRetreat();
                        break;
                    }

                    if (!Eligible(target))
                    {
                        SelectTarget();
                        break;
                    }

                    if (commitAt > 0)
                    {
                        if (!CanCommitGuestDeath(target))
                        {
                            BeginRetreat();
                            break;
                        }

                        if (Now >= commitAt)
                        {
                            if (CanCommitGuestDeath(target) && target.KillOnServer())
                                SoundRpc(2, nodes[State.Node].roomVolume.bounds.center);
                            BeginRetreat();
                        }

                        break;
                    }

                    if (!Travel(ductSpeed))
                    {
                        if (routeStep < route.Count && !ClearSegment(transform.position, Vector3.MoveTowards(transform.position, nodes[route[routeStep]].transform.position, .15f)))
                            BeginRetreat();
                        break;
                    }

                    if (!CanCommitGuestDeath(target))
                    {
                        BeginRetreat();
                        break;
                    }

                    // A short commit window admits the next received movement/view tick; safety is rechecked, never waited out.
                    commitAt = Now + .15;
                    break;
                case MimicPhase.Retreating:
                    if (routeStep >= route.Count)
                    {
                        if (route.Count > 0 && State.Node == hole)
                        {
                            verticalStep = 0;
                            Phase(MimicPhase.Submerging);
                            break;
                        }

                        if (Now >= nextPlan)
                            PlanEscape();
                        break;
                    }

                    if (!Travel(retreatSpeed) && !ClearSegment(transform.position, Vector3.MoveTowards(transform.position, nodes[route[routeStep]].transform.position, .15f)))
                    {
                        route.Clear();
                        routeStep = 0;
                        nextPlan = Now;
                    }

                    break;
                case MimicPhase.Defending:
                    if (Now < State.Until)
                        break;
                    if (NetworkManager.ConnectedClients.TryGetValue(defendingPlayer, out var victim) && victim.PlayerObject != null)
                    {
                        var at = victim.PlayerObject.transform.position + Vector3.up * .45f;
                        var delta = at - transform.position;
                        bool wall = Physics.RaycastAll(transform.position, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore).Any(h => !h.collider.transform.IsChildOf(transform) && !h.collider.transform.IsChildOf(victim.PlayerObject.transform));
                        if (delta.magnitude < defenseRange && !wall)
                        {
                            victim.PlayerObject.GetComponent<TowelCarrier>().StunOnServer(defenseStagger);
                            SoundRpc(2, transform.position);
                        }
                    }

                    nextDefense = Now + defenseCooldown;
                    BeginRetreat();
                    break;
                case MimicPhase.Submerging:
                    var retreat = verticalStep == 0 ? nodes[hole].hiddenBend.position : nodes[hole].hiddenAnchor.position;
                    if (!Move(retreat, .9f))
                    {
                        BeginRetreat();
                        break;
                    }

                    if (Vector3.Distance(transform.position, retreat) < .015f)
                    {
                        if (verticalStep == 0)
                        {
                            verticalStep = 1;
                            break;
                        }

                        if (!PlayerSees(transform.position))
                        {
                            var h = State;
                            h.HiddenDestination = nodes.Where(n => n.hiddenAnchor != null && n.index != hole).OrderBy(_ => UnityEngine.Random.value).First().index;
                            state.Value = h;
                            Phase(MimicPhase.HiddenTransit, Now + RandomSeconds(hiddenTransitSeconds));
                        }
                    }

                    break;
                case MimicPhase.HiddenTransit:
                    if (Now >= State.Until)
                    {
                        var arrived = State;
                        arrived.Node = arrived.HiddenDestination;
                        state.Value = arrived;
                        Phase(MimicPhase.Hidden, Now + RandomSeconds(excursionInterval));
                    }

                    break;
            }
        }

        public bool DamageOnServer(int amount)
        {
            if (!IsServer || !Visible || amount <= 0 || State.Health <= 0)
                return false;
            var s = State;
            s.Health = Mathf.Max(0, s.Health - amount);
            s.Hits++;
            if (s.Health == 0)
            {
                s.Active = false;
                s.Phase = MimicPhase.Dead;
                s.Since = Now;
                state.Value = s;
                Remains.Add(transform.position - Vector3.up * .15f);
                shift.RecordMimicKill();
                nextActivation = Now + nextMimicDelay;
                target = null;
                route.Clear();
                SoundRpc(4, transform.position);
                return true;
            }

            state.Value = s;
            SoundRpc(3, transform.position);
            if (State.Phase == MimicPhase.Emerging)
                abortOnEmergence = true;
            else if (State.Phase != MimicPhase.Submerging)
                BeginRetreat();
            return true;
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        void SoundRpc(byte kind, Vector3 at)
        {
            var clip = kind == 0 ? scratch : kind == 1 ? warning : kind == 2 ? attack : kind == 3 ? pain : death;
            if (clip == null)
                return;
            var go = new GameObject("UndergroundSound");
            go.transform.position = at;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.spatialBlend = 1;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1;
            source.maxDistance = kind == 2 ? 14 : 8;
            source.volume = .7f;
            source.Play();
            Destroy(go, clip.length + .1f);
        }

        // Preserved API for archived tooling. No legacy bait, room chase, baggage infiltration or evacuation is active.
        public bool BeginGuestInfiltrationOnServer(GuestLifecycle g) => QueueGuestOnServer(g);
        public bool BeginInfiltrationOnServer(int n, GuestLifecycle g, Vector3 p) => false;
        public bool ActivateOnServer(int n, GuestLifecycle g) => QueueGuestOnServer(g);
        public bool DecoyRelevantAt(int n) => false;
        public void HarmlessComplaintOnServer()
        {
            if (IsServer)
                Signal(UnityEngine.Random.Range(0, 3));
        }

        public bool PrepareQuickTestOnServer()
        {
            if (!IsServer || !IsSpawned)
                return false;
            if (!shift.IsRunning)
                shift.DebugStartHuntShift();
            foreach (var r in shift.Reception.Rooms)
                r.PrepareForHuntDebug();
            quickSetup = true;
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void EvacuateRpc(NetworkObjectReference reference, uint visit, uint revision, RpcParams rpc = default)
        {
        }

        public bool ConfigurationValid() => shift != null && firearm != null && creature != null && body is SphereCollider && nodes != null && nodes.Length > 6 && nodes.All(n => n.underground && n.index >= 0 && n.index < nodes.Length && n.neighbours.All(x => x.neighbours.Contains(n))) && nodes.Count(n => n.room != null && n.roomVolume != null) == 6 && nodes.Count(n => n.hiddenAnchor != null) >= 3;
    }
}
