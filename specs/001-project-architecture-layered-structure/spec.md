# DreamGenClone Specification

## Overview
DreamGenClone is a Blazor Server application that reproduces DreamGen's core workflow and UI behavior.
The product includes two primary creative modes:

- Story Mode
- Role-Play Mode

It also includes:

- Scenario system
- Template library
- Session manager
- Assistant tools

All AI generation is local and uses LM Studio at:

`http://127.0.0.1:1234`

DreamGenClone must replicate DreamGen workflows and interaction patterns from the provided references.
DreamGenClone must not include AdventureEngine or emotional-simulation features.

## Clarifications

### Session 2026-03-09

- Q: How should template images be persisted? -> A: Store template image files on local disk; store paths/metadata in SQLite.
- Q: What is the authentication model for Phase 1? -> A: Single local user with no authentication.
- Q: How should JSON session import validation behave? -> A: Strict import with required schema/version validation and explicit errors on invalid payloads.
- Q: How should assistant context windows be managed at limits? -> A: Deterministic recency truncation with pinned critical context retained.
- Q: How should auto-save be triggered? -> A: Save on meaningful changes using a short debounce window.
- Q: What UI visual baseline should be used during current implementation phases? -> A: Keep the default new Blazor template styling (layout, colors, spacing, and nav visual language) unless a later approved phase explicitly changes theme/styling.

## Scope and Boundaries

### In Scope

- Replicating documented DreamGen behavior for story writing and role-play
- Scenario creation/editing and session-based use of scenarios
- Reusable templates for personas, characters, locations, and objects
- Local model inference via LM Studio
- Session persistence, export, and import

### Out of Scope

- Cloud model providers
- Multiplayer/networked collaboration
- AdventureEngine-specific systems
- Emotion simulation systems
- User account authentication/authorization in Phase 1

## Reference Material

The following guides are authoritative references for expected behavior:

- `.github/spec/references/dreamgen_story_guide.md`
- `.github/spec/references/dreamgen_roleplay_guide.md`
- `.github/spec/references/dreamgen_scenario_guide.md`

All feature behavior and UX flow must remain aligned with these guides unless a documented exception is approved.

## Example Scenario References

The following files are technical structure references only:

- `.github/spec/references/scenario_example.md`
- `.github/spec/references/scenario_session_example.md`

They are used to understand:

- Scenario JSON structure
- Entity definitions and relationships
- Scenario starts and interaction trees
- Session serialization and linkage

Only structural format should be used from these examples.
Narrative content in examples must not influence product behavior or design decisions.

## Architecture

DreamGenClone follows a layered architecture with strict boundaries.

### UI Layer (Blazor)

- Razor pages/components for Story Mode, Role-Play Mode, Scenario Editor, Template Library, Session Manager, Model Settings, and Assistants
- Session state containers
- Component rendering for text blocks, instruction blocks, and role-play interactions

### Application Layer

- `ScenarioService`
- `SessionService`
- `TemplateService`
- `ModelService` (LM Studio integration)
- `ExportService`
- Story/role-play orchestration
- Validation and transformation rules

### Domain Layer

Core domain models include:

- Scenario system: `Scenario`, `Plot`, `Setting`, `Style`, `Character`, `Location`, `Object`, `Opening`, `Example`
- Template library: `PersonaTemplate`, `CharacterTemplate`, `LocationTemplate`, `ObjectTemplate`
- Story mode: `TextBlock`, `InstructionBlock`
- Role-play mode: `Interaction` (`Narrative`, `CharacterMessage`, `Instruction`), `BehaviorSettings`
- Sessions: `StorySession`, `RolePlaySession`, `SessionMetadata`

### Infrastructure Layer

- Persistence: SQLite (default and required unless explicitly overridden in a future approved spec)
- LM Studio HTTP client
- Local disk storage for template images with SQLite metadata references
- JSON export/import adapters
- Serilog logging integration

## Cross-Cutting Constraints

### Persistence

- Persisted data must use SQLite.
- Template image binaries are stored on local disk; SQLite stores image metadata and file references.
- Session export/import remains JSON-based file interchange.

### Import/Export Validation

- Session JSON import is strict: payloads must pass schema and version validation before loading.
- Invalid or unsupported JSON imports must fail with explicit, actionable validation errors.

### Identity and Access

- Phase 1 operates as a single local user experience.
- No sign-in, account system, or role-based authorization is required in Phase 1.

### Logging

- Logging framework must be Serilog.
- Logging must follow modern .NET 9 structured logging practices.
- Information-level logs must exist for major execution paths across layers, services, and components.
- Log levels must be externally configurable, including Verbose.

### Assistant Context Policy

- Assistant context truncation must be deterministic by recency.
- Pinned critical context must be retained when context limits are reached.
- Non-pinned older context may be truncated first.

### UI Baseline Style

- The app must keep default new Blazor template styling during current implementation phases.
- Existing scaffold visual language (sidebar gradient, top row, standard Bootstrap controls, default spacing/typography) is the required baseline.
- Any custom visual theming must be deferred to a later approved phase and documented in spec updates.

## Delivery Parts

### Part 1 - Project Architecture and Layered Structure

Defines the project skeleton and dependency boundaries.

**Includes:**

- Blazor Server project setup
- Folder and namespace structure for UI/Application/Domain/Infrastructure
- Dependency injection registration
- LM Studio client abstraction and base implementation
- Shared interfaces and contracts

**Acceptance Criteria:**

- Solution builds in Debug and Release
- Layer projects/folders and namespaces match architecture definition
- Dependency direction follows UI -> Application -> Domain, with Infrastructure consumed via abstractions
- LM Studio health/test request succeeds against `http://127.0.0.1:1234`
- UI renders using default new Blazor scaffold style without custom theme overrides

### Part 2 - Template Library

Implements reusable template management.

**Includes:**

- Persona templates (POV)
- Character templates (NPC)
- Location templates
- Object templates
- CRUD views and preview support
- Persistent storage and retrieval

**Acceptance Criteria:**

- Users can create, edit, delete, and preview each template type
- Template changes persist after application restart
- Templates are selectable during scenario/session setup

### Part 3 - Scenario Editor

Implements scenario authoring and editing.

**Includes:**

- Plot, Setting, Style
- Characters, Locations, Objects (including template-based selection)
- Openings and Examples
- Token count display

**Acceptance Criteria:**

- Users can create, edit, and save scenarios
- Saved scenarios are retrievable and editable later
- Token count updates after scenario content changes

### Part 4 - Story Mode Engine

Implements block-based writing workflow.

**Includes:**

- User and AI text blocks
- Instruction blocks
- Continue action
- Rewind and Undo
- Writing Assistant
- Scenario sidebar integration

**Acceptance Criteria:**

- Users can create/edit/reorder applicable story blocks
- AI generation produces new story blocks through LM Studio
- Rewind and Undo restore expected previous state without corruption
- Writing Assistant can read scenario context plus recent story context

### Part 5 - Role-Play Mode Engine

Implements interaction-based role-play workflow.

**Includes:**

- Interaction types: Narrative, Character Message, Instruction
- Character selector and continue controls (You / NPC / Custom)
- Behavior modes: Take-Turns, Spectate, NPC-Only
- Branches and forks
- Role-Play Assistant

**Acceptance Criteria:**

- Users can create and edit all interaction types
- AI continuation follows selected continuation mode and character context
- Behavior mode selection changes generation behavior as expected
- Branch and fork operations preserve prior history and create independent alternate paths
- Role-Play Assistant can read scenario plus recent interaction context

### Part 6 - Session Manager

Implements session lifecycle operations.

**Includes:**

- Auto-save
- Session list views ("Your Stories" / "Your Role-Plays")
- Clone session
- Fork session
- Export: Markdown and JSON
- Import: JSON

**Acceptance Criteria:**

- Sessions persist across browser refresh and app restart
- Clone and Fork produce independent sessions
- Markdown and JSON exports are valid and complete
- JSON import restores session content, structure, and metadata only when schema/version validation passes
- Invalid JSON import attempts are rejected with explicit validation errors and no partial session mutation
- Auto-save triggers on meaningful session changes with a short debounce window to avoid excessive write churn

### Part 7 - Model Settings

Implements per-session model and generation configuration.

**Includes:**

- Model selection
- Temperature
- Top-p
- Max tokens
- Retry with model

**Acceptance Criteria:**

- Settings persist at session scope
- Retry uses currently selected retry model/settings
- LM Studio request payload matches configured generation parameters

### Part 8 - Assistants

Implements assistant workflows for story and role-play.

**Includes:**

- Writing Assistant (Story Mode)
- Role-Play Assistant (Role-Play Mode)
- Context window management policy
- Clear chat action

**Acceptance Criteria:**

- Assistants can read scenario context plus recent mode-specific content
- Assistant outputs are contextually relevant to current mode
- Clear chat resets assistant conversation state without affecting main session content
- When context limits are exceeded, truncation behavior is deterministic and retains pinned critical context

## Assumptions

- LM Studio is installed and running locally before generation is requested.
- Users may switch between Story Mode and Role-Play Mode for different sessions.
- Exported JSON is treated as interchange format, not the primary persistence backend.

