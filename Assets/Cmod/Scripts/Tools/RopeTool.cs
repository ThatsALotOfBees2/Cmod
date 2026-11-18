using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// Rope: click two points to tie them with a rope (a ConfigurableJoint limited to the rope length,
    /// drawn as a sagging LineRenderer). Either end may be the world, but not both. R removes ropes.
    /// </summary>
    public sealed class RopeTool : TwoClickTool
    {
        public override string Id => "rope";
        public override string Name => "Rope";
        public override string Category => "Constraints";
        public override string Description => "Ties two points together with a rope.";
        public override string Instructions => "LMB: select first, then second point   RMB: cancel   R: remove ropes from object";

        readonly ConVar _slack = ToolUi.Var("rope_addlength", "0", "Rope tool: extra length (slack) in meters", 0f, 10f);
        readonly ConVar _width = ToolUi.Var("rope_width", "0.02", "Rope tool: rope width in meters", 0.005f, 0.15f);
        readonly ConVar _color = ToolUi.Var("rope_color", "115 84 51", "Rope tool: color as \"r g b\" (0-255)");

        protected override string FirstPrompt => "Click the first point.";
        protected override string SecondPrompt => "Now click the second point.";

        public override bool LeftClick(RaycastHit hit, CmodEntity ent)
        {
            if (!HasFirst)
            {
                SetFirst(hit, ent);
                return true;
            }

            var first = FirstEnt;
            var a = FirstWorld;
            bool firstWasEntity = FirstIsEntity;
            ClearFirst();
            if (firstWasEntity && first == null) return false;
            if (first == ent && ent != null) { ToolHelpers.Notify("Both ends are on the same object."); return false; }
            if ((first == null || first.Body == null) && (ent == null || ent.Body == null))
            {
                ToolHelpers.Notify("At least one end must be on a physical object.");
                return false;
            }

            float length = Vector3.Distance(a, hit.point) + _slack.Float;
            var c = Constraints.Rope(first, a, ent, hit.point, length, _width.Float, ToolUi.ParseColor(_color.Value, new Color(0.45f, 0.33f, 0.2f)));
            if (c == null) return false;
            WeaponFx.PlayAt("rope", hit.point, 0.9f, Random.Range(0.9f, 1.1f));
            ToolHelpers.PushRemoveUndo("Rope", c);
            return true;
        }

        public override bool RightClick(RaycastHit hit, CmodEntity ent)
        {
            if (!HasFirst) return false;
            ClearFirst();
            return true;
        }

        public override bool Reload(RaycastHit hit, CmodEntity ent)
        {
            ClearFirst();
            if (ent == null) return false;
            var removed = Constraints.RemoveAll(ent, ConstraintType.Rope);
            if (removed.Count == 0) return false;
            ToolHelpers.PushRecreateUndo("Remove ropes", removed);
            ToolHelpers.Notify(removed.Count == 1 ? "Removed 1 rope" : $"Removed {removed.Count} ropes");
            return true;
        }

        public override float DrawSettings(Rect area)
        {
            float y = area.y;
            ToolUi.Slider(area, ref y, "Slack (m)", _slack, 0f, 10f, "0.0");
            ToolUi.Slider(area, ref y, "Width (m)", _width, 0.005f, 0.15f, "0.000");
            y += 4f;
            ToolUi.ColorPicker(area, ref y, "Rope color", _color);
            ToolUi.Help(area, ref y, "The rope is as long as the distance between the two clicks plus the slack.");
            return y - area.y;
        }
    }
}
