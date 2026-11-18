using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// Lamp: LMB spawns a lamp (a spotlight in a small housing) on the surface, shining away from it.
    /// LMB on an existing lamp updates it with the current settings; RMB copies a lamp's settings;
    /// R switches a lamp on or off. With "Weld to surface" the lamp is welded to the object it was
    /// placed on, or frozen in place on world geometry.
    /// </summary>
    public sealed class LampTool : ToolMode
    {
        public override string Id => "lamp";
        public override string Name => "Lamp";
        public override string Category => "Construction";
        public override string Description => "Places lamps that light up the world.";
        public override string Instructions => "LMB: place lamp / update lamp   RMB: copy lamp settings   R: switch lamp on/off";

        readonly ConVar _color = ToolUi.Var("lamp_color", "255 244 214", "Lamp tool: light color as \"r g b\" (0-255)");
        readonly ConVar _brightness = ToolUi.Var("lamp_brightness", "3", "Lamp tool: light intensity", 0.2f, 8f);
        readonly ConVar _range = ToolUi.Var("lamp_range", "20", "Lamp tool: light range in meters", 3f, 60f);
        readonly ConVar _weld = ToolUi.Var("lamp_weld", "1", "Lamp tool: 1 = weld to the surface (or freeze on the world)", 0f, 1f);

        Color LightColor => ToolUi.ParseColor(_color.Value, Color.white);

        public override bool LeftClick(RaycastHit hit, CmodEntity ent)
        {
            var existing = ent != null ? ent.GetComponent<LampEntity>() : null;
            if (existing != null) return UpdateLamp(existing);

            var normal = hit.normal;
            var up = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.95f
                ? (CmodPlayer.Local != null ? Vector3.ProjectOnPlane(CmodPlayer.Local.AimRay.direction, normal) : Vector3.forward)
                : Vector3.up;
            if (up.sqrMagnitude < 0.0001f) up = Vector3.forward;
            var rot = Quaternion.LookRotation(normal, up);
            const float halfDepth = 0.17f;      // lamp scale.z / 2
            var pos = hit.point + normal * (halfDepth + 0.01f);

            var lamp = ToolEntities.SpawnLamp(pos, rot, LightColor, _brightness.Float, _range.Float);
            if (lamp == null) return false;

            if (_weld.Bool)
            {
                if (ent != null) Constraints.Weld(lamp, ent);
                else lamp.Frozen = true;
            }
            ToolHelpers.PushRemoveEntityUndo("Lamp", lamp);
            return true;
        }

        bool UpdateLamp(LampEntity lamp)
        {
            var oldColor = lamp.LightColor;
            float oldB = lamp.Brightness, oldR = lamp.Range;
            lamp.LightColor = LightColor;
            lamp.Brightness = _brightness.Float;
            lamp.Range = _range.Float;
            lamp.Apply();
            UndoManager.Push("Lamp settings", () =>
            {
                if (lamp == null) return;
                lamp.LightColor = oldColor; lamp.Brightness = oldB; lamp.Range = oldR;
                lamp.Apply();
            });
            return true;
        }

        public override bool RightClick(RaycastHit hit, CmodEntity ent)
        {
            var lamp = ent != null ? ent.GetComponent<LampEntity>() : null;
            if (lamp == null) return false;
            string pc = _color.Value, pb = _brightness.Value, pr = _range.Value;
            _color.Value = ToolUi.FormatColor(lamp.LightColor);
            _brightness.SetFloat(lamp.Brightness);
            _range.SetFloat(lamp.Range);
            UndoManager.Push("Copy lamp settings", () => { _color.Value = pc; _brightness.Value = pb; _range.Value = pr; });
            ToolHelpers.Notify("Copied lamp settings");
            return true;
        }

        public override bool Reload(RaycastHit hit, CmodEntity ent)
        {
            var lamp = ent != null ? ent.GetComponent<LampEntity>() : null;
            if (lamp == null) return false;
            lamp.Toggle();
            UndoManager.Push("Toggle lamp", () => { if (lamp != null) lamp.Toggle(); });
            return true;
        }

        public override float DrawSettings(Rect area)
        {
            float y = area.y;
            ToolUi.Slider(area, ref y, "Brightness", _brightness, 0.2f, 8f, "0.0");
            ToolUi.Slider(area, ref y, "Range (m)", _range, 3f, 60f, "0");
            ToolUi.Checkbox(area, ref y, "Weld to surface", _weld);
            y += 4f;
            ToolUi.ColorPicker(area, ref y, "Light color", _color);
            return y - area.y;
        }
    }
}
