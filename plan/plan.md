# Plan: docs-ci-integration

## Steps

- [x] 1. Add `pages: write` and `id-token: write` to the workflow-level `permissions` in `.github/workflows/build.yml`
- [x] 2. Append `docs` job to `build.yml` — `needs: release`, master-push guard, `dotnet restore` → `docfx docs/docfx.json` → `upload-pages-artifact@v3` (path `docs/_site`)
- [x] 3. Append `docs-deploy` job to `build.yml` — `needs: docs`, master-push guard, `concurrency: { group: pages, cancel-in-progress: false }`, `environment: github-pages`, `deploy-pages@v4`
- [x] 4. Delete `.github/workflows/docs.yml`
- [x] 5. Validate `build.yml` YAML — confirmed valid; job graph build/security → release → docs → docs-deploy
- [x] 6. Update `Requests.md` — R1 (CI docs) Done; R2 (PackageIconUrl) Done + URL verified 200; R3 (subdomain) noted DNS-pending; AzDo-disable already noted in CI/CD section
- [~] 7. Commit code + plan at milestone; push feature branch for review
- [ ] 8. Close-out: archive `plan/feature.md` to Plan directory `done/docs-ci-integration.md`, `git rm -r plan`, final `ci:` commit, open PR

## Last session

2026-06-10 — Implemented the CI consolidation. Discovered R2 (icon URL) was already coded (commit `f953de4`, verified 200/image/png) and R3's CNAME already present, so the only code change was R1: added `pages`/`id-token` permissions and appended `docs` (needs: release) + `docs-deploy` (needs: docs) jobs to `build.yml`, deleted `docs.yml`. YAML validated, job graph confirmed. Requests.md statuses updated. Next: commit + push for review; close-out (archive plan, remove `plan/`, PR) once you confirm.
