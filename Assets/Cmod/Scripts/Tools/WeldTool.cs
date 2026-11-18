using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// Weld: click two objects to join them rigidly (FixedJoint). The second click may be the world.
    /// RMB welds an object straight to the world. R removes every weld on the object.
    /// </summary>
    public sealed class WeldTool : TwoClickTool
    {
        public override string Id => "weld";
        public override string Name => "Weld";
        public override string Category => "Constraints";
        public override string Description => "Welds two objects together so they move as one.";
        public override string Instructions => "LMB: select first, then second object (or the world)   RMB: weld to world   R: remove welds";

        readonly ConVar _force = ToolUi.Var("weld_forcelimit", "0", "Weld tool: break force in newtons (0 = unbreakable)", 0f, 50000f);

        protected override string FirstPrompt => "Click the first object.";
        protected override string SecondPrompt => "Now click the second object (or the world).";

        public override bool LeftClick(RaycastHit hit, CmodEntity ent)
        {
            if (!HasFirst)
            {
                if (ent == null) return false;      // the first click must be an object
                SetFirst(hit, ent);
                return true;
            }

            var first = FirstEnt;
            if (first == null) { ClearFirst(); return false; }
            if (ent == first) return false;         // can't weld to itself; keep the selection
            var c = Constraints.Weld(first, ent, _force.Float);
            ClearFirst();
            if (c == null)
            {
                ToolHelpers.Notify((Constraints.FindBetween(first, ent, ConstraintType.Weld) != null
                                  || Constraints.FindBetween(ent, first, ConstraintType.Weld) != null)
                    ? "Those are already welded." : "Can't weld those (neither one is physical).");
                return false;
            }
            Effect(hit.point);
            ToolHelpers.PushRemoveUndo("Weld", c);
            return true;
        }

        public override bool RightClick(RaycastHit hit, CmodEntity ent)
        {
            if (ent == null || ent.Body == null) return false;
            ClearFirst();
            var c = Constraints.Weld(ent, null, _force.Float);
            if (c == null) { ToolHelpers.Notify("That object is already welded to the world."); return false; }
            Effect(hit.point);
            ToolHelpers.PushRemoveUndo("Weld to world", c);
            return true;
        }

        public override bool Reload(RaycastHit hit, CmodEntity ent)
        {
            ClearFirst();
            if (ent == null) return false;
            var removed = Constraints.RemoveAll(ent, ConstraintType.Weld);
            if (removed.Count == 0) return false;
            ToolHelpers.PushRecreateUndo("Remove welds", removed);
            ToolHelpers.Notify(removed.Count == 1 ? "Removed 1 weld" : $"Removed {removed.Count} welds");
            return true;
        }

        static void Effect(Vector3 at)
        {
            WeaponFx.PlayAt("weld", at, 0.9f, Random.Range(0.95f, 1.1f));
            WeaponFx.FlashSprite(at, new Color(1f, 0.85f, 0.4f, 1f), 0.4f, 0.2f);
        }

        public override float DrawSettings(Rect area)
        {
            float y = area.y;
            ToolUi.Slider(area, ref y, "Force limit", _force, 0f, 50000f, "0");
            ToolUi.Help(area, ref y, "0 = unbreakable. A weld snaps when it is pulled harder than the limit.");
            return y - area.y;
        }
    }
}
