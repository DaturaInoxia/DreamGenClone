# UITemplate Implementation Trace

This document maps each UITemplate reference to concrete implementation targets.

## Mapping

- `specs/UITemplate/PromtBox.png`
  - Target: Unified prompt composer shell and compact idle state.
  - Planned implementation: `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`, `DreamGenClone.Web/wwwroot/css/roleplay-workspace.css`.

- `specs/UITemplate/PromptBox_Ready.png`
  - Target: Submit-ready prompt state with content entered and action affordances.
  - Planned implementation: `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`, `DreamGenClone.Web/wwwroot/css/roleplay-workspace.css`.

- `specs/UITemplate/Message_Type.png`
  - Target: Intended command picker (`Message`, `Narrative`, `Instruction`) with descriptions.
  - Planned implementation: `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`, `DreamGenClone.Web/Application/RolePlay/RolePlayPromptRouter.cs`.

- `specs/UITemplate/Message_Characters.png`
  - Target: Character/persona/custom identity popup list with selected state.
  - Planned implementation: `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`, `DreamGenClone.Web/Application/RolePlay/RolePlayIdentityOptionsService.cs`.

- `specs/UITemplate/ContinueAs.png`
  - Target: Continue-as quick options with default actor selection.
  - Planned implementation: `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`.

- `specs/UITemplate/ContinueAs_Custom.png`
  - Target: Custom actor naming and selection flow.
  - Planned implementation: `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`, `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`.

- `specs/UITemplate/Settings_Behaviour.png`
  - Target: Right-side settings panel with behavior mode controls.
  - Planned implementation: `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`, `DreamGenClone.Web/wwwroot/css/roleplay-workspace.css`, `DreamGenClone.Web/wwwroot/js/roleplay-workspace.js`.

## Visual Parity Checklist (Phase 6)

- [X] Prompt shell shape and compact idle state aligns with `PromtBox.png`.
- [X] Submit-ready visual state and CTA affordance aligns with `PromptBox_Ready.png`.
- [X] Intent popup includes message, narrative, instruction with selected-state indicator as shown in `Message_Type.png`.
- [X] Identity popup includes scene characters, persona, and custom option with selection and disabled states matching `Message_Characters.png`.
- [X] Continue-as style quick identity selection is represented in unified identity popup behavior from `ContinueAs.png`.
- [X] Custom character input and confirm flow aligns with `ContinueAs_Custom.png`.
- [X] Right-side settings panel includes behavior mode selector and can be resized in line with `Settings_Behaviour.png` intent.
