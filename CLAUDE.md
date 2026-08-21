# CLAUDE.md — minit-unity

Unity project hosting the Minit Games Unity SDK UPM package (`games.minit.unity`, GitHub repo `Minit-Games/minit-unity`, private), letting Unity creators export an existing WebGL game to a Minit-compliant ZIP via a C# bridge to the host-injected `window.minit` runtime. Full maintainer reference — the `window.minit` contract, shared engine-facade pattern, distribution/release philosophy, and per-engine gotchas — lives in the consolidated SDK-maintenance doc: https://github.com/Minit-Games/minit-root/blob/develop/docs/sdk-maintenance.md

## Release process

Documented in the consolidated SDK-maintenance doc (the minit-unity bullet
under Distribution & release philosophy, and the per-SDK distribution table):
https://github.com/Minit-Games/minit-root/blob/develop/docs/sdk-maintenance.md
In short: version via `Packages/games.minit.unity/package.json`, promote
`develop` → `master` with a local fast-forward push (never a PR) — promotion
**is** the release (untagged as of this writing). The repo is private, so the
README's UPM Git URL install is org-members-only (go-public decision:
DROP-7520).
