<!--
Sync Impact Report
Version change: 1.1.0 -> 1.2.0
Modified principles:
- None
Added principles:
- IX. Serilog-Centric Observability and Layer Coverage
Added sections:
- Logging Baseline
Removed sections:
- None
Templates requiring updates:
- ✅ updated: .specify/templates/plan-template.md
- ✅ updated: .specify/templates/spec-template.md
- ✅ updated: .specify/templates/tasks-template.md
- ⚠ pending: .specify/templates/commands/*.md (directory not present in repository)
Runtime guidance requiring updates:
- ✅ no updates required: README.md (file not present)
Deferred TODOs:
- TODO(RATIFICATION_DATE): Original ratification date is not recorded in repository history.
-->

# AdventureEngine Constitution

## Purpose

AdventureEngine is a local, private, modular interactive fiction engine that generates
branching narrative scenes using a local LLM. The system MUST maintain story state,
present choices, and evolve narrative state based on user decisions without requiring
cloud inference for core story generation.

## Core Principles

### I. Local-First, Private Runtime

All runtime story generation MUST execute on the user's local Windows machine in
Phase 1. Core narrative generation MUST NOT depend on cloud services. Story data,
state, and logs MUST remain local by default. Any future cloud capability MUST be
explicitly opt-in and isolated behind infrastructure adapters.

Rationale: Local execution protects privacy, reduces external dependency risk, and
keeps core functionality available offline.

### II. Modular Boundaries and Replaceable Components

The system MUST separate domain/state logic, orchestration engine, prompt assembly,
and model client into independent modules with explicit interfaces. Implementations
MUST be swappable without changing domain rules, including model provider adapters.

Rationale: Explicit boundaries improve maintainability and enable controlled evolution.

### III. Deterministic State Evolution

State transitions MUST be explicit, validated, and logged. Given identical starting
state and identical inputs, transition results MUST be identical. Choice processing,
state reducers, and history logging MUST be deterministic at the engine boundary.

Rationale: Determinism enables reliable testing, debugging, and reproducibility.

### IV. LLM-Agnostic Model Boundary

Model access MUST occur through an OpenAI-compatible abstraction that supports local
providers (for example Ollama or LM Studio) without coupling core logic to a single
vendor. Provider-specific concerns MUST remain in adapter modules.

Rationale: LLM-agnostic boundaries prevent lock-in and support model interchangeability.

### V. JSON-In / JSON-Out Contract Enforcement

Model requests and responses MUST use strict JSON contracts. Prompt instructions and
model adapters MUST enforce schema validation before state mutation. Invalid payloads
MUST fail fast with explicit errors and MUST NOT silently degrade into untyped output.

Rationale: Contract-first IO reduces ambiguity and guards state integrity.

### VI. Spec-Kit Reproducibility and Drift Control

Spec-Kit artifacts (spec, plan, tasks, contracts) are the authoritative design source.
Scaffolding regeneration MUST preserve declared contracts and module boundaries. Manual
code deviations MUST be reconciled by updating specs first, then regenerating.

Rationale: Reproducibility keeps implementation aligned with documented intent.

### VII. Testability and Verification by Default

Domain logic, state reducers, prompt builders, and schema validators MUST be testable
without live model calls. Phase 1 MUST include unit tests for state transitions,
prompt composition, schema validation, and persistence behavior.

Rationale: Fast, isolated tests provide quality gates for iterative story engine work.

### VIII. SQLite-Default Persistence Policy

All persisted application data MUST use SQLite unless a feature specification explicitly
states another persistence mechanism. Explicit exceptions MAY include session storage,
browser local storage, or an alternate backend store, and each exception MUST document
scope, rationale, and lifecycle boundaries.

Rationale: A single default persistence backend reduces architectural drift while
allowing intentional, traceable exceptions.

### IX. Serilog-Centric Observability and Layer Coverage

The application MUST use Serilog as the primary logging framework and integrate with
the .NET 9 logging pipeline using modern best practices: structured logging, message
templates, contextual enrichment, correlation identifiers, and centralized
configuration. Logging MUST exist across all layers, components, and services.
All major code calls (for example request entry points, orchestration steps,
persistence operations, adapter calls, and error boundaries) MUST emit Information
level logs at minimum. The runtime MUST support easy log-level overrides,
including Verbose-level diagnostics for deep troubleshooting without code changes.

Rationale: Consistent, structured, and configurable logging improves operability,
incident response, and root-cause analysis.

## Phase 1 Out of Scope

The following capabilities are explicitly out of scope for Phase 1:

- Audio generation
- Image generation
- Multi-agent orchestration
- Cloud model integration
- Multiplayer or networked features

## Phase 1 Deliverables

Phase 1 deliverables MUST include:

- C# solution with modular projects
- Story engine with state machine
- Prompt builder
- Local model client
- Scene and choice schema
- State summarization logic
- Serilog-based logging infrastructure and SQLite-backed persistence abstraction

## Starting Specifications (Spec-Kit Inputs)

### Domain Model Definitions

Phase 1 baseline domain objects:

- PlayerProfile: name, personality traits, preferences, boundaries
- WorldState: locations, time of day, relationship scores, key-value flags
- Scene: id, narrative, choices[]
- Choice: id, label, tag
- StoryState: PlayerProfile, WorldState, History[], Flags{}
- SceneLog: scene id, choice tag, timestamp

### Engine Modules

The baseline module split is:

- Adventure.Core: domain models, interfaces, enums, constants
- Adventure.Engine: state machine, scene generator, state reducer, summarizer,
  orchestration loop
- Adventure.Prompts: prompt templates, prompt builder, JSON schema definitions
- Adventure.Models: local model client adapters and DTOs
- Adventure.Tests: unit tests for transitions, prompts, and schema validation

### Prompt Contract Baseline

System prompts MUST instruct model behavior for continuity and strict JSON output.
User prompts MUST include story summary, player profile, world state, last choice,
instructions, and JSON schema.

### JSON Schema Baseline

Scene generation responses MUST conform to this minimum structure:

- narrative: string
- choices: array of objects with id, label, tag (all strings)

### State Machine Baseline

Choice tags MUST map deterministically to state updates. Relationship scores and flags
MUST be updated by reducer rules. History logs and summaries MUST update incrementally
for every accepted scene transition.

### Persistence Baseline

Phase 1 persistence MUST use SQLite for persisted state and logs behind repository
abstractions. Non-SQLite persistence (for example session storage, local storage, or
other backend stores) MUST be explicitly declared per feature with documented rationale.

### Logging Baseline

Phase 1 logging MUST be implemented with Serilog and wired through the .NET 9 logging
abstractions. Logs MUST be structured and include sufficient context to trace
cross-layer flows. Information-level logs MUST exist for major execution paths, and
operators MUST be able to increase verbosity (including Verbose) via configuration.

## Governance

This constitution overrides conflicting project conventions for architecture and
delivery workflow.

Amendment procedure:

1. Propose amendment in a spec or governance change note with explicit rationale.
2. Classify version bump using semantic versioning policy below.
3. Update constitution and dependent templates in the same change.
4. Record a Sync Impact Report at the top of the constitution.
5. Obtain maintainer approval before merging.

Versioning policy:

- MAJOR: Backward-incompatible governance changes or principle removals/redefinitions.
- MINOR: New principle or mandatory section; materially expanded guidance.
- PATCH: Clarifications, wording improvements, typo fixes, non-semantic refinements.

Compliance review expectations:

- Every plan MUST pass the Constitution Check gates before implementation begins.
- Every tasks file MUST include work for deterministic state handling, schema
  validation, local-first operation, and SQLite persistence defaults (or explicit
  exception handling) when in scope.
- Every tasks file MUST include Serilog setup, cross-layer logging coverage, and
  configurable log-level controls when application behavior is implemented.
- Pull requests MUST cite affected principles when changing architecture or contracts.

**Version**: 1.2.0 | **Ratified**: TODO(RATIFICATION_DATE): Original adoption date not found in repository history. | **Last Amended**: 2026-03-08
