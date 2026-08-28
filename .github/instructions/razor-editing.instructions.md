---
applyTo: '**/*.razor, **/*.razor.cs, **/*.razor.css'
description: 'Razor editing rules: prevent hallucinated Tag Helpers, enforce full-context reads, strict-mode constraints, grammar reminders, self-validation checklist, and diff-only / micro-step workflows. Use when editing, modifying, or creating .razor files in this project. Essential for DeepSeek and other models prone to Razor syntax corruption.'
---

# Razor Editing Rules

These rules prevent common Razor-specific mistakes when editing `.razor` files. They apply to all models (DeepSeek, Claude, GPT, etc.) but are especially critical for models prone to hallucinating Tag Helpers or mixing Blazor/Razor syntax.

## 1. Full-Context Requirement (CRITICAL)

**Before any edit to a `.razor` file, you MUST read the complete context:**

1. The **entire `.razor` file** being edited (never edit from a fragment)
2. Any **code-behind `.razor.cs` file** if one exists
3. The **model class(es)** bound in the component (classes referenced by `@bind`, `[Parameter]`, or `@inject` types)
4. The **layout file** if modifying layout structure (`MainLayout.razor`, etc.)
5. The **`_Imports.razor`** file if adding new `@using` or `@inject` directives — check existing global imports first

DeepSeek's architecture is optimized for long-context reasoning. Providing full context dramatically improves correctness. Do not rely on memory of what the file contains — read it fresh.

## 2. Strict-Mode Anti-Hallucination Rules

When editing .razor files, obey these constraints absolutely:

### Forbidden Actions
- **Do NOT invent Tag Helpers** that don't exist in this project. If you haven't seen a Tag Helper used elsewhere in the codebase, don't introduce it.
- **Do NOT introduce Blazor-specific syntax** (`@code { }`, `@bind`, Blazor component tags like `<EditForm>`, `<DataAnnotationsValidator>`) unless the file already uses that pattern. This project uses Razor Components, not Blazor WebAssembly.
- **Do NOT restructure layout sections** — preserve `@Body`, sidebar (`<NavMenu />`), `<ProcessingToast />`, and the error UI container exactly as-is.
- **Do NOT alter model binding** — preserve `@bind`, `[Parameter]`, `value="@..."` patterns as they exist.
- **Do NOT change directives** — preserve `@page`, `@rendermode`, `@inject`, `@using`, `@inherits`, `@implements` at the top of the file.
- **Do NOT rewrite the entire file** unless explicitly asked — return only the changed lines.

### Required Preservation
- Preserve all existing `@{}` code blocks — only modify targeted sections within them
- Preserve all `@()` inline expressions — their boundaries and content
- Preserve all `@if`, `@foreach`, `@for` control flow blocks — only change content within
- Preserve all `@* ... *@` Razor comments
- Preserve all HTML structure that is not part of the requested change

## 3. Razor Grammar Reminder

This project uses **Razor Components (Blazor Server)** syntax, not legacy Razor Pages or Blazor WebAssembly:

| Construct | Syntax | Notes |
|-----------|--------|-------|
| Code blocks | `@{ }` | Multi-line C# code within Razor markup |
| Inline expressions | `@(expression)` | Single C# expression evaluated inline |
| Implicit expressions | `@variable` | Shorthand for simple variable output |
| Control flow | `@if`, `@foreach`, `@for` | Standard C# control flow in markup |
| Directives | `@page`, `@rendermode`, `@inject`, `@using`, `@implements`, `@inherits` | Always at top of file before content |
| Component parameters | `[Parameter]` | C# attribute in `@code` block |
| Event handlers | `@onclick`, `@onchange`, `@oninput` | Blazor event binding |
| Attribute binding | `value="@expr"`, `checked="@expr"` | One-way or two-way binding |
| Comments | `@* comment *@` | Razor comments (not HTML comments) |
| Render fragments | `@Body`, `ChildContent` | Layout / templating placeholders |

## 4. Diff-Only Preference

When editing existing `.razor` files:

- **Prefer returning a unified diff** showing only the changed lines
- If a full file rewrite is necessary, state why before returning the full file
- Never rewrite unrelated sections — changes must be surgical and targeted

## 5. Self-Validation Checklist

**Before returning any Razor edit, validate ALL of these checks:**

1. **Balanced blocks**: Every `@{` has a matching `}` — count them explicitly
2. **No invented Tag Helpers**: Every HTML helper element used exists elsewhere in this project — verify by searching the codebase
3. **No Blazor syntax leakage**: No `<EditForm>`, `<DataAnnotationsValidator>`, `<ValidationSummary>`, or other Blazor-specific tags appear unless the file already used them
4. **Model properties exist**: Every `@Model.Property` or `@item.Property` reference maps to an actual property on the declared model class — verify the model file
5. **Layout sections match**: `@Body`, sidebar, nav, toast, and error container are unchanged from the original layout
6. **Directives preserved**: All `@page`, `@rendermode`, `@inject`, `@using` directives at the top are unchanged (unless the task explicitly required adding/removing one)

If ANY check fails, fix the issue before returning. State "Self-validation: all 6 checks passed" or list which checks failed and why.

## 6. Micro-Step Workflow

For complex multi-concept changes, break into separate sequential edits:

| Instead of | Do this |
|-----------|---------|
| "Add a table and a form and update the model binding" | 1. "Add the table only." 2. "Now add the form." 3. "Now update the model binding." |
| "Refactor the layout and add a new section and change the sidebar" | 1. "Change the sidebar." 2. "Add the new section." 3. "Refactor the layout." |

Apply **one logical concept per edit**. Wait for verification before proceeding to the next step.

## 7. Style Conventions

See [`.github/razor-style-reference.md`](../../.github/razor-style-reference.md) for this project's actual Razor patterns and conventions. When in doubt, match existing code in the same file or nearby files.

## Communication Checklist

Before making a Razor edit, state:
1. Which file(s) you read for full context
2. Which specific section you are modifying
3. That you have verified the model properties / Tag Helpers / directives exist in this project

After the edit, state:
1. That self-validation passed (all 6 checks)
2. Which lines changed and why
