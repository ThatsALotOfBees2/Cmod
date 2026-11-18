using System;
using UnityEngine;

namespace Cmod
{
    /// <summary>
    /// Entry point. Boots automatically after any scene loads, so an empty scene works.
    /// The whole game (world, player, UI) is built from code, with no prefabs or assets needed.
    /// Each subsystem provides its part through the CmodModules partial methods below.
    /// To stop the game booting in a scene (for a test scene, say), add a GameObject with the
    /// CmodNoBoot component.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class CmodGame : MonoBehaviour
    {
        public const string GameName = "Cmod";
        /// <summary>GitHub repo used for auto-update and log upload.</summary>
        public const string RepoOwner = "ThatsALotOfBees2";
        public const string RepoName = "Cmod";

        public static CmodGame Instance { get; private set; }
        public static Version GameVersion => ParseVersion(Application.version);

        public Pose SpawnPoint { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            if (FindObjectOfType<CmodNoBoot>() != null) return;
            var go = new GameObject("Cmod");
            DontDestroyOnLoad(go);
            go.AddComponent<CmodGame>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            CmodLog.BeginUnityCapture();
            CmodLog.Info($"{GameName} {Application.version} starting (Unity {Application.unityVersion}, {Application.platform})");

            Step("Services", () => CmodModules.Services_Init(gameObject));   // local log file, GitHub log upload
            Step("UI", () => CmodModules.Ui_Init(gameObject));               // WindowManager, taskbar, Win98 skin
            Step("World", () => SpawnPoint = CmodModules.World_Build());     // ground, light, sky, spawn point
            Step("Content", CmodModules.Content_Register);                   // default props
            Step("Weapons", CmodModules.Weapons_Register);                   // physgun, toolgun, tool modes, guns
            Step("Commands", CmodModules.Commands_Register);                 // base concommands, convars, keybinds
            Step("Player", () => CmodModules.Player_Spawn(SpawnPoint));      // the local player and HUD
            Step("Windows", () => CmodModules.Windows_Init(gameObject));     // console, spawn menu, options, start menu entries
            Step("Updater", () => CmodModules.Updater_Init(gameObject));     // GitHub auto-update check

            CmodLog.Info("Ready. Hold Q for the spawn menu, press ` for the console.");
        }

        static void Step(string name, Action a)
        {
            try { a(); }
            catch (Exception ex)
            {
                CmodLog.Error($"[boot:{name}] {ex}");
                Debug.LogException(ex);
            }
        }

        void Update()
        {
            Hooks.Run(Hooks.Think);
        }

        void OnDestroy()
        {
            if (Instance == this) { Instance = null; CmodLog.EndUnityCapture(); }
        }

        public static Version ParseVersion(string s)
        {
            if (string.IsNullOrEmpty(s)) return new Version(0, 0, 0);
            s = s.Trim().TrimStart('v', 'V');
            var dash = s.IndexOfAny(new[] { '-', '+', ' ' });
            if (dash >= 0) s = s.Substring(0, dash);
            var parts = s.Split('.');
            int Get(int i) => i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;
            return new Version(Get(0), Get(1), Get(2));
        }
    }

    /// <summary>Put this on any GameObject in a scene to stop Cmod booting there.</summary>
    public sealed class CmodNoBoot : MonoBehaviour { }

    /// <summary>
    /// Each subsystem implements its own method in its own file (C# 9 extended partial methods,
    /// so a missing implementation is a compile error). CmodGame calls them in the order above.
    /// </summary>
    public static partial class CmodModules
    {
        /// <summary>Services/: local log file, GitHub log uploader, settings.</summary>
        public static partial void Services_Init(GameObject root);
        /// <summary>UI/: add WindowManager to root and set up the Win98 skin, taskbar and start menu.</summary>
        public static partial void Ui_Init(GameObject root);
        /// <summary>Game/WorldBuilder.cs: build the map and return the player spawn pose.</summary>
        public static partial Pose World_Build();
        /// <summary>Game/DefaultProps.cs: PropRegistry.Register(...) for the built-in props.</summary>
        public static partial void Content_Register();
        /// <summary>Weapons/ + Tools/: WeaponRegistry.Register and ToolRegistry.Register for the built-ins.</summary>
        public static partial void Weapons_Register();
        /// <summary>Game/BaseCommands.cs: base concommands and convars, plus the global keybind handler.</summary>
        public static partial void Commands_Register();
        /// <summary>Player/: create the local player at the spawn pose, give default weapons, add HUD.</summary>
        public static partial void Player_Spawn(Pose spawn);
        /// <summary>UI/Apps/: create and register the console, spawn menu, options and about windows, plus their start menu items.</summary>
        public static partial void Windows_Init(GameObject root);
        /// <summary>Services/: GitHub release auto-update check.</summary>
        public static partial void Updater_Init(GameObject root);
    }
}
