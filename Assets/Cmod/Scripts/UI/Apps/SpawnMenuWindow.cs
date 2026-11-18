using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Cmod.UI
{
    /// <summary>
    /// The spawn menu (hold Q; "spawnmenu_toggle 1" makes Q a toggle). A large Win98 window with tabs:
    ///   Props      category list on the left, a grid of prop buttons on the right (click = spawn at the crosshair)
    ///   Weapons    a grid of weapons (click = give and select)
    ///   Tools      the toolgun modes (click = select the mode and the toolgun) and the mode's settings
    ///   Utilities  undo, cleanup, respawn, gravity / timescale sliders, noclip and god mode
    /// A status bar at the bottom shows what is under the mouse and the result of the last click.
    /// </summary>
    public sealed class SpawnMenuWindow : Win98Window
    {
        static readonly string[] s_tabNames = { "Props", "Weapons", "Tools", "Utilities" };
        const int TabProps = 0, TabWeapons = 1, TabTools = 2, TabUtilities = 3;

        const float Pad = 4f;
        const float StatusH = 20f;
        const float CellW = 92f, CellH = 80f, CellGap = 4f;
        const float HeadingH = 22f;
        const float ToolListW = 170f;
        const float CategoryListW = 160f;
        const string AllCategory = "All Props";

        int _tab;
        bool _sizedOnce;

        // ---- Status bar ----
        string _status = "Ready";
        float _statusTime;
        string _hoverText;

        // ---- Props tab ----
        readonly List<string> _categories = new List<string>();      // category names, index 0 = all
        readonly List<string> _categoryLabels = new List<string>();  // "Construction (6)"
        int _category;
        Vector2 _categoryScroll, _propScroll;
        readonly List<GridEntry> _propEntries = new List<GridEntry>();
        bool _propsDirty = true;
        int _propsBuiltFor = -1;

        // ---- Weapons tab ----
        readonly List<GridEntry> _weaponEntries = new List<GridEntry>();
        bool _weaponsDirty = true;
        Vector2 _weaponScroll;

        // ---- Tools tab ----
        readonly List<ToolMode> _tools = new List<ToolMode>();
        bool _toolsDirty = true;
        Vector2 _toolListScroll, _toolSettingsScroll;
        float _toolSettingsH = 200f;
        ToolMode _settingsFor;

        // ---- Utilities tab ----
        Vector2 _utilScroll;

        /// <summary>One cell (or a category heading) of a button grid.</summary>
        sealed class GridEntry
        {
            public bool Heading;
            public string Label;
            public string Sub;
            public string Tooltip;
            public string Icon;
            public Texture2D Shape;   // tinted shape preview instead of an icon (props)
            public Color Tint = Color.white;
            public object Payload;    // PropDefinition or weapon id
        }

        public SpawnMenuWindow()
        {
            Title = "Spawn Menu";
            IconName = "box";
            Rect = new Rect(60, 40, 860, 560);
            Resizable = true;
            MinSize = new Vector2(540, 380);
            PropRegistry.Changed += () => _propsDirty = true;
            WeaponRegistry.Changed += () => _weaponsDirty = true;
            ToolRegistry.Changed += () => _toolsDirty = true;
        }

        public override void OnOpened()
        {
            if (!_sizedOnce)
            {
                // First open: a large window that still fits small screens, centered above the taskbar.
                _sizedOnce = true;
                var s = WindowManager.ScreenSizeScaled;
                Rect.width = Mathf.Clamp(s.x - 80f, MinSize.x, 900f);
                Rect.height = Mathf.Clamp(s.y - WindowManager.TaskbarHeight - 80f, MinSize.y, 600f);
                CenterOnScreen();
            }
            _hoverText = null;
        }

        void SetStatus(string text)
        {
            _status = text;
            _statusTime = Time.unscaledTime;
        }

        // =====================================================================================
        // Layout
        // =====================================================================================

        public override void DrawContents(Rect c)
        {
            if (Event.current.type == EventType.Layout) _hoverText = null;

            var tabsRect = new Rect(c.x + Pad, c.y + Pad, c.width - 2 * Pad, c.height - 2 * Pad - StatusH - 2);
            _tab = Win98.Tabs(tabsRect, _tab, s_tabNames);
            var page = Win98.TabContentRect(tabsRect);

            switch (_tab)
            {
                case TabProps: DrawPropsTab(page); break;
                case TabWeapons: DrawWeaponsTab(page); break;
                case TabTools: DrawToolsTab(page); break;
                default: DrawUtilitiesTab(page); break;
            }

            DrawStatusBar(new Rect(c.x + Pad, c.yMax - Pad - StatusH, c.width - 2 * Pad, StatusH));
        }

        void DrawStatusBar(Rect r)
        {
            int ents = EntityManager.All()?.Count ?? 0;
            string right = $"Entities: {ents}   Undo: {UndoManager.Count}";
            float rightW = Win98.TextWidth(right) + 16f;
            var left = new Rect(r.x, r.y, r.width - rightW - 2f, r.height);
            var rightRect = new Rect(left.xMax + 2f, r.y, rightW, r.height);

            Win98.ThinSunken(left);
            Win98.ThinSunken(rightRect);

            string text = _hoverText;
            if (string.IsNullOrEmpty(text))
                text = Time.unscaledTime - _statusTime < 6f ? _status : "Ready";
            var tr = new Rect(left.x + 4f, left.y, left.width - 8f, left.height);
            Win98.DrawText(tr, Win98.Ellipsize(text, tr.width), false, TextAnchor.MiddleLeft, Color.black);
            Win98.DrawText(rightRect, right, false, TextAnchor.MiddleCenter, Color.black);
        }

        // =====================================================================================
        // Props
        // =====================================================================================

        void RebuildCategories()
        {
            _categories.Clear();
            _categoryLabels.Clear();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var def in PropRegistry.All)
            {
                var cat = string.IsNullOrEmpty(def.Category) ? "Props" : def.Category;
                if (!counts.ContainsKey(cat)) { counts[cat] = 0; order.Add(cat); }
                counts[cat]++;
            }
            _categories.Add(AllCategory);
            _categoryLabels.Add($"{AllCategory} ({PropRegistry.All.Count})");
            foreach (var cat in order)
            {
                _categories.Add(cat);
                _categoryLabels.Add($"{cat} ({counts[cat]})");
            }
            _category = Mathf.Clamp(_category, 0, _categories.Count - 1);
        }

        void RebuildPropEntries()
        {
            _propsBuiltFor = _category;
            _propEntries.Clear();
            bool all = _category == 0;
            string only = all ? null : _categories[_category];

            // Group by category in registration order (headings only in the "All" view).
            foreach (var cat in _categories)
            {
                if (cat == AllCategory) continue;
                if (!all && !string.Equals(cat, only, StringComparison.OrdinalIgnoreCase)) continue;
                if (all) _propEntries.Add(new GridEntry { Heading = true, Label = cat });
                foreach (var def in PropRegistry.All)
                {
                    var dc = string.IsNullOrEmpty(def.Category) ? "Props" : def.Category;
                    if (!string.Equals(dc, cat, StringComparison.OrdinalIgnoreCase)) continue;
                    var col = def.Color;
                    col.a = 1f;
                    string mass = def.Mass.ToString(def.Mass < 10f ? "0.#" : "0", CultureInfo.InvariantCulture) + " kg";
                    _propEntries.Add(new GridEntry
                    {
                        Label = string.IsNullOrEmpty(def.DisplayName) ? def.Id : def.DisplayName,
                        Sub = mass,
                        Tooltip = $"{def.DisplayName}  -  {mass}, {def.Material}  ({def.Id})   Click to spawn it where you are looking.",
                        Shape = ShapeIcons.Get(def.Shape),
                        Tint = col,
                        Payload = def,
                    });
                }
            }
        }

        void DrawPropsTab(Rect p)
        {
            if (_propsDirty)
            {
                _propsDirty = false;
                RebuildCategories();
                _propsBuiltFor = -1;
            }

            var listRect = new Rect(p.x, p.y, CategoryListW, p.height);
            int newCat = Win98.ListBox(listRect, _category, _categoryLabels, ref _categoryScroll, 18f);
            if (newCat != _category)
            {
                _category = Mathf.Clamp(newCat, 0, _categories.Count - 1);
                _propScroll = Vector2.zero;
            }
            if (_propsBuiltFor != _category) RebuildPropEntries();

            var gridRect = new Rect(listRect.xMax + 6f, p.y, p.xMax - listRect.xMax - 6f, p.height);
            if (_propEntries.Count == 0)
            {
                Win98.SunkenPanel(gridRect, true);
                Win98.DrawText(gridRect, "No props are registered.", false, TextAnchor.MiddleCenter, Win98.Shadow);
                return;
            }

            var clicked = DrawGrid(gridRect, _propEntries, ref _propScroll, null);
            if (clicked?.Payload is PropDefinition def) SpawnProp(def);
        }

        void SpawnProp(PropDefinition def)
        {
            if (CmodPlayer.Local == null) { SetStatus("There is no player to spawn for."); return; }
            CmodEntity ent = null;
            try { ent = EntityManager.SpawnPropAtCrosshair(def); }
            catch (Exception ex) { CmodLog.Error($"[spawnmenu] spawning {def.Id} failed: {ex.Message}"); }
            SetStatus(ent != null ? $"Spawned {def.DisplayName}." : $"Could not spawn {def.DisplayName}.");
        }

        // =====================================================================================
        // Weapons
        // =====================================================================================

        void RebuildWeaponEntries()
        {
            _weaponEntries.Clear();
            var protos = new List<WeaponBase>();
            foreach (var w in WeaponRegistry.Prototypes) if (w != null && w.Spawnable) protos.Add(w);
            // Stable sort: by category, then slot, then name.
            protos.Sort((a, b) =>
            {
                int c = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
                if (c == 0) c = a.Slot.CompareTo(b.Slot);
                if (c == 0) c = string.Compare(a.PrintName, b.PrintName, StringComparison.OrdinalIgnoreCase);
                return c;
            });
            string lastCat = null;
            foreach (var w in protos)
            {
                if (!string.Equals(lastCat, w.Category, StringComparison.OrdinalIgnoreCase))
                {
                    lastCat = w.Category;
                    _weaponEntries.Add(new GridEntry { Heading = true, Label = w.Category });
                }
                string ammo = w.ClipSize > 0 ? $"clip {w.ClipSize}" : "no ammo";
                _weaponEntries.Add(new GridEntry
                {
                    Label = w.PrintName,
                    Sub = $"slot {w.Slot + 1}",
                    Tooltip = $"{w.PrintName}  -  {w.Category}, slot {w.Slot + 1}, {ammo}  ({w.Id})   Click to equip it.",
                    Icon = WeaponIcon(w.Id),
                    Payload = w.Id,
                });
            }
        }

        static string WeaponIcon(string id)
        {
            id = id ?? "";
            if (id.IndexOf("tool", StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("wrench", StringComparison.OrdinalIgnoreCase) >= 0) return "tool";
            return "gun";
        }

        void DrawWeaponsTab(Rect p)
        {
            if (_weaponsDirty)
            {
                _weaponsDirty = false;
                RebuildWeaponEntries();
            }
            if (_weaponEntries.Count == 0)
            {
                Win98.SunkenPanel(p, true);
                Win98.DrawText(p, "No weapons are registered.", false, TextAnchor.MiddleCenter, Win98.Shadow);
                return;
            }

            var player = CmodPlayer.Local;
            string active = player != null && player.ActiveWeapon != null ? player.ActiveWeapon.Id : null;
            var clicked = DrawGrid(p, _weaponEntries, ref _weaponScroll, active);
            if (clicked?.Payload is string id)
                SetStatus(BaseCommands.GiveAndSelect(id) ? $"Equipped {clicked.Label}." : $"Could not equip {clicked.Label}.");
        }

        // =====================================================================================
        // Shared button grid
        // =====================================================================================

        /// <summary>Draws a scrollable grid of big icon buttons with optional category headings.
        /// The cell whose payload equals `activePayload` is drawn pressed. Returns the clicked entry, or null.</summary>
        GridEntry DrawGrid(Rect area, List<GridEntry> entries, ref Vector2 scroll, object activePayload)
        {
            Win98.SunkenPanel(area, true);
            var inner = Win98.Inset(area, 2);
            float usableW = inner.width - 16f - 2 * CellGap;   // always leave room for the scrollbar
            int cols = Mathf.Max(1, Mathf.FloorToInt((usableW + CellGap) / (CellW + CellGap)));

            // Measure.
            float y = CellGap;
            int col = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var en = entries[i];
                if (en.Heading)
                {
                    if (col != 0) { y += CellH + CellGap; col = 0; }
                    y += HeadingH;
                    continue;
                }
                if (++col == cols) { y += CellH + CellGap; col = 0; }
            }
            if (col != 0) y += CellH + CellGap;
            var content = new Rect(0, 0, inner.width - 16f, y);

            GridEntry clicked = null;
            var e = Event.current;
            scroll = Win98.BeginScroll(inner, scroll, content);
            try
            {
                float viewTop = scroll.y, viewBottom = scroll.y + inner.height;
                y = CellGap;
                col = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    var en = entries[i];
                    if (en.Heading)
                    {
                        if (col != 0) { y += CellH + CellGap; col = 0; }
                        var hr = new Rect(CellGap, y, content.width - 2 * CellGap, HeadingH);
                        if (hr.yMax >= viewTop && hr.y <= viewBottom)
                        {
                            Win98.DrawText(new Rect(hr.x + 2, hr.y + 2, hr.width, 14), en.Label, true, TextAnchor.MiddleLeft, Win98.SelectionBg);
                            Win98.Separator(new Rect(hr.x, hr.y + 17, hr.width, 2));
                        }
                        y += HeadingH;
                        continue;
                    }

                    var cell = new Rect(CellGap + col * (CellW + CellGap), y, CellW, CellH);
                    if (cell.yMax >= viewTop && cell.y <= viewBottom)
                    {
                        bool on = activePayload != null && activePayload.Equals(en.Payload);
                        if (DrawCell(cell, en, on)) clicked = en;
                        if (cell.Contains(e.mousePosition)) _hoverText = en.Tooltip;
                    }
                    if (++col == cols) { y += CellH + CellGap; col = 0; }
                }
            }
            finally
            {
                Win98.EndScroll();
            }
            return clicked;
        }

        static bool DrawCell(Rect cell, GridEntry en, bool on)
        {
            bool clicked = Win98.ToggleButton(cell, "", on);
            if (Event.current.type != EventType.Repaint) return clicked;

            var iconRect = new Rect(Mathf.Round(cell.center.x - 16f), cell.y + 8f, 32f, 32f);
            if (en.Shape != null)
            {
                var old = GUI.color;
                GUI.color = en.Tint;
                GUI.DrawTexture(iconRect, en.Shape, ScaleMode.StretchToFill, true);
                GUI.color = old;
            }
            else
            {
                Win98.Icon(iconRect, en.Icon);
            }

            var labelRect = new Rect(cell.x + 4f, cell.y + 44f, cell.width - 8f, 14f);
            Win98.DrawText(labelRect, Win98.Ellipsize(en.Label, labelRect.width), false, TextAnchor.MiddleCenter, Color.black);
            if (!string.IsNullOrEmpty(en.Sub))
            {
                var subRect = new Rect(cell.x + 4f, cell.y + 59f, cell.width - 8f, 14f);
                Win98.DrawText(subRect, Win98.Ellipsize(en.Sub, subRect.width), false, TextAnchor.MiddleCenter, Win98.Shadow);
            }
            return clicked;
        }

        // =====================================================================================
        // Tools
        // =====================================================================================

        void RebuildTools()
        {
            _tools.Clear();
            _tools.AddRange(ToolRegistry.All);
            // Sort by category, keeping registration order inside each category.
            var order = new Dictionary<ToolMode, int>();
            for (int i = 0; i < _tools.Count; i++) order[_tools[i]] = i;
            var catOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in _tools) if (!catOrder.ContainsKey(t.Category ?? "")) catOrder[t.Category ?? ""] = catOrder.Count;
            _tools.Sort((a, b) =>
            {
                int c = catOrder[a.Category ?? ""].CompareTo(catOrder[b.Category ?? ""]);
                return c != 0 ? c : order[a].CompareTo(order[b]);
            });
        }

        void DrawToolsTab(Rect p)
        {
            if (_toolsDirty)
            {
                _toolsDirty = false;
                RebuildTools();
            }

            var listRect = new Rect(p.x, p.y, ToolListW, p.height);
            DrawToolList(listRect);

            var right = new Rect(listRect.xMax + 6f, p.y, p.xMax - listRect.xMax - 6f, p.height);
            var tool = ToolRegistry.Active;
            if (tool == null)
            {
                Win98.GroupBox(right, "Tool");
                Win98.DrawText(right, "No tools are registered.", false, TextAnchor.MiddleCenter, Win98.Shadow);
                return;
            }
            DrawToolPanel(right, tool);
        }

        void DrawToolList(Rect r)
        {
            const float rowH = 18f;
            Win98.SunkenPanel(r, true);
            var inner = Win98.Inset(r, 2);

            // Rows: a heading per category, then its tools.
            int rows = 0;
            string lastCat = null;
            foreach (var t in _tools)
            {
                if (!string.Equals(lastCat, t.Category, StringComparison.OrdinalIgnoreCase)) { lastCat = t.Category; rows++; }
                rows++;
            }
            var content = new Rect(0, 0, inner.width - 16f, rows * rowH + 4f);

            var active = ToolRegistry.Active;
            ToolMode clicked = null;
            var e = Event.current;
            _toolListScroll = Win98.BeginScroll(inner, _toolListScroll, content);
            try
            {
                float y = 2f;
                lastCat = null;
                foreach (var t in _tools)
                {
                    if (!string.Equals(lastCat, t.Category, StringComparison.OrdinalIgnoreCase))
                    {
                        lastCat = t.Category;
                        var hr = new Rect(2f, y, content.width - 4f, rowH);
                        Win98.Icon(new Rect(hr.x, hr.y + 1f, 16f, 16f), "folder");
                        Win98.DrawText(new Rect(hr.x + 20f, hr.y, hr.width - 20f, rowH), string.IsNullOrEmpty(t.Category) ? "Other" : t.Category,
                            true, TextAnchor.MiddleLeft, Color.black);
                        y += rowH;
                    }
                    var row = new Rect(2f, y, content.width - 4f, rowH);
                    bool sel = t == active;
                    if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
                    {
                        clicked = t;
                        e.Use();
                    }
                    if (row.Contains(e.mousePosition)) _hoverText = $"{t.Name}  -  {t.Description}";
                    if (e.type == EventType.Repaint)
                    {
                        var textRect = new Rect(row.x + 20f, row.y, row.width - 20f, rowH);
                        if (sel)
                        {
                            float w = Mathf.Min(textRect.width, Win98.TextWidth(t.Name) + 6f);
                            Win98.FillRect(new Rect(textRect.x - 2f, row.y + 1f, w, rowH - 2f), Win98.SelectionBg);
                        }
                        Win98.DrawText(new Rect(textRect.x + 1f, textRect.y, textRect.width, rowH), Win98.Ellipsize(t.Name, textRect.width - 4f),
                            false, TextAnchor.MiddleLeft, sel ? Color.white : Color.black);
                    }
                    y += rowH;
                }
            }
            finally
            {
                Win98.EndScroll();
            }

            if (clicked != null) SelectTool(clicked);
        }

        void SelectTool(ToolMode tool)
        {
            ToolRegistry.Active = tool;
            _toolSettingsScroll = Vector2.zero;
            var player = CmodPlayer.Local;
            if (player != null && !player.IsDead && (player.ActiveWeapon == null || player.ActiveWeapon.Id != "weapon_toolgun"))
                BaseCommands.GiveAndSelect("weapon_toolgun");
            SetStatus($"Tool: {tool.Name}");
        }

        void DrawToolPanel(Rect r, ToolMode tool)
        {
            Win98.GroupBox(r, tool.Name);
            var inner = new Rect(r.x + 10f, r.y + 18f, r.width - 20f, r.height - 28f);
            float y = inner.y;

            // Description and instructions (wrapped).
            var desc = tool.Description ?? "";
            float dh = Win98.TextHeight(desc, inner.width);
            Win98.DrawText(new Rect(inner.x, y, inner.width, dh), desc, false, TextAnchor.UpperLeft, Color.black, true);
            y += dh + 4f;
            var instr = tool.Instructions;
            if (!string.IsNullOrEmpty(instr))
            {
                float ih = Win98.TextHeight(instr, inner.width);
                Win98.DrawText(new Rect(inner.x, y, inner.width, ih), instr, false, TextAnchor.UpperLeft, Win98.SelectionBg, true);
                y += ih + 4f;
            }
            Win98.Separator(new Rect(inner.x, y, inner.width, 2f));
            y += 8f;

            // Settings in a scroll view sized by what the tool drew last frame.
            var view = new Rect(inner.x, y, inner.width, inner.yMax - y);
            if (view.height < 20f) return;
            if (_settingsFor != tool)
            {
                _settingsFor = tool;
                _toolSettingsH = view.height;
            }
            float contentW = view.width - 16f;
            var content = new Rect(0, 0, contentW, Mathf.Max(1f, _toolSettingsH));
            _toolSettingsScroll = Win98.BeginScroll(view, _toolSettingsScroll, content);
            float used = 0f;
            try
            {
                used = tool.DrawSettings(new Rect(0, 0, contentW - 4f, Mathf.Max(view.height, _toolSettingsH)));
            }
            catch (Exception ex)
            {
                WindowManager.LogError("tool settings:" + tool.Id, ex);
            }
            finally
            {
                Win98.EndScroll();
            }
            if (Event.current.type == EventType.Repaint)
            {
                if (used <= 0f)
                    Win98.DrawText(view, "This tool has no settings.", false, TextAnchor.UpperLeft, Win98.Shadow);
                _toolSettingsH = Mathf.Max(0f, used) + 8f;
            }
        }

        // =====================================================================================
        // Utilities
        // =====================================================================================

        void DrawUtilitiesTab(Rect p)
        {
            const float groupGap = 8f;
            float colW = Mathf.Floor((p.width - groupGap - 16f) / 2f);
            float contentH = 300f;
            var content = new Rect(0, 0, p.width - 16f, contentH);
            _utilScroll = Win98.BeginScroll(p, _utilScroll, content);
            try
            {
                DrawActionsGroup(new Rect(0, 0, colW, 190f));
                DrawWorldGroup(new Rect(colW + groupGap, 0, colW, 190f));
                DrawPlayerGroup(new Rect(0, 198f, colW * 2 + groupGap, 96f));
            }
            finally
            {
                Win98.EndScroll();
            }
        }

        void DrawActionsGroup(Rect g)
        {
            Win98.GroupBox(g, "Actions");
            float x = g.x + 12f, w = Mathf.Min(170f, g.width - 24f), y = g.y + 20f;

            if (Win98.Button(new Rect(x, y, w, 23f), "Undo  (Z)", UndoManager.Count > 0))
            {
                BaseCommands.DoUndo();
                SetStatus("Undone.");
            }
            Win98.Label(new Rect(x, y + 25f, g.width - 24f, 16f), $"{UndoManager.Count} action(s) can be undone.", false, TextAnchor.MiddleLeft, Win98.Shadow);
            y += 48f;

            int ents = EntityManager.All()?.Count ?? 0;
            if (Win98.Button(new Rect(x, y, w, 23f), "Clean Up Everything", ents > 0))
            {
                MessageBox.Show("Clean Up", $"Remove all {ents} spawned entities? This cannot be undone.",
                    MessageBox.Kind.Question, new[] { "Yes", "No" }, res =>
                    {
                        if (res != 0) return;
                        BaseCommands.DoCleanup();
                        SetStatus("Cleaned up.");
                    });
            }
            Win98.Label(new Rect(x, y + 25f, g.width - 24f, 16f), $"{ents} entities in the world.", false, TextAnchor.MiddleLeft, Win98.Shadow);
            y += 48f;

            if (Win98.Button(new Rect(x, y, w, 23f), "Respawn", CmodPlayer.Local != null))
            {
                CmodPlayer.Local.Respawn();
                SetStatus("Respawned.");
            }
        }

        void DrawWorldGroup(Rect g)
        {
            Win98.GroupBox(g, "World");
            float x = g.x + 12f, w = g.width - 24f, y = g.y + 20f;
            const float labelW = 70f, valueW = 44f, resetW = 50f;
            float sliderW = Mathf.Max(40f, w - labelW - valueW - resetW - 8f);

            var grav = BaseCommands.Gravity;
            if (grav != null)
            {
                Win98.Label(new Rect(x, y + 3f, labelW, 16f), "Gravity:");
                float cur = grav.Float;
                float v = Win98.Slider(new Rect(x + labelW, y, sliderW, 22f), cur, -40f, 0f);
                // Only write when the user moved the slider; never snap a value set from the console.
                if (v != cur)
                {
                    v = Mathf.Round(v * 2f) / 2f;
                    if (!Mathf.Approximately(v, cur)) grav.SetFloat(v);
                }
                Win98.Label(new Rect(x + labelW + sliderW + 4f, y + 3f, valueW, 16f), grav.Float.ToString("0.#", CultureInfo.InvariantCulture));
                if (Win98.Button(new Rect(x + w - resetW, y, resetW, 22f), "Reset")) grav.Value = grav.Default;
                y += 28f;
            }

            var ts = BaseCommands.Timescale;
            if (ts != null)
            {
                Win98.Label(new Rect(x, y + 3f, labelW, 16f), "Timescale:");
                float cur = ts.Float;
                float v = Win98.Slider(new Rect(x + labelW, y, sliderW, 22f), cur, 0.1f, 3f);
                if (v != cur)
                {
                    v = Mathf.Max(0.1f, Mathf.Round(v * 20f) / 20f);
                    if (!Mathf.Approximately(v, cur)) ts.SetFloat(v);
                }
                Win98.Label(new Rect(x + labelW + sliderW + 4f, y + 3f, valueW, 16f), ts.Float.ToString("0.##", CultureInfo.InvariantCulture) + "x");
                if (Win98.Button(new Rect(x + w - resetW, y, resetW, 22f), "Reset")) ts.Value = ts.Default;
                y += 32f;
            }

            Win98.DrawText(new Rect(x, y, w, 60f),
                "Gravity is in m/s² (default -15). Timescale slows down or speeds up the whole game (sv_gravity, host_timescale).",
                false, TextAnchor.UpperLeft, Win98.Shadow, true);
        }

        void DrawPlayerGroup(Rect g)
        {
            Win98.GroupBox(g, "Player");
            float x = g.x + 12f, y = g.y + 20f;
            float colW = Mathf.Floor((g.width - 24f) / 2f);

            var player = CmodPlayer.Local;
            bool noclip = player != null && player.Noclip;
            bool newNoclip = Win98.Checkbox(new Rect(x, y, colW, 18f), noclip, "Noclip (fly through walls)  [V]");
            if (newNoclip != noclip && player != null && !player.IsDead) player.Noclip = newNoclip;

            var god = BaseCommands.God;
            if (god != null)
            {
                bool gv = Win98.Checkbox(new Rect(x + colW, y, colW, 18f), god.Bool, "God mode (no damage)");
                if (gv != god.Bool) god.SetBool(gv);
            }
            y += 24f;

            var toggle = BaseCommands.SpawnMenuToggle;
            if (toggle != null)
            {
                bool tv = Win98.Checkbox(new Rect(x, y, colW * 2, 18f), toggle.Bool, "Q toggles the spawn menu (instead of holding Q)");
                if (tv != toggle.Bool) toggle.SetBool(tv);
            }
            y += 24f;

            var fps = BaseCommands.ShowFps;
            if (fps != null)
            {
                bool fv = Win98.Checkbox(new Rect(x, y, colW * 2, 18f), fps.Bool, "Show FPS in the taskbar");
                if (fv != fps.Bool) fps.SetBool(fv);
            }
        }
    }

    /// <summary>
    /// 32x32 grayscale shape previews for the props grid (cube, sphere, cylinder, capsule, plane), shaded from
    /// the top left with a dark outline. They are drawn tinted with the prop's color (GUI.color).
    /// </summary>
    internal static class ShapeIcons
    {
        const int N = 32;
        static readonly Dictionary<PrimitiveType, Texture2D> s_cache = new Dictionary<PrimitiveType, Texture2D>();

        public static Texture2D Get(PrimitiveType shape)
        {
            if (s_cache.TryGetValue(shape, out var t) && t != null) return t;
            t = Build(shape);
            s_cache[shape] = t;
            return t;
        }

        static Texture2D Build(PrimitiveType shape)
        {
            var px = new Color32[N * N];
            var inside = new bool[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float b = Shade(shape, x + 0.5f, y + 0.5f);   // y measured from the top
                    if (b < 0f) continue;
                    byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(b * 255f), 0, 255);
                    int i = (N - 1 - y) * N + x;                   // textures are stored bottom-up
                    px[i] = new Color32(v, v, v, 255);
                    inside[i] = true;
                }
            }
            // 1px dark outline on the shape's edge.
            var outlined = (Color32[])px.Clone();
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    if (!inside[i]) continue;
                    bool edge = x == 0 || y == 0 || x == N - 1 || y == N - 1 ||
                                !inside[i - 1] || !inside[i + 1] || !inside[i - N] || !inside[i + N];
                    if (edge) outlined[i] = new Color32(24, 24, 24, 255);
                }
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = "cmod_shape_" + shape,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            tex.SetPixels32(outlined);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Brightness 0..1 of the pixel, or -1 when it is outside the shape.</summary>
        static float Shade(PrimitiveType shape, float x, float y)
        {
            switch (shape)
            {
                case PrimitiveType.Sphere:
                {
                    float dx = (x - 16f) / 14f, dy = (y - 16f) / 14f;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > 1f) return -1f;
                    float nz = Mathf.Sqrt(1f - d2);
                    var n = new Vector3(dx, -dy, nz);
                    var l = new Vector3(-0.45f, 0.55f, 0.7f).normalized;
                    float diff = Mathf.Max(0f, Vector3.Dot(n, l));
                    float spec = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(Vector3.Reflect(-l, n), Vector3.forward)), 12f);
                    return Mathf.Clamp01(0.3f + 0.65f * diff + 0.35f * spec);
                }
                case PrimitiveType.Cylinder:
                case PrimitiveType.Capsule:
                {
                    bool capsule = shape == PrimitiveType.Capsule;
                    float rx = capsule ? 8f : 11f;
                    float top = capsule ? 3f : 7f, bottom = capsule ? 29f : 25f;
                    float ry = capsule ? rx : 4f;
                    float dx = (x - 16f) / rx;
                    if (Mathf.Abs(dx) > 1f) return -1f;
                    float side = 0.35f + 0.6f * Mathf.Clamp01(Mathf.Cos((dx + 0.35f) * 1.3f));
                    if (capsule)
                    {
                        float cy1 = top + rx, cy2 = bottom - rx;
                        if (y < cy1) { float dy = (y - cy1) / rx; if (dx * dx + dy * dy > 1f) return -1f; return Mathf.Clamp01(side + 0.1f); }
                        if (y > cy2) { float dy = (y - cy2) / rx; if (dx * dx + dy * dy > 1f) return -1f; return side * 0.85f; }
                        return side;
                    }
                    // Cylinder: an elliptical top cap above a shaded body with a rounded bottom.
                    float ty = (y - top) / ry;
                    if (dx * dx + ty * ty <= 1f) return 0.92f;
                    float by = (y - bottom) / ry;
                    if (y > top && (y <= bottom || dx * dx + by * by <= 1f)) return side;
                    return -1f;
                }
                case PrimitiveType.Plane:
                case PrimitiveType.Quad:
                {
                    // A flat rhombus seen from above at an angle, with a thin front edge.
                    float dx = Mathf.Abs(x - 16f) / 15f, dy = Mathf.Abs(y - 15f) / 7f;
                    if (dx + dy <= 1f) return 0.85f - 0.2f * ((y - 8f) / 14f);
                    float dy2 = Mathf.Abs(y - 17f) / 7f;
                    if (y > 15f && dx + dy2 <= 1f) return 0.45f;
                    return -1f;
                }
                default:
                {
                    // Isometric cube: top (light), left (mid) and right (dark) faces.
                    var p = new Vector2(x, y);
                    var t = new Vector2(16f, 2f);
                    var tl = new Vector2(3f, 9f);
                    var tr = new Vector2(29f, 9f);
                    var m = new Vector2(16f, 16f);
                    var bl = new Vector2(3f, 23f);
                    var br = new Vector2(29f, 23f);
                    var b = new Vector2(16f, 30f);
                    if (InQuad(p, t, tr, m, tl)) return 0.95f;
                    if (InQuad(p, tl, m, b, bl)) return 0.68f;
                    if (InQuad(p, m, tr, br, b)) return 0.45f;
                    return -1f;
                }
            }
        }

        /// <summary>Point in a convex quad (vertices in either winding order).</summary>
        static bool InQuad(Vector2 p, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float s1 = Cross(b - a, p - a), s2 = Cross(c - b, p - b), s3 = Cross(d - c, p - c), s4 = Cross(a - d, p - d);
            bool neg = s1 < 0 || s2 < 0 || s3 < 0 || s4 < 0;
            bool pos = s1 > 0 || s2 > 0 || s3 > 0 || s4 > 0;
            return !(neg && pos);
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
