using System.Collections.Generic;
using UnityEngine;
#if UNITY_6000_0_OR_NEWER
using PhysMat = UnityEngine.PhysicsMaterial;
using PhysMatCombine = UnityEngine.PhysicsMaterialCombine;
#else
using PhysMat = UnityEngine.PhysicMaterial;
using PhysMatCombine = UnityEngine.PhysicMaterialCombine;
#endif

namespace Cmod
{
    /// <summary>
    /// Covers the Unity 2021/2022 vs Unity 6 physics API renames (velocity -> linearVelocity,
    /// drag -> linearDamping, PhysicMaterial -> PhysicsMaterial). ALWAYS use these helpers
    /// instead of touching those members directly, so the code compiles on both.
    /// </summary>
    public static class PhysicsCompat
    {
        public static Vector3 GetVelocity(Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        public static void SetVelocity(Rigidbody rb, Vector3 v)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
        }

        public static float GetDrag(Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearDamping;
#else
            return rb.drag;
#endif
        }

        public static void SetDrag(Rigidbody rb, float drag)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearDamping = drag;
#else
            rb.drag = drag;
#endif
        }

        public static float GetAngularDrag(Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.angularDamping;
#else
            return rb.angularDrag;
#endif
        }

        public static void SetAngularDrag(Rigidbody rb, float drag)
        {
#if UNITY_6000_0_OR_NEWER
            rb.angularDamping = drag;
#else
            rb.angularDrag = drag;
#endif
        }

        static readonly Dictionary<PhysicMaterialPreset, PhysMat> s_mats = new Dictionary<PhysicMaterialPreset, PhysMat>();

        /// <summary>Gives the collider a shared, cached physics material for the preset.</summary>
        public static void ApplyMaterial(Collider c, PhysicMaterialPreset preset)
        {
            if (c == null) return;
            if (preset == PhysicMaterialPreset.Default) { c.sharedMaterial = null; return; }
            if (!s_mats.TryGetValue(preset, out var m))
            {
                m = new PhysMat(preset.ToString());
                switch (preset)
                {
                    case PhysicMaterialPreset.Wood:   Set(m, 0.5f, 0.6f, 0.15f, PhysMatCombine.Average); break;
                    case PhysicMaterialPreset.Metal:  Set(m, 0.35f, 0.4f, 0.05f, PhysMatCombine.Average); break;
                    case PhysicMaterialPreset.Rubber: Set(m, 0.9f, 1.0f, 0.85f, PhysMatCombine.Maximum); break;
                    case PhysicMaterialPreset.Ice:    Set(m, 0.02f, 0.02f, 0.05f, PhysMatCombine.Minimum); break;
                }
                s_mats[preset] = m;
            }
            c.sharedMaterial = m;
        }

        static void Set(PhysMat m, float dyn, float stat, float bounce, PhysMatCombine bounceCombine)
        {
            m.dynamicFriction = dyn;
            m.staticFriction = stat;
            m.bounciness = bounce;
            m.bounceCombine = bounceCombine;
            m.frictionCombine = bounceCombine == PhysMatCombine.Minimum ? PhysMatCombine.Minimum : PhysMatCombine.Average;
        }

        /// <summary>FindObjectOfType replacement that isn't obsolete on Unity 6.</summary>
        public static T FindAny<T>() where T : Object
        {
#if UNITY_6000_0_OR_NEWER
            return Object.FindAnyObjectByType<T>();
#else
            return Object.FindObjectOfType<T>();
#endif
        }

        public static T[] FindAll<T>() where T : Object
        {
#if UNITY_6000_0_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<T>();
#endif
        }
    }
}
