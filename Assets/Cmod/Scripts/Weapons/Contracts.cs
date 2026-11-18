using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// Base for every weapon (physgun, toolgun, guns). It follows GMod's SWEP table on purpose,
    /// because the Lua SWEP API will map onto it later.
    /// Weapons are plain C# objects owned by a CmodPlayer, not MonoBehaviours.
    /// </summary>
    public abstract class WeaponBase
    {
        public abstract string Id { get; }          // "weapon_physgun"
        public abstract string PrintName { get; }   // "Physics Gun"
        public virtual string Category => "Other";
        public virtual int Slot => 0;               // 0-based slot; number key = Slot + 1
        public virtual bool Spawnable => true;      // show in the spawn menu's Weapons tab

        public virtual int ClipSize => -1;          // -1 = no ammo
        public int Clip;

        public CmodPlayer Owner { get; internal set; }

        public virtual void Initialize() { }         // once, when given to the player
        public virtual void Deploy() { }             // switched to
        public virtual void Holster() { }            // switched away (also called before removal)
        public virtual void Think(float dt) { }      // every frame while active (input is not blocked by UI when called)
        public virtual void FixedThink(float dt) { } // every physics step while active
        public virtual void PrimaryAttack() { }      // left mouse pressed this frame
        public virtual void PrimaryHeld(bool held) { } // left mouse held state, every frame
        public virtual void SecondaryAttack() { }    // right mouse pressed this frame
        public virtual void Reload() { }             // R pressed
        /// <summary>Draws weapon-specific HUD with IMGUI (called from OnGUI while active).</summary>
        public virtual void DrawHUD() { }
        /// <summary>True to stop mouse-look this frame (physgun rotating with E). The weapon reads CmodInput.LookDelta itself.</summary>
        public virtual bool BlocksMouseLook => false;
        /// <summary>True while the weapon uses the scroll wheel itself (physgun distance while holding),
        /// so the player must not switch weapons with the wheel.</summary>
        public virtual bool UsesScrollWheel => false;
    }

    public static class WeaponRegistry
    {
        static readonly Dictionary<string, Func<WeaponBase>> s_factories = new Dictionary<string, Func<WeaponBase>>(StringComparer.OrdinalIgnoreCase);
        static readonly List<string> s_order = new List<string>();
        static readonly Dictionary<string, WeaponBase> s_proto = new Dictionary<string, WeaponBase>(StringComparer.OrdinalIgnoreCase);

        public static event Action Changed;

        public static void Register(Func<WeaponBase> factory)
        {
            var proto = factory();
            if (!s_factories.ContainsKey(proto.Id)) s_order.Add(proto.Id);
            s_factories[proto.Id] = factory;
            s_proto[proto.Id] = proto;
            Changed?.Invoke();
        }

        public static WeaponBase Create(string id) => s_factories.TryGetValue(id, out var f) ? f() : null;

        /// <summary>One instance per weapon type, only for reading display info.</summary>
        public static IEnumerable<WeaponBase> Prototypes { get { foreach (var id in s_order) yield return s_proto[id]; } }
    }

    /// <summary>
    /// A toolgun mode, like GMod's TOOL table. The toolgun calls these on the active mode.
    /// </summary>
    public abstract class ToolMode
    {
        public abstract string Id { get; }          // "remover"
        public abstract string Name { get; }        // "Remover"
        public virtual string Category => "Construction";
        public abstract string Description { get; } // one line, shown on the HUD
        public virtual string Instructions => "";   // e.g. "LMB: remove   RMB: remove constrained"

        /// <summary>Return true if the tool did something (plays the toolgun effect).</summary>
        public virtual bool LeftClick(RaycastHit hit, CmodEntity ent) => false;
        public virtual bool RightClick(RaycastHit hit, CmodEntity ent) => false;
        public virtual bool Reload(RaycastHit hit, CmodEntity ent) => false;
        public virtual void Think(CmodPlayer player) { }
        public virtual void Holster() { }

        /// <summary>Draws this tool's settings with the Win98 widgets inside `area`
        /// (the spawn menu's tool panel). Return the height used.</summary>
        public virtual float DrawSettings(Rect area) => 0f;
    }

    public static class ToolRegistry
    {
        static readonly List<ToolMode> s_tools = new List<ToolMode>();
        public static event Action Changed;
        public static IReadOnlyList<ToolMode> All => s_tools;

        public static void Register(ToolMode tool)
        {
            s_tools.RemoveAll(t => t.Id == tool.Id);
            s_tools.Add(tool);
            Changed?.Invoke();
        }

        public static ToolMode Get(string id) => s_tools.Find(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

        static ToolMode s_active;
        /// <summary>The selected toolgun mode. Setting it runs Hooks.ToolChanged.</summary>
        public static ToolMode Active
        {
            get => s_active ?? (s_tools.Count > 0 ? s_tools[0] : null);
            set
            {
                if (s_active == value) return;
                s_active?.Holster();
                s_active = value;
                Hooks.Run(Hooks.ToolChanged, value);
            }
        }
    }
}
