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
```

`Packages/manifest.json` and `Packages/packages-lock.json` are the Unity UPM package manifests for the host project — they are not part of the SDK package itself.

---

## Design decisions

**No `@minit-games/sdk` npm dependency.** The bridge calls `window.minit` directly from `MinitBridge.jslib`. This keeps the Unity package self-contained — there is no npm install step, no build pipeline, and no JavaScript bundler. The C# API surface maps 1:1 to the same `window.minit` contract that the JavaScript SDK also targets, so the behaviour is identical from the host's perspective.

**Namespace.** Both `Minit` and `MinitReady` live in the `MinitGames` namespace — add `using MinitGames;` to any creator script that references them.

**Config source.** `Minit.GetConfigValue` reads URL query parameters (via `URLSearchParams(window.location.search)` in the jslib), mirroring the `@minit-games/sdk` JS implementation. The key `"userData"` is reserved and always returns the default value.

**All calls are no-ops outside WebGL.** The `#if UNITY_WEBGL && !UNITY_EDITOR` guard means `Minit.*` calls compile to `Debug.Log(...)` in the editor and in non-WebGL build targets. Safe to call from any script regardless of build target.

---

## Validation (no automated test suite)

There is no automated C# test suite. Validation steps:

1. Open the project in Unity 6000.3.x.
2. Open the scene under `Assets/` and enter Play Mode — verify that `Minit.LoadingDone()` and `Minit.ReportResult(...)` log to the Console without errors.
3. In **Project Settings → Player → WebGL → Resolution and Presentation → WebGL Template**, select **Minit**. The identifier is `PROJECT:Minit`. Confirm no compile errors appear.
4. Build for WebGL (File → Build Settings → WebGL → Build). Inspect the emitted `index.html` — it should have no progress bar / loader / footer markup and should contain the correct `{{{ LOADER_FILENAME }}}` / `{{{ DATA_FILENAME }}}` / `{{{ FRAMEWORK_FILENAME }}}` macro substitutions.
5. Upload the resulting ZIP to the Minit creator console (dev environment).
6. Play the game inside the Minit feed and confirm: the game is hidden during boot (until `loadingDone` fires), then revealed; `reportResult` triggers the result screen with the correct score and flavor text.

The **"Build for Minit" editor menu** (one-click ZIP packaging) is planned in DROP-2022 — it will replace steps 4–5 once available.

When checking device behaviour, also verify portrait orientation and touch-only input on a real mobile device or iOS Simulator.

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
