using Cmod.UI;
using UnityEngine;

namespace Cmod
{
    /// <summary>Small IMGUI pieces shared by the weapon HUDs (drawn inside the player HUD's scaled GUI space).</summary>
    public static class WeaponHud
    {
        const float NoticeSeconds = 3f;

        /// <summary>Draws the latest ToolHelpers notice as a Win98 tooltip-style box centered under the crosshair.
        /// It fades out after a few seconds.</summary>
        public static void DrawNotice()
        {
            var text = ToolHelpers.LastNotice;
            if (string.IsNullOrEmpty(text)) return;
            float age = Time.unscaledTime - ToolHelpers.LastNoticeTime;
            if (age < 0f || age > NoticeSeconds) return;

            var size = WindowManager.ScreenSizeScaled;
            var style = Win98.TextStyle();
            var content = ToolUi.TempContent(text);
            var ts = style.CalcSize(content);
            var r = new Rect(Mathf.Round((size.x - ts.x) / 2f) - 6f, Mathf.Round(size.y * 0.5f + 40f), ts.x + 12f, 20f);

            var prev = GUI.color;
            float a = Mathf.Clamp01((NoticeSeconds - age) / 0.5f);
            GUI.color = new Color(1f, 1f, 1f, a);
            Win98.FillRect(r, Color.black);
            Win98.FillRect(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), Win98.TooltipBg);
            Win98.Label(new Rect(r.x + 6f, r.y, r.width - 12f, r.height), text, false, TextAnchor.MiddleLeft, Color.black);
            GUI.color = prev;
        }

        /// <summary>A one-line hint strip near the bottom center (key help while using a weapon).</summary>
        public static void DrawHint(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var size = WindowManager.ScreenSizeScaled;
            var content = ToolUi.TempContent(text);
            var ts = Win98.TextStyle().CalcSize(content);
            var r = new Rect(Mathf.Round((size.x - ts.x) / 2f) - 8f, size.y - WindowManager.TaskbarHeight - 92f, ts.x + 16f, 22f);
            Win98.RaisedPanel(r);
            Win98.Label(new Rect(r.x + 8f, r.y, r.width - 16f, r.height), text);
        }
    }
}
