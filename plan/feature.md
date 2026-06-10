# Feature: docs-ci-integration

## Goal

Close the three pending cross-project requests for Tharga.MongoDB as one feature, mirroring the Tharga.Test CI setup (single `build.yml`, no separate `docs.yml`):

- **R1** — Fold docs build/deploy into `build.yml` (`docs` + `docs-deploy` jobs gated on `needs: release`); delete `docs.yml`. (`## CI: Publish docs after release` in Requests.md)
- **R2** — Finalize `<PackageIconUrl>` → `https://thargelion.net/assets/component-mongodb.png` and publish via release. (`## Move PackageIconUrl to thargelion.net/assets`)
- **R3** — Move docs site to the `mongodb.tharga.net` subdomain. (`## Documentation sites under tharga.net`)

## Current state (discovered before starting)

- **R2 is already coded** — all 5 published csprojs carry the new icon URL (commit `f953de4`); URL verified `200 OK / image/png`. Needs only a release to publish + Requests.md status flip.
- **R3 CNAME already present** — `docs/CNAME` = `mongodb.tharga.net`. Only DNS remains (external/meta-repo, manual).
- **R1 is the real code change** — `build.yml` lacks `pages`/`id-token` permissions and the docs jobs; standalone `docs.yml` still exists.

Reference: `c:/dev/tharga/Toolkit/Test/.github/workflows/build.yml` (docs jobs lines 281–326, permissions 16–17).

## Scope

In scope:
- Add `pages: write` + `id-token: write` to `build.yml` permissions.
- Append `docs` job (`needs: release`) and `docs-deploy` job (`needs: docs`) to `build.yml`.
- Delete `.github/workflows/docs.yml`.
- Update `Requests.md`: R1 → Done; R2 → Done (verified + released); R3 → note remaining DNS; note AzDo-pipeline disable.

Out of scope:
- NuGet dependency refresh (decided: keep release CI-only).
- DNS configuration for `mongodb.tharga.net` (external/manual).
- Disabling the old Azure DevOps pipeline (manual UI step).
- DocFX theme/logo changes (separate planned feature `docs-site-theme`).

## Acceptance criteria

- `build.yml` has a linear graph: build/security → release → docs → docs-deploy; docs deploy only after a successful release (master push only).
- `docs.yml` is deleted.
- `build.yml` permissions include `pages: write` and `id-token: write`.
- `docfx docs/docfx.json` is the docs build command; artifact path `docs/_site`.
- Requests.md statuses updated (R1 Done, R2 Done, R3 DNS-pending note).
- Build/CI valid (YAML well-formed; mirrors the verified Tharga.Test structure).

## Done condition

PR open from `feature/docs-ci-integration` → `master` with the CI consolidation, after the close-out commit removes `plan/`. The 2.10.15 release the merge produces publishes the new icon metadata and deploys docs via the folded pipeline.

## Behavior change to note

The folded `docs` job drops `docs.yml`'s `workflow_dispatch` and path-filter triggers. Docs now rebuild on every master-push release. Matches the Tharga.Test reference pattern.
