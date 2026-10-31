#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Text;
using System.Collections;
using UnityEngine;
using MotelGame.Horror;
namespace MotelGame.Networking.Tests
{
    public sealed partial class MotelLayoutSmokeTest
    {
        MimicSlug observedSlug;
        int slugFrames, slugVerticalFrames, slugTransitChecks;
        float slugMaximumGap;
        MimicPhase previousSlugPhase;
        bool SlugValidation => OnlineIdentity.Argument("-motel-slug-validation") == "yes";
        void ObserveSlug()
        {
            if (!SlugValidation || hunt == null || !hunt.IsSpawned) return;
            if (observedSlug == null) observedSlug = hunt.GetComponent<MimicSlug>();
            if (observedSlug == null) { if (slugFrames == 0) errors.Add("Missing Slug integration"); slugFrames++; return; }
            var body = observedSlug.Body;
            if (Host && previousSlugPhase == MimicPhase.Submerging && hunt.State.Phase == MimicPhase.HiddenTransit)
            {
                var node = hunt.Nodes[hunt.State.Node];
                var pocketAxis = (node.hiddenAnchor.position - node.hiddenBend.position).normalized;
                if (body.Centers == null || body.Centers[body.Centers.Length - 1].y < -1.65f ||
                    Vector3.Dot(body.Centers[body.Centers.Length - 1] - node.hiddenAnchor.position, pocketAxis) < .55f)
                    errors.Add("Hidden transit started while Slug tail was still outside the opaque elbow");
                slugTransitChecks++;
            }
            previousSlugPhase = hunt.State.Phase;
            if (!hunt.Visible || !observedSlug.Ready || body.Centers == null) return;
            slugFrames++;
            if (hunt.State.Phase == MimicPhase.Emerging || hunt.State.Phase == MimicPhase.Submerging) slugVerticalFrames++;
            if (!body.GetComponent<Renderer>().enabled || body.BodyMesh.vertexCount != 398)
                errors.Add("Visible Mimic lacks continuous Slug mesh");
            for (int i = 0; i < body.Centers.Length; i++)
            {
                var v = body.Centers[i];
                if (!float.IsFinite(v.x) || !float.IsFinite(v.y) || !float.IsFinite(v.z)) errors.Add("Nonfinite Slug centerline");
                if (i > 0) slugMaximumGap = Mathf.Max(slugMaximumGap, Vector3.Distance(v, body.Centers[i - 1]));
            }
            if (slugMaximumGap > .16f && !errors.Contains("Disconnected Slug mesh"))
            {
                errors.Add("Disconnected Slug mesh");
                var detail = new StringBuilder(hunt.State.Phase + " distance=" + observedSlug.PresentedDistance + " gap=" + slugMaximumGap + "\n");
                for (int i = 0; i < observedSlug.Path.Count; i++) detail.AppendLine(observedSlug.Path[i].Distance + " " + observedSlug.Path[i].Position);
                File.WriteAllText(System.IO.Path.Combine(dir, role + "-slug-failure.txt"), detail.ToString());
            }
            if (observedSlug.TrailCount > SlugPath.Capacity) errors.Add("Unbounded Slug trail");
        }
        IEnumerator SlugLateVisible()
        {
            yield return Wait(() => hunt.Visible && hunt.GetComponent<MimicSlug>().Ready, "late visible Slug trail");
            yield return new WaitForSecondsRealtime(2);
            Need(slugFrames > 10 && observedSlug.Path.Count > 2 && observedSlug.Body.GetComponent<Renderer>().enabled,
                "Late client failed to reconstruct moving Slug");
            checks.Add("Late join reconstructed complete moving Slug from authoritative trail before the first death.");
            Mark("slug-late-visible");
        }
        void ReportSlug()
        {
            if (!SlugValidation) return;
            checks.Add("Slug observed frames=" + slugFrames + " vertical=" + slugVerticalFrames +
                " completedTailIngress=" + slugTransitChecks + " maximumRingGap=" + slugMaximumGap);
            if (mode == "underground-hunt" && (slugFrames < 10 || (Host && (slugVerticalFrames < 10 || slugTransitChecks < 1))))
                errors.Add("Insufficient Slug motion/hidden-entry coverage");
            File.WriteAllText(Path.Combine(dir, role + "-slug.txt"), checks[checks.Count - 1]);
        }
    }
}
#endif
