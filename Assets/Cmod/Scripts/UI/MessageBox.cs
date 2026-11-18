using System;
using UnityEngine;

namespace Cmod.UI
{
    /// <summary>Classic Win98 message box (contract in UI/Contracts.cs).</summary>
    public static partial class MessageBox
    {
        public static partial void Show(string title, string text, Kind kind, string[] buttons, Action<int> onResult)
        {
            var w = new MessageBoxWindow(title, text, kind, buttons, onResult);
            WindowManager.OpenModal(w);
        }
    }

    /// <summary>
    /// The modal dialog behind MessageBox.Show: a Kind icon, word-wrapped text, and 75x23 buttons centered
    /// at the bottom. Enter picks the first (default) button, Esc the last one; the [X] reports -1.
    /// While it is open WindowManager blocks input to every other window and the taskbar.
    /// </summary>
    internal sealed class MessageBoxWindow : Win98Window
    {
        const float Pad = 12f;
        const float IconSize = 32f;
        const float IconGap = 12f;
        const float BtnW = 75f, BtnH = 23f, BtnGap = 6f;
        const float MaxTextW = 380f, MinTextW = 80f;

        readonly string m_text;
        readonly MessageBox.Kind m_kind;
        readonly string[] m_buttons;
        readonly Action<int> m_onResult;
        bool m_done;
        bool m_laidOut;
        int m_openFrame;
        float m_textW, m_textH;

        public MessageBoxWindow(string title, string text, MessageBox.Kind kind, string[] buttons, Action<int> onResult)
        {
            Title = string.IsNullOrEmpty(title) ? CmodGame.GameName : title;
            m_text = text ?? "";
            m_kind = kind;
            m_buttons = buttons != null && buttons.Length > 0 ? buttons : new[] { "OK" };
            m_onResult = onResult;
            IconName = IconFor(kind);
            Closable = true;
            Minimizable = false;
            Resizable = false;
            CapturesCursor = true;
            ShowInTaskbar = false;
            Rect = new Rect(0, 0, 300, 140);
        }

        static string IconFor(MessageBox.Kind kind)
        {
            switch (kind)
            {
                case MessageBox.Kind.Warning: return "warning";
                case MessageBox.Kind.Error: return "error";
                case MessageBox.Kind.Question: return "question";
                default: return "info";
            }
        }

        public override void OnOpened()
        {
            m_openFrame = Time.frameCount;
            m_laidOut = false;
        }

        /// <summary>Sizes the dialog to its text and buttons and centers it. Needs the GUI font, so
        /// WindowManager calls it from OnGUI before the dialog is first drawn.</summary>
        internal void EnsureLayout()
        {
            if (m_laidOut) return;
            m_laidOut = true;

            // Single-line width (up to the max), then the wrapped height for that width.
            float longest = 0f;
            foreach (var line in m_text.Split('\n')) longest = Mathf.Max(longest, Win98.TextWidth(line));
            m_textW = Mathf.Clamp(Mathf.Ceil(longest) + 2f, MinTextW, MaxTextW);
            m_textH = Mathf.Ceil(Win98.TextHeight(m_text, m_textW));

            int n = m_buttons.Length;
            float buttonsW = n * BtnW + (n - 1) * BtnGap;
            float clientW = Mathf.Max(Pad + IconSize + IconGap + m_textW + Pad, buttonsW + 2 * Pad);
            // Room for the title too, so it never gets cut off.
            clientW = Mathf.Max(clientW, Win98.TextWidth(Title, true) + 50f);
            float clientH = Pad + Mathf.Max(IconSize, m_textH) + 14f + BtnH + 10f;

            // Frame: border + title bar around the client area (see WindowManager.ClientRect).
            Rect.width = Mathf.Ceil(clientW + 2 * (WindowManager.Border + 1));
            Rect.height = Mathf.Ceil(clientH + WindowManager.Border + WindowManager.TitleHeight + 1 + WindowManager.Border + 1);
            CenterOnScreen();
        }

        public override void DrawContents(Rect client)
        {
            EnsureLayout();

            Win98.Icon(new Rect(client.x + Pad, client.y + Pad, IconSize, IconSize), IconName);

            float textX = client.x + Pad + IconSize + IconGap;
            float textY = client.y + Pad + (m_textH < IconSize ? Mathf.Floor((IconSize - m_textH) / 2f) : 0f);
            Win98.DrawText(new Rect(textX, textY, m_textW, m_textH + 2), m_text, false, TextAnchor.UpperLeft, Color.black, true);

            int n = m_buttons.Length;
            float buttonsW = n * BtnW + (n - 1) * BtnGap;
            float bx = client.x + Mathf.Floor((client.width - buttonsW) / 2f);
            float by = client.yMax - 10f - BtnH;
            for (int i = 0; i < n; i++)
            {
                var r = new Rect(bx + i * (BtnW + BtnGap), by, BtnW, BtnH);
                bool clicked;
                if (i == 0)
                {
                    // Default button: the extra black frame Win98 draws around the button Enter activates.
                    Win98.Outline(r, Win98.DarkShadow);
                    clicked = Win98.Button(Win98.Inset(r, 1), m_buttons[i]);
                }
                else
                {
                    clicked = Win98.Button(r, m_buttons[i]);
                }
                if (clicked)
                {
                    Finish(i);
                    return;
                }
            }
        }

        public override void Tick()
        {
            // Only the topmost dialog listens, and not on the frame it opened (that key press opened it).
            if (m_done || WindowManager.TopModal != this || Time.frameCount == m_openFrame) return;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                Finish(0);
            else if (Input.GetKeyDown(KeyCode.Escape))
                Finish(m_buttons.Length > 0 ? m_buttons.Length - 1 : -1);
        }

        public override void OnClosed()
        {
            // Closed with [X] (or by code): report -1.
            if (m_done) return;
            m_done = true;
            Deliver(-1);
        }

        void Finish(int index)
        {
            if (m_done) return;
            m_done = true;
            WindowManager.Close(this);
            Deliver(index);
        }

        void Deliver(int index)
        {
            if (m_onResult == null) return;
            try { m_onResult(index); }
            catch (Exception ex) { CmodLog.Error($"[MessageBox '{Title}'] callback failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }
}
