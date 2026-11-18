using Cmod.UI;
using UnityEngine;

namespace Cmod
{
    public static partial class CmodModules
    {
        /// <summary>
        /// C-APPS boot step: creates and registers the console, spawn menu, options, about and help
        /// windows, and adds their Start menu entries.
        /// </summary>
        public static partial void Windows_Init(GameObject root)
        {
            CmodApps.Init();
        }
    }

    /// <summary>The built-in app windows. Null until Windows_Init has run.</summary>
    public static class CmodApps
    {
        public static ConsoleWindow Console { get; private set; }
        public static SpawnMenuWindow SpawnMenu { get; private set; }
        public static OptionsWindow Options { get; private set; }
        public static AboutWindow About { get; private set; }
        public static HelpWindow Help { get; private set; }

        internal static void Init()
        {
            if (Console != null) return; // idempotent

            Console = Create(new ConsoleWindow());
            SpawnMenu = Create(new SpawnMenuWindow());
            Options = Create(new OptionsWindow());
            About = Create(new AboutWindow());
            Help = Create(new HelpWindow());

            // Group 0 = programs, 50 = settings, 100 = power.
            StartMenu.AddItem("Spawn Menu", "box", () => SpawnMenu.Open(), 0, 0);
            StartMenu.AddItem("Console", "console", () => Console.Open(), 0, 10);
            StartMenu.AddItem("Help", "help", () => Help.Open(), 0, 20);
            StartMenu.AddItem("Options", "settings", () => Options.Open(), 50, 0);
            StartMenu.AddItem("About Cmod", "info", () => About.Open(), 50, 90);
            StartMenu.AddItem("Shut Down...", "shutdown", ConfirmShutDown, 100, 0);
        }

        static T Create<T>(T w) where T : Win98Window
        {
            WindowManager.Register(w);
            return w;
        }

        /// <summary>The classic "Shut Down Windows" question, then quit.</summary>
        public static void ConfirmShutDown()
        {
            MessageBox.Show("Shut Down Cmod", "Are you sure you want to shut down Cmod?",
                MessageBox.Kind.Question, new[] { "Yes", "No" }, result =>
                {
                    if (result == 0) Quit();
                });
        }

        /// <summary>Quits the game (stops play mode in the editor).</summary>
        public static void Quit()
        {
            CmodLog.Info("Shutting down...");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
