using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Cmod.UI;
using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// C-APPS boot step: the base console commands and convars (GMod-style), plus the global keybind
    /// handler (<see cref="Keybinds"/>) for Q / ` / Z / F1.
    /// Runs before Player_Spawn and Windows_Init, so everything here must cope with the player and the
    /// app windows not existing yet (they are always looked up when a command runs).
    /// </summary>
    public static partial class CmodModules
    {
        public static partial void Commands_Register()
        {
            BaseCommands.Register();

            // The keybind handler lives on the game's root object (the one CmodGame is on).
            var root = CmodGame.Instance != null ? CmodGame.Instance.gameObject : null;
            if (root != null && root.GetComponent<Keybinds>() == null) root.AddComponent<Keybinds>();
        }
    }

    /// <summary>The base commands and convars. Everything is also reachable from the console (` key).</summary>
    public static class BaseCommands
    {
        /// <summary>Physics.gravity.y the game starts with (Unity's -9.81 feels floaty at GMod scale).</summary>
        public const float DefaultGravity = -15f;

        // Cached convars (created in Register).
        public static ConVar Gravity { get; private set; }
        public static ConVar Timescale { get; private set; }
        public static ConVar PlayerName { get; private set; }
        public static ConVar ShowFps { get; private set; }
        public static ConVar SpawnMenuToggle { get; private set; }
        public static ConVar Volume { get; private set; }
        public static ConVar God { get; private set; }

        /// <summary>Time.fixedDeltaTime at boot; scaled with host_timescale so slow motion stays smooth.</summary>
        static float s_baseFixedDelta = 0.02f;
        static bool s_registered;

        internal static void Register()
        {
            if (s_registered) return;
            s_registered = true;
            s_baseFixedDelta = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;

            RegisterConVars();
            RegisterCommands();
        }

        // =====================================================================================
        // Convars
        // =====================================================================================

        static void RegisterConVars()
        {
            Gravity = ConCommands.CreateVar("sv_gravity", DefaultGravity.ToString(CultureInfo.InvariantCulture),
                "World gravity (Physics.gravity.y, m/s^2). Default -15.", -200f, 200f);
            Gravity.Changed += v => ApplyGravity();
            ApplyGravity();

            Timescale = ConCommands.CreateVar("host_timescale", "1", "Game speed multiplier (0.05 - 5). 1 = normal.", 0.05f, 5f);
            Timescale.Changed += v => ApplyTimescale();
            ApplyTimescale();

            PlayerName = ConCommands.CreateVar("name", DefaultPlayerName(), "Your player name.", archive: true);
            PlayerName.Changed += v =>
            {
                if (CmodPlayer.Local != null && !string.IsNullOrEmpty(v.Value)) CmodPlayer.Local.PlayerName = v.Value;
            };

            ShowFps = ConCommands.CreateVar("cl_showfps", "0", "Show the FPS counter in the taskbar tray (0/1).", 0f, 1f, archive: true);
            SpawnMenuToggle = ConCommands.CreateVar("spawnmenu_toggle", "0",
                "0 = hold Q to show the spawn menu, 1 = Q toggles it.", 0f, 1f, archive: true);

            Volume = ConCommands.CreateVar("volume", "1", "Master volume (0 - 1).", 0f, 1f, archive: true);
            Volume.Changed += v => ApplyVolume();
            ApplyVolume();

            // Read by CmodPlayer.TakeDamage. Not archived: god mode is a per-session cheat.
            God = ConCommands.CreateVar("god", "0", "God mode: take no damage (0/1).", 0f, 1f);
        }

        static string DefaultPlayerName()
        {
            try
            {
                var user = Environment.UserName;
                if (!string.IsNullOrWhiteSpace(user)) return SanitizeName(user);
            }
            catch (Exception) { /* some platforms don't expose the user name */ }
            return "player";
        }

        /// <summary>Trims a player name and limits it to 32 printable characters.</summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "player";
            var sb = new StringBuilder(name.Length);
            foreach (var c in name.Trim())
            {
                if (char.IsControl(c) || c == '"' || c == ';') continue;
                sb.Append(c);
                if (sb.Length >= 32) break;
            }
            return sb.Length > 0 ? sb.ToString() : "player";
        }

        static void ApplyGravity()
        {
            var g = Physics.gravity;
            g.y = Gravity.Float;
            Physics.gravity = g;
        }

        static void ApplyTimescale()
        {
            float t = Mathf.Clamp(Timescale.Float, 0.05f, 5f);
            Time.timeScale = t;
            // Keep the physics step proportional, so slow motion doesn't stutter and fast motion stays stable.
            Time.fixedDeltaTime = s_baseFixedDelta * Mathf.Min(1f, t);
        }

        static void ApplyVolume()
        {
            AudioListener.volume = Mathf.Clamp01(Volume.Float);
        }

        // =====================================================================================
        // Commands
        // =====================================================================================

        static void RegisterCommands()
        {
            ConCommands.Register("help", "help [command]: lists every command and convar, or shows help for one.", CmdHelp);
            ConCommands.Register("find", "find <text>: lists commands and convars whose name or help contains the text.", CmdFind);
            ConCommands.Register("echo", "echo <text>: prints the text to the console.", args => CmodLog.Info(string.Join(" ", args)));
            ConCommands.Register("clear", "Clears the console.", _ =>
            {
                CmodLog.Clear();
                CmodApps.Console?.ClearView();
            });

            ConCommands.Register("noclip", "Toggles noclip (fly through walls). Also V.", _ =>
            {
                var p = RequirePlayer();
                if (p == null) return;
                if (p.IsDead) { CmodLog.Warn("You are dead."); return; }
                p.Noclip = !p.Noclip;
                CmodLog.Info(p.Noclip ? "noclip ON" : "noclip OFF");
            });

            ConCommands.Register("god", "god [0|1]: toggles god mode (no damage).", args =>
            {
                if (args.Length > 0) God.Value = args[0];
                else God.SetBool(!God.Bool);
                CmodLog.Info(God.Bool ? "godmode ON" : "godmode OFF");
            });

            ConCommands.Register("kill", "Kills you (you respawn after a moment).", _ =>
            {
                var p = RequirePlayer();
                if (p == null || p.IsDead) return;
                // TakeDamage ignores damage in god mode, so lift it for the suicide.
                bool wasGod = God.Bool;
                if (wasGod) God.SetBool(false);
                try { p.TakeDamage(p.Health + p.MaxHealth + 1000f, Vector3.zero); }
                finally { if (wasGod) God.SetBool(true); }
            });

            ConCommands.Register("respawn", "Respawns you at the spawn point with full health and the default weapons.", _ =>
            {
                var p = RequirePlayer();
                if (p != null) p.Respawn();
            });

            ConCommands.Register("give", "give <weapon_id>: gives you a weapon and selects it. No argument lists the weapons.", CmdGive);
            ConCommands.Register("spawn", "spawn <prop_id> [count]: spawns a prop where you look. No argument lists the props.", CmdSpawn);

            ConCommands.Register("undo", "Undoes your last action (spawn, weld, ...). Also Z.", _ => DoUndo());
            ConCommands.Register("cleanup", "Removes every spawned entity and clears the undo list.", _ => DoCleanup());

            ConCommands.Register("toggleconsole", "Shows or hides the console. Also `.", _ => CmodApps.Console?.Toggle());
            ConCommands.Register("spawnmenu", "Opens the spawn menu (hold Q).", _ => CmodApps.SpawnMenu?.Open());
            ConCommands.Register("options", "Opens the Options dialog.", _ => CmodApps.Options?.Open());
            ConCommands.Register("about", "Shows the About Cmod dialog.", _ => CmodApps.About?.Open());
            ConCommands.Register("controls", "Shows the controls (Help). Also F1.", _ => CmodApps.Help?.Open());
            ConCommands.Register("quit", "Quits the game immediately.", _ => CmodApps.Quit());
            ConCommands.Register("exit", "Quits the game immediately.", _ => CmodApps.Quit());
        }

        static CmodPlayer RequirePlayer()
        {
            var p = CmodPlayer.Local;
            if (p == null) CmodLog.Warn("There is no local player.");
            return p;
        }

        /// <summary>Pops the undo stack and reports what was undone (Z key, "undo", spawn menu button).</summary>
        public static void DoUndo()
        {
            var label = UndoManager.Undo();
            if (label == null) CmodLog.Info("Nothing to undo.");
            else CmodLog.Info("Undone " + (string.IsNullOrEmpty(label) ? "action" : label) + ".");
        }

        /// <summary>Removes every entity and clears undo ("cleanup", spawn menu button).</summary>
        public static void DoCleanup()
        {
            int n = EntityManager.All()?.Count ?? 0;
            EntityManager.Cleanup();
            UndoManager.Clear();
            CmodLog.Info($"Cleaned up {n} entit{(n == 1 ? "y" : "ies")}.");
        }

        static void CmdHelp(string[] args)
        {
            if (args.Length > 0)
            {
                var name = args[0];
                var cmd = ConCommands.GetCommand(name);
                var cv = ConCommands.GetVar(name);
                if (cmd == null && cv == null) { CmodLog.Warn($"No command or convar named '{name}'. Try: find {name}"); return; }
                if (cmd != null) CmodLog.Info($"{cmd.Name} - {cmd.Help}");
                if (cv != null) CmodLog.Info($"\"{cv.Name}\" = \"{cv.Value}\" (default \"{cv.Default}\"{(cv.Archive ? ", saved" : "")}) - {cv.Help}");
                return;
            }

            var sb = new StringBuilder();
            sb.Append("Commands:");
            foreach (var c in ConCommands.Commands) sb.Append("\n  ").Append(c.Name).Append(" - ").Append(c.Help);
            CmodLog.Info(sb.ToString());

            sb.Clear();
            sb.Append("Convars (type the name to see its value, \"name value\" to set it):");
            foreach (var v in ConCommands.Vars) sb.Append("\n  ").Append(v.Name).Append(" = ").Append(v.Value).Append(" - ").Append(v.Help);
            CmodLog.Info(sb.ToString());
        }

        static void CmdFind(string[] args)
        {
            if (args.Length == 0) { CmodLog.Info("Usage: find <text>"); return; }
            var q = string.Join(" ", args);
            bool Match(string name, string help) =>
                name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (help ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

            var sb = new StringBuilder();
            int n = 0;
            foreach (var c in ConCommands.Commands)
                if (Match(c.Name, c.Help)) { sb.Append("\n  ").Append(c.Name).Append(" - ").Append(c.Help); n++; }
            foreach (var v in ConCommands.Vars)
                if (Match(v.Name, v.Help)) { sb.Append("\n  ").Append(v.Name).Append(" = ").Append(v.Value).Append(" - ").Append(v.Help); n++; }
            CmodLog.Info(n == 0 ? $"Nothing matches '{q}'." : $"{n} match{(n == 1 ? "" : "es")} for '{q}':{sb}");
        }

        static void CmdGive(string[] args)
        {
            if (args.Length == 0)
            {
                var ids = WeaponRegistry.Prototypes.Select(w => $"{w.Id} ({w.PrintName})");
                CmodLog.Info("Weapons: " + string.Join(", ", ids));
                return;
            }
            GiveAndSelect(args[0]);
        }

        /// <summary>Gives the weapon to the local player and switches to it. Returns false (and logs why) on failure.</summary>
        public static bool GiveAndSelect(string weaponId)
        {
            var p = RequirePlayer();
            if (p == null) return false;
            if (p.IsDead) { CmodLog.Warn("You are dead."); return false; }
            if (WeaponRegistry.Create(weaponId) == null) { CmodLog.Warn($"Unknown weapon '{weaponId}'. Type 'give' for a list."); return false; }
            var w = p.Give(weaponId);
            if (w == null) { CmodLog.Warn($"Could not give '{weaponId}'."); return false; }
            p.SelectWeapon(w);
            return true;
        }

        static void CmdSpawn(string[] args)
        {
            if (args.Length == 0)
            {
                var sb = new StringBuilder("Props:");
                foreach (var group in PropRegistry.All.GroupBy(d => d.Category))
                {
                    sb.Append("\n  ").Append(group.Key).Append(": ");
                    sb.Append(string.Join(", ", group.Select(d => d.Id)));
                }
                CmodLog.Info(sb.ToString());
                return;
            }
            var def = PropRegistry.Get(args[0]);
            if (def == null) { CmodLog.Warn($"Unknown prop '{args[0]}'. Type 'spawn' for a list."); return; }
            int count = 1;
            if (args.Length > 1 && int.TryParse(args[1], out var n)) count = Mathf.Clamp(n, 1, 50);
            if (RequirePlayer() == null) return;
            int spawned = 0;
            for (int i = 0; i < count; i++)
                if (EntityManager.SpawnPropAtCrosshair(def) != null) spawned++;
            if (spawned == 0) CmodLog.Warn($"Could not spawn '{def.Id}'.");
            else if (count > 1) CmodLog.Info($"Spawned {spawned} x {def.DisplayName}.");
        }
    }
}
