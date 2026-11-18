using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cmod
{
    public enum ConstraintType { Weld, Rope }

    /// <summary>
    /// One joint between two entities (or an entity and the world). The record keeps every
    /// parameter needed to rebuild it, so undo can recreate removed constraints.
    /// </summary>
    public sealed class Constraint
    {
        public ConstraintType Type;
        /// <summary>The entity the Joint component lives on (always has a Body).</summary>
        public CmodEntity A;
        /// <summary>The other entity, or null for the world.</summary>
        public CmodEntity B;
        /// <summary>True when B was an entity at creation (so B == null later means it was removed).</summary>
        public bool BIsEntity;
        /// <summary>Attachment point in A's local space.</summary>
        public Vector3 LocalA;
        /// <summary>Attachment point in B's local space, or a world position when B is the world.</summary>
        public Vector3 LocalB;

        // Parameters
        public float BreakForce;          // weld: 0 = unbreakable
        public float Length;              // rope: max length in meters
        public float Width = 0.02f;       // rope: visual width
        public Color RopeColor = new Color(0.45f, 0.33f, 0.2f);

        // Runtime objects
        public Joint Joint;
        public LineRenderer Line;

        public Vector3 WorldA => A != null ? A.transform.TransformPoint(LocalA) : Vector3.zero;
        public Vector3 WorldB => B != null ? B.transform.TransformPoint(LocalB) : LocalB;

        /// <summary>True while both ends and the joint still exist.</summary>
        public bool IsValid => A != null && Joint != null && (!BIsEntity || B != null);

        public bool Involves(CmodEntity e) => e != null && (A == e || (BIsEntity && B == e));
        public CmodEntity Other(CmodEntity e) => A == e ? (BIsEntity ? B : null) : A;
    }

    /// <summary>
    /// Bookkeeping for every tool-made constraint (welds, ropes, balloon ropes). Remover, Weld and the
    /// physgun's R use it to find what is connected to an entity. Dead constraints (an end removed, a
    /// joint broken) are pruned automatically, so a joint never silently re-attaches to the world.
    /// </summary>
    public static class Constraints
    {
        static readonly List<Constraint> s_all = new List<Constraint>();
        static ConstraintRunner s_runner;
        static bool s_hooked;
        static Material s_ropeMat;

        public static IReadOnlyList<Constraint> All => s_all;

        /// <summary>Raised after a constraint is created or removed.</summary>
        public static event Action Changed;

        static void EnsureRunner()
        {
            if (s_runner == null)
            {
                var go = new GameObject("CmodConstraints");
                UnityEngine.Object.DontDestroyOnLoad(go);
                s_runner = go.AddComponent<ConstraintRunner>();
            }
            if (!s_hooked)
            {
                s_hooked = true;
                // Drop constraints the moment an entity goes away (before Unity re-anchors its joints to the world).
                Hooks.Add(Hooks.EntityRemoved, "cmod.constraints", args =>
                {
                    if (args != null && args.Length > 0 && args[0] is CmodEntity e) RemoveAll(e);
                });
            }
        }

        // =====================================================================================
        // Creation
        // =====================================================================================

        /// <summary>Welds a to b (b null = the world) with a FixedJoint, keeping their current relative pose.
        /// Returns null when nothing can be welded (no rigidbody on either side, or a == b).</summary>
        public static Constraint Weld(CmodEntity a, CmodEntity b, float breakForce = 0f)
        {
            if (a == b) return null;
            if (!HasBody(a)) { var t = a; a = b; b = t; }      // the joint must live on a body
            if (!HasBody(a)) return null;
            if (FindBetween(a, b, ConstraintType.Weld) != null) return null; // already welded

            var c = new Constraint
            {
                Type = ConstraintType.Weld, A = a, B = b, BIsEntity = b != null,
                LocalA = Vector3.zero, LocalB = b != null ? Vector3.zero : a.transform.position,
                BreakForce = Mathf.Max(0f, breakForce),
            };
            Build(c);
            return c;
        }

        /// <summary>Ropes a point on a to a point on b (b null = the world point). The rope can't stretch past length.</summary>
        public static Constraint Rope(CmodEntity a, Vector3 worldA, CmodEntity b, Vector3 worldB, float length, float width, Color color)
        {
            if (a == b && a != null) return null;
            if (!HasBody(a))
            {
                var te = a; a = b; b = te;
                var tp = worldA; worldA = worldB; worldB = tp;
            }
            if (!HasBody(a)) return null;

            var c = new Constraint
            {
                Type = ConstraintType.Rope, A = a, B = b, BIsEntity = b != null,
                LocalA = a.transform.InverseTransformPoint(worldA),
                LocalB = b != null ? b.transform.InverseTransformPoint(worldB) : worldB,
                Length = Mathf.Max(0.05f, length), Width = Mathf.Max(0.002f, width), RopeColor = color,
            };
            Build(c);
            return c;
        }

        /// <summary>Recreates a removed constraint (undo). `map` translates old entity references to their
        /// restored copies (or returns the same entity if it still exists). Returns null if an end is gone.</summary>
        public static Constraint Recreate(Constraint old, Func<CmodEntity, CmodEntity> map = null)
        {
            if (old == null) return null;
            CmodEntity Map(CmodEntity e) => map != null ? map(e) : e;
            var a = Map(old.A);
            var b = old.BIsEntity ? Map(old.B) : null;
            if (a == null || (old.BIsEntity && b == null) || !HasBody(a)) return null;
            var c = new Constraint
            {
                Type = old.Type, A = a, B = b, BIsEntity = old.BIsEntity,
                LocalA = old.LocalA, LocalB = old.LocalB,
                BreakForce = old.BreakForce, Length = old.Length, Width = old.Width, RopeColor = old.RopeColor,
            };
            if (c.Type == ConstraintType.Weld && b == null) c.LocalB = a.transform.position;
            Build(c);
            return c;
        }

        static void Build(Constraint c)
        {
            EnsureRunner();
            var go = c.A.gameObject;
            var otherBody = c.B != null ? c.B.Body : null;

            if (c.Type == ConstraintType.Weld)
            {
                var j = go.AddComponent<FixedJoint>();
                j.connectedBody = otherBody;   // null = welded to the world
                j.enableCollision = false;
                if (c.BreakForce > 0f) { j.breakForce = c.BreakForce; j.breakTorque = c.BreakForce; }
                c.Joint = j;
            }
            else
            {
                var j = go.AddComponent<ConfigurableJoint>();
                j.autoConfigureConnectedAnchor = false;
                j.anchor = c.LocalA;
                // For a static (body-less) entity, anchor to its point in world space.
                if (otherBody != null)
                {
                    j.connectedBody = otherBody;
                    j.connectedAnchor = otherBody.transform.InverseTransformPoint(c.WorldB);
                }
                else
                {
                    j.connectedBody = null;
                    j.connectedAnchor = c.WorldB;
                }
                j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Limited;
                j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Free;
                j.linearLimit = new SoftJointLimit { limit = c.Length, contactDistance = 0.02f };
                j.enableCollision = true;
                j.projectionMode = JointProjectionMode.PositionAndRotation;
                j.projectionDistance = 0.2f;
                c.Joint = j;

                c.Line = WeaponFx.MakeLine("Rope", s_runner.transform, c.RopeColor, c.Width, RopeSegments + 1);
                if (s_ropeMat == null) s_ropeMat = CmodMaterials.Unlit(Color.white);
                var old = c.Line.sharedMaterial;
                c.Line.sharedMaterial = s_ropeMat;
                if (old != null && old != s_ropeMat) UnityEngine.Object.Destroy(old);
                UpdateRope(c);
            }

            if (c.A.Body != null) c.A.Body.WakeUp();
            if (otherBody != null) otherBody.WakeUp();
            s_all.Add(c);
            RaiseChanged();
        }

        // =====================================================================================
        // Queries
        // =====================================================================================

        /// <summary>Constraints involving the entity (optionally of one type). Allocates a new list.</summary>
        public static List<Constraint> For(CmodEntity e, ConstraintType? type = null)
        {
            var list = new List<Constraint>();
            if (e == null) return list;
            foreach (var c in s_all)
                if (c.Involves(e) && (type == null || c.Type == type.Value)) list.Add(c);
            return list;
        }

        public static Constraint FindBetween(CmodEntity a, CmodEntity b, ConstraintType type)
        {
            foreach (var c in s_all)
            {
                if (c.Type != type) continue;
                if ((c.A == a && c.B == b && c.BIsEntity == (b != null)) || (b != null && c.A == b && c.B == a)) return c;
            }
            return null;
        }

        /// <summary>Every entity connected to root through constraints (optionally only one type), root included.</summary>
        public static HashSet<CmodEntity> GetConnected(CmodEntity root, ConstraintType? type = null)
        {
            var set = new HashSet<CmodEntity>();
            if (root == null) return set;
            var stack = new Stack<CmodEntity>();
            set.Add(root);
            stack.Push(root);
            while (stack.Count > 0)
            {
                var e = stack.Pop();
                foreach (var c in s_all)
                {
                    if (!c.IsValid || !c.Involves(e) || (type != null && c.Type != type.Value)) continue;
                    var o = c.Other(e);
                    if (o != null && set.Add(o)) stack.Push(o);
                }
            }
            return set;
        }

        // =====================================================================================
        // Removal
        // =====================================================================================

        public static void Remove(Constraint c)
        {
            if (c == null) return;
            if (c.Joint != null) UnityEngine.Object.Destroy(c.Joint);
            if (c.Line != null) UnityEngine.Object.Destroy(c.Line.gameObject);
            c.Joint = null;
            c.Line = null;
            if (c.A != null && c.A.Body != null && !c.A.Body.isKinematic) c.A.Body.WakeUp();
            if (c.B != null && c.B.Body != null && !c.B.Body.isKinematic) c.B.Body.WakeUp();
            if (s_all.Remove(c)) RaiseChanged();
        }

        /// <summary>Removes the entity's constraints (optionally one type). Returns the removed records (for undo).</summary>
        public static List<Constraint> RemoveAll(CmodEntity e, ConstraintType? type = null)
        {
            var list = For(e, type);
            foreach (var c in list) Remove(c);
            return list;
        }

        /// <summary>Removes every constraint (map cleanup).</summary>
        public static void Clear()
        {
            for (int i = s_all.Count - 1; i >= 0; i--) Remove(s_all[i]);
        }

        static void RaiseChanged()
        {
            try { Changed?.Invoke(); } catch (Exception ex) { CmodLog.Error($"[constraints] {ex.Message}"); }
        }

        static bool HasBody(CmodEntity e) => e != null && e.Body != null;

        // =====================================================================================
        // Per-frame upkeep (called by the runner)
        // =====================================================================================

        internal const int RopeSegments = 14;
        static readonly Vector3[] s_ropePts = new Vector3[RopeSegments + 1];

        internal static void Tick()
        {
            for (int i = s_all.Count - 1; i >= 0; i--)
            {
                var c = s_all[i];
                if (!c.IsValid) { Remove(c); continue; }   // an end was destroyed or the weld broke
                if (c.Line != null) UpdateRope(c);
            }
        }

        /// <summary>Draws the rope as a sagging curve: a parabola whose depth matches the slack.</summary>
        static void UpdateRope(Constraint c)
        {
            var a = c.WorldA;
            var b = c.WorldB;
            float d = Vector3.Distance(a, b);
            float slack = Mathf.Max(0f, c.Length - d);
            // Parabola arc length ≈ d + 8h²/(3d)  =>  h = sqrt(3 d slack / 8)
            float sag = d > 0.001f ? Mathf.Min(Mathf.Sqrt(3f * d * slack / 8f), c.Length * 0.5f) : c.Length * 0.5f;
            var down = Physics.gravity.sqrMagnitude > 0.0001f ? Physics.gravity.normalized : Vector3.down;
            for (int i = 0; i <= RopeSegments; i++)
            {
                float t = i / (float)RopeSegments;
                s_ropePts[i] = Vector3.Lerp(a, b, t) + down * (sag * 4f * t * (1f - t));
            }
            c.Line.SetPositions(s_ropePts);
            c.Line.startWidth = c.Line.endWidth = c.Width;
            c.Line.startColor = c.Line.endColor = c.RopeColor;
        }
    }

    /// <summary>Prunes dead constraints and redraws ropes every frame.</summary>
    internal sealed class ConstraintRunner : MonoBehaviour
    {
        void LateUpdate()
        {
            try { Constraints.Tick(); }
            catch (Exception ex) { CmodLog.Error($"[constraints] {ex.GetType().Name}: {ex.Message}"); }
        }
    }
}
