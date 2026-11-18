using System;
using Cmod.UI;
using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// Global (non-weapon) keybinds owned by C-APPS (see BOARD.md):
    ///   Q (hold)  spawn menu (spawnmenu_toggle 1 makes it a toggle)
    ///   `         console
    ///   Z         undo
    ///   F1        help
    /// Added to the game's root object by Commands_Register. The app windows are created later
    /// (Windows_Init), so every lookup goes through <see cref="CmodApps"/> and tolerates null.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class Keybinds : MonoBehaviour
    {
        /// <summary>True while the spawn menu is open because Q is being held (hold mode).</summary>
        bool _spawnMenuHeld;

        void Update()
        {
            try
            {
                HandleConsole();
                HandleSpawnMenu();
                HandleHelp();
                HandleUndo();
            }
            catch (Exception ex)
            {
                CmodLog.Error($"[keybinds] {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>A modal message box (or an open dropdown list) owns the keyboard.</summary>
        static bool ModalOpen => WindowManager.TopModal != null;

        /// <summary>` toggles the console. While typing it only works in the console's own input line,
        /// so the key can still close the console you are typing in.</summary>
        static void HandleConsole()
        {
            if (!Input.GetKeyDown(KeyCode.BackQuote) || ModalOpen) return;
            var console = CmodApps.Console;
            if (console == null) return;
            if (UIState.TypingInTextField && !(console.IsOpen && console.InputHasFocus)) return;

            if (console.IsOpen && !console.IsMinimized && console.IsFocused) console.Close();
            else console.Open();
        }

        void HandleSpawnMenu()
        {
            var menu = CmodApps.SpawnMenu;
            if (menu == null) { _spawnMenuHeld = false; return; }

            bool toggleMode = BaseCommands.SpawnMenuToggle != null && BaseCommands.SpawnMenuToggle.Bool;

            // Releasing Q closes a menu opened by holding it, even if the user switched modes meanwhile.
            if (_spawnMenuHeld && (!Input.GetKey(KeyCode.Q) || Input.GetKeyUp(KeyCode.Q)))
            {
                _spawnMenuHeld = false;
                if (menu.IsOpen && !ModalOpen) menu.Close();
                return;
            }
            if (_spawnMenuHeld && !menu.IsOpen) _spawnMenuHeld = false; // closed by Esc or [X] while held

            if (!Input.GetKeyDown(KeyCode.Q) || ModalOpen || UIState.TypingInTextField) return;

            if (toggleMode)
            {
                if (menu.IsOpen && !menu.IsMinimized) menu.Close();
                else menu.Open();
            }
            else
            {
                menu.Open();
                _spawnMenuHeld = true;
            }
        }

        static void HandleHelp()
        {
            if (!Input.GetKeyDown(KeyCode.F1) || ModalOpen || UIState.TypingInTextField) return;
            var help = CmodApps.Help;
            if (help == null) return;
            if (help.IsOpen && !help.IsMinimized && help.IsFocused) help.Close();
            else help.Open();
        }

        static void HandleUndo()
        {
            // Gameplay key: CmodInput ignores it while a window has the cursor or a text field is focused.
            if (CmodInput.KeyDown(KeyCode.Z)) BaseCommands.DoUndo();
        }
    }
}
