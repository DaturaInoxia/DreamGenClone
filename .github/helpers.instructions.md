---
description: 'Helper script conventions and patterns'
applyTo: '**/helpers/**/*.ps1'
---

## Helper Script Standards

All PowerShell scripts in the `helpers/` folder should follow these conventions to ensure consistency, maintainability, and ease of use across the project.

## Naming Conventions

- Use lowercase with hyphens for multi-word script names: `start-webapp.ps1`, `verify-build.ps1`, `setup-database.ps1`
- Use verb-noun pattern matching PowerShell conventions: `Start-*`, `Stop-*`, `Verify-*`, `Reset-*`, `Setup-*`, `Build-*`
- Keep names concise but descriptive of the primary action

## Script Structure

Every helper script should follow this structure:

1. **Parameters Section** - Declare all parameters with descriptions and validation
2. **Usage Documentation** - Comment block showing example usage
3. **Initialization** - Set error handling, mode, and paths
4. **Helper Functions** - Private utility functions used by the script
5. **Main Logic** - Command routing or primary execution

### Template Structure

```powershell
param(
    [Parameter(Position = 0)]
    [ValidateSet("action1", "action2")]
    [string]$Action = "default",

    [Parameter()]
    [string]$CustomParam = "default-value",

    [Parameter()]
    [switch]$Verbose
)

# Usage:
#   ./helpers/script-name.ps1 action1
#   ./helpers/script-name.ps1 action2 -CustomParam "value"

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# Helper functions
function Invoke-Helper {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Green
}

# Main logic
switch ($Action) {
    "action1" { /* ... */ }
    "action2" { /* ... */ }
    default { Write-Host "Unknown action: $Action" -ForegroundColor Red; exit 1 }
}
```

## Parameters and Validation

- Use `[Parameter(Position = 0)]` for primary action parameters
- Always include `[ValidateSet(...)]` for parameters with restricted choices
- Use switches `[switch]` for boolean flags like `-OpenBrowser` or `-Force`
- Provide sensible defaults via `= "default-value"`
- Add XML documentation comments above parameters for clarity

## Error Handling

- Always include these at the top after parameters:
  ```powershell
  Set-StrictMode -Version Latest
  $ErrorActionPreference = "Stop"
  ```
- Use explicit error checks for required tools/files
- Catch expected errors with try-catch when appropriate
- Display helpful error messages in red using `-ForegroundColor Red`

## Usage Documentation

- Include a comment block immediately after parameters showing:
  - All possible invocations (2-4 examples)
  - Parameter combinations
  - Common use cases
- Example:
  ```powershell
  # Usage:
  #   ./helpers/verify-build.ps1
  #   ./helpers/verify-build.ps1 -Project "src/App.csproj"
  #   ./helpers/verify-build.ps1 -Project "tests/*.csproj" -Verbose
  ```

## Output and Logging

- Use `Write-Host` for user-facing messages with color coding:
  - Green (`-ForegroundColor Green`): Success, completed actions
  - Yellow (`-ForegroundColor Yellow`): Warnings, in-progress actions
  - Red (`-ForegroundColor Red`): Errors
  - Cyan (`-ForegroundColor Cyan`): Section headers, important info
- Prefix important messages: "Checking...", "Starting...", "Completed..."
- Leave blank lines (`Write-Host ""`) between logical sections for readability

## Primary Entry Script (Required)

Every project must have a primary entry script in `helpers/` that starts the main application:

- **Name pattern**: `start-[app-name].ps1` (e.g., `start-adventure.ps1`)
- **Multi-action support**: Support multiple modes if applicable:
  - Web apps: Primary action should be `webapp` (or the default)
  - Console apps: Primary action should be `run` or default to it
  - Tools: Primary action should be `execute` or operation-specific (e.g., `build`, `test`)
- **Purpose**: Centralized entry point for developers to launch the application
- **Documentation**: Include usage examples for all supported actions

### Example: Web App Entry Script

```powershell
param(
    [Parameter(Position = 0)]
    [ValidateSet("init", "progress", "inspect", "webapp")]
    [string]$Action = "webapp"
)

# Usage:
#   ./helpers/start-app.ps1                # Starts web app (default)
#   ./helpers/start-app.ps1 webapp -OpenBrowser
#   ./helpers/start-app.ps1 init           # Other actions
```

## Process Management and Cleanup

- For web apps (Blazor, ASP.NET Core) and any long-running processes:
  - **Always include a process cleanup function** that finds and stops existing instances
  - Clear any persisted state files that could cause "already in use" conflicts
  - Wait for process termination before starting new instance (usually 2 seconds)
  - This pattern is essential for Blazor web app development to avoid file lock errors

- **When to include cleanup**:
  - ASP.NET Core / Blazor Server apps (very common; file locks on .exe and .dll)
  - Node.js servers or any framework that binds to ports
  - Build watches or file system watchers
  - Any process that needs exclusive access to files or ports

- **When cleanup is optional**:
  - Pure console utilities that exit cleanly
  - One-shot operations (e.g., code generation, formatting)
  - Scripts that don't start long-running processes

### Full Cleanup Pattern Example (Blazor/ASP.NET Core)

```powershell
function Stop-BlazorProcesses {
    Write-Host "Checking for running AdventureEngine.BlazorServer processes..." -ForegroundColor Yellow
    
    $processes = Get-Process -Name "AdventureEngine.BlazorServer" -ErrorAction SilentlyContinue
    
    if ($processes) {
        Write-Host "Found $($processes.Count) running process(es). Stopping..." -ForegroundColor Yellow
        $processes | ForEach-Object {
            Write-Host "  Stopping process $($_.Id)..." -ForegroundColor Yellow
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
        
        # Wait for processes to fully terminate
        Start-Sleep -Seconds 2
        Write-Host "Processes stopped." -ForegroundColor Green
    }
    else {
        Write-Host "No running processes found." -ForegroundColor Green
    }
    Write-Host ""
}

# Usage in main script:
switch ($Action) {
    "webapp" {
        Stop-BlazorProcesses
        
        # Clear persisted state that could cause locks
        $profilePath = Join-Path $repoRoot "src\App\bin\Debug\net9.0\data\profile.json"
        if (Test-Path $profilePath) {
          Write-Host "Clearing persisted state..." -ForegroundColor Yellow
          Remove-Item $profilePath -Force -ErrorAction SilentlyContinue
          Write-Host "State cleared." -ForegroundColor Green
            Write-Host ""
        }
        
        Write-Host "Starting application..." -ForegroundColor Cyan
        dotnet run --project "./src/App/App.csproj" --urls "http://localhost:5000"
    }
}
```

### Minimal Cleanup Pattern (Generic Process)

For simpler scripts, a minimal version:

```powershell
function Stop-RunningProcesses {
    Write-Host "Checking for running processes..." -ForegroundColor Yellow
    $processes = Get-Process -Name "ProcessName" -ErrorAction SilentlyContinue
    
    if ($processes) {
        Write-Host "Stopping $($processes.Count) process(es)..." -ForegroundColor Yellow
        $processes | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        Write-Host "Processes stopped." -ForegroundColor Green
    }
}
```

## Path Handling

- Always resolve repo root relative to script location:
  ```powershell
  $repoRoot = Split-Path -Parent $PSScriptRoot
  Set-Location $repoRoot
  ```
- Use `Join-Path` for constructing file paths (Windows-safe)
- Avoid hardcoded absolute paths; use relative paths from repo root
- Quote paths that may contain spaces: `"C:\Path With Spaces\file.txt"`

## Dependency Checks

- Verify required tools are available before using them:
  ```powershell
  if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
      Write-Host "Error: dotnet CLI not found. Please install .NET SDK." -ForegroundColor Red
      exit 1
  }
  ```

## Testing and Validation

- Include verify/check logic where applicable:
  - Validate that required files exist before processing
  - Check exit codes of invoked commands
  - Provide clear feedback on success/failure
- Consider adding a `--dry-run` or `-WhatIf` option for destructive operations

## Documentation and Help

- Add a `Get-Help` compatible comment-based help block for discovery:
  ```powershell
  <#
  .SYNOPSIS
  Starts the development server and optionally opens it in the browser.
  
  .DESCRIPTION
  Launches the web app, clears any stale state, and manages running processes.
  
  .EXAMPLE
  ./helpers/start-webapp.ps1
  
  .EXAMPLE
  ./helpers/start-webapp.ps1 -OpenBrowser
  #>
  ```

## Performance and Optimization

- Minimize repeated disk I/O (read files once, cache values)
- Avoid unnecessary sleeps; use them only for process cleanup or state stabilization
- Use `Start-Sleep -Milliseconds` for short waits
- For long operations, provide progress feedback

## Security

- Avoid storing credentials or secrets in scripts
- Use environment variables or secure vaults for sensitive configuration
- Validate and sanitize any user input before using in commands
- Do not use `Invoke-Expression` or similar dynamic execution with user input

## Action Routing Pattern

For scripts with multiple actions, use a `switch` statement as the main dispatcher:

```powershell
switch ($Action) {
    "init" {
        Write-Host "Initializing..." -ForegroundColor Cyan
        # init logic
    }
    "run" {
        Stop-MainProcess
        Write-Host "Running application..." -ForegroundColor Cyan
        # run/start logic
    }
    "verify" {
        Write-Host "Verifying setup..." -ForegroundColor Cyan
        # verify logic
    }
    default {
        Write-Host "Unknown action: $Action" -ForegroundColor Red
        Write-Host "Valid actions: init, run, verify"
        exit 1
    }
}
```

## Examples

See existing helpers for patterns:
- `helpers/start-adventure.ps1` - Multi-action routing, process kill, state cleanup (primary pattern)
- Build/test helpers should follow similar structure with clear success/failure messaging
- New web app projects should follow the Blazor pattern shown in Process Management section above
