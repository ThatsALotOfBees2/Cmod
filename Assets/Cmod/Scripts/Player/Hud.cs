using System;
using Cmod.UI;
using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// The in-game HUD, drawn with IMGUI underneath the Win98 windows (GUI.depth = 10).
    /// Everything is in WindowManager.Scale() space and stays above the taskbar.
    ///
    ///   centre         crosshair (hidden while dead)
    ///   top centre     weapon-switch popup (Win98 list, active weapon highlighted, fades after 1.5 s)
    ///   bottom left    "Health" panel with a progress bar and number, "NOCLIP" tag above it
    ///   bottom right   ammo panel (Clip / ClipSize) when the active weapon uses ammo
    ///   full screen    red damage flash, death message with the respawn countdown
    ///   weapon         ActiveWeapon.DrawHUD() (in the same scaled GUI space)
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        const float Margin = 8f;
        const float PopupShowTime = 1.5f;
        const float PopupFadeTime = 0.4f;
        const float DamageFlashTime = 0.5f;
        const float RowHeight = 18f;

        GUIStyle _bigNumber;
        GUIStyle _mediumNumber;
        Texture2D _white;
        ConVar _drawHud;
        bool _loggedError;

        // Cached strings so the HUD doesn't allocate every frame.
        int _cachedHealth = int.MinValue;
        string _healthText = "";
        int _cachedClip = int.MinValue, _cachedClipSize = int.MinValue;
        string _ammoText = "";

        void OnGUI()
        {
            GUI.depth = 10;
            var player = CmodPlayer.Local;
            if (player == null) return;
            if (_drawHud == null) _drawHud = ConCommands.GetVar("cl_drawhud");
            if (_drawHud != null && !_drawHud.Bool) return;

            var oldMatrix = GUI.matrix;
            var oldColor = GUI.color;
            try
            {
                Win98.EnsureInit();
                EnsureStyles();
                float s = WindowManager.Scale();
                GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
                var size = WindowManager.ScreenSizeScaled;

                if (Event.current.type == EventType.Repaint)
                {
                    DrawDamageFlash(player, size);
                    if (!player.IsDead) DrawCrosshair(size);
                    DrawHealth(player, size);
                    DrawAmmo(player, size);
                    DrawWeaponPopup(player, size);
                    if (player.IsDead) DrawDeathMessage(player, size);
                }

                // Weapon HUD runs for every event type, so weapons may use interactive IMGUI if they want.
                var w = player.ActiveWeapon;
                if (w != null && !player.IsDead)
                {
                    GUI.color = Color.white;
                    try { w.DrawHUD(); }
                    catch (ExitGUIException) { throw; }
                    catch (Exception ex) { LogOnce($"[{w.Id}.DrawHUD] {ex.GetType().Name}: {ex.Message}"); }
                }
            }
            catch (ExitGUIException) { throw; }
            catch (Exception ex) { LogOnce($"[hud] {ex.GetType().Name}: {ex.Message}"); }
            finally
            {
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
            }
        }

        void LogOnce(string msg)
        {
            if (_loggedError) return;
            _loggedError = true;
            CmodLog.Error(msg);
        }

        void EnsureStyles()
        {
            if (_white == null) _white = Texture2D.whiteTexture;
            if (_bigNumber == null)
            {
                _bigNumber = new GUIStyle(Win98.TextStyle(true)) { fontSize = 20, alignment = TextAnchor.MiddleRight };
                _mediumNumber = new GUIStyle(Win98.TextStyle(true)) { fontSize = 14, alignment = TextAnchor.MiddleRight };
            }
        }

        float BottomY(Vector2 size) => size.y - WindowManager.TaskbarHeight - Margin;

        void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = old;
        }

        // ---- Crosshair ----------------------------------------------------------------------

        void DrawCrosshair(Vector2 size)
        {
            float cx = Mathf.Round(size.x * 0.5f), cy = Mathf.Round(size.y * 0.5f);
            const float gap = 3f, len = 6f, th = 2f;
            var outline = new Color(0f, 0f, 0f, 0.6f);
            var col = new Color(1f, 1f, 1f, 0.9f);

            for (int pass = 0; pass < 2; pass++)
            {
                float o = pass == 0 ? 1f : 0f;       // outline is 1px bigger on every side
                var c = pass == 0 ? outline : col;
                Fill(new Rect(cx - gap - len - o, cy - th * 0.5f - o, len + 2 * o, th + 2 * o), c);   // left
                Fill(new Rect(cx + gap - o, cy - th * 0.5f - o, len + 2 * o, th + 2 * o), c);         // right
                Fill(new Rect(cx - th * 0.5f - o, cy - gap - len - o, th + 2 * o, len + 2 * o), c);   // top
                Fill(new Rect(cx - th * 0.5f - o, cy + gap - o, th + 2 * o, len + 2 * o), c);         // bottom
            }
            Fill(new Rect(cx - 0.5f, cy - 0.5f, 1f, 1f), col);
        }

        // ---- Health -------------------------------------------------------------------------

        void DrawHealth(CmodPlayer p, Vector2 size)
        {
            var r = new Rect(Margin, BottomY(size) - 48f, 190f, 48f);
            Win98.RaisedPanel(r);
            Win98.Label(new Rect(r.x + 8f, r.y + 4f, 80f, 18f), "Health", true);

            int hp = Mathf.CeilToInt(p.Health);
            if (hp != _cachedHealth) { _cachedHealth = hp; _healthText = hp.ToString(); }
            var numColor = p.Health <= p.MaxHealth * 0.25f ? new Color32(160, 0, 0, 255) : (Color32)Color.black;
            _mediumNumber.normal.textColor = numColor;
            GUI.Label(new Rect(r.xMax - 88f, r.y + 3f, 80f, 20f), _healthText, _mediumNumber);

            float t = p.MaxHealth > 0f ? Mathf.Clamp01(p.Health / p.MaxHealth) : 0f;
            Win98.ProgressBar(new Rect(r.x + 8f, r.y + 25f, r.width - 16f, 16f), t);

            if (p.Noclip)
            {
                var nr = new Rect(r.x, r.y - 24f, 72f, 20f);
                Win98.RaisedPanel(nr);
                Win98.Label(nr, "NOCLIP", true, TextAnchor.MiddleCenter, Win98.TitleActive);
            }
        }

        // ---- Ammo ---------------------------------------------------------------------------

        void DrawAmmo(CmodPlayer p, Vector2 size)
        {
            var w = p.ActiveWeapon;
            if (w == null || p.IsDead) return;
            int clipSize;
            try { clipSize = w.ClipSize; } catch (Exception) { return; }
            if (clipSize <= 0) return;

            if (w.Clip != _cachedClip || clipSize != _cachedClipSize)
            {
                _cachedClip = w.Clip;
                _cachedClipSize = clipSize;
                _ammoText = $"{w.Clip} / {clipSize}";
            }

            var r = new Rect(size.x - Margin - 160f, BottomY(size) - 48f, 160f, 48f);
            Win98.RaisedPanel(r);
            Win98.Label(new Rect(r.x + 8f, r.y + 4f, r.width - 16f, 16f), "Ammo", true);
            var sunk = new Rect(r.x + 8f, r.y + 21f, r.width - 16f, 22f);
            Win98.SunkenPanel(sunk, true);
            _bigNumber.normal.textColor = w.Clip == 0 ? new Color32(160, 0, 0, 255) : (Color32)Color.black;
            GUI.Label(new Rect(sunk.x + 4f, sunk.y, sunk.width - 10f, sunk.height), _ammoText, _bigNumber);
        }

        // ---- Weapon switch popup ------------------------------------------------------------

        void DrawWeaponPopup(CmodPlayer p, Vector2 size)
        {
            float age = Time.unscaledTime - p.LastWeaponSwitchTime;
            if (age < 0f || age > PopupShowTime + PopupFadeTime) return;
            var list = p.SortedWeapons();
            if (list.Count == 0) return;
            float alpha = age <= PopupShowTime ? 1f : 1f - (age - PopupShowTime) / PopupFadeTime;

            const float width = 210f;
            float height = 3f + 18f + 2f + list.Count * RowHeight + 4f + 3f;
            var r = new Rect(Mathf.Round((size.x - width) * 0.5f), Margin, width, height);

            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);

            // Window-like frame: raised border, active title bar, sunken white list.
            Win98.RaisedPanel(r);
            var title = new Rect(r.x + 3f, r.y + 3f, r.width - 6f, 18f);
            Win98.Gradient(title, Win98.TitleActive, Win98.TitleActiveEnd);
            Win98.Label(new Rect(title.x + 4f, title.y, title.width - 8f, title.height), "Weapons", true,
                TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, alpha));

            var listRect = new Rect(r.x + 4f, title.yMax + 2f, r.width - 8f, list.Count * RowHeight + 4f);
            Win98.SunkenPanel(listRect, true);
            for (int i = 0; i < list.Count; i++)
            {
                var w = list[i];
                var row = new Rect(listRect.x + 2f, listRect.y + 2f + i * RowHeight, listRect.width - 4f, RowHeight);
                bool active = w == p.ActiveWeapon;
                if (active) Fill(row, new Color(Win98.SelectionBg.r, Win98.SelectionBg.g, Win98.SelectionBg.b, alpha));
                var textCol = active ? new Color(1f, 1f, 1f, alpha) : new Color(0f, 0f, 0f, alpha);
                Win98.Label(new Rect(row.x + 4f, row.y, 22f, row.height), (w.Slot + 1).ToString(), active, TextAnchor.MiddleLeft, textCol);
                Win98.Label(new Rect(row.x + 24f, row.y, row.width - 28f, row.height), w.PrintName, active, TextAnchor.MiddleLeft, textCol);
            }

            GUI.color = old;
        }

        // ---- Damage / death -----------------------------------------------------------------

        void DrawDamageFlash(CmodPlayer p, Vector2 size)
        {
            float age = Time.unscaledTime - p.LastDamageTime;
            float a = 0f;
            if (age >= 0f && age < DamageFlashTime) a = 0.4f * (1f - age / DamageFlashTime);
            if (p.IsDead) a = Mathf.Max(a, 0.25f);
            else if (p.Health > 0f && p.Health <= p.MaxHealth * 0.2f)
                a = Mathf.Max(a, 0.06f + 0.04f * Mathf.Sin(Time.unscaledTime * 5f));   // low-health pulse
            if (a <= 0.001f) return;
            Fill(new Rect(0f, 0f, size.x, size.y), new Color(0.8f, 0f, 0f, a));
        }

        void DrawDeathMessage(CmodPlayer p, Vector2 size)
        {
            float left = Mathf.Max(0f, p.RespawnDelaySeconds - (Time.unscaledTime - p.DeathTime));
            const float w = 240f, h = 74f;
            var r = new Rect(Mathf.Round((size.x - w) * 0.5f), Mathf.Round(size.y * 0.5f - h - 20f), w, h);
            Win98.RaisedPanel(r);
            var title = new Rect(r.x + 3f, r.y + 3f, r.width - 6f, 18f);
            Win98.Gradient(title, Win98.TitleActive, Win98.TitleActiveEnd);
            Win98.Label(new Rect(title.x + 4f, title.y, title.width - 8f, title.height), Application.productName, true,
                TextAnchor.MiddleLeft, Color.white);
            Win98.Icon(new Rect(r.x + 12f, r.y + 32f, 32f, 32f), "error");
            Win98.Label(new Rect(r.x + 52f, r.y + 28f, r.width - 60f, 18f), "You died.", true);
            Win98.Label(new Rect(r.x + 52f, r.y + 46f, r.width - 60f, 18f), $"Respawning in {left:0.0} s...");
        }
    }
}
