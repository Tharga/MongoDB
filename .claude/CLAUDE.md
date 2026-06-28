## Session Continuity

### Starting a session
1. Run `git status` to check for uncommitted changes
   - If uncommitted changes exist, alert me immediately and stop
   - Do not proceed until I have confirmed how to handle them (commit, stash, or discard)
2. Check if `.claude/mission.md` exists and read it.
   - Follow **all** file references listed under "External References" — read each one (shared-instructions, backlog, incoming requests, etc.)
   - **Environment preflight — do this before reading any reference and before any work.** Several External References live under `$DOC_ROOT` (the SynologyDrive Notes folder), so the session cannot run without it. Resolve `$DOC_ROOT` from the environment variable defined in `~/.claude/settings.json`.
     - If `$DOC_ROOT` is **unset/empty**, does not point to an existing directory, or **any** `$DOC_ROOT/...` reference cannot be read: **STOP — do not continue the session.** Ask me to prepare the environment by adding the following to `~/.claude/settings.json` and then **restarting the session** (the env var is only loaded at startup):
       ```json
       { "env": { "DOC_ROOT": "<absolute path to the SynologyDrive Notes folder on this machine>" } }
       ```
     - Do not guess paths, skip a reference, or proceed on partial information — every External Reference must be readable before work begins.
3. Check if `plan/` exists in the project root.
   - If `plan/plan.md` exists, summarize what has been done and what the next step is.
   - If `plan/feature.md` exists, read the current feature scope.
   - If neither exists, ask me how I would like to proceed.

### During a session
After completing each step in the plan:
- Mark it as `[x]` done in `plan/plan.md`
- Add a brief note about what was done and any important decisions made
- Mark the next step as `[~]` in progress

### Ending a session
- Update `plan/plan.md` with the current status of all steps
- Add a "Last session" note summarizing what was completed and what comes next
- Note any README.md changes that will be needed when the feature is complete

