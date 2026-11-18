using System;
using System.Collections.Generic;

namespace Cmod
{
    /// <summary>GMod-style undo stack (Z key / "undo" command).</summary>
    public static class UndoManager
    {
        public const int MaxEntries = 200;

        struct Entry { public string Label; public Action Undo; }
        static readonly List<Entry> s_stack = new List<Entry>();

        public static int Count => s_stack.Count;

        public static void Push(string label, Action undo)
        {
            if (undo == null) return;
            s_stack.Add(new Entry { Label = label, Undo = undo });
            if (s_stack.Count > MaxEntries) s_stack.RemoveAt(0);
        }

        /// <summary>Undoes the most recent action. Returns its label, or null if the stack is empty.</summary>
        public static string Undo()
        {
            if (s_stack.Count == 0) return null;
            var e = s_stack[s_stack.Count - 1];
            s_stack.RemoveAt(s_stack.Count - 1);
            try { e.Undo(); }
            catch (Exception ex) { CmodLog.Error($"[undo {e.Label}] {ex.Message}"); }
            return e.Label;
        }

        public static void Clear() => s_stack.Clear();
    }
}
