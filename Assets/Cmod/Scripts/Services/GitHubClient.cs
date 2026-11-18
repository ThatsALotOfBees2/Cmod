using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Cmod.Services
{
    /// <summary>Result of a GitHub request. Always delivered on the main thread.</summary>
    public sealed class GitHubResponse
    {
        /// <summary>HTTP status code, or 0 when the request never got a response (offline, DNS, timeout).</summary>
        public long Status;
        /// <summary>Response body as text (empty for file downloads).</summary>
        public string Text = "";
        /// <summary>Transport error from UnityWebRequest, or null.</summary>
        public string TransportError;
        public Dictionary<string, string> Headers;
        /// <summary>True when the user cancelled the request (downloads).</summary>
        public bool Cancelled;

        public bool Ok => !Cancelled && Status >= 200 && Status < 300 &&
                          (TransportError == null || TransportError.Length == 0);

        public string Header(string name)
        {
            if (Headers == null || name == null) return null;
            foreach (var kv in Headers)
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }

        /// <summary>A short human-readable description of what went wrong (GitHub's "message" if present).</summary>
        public string ErrorMessage
        {
            get
            {
                if (Cancelled) return "Cancelled.";
                if (Ok) return null;
                string gh = null;
                if (!string.IsNullOrEmpty(Text) && Text.TrimStart().StartsWith("{"))
                {
                    try { gh = JsonUtility.FromJson<GitHubErrorJson>(Text)?.message; }
                    catch (Exception) { /* not GitHub's error JSON */ }
                }
                if (Status == 0) return "Could not reach GitHub: " + (TransportError ?? "no response");
                var hint = "";
                if (Status == 401) hint = " (the token is invalid or expired)";
                else if (Status == 403 && Header("X-RateLimit-Remaining") == "0") hint = " (rate limit reached, try again later)";
                else if (Status == 403 || Status == 404) hint = " (the token may lack access to this repo)";
                return $"HTTP {Status}: {(string.IsNullOrEmpty(gh) ? TransportError ?? "error" : gh)}{hint}";
            }
        }
    }

    /// <summary>Progress and cancel flag shared between a download coroutine and the progress UI.</summary>
    public sealed class DownloadProgress
    {
        public ulong Bytes;
        /// <summary>Expected size in bytes (0 if unknown).</summary>
        public ulong Total;
        public float Fraction => Total > 0 ? Mathf.Clamp01((float)((double)Bytes / Total)) : 0f;
        /// <summary>Set to true to abort the download.</summary>
        public bool CancelRequested;
        public float StartTime;
        /// <summary>Average speed since the start, in bytes per second.</summary>
        public double BytesPerSecond
        {
            get
            {
                var t = Time.realtimeSinceStartup - StartTime;
                return t > 0.25f ? Bytes / (double)t : 0d;
            }
        }
    }

    /// <summary>
    /// Minimal GitHub REST client on UnityWebRequest coroutines. Requests send
    /// "User-Agent: Cmod-Game", "Accept: application/vnd.github+json" and, when a token is set,
    /// "Authorization: Bearer &lt;token&gt;". The token is never logged.
    /// Use with <c>yield return GitHubClient.Get(...)</c> inside a coroutine, or
    /// <c>GitHubClient.Start(GitHubClient.Get(...))</c> from normal code.
    /// </summary>
    public static class GitHubClient
    {
        public const string ApiBase = "https://api.github.com";
        public const string TokenPrefsKey = "github_token";
        public const string UserAgent = "Cmod-Game";
        public const string AcceptJson = "application/vnd.github+json";
        public const string AcceptBinary = "application/octet-stream";
        public const int ApiTimeoutSeconds = 30;

        public static string Owner => CmodGame.RepoOwner;
        public static string Repo => CmodGame.RepoName;
        /// <summary>"https://api.github.com/repos/owner/repo"</summary>
        public static string RepoApi => $"{ApiBase}/repos/{Owner}/{Repo}";
        /// <summary>"https://github.com/owner/repo"</summary>
        public static string RepoWeb => $"https://github.com/{Owner}/{Repo}";
        /// <summary>Page that creates a fine-grained personal access token.</summary>
        public const string NewTokenUrl = "https://github.com/settings/personal-access-tokens/new";

        /// <summary>Fired after the saved token changes (set or cleared).</summary>
        public static event Action TokenChanged;

        static string s_token;
        static bool s_tokenLoaded;

        /// <summary>The saved token (PlayerPrefs "github_token"), or "" when none. Setting saves it;
        /// setting null or whitespace deletes it.</summary>
        public static string Token
        {
            get
            {
                if (!s_tokenLoaded)
                {
                    s_tokenLoaded = true;
                    try { s_token = PlayerPrefs.GetString(TokenPrefsKey, ""); }
                    catch (Exception) { s_token = ""; }
                }
                return s_token ?? "";
            }
            set
            {
                var v = (value ?? "").Trim();
                s_token = v;
                s_tokenLoaded = true;
                if (v.Length == 0) PlayerPrefs.DeleteKey(TokenPrefsKey);
                else PlayerPrefs.SetString(TokenPrefsKey, v);
                PlayerPrefs.Save();
                try { TokenChanged?.Invoke(); } catch (Exception ex) { CmodLog.Warn("[github] " + ex.Message); }
            }
        }

        public static bool HasToken => Token.Length > 0;

        /// <summary>Starts a GitHub coroutine on the service host.</summary>
        public static Coroutine Start(IEnumerator routine) => ServiceHost.Run(routine, "github");

        /// <summary>Makes "/repos/..." style paths absolute.</summary>
        public static string Url(string pathOrUrl)
        {
            if (string.IsNullOrEmpty(pathOrUrl)) return ApiBase;
            if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return pathOrUrl;
            return ApiBase + (pathOrUrl.StartsWith("/") ? pathOrUrl : "/" + pathOrUrl);
        }

        /// <summary>GET a JSON endpoint. `tokenOverride` (non-null) replaces the saved token, e.g. to test a typed one.</summary>
        public static IEnumerator Get(string pathOrUrl, Action<GitHubResponse> done, string tokenOverride = null)
        {
            return Send("GET", pathOrUrl, null, done, tokenOverride);
        }

        /// <summary>POST a JSON body (a string produced by JsonUtility.ToJson).</summary>
        public static IEnumerator PostJson(string pathOrUrl, string json, Action<GitHubResponse> done, string tokenOverride = null)
        {
            return Send("POST", pathOrUrl, json ?? "{}", done, tokenOverride);
        }

        static IEnumerator Send(string method, string pathOrUrl, string json, Action<GitHubResponse> done, string tokenOverride)
        {
            var url = Url(pathOrUrl);
            using (var req = new UnityWebRequest(url, method))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                if (json != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)) { contentType = "application/json" };
                    req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                }
                ApplyHeaders(req, AcceptJson, tokenOverride ?? Token);
                req.timeout = ApiTimeoutSeconds;
                yield return req.SendWebRequest();
                Deliver(done, ToResponse(req, true));
            }
        }

        /// <summary>
        /// Downloads a file to `destPath` with progress. When `assetApiUrl` is true, `url` is a release
        /// asset API URL (".../releases/assets/123"): it is requested with "Accept: application/octet-stream"
        /// and the token (works for private repos), and the redirect to the storage host is followed
        /// manually WITHOUT the Authorization header (the storage host rejects it, and it must not leak).
        /// </summary>
        public static IEnumerator Download(string url, bool assetApiUrl, string destPath, DownloadProgress progress,
            Action<GitHubResponse> done)
        {
            progress = progress ?? new DownloadProgress();
            progress.StartTime = Time.realtimeSinceStartup;
            string target = url;

            if (assetApiUrl)
            {
                // Step 1: ask the API for the asset without following the redirect.
                GitHubResponse first;
                using (var req = new UnityWebRequest(url, "GET"))
                {
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.redirectLimit = 0;
                    ApplyHeaders(req, AcceptBinary, Token);
                    req.timeout = ApiTimeoutSeconds;
                    yield return req.SendWebRequest();
                    first = ToResponse(req, false);
                    if (progress.CancelRequested) { Deliver(done, new GitHubResponse { Cancelled = true }); yield break; }

                    var location = first.Header("Location");
                    if (first.Status >= 300 && first.Status < 400 && !string.IsNullOrEmpty(location))
                    {
                        target = location; // signed storage URL: download it without auth below
                    }
                    else if (first.Ok && req.downloadHandler.data != null && req.downloadHandler.data.Length > 0)
                    {
                        // Served directly (no redirect): write the bytes we already have.
                        var bytes = req.downloadHandler.data;
                        try
                        {
                            System.IO.File.WriteAllBytes(destPath, bytes);
                            progress.Bytes = progress.Total = (ulong)bytes.Length;
                        }
                        catch (Exception ex)
                        {
                            Deliver(done, new GitHubResponse { Status = 0, TransportError = "Could not write file: " + ex.Message });
                            yield break;
                        }
                        Deliver(done, first);
                        yield break;
                    }
                    else
                    {
                        Deliver(done, first);
                        yield break;
                    }
                }
            }

            // Step 2: the actual file transfer, straight to disk.
            using (var req = new UnityWebRequest(target, "GET"))
            {
                var handler = new DownloadHandlerFile(destPath) { removeFileOnAbort = true };
                req.downloadHandler = handler;
                // Only send the token to GitHub itself, never to the storage/CDN host of a redirect.
                bool isApi = target.StartsWith(ApiBase, StringComparison.OrdinalIgnoreCase);
                ApplyHeaders(req, AcceptBinary, isApi ? Token : null);
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    if (progress.CancelRequested)
                    {
                        req.Abort();
                        Deliver(done, new GitHubResponse { Cancelled = true });
                        yield break;
                    }
                    progress.Bytes = req.downloadedBytes;
                    if (progress.Total == 0)
                    {
                        var len = req.GetResponseHeader("Content-Length");
                        if (ulong.TryParse(len, out var total)) progress.Total = total;
                    }
                    yield return null;
                }
                progress.Bytes = req.downloadedBytes;
                if (progress.Total == 0) progress.Total = progress.Bytes;
                Deliver(done, ToResponse(req, false));
            }
        }

        static void ApplyHeaders(UnityWebRequest req, string accept, string token)
        {
            req.SetRequestHeader("User-Agent", UserAgent);
            req.SetRequestHeader("Accept", accept);
            req.SetRequestHeader("X-GitHub-Api-Version", "2022-11-28");
            if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", "Bearer " + token);
        }

        static GitHubResponse ToResponse(UnityWebRequest req, bool readText)
        {
            var r = new GitHubResponse
            {
                Status = req.responseCode,
                Headers = req.GetResponseHeaders(),
            };
            if (req.result == UnityWebRequest.Result.ConnectionError ||
                req.result == UnityWebRequest.Result.DataProcessingError)
                r.TransportError = req.error ?? "connection error";
            else if (req.result == UnityWebRequest.Result.ProtocolError && (r.Status < 300 || r.Status >= 400))
                r.TransportError = req.error;
            if (readText)
            {
                try { r.Text = req.downloadHandler?.text ?? ""; }
                catch (Exception) { r.Text = ""; }
            }
            return r;
        }

        static void Deliver(Action<GitHubResponse> done, GitHubResponse r)
        {
            if (done == null) return;
            try { done(r); }
            catch (Exception ex) { CmodLog.Warn($"[github] callback failed: {ex.GetType().Name}: {ex.Message}"); }
        }

        /// <summary>JsonUtility.FromJson that returns null instead of throwing.</summary>
        public static T ParseJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch (Exception) { return null; }
        }
    }

    // ---- JSON shapes (only the fields Cmod uses; JsonUtility ignores the rest) -------------------

    [Serializable]
    public sealed class GitHubRelease
    {
        public string tag_name;
        public string name;
        public string body;
        public string html_url;
        public string published_at;
        public bool draft;
        public bool prerelease;
        public GitHubAsset[] assets;
    }

    [Serializable]
    public sealed class GitHubAsset
    {
        public string name;
        /// <summary>API URL (download with Accept: application/octet-stream; works for private repos).</summary>
        public string url;
        /// <summary>Public download URL (public repos only).</summary>
        public string browser_download_url;
        public string content_type;
        public long size;
    }

    [Serializable]
    public sealed class GitHubIssue
    {
        public int number;
        public string html_url;
        public string title;
    }

    [Serializable]
    public sealed class GitHubRepoPermissions
    {
        public bool admin;
        public bool push;
        public bool pull;
    }

    [Serializable]
    public sealed class GitHubRepoJson
    {
        public string full_name;
        public bool @private;
        public bool has_issues;
        public GitHubRepoPermissions permissions;
    }

    [Serializable]
    internal sealed class GitHubErrorJson
    {
        public string message;
        public string documentation_url;
    }

    [Serializable]
    internal sealed class NewIssueJson
    {
        public string title;
        public string body;
        public string[] labels;
    }

    [Serializable]
    internal sealed class NewCommentJson
    {
        public string body;
    }

    [Serializable]
    internal sealed class NewLabelJson
    {
        public string name;
        public string color;
        public string description;
    }
}
