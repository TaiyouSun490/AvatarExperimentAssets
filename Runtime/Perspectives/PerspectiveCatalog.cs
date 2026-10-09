using System;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarExperiments.Perspectives
{
    [Serializable]
    public sealed class SpatialProfile
    {
        public string id;
        public string label;
        public Vector3 offset_m;
        public bool requires_overlap_calibration;
        public bool exploratory;
        public string evidence;
    }

    [Serializable]
    public sealed class PerspectiveStep
    {
        public string profile_id;
        public float minimum_dwell_seconds;
        public string instruction;
    }

    [Serializable]
    public sealed class ProtocolDefinition
    {
        public string id;
        public string label;
        public PerspectiveStep[] steps;
    }

    [Serializable]
    public sealed class PerspectiveCatalog
    {
        public string schema_version;
        public float fade_out_seconds = 0.3f;
        public float fade_in_seconds = 0.3f;
        public SpatialProfile[] profiles;
        public ProtocolDefinition[] protocols;

        public static PerspectiveCatalog Parse(string json)
        {
            var result = JsonUtility.FromJson<PerspectiveCatalog>(json);
            if (result == null) throw new ArgumentException("Catalog is missing.");
            result.Validate();
            return result;
        }

        public SpatialProfile Profile(string id)
        {
            foreach (var profile in profiles) if (profile.id == id) return profile;
            throw new ArgumentException("Unknown perspective: " + id);
        }

        public ProtocolDefinition Protocol(string id)
        {
            foreach (var protocol in protocols) if (protocol.id == id) return protocol;
            throw new ArgumentException("Unknown protocol: " + id);
        }

        public void Validate()
        {
            if (schema_version != "1.0.0") throw new ArgumentException("Unsupported catalog version.");
            if (!Finite(fade_out_seconds) || !Finite(fade_in_seconds) ||
                fade_out_seconds <= 0 || fade_in_seconds <= 0)
                throw new ArgumentException("Fade durations must be positive and finite.");
            if (profiles == null || profiles.Length == 0 || protocols == null || protocols.Length == 0)
                throw new ArgumentException("Profiles and protocols are required.");
            var ids = new HashSet<string>();
            foreach (var p in profiles)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.id) || !ids.Add(p.id) || !Finite(p.offset_m))
                    throw new ArgumentException("Invalid or duplicate spatial profile.");
            }
            if (Profile("first_person").offset_m != Vector3.zero)
                throw new ArgumentException("First person must have zero offset.");
            ids.Clear();
            foreach (var protocol in protocols)
            {
                if (protocol == null || string.IsNullOrWhiteSpace(protocol.id) || !ids.Add(protocol.id) ||
                    protocol.steps == null || protocol.steps.Length == 0)
                    throw new ArgumentException("Invalid or duplicate protocol.");
                foreach (var step in protocol.steps)
                {
                    if (step == null || !Finite(step.minimum_dwell_seconds) || step.minimum_dwell_seconds < 0)
                        throw new ArgumentException("Invalid protocol dwell.");
                    Profile(step.profile_id);
                }
            }
        }

        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
