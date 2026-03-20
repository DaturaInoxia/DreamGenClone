# Tasks: Role-Play Session Screen Separation

**Input**: Design documents from `specs/001-roleplay-session-screens/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/roleplay-session-flow.md`, `quickstart.md`

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the baseline page and routing structure used by all stories.

- [X] T001 Create role-play route constants in `DreamGenClone.Web/Application/RolePlay/RolePlayRoutes.cs`
- [X] T002 [P] Create dedicated page shells in `DreamGenClone.Web/Components/Pages/RolePlayCreate.razor`, `DreamGenClone.Web/Components/Pages/RolePlaySessionsList.razor`, and `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`
- [X] T003 [P] Create shared session row component in `DreamGenClone.Web/Components/Shared/RolePlaySessionRow.razor`
- [X] T004 Add navigation links for separated role-play flow in `DreamGenClone.Web/Components/Layout/NavMenu.razor`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add domain/service contracts and lifecycle primitives that all user stories depend on.

**CRITICAL**: No user story work starts before this phase is complete.

- [X] T005 Add explicit role-play session status model in `DreamGenClone.Web/Domain/RolePlay/RolePlaySessionStatus.cs` and `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs`
- [X] T006 Update role-play engine contracts for open/delete lifecycle methods in `DreamGenClone.Web/Application/RolePlay/IRolePlayEngineService.cs`
- [X] T007 Implement status transitions and in-memory delete behavior in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- [X] T008 Extend role-play session list projection fields in `DreamGenClone.Web/Application/Sessions/SessionListItem.cs`
- [X] T009 Update persisted session query/serialization handling for status and metadata in `DreamGenClone.Web/Application/Sessions/SessionService.cs`
- [X] T010 [P] Add structured logging event definitions for session lifecycle paths in `DreamGenClone.Web/Application/Sessions/SessionLogEvents.cs`
- [X] T011 [P] Create automated tests project scaffold in `DreamGenClone.Tests/DreamGenClone.Tests.csproj` and add it to `DreamGenClone.sln`
- [X] T012 Add deterministic lifecycle tests for status mapping and hard delete in `DreamGenClone.Tests/RolePlay/RolePlaySessionLifecycleTests.cs`

**Checkpoint**: Foundation complete, user story tasks can proceed.

---

## Phase 3: User Story 1 - Separate Create Screen and Post-Create Navigation (Priority: P1) 🎯 MVP

**Goal**: Create role-play sessions on a dedicated page and redirect to the saved-sessions page after successful creation.

**Independent Test**: Open create page, submit valid session data, verify redirect to saved-sessions page and visibility of newly created session.

- [X] T013 [US1] Implement create-session form UI and validation on `DreamGenClone.Web/Components/Pages/RolePlayCreate.razor`
- [X] T014 [US1] Wire create command and success redirect to saved-sessions route in `DreamGenClone.Web/Components/Pages/RolePlayCreate.razor`
- [X] T015 [P] [US1] Add default `NotStarted` initialization and interaction count baseline in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- [X] T016 [US1] Add created-session visibility and empty-state handling in `DreamGenClone.Web/Components/Pages/RolePlaySessionsList.razor`
- [X] T017 [US1] Add create flow success/error feedback and logging in `DreamGenClone.Web/Components/Pages/RolePlayCreate.razor`

**Checkpoint**: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Start/Continue from Saved Sessions with Dedicated Workspace (Priority: P1)

**Goal**: Start or continue sessions from saved-sessions list and run interactions on a dedicated workspace page.

**Independent Test**: From saved-sessions page select Start/Continue based on status, verify navigation to workspace page and preserved session context.

- [X] T018 [US2] Implement saved-sessions list rendering with Title, Status, Interaction Count, and Last Updated in `DreamGenClone.Web/Components/Pages/RolePlaySessionsList.razor`
- [X] T019 [P] [US2] Implement status-driven Start/Continue button rendering in `DreamGenClone.Web/Components/Shared/RolePlaySessionRow.razor`
- [X] T020 [US2] Implement open-session orchestration and stale-session recovery feedback in `DreamGenClone.Web/Components/Pages/RolePlaySessionsList.razor`
- [X] T021 [US2] Extract interaction and command UI from `DreamGenClone.Web/Components/Pages/RolePlayMode.razor` into `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`
- [X] T022 [US2] Enforce workspace-only interaction route loading by session id in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`
- [X] T023 [US2] Remove create/list management controls from workspace page in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`
- [X] T024 [US2] Enforce Start vs Continue status rules in service layer in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- [X] T025 [US2] Keep legacy role-play entry route redirect behavior in `DreamGenClone.Web/Components/Pages/RolePlayMode.razor`
- [X] T026 [US2] Add Information/Error logs for start/continue/open decision paths in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`

**Checkpoint**: User Story 2 is independently functional and testable.

---

## Phase 5: User Story 3 - Delete Saved Role-Play Sessions from List Screen (Priority: P2)

**Goal**: Delete saved role-play sessions only from the saved-sessions page using confirmed hard delete.

**Independent Test**: Delete a session from saved-sessions page, verify row removal and inability to open deleted session.

- [X] T027 [US3] Add delete action and confirmation UI to session rows in `DreamGenClone.Web/Components/Pages/RolePlaySessionsList.razor`
- [X] T028 [US3] Implement confirmed hard-delete flow with immediate list refresh in `DreamGenClone.Web/Components/Pages/RolePlaySessionsList.razor`
- [X] T029 [US3] Implement engine-level hard delete including in-memory cache eviction in `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- [X] T030 [US3] Ensure delete action is not available on workspace page in `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`
- [X] T031 [US3] Add stale/deleted-session recovery messaging for delete/open failures in `DreamGenClone.Web/Components/Pages/RolePlaySessionsList.razor`
- [X] T032 [US3] Add deletion outcome logging and error diagnostics in `DreamGenClone.Web/Application/Sessions/SessionService.cs`

**Checkpoint**: User Story 3 is independently functional and testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final consistency, documentation, and validation across stories.

- [X] T033 [P] Update final flow contract details in `specs/001-roleplay-session-screens/contracts/roleplay-session-flow.md`
- [X] T034 [P] Update validation walkthrough to implemented routes and labels in `specs/001-roleplay-session-screens/quickstart.md`
- [X] T035 Run end-to-end quickstart validation and record results in `specs/001-roleplay-session-screens/checklists/implementation-validation.md`
- [X] T036 [P] Verify verbose log-level overrides and session lifecycle log configuration in `DreamGenClone.Web/appsettings.Development.json` and `DreamGenClone.Web/appsettings.json`

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1 (Setup): No dependencies.
- Phase 2 (Foundational): Depends on Phase 1 and blocks all user stories.
- Phase 3 (US1): Depends on Phase 2.
- Phase 4 (US2): Depends on Phase 2 and uses routes/pages introduced by Phase 3.
- Phase 5 (US3): Depends on Phase 2 and saved-sessions UI from Phases 3-4.
- Phase 6 (Polish): Depends on completion of all selected user stories.

### User Story Dependencies

- US1 (P1): First MVP slice after Foundational.
- US2 (P1): Depends on foundational lifecycle contracts and saved-sessions navigation structure from US1.
- US3 (P2): Depends on saved-sessions list rendering from US2.

### Within Each User Story

- Service/domain lifecycle updates before page wiring where applicable.
- Shared components before page integration.
- Logging and error paths completed before story sign-off.
- Independent test criteria validated at each checkpoint.

## Parallel Opportunities

- Phase 1: T002 and T003 can run in parallel.
- Phase 2: T010 and T011 can run in parallel after T005-T009 contract direction is set.
- US1: T015 can run in parallel with T013-T014.
- US2: T019 can run in parallel with T018 and T021.
- US3: T030 can run in parallel with T027-T029.
- Polish: T033, T034, and T036 can run in parallel.

## Parallel Example: User Story 1

```bash
# Parallelizable US1 tasks
T013 [US1] Implement create-session form UI and validation in DreamGenClone.Web/Components/Pages/RolePlayCreate.razor
T015 [P] [US1] Add default NotStarted initialization and interaction count baseline in DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs
```

## Parallel Example: User Story 2

```bash
# Parallelizable US2 tasks
T019 [P] [US2] Implement status-driven Start/Continue button rendering in DreamGenClone.Web/Components/Shared/RolePlaySessionRow.razor
T021 [US2] Extract interaction and command UI from DreamGenClone.Web/Components/Pages/RolePlayMode.razor into DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
```

## Parallel Example: User Story 3

```bash
# Parallelizable US3 tasks
T027 [US3] Add delete action and confirmation UI to session rows in DreamGenClone.Web/Components/Pages/RolePlaySessionsList.razor
T030 [US3] Ensure delete action is not available on workspace page in DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor
```

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1 and Phase 2.
2. Deliver Phase 3 (US1).
3. Validate US1 independent test criteria.

### Incremental Delivery

1. Deliver US1 (create + redirect + list visibility).
2. Deliver US2 (start/continue + dedicated workspace).
3. Deliver US3 (hard delete on saved-sessions only).
4. Complete polish and quickstart validation.

### Parallel Team Strategy

1. Team finishes Phase 1-2 together.
2. After foundation:
   - Developer A: US1 page flow.
   - Developer B: US2 workspace separation.
   - Developer C: US3 delete behavior.
3. Merge once each story passes its independent test checkpoint.
