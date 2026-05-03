# Feature: Tharga.Mcp follow-up — UseThargaMcp + verify dispatcher cleanup

## Originating Branch
`feature/mcp-followup` (off develop)

## Source
Two follow-up items in `$DOC_ROOT/Tharga/Requests.md`:
- **Line 44**: *"Tharga.MongoDB.Mcp should upgrade Tharga.Mcp to >= 0.1.1 — `IMcpToolProvider`/`IMcpResourceProvider` are now dispatched by the SDK via the built-in `McpProviderDispatcher`; no per-provider SDK wiring needed"*
- **Line 47**: *"Tharga.MongoDB.Mcp should switch `app.MapMcp()` → `app.UseThargaMcp()` at next Tharga.Mcp upgrade — MapMcp is now `[Obsolete]` and will be removed in a future version"*

## Current state (pre-branch audit)
- `Tharga.MongoDB.Mcp.csproj` already references **`Tharga.Mcp` 0.1.3** (≥ 0.1.1). No version bump needed.
- `Sample/Tharga.TemplateBlazor.Web/Program.cs:84` already calls `app.UseThargaMcp()`.
- `README.md:688` still shows `app.MapMcp();` in the MCP registration example — **the only code/doc change left**.
- Provider classes implement `IMcpToolProvider` / `IMcpResourceProvider` directly. `ThargaMcpBuilderExtensions.AddMongoDB()` registers them via standard `AddResourceProvider<T>` / `AddToolProvider<T>`. No legacy per-provider dispatcher wiring detected — verify-only.

## Goal
Bring docs in line with the new Tharga.Mcp surface, confirm no leftover dispatcher wiring, and strike the two follow-up entries from `Requests.md`.

## Scope
- README MCP section: `app.MapMcp()` → `app.UseThargaMcp()`.
- Audit the `Tharga.MongoDB.Mcp` source for any leftover code referencing pre-0.1.1 Tharga.Mcp APIs (none expected — verify only).
- Build + test on net8/9/10 to confirm no regression.
- Strike both follow-up entries from `$DOC_ROOT/Tharga/Requests.md` (mark `[x]` with date).

## Out of scope
- Bumping Tharga.Mcp beyond 0.1.3.
- New MCP tools/resources (covered by planned feature #27 and the PlutusWave document-inspection request).
- Marking the Lockable Lock-index Eplicta request Done — that is paired with a Tharga.MongoDB release, not this branch.

## Acceptance criteria
- [ ] README MCP example uses `app.UseThargaMcp()`.
- [ ] No remaining `MapMcp` references in this repo (sample + README + tests).
- [ ] No legacy dispatcher wiring in `Tharga.MongoDB.Mcp/`.
- [ ] Build + tests green on net8/9/10 with the existing 50-warning budget.
- [ ] Both `Requests.md` follow-up entries struck.

## Done condition
PR merged into develop. The two `Requests.md` follow-up bullets are gone (or `[x]` with date), the README sample mirrors the sample project, and no consumer is left guessing whether `MapMcp` or `UseThargaMcp` is the supported entry point.
