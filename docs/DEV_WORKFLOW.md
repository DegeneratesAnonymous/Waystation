# Development Workflow

This document describes the end-to-end workflow for AI-agent-driven development in this repository.
All roles — Research, Tech Lead, Backend, Frontend, DevOps, Security, QA, and Documentation — are
executed in a single Orchestrator invocation.

---

## Option A — Orchestrator (recommended)

Invoke the **Orchestrator Agent** with the full work order context. The Orchestrator executes all
eight specialist roles in one structured response:

```
Research → Tech Lead → Backend → Frontend → DevOps → Security → QA → Documentation
```

### Quick Start

1. Open [`docs/dev-agents/ORCHESTRATOR.md`](./dev-agents/ORCHESTRATOR.md) and copy the full prompt.
2. Paste it into your AI assistant as the system prompt or first message.
3. Fill in the **Required Inputs** table (§ 1–6) from the work order / issue body.
4. Submit — the Orchestrator produces all eight sections in a single response.

### Required Inputs

| # | Field | Source |
|---|---|---|
| 1 | Goal | Work order — Goal section |
| 2 | Scope | Work order — Scope section |
| 3 | Acceptance Criteria | Work order — Acceptance Criteria section |
| 4 | Constraints | Work order — Project Context section |
| 5 | Links / References | Issue URL, linked PRs, external docs |
| 6 | QA Findings (restart only) | QA Agent defect report from previous run |

### QA Failure Restart

If the QA section of the Orchestrator response reports blocking defects:

1. Document the defects in a comment on the issue.
2. Apply the **`qa-failed`** label to the issue.
3. The **Agent Workflow** (`.github/workflows/agent-workflow.yml`) fires automatically and updates
   the checklist comment with a reminder to paste QA findings into Required Input §6.
4. Re-invoke the Orchestrator with the QA defect report in Required Input §6.
5. Once QA passes, remove the `qa-failed` label and proceed to merge.

---

## Agent Roles

| Agent | Responsibility |
|---|---|
| **Research** | Validates the request, investigates best practices, prior art, and external standards; recommends an approach |
| **Tech Lead** | Structures an actionable plan, defines interface contracts, divides work by role |
| **Backend** | Implements server-side tasks, API contracts, and data layer changes |
| **Frontend** | Implements UI/UX, API consumption, and client-side logic |
| **Security** | Reviews auth/RBAC, secrets handling, input validation, and data-protection requirements |
| **DevOps** | Maintains CI/CD, reproducible environments, dependency management, and infra tooling |
| **QA** | Defines and executes test plans; validates acceptance criteria and coverage targets |
| **Documentation** | Keeps README, planning docs, API references, and inline comments current |

Individual role prompts are also available in [`.github/agents/`](../.github/agents/) for cases
where you want to invoke a single specialist role in isolation.

---

## Automated Workflow

When any issue or pull request is opened in this repository, the **Agent Workflow Orchestration**
GitHub Actions workflow (`.github/workflows/agent-workflow.yml`) fires automatically and posts an
Orchestrator checklist comment with a direct link to the prompt and quick-start instructions.

---

## Creating a Work Order

Open a GitHub issue using the appropriate template:

- **Work Order** (`.github/ISSUE_TEMPLATE/work-order.yml`) — for new features, tasks, or planned changes.
- **Bug Report** (`.github/ISSUE_TEMPLATE/bug-report.yml`) — for defects and unexpected behaviour.

Both templates share a common structure that maps directly to the Orchestrator's Required Inputs:

| Template section | Required Input |
|---|---|
| Goal | §1 Goal |
| Project Context | §4 Constraints |
| Scope | §2 Scope |
| Acceptance Criteria | §3 Acceptance Criteria |
| (Issue URL) | §5 Links / References |

---

## Pull Request

Open a PR using the pull request template (`.github/pull_request_template.md`):

- Link the issue
- Restate the **Goal** and **Summary of Changes**
- Confirm **Scope** (in/out)
- Mark off each **Acceptance Criterion**
- Record **Test Plan** results
- Confirm the **Rollback Plan** is still valid
- Complete the Verification Checklist
- Collect **Agent Sign-offs** (matching the Agents Required from the issue)

---

## File Reference

| File | Purpose |
|---|---|
| `docs/dev-agents/ORCHESTRATOR.md` | Single all-in-one agent prompt (Research → Docs in one pass) |
| `.github/workflows/agent-workflow.yml` | Auto-posts Orchestrator checklist on every new issue or PR |
| `.github/agents/<ROLE>.md` | Individual role prompts for single-specialist invocation |
| `.github/ISSUE_TEMPLATE/config.yml` | Issue template chooser configuration |
| `.github/ISSUE_TEMPLATE/work-order.yml` | Structured work order GitHub issue template |
| `.github/ISSUE_TEMPLATE/bug-report.yml` | Structured bug report GitHub issue template |
| `.github/pull_request_template.md` | PR template mirroring issue section structure |
| `docs/DEV_WORKFLOW.md` | This document — overall workflow guide |
| `docs/WORK_ORDER_TEMPLATE.md` | Manual / offline work order template |

