# Migration from Cursor to Claude Code

This document explains how the Cursor IDE configuration has been adapted for Claude Code.

## Overview

The original `.github/.cursor/rules/` configuration has been converted to `.claude/` with adaptations for Claude Code's tooling and agent system.

## Directory Mapping

| Cursor | Claude Code | Notes |
|--------|-------------|-------|
| `.github/.cursor/rules/agent/` | `.claude/agents/` | Agent role definitions |
| `.github/.cursor/rules/rule/` | `.claude/rules/` | Global standards and rules |
| `.github/.cursor/rules/skill/` | `.claude/skills/` | Reusable procedures |
| `.github/.cursor/rules/sub-agent/` | *Integrated* | Sub-agent patterns integrated into agent workflows |

## Files Converted

### Agents (4)
- ✅ `developer.md` - Senior .NET Developer
- ✅ `staff-engineer.md` - Staff Engineer / Architect  
- ✅ `product-manager.md` - Product Manager
- ✅ `tester-qa.md` - QA Tester

### Rules (4)
- ✅ `code-standards.md` - .NET coding standards
- ✅ `security-safety.md` - Security protocols
- ✅ `integration-testing.md` - Integration test patterns
- ✅ `testing-observability.md` - Testing and logging standards

### Skills (3)
- ✅ `ef-core-migration.md` - EF Core migration procedures
- ✅ `pre-implementation-checklist.md` - Pre-coding verification
- ✅ `acceptance-criteria-quality.md` - AC writing guidelines

### Documentation (2)
- ✅ `README.md` - Overview and usage guide
- ✅ `MIGRATION.md` - This file

**Total: 13 files created**

## Key Adaptations

### 1. Agent Invocation

**Cursor:**
```
@agent_developer implement the feature
```

**Claude Code:**
```
Use the developer agent to implement the feature
```
Or invoke the Agent tool directly with appropriate context from the agent definition.

### 2. Sub-Agent Dispatching

**Cursor:**
```
Dispatch an `explore` sub-agent to map the codebase...
```

**Claude Code:**
```
Use Agent tool with subagent_type: "Explore" to map the codebase...
```

Example:
```
Agent({
  description: "Codebase architecture scan",
  subagent_type: "Explore",
  prompt: "Explore the KanbAI-Core codebase and return a structured summary..."
})
```

### 3. Build Verification

**Cursor:**
```
Dispatch a `shell` sub-agent with `@build_verifier` instructions...
```

**Claude Code:**
```
Use Bash tool to run build and test commands:
dotnet build
dotnet test --verbosity quiet
```

The agent interprets results directly rather than dispatching to a specialized sub-agent.

### 4. Context Efficiency

Both systems emphasize context management, but Claude Code uses:
- **Agent tool** for complex explorations
- **Direct tools** (Grep, Glob, Read) for focused queries
- **Parallel tool calls** for independent operations

### 5. Logging

**Cursor:**
```
Append to cursor_prompts.md using StrReplace
```

**Claude Code:**
```
Standard git commit messages and handoff documentation
```

The mandatory `cursor_prompts.md` logging is less critical in Claude Code, which has built-in conversation history.

## Workflow Compatibility

The core workflow remains identical:

```
GitHub Issue
    ↓
Product Manager → issue_{N}_context.md
    ↓
Staff Engineer → issue_{N}_tech_spec.md
    ↓
Developer → implementation + tech_spec update
    ↓
QA Tester → tests + tech_spec update
```

## What Wasn't Migrated

### Sub-Agent Definitions
These were integrated into main agent workflows:
- `bridge_frontend` - Frontend contract checking (integrated into staff-engineer)
- `build_verifier` - Build/test execution (replaced with direct Bash tool usage)
- `codebase_scanner` - Architecture discovery (use Agent tool with Explore subagent)
- `issue_scout` - Issue fetching (use Bash + gh CLI directly)
- `context_pruner` - Not needed (Claude Code handles context differently)

### Global Logging Rule
The `global_logging` rule was specific to Cursor's workflow and isn't required in Claude Code, which has:
- Built-in conversation history
- Git commit messages for code changes
- Handoff documentation for project state

## Usage Recommendations

### For Developers

1. **Start with the agent definition**: Read the relevant agent file before starting work
2. **Follow the workflow**: Respect the PM → Engineer → Developer → QA sequence
3. **Use exploration wisely**: Delegate codebase exploration to keep context clean
4. **Check prerequisites**: Run pre-implementation checklist before coding

### For Customization

1. **Agent modifications**: Edit files in `.claude/agents/` to adjust agent behaviors
2. **Rule additions**: Add new `.md` files to `.claude/rules/` for project-specific standards
3. **Skill creation**: Create new skills in `.claude/skills/` for repeated procedures

### Tool Equivalents

| Cursor Concept | Claude Code Equivalent |
|----------------|----------------------|
| `@agent_name` | Read agent definition, follow its workflow |
| Sub-agent dispatch | Agent tool with appropriate subagent_type |
| `explore` sub-agent | Agent tool with `subagent_type: "Explore"` |
| `shell` sub-agent | Bash tool directly |
| `@rule_name` | Read rule definition, apply guidelines |
| `@skill_name` | Read skill definition, follow procedure |

## Compatibility Notes

### Strengths of Claude Code Approach
- ✅ More explicit tool usage (less abstraction)
- ✅ Built-in context management
- ✅ Direct integration with git/GitHub
- ✅ Native support for parallel operations

### Differences to Note
- ⚠️ Less rigid agent invocation (more flexible but requires discipline)
- ⚠️ No explicit sub-agent types (use general Agent tool)
- ⚠️ Different context window management strategies

## Testing the Migration

To verify the migration works:

1. **Try a complete workflow**:
   ```
   1. Pick a GitHub issue
   2. Invoke product-manager agent → creates context note
   3. Invoke staff-engineer agent → creates tech spec
   4. Invoke developer agent → implements feature
   5. Invoke tester-qa agent → writes tests
   ```

2. **Check handoff documents**:
   - `docs/handoffs/issue_{N}_context.md` exists
   - `docs/handoffs/issue_{N}_tech_spec.md` exists and is complete
   - Tech spec has "Development Status" section
   - Tech spec has "QA Status" section

3. **Verify standards are followed**:
   - Code follows `code-standards.md`
   - Security follows `security-safety.md`
   - Tests follow `testing-observability.md`

## Future Enhancements

Potential improvements to this configuration:

1. **Claude Code Skills**: Convert some agents to Claude Code native skills with the `/` command syntax
2. **MCP Integration**: Add Model Context Protocol servers for external integrations
3. **Custom Tools**: Create project-specific tools for repeated operations
4. **Workflow Automation**: Add hooks for automatic agent transitions

## Questions?

For questions about:
- **Original design**: Check `.github/.cursor/rules/` files
- **Claude Code adaptation**: Check this `.claude/` directory
- **Usage**: See `.claude/README.md`
- **Workflow**: See agent definition files in `.claude/agents/`
