using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cmod.UI
{
    /// <summary>
    /// Implementation of the WindowManager contract (UI/Contracts.cs).
    ///
    /// Draw order (one OnGUI, GUI.depth 0, scaled by ui_scale):
    ///   normal windows (bottom to top) → taskbar → start menu → modal message boxes → dropdown popup.
    ///
    /// Input routing: at the start of every event the manager decides which single layer is under the
    /// mouse (or holds the mouse capture while a button is down). Every other layer is drawn with a far-away
    /// mouse position, so clicks and hover only ever reach the topmost thing under the cursor. Mouse events
    /// that hit UI are consumed so the HUD and other OnGUI code never see them.
    /// </summary>
    public partial class WindowManager
    {
        // ---- Window bookkeeping -------------------------------------------------------------
        static readonly List<Win98Window> s_windows = new List<Win98Window>();    // registered (Windows())
        static readonly List<Win98Window> s_zOrder = new List<Win98Window>();     // open normal windows, bottom → top
        static readonly List<Win98Window> s_openOrder = new List<Win98Window>();  // open windows in opening order (taskbar)
        static readonly List<Win98Window> s_modals = new List<Win98Window>();     // open modal dialogs, bottom → top
        static readonly List<Win98Window> s_buffer = new List<Win98Window>();     // reusable snapshot for iteration
        static int s_nextId = 0x4000;
        static ConVar s_scaleVar;

        // ---- Frame metrics --------------------------------------------------------------------
        /// <summary>Raised border width around a window.</summary>
        internal const float Border = 3f;
        internal const float TitleHeight = 18f;
        const float CaptionW = 14f, CaptionH = 12f;
        const float GripSize = 12f;

        // ---- Input routing ------------------------------------------------------------------
        internal static readonly object TaskbarLayer = new object();
        internal static readonly object StartMenuLayer = new object();
        static readonly object PopupLayer = new object();
        static readonly object BlockedLayer = new object();   // input swallowed (modal open, or a closing click)

        internal const int TaskbarLayerId = -1, StartMenuLayerId = -2, PopupLayerId = -3;

        static object s_capture;                 // layer that received the current mouse press
        static Win98Window s_dragWin, s_resizeWin;
        static Vector2 s_dragOffset;
        static int s_closedFrame = -1;           // frame a window was last closed (Esc de-duplication)
        static float s_flashUntil;               // modal title flashes when a click is blocked
        static readonly Dictionary<string, float> s_errorTimes = new Dictionary<string, float>();

        /// <summary>Open windows in the order they were opened (taskbar buttons).</summary>
        internal static IReadOnlyList<Win98Window> OpenOrder => s_openOrder;
        /// <summary>The topmost modal dialog, or null.</summary>
        internal static Win98Window TopModal => s_modals.Count > 0 ? s_modals[s_modals.Count - 1] : null;

        // =====================================================================================
        // Setup
        // =====================================================================================

        /// <summary>Creates the UI convars. Called from Ui_Init (idempotent).</summary>
        internal static void CreateConVars()
        {
            s_scaleVar = ConCommands.CreateVar("ui_scale", "1", "Scale of the whole Win98 interface (0.75 - 2.5).", 0.75f, 2.5f, archive: true);
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            if (s_scaleVar == null) CreateConVars();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) ApplyCursor();
        }

        // =====================================================================================
        // Public API (contract)
        // =====================================================================================

        public static partial float Scale()
        {
            if (s_scaleVar == null) return 1f;
            float s = s_scaleVar.Float;
            return s <= 0f ? 1f : Mathf.Clamp(s, 0.75f, 2.5f);
        }

        public static partial void Register(Win98Window w)
        {
            if (w == null || s_windows.Contains(w)) return;
            if (w.WindowId == 0) w.WindowId = ++s_nextId;
            s_windows.Add(w);
        }

        public static partial void Open(Win98Window w)
        {
            if (w == null) return;
            if (s_modals.Contains(w))
            {
                BringToFront(w);
                return;
            }
            if (!s_windows.Contains(w)) Register(w);

            bool wasOpen = w.IsOpen;
            w.IsMinimized = false;
            if (!wasOpen)
            {
                w.IsOpen = true;
                s_zOrder.Add(w);
                s_openOrder.Add(w);
            }
            BringToFront(w);
            if (s_modals.Count == 0) SetFocus(w);

            if (!wasOpen)
            {
                try { w.OnOpened(); }
                catch (Exception ex) { LogError("open:" + w.Title, ex); }
                ClampToScreen(w);
            }
        }

        public static partial void Close(Win98Window w)
        {
            if (w == null || !w.IsOpen) return;
            w.IsOpen = false;
            w.IsMinimized = false;
            s_zOrder.Remove(w);
            s_openOrder.Remove(w);
            s_modals.Remove(w);
            if (s_dragWin == w) s_dragWin = null;
            if (s_resizeWin == w) s_resizeWin = null;
            s_closedFrame = Time.frameCount;
            if (Focused == w)
            {
                Focused = null;
                GUIUtility.keyboardControl = 0;
                FocusTopmost();
            }
            try { w.OnClosed(); }
            catch (Exception ex) { LogError("close:" + w.Title, ex); }
        }

        public static partial void Minimize(Win98Window w)
        {
            if (w == null || !w.IsOpen || w.IsMinimized || s_modals.Contains(w)) return;
            w.IsMinimized = true;
            if (s_dragWin == w) s_dragWin = null;
            if (s_resizeWin == w) s_resizeWin = null;
            if (Focused == w)
            {
                Focused = null;
                GUIUtility.keyboardControl = 0;
                FocusTopmost();
            }
        }

        public static partial void BringToFront(Win98Window w)
        {
            if (w == null) return;
            var list = s_modals.Contains(w) ? s_modals : s_zOrder.Contains(w) ? s_zOrder : null;
            if (list == null || list[list.Count - 1] == w) return;
            list.Remove(w);
            list.Add(w);
        }

        public static partial IReadOnlyList<Win98Window> Windows() => s_windows;

        public static partial bool UIHasFocus()
        {
            if (s_modals.Count > 0 || StartMenu.IsOpen || UIState.AnyCursorRequest) return true;
            for (int i = 0; i < s_zOrder.Count; i++)
            {
                var w = s_zOrder[i];
                if (!w.IsMinimized && w.CapturesCursor) return true;
            }
            return false;
        }

        // =====================================================================================
        // Internal API (MessageBox, Taskbar)
        // =====================================================================================

        /// <summary>Opens a modal dialog above everything. While any modal is open, all other UI is blocked.</summary>
        internal static void OpenModal(Win98Window w)
        {
            if (w == null) return;
            if (w.WindowId == 0) w.WindowId = ++s_nextId;
            if (w.IsOpen && !s_modals.Contains(w)) Close(w);
            StartMenu.Close();
            Win98.ClosePopup();
            s_dragWin = s_resizeWin = null;
            if (!s_modals.Contains(w)) s_modals.Add(w);
            bool wasOpen = w.IsOpen;
            w.IsOpen = true;
            w.IsMinimized = false;
            BringToFront(w);
            SetFocus(w);
            if (!wasOpen)
            {
                try { w.OnOpened(); }
                catch (Exception ex) { LogError("open:" + w.Title, ex); }
            }
        }

        /// <summary>Taskbar button behavior: restore a minimized window, minimize the focused one, otherwise focus it.</summary>
        internal static void TaskbarClick(Win98Window w)
        {
            if (w == null || !w.IsOpen) return;
            if (w.IsMinimized) Open(w);
            else if (w.IsFocused && w.Minimizable) Minimize(w);
            else
            {
                BringToFront(w);
                SetFocus(w);
            }
        }

        static void SetFocus(Win98Window w)
        {
            if (Focused == w) return;
            Focused = w;
            GUIUtility.keyboardControl = 0; // a text field in the previous window must stop receiving keys
        }

        /// <summary>The topmost open, non-minimized, closable window that frees the cursor, or null.</summary>
        static Win98Window TopmostClosable()
        {
            for (int i = s_zOrder.Count - 1; i >= 0; i--)
            {
                var w = s_zOrder[i];
                if (w.IsOpen && !w.IsMinimized && w.Closable && w.CapturesCursor) return w;
            }
            return null;
        }

        static void FocusTopmost()
        {
            if (s_modals.Count > 0) { SetFocus(s_modals[s_modals.Count - 1]); return; }
            for (int i = s_zOrder.Count - 1; i >= 0; i--)
            {
                if (!s_zOrder[i].IsMinimized) { SetFocus(s_zOrder[i]); return; }
            }
            Focused = null;
        }

        /// <summary>Logs an exception at most once every 5 seconds per source, so a broken window can't flood the console.</summary>
        internal static void LogError(string where, Exception ex)
        {
            float now = Time.realtimeSinceStartup;
            if (s_errorTimes.TryGetValue(where, out var last) && now - last < 5f) return;
            s_errorTimes[where] = now;
            CmodLog.Error($"[ui {where}] {ex.GetType().Name}: {ex.Message}");
        }

        // =====================================================================================
        // Update: ticks, keyboard, cursor
        // =====================================================================================

        void Update()
        {
            try
            {
                Taskbar.UpdateStats();
                ReleaseStaleCapture();
                TickWindows();
                HandleKeys();
            }
            catch (Exception ex)
            {
                LogError("update", ex);
            }
            ApplyCursor();
        }

        /// <summary>Locks the cursor for gameplay, frees it while UI has focus. Applied every frame, so
        /// focus changes and the game window regaining focus always end up in the right state.</summary>
        static void ApplyCursor()
        {
            try
            {
                bool ui = UIHasFocus();
                Cursor.lockState = ui ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = ui;
            }
            catch (Exception ex)
            {
                LogError("cursor", ex);
            }
        }

        /// <summary>If the mouse was released outside the game window, OnGUI never sees the MouseUp.
        /// Drop the capture one frame after every button is up.</summary>
        static void ReleaseStaleCapture()
        {
            if (s_capture == null && s_dragWin == null && s_resizeWin == null && !Win98.IsPressing) return;
            bool held = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
            bool justUp = Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1) || Input.GetMouseButtonUp(2);
            if (held || justUp) return;
            s_capture = null;
            s_dragWin = null;
            s_resizeWin = null;
            Win98.ReleasePress();
        }

        static void TickWindows()
        {
            s_buffer.Clear();
            s_buffer.AddRange(s_zOrder);
            s_buffer.AddRange(s_modals);
            for (int i = 0; i < s_buffer.Count; i++)
            {
                var w = s_buffer[i];
                if (!w.IsOpen) continue;
                try { w.Tick(); }
                catch (Exception ex) { LogError("tick:" + w.Title, ex); }
            }
            s_buffer.Clear();
        }

        static void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (Win98.PopupIsOpen) Win98.ClosePopup();
                else if (s_modals.Count > 0) { /* the message box handles Esc in its Tick */ }
                else if (s_closedFrame == Time.frameCount) { /* a window closed itself on Esc in its Tick */ }
                else if (StartMenu.IsOpen) StartMenu.Close();
                else if (Focused != null && Focused.IsOpen && !Focused.IsMinimized && Focused.Closable) Close(Focused);
                else if (Focused == null && TopmostClosable() is Win98Window top) Close(top); // e.g. after a desktop click
                else StartMenu.Toggle();
                return;
            }
            if (StartMenu.IsOpen && s_modals.Count == 0) StartMenu.HandleKeys();
        }

        // =====================================================================================
        // OnGUI
        // =====================================================================================

        void OnGUI()
        {
            GUI.depth = 0;
            var oldMatrix = GUI.matrix;
            try
            {
                DrawEverything();
            }
            catch (Exception ex)
            {
                LogError("ongui", ex);
            }
            finally
            {
                GUI.matrix = oldMatrix;
                Win98.CurrentLayer = 0;
            }
        }

        static bool IsMouseEvent(EventType t) =>
            t == EventType.MouseDown || t == EventType.MouseUp || t == EventType.MouseDrag ||
            t == EventType.ScrollWheel || t == EventType.MouseMove || t == EventType.ContextClick;

        static void DrawEverything()
        {
            Win98.EnsureInit();
            float s = Scale();
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

            var e = Event.current;
            EventType type = e.type;
            bool mouseEvent = IsMouseEvent(type);
            bool uiActive = UIHasFocus();

            // Typing only makes sense while the UI has the cursor.
            if (!uiActive && GUIUtility.keyboardControl != 0) GUIUtility.keyboardControl = 0;

            if (type == EventType.Layout)
            {
                for (int i = 0; i < s_zOrder.Count; i++) ClampToScreen(s_zOrder[i]);
                for (int i = 0; i < s_modals.Count; i++) ClampToScreen(s_modals[i]);
            }

            Vector2 mp = e.mousePosition;
            object target = null;
            if (uiActive)
            {
                if (type == EventType.MouseDown) target = OnMouseDown(HitTest(mp), mp);
                else target = s_capture ?? HitTest(mp);
            }

            // ---- Layers, bottom to top ----
            s_buffer.Clear();
            s_buffer.AddRange(s_zOrder);
            for (int i = 0; i < s_buffer.Count; i++)
            {
                var w = s_buffer[i];
                if (!w.IsOpen || w.IsMinimized) continue;
                DrawWindowLayer(w, target);
            }
            s_buffer.Clear();

            {
                bool redirected = BeginRedirect(TaskbarLayer, target, out var saved);
                Win98.CurrentLayer = TaskbarLayerId;
                try { Taskbar.Draw(target == TaskbarLayer); }
                catch (Exception ex) { LogError("taskbar", ex); }
                EndRedirect(redirected, saved);
            }

            if (StartMenu.IsOpen)
            {
                bool redirected = BeginRedirect(StartMenuLayer, target, out var saved);
                Win98.CurrentLayer = StartMenuLayerId;
                try { StartMenu.Draw(target == StartMenuLayer); }
                catch (Exception ex) { LogError("startmenu", ex); }
                EndRedirect(redirected, saved);
            }

            if (s_modals.Count > 0)
            {
                s_buffer.AddRange(s_modals);
                for (int i = 0; i < s_buffer.Count; i++)
                {
                    var w = s_buffer[i];
                    if (!w.IsOpen) continue;
                    if (w is MessageBoxWindow mb) mb.EnsureLayout();
                    DrawWindowLayer(w, target);
                }
                s_buffer.Clear();
            }

            if (Win98.PopupIsOpen)
            {
                bool redirected = BeginRedirect(PopupLayer, target, out var saved);
                Win98.CurrentLayer = PopupLayerId;
                try { Win98.DrawPopupOverlay(target == PopupLayer); }
                catch (Exception ex) { LogError("popup", ex); }
                EndRedirect(redirected, saved);
            }

            // Mouse events that hit UI never fall through to the HUD or the game.
            if (mouseEvent && target != null && e.type != EventType.Used && e.type != EventType.Ignore) e.Use();

            if (type == EventType.MouseUp)
            {
                s_capture = null;
                s_dragWin = null;
                s_resizeWin = null;
                Win98.ReleasePress();
            }
        }

        /// <summary>Which layer is under the mouse. Order matches the draw order, top first.</summary>
        static object HitTest(Vector2 p)
        {
            if (Win98.PopupIsOpen && Win98.PopupGuiRect().Contains(p)) return PopupLayer;
            if (s_modals.Count > 0)
            {
                for (int i = s_modals.Count - 1; i >= 0; i--)
                    if (s_modals[i].Rect.Contains(p)) return s_modals[i];
                return BlockedLayer;
            }
            if (StartMenu.IsOpen && StartMenu.MenuRect.Contains(p)) return StartMenuLayer;
            if (Taskbar.BarRect.Contains(p)) return TaskbarLayer;
            for (int i = s_zOrder.Count - 1; i >= 0; i--)
            {
                var w = s_zOrder[i];
                if (!w.IsMinimized && w.Rect.Contains(p)) return w;
            }
            return null;
        }

        /// <summary>Focus, z-order and "click outside closes it" rules for a new mouse press.
        /// Returns the layer that should receive the press (BlockedLayer swallows it).</summary>
        static object OnMouseDown(object target, Vector2 p)
        {
            // An open dropdown list closes on any click outside it, and that click goes nowhere.
            if (Win98.PopupIsOpen && target != PopupLayer)
            {
                Win98.ClosePopup();
                s_capture = BlockedLayer;
                return BlockedLayer;
            }
            if (target == BlockedLayer)
            {
                s_flashUntil = Time.unscaledTime + 0.6f; // "ding": flash the modal's title bar
                s_capture = BlockedLayer;
                return BlockedLayer;
            }
            if (StartMenu.IsOpen && target != StartMenuLayer &&
                !(target == TaskbarLayer && Taskbar.StartButtonRect.Contains(p)))
            {
                StartMenu.Close();
                s_capture = BlockedLayer;
                return BlockedLayer;
            }

            // Clicking anywhere ends text editing; a text field under the mouse takes focus again right away.
            GUIUtility.keyboardControl = 0;

            if (target is Win98Window w)
            {
                BringToFront(w);
                SetFocus(w);
            }
            else if (target == null && s_modals.Count == 0)
            {
                Focused = null; // clicked the "desktop"
            }
            s_capture = target;
            return target;
        }

        static bool BeginRedirect(object layer, object target, out Vector2 saved)
        {
            var e = Event.current;
            saved = e.mousePosition;
            if (layer == target) return false;
            e.mousePosition = Win98.FarAway;
            return true;
        }

        static void EndRedirect(bool redirected, Vector2 saved)
        {
            if (redirected) Event.current.mousePosition = saved;
        }

        // =====================================================================================
        // Window frame
        // =====================================================================================

        static void DrawWindowLayer(Win98Window w, object target)
        {
            bool redirected = BeginRedirect(w, target, out var saved);
            Win98.CurrentLayer = w.WindowId;
            try { DrawWindow(w, target == w); }
            catch (Exception ex) { LogError("frame:" + w.Title, ex); }
            EndRedirect(redirected, saved);
        }

        static Rect CloseButtonRect(Rect local) =>
            new Rect(local.width - Border - 2 - CaptionW, Border + (TitleHeight - CaptionH) / 2f, CaptionW, CaptionH);

        static Rect MinButtonRect(Rect local, bool closable)
        {
            var c = CloseButtonRect(local);
            return closable ? new Rect(c.x - 2 - CaptionW, c.y, CaptionW, CaptionH) : c;
        }

        /// <summary>Client area in window-local coordinates (below the title bar, inside the border).</summary>
        internal static Rect ClientRect(Rect windowRect) =>
            new Rect(Border + 1, Border + TitleHeight + 1, windowRect.width - 2 * (Border + 1), windowRect.height - (Border + TitleHeight + 1) - (Border + 1));

        static void DrawWindow(Win98Window w, bool isTarget)
        {
            var e = Event.current;
            Vector2 mp = e.mousePosition;
            var local = new Rect(0, 0, w.Rect.width, w.Rect.height);

            // ---- Move / resize (handled in screen space before the window contents) ----
            if (isTarget)
            {
                var r = w.Rect;
                var title = new Rect(r.x + Border, r.y + Border, r.width - 2 * Border, TitleHeight);
                float buttonsLeft = r.x + (w.Minimizable ? MinButtonRect(local, w.Closable).x : w.Closable ? CloseButtonRect(local).x : local.width);
                var grip = new Rect(r.xMax - Border - GripSize, r.yMax - Border - GripSize, GripSize + Border, GripSize + Border);
                switch (e.type)
                {
                    case EventType.MouseDown:
                        if (e.button != 0) break;
                        if (w.Resizable && grip.Contains(mp))
                        {
                            s_resizeWin = w;
                            s_dragOffset = new Vector2(r.xMax, r.yMax) - mp;
                            e.Use();
                        }
                        else if (title.Contains(mp) && mp.x < buttonsLeft - 1)
                        {
                            s_dragWin = w;
                            s_dragOffset = mp - r.position;
                            e.Use();
                        }
                        break;
                    case EventType.MouseDrag:
                        if (s_dragWin == w)
                        {
                            var p = mp - s_dragOffset;
                            w.Rect.x = Mathf.Round(p.x);
                            w.Rect.y = Mathf.Round(p.y);
                            ClampToScreen(w);
                            e.Use();
                        }
                        else if (s_resizeWin == w)
                        {
                            var corner = mp + s_dragOffset;
                            w.Rect.width = Mathf.Round(Mathf.Max(w.MinSize.x, corner.x - w.Rect.x));
                            w.Rect.height = Mathf.Round(Mathf.Max(w.MinSize.y, corner.y - w.Rect.y));
                            ClampToScreen(w);
                            e.Use();
                        }
                        break;
                    case EventType.MouseUp:
                        if (s_dragWin == w || s_resizeWin == w)
                        {
                            s_dragWin = null;
                            s_resizeWin = null;
                            e.Use();
                        }
                        break;
                }
            }

            // ---- Frame ----
            var rect = w.Rect;
            local = new Rect(0, 0, rect.width, rect.height);
            GUI.BeginGroup(rect);
            try
            {
                bool isModal = s_modals.Contains(w);
                bool flashing = isModal && w == TopModal && Time.unscaledTime < s_flashUntil &&
                                Mathf.FloorToInt(Time.unscaledTime * 10f) % 2 == 0;
                bool active = w.IsFocused && !flashing;

                if (e.type == EventType.Repaint)
                {
                    Win98.RaisedPanel(local);
                    var bar = new Rect(Border, Border, local.width - 2 * Border, TitleHeight);
                    if (active) Win98.Gradient(bar, Win98.TitleActive, Win98.TitleActiveEnd);
                    else Win98.Gradient(bar, Win98.TitleInactive, Win98.TitleInactiveEnd);
                    Win98.Icon(new Rect(bar.x + 2, bar.y + 1, 16, 16), w.IconName);

                    float textRight = w.Minimizable ? MinButtonRect(local, w.Closable).x : w.Closable ? CloseButtonRect(local).x : bar.xMax;
                    var tr = new Rect(bar.x + 21, bar.y, textRight - bar.x - 24, bar.height);
                    if (tr.width > 4)
                        Win98.DrawText(tr, Win98.Ellipsize(w.Title, tr.width, true), true, TextAnchor.MiddleLeft,
                            active ? Color.white : Win98.Face);
                }

                if (w.Minimizable && !isModal && Win98.CaptionButton(MinButtonRect(local, w.Closable), '_')) Minimize(w);
                if (w.Closable && Win98.CaptionButton(CloseButtonRect(local), 'X')) Close(w);

                if (w.IsOpen && !w.IsMinimized)
                {
                    var client = ClientRect(rect);
                    try { w.DrawContents(client); }
                    catch (Exception ex) { LogError("draw:" + w.Title, ex); }

                    if (w.Resizable && e.type == EventType.Repaint)
                    {
                        var g = new Rect(local.width - Border - GripSize, local.height - Border - GripSize, GripSize, GripSize);
                        Win98.DrawGrip(g);
                    }
                }
            }
            finally
            {
                GUI.EndGroup();
            }
        }

        /// <summary>Keeps a window fully on screen (above the taskbar), and inside its size limits.</summary>
        static void ClampToScreen(Win98Window w)
        {
            var screen = ScreenSizeScaled;
            float maxH = screen.y - TaskbarHeight;
            if (w.Resizable)
            {
                w.Rect.width = Mathf.Clamp(w.Rect.width, w.MinSize.x, Mathf.Max(w.MinSize.x, screen.x));
                w.Rect.height = Mathf.Clamp(w.Rect.height, w.MinSize.y, Mathf.Max(w.MinSize.y, maxH));
            }
            w.Rect.x = Mathf.Round(Mathf.Clamp(w.Rect.x, 0f, Mathf.Max(0f, screen.x - w.Rect.width)));
            w.Rect.y = Mathf.Round(Mathf.Clamp(w.Rect.y, 0f, Mathf.Max(0f, maxH - w.Rect.height)));
        }
    }
}
