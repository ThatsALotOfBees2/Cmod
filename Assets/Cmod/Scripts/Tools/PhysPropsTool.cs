using UnityEngine;
#if UNITY_6000_0_OR_NEWER
using PhysMat = UnityEngine.PhysicsMaterial;
#else
using PhysMat = UnityEngine.PhysicMaterial;
#endif

namespace Cmod
{
    /// <summary>
    /// Physical Properties: LMB applies the gravity toggle and a material preset (friction, bounce)
    /// to every collider of the object. RMB copies the object's settings.
    /// </summary>
    public sealed class PhysPropsTool : ToolMode
    {
        public override string Id => "physprop";
        public override string Name => "Physical Properties";
        public override string Category => "Construction";
        public override string Description => "Changes an object's gravity and surface material.";
        public override string Instructions => "LMB: apply settings   RMB: copy settings from object";

        static readonly string[] s_presetNames = System.Enum.GetNames(typeof(PhysicMaterialPreset));

        readonly ConVar _gravity = ToolUi.Var("physprop_gravity", "1", "Physical Properties tool: 1 = affected by gravity", 0f, 1f);
        readonly ConVar _material = ToolUi.Var("physprop_material", "0", "Physical Properties tool: material preset index",
            0f, s_presetNames.Length - 1);

        PhysicMaterialPreset Preset => (PhysicMaterialPreset)Mathf.Clamp(_material.Int, 0, s_presetNames.Length - 1);

        public override bool LeftClick(RaycastHit hit, CmodEntity ent)
        {
            if (ent == null) return false;
            var cols = ent.GetComponentsInChildren<Collider>();
            var oldMats = new PhysMat[cols.Length];
            for (int i = 0; i < cols.Length; i++) oldMats[i] = cols[i].sharedMaterial;
            bool hadBody = ent.Body != null;
            bool oldGravity = hadBody && ent.Body.useGravity;

            var preset = Preset;
            foreach (var c in cols) PhysicsCompat.ApplyMaterial(c, preset);
            if (hadBody)
            {
                ent.Body.useGravity = _gravity.Bool;
                if (!ent.Body.isKinematic) ent.Body.WakeUp();
            }

            UndoManager.Push("Physical Properties", () =>
            {
                if (ent == null) return;
                for (int i = 0; i < cols.Length; i++) if (cols[i] != null) cols[i].sharedMaterial = oldMats[i];
                if (hadBody && ent.Body != null) { ent.Body.useGravity = oldGravity; ent.Body.WakeUp(); }
            });
            return true;
        }

        public override bool RightClick(RaycastHit hit, CmodEntity ent)
        {
            if (ent == null) return false;
            string prevG = _gravity.Value, prevM = _material.Value;
            if (ent.Body != null) _gravity.SetBool(ent.Body.useGravity);
            // Match the collider's material name against the presets (PhysicsCompat names them after the preset).
            var col = hit.collider;
            var matName = col != null && col.sharedMaterial != null ? col.sharedMaterial.name : "";
            int idx = 0;
            for (int i = 0; i < s_presetNames.Length; i++)
                if (matName.StartsWith(s_presetNames[i], System.StringComparison.OrdinalIgnoreCase)) idx = i;
            _material.SetFloat(idx);
            UndoManager.Push("Copy physical properties", () => { _gravity.Value = prevG; _material.Value = prevM; });
            ToolHelpers.Notify($"Copied: gravity {(_gravity.Bool ? "on" : "off")}, {s_presetNames[idx]}");
            return true;
        }

        public override float DrawSettings(Rect area)
        {
            float y = area.y;
            ToolUi.Checkbox(area, ref y, "Gravity", _gravity);
            ToolUi.Dropdown(area, ref y, "Material", _material, s_presetNames, "physprop_material");
            ToolUi.Help(area, ref y, "Wood: grippy, a little bouncy. Metal: smooth. Rubber: very bouncy. Ice: almost no friction.");
            return y - area.y;
        }
    }
}
