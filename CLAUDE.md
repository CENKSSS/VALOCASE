# ValoCase Claude Rules

ValoCase is a Unity mobile case simulator project.

Primary objective:
Build a premium, polished, mobile-first experience that feels comparable to modern case opening and inventory games.

# Development Philosophy

* Prefer practical solutions over theoretical perfection.
* Prefer shipping working improvements over endless planning.
* Be decisive when enough information exists.
* Avoid unnecessary complexity.
* Preserve project momentum.
* Improve existing systems before creating new ones.

# Scope Discipline

* Implement only the requested task.
* Do not redesign unrelated screens.
* Do not modify unrelated systems.
* Do not create new architectures unless clearly needed.
* Keep file scope as small as possible.
* Respect existing project structure.

# Agent Workflow

Use agents when appropriate:

planner
→ identifies files, risks, and implementation path

ui-designer
→ creates builder-ready UI specifications

unity-builder
→ implements approved changes

reviewer
→ validates scope, quality, and regressions

Do not overlap responsibilities.

# UI Direction

ValoCase should feel:

* Premium
* Competitive
* Modern
* Mobile-first
* Valorant-inspired

Visual characteristics:

* Dark backgrounds
* Red and black primary palette
* Sharp geometry
* Strong rarity visibility
* Strong reward visibility
* Clear progression visibility
* High perceived value
* Clean visual hierarchy

Avoid:

* Generic business-app UI
* Cartoon-style layouts
* Excessive clutter
* Tiny unreadable elements
* Weak visual emphasis

# Implementation Philosophy

When redesign is requested:

* Prefer meaningful improvements over cosmetic tweaks.
* Improve hierarchy before adding decoration.
* Improve layout before adding effects.
* Improve usability before adding visuals.

Builder should not be afraid to make significant UI improvements when the request clearly calls for redesign.

# Performance Philosophy

Prioritize:

1. Stable gameplay
2. Mobile performance
3. UI responsiveness
4. Visual polish

Avoid unnecessary allocations, expensive Update loops, and over-engineered systems.

# Protected Systems

Do not modify unless explicitly requested:

* Case reward logic
* Drop probabilities
* Economy balancing
* Save/load systems
* Inventory persistence
* Core GameContext architecture

# Response Style

* Be concise.
* Be practical.
* Be implementation-focused.
* Avoid generic advice.
* Prefer concrete recommendations.
* Prefer exact file paths when possible.
* Prefer builder-ready specifications.

# Success Criteria

A successful change:

* Solves the requested problem.
* Preserves existing functionality.
* Improves player experience.
* Maintains mobile usability.
* Fits the ValoCase visual identity.
* Avoids unnecessary complexity.


## Agentic QE v3

This project uses **Agentic QE v3** - a Domain-Driven Quality Engineering platform with 13 bounded contexts, ReasoningBank learning, HNSW vector search, and Agent Teams coordination (ADR-064).

---

### CRITICAL POLICIES

#### Integrity Rule (ABSOLUTE)
- NO shortcuts, fake data, or false claims
- ALWAYS implement properly, verify before claiming success
- ALWAYS use real database queries for integration tests
- ALWAYS run actual tests, not assume they pass

**We value the quality we deliver to our users.**

#### Test Execution
- NEVER run `npm test` without `--run` flag (watch mode risk)
- Use: `npm test -- --run`, `npm run test:unit`, `npm run test:integration` when available

#### Data Protection
- NEVER run `rm -f` on `.agentic-qe/` or `*.db` files without confirmation
- ALWAYS backup before database operations

#### Git Operations
- NEVER auto-commit/push without explicit user request
- ALWAYS wait for user confirmation before git operations

---

### Quick Reference

```bash
# Run tests
npm test -- --run

# Check quality
aqe quality assess

# Generate tests
aqe test generate <file>

# Coverage analysis
aqe coverage <path>
```

### Using AQE MCP Tools

AQE exposes tools via MCP with the `mcp__agentic-qe__` prefix. You MUST call `fleet_init` before any other tool.

#### 1. Initialize the Fleet (required first step)

```typescript
mcp__agentic-qe__fleet_init({
  topology: "hierarchical",
  maxAgents: 15,
  memoryBackend: "hybrid"
})
```

#### 2. Generate Tests

```typescript
mcp__agentic-qe__test_generate_enhanced({
  targetPath: "src/services/auth.ts",
  framework: "vitest",
  strategy: "boundary-value"
})
```

#### 3. Analyze Coverage

```typescript
mcp__agentic-qe__coverage_analyze_sublinear({
  paths: ["src/"],
  threshold: 80
})
```

#### 4. Assess Quality

```typescript
mcp__agentic-qe__quality_assess({
  scope: "full",
  includeMetrics: true
})
```

#### 5. Store and Query Patterns (with learning persistence)

```typescript
// Store a learned pattern
mcp__agentic-qe__memory_store({
  key: "patterns/coverage-gap/{timestamp}",
  namespace: "learning",
  value: {
    pattern: "...",
    confidence: 0.95,
    type: "coverage-gap",
    metadata: { /* domain-specific */ }
  },
  persist: true
})

// Query stored patterns
mcp__agentic-qe__memory_query({
  pattern: "patterns/*",
  namespace: "learning",
  limit: 10
})
```

#### 6. Orchestrate Multi-Agent Tasks

```typescript
mcp__agentic-qe__task_orchestrate({
  task: "Full quality assessment of auth module",
  domains: ["test-generation", "coverage-analysis", "security-compliance"],
  parallel: true
})
```

### MCP Tool Reference

| Tool | Description |
|------|-------------|
| `fleet_init` | Initialize QE fleet (MUST call first) |
| `fleet_status` | Get fleet health and agent status |
| `agent_spawn` | Spawn specialized QE agent |
| `test_generate_enhanced` | AI-powered test generation |
| `test_execute_parallel` | Parallel test execution with retry |
| `task_orchestrate` | Orchestrate multi-agent QE tasks |
| `coverage_analyze_sublinear` | O(log n) coverage analysis |
| `quality_assess` | Quality gate evaluation |
| `memory_store` | Store patterns with namespace + persist |
| `memory_query` | Query patterns by namespace/pattern |
| `security_scan_comprehensive` | SAST/DAST scanning |

### Configuration

- **Enabled Domains**: test-generation, test-execution, coverage-analysis, quality-assessment, defect-intelligence, requirements-validation (+7 more)
- **Learning**: Enabled (auto embeddings)
- **Max Concurrent Agents**: 15
- **Background Workers**: pattern-consolidator

### V3 QE Agents

QE agents are in `.claude/agents/v3/`. Use with Task tool:

```javascript
Task({ prompt: "Generate tests", subagent_type: "qe-test-architect", run_in_background: true })
Task({ prompt: "Find coverage gaps", subagent_type: "qe-coverage-specialist", run_in_background: true })
Task({ prompt: "Security audit", subagent_type: "qe-security-scanner", run_in_background: true })
```

### Data Storage

- **Memory Backend**: `.agentic-qe/memory.db` (SQLite)
- **Configuration**: `.agentic-qe/config.yaml`

---
*Generated by AQE v3 init - 2026-06-22T22:43:08.039Z*
