using System.Globalization;
using UnityEngine;

namespace Cmod.UI
{
    /// <summary>
    /// The Options dialog: a Win98 property sheet with OK / Cancel / Apply.
    /// Edits are kept locally until Apply (or OK) writes them to the archived convars:
    ///   Mouse  sensitivity, m_invert
    ///   Video  fov, ui_scale, cl_showfps
    ///   Audio  volume (AudioListener.volume)
    ///   Game   name, spawnmenu_toggle
    /// </summary>
    public sealed class OptionsWindow : Win98Window
    {
        static readonly string[] s_tabNames = { "Mouse", "Video", "Audio", "Game" };
        const float Pad = 6f;
        const float BtnW = 75f, BtnH = 23f, BtnGap = 6f;
        const string NameField = "cmod_options_name";

        int _tab;

        // Pending values (what the dialog shows).
        float _sensitivity, _fov, _uiScale, _volume;
        bool _invert, _showFps, _spawnToggle;
        string _name = "";

        public OptionsWindow()
        {
            Title = "Options";
            IconName = "settings";
            Rect = new Rect(0, 0, 400, 360);
            Resizable = false;
            Minimizable = false;
        }

        public override void OnOpened()
        {
            Load();
            CenterOnScreen();
        }

        // =====================================================================================
        // Convar plumbing
        // =====================================================================================

        static ConVar Var(string name) => ConCommands.GetVar(name);
        static float GetF(string name, float fallback) { var v = Var(name); return v != null ? v.Float : fallback; }
        static bool GetB(string name) { var v = Var(name); return v != null && v.Bool; }

        /// <summary>Copies the current convar values into the dialog.</summary>
        void Load()
        {
            _sensitivity = GetF("sensitivity", 2f);
            _invert = GetB("m_invert");
            _fov = GetF("fov", 80f);
            _uiScale = GetF("ui_scale", 1f);
            _showFps = GetB("cl_showfps");
            _volume = GetF("volume", 1f);
            _name = Var("name")?.Value ?? "player";
            _spawnToggle = GetB("spawnmenu_toggle");
        }

        bool Dirty =>
            !Mathf.Approximately(_sensitivity, GetF("sensitivity", 2f)) ||
            _invert != GetB("m_invert") ||
            !Mathf.Approximately(_fov, GetF("fov", 80f)) ||
            !Mathf.Approximately(_uiScale, GetF("ui_scale", 1f)) ||
            _showFps != GetB("cl_showfps") ||
            !Mathf.Approximately(_volume, GetF("volume", 1f)) ||
            _name != (Var("name")?.Value ?? "player") ||
            _spawnToggle != GetB("spawnmenu_toggle");

        /// <summary>Writes the dialog's values to the convars (which save themselves).</summary>
        void Apply()
        {
            Var("sensitivity")?.SetFloat(_sensitivity);
            Var("m_invert")?.SetBool(_invert);
            Var("fov")?.SetFloat(_fov);
            Var("cl_showfps")?.SetBool(_showFps);
            Var("volume")?.SetFloat(_volume);
            Var("spawnmenu_toggle")?.SetBool(_spawnToggle);
            var cleanName = BaseCommands.SanitizeName(_name);
            var nameVar = Var("name");
            if (nameVar != null) nameVar.Value = cleanName;
            _name = cleanName;
            // ui_scale last: it changes the coordinate space this window lives in.
            Var("ui_scale")?.SetFloat(_uiScale);
            Load();
        }

        public override void Tick()
        {
            // Enter = OK, like every Win98 property sheet.
            if (IsFocused && WindowManager.TopModal == null &&
                (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                Apply();
                Close();
            }
        }

        // =====================================================================================
        // Drawing
        // =====================================================================================

        public override void DrawContents(Rect c)
        {
            var tabs = new Rect(c.x + Pad, c.y + Pad, c.width - 2 * Pad, c.height - 2 * Pad - BtnH - Pad);
            _tab = Win98.Tabs(tabs, _tab, s_tabNames);
            var page = Win98.TabContentRect(tabs);

            switch (_tab)
            {
                case 0: DrawMouse(page); break;
                case 1: DrawVideo(page); break;
                case 2: DrawAudio(page); break;
                default: DrawGame(page); break;
            }

            // OK / Cancel / Apply, bottom right.
            float y = c.yMax - Pad - BtnH;
            var apply = new Rect(c.xMax - Pad - BtnW, y, BtnW, BtnH);
            var cancel = new Rect(apply.x - BtnGap - BtnW, y, BtnW, BtnH);
            var ok = new Rect(cancel.x - BtnGap - BtnW, y, BtnW, BtnH);
            if (Win98.Button(ok, "OK")) { Apply(); Close(); }
            if (Win98.Button(cancel, "Cancel")) Close();
            if (Win98.Button(apply, "Apply", Dirty)) Apply();
        }

        /// <summary>"Label  [slow ---|--- fast]  value" row inside a group box. Returns the new value.</summary>
        static float SliderRow(Rect g, float y, string left, string right, float value, float min, float max, float step, string valueText)
        {
            const float sideW = 40f, valueW = 48f;
            float x = g.x + 12f;
            float w = g.width - 24f;
            Win98.Label(new Rect(x, y + 3f, sideW, 16f), left);
            float sliderW = w - 2 * sideW - valueW - 4f;
            float v = Win98.Slider(new Rect(x + sideW, y, sliderW, 22f), value, min, max);
            Win98.Label(new Rect(x + sideW + sliderW + 4f, y + 3f, sideW, 16f), right);
            Win98.Label(new Rect(x + w - valueW, y + 3f, valueW, 16f), valueText, true, TextAnchor.MiddleRight);
            // Snap only when the user actually moved the slider; leave loaded (off-grid) values untouched.
            if (v == value) return value;
            return Mathf.Clamp(Mathf.Round(v / step) * step, min, max);
        }

        static void Hint(Rect g, float y, string text)
        {
            var r = new Rect(g.x + 12f, y, g.width - 24f, Win98.TextHeight(text, g.width - 24f));
            Win98.DrawText(r, text, false, TextAnchor.UpperLeft, Win98.Shadow, true);
        }

        void DrawMouse(Rect p)
        {
            var g = new Rect(p.x, p.y, p.width, 104f);
            Win98.GroupBox(g, "Pointer speed");
            Win98.Icon(new Rect(g.x + 12f, g.y + 18f, 32f, 32f), "computer");
            Hint(new Rect(g.x + 44f, g.y, g.width - 44f, g.height), g.y + 22f, "How far the view turns when you move the mouse (sensitivity).");
            _sensitivity = SliderRow(g, g.y + 60f, "Slow", "Fast", _sensitivity, 0.1f, 10f, 0.05f,
                _sensitivity.ToString("0.0#", CultureInfo.InvariantCulture));

            var g2 = new Rect(p.x, g.yMax + 8f, p.width, 56f);
            Win98.GroupBox(g2, "Buttons");
            _invert = Win98.Checkbox(new Rect(g2.x + 12f, g2.y + 22f, g2.width - 24f, 18f), _invert, "Invert mouse Y axis (m_invert)");
        }

        void DrawVideo(Rect p)
        {
            var g = new Rect(p.x, p.y, p.width, 70f);
            Win98.GroupBox(g, "Field of view");
            _fov = SliderRow(g, g.y + 20f, "60", "120", _fov, 60f, 120f, 1f, Mathf.RoundToInt(_fov) + "°");
            Hint(g, g.y + 46f, "Wider views show more but look more distorted.");

            var g2 = new Rect(p.x, g.yMax + 8f, p.width, 92f);
            Win98.GroupBox(g2, "Interface");
            _uiScale = SliderRow(g2, g2.y + 20f, "Small", "Large", _uiScale, 0.75f, 2.5f, 0.25f, Mathf.RoundToInt(_uiScale * 100f) + "%");
            _showFps = Win98.Checkbox(new Rect(g2.x + 12f, g2.y + 50f, g2.width - 24f, 18f), _showFps, "Show frames per second in the taskbar");
            Hint(g2, g2.y + 70f, "The interface size changes when you click Apply.");
        }

        void DrawAudio(Rect p)
        {
            var g = new Rect(p.x, p.y, p.width, 92f);
            Win98.GroupBox(g, "Master volume");
            _volume = SliderRow(g, g.y + 22f, "Low", "High", _volume, 0f, 1f, 0.01f, Mathf.RoundToInt(_volume * 100f) + "%");
            bool muted = _volume <= 0f;
            bool newMuted = Win98.Checkbox(new Rect(g.x + 12f, g.y + 52f, g.width - 24f, 18f), muted, "Mute");
            if (newMuted != muted) _volume = newMuted ? 0f : 1f;
        }

        void DrawGame(Rect p)
        {
            var g = new Rect(p.x, p.y, p.width, 72f);
            Win98.GroupBox(g, "Player");
            Win98.Label(new Rect(g.x + 12f, g.y + 24f, 80f, 16f), "Player name:");
            var typed = Win98.TextField(new Rect(g.x + 96f, g.y + 21f, g.width - 108f, 21f), _name, NameField);
            if (typed != null && typed.Length > 32) typed = typed.Substring(0, 32);
            _name = typed ?? "";
            Hint(g, g.y + 48f, "Shown in About and, later, to other players.");

            var g2 = new Rect(p.x, g.yMax + 8f, p.width, 56f);
            Win98.GroupBox(g2, "Spawn menu");
            _spawnToggle = Win98.Checkbox(new Rect(g2.x + 12f, g2.y + 22f, g2.width - 24f, 18f), _spawnToggle,
                "Q toggles the spawn menu (instead of holding Q)");
        }
    }
}
