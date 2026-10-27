using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Collections;
using UnityEngine;
using UnityEngine.AI;
using MotelGame.Service;
using MotelGame.Networking;

namespace MotelGame.Horror
{
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
        SelectingTarget
    }

    public struct MimicSnapshot : INetworkSerializable, IEquatable<MimicSnapshot>
    {
        public bool Active;
        public MimicPhase Phase;
        public int Node, Health;
        public uint Revision, Hits;
        public double Since;
        public void NetworkSerialize<T>(BufferSerializer<T> s)
            where T : IReaderWriter
        {
            s.SerializeValue(ref Active);
            s.SerializeValue(ref Phase);
            s.SerializeValue(ref Node);
            s.SerializeValue(ref Health);
            s.SerializeValue(ref Revision);
            s.SerializeValue(ref Hits);
            s.SerializeValue(ref Since);
        }

        public bool Equals(MimicSnapshot v) => Active == v.Active && Phase == v.Phase && Node == v.Node && Health == v.Health && Revision == v.Revision && Hits == v.Hits && Since == v.Since;
    }

    [RequireComponent(typeof(NetworkObject), typeof(NetworkTransform))]
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
        float ductSpeed = 1.1f, floorSpeed = 1.65f;
        [SerializeField, Min(1)]
        float heatGraphRange = 5.5f, warningSeconds = 12, exposedSeconds = 14, attackWindup = 1.6f;
        [SerializeField, Min(0)] float explorationSeconds = 85, acquisitionSeconds = 8, explorationPause = 1.5f;
        [SerializeField, Min(.1f)] float emergenceSeconds = 5, infiltrationSpeed = .7f, retreatSpeed = 1;
        [SerializeField, Min(0)] float retreatHeatCooldown = 10;
        double exploreUntil, acquisitionEnds, huntStartedAt;
        bool acquiredTarget;
        int previousNode = -1, infiltrationStep;
        int[] explorationVisits;
        readonly List<Vector3> infiltrationPath = new();
        public int ValidVictims => IsServer ? shift.Reception.Guests.Count(Eligible) : 0;
        public float InitialExplorationSeconds => explorationSeconds;
        public double HuntAge => IsServer && State.Active ? Now - huntStartedAt : 0;
        readonly NetworkVariable<MimicSnapshot> state = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        readonly NetworkVariable<FixedString128Bytes> complaint = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        GuestLifecycle carrier, targetGuest, attackGuest;
        uint carrierVisit;
        ulong? attackPlayer;
        int nextNode = -1, destinationNode = -1, emergeStep, retreatStep;
        bool baitGoal, quickSetup;
        double nextDecision, nextScratch, exposureEnds, attackAllowed, ignoreHeatUntil;
        readonly List<Vector3> trail = new();
        Vector3 emergenceEnd;
        public MimicSnapshot State => state.Value;
        public VentNode[] Nodes => nodes;
        public NightShiftController Shift => shift;
        public HuntEquipment Decoy => decoy;
        public HuntEquipment Firearm => firearm;
        public Collider Body => body;
        public string Complaint => complaint.Value.ToString();
        public GuestLifecycle DebugCarrier => IsServer && IsCarrier(carrier) ? carrier : null;
        public string DebugTarget => !IsServer ? "" : targetGuest != null ? targetGuest.GuestName : baitGoal ? "Local heat" : "None";
        public int DebugNextNode => IsServer ? nextNode : -1;
        public bool ComputerUsedBy(ulong id)
        {
            var c = FindFirstObjectByType<ReceptionComputer>();
            return c != null && c.Occupied && c.User == id;
        }

        double Now => NetworkManager.ServerTime.Time;
        void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            state.OnValueChanged += Changed;
            if (IsServer)
            {
                state.Value = new MimicSnapshot{Health = maximumHealth, Node = 0};
                NetworkManager.OnClientDisconnectCallback += Disconnected;
            }

            Present();
        }

        public override void OnNetworkDespawn()
        {
            state.OnValueChanged -= Changed;
            if (NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= Disconnected;
            creature.SetActive(false);
            body.enabled = false;
        }

        public override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }

        void Changed(MimicSnapshot before, MimicSnapshot after)
        {
            Present();
        }

        void Present()
        {
            creature.SetActive(IsSpawned && State.Active);
            body.enabled = IsSpawned && State.Active && State.Phase != MimicPhase.Dead;
        }

        void Disconnected(ulong id)
        {
            if (attackPlayer == id)
                attackPlayer = null;
        }

        void Phase(MimicPhase phase)
        {
            var v = State;
            v.Phase = phase;
            v.Since = Now;
            state.Value = v;
        }

        public bool ActivateOnServer(int start, GuestLifecycle designatedCarrier)
        {
            if (!IsServer || !IsSpawned || !shift.IsRunning || State.Active || start < 0 || start >= nodes.Length || designatedCarrier == null || !designatedCarrier.IsSpawned || !designatedCarrier.Alive)
                return false;
            carrier = designatedCarrier;
            acquiredTarget = false; previousNode = -1; explorationVisits = new int[nodes.Length]; exploreUntil = Now + explorationSeconds; huntStartedAt = Now;
            carrierVisit = carrier.State.Visit;
            targetGuest = null;
            attackGuest = null;
            attackPlayer = null;
            nextNode = -1;
            destinationNode = -1;
            trail.Clear();
            nextDecision = Now;
            nextScratch = Now + 2;
            ignoreHeatUntil = 0;
            // Only activation places a dormant, invisible creature. Every subsequent transition is continuous.
            GetComponent<NetworkTransform>().Teleport(nodes[start].transform.position, nodes[start].transform.rotation, Vector3.one);
            state.Value = new MimicSnapshot{Active = true, Health = maximumHealth, Node = start, Revision = State.Revision + 1, Since = Now};
            SoundRpc(0, transform.position);
            return true;
        }

        // The only initial placement is hidden inside this carrier's actual bag. The rest is continuous.
        public bool BeginInfiltrationOnServer(int node, GuestLifecycle designatedCarrier, Vector3 baggagePosition)
        {
            if (!IsServer || !IsSpawned || !shift.IsRunning || State.Active || node < 0 || node >= nodes.Length || nodes[node].room != designatedCarrier.Room) return false;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(baggagePosition, nodes[node].landing.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return false;
            carrier = designatedCarrier; carrierVisit = carrier.State.Visit; targetGuest = attackGuest = null; attackPlayer = null;
            nextNode = destinationNode = -1; trail.Clear(); infiltrationPath.Clear();
            infiltrationPath.Add(baggagePosition + Vector3.up * .5f);
            var exitDirection = (path.corners.Length > 1 ? path.corners[1] : nodes[node].landing.position) - baggagePosition; exitDirection.y = 0;
            infiltrationPath.Add(baggagePosition + Vector3.up * .5f + exitDirection.normalized * .4f);
            foreach (var point in path.corners.Skip(1)) infiltrationPath.Add(new Vector3(point.x, nodes[node].landing.position.y, point.z));
            infiltrationPath.Add(nodes[node].landing.position); infiltrationPath.Add(nodes[node].mouth.position); infiltrationPath.Add(nodes[node].transform.position);
            infiltrationStep = 0; acquiredTarget = false; previousNode = -1; explorationVisits = new int[nodes.Length];
            GetComponent<NetworkTransform>().Teleport(baggagePosition + Vector3.up * .1f, nodes[node].transform.rotation, Vector3.one);
            state.Value = new MimicSnapshot { Active = true, Health = maximumHealth, Node = node, Revision = State.Revision + 1, Phase = MimicPhase.Infiltrating, Since = Now };
            nextScratch = Now + 2; ignoreHeatUntil = 0; huntStartedAt = Now;
            return true;
        }
        public void ResetOnServer()
        {
            if (!IsServer || !IsSpawned)
                return;
            quickSetup = false;
            carrier = null;
            targetGuest = null;
            attackGuest = null;
            attackPlayer = null;
            nextNode = -1;
            trail.Clear();
            state.Value = new MimicSnapshot{Node = 0, Health = maximumHealth, Revision = State.Revision + 1};
            complaint.Value = default;
            decoy?.ResetOnServer();
            firearm?.ResetOnServer();
            foreach (var c in NetworkManager.ConnectedClientsList)
                c.PlayerObject?.GetComponent<TowelCarrier>()?.ClearStunOnServer();
        }

        public bool PrepareQuickTestOnServer()
        {
            if (!IsServer || !IsSpawned || State.Active)
                return false;
            if (!shift.IsRunning)
                shift.DebugStartHuntShift();
            foreach (var room in shift.Reception.Rooms)
                room.PrepareForHuntDebug();
            quickSetup = true;
            return true;
        }

        public void HarmlessComplaintOnServer()
        {
            if (!IsServer || !IsSpawned)
                return;
            var fan = nodes.First(n => n.looseFan);
            complaint.Value = new FixedString128Bytes("Room " + fan.room.RoomNumber + ": regular rattling in the ventilation.");
            SoundRpc(5, fan.transform.position);
        }

        public float GraphDistance(int from, int to)
        {
            var d = new float[nodes.Length];
            var visited = new bool[nodes.Length];
            Array.Fill(d, float.PositiveInfinity);
            d[from] = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                int u = -1;
                for (int j = 0; j < nodes.Length; j++)
                    if (!visited[j] && (u < 0 || d[j] < d[u]))
                        u = j;
                if (u < 0 || float.IsPositiveInfinity(d[u]))
                    break;
                if (u == to)
                    return d[u];
                visited[u] = true;
                foreach (var n in nodes[u].neighbours)
                    d[n.index] = Mathf.Min(d[n.index], d[u] + Vector3.Distance(nodes[u].transform.position, n.transform.position));
            }

            return d[to];
        }

        int NextHop(int from, int goal)
        {
            return nodes[from].neighbours.OrderBy(n => Vector3.Distance(nodes[from].transform.position, n.transform.position) + GraphDistance(n.index, goal)).First().index;
        }

        public bool DecoyRelevantAt(int node)
        {
            var vent = nodes[node];
            return vent.room != null && decoy.IsHeating && Vector3.Distance(decoy.State.Position, vent.landing.position) < 2.1f;
        }

        bool PlayerHeatAt(VentNode n)
        {
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                var player = client.PlayerObject;
                if (player == null || player.GetComponent<TowelCarrier>().Incapacitated)
                    continue;
                if (Vector3.Distance(player.transform.position, n.landing.position) < 1.65f && ClearLine(n.mouth.position, player.transform.position + Vector3.up * 1.4f, player.transform))
                    return true;
            }

            return false;
        }

        bool IsCarrier(GuestLifecycle guest) => guest != null && guest == carrier && guest.State.Visit == carrierVisit;
        bool Eligible(GuestLifecycle g) => g != null && !IsCarrier(g) && g.IsSpawned && g.Phase == GuestPhase.InRoom && g.Room != null && g.IsInRoomFor(g.Room);
        void Decide()
        {
            int here = State.Node;
            destinationNode = -1;
            baitGoal = false;
            if (Now >= ignoreHeatUntil)
            {
                var heat = nodes.Where(n => n.room != null && GraphDistance(here, n.index) <= heatGraphRange && (DecoyRelevantAt(n.index) || PlayerHeatAt(n))).OrderBy(n => GraphDistance(here, n.index)).FirstOrDefault();
                if (heat != null)
                {
                    destinationNode = heat.index;
                    baitGoal = true;
                    targetGuest = null;
                }
            }

            if (destinationNode < 0 && Now < exploreUntil)
            {
                targetGuest = null;
                var choices = nodes[here].neighbours.Where(n => n.index != previousNode).OrderBy(n => explorationVisits[n.index]).ThenBy(n => n.index).ToArray();
                if (choices.Length == 0) choices = nodes[here].neighbours;
                nextNode = choices[0].index;
                Phase(MimicPhase.Exploring); return;
            }
            if (destinationNode < 0 && !acquiredTarget)
            {
                targetGuest = shift.Reception.Guests.Where(Eligible).OrderBy(g => GraphDistance(here, Array.FindIndex(nodes, n => n.room == g.Room))).FirstOrDefault();
                if (targetGuest != null) { acquisitionEnds = Now + acquisitionSeconds; Phase(MimicPhase.SelectingTarget); return; }
            }
            if (destinationNode < 0)
            {
                targetGuest = shift.Reception.Guests.Where(Eligible).OrderBy(g => GraphDistance(here, Array.FindIndex(nodes, n => n.room == g.Room))).FirstOrDefault();
                if (targetGuest != null)
                    destinationNode = Array.FindIndex(nodes, n => n.room == targetGuest.Room);
            }

            if (destinationNode < 0)
            {
                if (State.Phase != MimicPhase.Dormant)
                    Phase(MimicPhase.Dormant);
                nextDecision = Now + 1;
                return;
            }

            if (here != destinationNode)
            {
                nextNode = NextHop(here, destinationNode);
                Phase(MimicPhase.MovingThroughDucts);
                return;
            }

            Phase(baitGoal ? MimicPhase.ApproachingBait : MimicPhase.ApproachingTarget);
            SoundRpc(1, nodes[here].mouth.position);
            if (!baitGoal)
                complaint.Value = new FixedString128Bytes("Room " + nodes[here].room.RoomNumber + ": something is scraping behind the grille.");
        }

        void Update()
        {
            if (!IsSpawned)
                return;
            var capsule = (CapsuleCollider)body;
            capsule.height = State.Phase == MimicPhase.Infiltrating ? .45f : transform.position.y > .6f ? .65f : 1.1f;
            capsule.center = Vector3.up * (capsule.height * .5f);
            if (!IsServer)
                return;
            if (quickSetup)
            {
                var waiting = shift.Reception.WaitingNpc;
                var ready = shift.Reception.Rooms.FirstOrDefault(r => r.AvailableForCheckIn);
                if (waiting != null && ready != null)
                    waiting.TryCheckIn(ready, ready.State.Cycle);
                if (shift.Reception.Guests.Count(g => g.Phase == GuestPhase.InRoom) >= 2)
                {
                    foreach (var g in shift.Reception.Guests)
                        g.ExtendDebugStay(Now + 480);
                    quickSetup = false;
                }
            }

            if (!shift.IsRunning || !State.Active || State.Phase == MimicPhase.Dead)
                return;
            if (Now >= nextScratch)
            {
                nextScratch = Now + 5.5;
                SoundRpc(0, transform.position);
            }

            switch (State.Phase)
            {
                case MimicPhase.Infiltrating:
                    if (Now - State.Since < 2) break;
                    Move(infiltrationPath[infiltrationStep], infiltrationSpeed);
                    if (Vector3.Distance(transform.position, infiltrationPath[infiltrationStep]) < .015f && ++infiltrationStep == infiltrationPath.Count)
                    { exploreUntil = Now + explorationSeconds; huntStartedAt = Now; nextDecision = Now; Phase(MimicPhase.Exploring); }
                    break;
                case MimicPhase.SelectingTarget:
                    if (nodes.Any(n => n.room != null && GraphDistance(State.Node,n.index) <= heatGraphRange && (DecoyRelevantAt(n.index)||PlayerHeatAt(n)))) Decide();
                    else if (Now >= acquisitionEnds) { acquiredTarget = true; Decide(); }
                    break;
                case MimicPhase.Exploring:
                    if (nextNode < 0) { if (Now >= nextDecision) Decide(); break; }
                    Move(nodes[nextNode].transform.position, ductSpeed);
                    if (Vector3.Distance(transform.position,nodes[nextNode].transform.position) < .015f)
                    { previousNode = State.Node; explorationVisits[nextNode]++; var v = State; v.Node = nextNode; state.Value = v; nextNode = -1; nextDecision = Now + explorationPause; }
                    break;
                case MimicPhase.Dormant:
                    if (Now >= nextDecision)
                        Decide();
                    break;
                case MimicPhase.MovingThroughDucts:
                    Move(nodes[nextNode].transform.position, ductSpeed);
                    if (Vector3.Distance(transform.position, nodes[nextNode].transform.position) < .015f)
                    {
                        var v = State;
                        v.Node = nextNode;
                        state.Value = v;
                        nextNode = -1;
                        Decide();
                    }

                    break;
                case MimicPhase.ApproachingTarget:
                case MimicPhase.ApproachingBait:
                    if (!baitGoal && !Eligible(targetGuest))
                    {
                        Decide();
                        break;
                    }

                    if (Now - State.Since >= warningSeconds)
                    {
                        // Recheck heat locally; a removed decoy never summons a creature from a different branch.
                        if (baitGoal && !DecoyRelevantAt(State.Node) && !PlayerHeatAt(nodes[State.Node]))
                        {
                            Decide();
                            break;
                        }

                        emergenceEnd = nodes[State.Node].landing.position;
                        emergeStep = 0;
                        Phase(MimicPhase.Emerging);
                    }

                    break;
                case MimicPhase.Emerging:
                    Vector3 goal = emergeStep == 0 ? nodes[State.Node].mouth.position : emergenceEnd;
                    Move(goal, (Vector3.Distance(nodes[State.Node].transform.position,nodes[State.Node].mouth.position)+Vector3.Distance(nodes[State.Node].mouth.position,emergenceEnd))/emergenceSeconds);
                    if (Vector3.Distance(transform.position, goal) < .015f)
                    {
                        if (emergeStep++ == 0)
                            break;
                        trail.Clear();
                        trail.Add(transform.position);
                        exposureEnds = Now + exposedSeconds;
                        attackAllowed = Now + 2.5;
                        Phase(MimicPhase.Exposed);
                    }

                    break;
                case MimicPhase.Exposed:
                    Outside();
                    break;
                case MimicPhase.Attacking:
                    if (Now - State.Since >= attackWindup)
                    {
                        if (attackGuest != null && !IsCarrier(attackGuest) && attackGuest.Alive && InReach(attackGuest.transform.position, attackGuest.transform))
                            attackGuest.KillOnServer();
                        if (attackPlayer.HasValue && NetworkManager.ConnectedClients.TryGetValue(attackPlayer.Value, out var c) && c.PlayerObject != null && InReach(c.PlayerObject.transform.position, c.PlayerObject.transform))
                        {
                            c.PlayerObject.GetComponent<TowelCarrier>().StunOnServer(3);
                            decoy.DropOwnerOnServer(attackPlayer.Value);
                            firearm.DropOwnerOnServer(attackPlayer.Value);
                        }

                        SoundRpc(2, transform.position);
                        BeginRetreat();
                    }

                    break;
                case MimicPhase.Retreating:
                    Vector3 retreat = retreatStep >= 0 ? trail[retreatStep] : retreatStep == -1 ? nodes[State.Node].mouth.position : nodes[State.Node].transform.position;
                    Move(retreat, retreatStep >= 0 ? floorSpeed : retreatSpeed);
                    if (Vector3.Distance(transform.position, retreat) < .015f)
                    {
                        retreatStep--;
                        if (retreatStep < -2)
                        {
                            ignoreHeatUntil = Now + retreatHeatCooldown;
                            acquiredTarget = false;
                            nextDecision = Now + 2;
                            Phase(MimicPhase.Dormant);
                        }
                    }

                    break;
            }
        }

        void Outside()
        {
            if (Now >= exposureEnds)
            {
                BeginRetreat();
                return;
            }

            attackPlayer = null;
            attackGuest = null;
            Vector3? target = null;
            Transform targetRoot = null;
            // Exposed creature can hurt anyone it can physically reach, even an evacuating guest.
            foreach (var guest in shift.Reception.Guests)
                if (!IsCarrier(guest) && guest.Alive && Vector3.Distance(guest.transform.position, emergenceEnd) < 3.7f)
                {
                    if (target == null || Vector3.Distance(transform.position, guest.transform.position) < Vector3.Distance(transform.position, target.Value))
                    {
                        target = guest.transform.position;
                        targetRoot = guest.transform;
                        attackGuest = guest;
                    }
                }

            foreach (var c in NetworkManager.ConnectedClientsList)
                if (c.PlayerObject != null && !c.PlayerObject.GetComponent<TowelCarrier>().Incapacitated && Vector3.Distance(c.PlayerObject.transform.position, emergenceEnd) < 3.7f && (target == null || Vector3.Distance(transform.position, c.PlayerObject.transform.position) < Vector3.Distance(transform.position, target.Value)))
                {
                    target = c.PlayerObject.transform.position;
                    targetRoot = c.PlayerObject.transform;
                    attackGuest = null;
                    attackPlayer = c.ClientId;
                }

            if (target == null)
                return;
            if (InReach(target.Value, targetRoot))
            {
                if (Now >= attackAllowed)
                {
                    Phase(MimicPhase.Attacking);
                    SoundRpc(1, transform.position);
                }

                return;
            }

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(transform.position, target.Value, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete || path.corners.Length < 2)
                return;
            Vector3 next = path.corners[1];
            next.y = transform.position.y;
            Vector3 delta = Vector3.ClampMagnitude(next - transform.position, floorSpeed * Time.deltaTime);
            bool obstacle = Physics.SphereCastAll(transform.position + Vector3.up * .4f, .23f, delta.normalized, delta.magnitude + .04f, ~0, QueryTriggerInteraction.Ignore).Any(h => !h.collider.transform.IsChildOf(transform) && !h.collider.transform.IsChildOf(targetRoot) && h.collider.GetComponentInParent<GuestLifecycle>() == null);
            if (!obstacle)
            {
                Move(transform.position + delta, floorSpeed);
                if (Vector3.Distance(trail[trail.Count - 1], transform.position) > .25f)
                    trail.Add(transform.position);
            }
        }

        bool InReach(Vector3 p, Transform root) => Vector3.Distance(transform.position, p) < 1.3f && ClearLine(transform.position + Vector3.up * .55f, p + Vector3.up * .65f, root);
        bool ClearLine(Vector3 a, Vector3 b, Transform target)
        {
            return !Physics.RaycastAll(a, (b - a).normalized, Vector3.Distance(a, b), ~0, QueryTriggerInteraction.Ignore).Any(h => !h.collider.transform.IsChildOf(transform) && !h.collider.transform.IsChildOf(target));
        }

        void Move(Vector3 target, float speed)
        {
            Vector3 d = target - transform.position;
            var flat = new Vector3(d.x, 0, d.z);
            if (flat.sqrMagnitude > .0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), Time.deltaTime * 240);
            transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
        }

        void BeginRetreat()
        {
            retreatStep = trail.Count - 1;
            Phase(MimicPhase.Retreating);
            SoundRpc(0, transform.position);
        }

        public bool DamageOnServer(int amount)
        {
            if (!IsServer || !IsSpawned || !State.Active || State.Phase == MimicPhase.Dead || amount <= 0)
                return false;
            var v = State;
            v.Health = Mathf.Max(0, v.Health - amount);
            v.Hits++;
            if (v.Health == 0)
            {
                v.Phase = MimicPhase.Dead;
                v.Since = Now;
                shift.RecordMimicKill();
                complaint.Value = new FixedString128Bytes("Threat eliminated. Continue motel operations.");
            }

            state.Value = v;
            SoundRpc((byte)(v.Health == 0 ? 4 : 3), transform.position);
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void EvacuateRpc(NetworkObjectReference reference, uint visit, uint revision, RpcParams rpc = default)
        {
            if (!IsServer || !shift.IsRunning || revision != State.Revision || !reference.TryGet(out var obj, NetworkManager))
                return;
            var guest = obj.GetComponent<GuestLifecycle>();
            if (guest == null || guest.State.Visit != visit || !ServiceInteractionValidation.CanReach(NetworkManager, rpc.Receive.SenderClientId, guest.Body))
                return;
            if (guest.EvacuateOnServer())
                SoundRpc(1, guest.transform.position);
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        void SoundRpc(byte kind, Vector3 at)
        {
            var clips = new[]{scratch, warning, attack, pain, death, nodes.First(n => n.looseFan).rattle};
            if (clips[kind] == null)
                return;
            // Positional one-shot remains at the observed event location while the creature moves.
            var go = new GameObject("VentSound");
            go.transform.position = at;
            var a = go.AddComponent<AudioSource>();
            a.clip = clips[kind];
            a.spatialBlend = 1;
            a.rolloffMode = AudioRolloffMode.Linear;
            a.minDistance = 1;
            a.maxDistance = kind == 2 ? 14 : 9;
            a.volume = kind == 0 ? .5f : .8f;
            a.Play();
            Destroy(go, clips[kind].length + .1f);
        }

        public bool ConfigurationValid() => nodes != null && nodes.Length == 10 && nodes.Select(n => n.index).Distinct().Count() == nodes.Length && nodes.All(n => n.neighbours.Length > 0 && n.neighbours.All(x => x.neighbours.Contains(n))) && nodes.Count(n => n.room != null) == 4 && shift != null && decoy != null && firearm != null && creature != null && body != null && scratch != null && warning != null;
    }
}
