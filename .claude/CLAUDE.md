## Session Continuity

### Starting a session
1. Run `git status` to check for uncommitted changes
   - If uncommitted changes exist, alert me immediately and stop
   - Do not proceed until I have confirmed how to handle them (commit, stash, or discard)
2. Check if `.claude/plan.md` exists.
   If it does, read it and summarize what has been done and what the next step is.
   If it does not exist, ask me how I would like to proceed.   
3. Check if `.claude/feature.md` exists and read the current feature scope.

### During a session
After completing each step in the plan:
- Mark it as `[x]` done in `.claude/plan.md`
- Add a brief note about what was done and any important decisions made
- Mark the next step as `[~]` in progress

### Ending a session
- Update `.claude/plan.md` with the current status of all steps
- Add a "Last session" note summarizing what was completed and what comes next
- Note any README.md changes that will be needed when the feature is complete

## Testing Rules
- Run relevant tests after completing each step before marking it done
- If tests fail, fix the issue before moving on — do not proceed with a failing test
- Run the full test suite before any git commit
- If no tests exist for the code being changed, write them first before implementing

## Coding Guidelines
- Write tests for every new function

## Workflow Rules
- Before making changes, explain what you plan to do
- After completing a task, summarize what was changed
- If unsure about something, ask before proceeding

## Git Rules
- Never push to remote without explicit approval from me
- Never force push under any circumstances
- Create branch `feature/<feature-name>` at the start of each feature
- Commit at logical milestones (e.g. a component is complete and tested)
- Never commit failing tests
- Use conventional commits: `feat:`, `fix:`, `test:`, `docs:`
- Never merge to main — leave that for me to review and merge

## Feature Workflow

### Starting a feature
When told to start a new feature:
1. Ask for the feature name and goal if not provided
2. Create a new branch: `git checkout -b feature/<feature-name>`
3. Create `.claude/feature.md` with goal, scope, acceptance criteria, and done condition
4. Create or update `.claude/plan.md` with the steps to implement the feature
5. Confirm the plan before starting any code changes

### Finishing a feature
A feature is done when:
- All acceptance criteria in `.claude/feature.md` are met
- All tests pass
- README.md has been updated to reflect the new feature
- `.claude/feature.md` is archived to `.claude/features-done/<feature-name>.md`
- A final commit is made with message: `feat: <feature-name> complete`