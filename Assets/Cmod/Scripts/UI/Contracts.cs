using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cmod.UI
{
    /// <summary>
    /// Windows 98-style IMGUI widget kit. Contract only: implemented in UI/Win98.cs.
    /// Every widget takes a Rect in the current GUI space (inside a window, that is window-local coords).
    /// Style: face #C0C0C0, raised 3D bevels (white/light top-left, dark gray/black bottom-right),
    /// sunken white fields, navy (#000080 to #1084D0) title bars, MS Sans Serif-like small text (11px).
    /// All textures are generated in code, so there are no asset dependencies.
    /// </summary>
    public static partial class Win98
    {
        // Palette (classic Windows 98 "Windows Standard" scheme)
        public static readonly Color Face = new Color32(192, 192, 192, 255);
        public static readonly Color Highlight = new Color32(255, 255, 255, 255);
        public static readonly Color Light = new Color32(223, 223, 223, 255);
        public static readonly Color Shadow = new Color32(128, 128, 128, 255);
        public static readonly Color DarkShadow = new Color32(0, 0, 0, 255);
        public static readonly Color TitleActive = new Color32(0, 0, 128, 255);
        public static readonly Color TitleActiveEnd = new Color32(16, 132, 208, 255);
        public static readonly Color TitleInactive = new Color32(128, 128, 128, 255);
        public static readonly Color TitleInactiveEnd = new Color32(181, 181, 181, 255);
        public static readonly Color SelectionBg = new Color32(0, 0, 128, 255);
        public static readonly Color Desktop = new Color32(0, 128, 128, 255);
        public static readonly Color TooltipBg = new Color32(255, 255, 225, 255);
        public const int FontSize = 11;

        /// <summary>Creates textures and GUIStyles. Must be called from OnGUI (GUI.skin is only valid there). Idempotent.</summary>
        public static partial void EnsureInit();

        // ---- Frames --------------------------------------------------------------------------
        /// <summary>Flat gray face with a raised 2px bevel (buttons, window frames).</summary>
        public static partial void RaisedPanel(Rect r);
        /// <summary>Sunken 2px bevel; white inside when `white`, otherwise face color (text fields, lists).</summary>
        public static partial void SunkenPanel(Rect r, bool white = true);
        /// <summary>Etched group box with a title in the top border, like a Win98 frame.</summary>
        public static partial void GroupBox(Rect r, string title);
        /// <summary>1px etched horizontal separator line.</summary>
        public static partial void Separator(Rect r);
        public static partial void FillRect(Rect r, Color c);
        /// <summary>Horizontal title-bar gradient.</summary>
        public static partial void Gradient(Rect r, Color left, Color right);

        // ---- Widgets ------------------------------------------------------------------------
        public static partial void Label(Rect r, string text, bool bold = false, TextAnchor align = TextAnchor.MiddleLeft, Color? color = null);
        /// <summary>Push button. When pressed, the bevel inverts and the text shifts 1px down and right. Returns true on click.</summary>
        public static partial bool Button(Rect r, string text, bool enabled = true);
        /// <summary>A button that stays pressed while `on` (toolbar or taskbar style). Returns true on click.</summary>
        public static partial bool ToggleButton(Rect r, string text, bool on);
        /// <summary>13x13 sunken white checkbox with a check mark, label on the right. Returns the new value.</summary>
        public static partial bool Checkbox(Rect r, bool value, string label);
        /// <summary>Radio button (round). Returns true if clicked (caller sets selection).</summary>
        public static partial bool Radio(Rect r, bool selected, string label);
        /// <summary>Horizontal trackbar with a Win98-style thumb. Returns the new value.</summary>
        public static partial float Slider(Rect r, float value, float min, float max);
        /// <summary>Sunken white single-line text field. `controlName` lets callers focus it with GUI.FocusControl.</summary>
        public static partial string TextField(Rect r, string text, string controlName = null);
        /// <summary>Like TextField, but shows `mask` characters instead of the text (tokens, passwords).</summary>
        public static partial string PasswordField(Rect r, string text, char mask = '*', string controlName = null);
        /// <summary>Multi-line sunken white text area (read-write). Returns the new text.</summary>
        public static partial string TextArea(Rect r, string text);
        /// <summary>Dropdown (combo box) with a ▼ button. Shows a popup list on click. Returns the new index.</summary>
        public static partial int Dropdown(Rect r, int selected, string[] options, string id);
        /// <summary>Row of tabs. The selected tab is raised and joined to the content panel below.
        /// Draws the content panel too (r is the whole tab control). Returns the new selected index.
        /// Content area = TabContentRect(r).</summary>
        public static partial int Tabs(Rect r, int selected, string[] tabs);
        public static partial Rect TabContentRect(Rect tabControl);
        /// <summary>Sunken white list box with selectable rows (navy selection). Returns the new selected index.
        /// `scroll` is the caller's scroll state.</summary>
        public static partial int ListBox(Rect r, int selected, IList<string> items, ref Vector2 scroll, float rowHeight = 16f);
        /// <summary>Segmented navy-block progress bar, 0..1.</summary>
        public static partial void ProgressBar(Rect r, float t01);
        /// <summary>Scroll view with Win98 scrollbars (gray track, beveled thumb and arrow buttons).</summary>
        public static partial Vector2 BeginScroll(Rect r, Vector2 scroll, Rect content);
        public static partial void EndScroll();
        /// <summary>Small beveled square icon button with a glyph ('X', '_', '□', '?').</summary>
        public static partial bool CaptionButton(Rect r, char glyph);
        /// <summary>Draws a tiny procedural 16x16 icon (Win98-ish). Known names: "computer", "folder", "box",
        /// "gun", "tool", "console", "settings", "info", "warning", "error", "question", "cmod", "help".
        /// Unknown names draw a generic window icon.</summary>
        public static partial void Icon(Rect r, string name);

        /// <summary>Text style for the Win98 font: 11px, black, no wrap. Use for measuring with CalcSize.</summary>
        public static partial GUIStyle TextStyle(bool bold = false);
    }

    /// <summary>
    /// Base class for Win98 windows. Subclass, register with WindowManager.Register, then Open().
    /// The WindowManager draws the frame (raised 3D border, gradient title bar with icon + title,
    /// [_][X] caption buttons), handles dragging by the title bar, focus/z-order and the taskbar button.
    /// </summary>
    public abstract class Win98Window
    {
        public string Title = "Window";
        public string IconName = "cmod";
        public Rect Rect = new Rect(100, 100, 400, 300);   // screen coords, includes the frame
        public bool Closable = true;
        public bool Minimizable = true;
        public bool Resizable = false;
        public Vector2 MinSize = new Vector2(160, 100);
        /// <summary>While open, this window frees the mouse cursor and blocks gameplay input.</summary>
        public bool CapturesCursor = true;
        /// <summary>Shown as a button on the taskbar while open.</summary>
        public bool ShowInTaskbar = true;

        public bool IsOpen { get; internal set; }
        public bool IsMinimized { get; internal set; }
        public bool IsFocused => WindowManager.Focused == this;
        /// <summary>Stable unique id for GUI.Window (assigned by WindowManager.Register).</summary>
        public int WindowId { get; internal set; }

        /// <summary>Draw the client area. `client` is in window-local coordinates (below the title bar, inside the border).</summary>
        public abstract void DrawContents(Rect client);
        public virtual void OnOpened() { }
        public virtual void OnClosed() { }
        /// <summary>Called every frame (Update) while open, for key handling like Enter or Escape.</summary>
        public virtual void Tick() { }

        public void Open() => WindowManager.Open(this);
        public void Close() => WindowManager.Close(this);
        public void Toggle() { if (IsOpen && !IsMinimized) Close(); else Open(); }
        public void CenterOnScreen()
        {
            var s = WindowManager.ScreenSizeScaled;
            Rect.x = Mathf.Round((s.x - Rect.width) / 2f);
            Rect.y = Mathf.Round((s.y - WindowManager.TaskbarHeight - Rect.height) / 2f);
        }
    }

    /// <summary>
    /// Implemented in UI/WindowManager.cs (a MonoBehaviour that CmodGame adds).
    /// Owns all windows, draws them and the taskbar, and decides cursor lock.
    /// </summary>
    public partial class WindowManager : MonoBehaviour
    {
        public static WindowManager Instance { get; internal set; }
        public const float TaskbarHeight = 28f;

        /// <summary>UI scale (convar "ui_scale", default 1). Everything is drawn with GUI.matrix scaled by this.</summary>
        public static partial float Scale();
        /// <summary>Screen size divided by the UI scale: the coordinate space windows live in.</summary>
        public static Vector2 ScreenSizeScaled => new Vector2(Screen.width / Mathf.Max(0.01f, Scale()), Screen.height / Mathf.Max(0.01f, Scale()));

        public static Win98Window Focused { get; internal set; }

        public static partial void Register(Win98Window w);
        public static partial void Open(Win98Window w);   // un-minimizes, brings to front, focuses
        public static partial void Close(Win98Window w);
        public static partial void Minimize(Win98Window w);
        public static partial void BringToFront(Win98Window w);
        public static partial IReadOnlyList<Win98Window> Windows();

        /// <summary>True when an open, non-minimized window has CapturesCursor, or the start menu is open,
        /// or something else asked via UIState.RequestCursor. Gameplay input is ignored while this is true.</summary>
        public static partial bool UIHasFocus();
    }

    /// <summary>Start menu entries, which the taskbar's Start button shows. Implemented by WindowManager.</summary>
    public static partial class StartMenu
    {
        /// <summary>Adds an entry. Items are sorted by (group, order). Groups are separated by a line.
        /// Group 0 = main programs, 50 = settings, 100 = power (Shut Down...).</summary>
        public static partial void AddItem(string label, string iconName, Action onClick, int group = 0, int order = 0);
        public static partial void RemoveItem(string label);
    }

    /// <summary>Classic Win98 modal message box (icon, text, buttons). Implemented in UI/MessageBox.cs.</summary>
    public static partial class MessageBox
    {
        public enum Kind { Info, Warning, Error, Question }
        /// <summary>Shows a centered modal dialog. buttons e.g. {"OK"} or {"Yes","No"}. onResult gets the clicked button index (-1 if closed with X).</summary>
        public static partial void Show(string title, string text, Kind kind, string[] buttons, Action<int> onResult = null);
        public static void Info(string title, string text) => Show(title, text, Kind.Info, new[] { "OK" });
        public static void Error(string title, string text) => Show(title, text, Kind.Error, new[] { "OK" });
    }

    /// <summary>Extra cursor requests from code that isn't a window (for example, holding C for a context menu).</summary>
    public static class UIState
    {
        static readonly HashSet<object> s_requests = new HashSet<object>();
        public static void RequestCursor(object owner) => s_requests.Add(owner);
        public static void ReleaseCursor(object owner) => s_requests.Remove(owner);
        public static bool AnyCursorRequest => s_requests.Count > 0;

        /// <summary>True while a TextField has keyboard focus. Gameplay keybinds must ignore keys then.</summary>
        public static bool TypingInTextField => GUIUtility.keyboardControl != 0;
    }
}
