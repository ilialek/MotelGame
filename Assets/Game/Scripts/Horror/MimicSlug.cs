using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace MotelGame.Horror
{
    [DefaultExecutionOrder(300)]
    public sealed class MimicSlug : NetworkBehaviour
    {
        [SerializeField] MimicHunt hunt;
        [SerializeField] SlugBody body;
        [SerializeField] CapsuleCollider hitbox;
        NetworkList<SlugKnot> trail, remains;
        readonly NetworkVariable<uint> identity = new();
        readonly SlugPath path = new();
        readonly List<GameObject> corpses = new();
        Vector3 lastForward = Vector3.forward;
        double nextSample;
        float swallowed, recoil = 1;
        uint hits;
        public SlugBody Body => body;
        public SlugPath Path => path;
        public float PresentedDistance { get; private set; }
        public float SwallowedDistance => swallowed;
        public Vector3 TailPosition => body.Centers == null ? transform.position : body.Centers[body.Centers.Length - 1];
        public int TrailCount => trail.Count;
        public int PresentedCorpses => corpses.Count;
        public bool Ready => trail.Count >= 2 && identity.Value == hunt.State.Revision;
        double Now => NetworkManager.ServerTime.Time;
        void Awake() { trail = new NetworkList<SlugKnot>(); remains = new NetworkList<SlugKnot>(); }
        public override void OnNetworkDespawn() { ClearCorpses(); body.GetComponent<Renderer>().enabled = false; }

        public void BeginEmergence(Vector3 anchor, Vector3 bend)
        {
            if (!IsServer) return;
            Seed(anchor, (bend - anchor).normalized);
        }
        void Seed(Vector3 at, Vector3 forward)
        {
            path.Reset(at, forward, Now, body.bodyLength + .3f);
            trail.Clear(); trail.Add(path[0]); trail.Add(path[1]);
            identity.Value = hunt.State.Revision;
            lastForward = forward; swallowed = 0; nextSample = Now;
        }
        void Record(Vector3 at)
        {
            if (trail.Count < 2) { Seed(at, transform.forward); return; }
            var last = trail[trail.Count - 1];
            float delta = Vector3.Distance(last.Position, at);
            // Only debug teleport fixtures should reach this guard; normal AI never jumps while visible.
            if (delta > .8f) { Seed(at, transform.forward); return; }
            if (delta < .0001f) return;
            Vector3 tangent = (at - last.Position).normalized;
            var point = new SlugKnot { Position = at, Right = SlugPath.Transport(last.Right, lastForward, tangent),
                Distance = last.Distance + delta, Time = Now };
            // Keep the final segment's moving endpoint; retain corners as soon as direction changes.
            var previous = trail[trail.Count - 2];
            bool corner = Vector3.Dot(lastForward, tangent) < .98f;
            if (last.Distance - previous.Distance >= .07f || corner) trail.Add(point);
            else trail[trail.Count - 1] = point;
            if (trail.Count > SlugPath.Capacity) trail.RemoveAt(0);
            lastForward = tangent;
        }
        // The root stops at the existing opaque pocket. Feed the remaining body behind its bend,
        // then let the existing HiddenTransit timer start; no geometry or navigation graph is added.
        public bool FinishEntering(VentNode node, float deltaTime)
        {
            if (!IsServer) return false;
            // The end wall is .5m beyond the authored anchor. Clear it with the tail too,
            // including presentation interpolation; the opening may be watched from below.
            float required = body.bodyLength + .75f;
            swallowed = Mathf.Min(required, swallowed + .9f * deltaTime);
            Record(node.hiddenAnchor.position + (node.hiddenAnchor.position - node.hiddenBend.position).normalized * swallowed);
            return swallowed >= required;
        }
        public void CaptureRemains()
        {
            if (!IsServer || trail.Count < 2) return;
            // Fixed-size snapshots are sent only on death, and survive late join and the next Mimic.
            for (int i = 0; i < SlugPath.Capacity; i++) remains.Add(trail[Mathf.Clamp(i - (SlugPath.Capacity - trail.Count), 0, trail.Count - 1)]);
        }
        void PresentRemains()
        {
            if (hunt.Remains.Count < corpses.Count) ClearCorpses();
            while (corpses.Count < hunt.Remains.Count && remains.Count >= (corpses.Count + 1) * SlugPath.Capacity)
            {
                var corpsePath = new SlugPath();
                corpsePath.Copy(remains, corpses.Count * SlugPath.Capacity, SlugPath.Capacity);
                var g = Instantiate(body.gameObject); g.name = "EliminatedSlugMimic";
                g.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); g.transform.localScale = Vector3.one;
                g.SetActive(true); g.GetComponent<SlugBody>().Render(corpsePath, corpsePath.End, .7f);
                var renderer = g.GetComponent<Renderer>(); renderer.enabled = true;
                var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", new Color(.12f, .05f, .09f));
                renderer.SetPropertyBlock(block); corpses.Add(g);
            }
        }
        void ClearCorpses() { foreach (var g in corpses) if (g != null) Destroy(g); corpses.Clear(); }
        void LateUpdate()
        {
            if (!IsSpawned) return;
            if (IsServer)
            {
                if (hunt.Remains.Count == 0 && remains.Count > 0) remains.Clear();
                if (hunt.State.Phase != MimicPhase.Submerging) swallowed = 0;
                if (hunt.Visible && swallowed == 0 && Now >= nextSample)
                { Record(transform.position); nextSample = Now + .05; }
            }
            PresentRemains();
            if (!hunt.Visible || !Ready) { body.GetComponent<Renderer>().enabled = false; return; }
            path.Copy(trail);
            // A short, shared presentation delay interpolates the authoritative trail on every peer.
            // It does not move the AI or postpone gameplay decisions.
            PresentedDistance = Mathf.Max(path[0].Distance + body.bodyLength, path.DistanceAt(Now - .1));
            if (hits != hunt.State.Hits) { hits = hunt.State.Hits; recoil = .85f; }
            recoil = Mathf.MoveTowards(recoil, 1, Time.deltaTime * 1.2f);
            body.Render(path, PresentedDistance, recoil); body.GetComponent<Renderer>().enabled = true;
            var head = path.EvaluateBody(path.End).Position;
            var chest = path.EvaluateBody(path.End - .6f).Position;
            head -= Vector3.up * (.19f * Mathf.Clamp01((-head.y - 1.8f) / .85f));
            chest -= Vector3.up * (.19f * Mathf.Clamp01((-chest.y - 1.8f) / .85f));
            var axis = head - chest;
            hitbox.transform.SetPositionAndRotation((head + chest) * .5f, Quaternion.LookRotation(axis.sqrMagnitude > .001f ? axis : lastForward));
            hitbox.height = axis.magnitude + hitbox.radius * 2;
        }
    }
}
