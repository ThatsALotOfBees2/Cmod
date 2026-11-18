using UnityEngine;

namespace Cmod
{
    /// <summary>Ray and sphere traces for weapons that skip the shooter's own colliders and trigger volumes.</summary>
    public static class WeaponTrace
    {
        static readonly RaycastHit[] s_hits = new RaycastHit[64];

        /// <summary>Nearest hit along the ray, ignoring anything under `ignoreRoot` (the player).</summary>
        public static bool Ray(Vector3 origin, Vector3 dir, float maxDistance, Transform ignoreRoot, out RaycastHit hit)
        {
            int n = Physics.RaycastNonAlloc(origin, dir, s_hits, maxDistance, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            return Nearest(n, ignoreRoot, out hit);
        }

        /// <summary>Nearest hit of a sphere swept along the ray (forgiving melee traces). For hits that start
        /// inside the sphere the point is fixed up to the collider's closest point.</summary>
        public static bool Sphere(Vector3 origin, Vector3 dir, float radius, float maxDistance, Transform ignoreRoot, out RaycastHit hit)
        {
            int n = Physics.SphereCastNonAlloc(origin, radius, dir, s_hits, maxDistance, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (!Nearest(n, ignoreRoot, out hit)) return false;
            if (hit.distance <= 0f || hit.point == Vector3.zero)
            {
                var p = hit.collider.ClosestPoint(origin);
                hit.point = p;
                var nrm = origin - p;
                hit.normal = nrm.sqrMagnitude > 1e-6f ? nrm.normalized : -dir;
            }
            return true;
        }

        static bool Nearest(int n, Transform ignoreRoot, out RaycastHit hit)
        {
            hit = default;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = s_hits[i];
                if (h.collider == null) continue;
                if (ignoreRoot != null && h.collider.transform.IsChildOf(ignoreRoot)) continue;
                if (h.distance < best) { best = h.distance; hit = h; found = true; }
            }
            return found;
        }
    }
}
