using System;
using Unity.Netcode;
using UnityEngine;

namespace MotelGame.Horror
{
    public struct SlugKnot : INetworkSerializable, IEquatable<SlugKnot>
    {
        public Vector3 Position, Right;
        public float Distance;
        public double Time;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Position); s.SerializeValue(ref Right);
            s.SerializeValue(ref Distance); s.SerializeValue(ref Time);
        }
        public bool Equals(SlugKnot k) => Position == k.Position && Right == k.Right && Distance == k.Distance && Time == k.Time;
    }

    // Bounded, arc-length trail. No laboratory route, no per-frame collections or mesh networking.
    public sealed class SlugPath
    {
        public const int Capacity = 96;
        readonly SlugKnot[] knots = new SlugKnot[Capacity];
        readonly int[] reversals = new int[Capacity];
        int reversalCount;
        public int Count { get; private set; }
        public SlugKnot this[int i] => knots[i];
        public float End => Count == 0 ? 0 : knots[Count - 1].Distance;
        public void Copy(NetworkList<SlugKnot> source, int offset = 0, int count = -1)
        {
            Count = Mathf.Min(Capacity, count < 0 ? source.Count : count);
            for (int i = 0; i < Count; i++) knots[i] = source[offset + i];
        }
        public void Reset(Vector3 head, Vector3 forward, double time, float length)
        {
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            if (right.sqrMagnitude < .5f) right = Vector3.right;
            Count = 2;
            knots[0] = new SlugKnot { Position = head - forward * length, Right = right, Time = time - length, Distance = 0 };
            knots[1] = new SlugKnot { Position = head, Right = right, Time = time, Distance = length };
        }
        public SlugKnot Evaluate(float distance)
        {
            if (Count == 0) return default;
            if (distance <= knots[0].Distance) return knots[0];
            if (distance >= End) return knots[Count - 1];
            int lo = 0, hi = Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (knots[mid].Distance < distance) lo = mid; else hi = mid;
            }
            var a = knots[lo]; var b = knots[hi];
            float t = Mathf.InverseLerp(a.Distance, b.Distance, distance);
            return new SlugKnot { Position = Vector3.Lerp(a.Position, b.Position, t),
                Right = Vector3.Slerp(a.Right, b.Right, t).normalized, Distance = distance,
                Time = a.Time + (b.Time - a.Time) * t };
        }
        public float DistanceAt(double time)
        {
            if (Count < 2) return End;
            for (int i = Count - 1; i > 0; i--)
                if (time >= knots[i - 1].Time)
                    return Mathf.Lerp(knots[i - 1].Distance, knots[i].Distance,
                        Mathf.Clamp01((float)((time - knots[i - 1].Time) / Math.Max(.00001, knots[i].Time - knots[i - 1].Time))));
            return knots[0].Distance;
        }
        public void PrepareBody()
        {
            reversalCount = 0;
            for (int i = 1; i + 1 < Count; i++)
            {
                var before = knots[i].Position - knots[i - 1].Position;
                var after = knots[i + 1].Position - knots[i].Position;
                if (before.sqrMagnitude > .000001f && after.sqrMagnitude > .000001f &&
                    Vector3.Dot(before.normalized, after.normalized) < -.85f) reversals[reversalCount++] = i;
            }
        }
        public SlugKnot EvaluateBody(float distance)
        {
            var sample = Evaluate(distance);
            var rawPosition = sample.Position;
            sample.Position = SmoothPosition(distance, .6f);
            Vector3 correction = Vector3.zero; float totalWeight = 0;
            // Blend overlapping turns continuously. Picking a nearest reversal would create a seam
            // when the AI briefly doubles back twice near a junction or an occupied escape route.
            for (int r = 0; r < reversalCount; r++)
            {
                int index = reversals[r]; var corner = knots[index];
                float s = distance - corner.Distance, a = Mathf.Abs(s);
                if (a >= 3) continue;
                var forward = (corner.Position - knots[index - 1].Position).normalized;
                var side = Vector3.Cross(Vector3.up, forward).normalized;
                if (side.sqrMagnitude < .5f) side = corner.Right;
                float radius = corner.Position.y < -2.2f ? .28f : .08f;
                float arc = radius * Mathf.PI * .5f;
                float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(arc, 3, a));
                Vector3 target = a < arc ?
                    rawPosition + forward * (a + radius * Mathf.Cos(s / radius) - radius) + side * (radius * Mathf.Sin(s / radius)) :
                    rawPosition + (forward * (arc - radius) + side * (Mathf.Sign(s) * radius)) * fade;
                float unfold = Mathf.SmoothStep(0, 1, Mathf.Clamp01((End - corner.Distance) / arc));
                float weight = (1 - Mathf.SmoothStep(0, 1, a / 3)) * unfold;
                correction += (target - sample.Position) * weight; totalWeight += weight;
            }
            sample.Position += correction / Mathf.Max(1, totalWeight);
            return sample;
        }
        Vector3 SmoothPosition(float distance, float halfWindow)
        {
            // Exact moving average of the piecewise-linear trail: a quadratic, tangent-continuous
            // corner, rather than several smaller sharp elbows from discrete point averaging.
            float cursor = distance - halfWindow, end = distance + halfWindow;
            Vector3 previous = Evaluate(cursor).Position, integral = Vector3.zero;
            for (int i = 0; i < Count; i++)
            {
                float next = knots[i].Distance;
                if (next <= cursor) continue;
                if (next >= end) break;
                integral += (previous + knots[i].Position) * (.5f * (next - cursor));
                cursor = next; previous = knots[i].Position;
            }
            integral += (previous + Evaluate(end).Position) * (.5f * (end - cursor));
            return integral / (2 * halfWindow);
        }
        public static Vector3 Transport(Vector3 right, Vector3 previous, Vector3 tangent)
        {
            right = Quaternion.FromToRotation(previous, tangent) * right;
            return Vector3.ProjectOnPlane(right, tangent).normalized;
        }
    }
}
