using Cmod.UI;
using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// The one place gameplay code reads input from (legacy Input Manager). Every gameplay
    /// query returns false or zero while a UI window has focus or a text field is being typed in.
    /// </summary>
    public static class CmodInput
    {
        /// <summary>False while a window wants the cursor or the user is typing.</summary>
        public static bool GameplayAllowed => !WindowManager.UIHasFocus() && !UIState.TypingInTextField;

        public static bool Key(KeyCode k) => GameplayAllowed && Input.GetKey(k);
        public static bool KeyDown(KeyCode k) => GameplayAllowed && Input.GetKeyDown(k);
        public static bool KeyUp(KeyCode k) => Input.GetKeyUp(k); // always report releases so held states never stick

        public static bool Mouse(int button) => GameplayAllowed && Input.GetMouseButton(button);
        public static bool MouseDown(int button) => GameplayAllowed && Input.GetMouseButtonDown(button);
        public static bool MouseUp(int button) => Input.GetMouseButtonUp(button);

        /// <summary>Raw mouse movement this frame (already scaled by "sensitivity", with "m_invert" applied).</summary>
        public static Vector2 LookDelta
        {
            get
            {
                if (!GameplayAllowed) return Vector2.zero;
                var sens = ConCommands.GetVar("sensitivity")?.Float ?? 2f;
                var inv = ConCommands.GetVar("m_invert")?.Bool ?? false;
                var d = new Vector2(SafeAxis("Mouse X"), SafeAxis("Mouse Y")) * sens;
                if (inv) d.y = -d.y;
                return d;
            }
        }

        /// <summary>Scroll wheel ticks this frame (+ = up).</summary>
        public static float Scroll => GameplayAllowed ? Input.mouseScrollDelta.y : 0f;

        /// <summary>WASD movement vector (x = strafe, y = forward), not normalized.</summary>
        public static Vector2 Move
        {
            get
            {
                if (!GameplayAllowed) return Vector2.zero;
                float x = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
                float y = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
                return new Vector2(x, y);
            }
        }

        static bool s_axisWarned;
        static float SafeAxis(string axis)
        {
            try { return Input.GetAxisRaw(axis); }
            catch (System.ArgumentException)
            {
                if (!s_axisWarned) { s_axisWarned = true; CmodLog.Error($"Input axis '{axis}' missing. Reset Project Settings > Input Manager."); }
                return 0f;
            }
        }
    }
}
