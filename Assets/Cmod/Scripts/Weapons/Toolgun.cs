using System;
using Cmod.UI;
using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// The tool gun. LMB / RMB / R call the active ToolMode (ToolRegistry.Active) with an aim trace,
    /// as long as Hooks.CanTool allows it. A successful action fires a bright tracer, flashes the gun's
    /// screen and plays a zap. The HUD shows a Win98 window-styled panel with the tool's name,
    /// description, instructions and current status.
    /// </summary>
    public sealed class Toolgun : WeaponBase
    {
        public override string Id => "weapon_toolgun";
        public override string PrintName => "Tool Gun";
        public override string Category => "Tools";
        public override int Slot => 1;

        const float Range = 4096f;
        const float FireDelay = 0.2f;
        static readonly Color BeamColor = new Color(0.55f, 0.85f, 1f, 1f);
        static readonly Color ScreenIdle = new Color(0f, 0.5f, 0.5f);        // teal
        static readonly Color ScreenFlash = new Color(0.6f, 1f, 1f);

        ViewModel _vm;
        Material _screenMat;
        float _nextFire;
        float _flash;          // 0..1 screen flash after a successful shot

        // =====================================================================================
        // Lifecycle
        // =====================================================================================

        public override void Deploy()
        {
            BuildViewModel();
        }

        public override void Holster()
        {
            try { ToolRegistry.Active?.Holster(); }   // cancels half-finished two-click actions
            catch (Exception ex) { CmodLog.Error($"[toolgun] {ex.Message}"); }
            _vm?.Destroy();          // also destroys the screen's private material
            _vm = null;
            _screenMat = null;
        }

        void BuildViewModel()
        {
            _vm?.Destroy();
            _vm = new ViewModel(Owner, "toolgun");
            var dark = new Color(0.28f, 0.29f, 0.31f);
            var mid = new Color(0.55f, 0.56f, 0.58f);
            var light = new Color(0.75f, 0.75f, 0.75f);

            _vm.Part("Body", new Vector3(0f, 0f, 0f), new Vector3(0.07f, 0.085f, 0.24f), mid);
            _vm.Part("Top", new Vector3(0f, 0.05f, -0.02f), new Vector3(0.055f, 0.02f, 0.17f), light);
            _vm.Part("Grip", new Vector3(0f, -0.085f, -0.07f), new Vector3(0.045f, 0.11f, 0.055f), dark, PrimitiveType.Cube, null, new Vector3(-12f, 0f, 0f));
            _vm.Part("Barrel", new Vector3(0f, 0.005f, 0.15f), new Vector3(0.03f, 0.04f, 0.03f), dark, PrimitiveType.Cylinder, null, new Vector3(90f, 0f, 0f));
            _vm.Part("Tip", new Vector3(0f, 0.005f, 0.195f), new Vector3(0.04f, 0.008f, 0.04f), light, PrimitiveType.Cylinder, null, new Vector3(90f, 0f, 0f));
            // Screen on the left side, facing the player a little (the "tool display").
            var screen = _vm.Part("Screen", new Vector3(-0.038f, 0.012f, -0.04f), new Vector3(0.006f, 0.05f, 0.09f), ScreenIdle);
            _screenMat = CmodMaterials.Lit(ScreenIdle);
            CmodMaterials.MakeEmissive(_screenMat, ScreenIdle);
            screen.GetComponent<Renderer>().sharedMaterial = _screenMat;
            _vm.Part("ScreenFrame", new Vector3(-0.036f, 0.012f, -0.04f), new Vector3(0.004f, 0.06f, 0.1f), dark);
            _vm.Muzzle = _vm.Marker("Muzzle", new Vector3(0f, 0.005f, 0.21f));
            _vm.RestPosition = new Vector3(0.19f, -0.17f, 0.36f);
            _vm.Model.localRotation = Quaternion.Euler(0f, -4f, 0f);
        }

        // =====================================================================================
        // Input
        // =====================================================================================

        public override void Think(float dt)
        {
            if (_vm == null || _vm.Root == null) BuildViewModel();
            _vm.Update(dt);

            _flash = Mathf.MoveTowards(_flash, 0f, dt * 4f);
            if (_screenMat != null)
            {
                var c = Color.Lerp(ScreenIdle, ScreenFlash, _flash);
                _screenMat.color = c;
                _screenMat.SetColor("_EmissionColor", c * (1f + _flash));
            }

            var tool = ToolRegistry.Active;
            if (tool != null && Owner != null)
            {
                try { tool.Think(Owner); }
                catch (Exception ex) { CmodLog.Error($"[tool {tool.Id}.Think] {ex.GetType().Name}: {ex.Message}"); }
            }
        }

        public override void PrimaryAttack() => Fire(0);
        public override void SecondaryAttack() => Fire(1);
        public override void Reload() => Fire(2);

        /// <summary>Traces, asks the active tool to act, and plays the effect on success.</summary>
        void Fire(int button)
        {
            if (Owner == null || Time.time < _nextFire) return;
            var tool = ToolRegistry.Active;
            if (tool == null) { Deny(); return; }
            ToolUi.FlushAllPending();   // a settings slider released outside the menu still gets saved first
            if (!Owner.TraceAim(out var hit, Range)) return;

            var ent = EntityManager.FromCollider(hit.collider);
            if (!Hooks.RunAllowed(Hooks.CanTool, tool, hit)) { Deny(); return; }

            bool ok;
            try
            {
                ok = button == 0 ? tool.LeftClick(hit, ent) : button == 1 ? tool.RightClick(hit, ent) : tool.Reload(hit, ent);
            }
            catch (Exception ex)
            {
                CmodLog.Error($"[tool {tool.Id}] {ex.GetType().Name}: {ex.Message}");
                ok = false;
            }

            if (ok)
            {
                _nextFire = Time.time + FireDelay;
                var from = _vm != null ? _vm.MuzzlePosition : Owner.EyePosition;
                WeaponFx.Tracer(from, hit.point, BeamColor, 0.03f, 0.12f);
                WeaponFx.FlashSprite(hit.point + hit.normal * 0.02f, BeamColor, 0.35f, 0.15f);
                WeaponFx.FlashSprite(from, BeamColor, 0.08f, 0.08f);
                WeaponFx.FlashLight(hit.point + hit.normal * 0.15f, BeamColor, 2f, 3f, 0.12f);
                WeaponFx.Play("toolgun_zap", 0.55f, UnityEngine.Random.Range(0.97f, 1.04f));
                _vm?.Kick(0.35f);
                _flash = 1f;
            }
            else
            {
                Deny();
            }
        }

        void Deny()
        {
            _nextFire = Time.time + FireDelay * 0.5f;
            WeaponFx.Play("tool_deny", 0.35f);
        }

        // =====================================================================================
        // HUD
        // =====================================================================================

        public override void DrawHUD()
        {
            Win98.EnsureInit();
            var tool = ToolRegistry.Active;

            string title = tool != null ? tool.Name : "No tool selected";
            string desc = tool != null ? tool.Description : "Hold Q to open the spawn menu and choose a tool on the Tools tab.";
            string instr = tool != null ? (tool.Instructions ?? "") : "";
            instr = instr.Replace("   ", "\n");
            string status = null;
            if (tool is IToolStatus st)
            {
                try { status = st.Status; } catch (Exception) { status = null; }
            }

            const float x = 8f, y = 8f, w = 300f;
            float inner = w - 16f;
            var wrap = ToolUi.WrapStyle;
            float hDesc = wrap.CalcHeight(ToolUi.TempContent(desc), inner);
            float hInstr = string.IsNullOrEmpty(instr) ? 0f : wrap.CalcHeight(ToolUi.TempContent(instr), inner);
            float h = 3f + 18f + 6f + hDesc + (hInstr > 0f ? 8f + hInstr : 0f) + (status != null ? 20f : 0f) + 8f;

            var frame = new Rect(x, y, w, h);
            Win98.RaisedPanel(frame);

            // Title bar
            var bar = new Rect(x + 3f, y + 3f, w - 6f, 18f);
            Win98.Gradient(bar, Win98.TitleActive, Win98.TitleActiveEnd);
            Win98.Icon(new Rect(bar.x + 2f, bar.y + 1f, 16f, 16f), "tool");
            Win98.Label(new Rect(bar.x + 21f, bar.y, bar.width - 24f, bar.height), "Tool Gun - " + title, true, TextAnchor.MiddleLeft, Color.white);

            float cy = bar.yMax + 6f;
            GUI.Label(new Rect(x + 8f, cy, inner, hDesc), ToolUi.TempContent(desc), wrap);
            cy += hDesc;
            if (hInstr > 0f)
            {
                cy += 3f;
                Win98.Separator(new Rect(x + 6f, cy, w - 12f, 2f));
                cy += 5f;
                GUI.Label(new Rect(x + 8f, cy, inner, hInstr), ToolUi.TempContent(instr), wrap);
                cy += hInstr;
            }
            if (status != null)
            {
                cy += 2f;
                Win98.Label(new Rect(x + 8f, cy, inner, 18f), "> " + status, true, TextAnchor.MiddleLeft, Win98.TitleActive);
            }

            WeaponHud.DrawNotice();
        }
    }
}
