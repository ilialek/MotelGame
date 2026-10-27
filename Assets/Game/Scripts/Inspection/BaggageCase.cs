using System;
using UnityEngine;

namespace MotelGame.Inspection
{
    public enum BagShell : byte
    {
        Suitcase,
        Duffel,
        Backpack,
        HardCase
    }

    public enum PackingResponse : byte
    {
        Fixed,
        Sliding,
        Liquid,
        Elastic,
        Counterbalanced
    }

    public enum AnalyzerTest : byte
    {
        Weight,
        Metal,
        Balance,
        TiltLeft,
        TiltRight,
        Level,
        Pulse
    }

    public enum BaggageQuestion : byte
    {
        Contents,
        Weight,
        Metal,
        Packing,
        Response
    }

    [Serializable]
    public struct BaggagePart
    {
        public string description; // Authoring only; never sent as inspection output.
        public float kilograms;
        [Range(0, 1)]
        public float metalFraction;
        public float offsetCm, travelCm;
        public PackingResponse response;
        public float delaySeconds, decaySeconds;
    }

    [CreateAssetMenu(menuName = "Motel/Baggage Case")]
    public sealed class BaggageCase : ScriptableObject
    {
        public BagShell shell;
        public Color color = new Color(.32f, .26f, .21f);
        public bool containsMimic;
        public BaggagePart[] contents;
        [TextArea]
        public string contentsAnswer, weightAnswer, metalAnswer, packingAnswer, responseAnswer;
        [TextArea]
        public string authorReasoning;
        public float ShellMass => shell switch
        {
            BagShell.Suitcase => 2.4f,
            BagShell.Duffel => .8f,
            BagShell.Backpack => .7f,
            _ => 1.8f
        };
        public float Mass
        {
            get
            {
                float total = ShellMass;
                foreach (var part in contents)
                    total += part.kilograms;
                return total;
            }
        }

        public float Metal
        {
            get
            {
                float total = ShellMass * (shell == BagShell.HardCase ? .18f : .06f);
                foreach (var part in contents)
                    total += part.kilograms * part.metalFraction;
                return Mathf.Clamp(total / Mass * 100, 0, 100);
            }
        }

        // A bounded authored physical model, shared by every shell and every case.
        // Tilt changes arrangement. Returning level does not undo a sliding object's last displacement.
        // The actuator gives a repeatable right-directed pulse; repetitions after settling are identical.
        public float Balance(int tilt, int lastTilt, bool pulsed)
        {
            float moment = 0;
            foreach (var part in contents)
            {
                float offset = part.offsetCm;
                switch (part.response)
                {
                    case PackingResponse.Sliding:
                        offset += part.travelCm * (lastTilt != 0 ? lastTilt : pulsed ? .4f : 0);
                        break;
                    case PackingResponse.Liquid:
                        offset += part.travelCm * tilt;
                        break;
                    case PackingResponse.Elastic:
                        offset += part.travelCm * tilt * (pulsed ? .15f : .55f);
                        break;
                    case PackingResponse.Counterbalanced:
                        offset -= part.travelCm * (tilt != 0 ? tilt * .65f : pulsed ? .3f : 0);
                        break;
                }

                moment += part.kilograms * offset;
            }

            return moment / Mass;
        }

        public float Delay
        {
            get
            {
                float v = 0;
                foreach (var p in contents)
                    if (p.travelCm > 0)
                        v = Mathf.Max(v, p.delaySeconds);
                return v;
            }
        }

        public float Decay
        {
            get
            {
                float v = .15f;
                foreach (var p in contents)
                    v = Mathf.Max(v, p.decaySeconds);
                return v;
            }
        }

        public float ResponseAmplitude
        {
            get
            {
                float v = 0;
                foreach (var p in contents)
                    v += p.kilograms * p.travelCm;
                return v / Mass;
            }
        }

        public string Answer(BaggageQuestion q) => q switch
        {
            BaggageQuestion.Contents => contentsAnswer,
            BaggageQuestion.Weight => weightAnswer,
            BaggageQuestion.Metal => metalAnswer,
            BaggageQuestion.Packing => packingAnswer,
            _ => responseAnswer
        };
        public bool Valid(out string error)
        {
            error = "";
            if (contents == null || contents.Length == 0 || Mass <= ShellMass)
                error = "Empty physical model";
            else
                foreach (var p in contents)
                    if (!float.IsFinite(p.kilograms) || !float.IsFinite(p.metalFraction) || !float.IsFinite(p.offsetCm) || !float.IsFinite(p.travelCm) || !float.IsFinite(p.decaySeconds) || !float.IsFinite(p.delaySeconds) || p.kilograms <= 0 || p.metalFraction < 0 || p.metalFraction > 1 || p.travelCm < 0 || p.decaySeconds < 0 || p.delaySeconds < 0)
                        error = "Invalid physical part";
            foreach (BaggageQuestion q in Enum.GetValues(typeof(BaggageQuestion)))
                if (string.IsNullOrWhiteSpace(Answer(q)) || System.Text.Encoding.UTF8.GetByteCount(Answer(q)) > 480)
                    error = "Missing/oversized answer";
            if (string.IsNullOrWhiteSpace(authorReasoning))
                error = "Missing defensible evidence path";
            return error.Length == 0;
        }
    }
}
