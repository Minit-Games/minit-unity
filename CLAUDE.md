# CLAUDE.md — minit-unity

Unity project hosting the Minit Games Unity SDK UPM package (`games.minit.unity`, GitHub repo `Minit-Games/minit-unity`, private), letting Unity creators export an existing WebGL game to a Minit-compliant ZIP via a C# bridge to the host-injected `window.minit` runtime. Full maintainer reference — the `window.minit` contract, shared engine-facade pattern, distribution/release philosophy, and per-engine gotchas — lives in the consolidated SDK-maintenance doc: https://github.com/Minit-Games/minit-root/blob/develop/docs/sdk-maintenance.md

## Release process

Like Defold/Godot, the UPM package is consumed as **source** — no
build/publish pipeline, unlike `minit-sdk`'s npm publish.

- **Versioning.** `Packages/games.minit.unity/package.json` `version` field
  (semver; currently `0.1.0`). Bump it on `develop` as part of the change
  that warrants a release, then promote.
- **Promotion IS the release.** The README's UPM Git URL has no `#branch`
  fragment, so Unity resolves it against the repo's **default branch** — it
  must be `master`, and `master` must contain `Packages/games.minit.unity/`
  (this ticket, DROP-7084, fixed exactly this: `master` was a bare Unity
  template with no package, so the documented install URL 404'd).
- **`develop` → `master` is a local fast-forward push, never a PR** — same
  convention as every other Minit repo:
  ```bash
  git checkout master
  git pull
  git merge --ff-only develop
  git push
  ```
  `.claude/rules/git-workflow.md` in the monorepo doesn't yet name
  `minit-unity` in its `Applies to:` header (DROP-7082 tracks recording
  that) — it applies here by convention in the meantime.
- **Tagging: none yet.** `git tag -l` is currently empty in this repo, so as
  of this writing a `master` promotion **is** the whole release — there's no
  tag or GitHub Release step. Sibling engine SDKs (`minit-defold`,
  `minit-godot`) do tag each release `v<x.y.z>` on `master`; adopt the same
  convention here once a promotion ships alongside a deliberate version bump.
- **Repo stays private** (`Minit-Games/minit-unity`) — the UPM Git URL only
  resolves for accounts with read access, so installs currently work for
  Minit-Games org members only. A future go-public decision is tracked
  separately as DROP-7520.
