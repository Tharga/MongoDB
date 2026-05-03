# Plan: Tharga.Mcp follow-up — UseThargaMcp + verify dispatcher cleanup

## Steps

### Step 1: Verify dispatcher state
- [x] Read `Tharga.MongoDB.Mcp/MongoDbToolProvider.cs` end-to-end — confirm only `IMcpToolProvider` is implemented; no manual dispatch / pre-0.1.1 wiring
- [x] Read `Tharga.MongoDB.Mcp/MongoDbResourceProvider.cs` end-to-end — same check for `IMcpResourceProvider`
- [x] Confirm `ThargaMcpBuilderExtensions.AddMongoDB()` registers via plain `AddResourceProvider<T>` / `AddToolProvider<T>` (no `McpProviderDispatcher` references)
- [x] If anything stale surfaces, list the cleanup before doing it — nothing stale found

### Step 2: README update
- [x] Replace `app.MapMcp();` with `app.UseThargaMcp();` in [README.md:688](../README.md#L688)
- [x] Grep the rest of the repo for any remaining `MapMcp` references — only plan files (this branch's own metadata) referenced it; no production/sample/test code

### Step 3: Build + test
- [x] Build solution on net8 / net9 / net10 — green, 15 warnings (under 50 budget), 0 errors
- [x] Run full test suite — 294 passed, 8 skipped, 0 failed on net10
- [~] Smoke-test the sample (`Tharga.TemplateBlazor.Web`) — skipped: README/plan-only change, sample already used `UseThargaMcp` pre-branch and the build covers compile correctness

### Step 4: Update `$DOC_ROOT/Tharga/Requests.md`
- [x] Mark the **line 44** follow-up `[x]` (Tharga.Mcp ≥ 0.1.1) with date + note that we're already on 0.1.3
- [x] Mark the **line 47** follow-up `[x]` (`MapMcp` → `UseThargaMcp`) with date + note that README is updated on this branch

### Step 5: Migrate stale local plan archives to external Plan directory
- [x] Copy `.claude/features-done/{28,32,mongodb-mcp}.md` → `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/done/`
- [x] Copy `.claude/features-planned/{27,29,30,31,33,README}.md` → `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/planned/`
- [x] Remove local `.claude/features-done/` and `.claude/features-planned/` directories

### Step 6: Milestone commit (implementation + cleanup)
- [ ] Commit current state: README, plan/*, deletion of `.claude/features-{done,planned}/`

### Step 7: Closure (per shared-instructions § "Closing a feature")
- [ ] Archive `plan/feature.md` → `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/done/mcp-followup.md`
- [ ] Delete the `plan/` directory
- [ ] Final commit: `feat: mcp-followup complete`

### Step 8: Push + PR
- [ ] **User pushes** `feature/mcp-followup` to origin
- [ ] Claude opens PR to `Tharga/master`
- [ ] After merge — delete feature branch

## Last session
Steps 1–5 complete. Branch is `feature/mcp-followup`, parented on master (`6665a51`). Net repo changes: README:688 (`MapMcp` → `UseThargaMcp`), updated `plan/feature.md` and `plan/plan.md`, plus removal of the stale local `.claude/features-{done,planned}/` directories (their contents were migrated to `$DOC_ROOT/Tharga/plans/Toolkit/MongoDB/{done,planned}/`). Build green on all three TFMs, 294/294 tests pass. Both `Requests.md` follow-ups marked done. Next: milestone commit, then closure (archive feature.md externally + delete plan/), then user pushes, then Claude opens PR.
