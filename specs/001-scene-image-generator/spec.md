# Feature Specification: Scene Image Generator

**Feature Branch**: `001-scene-image-generator`  
**Created**: 2026-08-19  
**Status**: Draft  
**Input**: User description: "Scene Image Generator Engine (B-032): the user selects a narrative interaction in the RP workspace and clicks Generate image, which opens a dedicated Image Studio screen. The studio runs a two-stage pipeline: (1) a pre-processor model consumes the interaction, the scene's atmosphere (setting, time of day, phase, characters, resolved intensity), and the user's image settings (style, size, explicitness) and produces an editable image prompt; (2) an image generation model renders the prompt into an image that is saved and persisted. The studio supports iterative refinement (edit prompt, change style/size, refine with AI, regenerate). Interactions that have images show an indicator, and a separate per-session gallery lists all generated images. Provider integration extends the existing Model Manager with image capability and two new functions. Phase 1 = all plumbing plus a POC to validate NSFW behavior, image quality, and the basics. Future: character likeness from reference images associated with character profiles."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Generate an image for a story moment (Priority: P1)

As a user, I can pick any narrative interaction in a roleplay session and open a dedicated image screen that produces a saved image of that moment, so I can visualize key scenes from the story.

**Why this priority**: This is the core value of the feature — the entire flow (select interaction → build prompt → render → save → view) must work before anything else is meaningful. Without it there is no feature.

**Independent Test**: Can be fully tested by opening a session, clicking "Generate image" on an interaction, and confirming an image appears on a dedicated screen and is still there after reopening the screen.

**Acceptance Scenarios**:

1. **Given** a session that has at least one narrative interaction, **When** I click the generate-image action on an interaction, **Then** a dedicated image screen opens for that interaction.
2. **Given** the image screen is open, **When** I request an image, **Then** the system produces an image from the interaction and the scene's context and displays it on the screen.
3. **Given** a generated image, **When** I leave the screen and reopen it, **Then** the saved image is still shown.
4. **Given** image generation has not been configured, **When** I request an image, **Then** I see clear guidance explaining how to configure an image provider and model, rather than a silent failure.

---

### User Story 2 - Build and edit the image prompt (Priority: P1)

As a user, I can have the system draft an image prompt from the story moment and scene context, then review and edit that prompt before rendering, so the final image matches what I envision.

**Why this priority**: Prompt control is what separates a usable tool from a black box — the user must be able to see and shape the prompt that drives the image.

**Independent Test**: Can be fully tested by generating a prompt, editing the prompt text, rendering, and confirming the resulting image differs from a render of the unedited prompt.

**Acceptance Scenarios**:

1. **Given** the image screen, **When** I trigger prompt generation, **Then** an editable prompt is produced from the selected interaction, the scene context (characters, setting, time of day, phase, intensity), and my image settings.
2. **Given** a generated prompt, **When** I edit it and render, **Then** an image is created from my edited prompt.
3. **Given** I change image settings (style, size, explicitness), **When** I regenerate the prompt, **Then** the new prompt reflects those settings.

---

### User Story 3 - Iterate and refine (Priority: P2)

As a user, I can iterate on an image — regenerate versions, tweak the prompt manually or with AI assistance — and keep each version, so I can explore variations without losing earlier results.

**Why this priority**: Iteration makes the feature practical for actually arriving at a desired image, but is secondary to the basic generate-and-view flow.

**Independent Test**: Can be fully tested by rendering an image, editing the prompt, rendering again, and confirming both versions are retained.

**Acceptance Scenarios**:

1. **Given** a rendered image, **When** I regenerate, **Then** a new version is created and the previous version is kept.
2. **Given** a prompt, **When** I ask the system to refine it with a short instruction (e.g. "more atmospheric"), **Then** the prompt is updated accordingly and can be rendered again.
3. **Given** a generation in progress, **When** I attempt another generation for the same moment, **Then** the system prevents duplicate in-flight work and shows progress instead of a confusing second result.

---

### User Story 4 - See which moments have images (Priority: P2)

As a user, I can see at a glance which interactions in a session already have images, so I know what has been visualized.

**Why this priority**: Lightweight visibility helps the user track progress across a long session and is cheap to deliver alongside the core flow.

**Independent Test**: Can be fully tested by generating an image for an interaction and confirming an image indicator (with count) appears on that interaction in the session view.

**Acceptance Scenarios**:

1. **Given** an interaction with at least one saved image, **When** I view the session, **Then** the interaction shows a visible image indicator with the count of images.
2. **Given** an interaction with no saved images, **When** I view the session, **Then** no image indicator is shown.

---

### User Story 5 - Browse all session images in a gallery (Priority: P2)

As a user, I can open a separate gallery that lists every image generated for a session, grouped by interaction, so I can review the whole session's visuals in one place.

**Why this priority**: The gallery is a distinct browsing surface that adds real value once images exist, but is not required for generating the first image.

**Independent Test**: Can be fully tested by generating images for multiple interactions and confirming the gallery lists them grouped by interaction, with full-size viewing.

**Acceptance Scenarios**:

1. **Given** a session with images, **When** I open the gallery, **Then** all of the session's images are listed, grouped by the interaction they belong to.
2. **Given** the gallery is open, **When** I select an image, **Then** I can view it full size and can open the image screen for its interaction.
3. **Given** a session with no images, **When** I open the gallery, **Then** I see an empty-state message rather than an error.

---

### User Story 6 - Content policy handling (Priority: P1)

As a user, I can rely on the system to respect the image provider's content rules — generating explicit content only when the configured provider allows it, and otherwise producing safe-for-work output or a clear explanation.

**Why this priority**: Explicit story content is a core part of this product's roleplay, and how the system behaves with an adult-content-filtering provider determines whether the feature is usable at all — so this must be correct from the POC onward.

**Independent Test**: Can be fully tested by requesting an explicit image from a provider configured to filter adult content and confirming the system either produces a safe-for-work version or clearly explains the limitation (never silently bypasses).

**Acceptance Scenarios**:

1. **Given** an image provider configured to filter adult content, **When** the moment is explicit and I request an image, **Then** the system produces a safe-for-work version or explains the limitation clearly — it never silently bypasses the filter.
2. **Given** an image provider configured to allow adult content, **When** I enable explicit rendering, **Then** explicit content can be generated.
3. **Given** an image provider whose content policy is not configured, **When** I request an image, **Then** the system asks for the policy to be configured rather than assuming one.

---

### User Story 7 - Configure image capability (Priority: P1)

As a user (or administrator), I can configure an image provider, register an image model, and assign models for both prompt-building and image rendering, so the feature works end-to-end with my chosen provider.

**Why this priority**: Image generation is unusable until an image provider and model are configured; configuration guidance is the gating requirement behind every other story.

**Independent Test**: Can be fully tested by configuring an image-capable provider and an image model, then generating an image successfully.

**Acceptance Scenarios**:

1. **Given** the configuration screen, **When** I register an image-capable provider and an image model, **Then** the image model becomes available for image rendering.
2. **Given** a provider that only supports text, **When** I try to use it for image rendering, **Then** the system clearly indicates it is not image-capable.
3. **Given** no image model is configured, **When** I attempt to generate an image, **Then** I am directed to the configuration screen.

---

### Edge Cases

- **No image model configured**: generation must surface clear setup guidance, never silently fail.
- **Provider rejects adult content**: the system must produce a safe-for-work version or a clear explanation — never bypass.
- **Provider content policy unset**: the system must require explicit policy configuration rather than assuming one.
- **Empty or very short interaction**: the system should still produce a usable prompt from scene context, or explain that there isn't enough content.
- **Very long interaction**: the prompt builder should work with a limited excerpt without failing.
- **Deleting an image**: deletion removes the image from the image screen and the gallery, and updates the interaction indicator count.
- **Regenerate while a generation is in progress**: duplicate in-flight generation is prevented.
- **Interaction that no longer exists** (e.g. session data changed): the image screen should handle a missing source interaction gracefully with a message.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow the user to open an image generation screen for any narrative interaction in an active roleplay session.
- **FR-002**: System MUST build an image prompt from the selected interaction combined with the session's scene context — including characters present, setting, time of day, narrative phase, and resolved intensity — and the user's chosen image settings.
- **FR-003**: System MUST provide two separate user actions: one to generate or refine the image prompt, and one to render the image from the current prompt.
- **FR-004**: The generated image prompt MUST be editable by the user before rendering.
- **FR-005**: Rendered images MUST be saved and MUST remain viewable when the image screen is reopened for that interaction.
- **FR-006**: Each render MUST create a distinct saved version; regenerating MUST NOT overwrite previous versions.
- **FR-007**: System MUST allow the user to specify image attributes including style, size, and explicitness, and MUST reflect them in generated prompts.
- **FR-008**: System MUST show a visible indicator with an image count on any interaction that has saved images.
- **FR-009**: System MUST provide a separate gallery view of all images for a session, grouped by the interaction each belongs to.
- **FR-010**: When image generation is not configured, system MUST surface clear configuration guidance and MUST NOT fail silently.
- **FR-011**: Explicit/adult content MUST only be generated when the configured image provider permits it; otherwise the system MUST produce safe-for-work output or clearly explain the limitation, and MUST NOT bypass the provider's policy.
- **FR-012**: System MUST allow the user to delete a saved image, updating the indicator and gallery accordingly.
- **FR-013**: System MUST support per-session image defaults (style, size, explicitness) that seed the image screen for each interaction.
- **FR-014**: System MUST record for each saved image the exact prompt and settings used, the provider/model, and the generation status, so the user can understand and audit what was produced.
- **FR-015**: Persisted feature data MUST use SQLite unless this spec explicitly states and justifies a different store (for example session storage, local storage, or another backend store).
- **FR-016**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-017**: Major execution paths across layers/components/services MUST emit Information-level logs and provide actionable failure/error logs.
- **FR-018**: Log levels MUST be configurable via settings (including Verbose) without code changes.

### Key Entities *(include if feature involves data)*

- **Narrative Interaction**: The story moment in a session that an image depicts; each image is tied to one interaction.
- **Image Prompt**: The generated (and user-editable) prompt text for an image, tied to an interaction and to a snapshot of the image settings used to produce it.
- **Generated Image**: A saved image result tied to an interaction; records the exact prompt, settings, provider/model, content policy, generation status, and creation time used.
- **Image Settings**: The user-controlled attributes for generation — style (e.g. realistic, anime, cartoon), size, aspect ratio, and explicitness — available as session defaults and per-request overrides.
- **Image Provider & Models**: The external service that renders images, characterized by its image capability and its content policy (safe-for-work vs. adult-allowed), together with the registered image model used for rendering and the text model used for building prompts.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can go from selecting an interaction to viewing a saved image in under 2 minutes.
- **SC-002**: 100% of interactions that have saved images show the image indicator in the session view.
- **SC-003**: 100% of saved images remain viewable after the image screen is closed and reopened.
- **SC-004**: In the POC, 0% of explicit-content requests against an adult-filtering provider bypass the provider's policy; the system produces a safe-for-work version or a clear explanation every time.
- **SC-005**: In the POC, at least 90% of generated images are rated acceptable by the user across the tested styles.
- **SC-006**: 100% of generation attempts made while image generation is unconfigured result in clear configuration guidance rather than silent failure.
- **SC-007**: 100% of session images appear in the gallery grouped by interaction.
- **SC-008**: Regenerating an image never loses the previous version (100% of regeneration attempts).

### Assumptions

- Image generation is **manual only** — it is not triggered automatically after each turn.
- The image screen is a **dedicated view** (not embedded in the story stream); the story stream only shows an indicator.
- The gallery is **per-session** (not a global cross-session gallery) in this scope.
- Phase 1 delivers the full plumbing plus a proof-of-concept validated for NSFW behavior, image quality, and the basic flow; advanced iteration polish and character-likeness features are later phases.
- Character likeness from reference photos on character profiles is out of scope for this phase (roadmap item).
- The user edits image attributes (style/size/explicitness) per request; sensible defaults are seeded from session settings.
