// Cmod editor tooling: the "Cmod" menu and the one-click project setup.
//
// Cmod builds its whole world from code at runtime (CmodGame boots itself after any scene loads),
// so the only things a fresh checkout needs are editor-side settings that Unity cannot take from
// code at runtime:
//   * a scene to put in Build Settings (an empty one is enough),
//   * the product and company names (they also decide Application.persistentDataPath),
//   * Active Input Handling set to the legacy Input Manager (or Both), because CmodInput uses UnityEngine.Input,
//   * the bundle version, taken from the latest git tag so the auto-updater compares the right numbers.
//
// "Cmod > Setup Project" does all of that and is safe to run any number of times.
// Batch / CI use:  Unity -batchmode -quit -projectPath . -executeMethod Cmod.EditorTools.CmodProjectSetup.SetupBatch
//
// This file only uses UnityEngine and UnityEditor (it does not reference any runtime Cmod type).
// Note: without an .asmdef it compiles into Assembly-CSharp-Editor, which Unity only builds after
// Assembly-CSharp compiles, so a compile error in any runtime script also hides this menu. Adding
// Assets/Cmod/Editor/Cmod.Editor.asmdef (includePlatforms ["Editor"], no references) makes it independent.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Cmod.EditorTools
{
    [InitializeOnLoad]
    public static class CmodProjectSetup
    {
        // ------------------------------------------------------------------ settings

        /// <summary>The scene added to Build Settings. It stays empty: CmodGame builds everything at runtime.</summary>
        public const string ScenePath = "Assets/Cmod/Scenes/Main.unity";
        const string SceneFolderParent = "Assets/Cmod";
        const string SceneFolderName = "Scenes";

        public const string ProductName = "Cmod";
        /// <summary>
        /// Kept stable on purpose: company + product decide persistentDataPath (logs, PlayerPrefs on some
        /// platforms, the update folder). Changing it later "loses" every player's settings and logs.
        /// </summary>
        public const string CompanyName = "Cmod";
        /// <summary>Used when there is no git tag and the project has no usable version yet.</summary>
        const string FallbackVersion = "0.1.0";

        // PlayerSettings.activeInputHandler values (serialized field in ProjectSettings/ProjectSettings.asset).
        const int InputOld = 0;   // Input Manager (legacy)
        const int InputNew = 1;   // Input System package
        const int InputBoth = 2;  // both

        const string MenuRoot = "Cmod/";
        const string LogTag = "[Cmod Setup] ";

        // Session/editor-pref keys for the "this project isn't set up yet" prompt.
        const string SessionCheckedKey = "Cmod.Setup.CheckedThisSession";
        // Set when Setup changed Active Input Handling in this editor session: the change only applies after
        // a restart, and entering play mode before that makes CmodInput's UnityEngine.Input calls throw.
        const string SessionRestartPendingKey = "Cmod.Setup.InputRestartPending";

        /// <summary>True when Active Input Handling was changed and Unity has not been restarted since.</summary>
        public static bool RestartPending => SessionState.GetBool(SessionRestartPendingKey, false);
        static string DontAskPrefKey => "Cmod.Setup.DontAsk." + ProjectRoot.GetHashCode().ToString("X8");

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        // ------------------------------------------------------------------ on editor load

        static CmodProjectSetup()
        {
            // Never prompt in batch mode (CI builds): a modal dialog would hang or be auto-answered.
            if (Application.isBatchMode) return;
            // Defer until the editor has finished loading, and only check once per editor session
            // (the static constructor runs again after every script reload).
            EditorApplication.delayCall += PromptIfNotSetUp;
        }

        static void PromptIfNotSetUp()
        {
            try
            {
                if (SessionState.GetBool(SessionCheckedKey, false)) return;
                SessionState.SetBool(SessionCheckedKey, true);
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (EditorPrefs.GetBool(DontAskPrefKey, false)) return;

                var missing = DescribeMissingSetup();
                if (missing.Count == 0) return;

                int choice = EditorUtility.DisplayDialogComplex(
                    "Cmod",
                    "This project isn't fully set up for Cmod yet:\n\n  - " + string.Join("\n  - ", missing) +
                    "\n\nRun Cmod > Setup Project now? (You can run it any time from the Cmod menu.)",
                    "Set Up Now", "Later", "Don't Ask Again");
                if (choice == 0) Setup(true);
                else if (choice == 2) EditorPrefs.SetBool(DontAskPrefKey, true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LogTag + "Setup check failed: " + ex.Message);
            }
        }

        /// <summary>Human-readable list of what Setup Project would still change. Empty when everything is in place.</summary>
        static List<string> DescribeMissingSetup()
        {
            var missing = new List<string>();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                missing.Add("the main scene " + ScenePath + " does not exist");
            if (!IsSceneFirstInBuildSettings())
                missing.Add("the main scene is not the first scene in Build Settings");
            if (PlayerSettings.productName != ProductName)
                missing.Add("the product name is not \"" + ProductName + "\"");
            int handler = ReadActiveInputHandler(out _);
            if (handler == InputNew)
                missing.Add("Active Input Handling is \"Input System Package (New)\", but Cmod uses the legacy Input Manager");
            return missing;
        }

        // ------------------------------------------------------------------ menu

        [MenuItem(MenuRoot + "Setup Project", false, 0)]
        static void SetupMenu() => Setup(true);

        [MenuItem(MenuRoot + "Setup Project", true)]
        static bool SetupMenuValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem(MenuRoot + "Open Main Scene", false, 20)]
        static void OpenMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem(MenuRoot + "Open Main Scene", true)]
        static bool OpenMainSceneValidate() =>
            !EditorApplication.isPlayingOrWillChangePlaymode && AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null;

        /// <summary>Opens the main scene (creating it via setup if needed) and enters play mode.</summary>
        [MenuItem(MenuRoot + "Play", false, 21)]
        static void PlayMainScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                if (!EditorUtility.DisplayDialog("Cmod", "The main scene doesn't exist yet. Run Setup Project first?", "Set Up", "Cancel"))
                    return;
                if (!Setup(true)) return;
            }
            if (RestartPending)
            {
                EditorUtility.DisplayDialog("Cmod",
                    "Active Input Handling was changed and only takes effect after Unity restarts. " +
                    "Restart Unity (File > Open Recent, or reopen the project) before pressing Play.", "OK");
                return;
            }
            var active = EditorSceneManager.GetActiveScene();
            if (active.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            EditorApplication.isPlaying = true;
        }

        [MenuItem(MenuRoot + "Play", true)]
        static bool PlayMainSceneValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>Shows the local log folder written by the game (persistentDataPath/logs).</summary>
        [MenuItem(MenuRoot + "Open Logs Folder", false, 40)]
        static void OpenLogsFolder()
        {
            var dir = Path.Combine(Application.persistentDataPath, "logs");
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex) { Debug.LogWarning(LogTag + "Could not create " + dir + ": " + ex.Message); }
            EditorUtility.RevealInFinder(dir);
        }

        [MenuItem(MenuRoot + "Open Persistent Data Folder", false, 41)]
        static void OpenPersistentDataFolder() => EditorUtility.RevealInFinder(Application.persistentDataPath);

        [MenuItem(MenuRoot + "Open BOARD.md", false, 60)]
        static void OpenBoard() => OpenProjectFile("BOARD.md");

        [MenuItem(MenuRoot + "Open README.md", false, 61)]
        static void OpenReadme() => OpenProjectFile("README.md");

        [MenuItem(MenuRoot + "Open PLAN.md", false, 62)]
        static void OpenPlan() => OpenProjectFile("PLAN.md");

        static void OpenProjectFile(string name)
        {
            var path = Path.Combine(ProjectRoot, name);
            if (File.Exists(path)) EditorUtility.OpenWithDefaultApp(path);
            else EditorUtility.DisplayDialog("Cmod", name + " was not found in the project root:\n" + ProjectRoot, "OK");
        }

        // ------------------------------------------------------------------ setup

        /// <summary>Entry point for -executeMethod (CI). Never shows dialogs; exits with code 1 on failure when -quit is used.</summary>
        public static void SetupBatch()
        {
            bool ok = Setup(false);
            if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
        }

        /// <summary>
        /// Runs every setup step. Each step is independent: one failing does not stop the others.
        /// Returns false if any step failed or the user cancelled.
        /// </summary>
        public static bool Setup(bool interactive)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Report(interactive, "Leave play mode before running Cmod > Setup Project.", true);
                return false;
            }

            var log = new StringBuilder();
            bool ok = true;
            bool restartNeeded = false;

            try
            {
                EditorUtility.DisplayProgressBar("Cmod Setup", "Main scene...", 0.1f);
                ok &= Step(log, "Main scene", () => EnsureMainScene(interactive, log));

                EditorUtility.DisplayProgressBar("Cmod Setup", "Build Settings...", 0.3f);
                ok &= Step(log, "Build Settings", () => EnsureBuildSettings(log));

                EditorUtility.DisplayProgressBar("Cmod Setup", "Player Settings...", 0.5f);
                ok &= Step(log, "Player Settings", () => ApplyPlayerSettings(log));

                EditorUtility.DisplayProgressBar("Cmod Setup", "Active Input Handling...", 0.7f);
                ok &= Step(log, "Input handling", () => EnsureInputHandler(log, out restartNeeded));

                EditorUtility.DisplayProgressBar("Cmod Setup", "Version from git...", 0.85f);
                ok &= Step(log, "Version", () => ApplyVersionFromGit(log));

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            string summary = (ok ? "Cmod setup finished.\n\n" : "Cmod setup finished with problems (see the Console).\n\n") + log;
            if (ok) Debug.Log(LogTag + summary);
            else Debug.LogWarning(LogTag + summary);

            if (restartNeeded) SessionState.SetBool(SessionRestartPendingKey, true);

            if (interactive)
            {
                if (restartNeeded)
                {
                    bool restart = EditorUtility.DisplayDialog("Cmod Setup",
                        summary + "\nActive Input Handling changed. Unity has to restart before it takes effect. Restart now?",
                        "Restart", "Later");
                    if (restart && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                        EditorApplication.OpenProject(ProjectRoot);
                }
                else
                {
                    EditorUtility.DisplayDialog("Cmod Setup",
                        summary + "\nPress Play (or Cmod > Play) to start. Commit Assets/Cmod/Scenes and ProjectSettings/ so CI builds use the same setup.",
                        "OK");
                }
            }
            else if (restartNeeded)
            {
                Debug.Log(LogTag + "Active Input Handling changed; it applies the next time Unity starts.");
            }
            return ok;
        }

        /// <summary>Runs one step, catching exceptions so later steps still run.</summary>
        static bool Step(StringBuilder log, string name, Func<bool> step)
        {
            try { return step(); }
            catch (Exception ex)
            {
                log.AppendLine("x " + name + ": failed (" + ex.Message + ")");
                Debug.LogException(ex);
                return false;
            }
        }

        static void Report(bool interactive, string message, bool warn)
        {
            if (warn) Debug.LogWarning(LogTag + message); else Debug.Log(LogTag + message);
            if (interactive) EditorUtility.DisplayDialog("Cmod Setup", message, "OK");
        }

        // ---- step 1: the main scene

        static bool EnsureMainScene(bool interactive, StringBuilder log)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                log.AppendLine("- Main scene: kept the existing " + ScenePath);
                return true;
            }

            // Creating a scene replaces the open one, so let the user save their work first.
            if (interactive && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                log.AppendLine("x Main scene: cancelled (the open scene was not saved)");
                return false;
            }

            EnsureFolder(SceneFolderParent, SceneFolderName);

            // An empty scene is all Cmod needs: CmodGame boots itself after any scene loads and builds
            // the world, player and UI from code. (EmptyScene, not DefaultGameObjects: the player makes its own camera.)
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                log.AppendLine("x Main scene: could not save " + ScenePath);
                return false;
            }
            log.AppendLine("+ Main scene: created " + ScenePath);
            return true;
        }

        static void EnsureFolder(string parent, string name)
        {
            // Make sure every level of the parent exists too (AssetDatabase.CreateFolder needs it).
            if (!AssetDatabase.IsValidFolder(parent))
            {
                var p = Path.GetDirectoryName(parent)?.Replace('\\', '/');
                var n = Path.GetFileName(parent);
                if (!string.IsNullOrEmpty(p) && !string.IsNullOrEmpty(n)) EnsureFolder(p, n);
            }
            var full = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(full)) AssetDatabase.CreateFolder(parent, name);
        }

        // ---- step 2: Build Settings

        static bool IsSceneFirstInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            return scenes.Length > 0 && scenes[0].path == ScenePath && scenes[0].enabled;
        }

        static bool EnsureBuildSettings(StringBuilder log)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                log.AppendLine("x Build Settings: skipped (the main scene does not exist)");
                return false;
            }
            if (IsSceneFirstInBuildSettings())
            {
                log.AppendLine("- Build Settings: " + ScenePath + " is already scene 0");
                return true;
            }

            // Put the main scene first (index 0 is what a player build loads), keep any other scenes after it.
            var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != ScenePath) list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();
            log.AppendLine("+ Build Settings: " + ScenePath + " added as scene 0");
            return true;
        }

        // ---- step 3: Player Settings

        static bool ApplyPlayerSettings(StringBuilder log)
        {
            var changes = new List<string>();
            if (PlayerSettings.productName != ProductName) { PlayerSettings.productName = ProductName; changes.Add("product \"" + ProductName + "\""); }
            if (PlayerSettings.companyName != CompanyName) { PlayerSettings.companyName = CompanyName; changes.Add("company \"" + CompanyName + "\""); }
            // Keep simulating while the window is unfocused (needed for hosting P2P games later, and nicer for testing).
            if (!PlayerSettings.runInBackground) { PlayerSettings.runInBackground = true; changes.Add("run in background"); }

            log.AppendLine(changes.Count > 0
                ? "+ Player Settings: set " + string.Join(", ", changes)
                : "- Player Settings: product \"" + ProductName + "\", company \"" + CompanyName + "\" already set");
            return true;
        }

        // ---- step 4: Active Input Handling

        /// <summary>The PlayerSettings object behind ProjectSettings/ProjectSettings.asset.</summary>
        static Object LoadPlayerSettingsObject()
        {
            var all = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (all == null) return null;
            foreach (var o in all)
                if (o != null && o.GetType().Name == "PlayerSettings") return o;
            return all.Length > 0 ? all[0] : null;
        }

        /// <summary>Returns the activeInputHandler value, or -1 if it can't be read.</summary>
        static int ReadActiveInputHandler(out SerializedObject so)
        {
            so = null;
            var ps = LoadPlayerSettingsObject();
            if (ps == null) return -1;
            so = new SerializedObject(ps);
            var prop = so.FindProperty("activeInputHandler");
            return prop != null ? prop.intValue : -1;
        }

        static bool EnsureInputHandler(StringBuilder log, out bool restartNeeded)
        {
            restartNeeded = false;
            int current = ReadActiveInputHandler(out var so);
            if (so == null || current < 0)
            {
                log.AppendLine("x Input: could not read activeInputHandler. Set Edit > Project Settings > Player > Other Settings > " +
                               "Active Input Handling to \"Input Manager (Old)\" or \"Both\" by hand.");
                return false;
            }
            if (current == InputOld || current == InputBoth)
            {
                log.AppendLine("- Input: Active Input Handling is already " + InputHandlerName(current));
                return true;
            }

            // If the Input System package is installed, something may use it, so keep both. Otherwise the
            // legacy manager alone (the new backend without its package would leave the game with no input at all).
            int target = InputSystemPackageInstalled() ? InputBoth : InputOld;
            var prop = so.FindProperty("activeInputHandler");
            prop.intValue = target;
            so.ApplyModifiedPropertiesWithoutUndo();
            restartNeeded = true;
            log.AppendLine("+ Input: Active Input Handling " + InputHandlerName(current) + " -> " + InputHandlerName(target) + " (restart Unity)");
            return true;
        }

        static string InputHandlerName(int v)
        {
            switch (v)
            {
                case InputOld: return "\"Input Manager (Old)\"";
                case InputNew: return "\"Input System Package (New)\"";
                case InputBoth: return "\"Both\"";
                default: return "unknown (" + v + ")";
            }
        }

        static bool InputSystemPackageInstalled()
        {
            try
            {
                var manifest = Path.Combine(ProjectRoot, "Packages", "manifest.json");
                return File.Exists(manifest) && File.ReadAllText(manifest).Contains("\"com.unity.inputsystem\"");
            }
            catch { return false; }
        }

        // ---- step 5: version from the latest git tag

        // Accepts 1, 1.2, 1.2.3, 1.2.3.4 with an optional -suffix / +build (the leading v is stripped first).
        static readonly Regex VersionPattern = new Regex(@"^\d+(\.\d+){0,3}([-+][0-9A-Za-z.\-]+)?$");

        static bool ApplyVersionFromGit(StringBuilder log)
        {
            string tag = LatestGitTag(out string why);
            if (tag == null)
            {
                if (string.IsNullOrWhiteSpace(PlayerSettings.bundleVersion))
                {
                    PlayerSettings.bundleVersion = FallbackVersion;
                    log.AppendLine("+ Version: no git tag (" + why + "), set " + FallbackVersion);
                }
                else
                {
                    log.AppendLine("- Version: no git tag (" + why + "), kept " + PlayerSettings.bundleVersion);
                }
                return true; // not an error: untagged checkouts are normal
            }

            string version = tag.Trim();
            if (version.StartsWith("v") || version.StartsWith("V")) version = version.Substring(1);
            if (!VersionPattern.IsMatch(version))
            {
                log.AppendLine("- Version: latest tag \"" + tag + "\" is not a version number, kept " + PlayerSettings.bundleVersion);
                return true;
            }
            if (PlayerSettings.bundleVersion == version)
            {
                log.AppendLine("- Version: already " + version + " (tag " + tag + ")");
                return true;
            }
            string old = PlayerSettings.bundleVersion;
            PlayerSettings.bundleVersion = version;
            log.AppendLine("+ Version: " + old + " -> " + version + " (tag " + tag + ")");
            return true;
        }

        /// <summary>
        /// The newest tag reachable from HEAD (git describe), falling back to the highest version-sorted
        /// v* tag (for shallow clones where describe finds nothing). Null with a reason if there is none.
        /// </summary>
        static string LatestGitTag(out string why)
        {
            why = null;
            if (!Directory.Exists(Path.Combine(ProjectRoot, ".git")) && !File.Exists(Path.Combine(ProjectRoot, ".git")))
            {
                why = "not a git checkout";
                return null;
            }

            // Only trust describe when the nearest tag is a version: a CI/marker tag would otherwise hide a real v* release tag.
            if (RunGit("describe --tags --abbrev=0", out string outText, out string err) && !string.IsNullOrWhiteSpace(outText)
                && IsVersionTag(FirstLine(outText)))
                return FirstLine(outText);

            if (RunGit("tag --list v* --sort=-v:refname", out outText, out err) && !string.IsNullOrWhiteSpace(outText))
            {
                foreach (var line in outText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    if (IsVersionTag(line)) return line.Trim();
                // No v* tag is a version: return the newest so the caller reports it.
                return FirstLine(outText);
            }

            why = string.IsNullOrEmpty(err) ? "no tags" : FirstLine(err);
            return null;
        }

        static bool IsVersionTag(string tag)
        {
            string v = tag.Trim();
            if (v.StartsWith("v") || v.StartsWith("V")) v = v.Substring(1);
            return VersionPattern.IsMatch(v);
        }

        static string FirstLine(string s)
        {
            s = s.Trim();
            int nl = s.IndexOfAny(new[] { '\r', '\n' });
            return nl >= 0 ? s.Substring(0, nl).Trim() : s;
        }

        /// <summary>Runs git in the project root with a short timeout. False if git is missing, fails or hangs.</summary>
        static bool RunGit(string args, out string stdout, out string stderr)
        {
            stdout = "";
            stderr = "";
            var outBuf = new StringBuilder();
            var errBuf = new StringBuilder();
            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    WorkingDirectory = ProjectRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var p = new Process { StartInfo = psi })
                {
                    // Read asynchronously so a full pipe can't deadlock the editor.
                    p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (outBuf) outBuf.AppendLine(e.Data); };
                    p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (errBuf) errBuf.AppendLine(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(5000))
                    {
                        try { p.Kill(); } catch { /* already gone */ }
                        stderr = "git timed out";
                        return false;
                    }
                    p.WaitForExit(); // flush the async readers
                    lock (outBuf) stdout = outBuf.ToString();
                    lock (errBuf) stderr = errBuf.ToString();
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                // Typically Win32Exception: git isn't installed or not on PATH.
                stderr = "git not available (" + ex.Message + ")";
                return false;
            }
        }
    }
}
