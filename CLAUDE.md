# CLAUDE.md — minit-unity

Unity project hosting the **Minit Games Unity SDK** UPM package. Lets Unity creators export an existing WebGL game to a Minit-compliant ZIP via a C# bridge to the host-injected `window.minit` runtime.

GitHub repo: `Minit-Games/minit-unity` (private)

This repo is a **gitignored sibling** of `minit-root` — it lives at `external/minit-unity/` inside the `minit-root` checkout but is an independent git repo (not a submodule). It follows the same `develop` + `master` branch flow as the other Minit sibling repos (`minit-sdk`, `minit-mcp`, `minit-db`, `minit-terraform`).

---

## Unity version

| Field | Value |
|---|---|
| Editor version | **6000.3.16f1** |
| Package Unity floor | **6000.0** (minimum declared in `package.json`) |

Unity 6 is the floor because reliable mobile-browser WebGL (WKWebView on iOS, Chrome on Android) only works from Unity 6 onwards.

WebGPU is **intentionally not used** — WebGL2 only. WebGPU is unreliable in iOS WKWebView and is not guaranteed to be available in the embedded browsers the Minit platform uses on all supported devices.

**Multithreading must be OFF.** Threaded WebGL builds require `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Embedder-Policy: require-corp` response headers (needed for `SharedArrayBuffer`). The Minit platform does not serve those headers, so any build with multithreading enabled will crash inside the WKWebView. Always leave **Player Settings → WebGL → Other Settings → Enable Native WebAssembly / Use Threads** disabled.

---

## Project layout

```
Assets/
└── WebGLTemplates/
    └── Minit/
        └── index.html        Custom WebGL template — viewport-filling canvas, no loader chrome, touch-action:none.
                              Select in Player Settings → WebGL → Resolution and Presentation → WebGL Template → Minit.

Packages/games.minit.unity/
├── package.json              (name: games.minit.unity, version: 0.1.0, unity: 6000.0)
├── Runtime/
│   ├── Minit.cs              static facade — ReportResult / LoadingDone / GetConfigValue
│   ├── MinitReady.cs         drop-in MonoBehaviour that fires LoadingDone on first frame
│   ├── MinitBridge.jslib     JS bridge: calls window.minit.reportResult / loadingDone; reads config from URLSearchParams
│   └── Minit.Runtime.asmdef  assembly definition (no editor-only references)
└── Editor/
    ├── MinitBuild.cs             Minit → Build for Minit menu; MinitBuild.BuildAndZip() is the reusable build+zip entry point (returns the ZIP path, or null on guard/build failure)
    ├── MinitEnvironment.cs       MinitEnvironment (Dev/Prod) enum + MinitEnvironments.BackendBaseUrl()/ConsoleBaseUrl()
    ├── MinitSession.cs           MinitSession — environment (EditorPrefs) + access token/identity (SessionState, in-memory only)
    ├── MinitHttp.cs              Dependency-free GET/POST-JSON HttpClient wrapper shared by MinitAuth and MinitDropsClient
    ├── MinitAuth.cs              Loopback browser-handoff login (LoginAsync), EnsureAccessTokenAsync, Logout
    ├── MinitDropsClient.cs       Drops/upload REST client — CreateDraftAsync, CreateUploadUrlAsync, UploadZipToS3Async, PollProcessingAsync (polls upload-history, not GET /drops), BuildConsoleDropUrl
    └── MinitLoginUploadWindow.cs Minit → Login & Upload editor window (DROP-3224) — see below
```

`Packages/manifest.json` and `Packages/packages-lock.json` are the Unity UPM package manifests for the host project — they are not part of the SDK package itself.

---

## Design decisions

**No `@minit-games/sdk` npm dependency.** The bridge calls `window.minit` directly from `MinitBridge.jslib`. This keeps the Unity package self-contained — there is no npm install step, no build pipeline, and no JavaScript bundler. The C# API surface maps 1:1 to the same `window.minit` contract that the JavaScript SDK also targets, so the behaviour is identical from the host's perspective.

**Namespace.** Both `Minit` and `MinitReady` live in the `MinitGames` namespace — add `using MinitGames;` to any creator script that references them.

**Config source.** `Minit.GetConfigValue` reads URL query parameters (via `URLSearchParams(window.location.search)` in the jslib), mirroring the `@minit-games/sdk` JS implementation. The key `"userData"` is reserved and always returns the default value.

**All calls are no-ops outside WebGL.** The `#if UNITY_WEBGL && !UNITY_EDITOR` guard means `Minit.*` calls compile to `Debug.Log(...)` in the editor and in non-WebGL build targets. Safe to call from any script regardless of build target.

---

## Login & Upload editor window (DROP-3224)

**Minit → Login & Upload** opens an in-editor window that lets a creator authenticate and ship a drop ZIP without leaving Unity. Implementation: `Packages/games.minit.unity/Editor/MinitLoginUploadWindow.cs`, built on `MinitEnvironment.cs` / `MinitSession.cs` / `MinitHttp.cs` / `MinitAuth.cs` / `MinitDropsClient.cs`.

**Environment.** A Dev/Prod popup bound to `MinitSession.Environment` (persisted via `EditorPrefs`, so it survives Editor restarts). Dev and Prod are different backends *and* different consoles — switching while logged in clears the session, since the stored token belongs to the old backend.

**Auth flow — loopback browser handoff, no api key.** Logging in opens the system browser at `console[-dev].minit.games/unity/authorize`, which (relying on the browser's existing Cognito session) redirects back to a local loopback listener with a Cognito token; that token is exchanged for backend tokens via the public `POST /auth/console` (authorizer: None). There is no api-key-based "refresh" path and `POST /users/refresh` is never called — when the ~15-minute access token expires, `MinitAuth.EnsureAccessTokenAsync` silently re-runs the exact same browser handoff. As long as the browser's Cognito session is still alive this is invisible to the creator (no prompt); if it has lapsed, the browser prompts for sign-in again. This removes the need for any long-lived secret inside the Unity tooling.

**Token storage.** The access token and user identity live in `SessionState` — in-memory for the life of the Editor process, never written to disk, cleared on logout or Editor restart. Storage is unencrypted; that's an accepted trade-off for a developer-facing editor tool holding a short-lived (~15 min) token, not a production credential store.

**Upload flow.** Once logged in:
1. **ZIP source** — either **Build for Minit now** (`MinitBuild.BuildAndZip()`, the same build+zip logic as the standalone `Minit → Build for Minit` menu, refactored in DROP-3224 to return the ZIP path instead of only revealing it in Finder/Explorer) or **Select ZIP…** to point at an existing archive.
2. **Target draft** — either create a new draft (title, description, result type, reverse-sorting) via `POST /drops`, or paste the id of an existing draft. **There is no list-drafts client method as of this cut** — pasting an id is the simplest "pick an existing draft" path; a picker is a natural follow-up once a list-drops client method exists.
3. **Upload** — `CreateUploadUrlAsync` (enforces the 50 MB cap pre-flight and mirrors it server-side) → `UploadZipToS3Async` (direct presigned POST to S3, no bearer token) → `PollProcessingAsync` (polls `GET /upload-history/drops/{dropId}?limit=1` — the same authoritative signal minit-web's UploadTracker polls — every ~2s/~60s cap until the newest upload record's `status` reaches `completed` or `failed`; an earlier version derived "done" from `streamingUrl`/`screenshotUrl` presence on `GET /drops/{dropId}`, which was wrong — the backend always populates both fields at create time, so that check reported success on the first poll and could never surface a ZIP-processing failure) → a **View in console** link built from `MinitDropsClient.BuildConsoleDropUrl`.

**`resultType` values.** `"score" | "time" | "group"` — confirmed against `minit-root/docs/api.md` (`POST /drops`). The window exposes them as a popup, defaulting to `"score"`.

---

## Validation (no automated test suite)

There is no automated C# test suite. Validation steps:

1. Open the project in Unity 6000.3.x.
2. Open the scene under `Assets/` and enter Play Mode — verify that `Minit.LoadingDone()` and `Minit.ReportResult(...)` log to the Console without errors.
3. In **Project Settings → Player → WebGL → Resolution and Presentation → WebGL Template**, select **Minit**. The identifier is `PROJECT:Minit`. Confirm no compile errors appear.
4. Build for WebGL (File → Build Settings → WebGL → Build). Inspect the emitted `index.html` — it should have no progress bar / loader / footer markup and should contain the **actual filenames** that Unity substituted for the template macros (e.g. `game.loader.js`, `game.data`, `game.framework.js`). The raw `{{{ LOADER_FILENAME }}}` / `{{{ DATA_FILENAME }}}` / `{{{ FRAMEWORK_FILENAME }}}` placeholders are replaced during the build; they must NOT appear in the output file.
5. Upload the resulting ZIP to the Minit creator console (dev environment).
6. Play the game inside the Minit feed and confirm: the game is hidden during boot (until `loadingDone` fires), then revealed; `reportResult` triggers the result screen with the correct score and flavor text.

The **Minit → Build for Minit** editor menu (DROP-2022; `Packages/games.minit.unity/Editor/MinitBuild.cs`) replaces steps 4–5: it applies compliant Player Settings, builds WebGL with the Minit template, and packages the result as `Build/<Product>_minit.zip`.

When checking device behaviour, also verify portrait orientation and touch-only input on a real mobile device or iOS Simulator.

**Minit → Login & Upload** (DROP-3224) validation:

1. Open **Minit → Login & Upload**. Confirm the Environment popup defaults to **Dev** and the window shows the login view.
2. Click **Log in with Minit** — a browser window opens at `console-dev.minit.games/unity/authorize`; the button shows "Waiting for browser sign-in…" and is disabled while the flow is in progress. After signing in, Unity should regain focus and show the logged-in header (name/email) without further interaction.
3. Toggle the Environment popup while logged in — confirm it logs out immediately and returns to the login view.
4. Log back in, then click **Build for Minit now** — confirm it builds and shows the resulting ZIP path + size, and that the window's ZIP source, unlike the standalone **Minit → Build for Minit** menu, does NOT reveal the file in Finder/Explorer.
5. Alternatively click **Select ZIP…** and pick an existing `Build/<Product>_minit.zip` — confirm the same path/size display appears.
6. Fill in **Create new draft** (Title required) and click **Upload to Minit** — confirm the progress bar/status text advances through "Creating draft…" → "Requesting upload URL…" → "Uploading…" → "Processing… (...)" → a success message with a working **Open in console** button that opens the drop in the creator console.
7. Repeat using **Use existing draft ID** with a previously-created draft's id — confirm it skips straight to the upload-URL step.
8. Force a failure (e.g. log out mid-upload by editing `SessionState` externally, or point at a bogus draft id) and confirm the failure surfaces inline instead of the Unity Editor showing an unhandled exception.
9. Close the window while an upload is in flight — confirm the Console shows no errors from a continuation writing to a destroyed window.

---

## Branch flow

Follows the same pattern as all other Minit sub-repos:

- **Feature branches** fork from `develop` with prefix `feature/DROP-XXXX-<slug>`.
- **Bug-fix branches** use `fix/DROP-XXXX-<slug>`.
- **PRs target `develop`** and are squash-merged.
- **`develop` → `master`** is a local fast-forward merge pushed directly (never a squash PR):
  ```bash
  git checkout master
  git pull
  git merge --ff-only develop
  git push
  ```
- No `chore/` or `hotfix/` prefixes — use `fix/` for hotfixes; the PR base branch (master vs develop) carries that signal.

For the full coordinated release-train flow across all 5 repos, see `minit-root/docs/release-workflow.md`.
