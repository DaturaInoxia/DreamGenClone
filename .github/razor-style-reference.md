# Razor Style Reference — DreamGenClone Project

This reference provides real examples of the Razor patterns used in this project. When editing `.razor` files, match these patterns. When the model needs to understand project conventions, point it here.

---

## Architecture: Blazor Server (Interactive Server-Side Rendering)

- **Framework**: .NET 9 Blazor Server
- **Render mode**: `InteractiveServer` (declared per-page or inherited from `App.razor`)
- **CSS Framework**: Bootstrap 5 (via `wwwroot/`)
- **No `.cshtml` files**: All UI is `.razor` components
- **No `.razor.cs` code-behind files**: All logic is in `@code` blocks within the `.razor` file

---

## Pattern 1: Simple Page Component

**File**: `Components/Pages/RolePlayMode.razor`

```razor
@page "/roleplay"
@rendermode InteractiveServer

@using DreamGenClone.Web.Application.RolePlay
@inject NavigationManager Navigation

<PageTitle>Role-Play Mode</PageTitle>

<div class="container-fluid mt-4">
    <h1>Role-Play Mode</h1>
    <p class="text-muted">Redirecting to saved sessions...</p>
</div>

@code {
    protected override void OnAfterRender(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        Navigation.NavigateTo(RolePlayRoutes.Sessions, replace: true);
    }
}
```

**Key conventions**:
- `@page` directive always first, with route in double quotes
- `@rendermode InteractiveServer` second (enables interactivity)
- `@using` and `@inject` directives before any HTML content
- `@code { }` block at the bottom for C# logic
- Bootstrap utility classes: `container-fluid`, `mt-4`, `text-muted`
- Lifecycle methods: `OnInitializedAsync`, `OnParametersSetAsync`, `OnAfterRender`

---

## Pattern 2: Complex Form Component (with Preset/Custom Select)

**File**: `Components/Shared/PhysicalAttributesEditor.razor`

```razor
@using DreamGenClone.Domain.Templates

<div class="physical-attributes-editor">

    @* ── Group: General ── *@
    <div class="pa-group-header">General</div>
    <div class="row g-2 mb-2">
        <div class="col-md-3">
            <label class="form-label small">Age</label>
            <input type="text" class="form-control form-control-sm"
                   value="@Attributes?.Age"
                   @onchange="@(e => SetField(a => a.Age = e.Value?.ToString()))" />
        </div>
        <div class="col-md-3">
            <label class="form-label small">Ethnicity</label>
            <select class="form-select form-select-sm"
                    value="@GetSelectValue(Attributes?.Ethnicity, PhysicalAttributesCatalog.Ethnicities)"
                    @onchange="@(e => OnPresetChanged(e, PhysicalAttributesCatalog.Ethnicities, a => a.Ethnicity = e.Value?.ToString(), v => _ethnicityCustom = v))">
                <option value="">(none)</option>
                @foreach (var opt in PhysicalAttributesCatalog.Ethnicities)
                {
                    <option value="@opt">@opt</option>
                }
                <option value="__custom__">(Custom...)</option>
            </select>
            @if (IsCustom(Attributes?.Ethnicity, PhysicalAttributesCatalog.Ethnicities))
            {
                <input type="text" class="form-control form-control-sm mt-1"
                       value="@Attributes?.Ethnicity"
                       @onchange="@(e => SetField(a => a.Ethnicity = e.Value?.ToString()))" />
            }
        </div>
    </div>

    @* ── Group: Body Measurements (female / mixed) ── *@
    @if (!string.Equals(Gender, "Male", StringComparison.OrdinalIgnoreCase))
    {
        <div class="pa-group-header">Body Measurements</div>
        <!-- conditional fields -->
    }
</div>
```

**Key conventions**:
- **Bootstrap grid**: `row g-2 mb-2` (grid row with gutters and margin-bottom)
- **Bootstrap columns**: `col-md-3`, `col-md-4` (responsive breakpoints)
- **Bootstrap form controls**: `form-control form-control-sm`, `form-select form-select-sm`
- **Bootstrap labels**: `form-label small`
- **Razor comments**: `@* comment *@` (NOT HTML `<!-- comment -->`)
- **Event handlers**: `@onchange="@(e => Method(e, args))"` — lambda wrapping method call
- **Conditional rendering**: `@if (condition) { ... }` blocks
- **Loop rendering**: `@foreach (var opt in collection) { ... }` blocks
- **Null-safe access**: `Attributes?.Property` (null-conditional operator)
- **Parameter binding**: `value="@expr"` (one-way), not `@bind-value`

---

## Pattern 3: Complex Page with Heavy DI

**File**: `Components/Pages/RolePlayWorkspace.razor`

```razor
@page "/roleplay/workspace/{sessionId}"
@rendermode InteractiveServer

@using DreamGenClone.Web.Application.RolePlay
@using DreamGenClone.Web.Domain.RolePlay
@using V2RolePlay = DreamGenClone.Domain.RolePlay
@using Microsoft.Extensions.Options

@inject IRolePlayEngineService RolePlayEngine
@inject IRolePlayAdaptiveStateService AdaptiveStateService
@inject ILogger<RolePlayWorkspace> Logger
@inject IOptions<LmStudioOptions> LmStudioConfiguration
@inject NavigationManager Navigation
@inject IJSRuntime JS

@implements IAsyncDisposable

@code {
    [Parameter]
    public string sessionId { get; set; } = string.Empty;

    private RolePlaySession? _session;
    private bool _backgroundSubmissionRunning = false;
    // ... many more fields ...
}
```

**Key conventions**:
- Route parameters: `{sessionId}` in the `@page` route, bound via `[Parameter]`
- Namespace aliases: `@using V2RolePlay = DreamGenClone.Domain.RolePlay`
- Many `@inject` directives for service dependencies (common in complex pages)
- `@implements IAsyncDisposable` for cleanup (timers, subscriptions)
- Private fields use `_camelCase` prefix
- Nullable reference types: `RolePlaySession? _session`

---

## Pattern 4: Layout Component

**File**: `Components/Layout/MainLayout.razor`

```razor
@inherits LayoutComponentBase
@using DreamGenClone.Components.Shared

<div class="page">
    <div class="sidebar">
        <NavMenu />
    </div>
    <div class="sidebar-backdrop" onclick="sidebarToggle()"></div>
    <button class="sidebar-toggle-btn" onclick="sidebarToggle()" title="Toggle navigation">&#x2039;</button>
    <main>
        <article class="content px-4">
            @Body
        </article>
    </main>
</div>

<ProcessingToast />

<div id="blazor-error-ui" data-nosnippet>
    An unhandled error has occurred.
    <a href="." class="reload">Reload</a>
    <span class="dismiss">🗙</span>
</div>
```

**Key conventions**:
- `@inherits LayoutComponentBase` — standard Blazor layout base class
- `@Body` — placeholder where page content renders
- `NavMenu` — navigation sidebar component
- `ProcessingToast` — global toast notification component
- Error UI: `#blazor-error-ui` container for unhandled exceptions
- JavaScript interop: `onclick="sidebarToggle()"` for sidebar toggle

---

## Global Imports

**File**: `Components/_Imports.razor`

```razor
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using static Microsoft.AspNetCore.Components.Web.RenderMode
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.JSInterop
@using DreamGenClone
@using DreamGenClone.Components
@using DreamGenClone.Components.Shared
@using DreamGenClone.Components.Scenarios
@using DreamGenClone.Web.Application.Scenarios
@using DreamGenClone.Web.Domain.Scenarios
@using DreamGenClone.Domain.RolePlay
```

These are available in ALL `.razor` files without explicit `@using`.

---

## Common Tag Helpers / Components in This Project

This project does NOT use traditional ASP.NET Tag Helpers (`asp-*` attributes). It uses **Razor Components** with:

| Element / Pattern | Usage |
|-------------------|-------|
| `<input>` | Standard HTML with Bootstrap `form-control` class |
| `<select>` | Standard HTML with Bootstrap `form-select` class |
| `<textarea>` | Standard HTML with Bootstrap `form-control` class |
| `<label>` | Standard HTML with Bootstrap `form-label` class |
| `<PageTitle>` | Blazor built-in — sets browser tab title |
| `<NavMenu />` | Project-specific sidebar navigation |
| `<ProcessingToast />` | Project-specific toast notifications |
| `@onclick`, `@onchange`, `@oninput` | Blazor event binding |
| `@ref` | Component/element reference capture |

**NOT used in this project** (do NOT introduce):
- `<EditForm>`, `<DataAnnotationsValidator>`, `<ValidationSummary>` (Blazor forms)
- `<InputText>`, `<InputSelect>`, `<InputNumber>` (Blazor input components)
- `<Virtualize>` (used sparingly, check `_Imports.razor`)
- `<AuthorizeView>`, `<Authorized>`, `<NotAuthorized>` (Blazor auth components)
- `asp-*` Tag Helpers (ASP.NET Core MVC pattern)

---

## Bootstrap Convention Summary

| Purpose | Classes |
|---------|---------|
| Page container | `container-fluid mt-4` |
| Grid row | `row g-2 mb-2` (g-2 = gutters, mb-2 = margin-bottom) |
| Column | `col-md-3`, `col-md-4`, `col-md-6`, `col-12` |
| Text input | `form-control form-control-sm` |
| Select | `form-select form-select-sm` |
| Label | `form-label small` |
| Button (primary) | `btn btn-primary` |
| Button (small) | `btn btn-sm btn-outline-secondary` |
| Card | `card`, `card-header`, `card-body` |
| Table | `table table-sm table-striped` |
| Alert | `alert alert-info` |
| Spacing | `mt-*`, `mb-*`, `ms-*`, `me-*`, `p-*` |
| Text | `text-muted`, `text-center`, `fw-bold` |

---

## Directive Order Convention

All `.razor` files follow this directive order at the top:

1. `@page "/route"` (if routable)
2. `@rendermode InteractiveServer` (if interactive)
3. `@inherits SomeBaseClass` (if inheriting)
4. `@implements ISomeInterface` (if implementing interface)
5. `@using` directives (namespace imports)
6. `@inject` directives (dependency injection)
7. Then HTML content
8. Then `@code { }` block at bottom
