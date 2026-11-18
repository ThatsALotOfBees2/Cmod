# Cmod Board

The single source of truth for work. Every feature or fix gets a card. Sub-agents work from their card,
only touch the files the card owns, and update **Status** when they finish.

**Statuses:** `todo` → `doing` → `review` → `done` (or `blocked: reason`)

## Ground rules (every card)
- Unity 6 (6000.0 LTS), built-in render pipeline, **legacy Input Manager**, IMGUI for all UI. C# 9 is the maximum.
- Everything is built from code at runtime. No prefabs, scenes or assets are needed (`CmodGame` boots itself).
- **Contracts** (do not change signatures; add to them only if your card says so):
  `Core/*`, `Entities/Contracts.cs`, `Weapons/Contracts.cs`, `Player/Contracts.cs`, `Player/CmodInput.cs`,
  `UI/Contracts.cs`, `Game/CmodGame.cs` (all under `Assets/Cmod/Scripts/`). Partial methods there must be implemented
  with the exact same signature. Leave default parameter values off the implementation.
- Physics: use `PhysicsCompat` for velocity, drag, physics materials and FindObjectOfType (these were renamed in Unity 6).
  Materials: use `CmodMaterials.Lit/Unlit`. Never call `Shader.Find` directly for lit things.
- Gameplay input goes **only** through `CmodInput` (it returns nothing while UI has focus or a text field is in use).
- IMGUI depth: HUD `GUI.depth = 10`, WindowManager `GUI.depth = 0` (windows on top). Scale GUI with `WindowManager.Scale()`.
- Log with `CmodLog.Info/Warn/Error`. Never let an exception escape `Update`/`OnGUI` in shared systems.
- **Compile check:** run `Tools/check-compile.sh`. Fix every error in *your* files. CS8795 (a partial method with no
  implementation) belonging to another card is expected while that card is still in progress.
- Keep the look **Windows 98**: gray #C0C0C0 face, 3D bevels, navy gradient title bars, teal desktop accents, 11px text.

## Keybinds (to avoid conflicts)
| Key | Owner |
|---|---|
| W A S D, Space, Shift (sprint), Ctrl (crouch), V (noclip), F (flashlight), 1–6 / scroll (weapon select) | C-PLAYER |
| LMB, RMB, R, E (+mouse rotate), scroll while holding | C-WEAPONS (active weapon) |
| Q hold (spawn menu), \` (console), Z (undo), F1 (help) | C-APPS |
| Esc (close the focused window, otherwise toggle the start menu) | C-UI |

## Weapon IDs
`weapon_physgun` (slot 0, **ON HOLD**, see C-PHYSGUN-HOLD), `weapon_toolgun` (slot 1), `weapon_pistol` (slot 2), `weapon_wrench` (slot 3, melee). The default loadout is toolgun, pistol and wrench, with the toolgun selected.

---

## Cards

### C-UI: Win98 widget kit, window manager, taskbar
**Status:** doing
**Owns:** `UI/Win98.cs`, `UI/WindowManager.cs`, `UI/Taskbar.cs`, `UI/MessageBox.cs`, `UI/UiModule.cs`
- Implement every `Win98` partial (UI/Contracts.cs). Use procedural Texture2D, 9-slice GUIStyles (`border`), point filtering,
  and the bevels pixel-exact like Win98 (outer: white/black, inner: light/dark gray). Pressed buttons invert. The checkbox check mark
  is drawn with pixels. Scrollbars are Win98 style. `Icon()` draws simple 16x16 pixel-art icons into cached textures.
- Window frame: 3px raised border, an 18px title bar with the active gradient (inactive when not focused), a 16x16 icon,
  bold white title, and caption buttons [_][X] (14x12 beveled). Drag by the title bar, clamped to the screen. Optional resize grip.
  Clicks must reach only the topmost window under the mouse (use GUI.Window plus z-order, or manual hit-testing that
  consumes events). Clicking a window focuses it and brings it to the front.
- `WindowManager` (MonoBehaviour added in `Ui_Init`): draws windows, then the taskbar, then the start menu and modal message boxes.
  Cursor: `Cursor.lockState = Locked, visible = false` when `!UIHasFocus()`, otherwise unlocked and visible. Apply it every frame
  (also re-lock after the game window regains focus). `ui_scale` convar (archive, 0.75–2.5, default 1).
- Taskbar (always visible, 28px, bottom): a raised "Start" button with the cmod logo icon; buttons for open windows (pressed when
  focused, click toggles minimize or focus); a sunken tray on the right with a clock (HH:mm) and an FPS readout when `cl_showfps` = 1.
- Start menu: classic tall menu with the vertical navy-to-blue "Cmod 98" banner on the left, items with icons and a
  navy hover highlight, and separators between groups. Closes on outside click or Esc. `StartMenu.AddItem/RemoveItem`.
- Esc: close the focused closable window, otherwise toggle the start menu.
- `MessageBox.Show`: modal (dims nothing, but blocks input to other windows), centered, with a Kind icon, wrapped text and buttons
  centered at the bottom (75x23 each). Enter = first button. Esc = last button or -1.
- Keep everything allocation-light (cache GUIContent and styles). Must look right at ui_scale 1 and 2.

### C-PLAYER: player controller, camera, HUD
**Status:** doing
**Owns:** `Player/CmodPlayer.cs`, `Player/Hud.cs`, `Player/PlayerModule.cs`
- Implement the `CmodPlayer` partials. Use a CharacterController (height 1.8, radius 0.35) with the camera at 1.62 (0.9 when crouched).
  Walk 5, sprint 9, crouch 2.5 m/s, jump 5.5 m/s, gravity from `Physics.gravity`, good air control, step offset 0.4.
  Push rigidbodies in OnControllerColliderHit. Noclip (V): fly along the camera direction, Space up, Ctrl down, Shift fast, no collision.
- Mouse look via `CmodInput.LookDelta`, pitch clamped to ±89. Skip it when `ActiveWeapon.BlocksMouseLook`.
  Convars: `sensitivity` (archive, default 2), `m_invert` (archive), `fov` (archive, 60–120, default 80).
- Camera: near clip 0.03. When spawning, disable all other Cameras and AudioListeners in the scene. Add an AudioListener.
  Flashlight F: a spotlight on the camera.
- Weapons: Give/Select/Strip. Number keys select by `Slot`. The scroll wheel cycles weapons unless `ActiveWeapon.UsesScrollWheel`.
  Drive the active weapon: Think (Update), FixedThink (FixedUpdate), PrimaryAttack on LMB down, PrimaryHeld every frame,
  SecondaryAttack on RMB down, Reload on R. All of these use CmodInput. Run `Hooks.WeaponSwitched`.
- Fall damage when landing faster than 14 m/s. Death → respawn after 2 seconds (`Respawn`). `TakeDamage` flashes the HUD red.
- `Player_Spawn(Pose)`: create the "Player" GameObject, set `CmodPlayer.Local`, give the default loadout, select the physgun,
  run `Hooks.PlayerSpawned`. Add the HUD.
- HUD (`GUI.depth = 10`, scaled): a small crosshair; bottom-left a Win98 raised panel "Health" with a progress bar and number;
  bottom-right ammo (Clip/ClipSize) when ClipSize > 0; a weapon-switch popup (a Win98 list of the weapons with the active one
  highlighted, fading after 1.5s); a "NOCLIP" label; call `ActiveWeapon.DrawHUD()`. Keep the HUD above the taskbar.

### C-WEAPONS: physgun, toolgun, tool modes, pistol, wrench
**Status:** doing
**Owns:** `Weapons/*.cs` (except Contracts.cs), `Tools/*.cs`, `Weapons/WeaponsModule.cs`
- Physgun: holding LMB grabs a CmodEntity with a Body (if `Hooks.PhysgunPickup` allows it). Keep the grab offset; move it with velocity
  steering in FixedThink (no teleporting) and damp its angular velocity. The scroll wheel changes the distance (`UsesScrollWheel`).
  E + mouse rotates (`BlocksMouseLook`), and Shift snaps to 45°. RMB while holding freezes it and lets go. R unfreezes the entity under
  the crosshair and everything welded to it. Grabbing a frozen prop unfreezes it. A curved beam LineRenderer (cyan, unlit)
  runs from the muzzle to the grab point, plus a glow sprite at the end. Simple procedural view model (box primitives, cyan glow) on the camera.
- Toolgun (**no view model needed**, user said not to worry about it; if a simple one already exists, leave it and spend no more time on it): LMB/RMB/R call `ToolRegistry.Active` with an aim trace (`Hooks.CanTool` must allow it), and a tracer flash plus click
  sound (procedural AudioClip) on success. HUD: top-left Win98 window-styled panel with the tool name, description and instructions.
- Tool modes (register all of them): **Remover** (LMB remove, RMB remove plus everything constrained to it),
  **Color** (settings: RGB sliders and swatches; LMB apply, RMB copy), **Weld** (two clicks → FixedJoint, R clears constraints, undo),
  **Rope** (two clicks → ConfigurableJoint/SpringJoint limited to its length, LineRenderer rope; settings: slack, width),
  **Balloon** (LMB spawns a floating balloon entity with lift force, roped to the hit point; settings: force, color),
  **Physical Properties** (settings: gravity toggle, material preset dropdown; LMB apply), **Lamp** (spawns a lamp entity with a
  spotlight). Keep constraint bookkeeping in `Tools/Constraints.cs` so Remover, Weld and the physgun's R can find connected entities.
  Every tool action pushes an Undo entry.
- `weapon_pistol`: hitscan with an impulse to rigidbodies, clip 18, reload 1.5s, muzzle flash light, small bullet-hole quads
  (pooled, max 64), procedural sound, recoil kick. `weapon_wrench`: melee 2m with an impulse and a swing animation.
- `Weapons_Register()`: register every weapon and tool.

### C-WORLD: entities, map, default props
**Status:** doing
**Owns:** `Entities/EntityManager.cs`, `Game/WorldBuilder.cs`, `Game/DefaultProps.cs`, `Game/WorldModule.cs`
- Implement the `EntityManager` partials (id counter, live list, `CanSpawnProp`/`EntitySpawned`/`EntityRemoved` hooks, undo, crosshair spawn
  using `CmodPlayer.Local.TraceAim`, collider bounds to lift the prop above the surface). Apply the material preset via `PhysicsCompat.ApplyMaterial`.
- Map "cm_flatgrass98" (original): a 600x600 m ground with a procedural grid texture (light/dark gray, 1 m tiles, accent lines every 8 m),
  a few static structures (platforms, a ramp, stairs, a tower with a lookout, a teal "Cmod 98" monolith), sun light with soft shadows,
  procedural skybox (`Skybox/Procedural` if found, otherwise a camera clear color), sensible ambient and fog. Return a spawn pose.
  Keep the world at least 50 m clear around the spawn.
- `DefaultProps`: about 25 props in categories Construction (plates, beams, blocks), Containers (crates, barrels, cans), Fun (balls,
  rubber ball, ice block, giant ball), Furniture (table, chair, shelf built from child primitives in `OnSpawned`, with colliders on children
  and one Rigidbody on the root). Realistic masses.

### C-APPS: console, spawn menu, options, about, help, base commands, keybinds
**Status:** doing
**Owns:** `UI/Apps/*.cs`, `Game/BaseCommands.cs`, `Game/Keybinds.cs`
- Console window: log lines colored by level in a sunken white scroll area (auto-scroll unless scrolled up), an input field + "Submit" button,
  ↑/↓ history, Tab autocomplete (`ConCommands.Complete`). \` toggles it.
- Spawn menu (large window): hold Q to open, release to close (`spawnmenu_toggle` 1 makes Q a toggle). Tabs: **Props** (category
  ListBox on the left, a grid of buttons with box icons on the right, click = `SpawnPropAtCrosshair`), **Weapons** (grid; click = Give
  + Select), **Tools** (tool list on the left; selecting sets `ToolRegistry.Active` and selects the toolgun; the right side shows
  `DrawSettings`), **Utilities** (Undo, Cleanup, gravity slider, timescale slider, noclip checkbox).
- Options dialog (Win98 property sheet, OK/Cancel/Apply): Mouse (sensitivity, invert), Video (fov, ui_scale, show FPS), Audio
  (volume → AudioListener.volume, `volume` archive convar).
- About ("About Cmod": icon, version, Unity version, memory, "licensed to" the player name). Help: a controls list. F1 opens Help.
- Start menu items: Spawn Menu, Console, Help (group 0); Options (50); Shut Down... (100 → Yes/No MessageBox → quit, stopping play mode in the editor).
- BaseCommands: help, echo, clear, noclip, god, kill, respawn, give, spawn, undo (Z), cleanup, `sv_gravity` (sets Physics.gravity.y, default -15), `host_timescale`, `name`, `cl_showfps`, `spawnmenu_toggle`, `volume`. Keybinds.cs is a MonoBehaviour on the root for Q/\`/Z/F1.

### C-GITHUB: auto-update from GitHub releases, debug logs to GitHub, CI
**Status:** doing — *requested by user*
**Owns:** `Services/*.cs`, `UI/Apps/GitHubSettingsWindow.cs`, `UI/Apps/UpdateWindow.cs`, `.github/workflows/*.yml`, `docs/GITHUB.md`
- `GitHubClient`: UnityWebRequest coroutine helpers (GET/POST JSON, download with progress). Headers: `User-Agent: Cmod-Game`,
  `Accept: application/vnd.github+json`, `Authorization: Bearer <token>` when a token is set. Owner/repo come from `CmodGame.RepoOwner/RepoName`.
- Token: entered by the player in the **GitHub Settings** window (PasswordField, "Test" button showing a MessageBox, saved to PlayerPrefs
  `github_token`). Never logged, never committed, and redacted from uploaded logs. Recommend a fine-grained PAT for this repo only with
  Issues: read/write (plus Contents: read if the repo is private).
- **Local log file**: always write `persistentDataPath/logs/cmod_<yyyyMMdd_HHmmss>.log`, keep the newest 10, and flush often.
- **Log upload** (`LogUploader`): label `cmod-log`. On the first error in a session (when `log_upload_auto` = 1, archive, default 1, and
  a token is set) create one issue: title `[cmod-log] <player> v<version> <platform> – <first error line, 80 chars>`, body = system info
  (version, Unity, OS, CPU, GPU, RAM, resolution) plus the last 300 log lines in a code block. Later errors are batched as **comments** on
  the same issue at most every 60 s, capped at 20 per session. Truncate bodies under 60k chars. Redact the token and the user's home
  directory path. Manual: `log_upload [note]` concommand and a Start menu "Send Debug Log..." with a note dialog. Report the issue URL in the console.
- **Auto-update** (`AutoUpdater`): at startup (`update_auto` archive, default 1) GET `/releases/latest`, compare `tag_name` with
  `Application.version` (`CmodGame.ParseVersion`). If newer, a MessageBox asks to update with Yes/No/Release Notes. Pick the asset for the platform
  (name contains Windows/Linux/macOS, .zip). Download it with the Win98 progress dialog (`UpdateWindow`) into `persistentDataPath/update`, extract it
  (System.IO.Compression), write an updater script (Windows .bat: wait for the PID to exit, robocopy /MIR into the install folder except
  `*_Data/../` user files, relaunch, delete itself; Linux/macOS .sh equivalent), start it and quit. Private repos: download via the asset API
  URL with `Accept: application/octet-stream` and the token. In the editor: check and notify only, never install. `update_check` concommand.
  Start menu "Windows Update..." style item "Cmod Update...".
- CI: `.github/workflows/compile-check.yml` (runs `Tools/check-compile.sh` on push/PR) and `.github/workflows/release.yml`
  (on tag `v*`: game-ci/unity-builder@v4 for StandaloneWindows64 + StandaloneLinux64 with `versioning: Tag`, zip as `Cmod-Windows.zip`
  / `Cmod-Linux.zip`, attach to the GitHub Release; needs secrets UNITY_LICENSE, UNITY_EMAIL, UNITY_PASSWORD).
- `docs/GITHUB.md`: how to make the token, how to cut a release, how Claude reads logs (issues labelled `cmod-log`).
- `Services_Init` (log file + uploader; it runs before the UI exists, so register windows and start menu items in `Updater_Init`).

### C-SOUND: UI and toolgun sounds
**Status:** todo (queued: starts after the current build finishes, because it hooks into C-UI and C-WEAPONS files) — *requested by user*
**Owns:** `Audio/*.cs`, plus small call-site edits in UI and weapon files
- `Sfx` service: every sound is synthesized at startup with `AudioClip.Create` (no audio assets) and played from a small pool of 2D AudioSources,
  plus `Sfx.PlayAt(name, pos)` for 3D sounds. Volume follows the `volume` convar, plus `sfx_ui_volume` (archive). Original sounds only:
  retro 90s-PC-flavoured, but no copies of real Windows sound files.
- UI sounds: button click (short tick), window open (soft rising chirp), window close/minimize (falling chirp), start menu open,
  menu hover (very quiet tick, rate-limited), message box (Info "ding"-style chime, Warning, Error "chord"-style buzz, Question),
  checkbox/radio toggle, tab switch, a startup jingle when the game boots (short and original), and a shutdown jingle on Shut Down.
- Toolgun: fire "zap" (bright click + short noise burst), tool success, tool denied/fail buzz, mode-switch beep, weld "clink", rope "twang",
  remover "poof", balloon "pop" when removed.
- (Physgun sounds: on hold with the physgun.)
- Chat: a short message blip.
- Pistol and wrench: replace any ad-hoc procedural sounds with the shared `Sfx` clips (gunshot, reload, swing, hit).
- Props: a soft physics impact sound on collisions above a speed threshold, chosen by material preset, rate-limited.

### C-CHAT: chat box
**Status:** todo (queued after the current build) — *requested by user*
**Owns:** `UI/Apps/ChatBox.cs`, `Game/ChatService.cs`; adds keybinds Y (all chat) and Enter-to-send
- `ChatService` (static): `Send(string text)`, `Receive(string sender, string text, Color nameColor)`, `event MessageReceived`,
  history (last 100). Today `Send` loops back locally. It is the seam B-NET plugs into later: networking just calls `Receive` for remote
  messages and forwards `Send`. It runs `Hooks.ChatMessage` (returning false blocks the message). Messages starting with `/` or `!` run as a
  console command via `ConCommands.Execute` (for example `/noclip`).
- Chat box (bottom-left, above the HUD health panel, not a taskbar window): Win98 styled. While closed, the last ~8 messages show over the game
  on a translucent sunken panel and fade out after 10 s. Y opens the input (a Win98 TextField with a "Say:" label and a Send button, full history scrollable,
  cursor free); Enter sends, Esc closes without sending; ↑/↓ recall sent lines; 128-char limit. Player names are bold and colored, system messages gray
  ("player joined" etc.). Join/leave and server messages go through `ChatService.System(text)`.
- Esc while chat is open closes the chat only (it must not also toggle the Start menu). Gameplay input stays blocked while typing.
- Plays the C-SOUND chat blip when C-SOUND exists (`Sfx.Play("chat")`).

### C-PHYSGUN-HOLD: physgun on hold
**Status:** todo (apply after the current build) — *requested by user: "don't do the physgun for now"*
- Don't register `weapon_physgun`, and remove it from the default loadout (spawn with the toolgun selected). Move its code to
  `Weapons/_OnHold/Physgun.cs.txt` so it doesn't compile but isn't lost. Remove its keybinds and HUD references, and drop the physgun bullets from C-SOUND.
- Without the physgun, players can still spawn props (spawn menu), weld, rope and remove them (toolgun), and push them by walking into them.

### C-MENU: main menu and P2P server list
**Status:** todo (queued after the current build) — *requested by user*
**Owns:** `UI/Menu/*.cs`, `Net/LobbyService.cs`, `Net/ILobbyProvider.cs`; changes the boot flow in `Game/CmodGame.cs` (lead approves the contract change)
- **Boot flow:** the game starts at the main menu, not in the world. `CmodGame` gets states `MainMenu` → `Loading` → `InGame`.
  The world, player and gameplay windows are built only when entering a game. "Disconnect / Main Menu" in the Start menu tears
  them down (cleanup entities, destroy the player and world root) and returns to the menu.
- **Main menu = a Win98 desktop:** a teal desktop with a background camera slowly orbiting a small diorama of props (or a plain teal desktop),
  a big "Cmod 98" logo, and double-clickable desktop icons with labels: **Start New Game**, **Find Servers**, **Options**, **GitHub / Logs**,
  **Shut Down**. The taskbar and Start menu also work here. Version text and "Update available" balloon tip at the bottom right (from C-GITHUB).
- **Start New Game** dialog: map dropdown (cm_flatgrass98), server name, max players (2–16), password (optional), "Friends only"
  checkbox, and Single player / Host P2P radio. Host P2P creates a lobby via `LobbyService` and loads the map.
- **Server Browser** window (Win98 ListView): tabs **Friends | Internet | LAN | Favorites | History**; sortable columns: 🔒, Name, Map,
  Players, Ping, Host; double-click or **Connect** to join; **Refresh** with a Win98 progress bar; a filter row (has players, not full, no
  password); a password prompt for locked lobbies; right-click → Add to Favorites; "Join by code" textbox (lobby ID / Steam ID).
  Favorites and history are saved to PlayerPrefs.
- **Networking seam (no real networking yet, that's B-NET):** `ILobbyProvider { RequestList(tab, callback); CreateLobby(settings, cb);
  Join(lobbyId, password, cb); Leave(); }` and `LobbyInfo { Id, Name, Map, Players, MaxPlayers, Ping, HostName, Locked, FriendsOnly }`.
  Ship a `LocalLobbyProvider` (shows your own hosted lobby; LAN tab does a UDP broadcast discovery on port 27015 so two copies on one network
  find each other in the list; joining shows "Networking arrives with B-NET" for now). B-NET later adds `SteamLobbyProvider`
  (Steam matchmaking lobbies, friends' lobbies via Steam friends) and wires the actual connect.

### C-EDITOR: editor setup menu and README
**Status:** doing
**Owns:** `Assets/Cmod/Editor/*.cs`, `README.md`
- Menu `Cmod/Setup Project`: create and save `Assets/Cmod/Scenes/Main.unity` (empty scene), add it to Build Settings, set the
  product name "Cmod", company, and Active Input Handling to Old or Both (via SerializedObject on ProjectSettings `activeInputHandler`),
  and the version from the latest git tag if available.
- README: what Cmod is, how to open it (Unity 6 LTS), controls, the folder layout, the board workflow, and releases.

---

## Backlog (from PLAN.md, not started)
- B-NET: FishNet + Steam P2P multiplayer (implements `SteamLobbyProvider` for C-MENU, plugs remote messages into `ChatService`)
- B-LUA: MoonSharp Lua runtime with the GMod-like API
- B-MODS: live mod loading, `require("owner/repo/mod")`, host permissions
- B-CPAK: compressed mod bundles, chunk cache, P2P chunk sharing
- B-AIKIT: mod template repo with CLAUDE.md, API.md, examples
