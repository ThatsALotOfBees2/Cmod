using System.Text;
using UnityEngine;

namespace Cmod
{
    /// <summary>Registers the built-in weapons and toolgun modes, plus a few related console commands.</summary>
    public static partial class CmodModules
    {
        public static partial void Weapons_Register()
        {
            // Weapons (see BOARD.md, Weapon IDs). Each factory makes a fresh instance per player.
            // weapon_physgun is ON HOLD (BOARD.md, C-PHYSGUN-HOLD): not registered, so it is not in the
            // spawn menu, "give" or the loadout. The class still compiles until that card moves it out.
            WeaponRegistry.Register(() => new Toolgun());
            WeaponRegistry.Register(() => new Pistol());
            WeaponRegistry.Register(() => new Wrench());

            // Tool modes. The first registered one is the default active tool.
            ToolRegistry.Register(new RemoverTool());
            ToolRegistry.Register(new ColorTool());
            ToolRegistry.Register(new WeldTool());
            ToolRegistry.Register(new RopeTool());
            ToolRegistry.Register(new BalloonTool());
            ToolRegistry.Register(new PhysPropsTool());
            ToolRegistry.Register(new LampTool());

            ConCommands.Register("gmod_tool", "gmod_tool <id>: select a toolgun mode (and the toolgun). No argument lists the tools.", args =>
            {
                if (args.Length == 0)
                {
                    var sb = new StringBuilder("Tools:");
                    foreach (var t in ToolRegistry.All) sb.Append("\n  ").Append(t.Id).Append(" - ").Append(t.Name);
                    CmodLog.Info(sb.ToString());
                    return;
                }
                var tool = ToolRegistry.Get(args[0]);
                if (tool == null) { CmodLog.Warn($"Unknown tool '{args[0]}'. Type gmod_tool for a list."); return; }
                ToolRegistry.Active = tool;
                var p = CmodPlayer.Local;
                if (p != null)
                {
                    p.Give("weapon_toolgun");
                    p.SelectWeapon("weapon_toolgun");
                }
            });

            ConCommands.Register("constraints_clear", "Removes every weld and rope.", args =>
            {
                int n = Constraints.All.Count;
                Constraints.Clear();
                CmodLog.Info($"Removed {n} constraints.");
            });

            CmodLog.Info($"[weapons] registered 3 weapons and {ToolRegistry.All.Count} tools");
        }
    }
}
