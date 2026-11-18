# Cmod

**Cmod** is a GMod-style physics sandbox made in Unity, dressed up as a Windows 98 desktop. You spawn props,
weld and rope them together with the toolgun, and mess around with physics. Every menu is a Win98 window:
beveled gray buttons, navy title bars, a taskbar and a Start menu.

The long-term goal (see [PLAN.md](PLAN.md)) is a game where friends write mods with AI (mainly Claude) in a
GMod-like Lua API, keep them in their own GitHub repos, and live-load them into a shared peer-to-peer session.

Everything is built from code at runtime: the map, the props, the player, the weapons and the whole UI. There are
no prefabs, models, textures or sound files, and the game boots itself in any scene.

---

## Getting started

### Requirements
- **Unity 6 LTS**, the version in [`ProjectSettings/ProjectVersion.txt`](ProjectSettings/ProjectVersion.txt)
  (`6000.0.58f2`, or any newer 6000.0 patch). Install it through Unity Hub. No extra packages are needed.
- Git (optional, used to read the version from the latest tag).

### Open the project
1. Clone the repo: `git clone https://github.com/ThatsALotOfBees2/Cmod.git`
2. In Unity Hub choose **Add → Add project from disk** and pick the cloned folder. Open it with Unity 6.
3. The first time the project opens, Cmod asks to set itself up. Click **Set Up Now** (or run
   **Cmod → Setup Project** from the menu bar at any time). It:
   - creates the empty scene `Assets/Cmod/Scenes/Main.unity` and makes it scene 0 in Build Settings,
   - sets the product name to "Cmod" and the company name,
   - switches **Active Input Handling** to the legacy *Input Manager (Old)* (or *Both* if the Input System
     package is installed). Cmod reads input through the legacy Input Manager,
   - sets the version from the latest git tag (for example `v0.3.0` → `0.3.0`).
4. If the input setting changed, Unity asks to restart. Say yes.
5. Press **Play**, or use **Cmod → Play**, which opens the main scene and starts it.

Setup is safe to run again: it only changes what isn't set up yet. Commit `Assets/Cmod/Scenes/` and
`ProjectSettings/` afterwards so CI builds use the same settings. For CI or scripts you can also run it headless:

```
Unity -batchmode -quit -projectPath . -executeMethod Cmod.EditorTools.CmodProjectSetup.SetupBatch
```

### The Cmod menu
| Item | What it does |
|---|---|
| Setup Project | The one-click setup above |
| Open Main Scene / Play | Opens `Main.unity` (and enters play mode) |
| Open Logs Folder | The game's log files (`persistentDataPath/logs`) |
| Open Persistent Data Folder | Settings, logs and downloaded updates |
| Open BOARD.md / README.md / PLAN.md | The project docs |

---

## Controls

| Key | Action |
|---|---|
| W A S D | Move |
| Space | Jump (noclip: up) |
| Shift | Sprint (noclip: fly fast) |
| Ctrl | Crouch (noclip: down) |
| V | Toggle noclip |
| F | Flashlight |
| 1–6, mouse wheel | Select a weapon |
| Left mouse | Primary fire (toolgun: use the active tool) |
| Right mouse | Secondary fire (toolgun: the tool's second action) |
| R | Reload (toolgun: the tool's reload action, for example clear constraints) |
| Q (hold) | Spawn menu: props, weapons, tools and utilities. `spawnmenu_toggle 1` makes Q a toggle |
| \` | Console |
| Z | Undo the last thing you spawned or made |
| F1 | Help (the controls list) |
| Esc | Close the focused window, otherwise open or close the Start menu |

The mouse cursor is free whenever a window wants it, and captured for mouse look otherwise.

### Weapons and tools
- **Toolgun** (selected when you spawn): use it with a tool mode from the spawn menu's **Tools** tab: Remover,
  Color, Weld, Rope, Balloon, Physical Properties and Lamp. Everything a tool does can be undone with Z.
- **Pistol**: hitscan, 18 rounds, pushes props around.
- **Wrench**: melee, knocks props over.
- The **physgun** is on hold for now. Move props by walking into them, or weld and rope them with the toolgun.

### Console
Open it with \`. Some useful commands: `help`, `find <text>`, `noclip`, `god`, `kill`, `respawn`, `give <weapon>`,
`spawn <prop>`, `undo`, `cleanup`, `sv_gravity <value>`, `host_timescale <value>`, `name <name>`,
`cl_showfps 1`, `ui_scale <0.75–2.5>`, `fov <60–120>`, `sensitivity <value>`, `volume <0–1>`, `log_upload [note]`,
`update_check`. Tab completes, ↑/↓ go through your history. Settings marked *archive* are remembered between sessions.

---

## Folder layout

```
Assets/Cmod/
├── Editor/          Editor-only tooling: the Cmod menu and Setup Project
├── Scenes/          Main.unity (empty: the game builds itself), created by Setup Project
└── Scripts/
    ├── Core/        Shared services: logging, console commands and convars, hooks, undo, materials, physics compatibility
    ├── Entities/    Entity base class, the prop registry and the entity manager
    ├── Game/        CmodGame (boot), the map builder, default props, base commands, keybinds
    ├── Player/      Player controller, camera, HUD, CmodInput (all gameplay input goes through it)
    ├── Weapons/     Weapon base and registry, toolgun, pistol, wrench, effects
    ├── Tools/       Toolgun modes (Remover, Weld, Rope, ...) and constraint bookkeeping
    ├── Services/    Local log file, GitHub log upload, auto-updater
    └── UI/          Win98 widget kit, window manager, taskbar, Start menu, message boxes
        └── Apps/    Console, spawn menu, options, help, about, GitHub settings, updater windows
.github/workflows/   CI: compile check on every push, release builds on tags
docs/GITHUB.md       GitHub token, releases, and how debug logs reach GitHub
Tools/               check-compile.sh: compile the runtime scripts without Unity
BOARD.md             The work board (see below)
PLAN.md              The long-term plan: networking, Lua mods, mod distribution
```

### How the code fits together
- `CmodGame` boots automatically after any scene loads (`RuntimeInitializeOnLoadMethod`). It calls one
  `CmodModules.*` partial method per subsystem, in order: Services → UI → World → Content → Weapons → Commands
  → Player → Windows → Updater. Each subsystem implements its partial method in its own file.
- The files listed as **contracts** in [BOARD.md](BOARD.md) (`Core/*`, the `Contracts.cs` files, `CmodInput.cs`,
  `CmodGame.cs`) are the shared API between subsystems. Their signatures don't change without the lead's approval.
- Gameplay input only goes through `CmodInput`, so nothing fires while you're typing in a window.
- Put a `CmodNoBoot` component in a scene to stop the game booting there (useful for test scenes).

### Compile check without Unity
```
Tools/check-compile.sh
```
This builds every runtime script (not `Editor/` folders) against the Unity reference assemblies with the .NET 8 SDK.
CI runs it on every push and pull request.

---

## The board workflow

All work is tracked on [BOARD.md](BOARD.md), the single source of truth:

1. Every new feature or fix becomes a **card** on the board, with the files it owns and a bullet list of what "done" means.
   Ideas that aren't started yet sit in the **Backlog**.
2. Cards move through `todo` → `doing` → `review` → `done` (or `blocked: reason`).
3. Several AI sub-agents build cards **in parallel in the same working tree**. Each one only creates or edits the
   files its card owns, follows the ground rules at the top of the board (Unity version, input, IMGUI depth, Win98
   look, logging) and the keybind table, and never changes the contracts. When a card is done, its agent sets
   the status to `review`.
4. The lead reviews the work, runs the compile check, updates the board and commits.

So when you ask for something new, it goes on the board first, and the agents build from the board.

---

## Releases and updates

- **Cutting a release:** push a tag like `v0.2.0`. The release workflow builds Windows and Linux players with
  game-ci (the version comes from the tag), zips them as `Cmod-Windows.zip` / `Cmod-Linux.zip` and attaches them to
  a GitHub Release. The repo needs the `UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD` secrets.
- **Auto-update:** when the game starts it checks the latest GitHub release. If it's newer than the running version,
  it offers to download and install it (Start menu → *Cmod Update...*, or the `update_check` command). In the editor it
  only tells you; it never installs.
- **Debug logs:** the game always writes a log file to `persistentDataPath/logs` (the newest 10 are kept). With a GitHub
  token set in *GitHub Settings*, errors are uploaded as issues labelled `cmod-log` so they can be read and fixed.
  Tokens are stored locally and redacted from uploads.

See [docs/GITHUB.md](docs/GITHUB.md) for how to create the token and how releases work in detail.

To set the editor's version to match the newest tag, run **Cmod → Setup Project** again after tagging.

---

## Roadmap

From [PLAN.md](PLAN.md): main menu and P2P server list, chat, sounds, multiplayer (FishNet + Steam P2P), a
MoonSharp Lua mod runtime with a GMod-like API, `require("owner/repo/mod")` live mod loading from GitHub, and
compressed `.cpak` mod bundles shared between peers.
