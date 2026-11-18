using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// Color: LMB paints the entity (every mesh part) with the chosen color, RMB copies an entity's color
    /// into the settings. The color lives in the archived convar "colour_color" ("r g b", 0-255).
    /// </summary>
    public sealed class ColorTool : ToolMode
    {
        public override string Id => "colour";
        public override string Name => "Color";
        public override string Category => "Render";
        public override string Description => "Changes the color of objects.";
        public override string Instructions => "LMB: apply color   RMB: copy color from object";

        readonly ConVar _color = ToolUi.Var("colour_color", "255 255 255", "Color tool: color as \"r g b\" (0-255)");

        public Color CurrentColor => ToolUi.ParseColor(_color.Value, Color.white);

        public override bool LeftClick(RaycastHit hit, CmodEntity ent)
        {
            if (ent == null) return false;
            var color = CurrentColor;
            var rends = PaintableRenderers(ent);
            if (rends.Count == 0) return false;

            var old = new Color[rends.Count];
            for (int i = 0; i < rends.Count; i++)
            {
                old[i] = rends[i].sharedMaterial != null ? rends[i].sharedMaterial.color : Color.white;
                var m = ToolEntities.PaintMaterial(ent, rends[i]);
                if (m != null) m.color = color;
            }

            UndoManager.Push("Color", () =>
            {
                if (ent == null) return;
                for (int i = 0; i < rends.Count; i++)
                {
                    if (rends[i] == null) continue;
                    var m = ToolEntities.PaintMaterial(ent, rends[i]);
                    if (m != null) m.color = old[i];
                }
            });
            return true;
        }

        /// <summary>The entity's mesh parts, minus effect parts (a lamp's lens and glow keep their own look).</summary>
        static System.Collections.Generic.List<MeshRenderer> PaintableRenderers(CmodEntity ent)
        {
            var list = new System.Collections.Generic.List<MeshRenderer>();
            bool isLamp = ent.GetComponent<LampEntity>() != null;
            foreach (var r in ent.GetComponentsInChildren<MeshRenderer>())
            {
                if (isLamp && r.gameObject != ent.gameObject) continue;
                if (r.GetComponent<GlowSprite>() != null) continue;
                list.Add(r);
            }
            return list;
        }

        public override bool RightClick(RaycastHit hit, CmodEntity ent)
        {
            if (ent == null) return false;
            // Read the shared material: Renderer.material (CmodEntity.Color) would clone and leak it.
            var c = ent.Renderer != null && ent.Renderer.sharedMaterial != null ? ent.Renderer.sharedMaterial.color : Color.white;
            var prev = _color.Value;
            _color.Value = ToolUi.FormatColor(c);
            UndoManager.Push("Copy color", () => _color.Value = prev);
            ToolHelpers.Notify($"Copied color {_color.Value}");
            return true;
        }

        public override float DrawSettings(Rect area)
        {
            float y = area.y;
            ToolUi.ColorPicker(area, ref y, "Color", _color);
            ToolUi.Help(area, ref y, "Pick a color, then left-click an object to paint it. Right-click an object to copy its color.");
            return y - area.y;
        }
    }
}
