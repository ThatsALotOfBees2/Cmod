using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// Spawns and tracks every CmodEntity (the implementation half of the contract in Entities/Contracts.cs).
    ///
    /// Responsibilities:
    ///  * assigns unique entity ids and keeps the list of live entities;
    ///  * builds props from a PropDefinition (primitive mesh, a collider that works on a dynamic Rigidbody,
    ///    physics material preset, colored lit material, Rigidbody with the definition's mass);
    ///  * runs the CanSpawnProp / EntitySpawned / EntityRemoved hooks and pushes undo entries;
    ///  * places props on the surface under the crosshair, lifted by their collider bounds.
    ///
    /// Besides the contract it exposes a few helpers (Count, Find, Bounds) other systems may use.
    /// </summary>
    public static partial class EntityManager
    {
        /// <summary>Maximum trace distance for crosshair spawning (contract: "up to 1000 units").</summary>
        public const float CrosshairSpawnRange = 1000f;
        /// <summary>Where a prop goes when the crosshair hits nothing: this far in front of the eye.</summary>
        public const float MidairSpawnDistance = 6f;
        /// <summary>Gap left between a spawned prop and the surface it is placed on.</summary>
        const float SurfaceGap = 0.02f;

        static int s_nextId = 1;
        static readonly List<CmodEntity> s_live = new List<CmodEntity>();

        // All() hands out a snapshot so callers may Remove() entities while iterating it.
        static CmodEntity[] s_snapshot = Array.Empty<CmodEntity>();
        static bool s_snapshotDirty;

        static ConVar s_maxProps;

        /// <summary>Number of live entities.</summary>
        public static int Count { get { Prune(); return s_live.Count; } }

        /// <summary>"sbox_maxprops": the most entities that may exist at once (0 = unlimited).</summary>
        static ConVar MaxPropsVar => s_maxProps ??= ConCommands.CreateVar("sbox_maxprops", "400",
            "Maximum number of spawned props (0 = no limit)", 0f, 10000f, archive: true);

        // ==========================================================================================
        // Contract
        // ==========================================================================================

        public static partial IReadOnlyList<CmodEntity> All()
        {
            Prune();
            if (s_snapshotDirty)
            {
                s_snapshot = s_live.ToArray();
                s_snapshotDirty = false;
            }
            return s_snapshot;
        }

        public static partial CmodEntity SpawnProp(PropDefinition def, Vector3 position, Quaternion rotation, bool addUndo)
        {
            if (def == null) { CmodLog.Warn("SpawnProp: no prop definition"); return null; }
            if (!Hooks.RunAllowed(Hooks.CanSpawnProp, def)) return null;

            // Entity limit. Tool entities (balloons, lamps) are spawned through here too, so they count as well.
            int limit = MaxPropsVar.Int;
            if (limit > 0 && Count >= limit)
            {
                CmodLog.Warn($"Prop limit reached ({limit}). Raise sbox_maxprops or clean up.");
                return null;
            }

            GameObject go = null;
            try
            {
                go = BuildPropObject(def, position, rotation, out var ent);

                // Custom construction (furniture children, lights, scripts...). A failing callback
                // must not leave a half-registered entity behind, so it is caught and logged.
                if (def.OnSpawned != null)
                {
                    try { def.OnSpawned(ent); }
                    catch (Exception ex) { CmodLog.Error($"[prop {def.Id}] OnSpawned failed: {ex.GetType().Name}: {ex.Message}"); }
                }
                FinishBody(ent, def);

                Register(ent);
                Hooks.Run(Hooks.EntitySpawned, ent);

                if (addUndo)
                {
                    var name = string.IsNullOrEmpty(def.DisplayName) ? def.Id : def.DisplayName;
                    UndoManager.Push("Prop (" + name + ")", () => Remove(ent));
                }
                return ent;
            }
            catch (Exception ex)
            {
                CmodLog.Error($"SpawnProp '{def.Id}' failed: {ex.GetType().Name}: {ex.Message}");
                if (go != null)
                {
                    var half = go.GetComponent<CmodEntity>();
                    if (half != null)
                    {
                        // It may have been registered before the failure; take it out quietly.
                        half.m_removed = true;
                        if (s_live.Remove(half)) s_snapshotDirty = true;
                    }
                    UnityEngine.Object.Destroy(go);
                }
                return null;
            }
        }

        public static partial CmodEntity SpawnPropAtCrosshair(PropDefinition def)
        {
            if (def == null) return null;

            // Work out the aim ray, the surface point and the facing yaw.
            var player = CmodPlayer.Local;
            Ray ray;
            float yaw;
            bool hitSomething = false;
            RaycastHit hit = default;
            if (player != null)
            {
                ray = player.AimRay;
                yaw = player.Cam != null ? player.Cam.transform.eulerAngles.y : player.transform.eulerAngles.y;
                hitSomething = player.TraceAim(out hit, CrosshairSpawnRange);
            }
            else
            {
                var cam = Camera.main;
                ray = cam != null ? new Ray(cam.transform.position, cam.transform.forward) : new Ray(Vector3.up * 2f, Vector3.forward);
                yaw = cam != null ? cam.transform.eulerAngles.y : 0f;
                hitSomething = Physics.Raycast(ray, out hit, CrosshairSpawnRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            }

            // Props stand upright and face the player (their +Z points back at the camera), like GMod.
            var rotation = Quaternion.Euler(0f, yaw + 180f, 0f);
            var point = hitSomething ? hit.point : ray.GetPoint(MidairSpawnDistance);

            var ent = SpawnProp(def, point, rotation, true);
            if (ent == null) return null;

            if (hitSomething) PlaceOnSurface(ent, hit.point, hit.normal);
            else CenterOn(ent, point);

            // Make sure the rigidbody starts where the transform now is (no interpolation smear on frame 1).
            if (ent.Body != null)
            {
                ent.Body.position = ent.transform.position;
                ent.Body.rotation = ent.transform.rotation;
            }
            Physics.SyncTransforms();
            return ent;
        }

        public static partial void Remove(CmodEntity ent)
        {
            // Unity's == treats destroyed objects as null; ReferenceEquals catches a real null.
            if (ReferenceEquals(ent, null)) return;
            if (ent.m_removed) return;
            ent.m_removed = true;

            if (ent != null)
            {
                // Listeners (constraints, tools, mods) still see a live object while the hook runs.
                Hooks.Run(Hooks.EntityRemoved, ent);
                UnityEngine.Object.Destroy(ent.gameObject);
            }

            if (s_live.Remove(ent)) s_snapshotDirty = true;
        }

        public static partial void Cleanup()
        {
            var list = s_live.ToArray();
            int n = 0;
            foreach (var e in list)
            {
                if (ReferenceEquals(e, null) || e.m_removed) continue;
                Remove(e);
                n++;
            }
            s_live.Clear();
            s_snapshotDirty = true;
            CmodLog.Info(n == 1 ? "Cleaned up 1 entity." : $"Cleaned up {n} entities.");
        }

        public static partial CmodEntity FromCollider(Collider c)
        {
            if (c == null) return null;
            CmodEntity ent = null;
            var rb = c.attachedRigidbody;
            if (rb != null) ent = rb.GetComponent<CmodEntity>();
            if (ent == null) ent = c.GetComponentInParent<CmodEntity>();
            if (ent == null || ent.m_removed) return null;
            return ent;
        }

        // ==========================================================================================
        // Extra helpers (not part of the contract, but public for other systems)
        // ==========================================================================================

        /// <summary>The live entity with this id, or null.</summary>
        public static CmodEntity Find(int id)
        {
            for (int i = 0; i < s_live.Count; i++)
            {
                var e = s_live[i];
                if (e != null && !e.m_removed && e.Id == id) return e;
            }
            return null;
        }

        /// <summary>World-space bounds of every collider on the entity (falls back to renderers, then the position).</summary>
        public static Bounds GetBounds(CmodEntity ent)
        {
            if (ent == null) return default;
            Physics.SyncTransforms(); // newly created/moved colliders report stale bounds otherwise
            bool any = false;
            var b = new Bounds(ent.transform.position, Vector3.zero);
            foreach (var c in ent.GetComponentsInChildren<Collider>())
            {
                if (!c.enabled || c.isTrigger) continue;
                var cb = c.bounds;
                if (cb.size == Vector3.zero) continue;
                if (!any) { b = cb; any = true; } else b.Encapsulate(cb);
            }
            if (any) return b;
            foreach (var r in ent.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        /// <summary>Moves the entity so its bounds sit on the surface at `point` (offset along `normal`), not sunk into it.</summary>
        public static void PlaceOnSurface(CmodEntity ent, Vector3 point, Vector3 normal)
        {
            if (ent == null) return;
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            normal.Normalize();
            var b = GetBounds(ent);
            // Support distance of the axis-aligned box along the normal: how far it reaches from its centre.
            var e = b.extents;
            float reach = Mathf.Abs(normal.x) * e.x + Mathf.Abs(normal.y) * e.y + Mathf.Abs(normal.z) * e.z;
            var targetCenter = point + normal * (reach + SurfaceGap);
            ent.transform.position += targetCenter - b.center;
        }

        /// <summary>Moves the entity so the centre of its bounds is at `point`.</summary>
        static void CenterOn(CmodEntity ent, Vector3 point)
        {
            var b = GetBounds(ent);
            ent.transform.position += point - b.center;
        }

        // ==========================================================================================
        // Internals
        // ==========================================================================================

        /// <summary>Creates the GameObject, mesh, collider, material, Rigidbody and CmodEntity for a definition.</summary>
        static GameObject BuildPropObject(PropDefinition def, Vector3 position, Quaternion rotation, out CmodEntity ent)
        {
            var go = GameObject.CreatePrimitive(def.Shape);
            go.name = string.IsNullOrEmpty(def.Id) ? "prop" : def.Id;
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = SafeScale(def.Scale);

            FixCollider(go, def.Shape);

            // Look: one material instance per prop so recoloring one never affects another.
            var rend = go.GetComponent<MeshRenderer>();
            if (rend != null)
            {
                var mat = CmodMaterials.Lit(def.Color);
                mat.name = "prop_" + go.name;
                ApplySurfaceLook(mat, def.Material);
                if (def.Color.a < 0.999f) CmodMaterials.MakeTransparent(mat);
                rend.sharedMaterial = mat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                rend.receiveShadows = true;
            }

            // Physics: Rigidbody before OnSpawned so the callback can tweak it.
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.01f, def.Mass);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            // Speculative CCD stays valid when the prop is frozen (made kinematic) later.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.maxAngularVelocity = 30f;
            PhysicsCompat.SetAngularDrag(rb, 0.08f);

            ent = go.AddComponent<CmodEntity>();
            if (rend != null) ent.OwnMaterial(rend.sharedMaterial);
            ent.ClassName = "prop_physics";
            ent.PropId = def.Id;
            ent.OwnerName = CmodPlayer.Local != null ? CmodPlayer.Local.PlayerName : "player";
            ent.Body = rb;
            ent.Renderer = rend;
            ent.Id = s_nextId++;
            go.name = $"{go.name} [{ent.Id}]";
            return go;
        }

        /// <summary>Runs after OnSpawned: physics materials on every collider, and mass properties that include child colliders.</summary>
        static void FinishBody(CmodEntity ent, PropDefinition def)
        {
            if (ent == null) return;
            foreach (var c in ent.GetComponentsInChildren<Collider>(true))
            {
                // OnSpawned may have chosen materials for specific parts; only fill in the rest.
                if (c.sharedMaterial == null) PhysicsCompat.ApplyMaterial(c, def.Material);
            }
            if (ent.Body != null)
            {
                // Keep the definition's mass even if OnSpawned replaced colliders, and recompute the
                // centre of mass / inertia from the final collider set (furniture is built from children).
                ent.Body.mass = Mathf.Max(0.01f, def.Mass);
                ent.Body.ResetCenterOfMass();
                ent.Body.ResetInertiaTensor();
                ent.Body.WakeUp();
            }
            // OnSpawned may have removed the root renderer (compound props); pick the first child one.
            if (ent.Renderer == null) ent.Renderer = ent.GetComponentInChildren<MeshRenderer>();
        }

        /// <summary>
        /// The primitive's default collider doesn't always work on a dynamic Rigidbody:
        /// cylinders get a capsule (rounded ends, rolls wrong) and planes/quads get a concave MeshCollider
        /// (not allowed on non-kinematic bodies). Replace those with something that behaves.
        /// </summary>
        static void FixCollider(GameObject go, PrimitiveType shape)
        {
            switch (shape)
            {
                case PrimitiveType.Cylinder:
                {
                    var cap = go.GetComponent<CapsuleCollider>();
                    if (cap != null) UnityEngine.Object.DestroyImmediate(cap);
                    var mf = go.GetComponent<MeshFilter>();
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf != null ? mf.sharedMesh : null;
                    mc.convex = true;
                    break;
                }
                case PrimitiveType.Plane:
                case PrimitiveType.Quad:
                {
                    var mc = go.GetComponent<MeshCollider>();
                    if (mc != null) UnityEngine.Object.DestroyImmediate(mc);
                    var box = go.AddComponent<BoxCollider>();
                    // Plane lies in XZ (10x10 units), Quad in XY (1x1): give them a thin but real thickness.
                    box.size = shape == PrimitiveType.Plane ? new Vector3(10f, 0.02f, 10f) : new Vector3(1f, 1f, 0.02f);
                    break;
                }
            }
        }

        /// <summary>Small visual differences per physics preset so metal looks metallic, ice glossy, etc.</summary>
        static void ApplySurfaceLook(Material m, PhysicMaterialPreset preset)
        {
            float metallic = 0f, gloss = 0.25f;
            switch (preset)
            {
                case PhysicMaterialPreset.Wood: gloss = 0.15f; break;
                case PhysicMaterialPreset.Metal: metallic = 0.65f; gloss = 0.55f; break;
                case PhysicMaterialPreset.Rubber: gloss = 0.35f; break;
                case PhysicMaterialPreset.Ice: gloss = 0.9f; break;
            }
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
        }

        static Vector3 SafeScale(Vector3 s)
        {
            const float min = 0.005f;
            return new Vector3(Mathf.Max(min, Mathf.Abs(s.x)), Mathf.Max(min, Mathf.Abs(s.y)), Mathf.Max(min, Mathf.Abs(s.z)));
        }

        static void Register(CmodEntity ent)
        {
            s_live.Add(ent);
            s_snapshotDirty = true;
        }

        /// <summary>Drops entries whose GameObject was destroyed without going through Remove (scene unload, Destroy by a mod).</summary>
        static void Prune()
        {
            for (int i = s_live.Count - 1; i >= 0; i--)
            {
                var e = s_live[i];
                if (e == null || e.m_removed) { s_live.RemoveAt(i); s_snapshotDirty = true; }
            }
        }

        /// <summary>Called by CmodEntity.OnDestroy when an entity is destroyed directly (not via Remove).</summary>
        internal static void NotifyDestroyed(CmodEntity ent)
        {
            if (ReferenceEquals(ent, null) || ent.m_removed) return;
            ent.m_removed = true;
            // The object is going away already; let listeners clean up their bookkeeping.
            try { Hooks.Run(Hooks.EntityRemoved, ent); } catch (Exception ex) { CmodLog.Error($"[EntityRemoved] {ex.Message}"); }
            if (s_live.Remove(ent)) s_snapshotDirty = true;
        }
    }

    /// <summary>Entity-manager bookkeeping on the entity itself.</summary>
    public partial class CmodEntity
    {
        /// <summary>Set once the entity has been removed (it may still exist until the end of the frame).</summary>
        internal bool m_removed;

        /// <summary>True after EntityManager.Remove (or after the object was destroyed).</summary>
        public bool IsRemoved => m_removed || this == null;

        /// <summary>Runtime materials created for this entity (destroyed with it).</summary>
        List<Material> m_ownedMaterials;

        /// <summary>Registers a material created for this entity so it is destroyed with the entity.</summary>
        internal void OwnMaterial(Material m)
        {
            if (m == null) return;
            if (m_ownedMaterials == null) m_ownedMaterials = new List<Material>(2);
            if (!m_ownedMaterials.Contains(m)) m_ownedMaterials.Add(m);
        }

        void OnDestroy()
        {
            // Covers entities destroyed by something other than EntityManager.Remove,
            // so the live list and constraint bookkeeping never hold dead entries.
            EntityManager.NotifyDestroyed(this);
            ReleaseMaterials();
        }

        /// <summary>
        /// Runtime-created materials are not freed with the GameObject (and the game never calls
        /// Resources.UnloadUnusedAssets), so destroy the ones this entity made: those registered with
        /// OwnMaterial, plus per-renderer instances made by Renderer.material (e.g. the Color setter).
        /// Shared materials from other systems are left alone.
        /// </summary>
        void ReleaseMaterials()
        {
            try
            {
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    foreach (var m in r.sharedMaterials)
                        if (m != null && m.name.EndsWith("(Instance)", StringComparison.Ordinal)) OwnMaterial(m);
                }
            }
            catch (Exception) { /* hierarchy already torn down; owned list still applies */ }

            if (m_ownedMaterials == null) return;
            foreach (var m in m_ownedMaterials) if (m != null) Destroy(m);
            m_ownedMaterials = null;
        }
    }
}
