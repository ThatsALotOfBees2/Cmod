# Cmod and GitHub

Cmod uses its GitHub repository (`ThatsALotOfBees2/Cmod`, set in `CmodGame.RepoOwner` / `RepoName`) for three things:

1. **Debug logs**: errors are uploaded as issues labelled `cmod-log`, so they can be read and fixed.
2. **Auto-update**: the game checks the latest GitHub Release at startup and can install it.
3. **CI**: every push is compile-checked, and every `v*` tag builds and publishes a release.

The code lives in `Assets/Cmod/Scripts/Services/` (`GitHubClient`, `LogFile`, `LogUploader`, `AutoUpdater`,
`UpdateInstaller`, `Redaction`) and the windows in `Assets/Cmod/Scripts/UI/Apps/GitHubSettingsWindow.cs` and `UpdateWindow.cs`.

---

## 1. Make a GitHub token (each player, once)

Log upload needs a token. Updates need one only if the repository is private.

1. Open <https://github.com/settings/personal-access-tokens/new> (or press **Create Token...** in the game's
   **Start > GitHub Settings...** window).
2. **Token name:** `Cmod`. **Expiration:** whatever you like (when it expires, uploads stop until you make a new one).
3. **Resource owner:** the owner of the repo (`ThatsALotOfBees2`).
4. **Repository access:** *Only select repositories* → `Cmod`. Don't give it access to anything else.
5. **Permissions → Repository permissions:**
   - **Issues: Read and write** (to upload logs)
   - **Contents: Read-only** (only if the repo is private, to download updates)
   - Leave everything else at *No access*.
6. **Generate token** and copy it (it starts with `github_pat_`).
7. In Cmod: **Start > GitHub Settings...**, paste it into **Token**, press **Test**, then **Save**.

For a private repository, an owner may need to approve the fine-grained token, and players need to be collaborators.

Where the token goes:

- It is stored only in Unity's PlayerPrefs on that computer (`github_token`). It's never written to the log,
  never committed, and the field only shows `*****`.
- Every uploaded text goes through `Redaction.Redact`, which removes the token (and anything that looks like
  a GitHub token) and replaces your home folder path with `~`.
- **Clear** in GitHub Settings deletes it. You can also revoke it on GitHub at any time.

## 2. Debug logs

### Local log files (always on)

Every session writes `persistentDataPath/logs/cmod_<yyyyMMdd_HHmmss>.log`. Only the newest 10 are kept.
The file starts with system info, contains every console line (including Unity's own log), adds full stack traces
for exceptions, and is flushed at least every 0.5 s (immediately on errors).

| OS | Folder |
|---|---|
| Windows | `%USERPROFILE%\AppData\LocalLow\<Company>\Cmod\logs` |
| Linux | `~/.config/unity3d/<Company>/Cmod/logs` |
| macOS | `~/Library/Application Support/<Company>/Cmod/logs` |

`log_folder` in the console prints the path and opens the folder. So does **Open Log Folder** in GitHub Settings.

### Uploads

| | |
|---|---|
| Automatic | With `log_upload_auto 1` (the default, saved) **and** a token set, the **first error** of a session creates one issue. |
| Issue title | `[cmod-log] <player> v<version> <platform> – <first error line, 80 chars>` |
| Issue body | System info (version, Unity, OS, CPU, GPU, RAM, resolution), the first error, and the last 300 log lines in a code block. |
| Later errors | Batched into **comments** on the same issue, at most one every 60 s and at most 20 per session. Repeated identical lines are collapsed (`(repeated N more times)`). |
| Size | Bodies are kept under 60,000 characters (the oldest lines are cut first). |
| Manual | `log_upload [note]` in the console, or **Start > Send Debug Log...** (asks for a note). It adds to this session's issue, or creates one. |
| Result | The issue URL is printed in the console (`[log upload] Log uploaded: https://...`). |

Turn automatic uploads off with `log_upload_auto 0` or the checkbox in GitHub Settings. Upload failures are logged as
warnings (never errors, so a failed upload can't trigger another); automatic uploads stop after 3 failures in a session.

### How Claude reads the logs

The issues are the bug inbox. To find and fix problems:

1. List open issues with the label `cmod-log`:
   `gh issue list --repo ThatsALotOfBees2/Cmod --label cmod-log`
   (or the GitHub MCP tools: list issues with `labels: ["cmod-log"]`).
2. Read the issue **and its comments**: `gh issue view <n> --repo ThatsALotOfBees2/Cmod --comments`.
   The title has the player, version and platform, plus the first error. The body has the system table and
   the log. Lines look like `[HH:mm:ss.fff] E text`, where the letter is `I` (info), `W` (warning) or `E` (error),
   and exception stack traces are indented under the error line.
3. Check the version in the title against the code. A log from an old version may already be fixed.
4. Fix the bug, mention the issue in the commit (`Fixes #<n>`), and close duplicate issues with a link to the fixed one.

## 3. Auto-update

- At startup (`update_auto 1`, the default, saved) the game requests `GET /repos/<owner>/<repo>/releases/latest` and
  compares its `tag_name` with `Application.version` (using `CmodGame.ParseVersion`, so `v1.2.3` = `1.2.3`).
  Drafts and pre-releases are never offered, because `/releases/latest` skips them.
- If the release is newer, a message box asks **Yes / No / Release Notes**.
- **Yes** picks the asset for the platform (name contains `Windows`, `Linux` or `macOS` and ends in `.zip`), downloads it
  into `persistentDataPath/update` with a Win98 progress dialog, extracts it, and writes an updater script:
  - **Windows** `cmod_update.bat`: waits for the game's process to exit, then `robocopy /MIR` for each top-level folder of the
    new build (`Cmod_Data`, `MonoBleedingEdge`, ...), copies the top-level files, relaunches `Cmod.exe` and deletes itself.
  - **Linux/macOS** `cmod_update.sh`: does the same with `rsync -a --delete` (or `cp -R`), restores the executable bit, and relaunches.
  - Only what the new build contains is replaced. Other files in the install folder are left alone, and saves,
    settings and logs live outside it. The script's output goes to `persistentDataPath/update/updater.log`. The next
    launch reports in the console whether the update worked.
- Private repos: with a token, the zip is downloaded through the asset API (`Accept: application/octet-stream`).
  The token is not sent to the storage host GitHub redirects to.
- In the **Unity Editor** the game only checks and tells you. It never installs.
- If the install folder isn't writable (for example Program Files), the dialog says so: move Cmod or update manually.
- **Start > Cmod Update...** or `update_check` checks on demand.

## 4. Cutting a release

One-time setup: add the repository secrets **UNITY_LICENSE**, **UNITY_EMAIL** and **UNITY_PASSWORD**
(Settings > Secrets and variables > Actions). See <https://game.ci/docs/github/activation> to get the license file.

Then, for each release:

```sh
git tag v0.2.0          # semantic version: vMAJOR.MINOR.PATCH (a "-beta" suffix makes a pre-release)
git push origin v0.2.0
```

`.github/workflows/release.yml` then:

1. builds `StandaloneWindows64` and `StandaloneLinux64` with `game-ci/unity-builder@v4`, using `versioning: Tag`
   so `Application.version` matches the tag,
2. zips each build as `Cmod-Windows.zip` / `Cmod-Linux.zip` (build files at the zip root),
3. creates the GitHub Release for the tag with generated notes and attaches both zips.

Edit the release notes on GitHub afterwards if you like. The game shows them under **Release Notes**.
Players running an older version get the update prompt the next time they start the game.

`.github/workflows/compile-check.yml` runs `Tools/check-compile.sh` on every push and pull request.
It compiles the runtime scripts against Unity reference assemblies, with no Unity license needed.

## Console reference

| Command / convar | |
|---|---|
| `log_upload [note]` | Upload the recent log now (needs a token) |
| `log_upload_auto 0/1` | Automatic error upload (saved, default 1) |
| `log_folder` | Print and open the log folder |
| `update_check` | Check for a newer release now |
| `update_auto 0/1` | Check at startup (saved, default 1) |
| `github_settings` | Open the GitHub Settings window |
