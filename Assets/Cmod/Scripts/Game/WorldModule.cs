using System;
using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// C-WORLD's boot entry points (see CmodGame): World_Build builds the map, Content_Register
    /// registers the default props. Also installs a small world watchdog and the sky fallback for cameras.
    /// </summary>
    public static partial class CmodModules
    {
        public static partial Pose World_Build()
        {
            var pose = WorldBuilder.Build();

            // Watchdog lives on the world root so it goes away with the map.
            if (WorldBuilder.Root != null) WorldBuilder.Root.AddComponent<WorldWatchdog>();

            // Cameras created after the map (the player's) must show this map's sky too.
            Hooks.Add(Hooks.PlayerSpawned, "cmod_world_sky", args =>
            {
                if (args != null && args.Length > 0 && args[0] is CmodPlayer p && p.Cam != null) WorldBuilder.ApplySky(p.Cam);
            });

            // Register the entity convars now so they show up in console autocomplete from the start.
            EntityManager.EnsureConVars();
            return pose;
        }

        public static partial void Content_Register()
        {
            DefaultProps.Register();
            CmodLog.Info($"Registered {PropRegistry.All.Count} props.");
        }
    }

    public static partial class EntityManager
    {
        /// <summary>Creates the entity-related convars (sbox_maxprops) if they don't exist yet.</summary>
        public static void EnsureConVars() { _ = MaxPropsVar; }
    }

    /// <summary>
    /// Removes props that fell out of the world (through the floor or off the map), checked twice a second.
    /// </summary>
    public sealed class WorldWatchdog : MonoBehaviour
    {
        /// <summary>Entities below this height are removed.</summary>
        public const float KillHeight = -100f;
        const float Interval = 0.5f;
        float _next;

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;
            try
            {
                var all = EntityManager.All(); // snapshot: safe to remove while iterating
                for (int i = 0; i < all.Count; i++)
                {
                    var e = all[i];
                    if (e == null || e.IsRemoved) continue;
                    if (e.transform.position.y < KillHeight)
                    {
                        CmodLog.Info($"{e.name} fell out of the world and was removed.");
                        EntityManager.Remove(e);
                    }
                }
            }
            catch (Exception ex)
            {
                CmodLog.Error($"[world watchdog] {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
