using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cmod
{
    /// <summary>Data for one spawnable prop. Mods will register these later.</summary>
    public sealed class PropDefinition
    {
        public string Id;                 // unique, e.g. "crate_small"
        public string DisplayName;        // shown in spawn menu
        public string Category = "Props"; // spawn-menu folder, e.g. "Construction", "Fun"
        public PrimitiveType Shape = PrimitiveType.Cube;
        public Vector3 Scale = Vector3.one;
        public float Mass = 10f;
        public Color Color = Color.white;
        public PhysicMaterialPreset Material = PhysicMaterialPreset.Default;
        /// <summary>Optional: called after the GameObject is built (add lights, custom scripts, ...).</summary>
        public Action<CmodEntity> OnSpawned;
    }

    public enum PhysicMaterialPreset { Default, Wood, Metal, Rubber, Ice }

    public static class PropRegistry
    {
        static readonly Dictionary<string, PropDefinition> s_props = new Dictionary<string, PropDefinition>(StringComparer.OrdinalIgnoreCase);
        static readonly List<PropDefinition> s_order = new List<PropDefinition>();

        public static event Action Changed;
        public static IReadOnlyList<PropDefinition> All => s_order;

        public static void Register(PropDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.Id)) throw new ArgumentException("PropDefinition needs an Id");
            if (s_props.TryGetValue(def.Id, out var old)) s_order.Remove(old);
            s_props[def.Id] = def;
            s_order.Add(def);
            Changed?.Invoke();
        }

        public static PropDefinition Get(string id) => id != null && s_props.TryGetValue(id, out var d) ? d : null;
    }

    /// <summary>
    /// Implemented in Entities/EntityManager.cs. Spawns and tracks every CmodEntity.
    /// Every networked/undoable thing in the world is a CmodEntity.
    /// </summary>
    public static partial class EntityManager
    {
        /// <summary>All live entities.</summary>
        public static partial IReadOnlyList<CmodEntity> All();

        /// <summary>Builds a prop from its definition (primitive mesh, collider, Rigidbody with Mass,
        /// colored material, CmodEntity with ClassName = "prop_physics", PropId = def.Id).
        /// Runs Hooks.CanSpawnProp first (returns null if blocked) and Hooks.EntitySpawned after.
        /// Pushes an undo entry when addUndo is true.</summary>
        public static partial CmodEntity SpawnProp(PropDefinition def, Vector3 position, Quaternion rotation, bool addUndo = true);

        /// <summary>Spawns the prop where the player is looking: on the surface under the crosshair
        /// (up to 1000 units away), raised so it doesn't sink into the surface, facing the player's yaw.</summary>
        public static partial CmodEntity SpawnPropAtCrosshair(PropDefinition def);

        /// <summary>Destroys the entity and runs Hooks.EntityRemoved. Safe to call twice.</summary>
        public static partial void Remove(CmodEntity ent);

        /// <summary>Removes every entity ("gmod_cleanup").</summary>
        public static partial void Cleanup();

        /// <summary>Returns the CmodEntity this collider belongs to, or null (for world geometry).</summary>
        public static partial CmodEntity FromCollider(Collider c);
    }

    /// <summary>Component on every spawned entity. EntityManager assigns Id.</summary>
    public partial class CmodEntity : MonoBehaviour
    {
        public int Id;
        public string ClassName = "prop_physics";
        public string PropId;
        public string OwnerName = "player";
        public Rigidbody Body;     // may be null for static entities
        public Renderer Renderer;  // main renderer, may be null

        /// <summary>Frozen = Rigidbody.isKinematic (physgun right-click freezes props in GMod).</summary>
        public bool Frozen
        {
            get => Body != null && Body.isKinematic;
            set { if (Body != null) { Body.isKinematic = value; if (!value) Body.WakeUp(); } }
        }

        public Color Color
        {
            get => Renderer != null ? Renderer.material.color : Color.white;
            set { if (Renderer != null) Renderer.material.color = value; }
        }
    }
}
