# Claude Code Configuration

This directory contains the Claude Code agent configuration for the KanbAI-Core project. It has been converted from the original Cursor IDE configuration.

## Directory Structure

```
.claude/
├── agents/           # Agent role definitions
├── rules/            # Global rules and standards
├── skills/           # Reusable skills and procedures
└── README.md         # This file
```

## Agents

Agents are specialized AI assistants with specific roles and responsibilities:

- **[product-manager](agents/product-manager.md)** - Analyzes business requirements and creates context handoff notes
- **[staff-engineer](agents/staff-engineer.md)** - Designs technical specifications from business context
- **[developer](agents/developer.md)** - Implements code from technical specifications
- **[tester-qa](agents/tester-qa.md)** - Reviews implementations and writes automated tests

### Using Agents

Agents work in a structured workflow:

1. **Product Manager** reads GitHub issues → creates `docs/handoffs/issue_{N}_context.md`
2. **Staff Engineer** reads context note → creates `docs/handoffs/issue_{N}_tech_spec.md`
3. **Developer** reads tech spec → implements code → updates tech spec with "Development Status"
4. **QA Tester** reads tech spec + implementation → writes tests → updates with "QA Status"

Each agent uses the Agent tool with `subagent_type: "Explore"` to delegate codebase exploration and keep context focused.

## Rules

Rules are mandatory standards that apply globally or to specific file types:

- **[code-standards](rules/code-standards.md)** - .NET coding standards and best practices
- **[security-safety](rules/security-safety.md)** - Security protocols and secret management
- **[integration-testing](rules/integration-testing.md)** - Integration test patterns for WebApplicationFactory
- **[testing-observability](rules/testing-observability.md)** - Testing standards and structured logging

### Applying Rules

Rules are automatically applied based on context:
- Code standards apply when writing/modifying `.cs` files
- Security rules apply to all code modifications
- Testing rules apply when writing test files
- Integration testing rules apply specifically to `WebApplicationFactory<Program>` tests

## Skills

Skills are reusable procedures for specific tasks:

- **[ef-core-migration](skills/ef-core-migration.md)** - EF Core migration generation and validation
- **[pre-implementation-checklist](skills/pre-implementation-checklist.md)** - Pre-coding verification checklist
- **[acceptance-criteria-quality](skills/acceptance-criteria-quality.md)** - Guidelines for writing quality acceptance criteria

### Using Skills

Skills are invoked when specific conditions are met:
- **ef-core-migration**: When generating or validating EF Core migrations
- **pre-implementation-checklist**: Before implementing any tech spec
- **acceptance-criteria-quality**: When writing or reviewing acceptance criteria

## Key Differences from Cursor Configuration

### Agent Invocation
- **Cursor**: Used `@agent_name` syntax and sub-agent dispatching
- **Claude Code**: Use the `Agent` tool with appropriate prompts, or invoke specialized agent definitions directly

### Exploration
- **Cursor**: Explicit `explore` and `shell` sub-agents
- **Claude Code**: Use `Agent` tool with `subagent_type: "Explore"` or direct tool usage (Bash, Grep, Glob)

### Context Management
- **Cursor**: Heavy emphasis on sub-agent dispatching to preserve main context
- **Claude Code**: Similar approach with Agent tool + explicit parallelization guidance

## Workflow Example

```bash
# 1. Product Manager analyzes issue #42
# Creates: docs/handoffs/issue_42_context.md

# 2. Staff Engineer designs solution
# Creates: docs/handoffs/issue_42_tech_spec.md

# 3. Developer implements
# Modifies: implementation files
# Updates: docs/handoffs/issue_42_tech_spec.md (Development Status section)

# 4. QA Tester validates
# Creates: test files
# Updates: docs/handoffs/issue_42_tech_spec.md (QA Status section)
```

## Best Practices

1. **Read First**: Always read the relevant agent definition before acting in that role
2. **Follow the Chain**: Respect the PM → Engineer → Developer → QA workflow
3. **Use Exploration**: Delegate codebase exploration to keep main context clean
4. **Check Prerequisites**: Run pre-implementation checklist before coding
5. **Validate**: Run post-implementation validation gates before handoff
6. **Document**: Update handoff notes at each stage

## Migration Notes

This configuration was converted from `.github/.cursor/rules/` to work with Claude Code. The core concepts remain the same:
- Role-based agent architecture
- Structured handoff documentation
- Context-aware rules
- Reusable skills

The main adaptation is in how agents are invoked and how sub-tasks are delegated to work within Claude Code's tool ecosystem.
