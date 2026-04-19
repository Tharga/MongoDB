# Plan: Tharga.MongoDB.Mcp

## Steps

### Step 1: Create Tharga.MongoDB.Mcp project
- [x] Create `Tharga.MongoDB.Mcp` project with net8/9/10 targets
- [x] Reference `Tharga.Mcp` and `Tharga.MongoDB`
- [x] Add to solution
- [x] Build and verify

### Step 2: Implement resource provider
- [x] Create `MongoDbResourceProvider` implementing `IMcpResourceProvider`
- [x] Scope: `McpScope.System`
- [x] Resources: `mongodb://collections`, `mongodb://monitoring`, `mongodb://clients`
- [x] Each resource reads from `IDatabaseMonitor`
- [x] Build and verify

### Step 3: Implement tool provider
- [x] Create `MongoDbToolProvider` implementing `IMcpToolProvider`
- [x] Scope: `McpScope.System`
- [x] Tools: `mongodb.touch`, `mongodb.rebuild_index`
- [x] Build and verify

### Step 4: Add registration extension
- [x] `AddMongoDB()` extension on `IThargaMcpBuilder`
- [x] Registers resource and tool providers
- [x] Build and verify

### Step 5: Update CI/CD and tests
- [x] Add `Tharga.MongoDB.Mcp` to pack step in GitHub Actions workflow
- [x] Write tests (13 tests — resource list, resource read, tool list, tool execution)
- [x] Build and run tests (274 passed total)

### Step 6: README and commit
- [x] Update README with MCP section
- [x] Final build and test
- [ ] Commit all changes
