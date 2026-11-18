using System;
using System.Collections.Generic;

namespace Cmod
{
    /// <summary>
    /// GMod-style event bus. Later the Lua runtime maps hook.Add / hook.Run straight onto this.
    /// Hook functions return null to let other hooks run, or a value to stop and return it.
    /// </summary>
    public static class Hooks
    {
        // Well-known hook names used by the base game. Mods can invent their own.
        public const string Think = "Think";                         // ()            every frame
        public const string PlayerSpawned = "PlayerSpawned";         // (CmodPlayer)
        public const string EntitySpawned = "EntitySpawned";         // (CmodEntity)
        public const string EntityRemoved = "EntityRemoved";         // (CmodEntity)
        public const string WeaponSwitched = "WeaponSwitched";       // (CmodPlayer, WeaponBase newWeapon)
        public const string ToolChanged = "ToolChanged";             // (ToolMode)
        public const string CanSpawnProp = "CanSpawnProp";           // (PropDefinition) -> return false to block
        public const string CanTool = "CanTool";                     // (ToolMode, RaycastHit) -> return false to block
        public const string PhysgunPickup = "PhysgunPickup";         // (CmodEntity) -> return false to block
        public const string ChatMessage = "ChatMessage";             // (string sender, string text)

        readonly struct Entry
        {
            public readonly string Id;
            public readonly Func<object[], object> Fn;
            public Entry(string id, Func<object[], object> fn) { Id = id; Fn = fn; }
        }

        static readonly Dictionary<string, List<Entry>> s_hooks = new Dictionary<string, List<Entry>>();

        public static void Add(string hookName, string id, Func<object[], object> fn)
        {
            if (fn == null) throw new ArgumentNullException(nameof(fn));
            if (!s_hooks.TryGetValue(hookName, out var list)) s_hooks[hookName] = list = new List<Entry>();
            list.RemoveAll(e => e.Id == id);
            list.Add(new Entry(id, fn));
        }

        /// <summary>Convenience overload for hooks that never return a value.</summary>
        public static void Add(string hookName, string id, Action<object[]> fn)
        {
            if (fn == null) throw new ArgumentNullException(nameof(fn));
            Add(hookName, id, args => { fn(args); return null; });
        }

        public static void Remove(string hookName, string id)
        {
            if (s_hooks.TryGetValue(hookName, out var list)) list.RemoveAll(e => e.Id == id);
        }

        /// <summary>Runs every hook for the event. Returns the first non-null result, or null.
        /// A hook that throws is logged and skipped, so one bad mod can't break the game.</summary>
        public static object Run(string hookName, params object[] args)
        {
            if (!s_hooks.TryGetValue(hookName, out var list) || list.Count == 0) return null;
            var snapshot = list.ToArray();
            foreach (var e in snapshot)
            {
                try
                {
                    var r = e.Fn(args);
                    if (r != null) return r;
                }
                catch (Exception ex)
                {
                    CmodLog.Error($"[hook {hookName}/{e.Id}] {ex.GetType().Name}: {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>True unless some hook explicitly returned false.</summary>
        public static bool RunAllowed(string hookName, params object[] args)
        {
            return !(Run(hookName, args) is bool b && b == false);
        }

        public static void ClearAll() => s_hooks.Clear();
    }
}
