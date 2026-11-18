using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Cmod.UI
{
    /// <summary>
    /// The developer console (` key). Shows CmodLog in a sunken white scroll area, colored by level and
    /// word-wrapped to the window width. It sticks to the bottom as lines arrive unless you scrolled up.
    /// The input line runs console commands: Enter or "Submit" runs it, Up/Down walk the history (saved
    /// between sessions), Tab autocompletes command and convar names (press again to cycle).
    /// </summary>
    public sealed class ConsoleWindow : Win98Window
    {
        const string InputName = "cmod_console_input";
        const float RowH = 14f;
        const float Pad = 4f;
        const int MaxRows = 4000;
        const int MaxHistory = 50;
        const int MaxListedCompletions = 40;
        const string HistoryPrefsKey = "console_history";

        static readonly Color InfoColor = Color.black;
        static readonly Color WarnColor = new Color32(128, 96, 0, 255);
        static readonly Color ErrorColor = new Color32(192, 0, 0, 255);
        static readonly Color EchoColor = new Color32(0, 0, 128, 255);

        static readonly char[] s_breakChars = { '/', '\\', '.', ',' };

        struct Row
        {
            public string Text;
            public Color Color;
        }

        // ---- Log view ----
        readonly List<Row> _rows = new List<Row>();
        readonly List<LogLine> _pending = new List<LogLine>();   // lines added since the last draw (wrapped in OnGUI)
        readonly List<LogLine> _pendingSwap = new List<LogLine>();
        readonly object _pendingLock = new object();
        readonly StringBuilder _sb = new StringBuilder();
        bool _needsRebuild = true;
        float _wrapWidth = -1f;
        Vector2 _scroll;
        bool _stickToBottom = true;

        // ---- Input line ----
        string _input = "";
        readonly List<string> _history = new List<string>();
        int _historyIndex = -1;          // -1 = editing a fresh line
        string _historyDraft = "";       // what was typed before walking the history
        bool _focusInput;
        bool _moveCursorEnd;

        // ---- Tab completion ----
        List<string> _completions;
        int _completionIndex = -1;
        string _completionResult;        // the input text the last Tab produced (to detect "Tab again")

        /// <summary>True while the console's input line has keyboard focus (updated every OnGUI pass).
        /// Also requires the window to be open, not minimized and focused at read time, so a stale value
        /// from the last drawn frame (e.g. after minimizing) never leaks out.</summary>
        public bool InputHasFocus
        {
            get => _inputHasFocus && IsOpen && !IsMinimized && IsFocused;
            private set => _inputHasFocus = value;
        }
        bool _inputHasFocus;

        public ConsoleWindow()
        {
            Title = "Console";
            IconName = "console";
            Rect = new Rect(40, 40, 600, 400);
            Resizable = true;
            MinSize = new Vector2(320, 200);
            CmodLog.LineAdded += OnLineAdded;
            LoadHistory();
        }

        // =====================================================================================
        // Window lifecycle
        // =====================================================================================

        public override void OnOpened()
        {
            _focusInput = true;
            _stickToBottom = true;
        }

        public override void OnClosed()
        {
            InputHasFocus = false;
        }

        /// <summary>Empties the view (the "clear" command clears CmodLog too).</summary>
        public void ClearView()
        {
            lock (_pendingLock) _pending.Clear();
            _rows.Clear();
            _scroll = Vector2.zero;
            _stickToBottom = true;
        }

        void OnLineAdded(LogLine line)
        {
            // May be called from anywhere (even another thread), so only queue it here.
            lock (_pendingLock)
            {
                // While the console is closed nothing drains the queue. Once it holds more than CmodLog
                // keeps anyway, drop it and rebuild from CmodLog.Lines on the next draw instead.
                if (_pending.Count >= CmodLog.MaxLines)
                {
                    _pending.Clear();
                    _needsRebuild = true;
                    return;
                }
                _pending.Add(line);
            }
        }

        // =====================================================================================
        // Drawing
        // =====================================================================================

        public override void DrawContents(Rect c)
        {
            var e = Event.current;
            const float inputH = 21f, buttonW = 75f, buttonH = 23f;

            var logRect = new Rect(c.x + Pad, c.y + Pad, c.width - 2 * Pad, c.height - 2 * Pad - buttonH - Pad);
            var buttonRect = new Rect(c.xMax - Pad - buttonW, c.yMax - Pad - buttonH, buttonW, buttonH);
            var inputRect = new Rect(c.x + Pad, buttonRect.y + 1, buttonRect.x - c.x - 2 * Pad - 2, inputH);

            // Clicking the log puts the caret back in the input line, like the Source console.
            if (e.type == EventType.MouseDown && e.button == 0 && logRect.Contains(e.mousePosition)) _focusInput = true;

            DrawLog(logRect);

            HandleInputKeys(e);

            string typed = Win98.TextField(inputRect, _input, InputName);
            if (typed != _input)
            {
                // The user edited the line: drop the completion cycle and history position.
                _input = typed.Replace("`", "");
                _completions = null;
                _completionResult = null;
                _historyIndex = -1;
            }

            if (_focusInput && IsFocused)
            {
                GUI.FocusControl(InputName);
                _focusInput = false;
                _moveCursorEnd = true;
            }
            if (_moveCursorEnd && GUIUtility.keyboardControl != 0 && GUI.GetNameOfFocusedControl() == InputName)
            {
                MoveCaretToEnd();
                _moveCursorEnd = false;
            }

            InputHasFocus = IsFocused && GUI.GetNameOfFocusedControl() == InputName;

            if (Win98.Button(buttonRect, "Submit"))
            {
                Submit();
                _focusInput = true;
            }
        }

        void DrawLog(Rect area)
        {
            Win98.SunkenPanel(area, true);
            var inner = Win98.Inset(area, 2);
            const float scrollbarW = 16f;
            float wrapW = Mathf.Max(40f, inner.width - scrollbarW - 6f);

            if (_needsRebuild || Mathf.Abs(wrapW - _wrapWidth) > 0.5f)
            {
                _wrapWidth = wrapW;
                Rebuild();
            }
            else
            {
                AppendPending();
            }

            float contentH = _rows.Count * RowH + 4f;
            var content = new Rect(0, 0, inner.width - scrollbarW, contentH);
            float maxY = Mathf.Max(0f, contentH - inner.height);
            if (_stickToBottom) _scroll.y = maxY;

            _scroll = Win98.BeginScroll(inner, _scroll, content);
            if (Event.current.type == EventType.Repaint && _rows.Count > 0)
            {
                int first = Mathf.Max(0, Mathf.FloorToInt((_scroll.y - 2f) / RowH));
                int last = Mathf.Min(_rows.Count - 1, Mathf.CeilToInt((_scroll.y + inner.height) / RowH));
                for (int i = first; i <= last; i++)
                {
                    var row = _rows[i];
                    Win98.DrawText(new Rect(3f, 2f + i * RowH, _wrapWidth + 4f, RowH), row.Text, false, TextAnchor.MiddleLeft, row.Color);
                }
            }
            Win98.EndScroll();

            // Follow new lines only while the view is at the bottom.
            _stickToBottom = _scroll.y >= maxY - 2f;
        }

        // =====================================================================================
        // Wrapping
        // =====================================================================================

        void Rebuild()
        {
            _needsRebuild = false;
            lock (_pendingLock) _pending.Clear(); // everything pending is already in CmodLog.Lines
            _rows.Clear();
            var lines = CmodLog.Lines;
            for (int i = 0; i < lines.Count; i++) AddWrapped(lines[i]);
            TrimRows();
        }

        void AppendPending()
        {
            lock (_pendingLock)
            {
                if (_pending.Count == 0) return;
                _pendingSwap.AddRange(_pending);
                _pending.Clear();
            }
            for (int i = 0; i < _pendingSwap.Count; i++) AddWrapped(_pendingSwap[i]);
            _pendingSwap.Clear();
            TrimRows();
        }

        void TrimRows()
        {
            int extra = _rows.Count - MaxRows;
            if (extra <= 0) return;
            _rows.RemoveRange(0, extra);
            _scroll.y = Mathf.Max(0f, _scroll.y - extra * RowH);
        }

        static Color ColorFor(LogLine line)
        {
            switch (line.Level)
            {
                case LogLevel.Error: return ErrorColor;
                case LogLevel.Warning: return WarnColor;
                default: return line.Text.StartsWith("] ", StringComparison.Ordinal) ? EchoColor : InfoColor;
            }
        }

        /// <summary>Splits a log line on newlines and word-wraps each part to the view width.</summary>
        void AddWrapped(LogLine line)
        {
            var color = ColorFor(line);
            var text = line.Text ?? "";
            if (text.IndexOf('\t') >= 0) text = text.Replace("\t", "    ");
            if (text.IndexOf('\r') >= 0) text = text.Replace("\r", "");

            int start = 0;
            while (true)
            {
                int nl = text.IndexOf('\n', start);
                string part = nl < 0 ? text.Substring(start) : text.Substring(start, nl - start);
                WrapPart(part, color);
                if (nl < 0) break;
                start = nl + 1;
            }
        }

        void WrapPart(string part, Color color)
        {
            if (part.Length == 0 || Win98.TextWidth(part) <= _wrapWidth)
            {
                _rows.Add(new Row { Text = part, Color = color });
                return;
            }

            // Leading indentation is kept on continuation rows so wrapped help lists stay readable.
            int indentLen = 0;
            while (indentLen < part.Length && part[indentLen] == ' ') indentLen++;
            string indent = indentLen > 0 && indentLen < 12 ? part.Substring(0, indentLen) + "  " : "  ";

            string rest = part;
            bool firstRow = true;
            int guard = 0;
            while (rest.Length > 0 && guard++ < 1000)
            {
                string prefix = firstRow ? "" : indent;
                float avail = _wrapWidth - (firstRow ? 0f : Win98.TextWidth(prefix));
                if (Win98.TextWidth(rest) <= avail)
                {
                    _rows.Add(new Row { Text = prefix + rest, Color = color });
                    break;
                }

                // Longest prefix of `rest` that fits (binary search on the length).
                int lo = 1, hi = rest.Length, fit = 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (Win98.TextWidth(rest.Substring(0, mid)) <= avail) { fit = mid; lo = mid + 1; }
                    else hi = mid - 1;
                }
                // Prefer breaking after a space (or a path separator) when one is reasonably close.
                int brk = fit;
                int space = rest.LastIndexOf(' ', Mathf.Max(0, fit - 1), fit);
                if (space > fit / 3) brk = space + 1;
                else
                {
                    int slash = rest.LastIndexOfAny(s_breakChars, Mathf.Max(0, fit - 1), fit);
                    if (slash > fit / 2) brk = slash + 1;
                }

                _rows.Add(new Row { Text = prefix + rest.Substring(0, brk).TrimEnd(), Color = color });
                rest = rest.Substring(brk).TrimStart(' ');
                firstRow = false;
            }
        }

        // =====================================================================================
        // Input line
        // =====================================================================================

        /// <summary>Intercepts Enter, Up/Down and Tab before the text field sees them (it would otherwise
        /// move the caret or the keyboard focus), and swallows the ` character the toggle key types.</summary>
        void HandleInputKeys(Event e)
        {
            if (e.type != EventType.KeyDown) return;
            if (!IsFocused || GUI.GetNameOfFocusedControl() != InputName) return;

            switch (e.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Submit();
                    e.Use();
                    return;
                case KeyCode.UpArrow:
                    HistoryStep(-1);
                    e.Use();
                    return;
                case KeyCode.DownArrow:
                    HistoryStep(+1);
                    e.Use();
                    return;
                case KeyCode.Tab:
                    Complete();
                    e.Use();
                    return;
                case KeyCode.BackQuote:
                    e.Use(); // Keybinds closes the console
                    return;
            }
            // Character events arrive separately from the key events.
            if (e.character == '\t' || e.character == '`' || e.character == '\n' || e.character == '\r') e.Use();
        }

        void MoveCaretToEnd()
        {
            var te = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            if (te == null) return;
            te.text = _input;
            te.MoveTextEnd();
        }

        void SetInput(string text)
        {
            _input = text ?? "";
            _moveCursorEnd = true;
        }

        void Submit()
        {
            string line = (_input ?? "").Trim();
            _input = "";
            _historyIndex = -1;
            _historyDraft = "";
            _completions = null;
            _completionResult = null;
            _stickToBottom = true;
            _moveCursorEnd = true;
            if (line.Length == 0) return;

            if (_history.Count == 0 || _history[_history.Count - 1] != line)
            {
                _history.Add(line);
                if (_history.Count > MaxHistory) _history.RemoveRange(0, _history.Count - MaxHistory);
                SaveHistory();
            }

            CmodLog.Info("] " + line);
            ConCommands.Execute(line);
        }

        /// <summary>Up (-1) goes back in the history, Down (+1) forward; past the newest entry restores the draft.</summary>
        void HistoryStep(int dir)
        {
            if (_history.Count == 0) return;
            if (dir < 0)
            {
                if (_historyIndex == -1)
                {
                    _historyDraft = _input;
                    _historyIndex = _history.Count - 1;
                }
                else if (_historyIndex > 0)
                {
                    _historyIndex--;
                }
                SetInput(_history[_historyIndex]);
            }
            else
            {
                if (_historyIndex == -1) return;
                _historyIndex++;
                if (_historyIndex >= _history.Count)
                {
                    _historyIndex = -1;
                    SetInput(_historyDraft);
                }
                else
                {
                    SetInput(_history[_historyIndex]);
                }
            }
            _completions = null;
        }

        /// <summary>Tab: completes the command or convar name being typed (the text after the last ';').
        /// One match fills it in; several fill in the common prefix and list them; Tab again cycles.</summary>
        void Complete()
        {
            string text = _input ?? "";

            // Tab again right after a completion: cycle through the candidates.
            if (_completions != null && _completions.Count > 1 && text == _completionResult)
            {
                _completionIndex = (_completionIndex + 1) % _completions.Count;
                string head = HeadBeforeStatement(text);
                _completionResult = head + _completions[_completionIndex] + " ";
                SetInput(_completionResult);
                return;
            }

            string before = HeadBeforeStatement(text);
            string statement = text.Substring(before.Length);
            string trimmed = statement.TrimStart();
            string lead = statement.Substring(0, statement.Length - trimmed.Length);
            if (trimmed.IndexOf(' ') >= 0) return; // only the command name is completed
            string prefixHead = before + lead;

            var raw = ConCommands.Complete(trimmed);
            var list = new List<string>(raw.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in raw) if (seen.Add(n)) list.Add(n); // a name can be both a command and a convar

            if (list.Count == 0)
            {
                _completions = null;
                return;
            }
            if (list.Count == 1)
            {
                _completions = null;
                SetInput(prefixHead + list[0] + " ");
                return;
            }

            // Several matches: list them and fill in the longest common prefix.
            _sb.Clear();
            _sb.Append("] ").Append(trimmed).Append("...  (").Append(list.Count).Append(" matches, Tab cycles)");
            int shown = Mathf.Min(list.Count, MaxListedCompletions);
            for (int i = 0; i < shown; i++)
            {
                _sb.Append("\n  ").Append(list[i]);
                var cv = ConCommands.GetVar(list[i]);
                if (cv != null) _sb.Append(" = ").Append(cv.Value);
                else
                {
                    var cmd = ConCommands.GetCommand(list[i]);
                    if (cmd != null && !string.IsNullOrEmpty(cmd.Help)) _sb.Append("  - ").Append(Shorten(cmd.Help, 60));
                }
            }
            if (list.Count > shown) _sb.Append("\n  ... and ").Append(list.Count - shown).Append(" more");
            CmodLog.Info(_sb.ToString());
            _stickToBottom = true;

            string common = LongestCommonPrefix(list);
            string completed = common.Length > trimmed.Length ? common : trimmed;
            _completions = list;
            _completionIndex = -1;
            _completionResult = prefixHead + completed;
            SetInput(_completionResult);
        }

        /// <summary>Everything up to and including the last unquoted ';' (earlier statements on the line).</summary>
        static string HeadBeforeStatement(string text)
        {
            bool quoted = false;
            int cut = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '"') quoted = !quoted;
                else if (text[i] == ';' && !quoted) cut = i + 1;
            }
            return text.Substring(0, cut);
        }

        static string LongestCommonPrefix(List<string> items)
        {
            if (items.Count == 0) return "";
            string p = items[0];
            for (int i = 1; i < items.Count && p.Length > 0; i++)
            {
                var s = items[i];
                int n = Mathf.Min(p.Length, s.Length), k = 0;
                while (k < n && char.ToLowerInvariant(p[k]) == char.ToLowerInvariant(s[k])) k++;
                p = p.Substring(0, k);
            }
            return p;
        }

        static string Shorten(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 3) + "...";

        // ---- History persistence --------------------------------------------------------------

        void LoadHistory()
        {
            try
            {
                var saved = PlayerPrefs.GetString(HistoryPrefsKey, "");
                if (string.IsNullOrEmpty(saved)) return;
                foreach (var line in saved.Split('\n'))
                    if (!string.IsNullOrWhiteSpace(line)) _history.Add(line);
                if (_history.Count > MaxHistory) _history.RemoveRange(0, _history.Count - MaxHistory);
            }
            catch (Exception ex)
            {
                CmodLog.Warn("[console] could not load history: " + ex.Message);
            }
        }

        void SaveHistory()
        {
            try
            {
                PlayerPrefs.SetString(HistoryPrefsKey, string.Join("\n", _history));
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                CmodLog.Warn("[console] could not save history: " + ex.Message);
            }
        }
    }
}
