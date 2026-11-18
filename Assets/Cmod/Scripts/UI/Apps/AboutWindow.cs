using System;
using UnityEngine;
using UnityEngine.Profiling;

namespace Cmod.UI
{
    /// <summary>"About Cmod": the classic Win98 about box with the logo, version, Unity version,
    /// memory figures and who the product is "licensed to" (the player name).</summary>
    public sealed class AboutWindow : Win98Window
    {
        const float Pad = 10f;
        const float BtnW = 75f, BtnH = 23f;

        // Refreshed twice a second rather than every OnGUI event (avoids string churn).
        string _memoryLine = "", _heapLine = "", _systemLine = "";
        float _nextRefresh;

        public AboutWindow()
        {
            Title = "About Cmod";
            IconName = "info";
            Rect = new Rect(0, 0, 420, 300);
            Resizable = false;
            Minimizable = false;
        }

        public override void OnOpened()
        {
            CenterOnScreen();
            _nextRefresh = 0f;
        }

        public override void Tick()
        {
            if (IsFocused && WindowManager.TopModal == null &&
                (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
                Close();
        }

        void Refresh()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.5f;
            long allocated = 0;
            try { allocated = Profiler.GetTotalAllocatedMemoryLong(); } catch (Exception) { /* not available on every platform */ }
            _memoryLine = $"Physical memory available: {SystemInfo.systemMemorySize:N0} MB RAM";
            _heapLine = allocated > 0
                ? $"Memory in use by Cmod: {allocated / (1024f * 1024f):N1} MB (managed heap {GC.GetTotalMemory(false) / (1024f * 1024f):N1} MB)"
                : $"Managed heap: {GC.GetTotalMemory(false) / (1024f * 1024f):N1} MB";
            _systemLine = $"{SystemInfo.operatingSystem}";
        }

        static string LicensedTo()
        {
            if (CmodPlayer.Local != null && !string.IsNullOrEmpty(CmodPlayer.Local.PlayerName)) return CmodPlayer.Local.PlayerName;
            var v = ConCommands.GetVar("name");
            return v != null && !string.IsNullOrEmpty(v.Value) ? v.Value : "player";
        }

        public override void DrawContents(Rect c)
        {
            Refresh();

            // Logo column.
            var logo = new Rect(c.x + Pad + 4f, c.y + Pad + 4f, 48f, 48f);
            Win98.Icon(logo, "cmod");

            float x = logo.xMax + 16f;
            float w = c.xMax - Pad - x;
            float y = c.y + Pad;

            Win98.Label(new Rect(x, y, w, 18f), "Cmod 98", true);
            y += 18f;
            Win98.Label(new Rect(x, y, w, 16f), $"Version {Application.version}");
            y += 16f;
            Win98.Label(new Rect(x, y, w, 16f), $"Unity {Application.unityVersion} ({Application.platform})");
            y += 16f;
            Win98.Label(new Rect(x, y, w, 16f), "A physics sandbox. Built entirely from code.");
            y += 24f;

            Win98.Label(new Rect(x, y, w, 16f), "This product is licensed to:");
            y += 16f;
            Win98.Label(new Rect(x + 12f, y, w - 12f, 16f), Win98.Ellipsize(LicensedTo(), w - 12f, true), true);
            y += 22f;

            Win98.Separator(new Rect(x, y, w, 2f));
            y += 8f;
            Win98.Label(new Rect(x, y, w, 16f), Win98.Ellipsize(_memoryLine, w));
            y += 16f;
            Win98.Label(new Rect(x, y, w, 16f), Win98.Ellipsize(_heapLine, w));
            y += 16f;
            Win98.Label(new Rect(x, y, w, 16f), Win98.Ellipsize(_systemLine, w), false, TextAnchor.MiddleLeft, Win98.Shadow);

            var ok = new Rect(c.xMax - Pad - BtnW, c.yMax - Pad - BtnH, BtnW, BtnH);
            if (Win98.Button(ok, "OK")) Close();
        }
    }
}
