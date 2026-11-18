using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// Balloon: LMB spawns a floating balloon roped to the point you click (an object or the world).
    /// RMB spawns a free balloon. Settings: lift force, rope length, color (or random colors).
    /// </summary>
    public sealed class BalloonTool : ToolMode
    {
        public override string Id => "balloon";
        public override string Name => "Balloon";
        public override string Category => "Construction";
        public override string Description => "Spawns balloons that lift whatever they are tied to.";
        public override string Instructions => "LMB: spawn balloon tied to the surface   RMB: spawn a free balloon";

        readonly ConVar _force = ToolUi.Var("balloon_force", "400", "Balloon tool: lift force in newtons", 0f, 3000f);
        readonly ConVar _length = ToolUi.Var("balloon_ropelength", "1.5", "Balloon tool: rope length in meters", 0.2f, 8f);
        readonly ConVar _color = ToolUi.Var("balloon_color", "255 0 0", "Balloon tool: color as \"r g b\" (0-255)");
        readonly ConVar _random = ToolUi.Var("balloon_random", "0", "Balloon tool: 1 = random color for each balloon", 0f, 1f);

        Color NextColor()
        {
            if (_random.Bool) return Color.HSVToRGB(Random.value, Random.Range(0.6f, 0.95f), Random.Range(0.85f, 1f));
            return ToolUi.ParseColor(_color.Value, Color.red);
        }

        public override bool LeftClick(RaycastHit hit, CmodEntity ent)
        {
            if (ent != null && ent.GetComponent<BalloonEntity>() != null) return false; // don't tie balloons to balloons
            float len = _length.Float;
            var up = Physics.gravity.sqrMagnitude > 0.0001f ? -Physics.gravity.normalized : Vector3.up;
            const float halfHeight = 0.325f;   // balloon scale.y / 2
            var pos = hit.point + up * (len + halfHeight);
            var balloon = ToolEntities.SpawnBalloon(pos, NextColor(), _force.Float);
            if (balloon == null) return false;

            var bottom = pos - up * halfHeight;
            var rope = Constraints.Rope(balloon, bottom, ent, hit.point, len, 0.012f, new Color(0.95f, 0.95f, 0.95f));
            if (rope == null) CmodLog.Warn("[balloon] couldn't tie the rope");
            WeaponFx.PlayAt("rope", hit.point, 0.5f, 1.5f);
            ToolHelpers.PushRemoveEntityUndo("Balloon", balloon);
            return true;
        }

        public override bool RightClick(RaycastHit hit, CmodEntity ent)
        {
            var up = Physics.gravity.sqrMagnitude > 0.0001f ? -Physics.gravity.normalized : Vector3.up;
            var balloon = ToolEntities.SpawnBalloon(hit.point + hit.normal * 0.4f + up * 0.1f, NextColor(), _force.Float);
            if (balloon == null) return false;
            ToolHelpers.PushRemoveEntityUndo("Balloon", balloon);
            return true;
        }

        public override float DrawSettings(Rect area)
        {
            float y = area.y;
            ToolUi.Slider(area, ref y, "Force (N)", _force, 0f, 3000f, "0");
            ToolUi.Slider(area, ref y, "Rope length", _length, 0.2f, 8f, "0.0");
            ToolUi.Checkbox(area, ref y, "Random colors", _random);
            if (!_random.Bool) ToolUi.ColorPicker(area, ref y, "Color", _color);
            ToolUi.Help(area, ref y, "Each balloon carries its own weight plus the force. Shoot a balloon to pop it.");
            return y - area.y;
        }
    }
}
