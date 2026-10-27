using UnityEngine;

namespace MotelGame.Horror
{
    public sealed class MimicPresentation : MonoBehaviour
    {
        public MimicHunt hunt;
        public Transform body, jaw;
        public Transform[] limbs;
        Quaternion[] rest;
        uint hits;
        float recoil;
        void Awake()
        {
            rest = new Quaternion[limbs.Length];
            for (int i = 0; i < rest.Length; i++)
                rest[i] = limbs[i].localRotation;
        }

        void OnEnable()
        {
            if (body == null) return;
            body.localPosition = Vector3.zero; body.localRotation = Quaternion.identity;
            body.localScale = hunt.State.Phase == MimicPhase.Infiltrating ? new Vector3(.4f, .28f, .4f) : new Vector3(.7f, .52f, .8f);
        }

        void Update()
        {
            if (!hunt.IsSpawned || !hunt.State.Active)
                return;
            var phase = hunt.State.Phase;
            bool duct = phase == MimicPhase.Infiltrating || phase == MimicPhase.Exploring || phase == MimicPhase.SelectingTarget || phase == MimicPhase.Dormant || phase == MimicPhase.MovingThroughDucts || phase == MimicPhase.ApproachingTarget || phase == MimicPhase.ApproachingBait || (phase == MimicPhase.Emerging || phase == MimicPhase.Retreating) && hunt.transform.position.y > .6f;
            if (hits != hunt.State.Hits)
            {
                hits = hunt.State.Hits;
                recoil = .3f;
            }

            recoil = Mathf.Max(0, recoil - Time.deltaTime);
            body.localScale = Vector3.Lerp(body.localScale, phase == MimicPhase.Infiltrating ? new Vector3(.4f,.28f,.4f) : duct ? new Vector3(.7f, .52f, .8f) : Vector3.one, Time.deltaTime * 4);
            bool dead = phase == MimicPhase.Dead;
            body.localPosition = Vector3.Lerp(body.localPosition, dead ? Vector3.up * .6f : Vector3.zero, Time.deltaTime * 5);
            body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(recoil * 70, 0, dead ? 86 : 0), Time.deltaTime * 5);
            jaw.localRotation = Quaternion.Euler(dead ? 35 : phase == MimicPhase.Attacking ? 65 : 18 + Mathf.Sin(Time.time * 3) * 6, 0, 0);
            for (int i = 0; i < limbs.Length; i++)
                limbs[i].localRotation = rest[i] * Quaternion.Euler(dead ? 0 : Mathf.Sin(Time.time * (duct ? 7 : 5) + i * 2) * 22, 0, phase == MimicPhase.Attacking ? -25 : 0);
        }
    }
}
