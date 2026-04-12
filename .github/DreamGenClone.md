# DreamGenClone - Repository Overview

**Last Updated:** January 2025  
**.NET Version:** .NET 9  
**Framework:** Blazor Server (Interactive Server-Side Rendering)

---

## What This Project Is

**DreamGenClone** is a .NET web application that serves as a clone/recreation of DreamGen — an AI-powered interactive role-play and storytelling platform. It uses a clean/layered architecture and communicates with LLM backends (like LM Studio, OpenAI-compatible APIs) to generate interactive narrative content.

---

## Architecture Overview (4-Layer Clean Architecture)

```
DreamGenClone.Web (Presentation Layer)
    ↓ depends on
DreamGenClone.Application (Application Layer)
    ↓ depends on
DreamGenClone.Domain (Domain Layer)
    ↓ depends on
DreamGenClone.Infrastructure (Infrastructure Layer)
```

| Layer | Purpose | Key Components |
|-------|---------|----------------|
| **Domain** | Core domain entities and interfaces | Story parsing models, story analysis models (themes, tones, styles), model manager entities, templates, workflow contracts |
| **Application** | Service abstractions and contracts | ICompletionClient, story parser/analysis interfaces, model manager repositories, processing queue, DTOs |
| **Infrastructure** | Concrete implementations | SQLite persistence, HTML fetching/parsing (AngleSharp), LLM completion client (OpenAI-compatible), story analysis services, model management, encrypted API key storage |
| **Web** | Blazor Server UI & web-specific domain | RolePlay/Scenario/Story web domain models, application services, Razor components/pages |

---

## Core Features

### 1. Role-Play Mode (Primary Feature)
- **Session Management**: Create role-play sessions linked to scenarios
- **Multi-Actor System**: 
  - User ("You")
  - NPCs (scenario characters)
  - Custom characters
  - System/Narrative
- **Behavior Modes**:
  - `TakeTurns` - Turn-based with configurable thresholds
  - (Other modes likely exist - check `BehaviorMode` enum)
- **Persona System**:
  - Persona name and description
  - Perspective modes: `FirstPersonInternalMonologue`, `ThirdPersonExternalOnly`, etc.
  - Optional template linking
- **Continue As System**:
  - Generate responses as specific characters
  - Auto-narrative insertion between interactions
  - Scene continue with multiple actors in batch
- **Turn-Taking Enforcement**:
  - Tracks consecutive NPC turns
  - Signals user turn when threshold reached
- **Interaction Commands**:
  - Retry, Rewrite, Make Longer, Ask to Rewrite
  - Edit, Delete, Exclude, Hide, Pin
  - Branch/fork at specific interaction
- **Streaming LLM Responses**: Real-time streaming via SignalR
- **Debug Event Logging**: Full trace of every prompt/response cycle for debugging

**Key Files:**
- `DreamGenClone.Web/Domain/RolePlay/RolePlaySession.cs`
- `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs`
- `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`
- `DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor`

---

### 2. Scenario Editor
- **Scenario Structure**:
  - **Plot**: Description, goals, conflicts
  - **Setting**: World description, world rules, environmental details, locations
  - **Style**: Writing style, tone, point of view, style profile ID, floor/ceiling bounds
  - **Characters**: Name, role, description, perspective mode, base stats
  - **Locations**: Name, description
  - **Objects**: Name, description
  - **Openings**: Starting scenarios
  - **Examples**: Example interactions
- **Scenario Adaptation**: Adapt parsed stories into scenarios
- **Token Counting**: Estimated token count for prompts
- **AI Assistant Integration**: Scenario building assistant with persistent chat threads
- **Base Stat Profiles**: Link to character stat defaults

**Key Files:**
- `DreamGenClone.Web/Domain/Scenarios/Scenario.cs`
- `DreamGenClone.Web/Application/Scenarios/ScenarioService.cs`
- `DreamGenClone.Web/Components/Pages/ScenarioEditor.razor`

---

### 3. Story Parser
- **Web Scraping**: Fetches stories from web URLs with multi-page pagination support
- **HTML Parsing**: Uses AngleSharp for DOM parsing and content extraction
- **Domain-Specific Extraction**: Custom extractors for different story sites
- **Persistence**: Stores parsed stories in SQLite catalog
- **Story Collections**: Grouping and collection matching
- **Lifecycle Management**: Archive, unarchive, purge operations
- **Search & Filter**: Search parsed stories, sort by various criteria

**Key Files:**
- `DreamGenClone.Infrastructure/StoryParser/StoryParserService.cs`
- `DreamGenClone.Infrastructure/StoryParser/HtmlFetchClient.cs`
- `DreamGenClone.Infrastructure/StoryParser/DomainStoryExtractor.cs`
- `DreamGenClone.Infrastructure/StoryParser/PaginationDiscoveryService.cs`
- `DreamGenClone.Web/Components/Pages/StoryParser.razor`

---

### 4. Story Analysis & Profiling

#### Theme Profiles
- **Theme Tiers**:
  - `MustHave` - Actively include when possible
  - `StronglyPrefer` - Bias toward these themes
  - `NiceToHave` - Optionally weave in
  - `Neutral` - No explicit preference
  - `Dislike` - Minimize or avoid
  - `HardDealBreaker` - Never generate content violating these
- **Theme Catalog**: Predefined theme library
- **Theme Tracker**: Per-session active themes with evidence, confidence scores, and selection rules

#### Tone Profiles
- Intensity settings (e.g., mild, moderate, intense)
- Per-scenario and per-session overrides
- Manual tone pinning to disable auto-adaptation

#### Style Profiles
- Writing style descriptions
- Style examples and rules of thumb
- Floor/ceiling intensity bounds
- Style profile selection per session

#### Base Stat Profiles
- Character stat defaults (strength, charisma, etc.)
- Per-character overrides in scenarios
- Resolved stats applied at session creation
- Adaptive stat tracking per character per session

#### Analysis Services
- **Story Ranking**: Rank stories based on preferences
- **Story Summarization**: Generate summaries of stories
- **Prompt Dealbreaker Validation**: Block prompts violating hard constraints
- **Adaptive Engine**: Updates theme/tone/state based on interactions

**Key Files:**
- `DreamGenClone.Domain/StoryAnalysis/*.cs`
- `DreamGenClone.Infrastructure/StoryAnalysis/*.cs`
- `DreamGenClone.Application/StoryAnalysis/*.cs`

---

### 5. Model Manager

#### Multi-Provider Support
- **Providers**: LM Studio, OpenAI-compatible endpoints
- **Provider Types**: Local, OpenAI-compatible, etc.
- **Registered Models**: Model ID, display name, function defaults
- **Health Checks**: Startup health checks for all providers/models

#### Function-to-Model Defaults
- Map application functions to specific models:
  - `RolePlayGeneration` - Main role-play responses
  - `RolePlayAssistant` - AI assistant responses
  - `StoryAnalysis` - Story analysis tasks
  - etc.

#### Session-Level Overrides
- Per-session model ID override
- Temperature, Top-P, Max Tokens overrides
- Separate overrides for main generation and assistant

#### Security
- Encrypted API key storage (AES encryption)
- Secure key encryption/decryption service
- API keys never stored in plain text

#### Background Processing
- **Model Processing Queue**: Background task queue for async operations
- **Processing Worker**: Hosted service that processes queue items
- Task types: Health checks, model analysis, etc.

**Key Files:**
- `DreamGenClone.Domain/ModelManager/*.cs`
- `DreamGenClone.Application/ModelManager/*.cs`
- `DreamGenClone.Infrastructure/ModelManager/*.cs`
- `DreamGenClone.Infrastructure/Processing/ModelProcessingQueue.cs`
- `DreamGenClone.Web/Application/ModelManager/*.cs`

---

### 6. AI Assistants

#### Assistant Types
- **Writing Assistant**: General writing assistance
- **Role-Play Assistant**: Character and story assistance within role-play sessions
- **Scenario Assistant**: Scenario building and editing assistance

#### Features
- Persistent chat threads per session/scenario
- Thread management (create, delete, select)
- Assistant chat messages with role and content
- Separate model settings for assistant vs main generation

**Key Files:**
- `DreamGenClone.Web/Application/Assistants/*.cs`
- `DreamGenClone.Web/Domain/RolePlay/RolePlayAssistantChatThread.cs`

---

### 7. Session Management

#### Persistence
- **SQLite Database**: Custom `ISqlitePersistence` abstraction
- **Auto-Save Coordinator**: Debounced auto-save for sessions
- **Session Types**: Role-play, Story, etc.

#### Lifecycle
- Create sessions
- Load/save sessions
- Delete sessions (soft/hard)
- Clone/fork sessions at specific interaction points

#### Import/Export
- Export sessions to JSON envelopes
- Import sessions from JSON
- Cross-session portability

**Key Files:**
- `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`
- `DreamGenClone.Application/Sessions/AutoSaveCoordinator.cs`
- `DreamGenClone.Web/Application/Sessions/SessionService.cs`
- `DreamGenClone.Web/Application/Export/ExportService.cs`

---

### 8. Templates

#### Template System
- Template definitions (for personas/characters)
- Template images storage
- Template preview functionality

**Key Files:**
- `DreamGenClone.Application/Templates/ITemplateService.cs`
- `DreamGenClone.Infrastructure/Storage/TemplateImageStorageService.cs`
- `DreamGenClone.Web/Components/Templates/TemplateImageEditor.razor`

---

## Key Technical Details

### LLM Integration

#### Completion Client
- **API Format**: OpenAI-compatible `/v1/chat/completions`
- **Transport**: HTTP with streaming (Server-Sent Events) support
- **Authentication**: Bearer token with encrypted API keys
- **Features**:
  - Synchronous and streaming responses
  - Automatic continuation for truncated responses (finish_reason="length")
  - Multiple response field support (content, reasoning_content, reasoning)
  - Configurable timeout per provider
  - Health check endpoints

#### Prompt Engineering
Sophisticated prompt building that injects:
- **Scenario Context**: Name, description, plot, setting, style, POV, goals, conflicts, rules
- **Character Details**: Name, role, description, perspective mode, stats
- **Locations & Objects**: Scene elements
- **Interaction History**: Recent interactions (configurable context window, default 30)
- **Adaptive State**: Character stats, theme tracker with evidence
- **Theme Preferences**: Must-have, strongly-prefer, nice-to-have, dislikes, dealbreakers
- **Tone/Style Profiles**: Intensity settings, floor/ceiling bounds, style guidelines
- **Perspective Instructions**: Different prompts based on narrative vs message intent
- **Writing Instructions**: Word count targets, POV rules, sensory details

### SignalR Integration
- **Maximum Message Size**: 1 MB for large text editing
- **Real-time Streaming**: Stream LLM responses to client
- **Session Synchronization**: Live updates for multi-user scenarios

### Database
- **Type**: SQLite
- **Location**: `data/` directory (excluded from git)
- **Encryption**: API keys encrypted at rest
- **Tables**: Parsed stories, sessions, model settings, theme preferences, etc.

### UI Framework
- **Blazor Server**: Interactive server-side rendering
- **Bootstrap**: CSS framework
- **Custom CSS**: Role-play workspace styling
- **Components**: Modular Razor components with CSS isolation

### Logging
- **Serilog**: Structured logging setup
- **Debug Event Sink**: Role-play debug events written to dedicated sink
- **Event Types**: SessionCreated, InteractionPersisted, PromptSubmitted, LlmRequestSent, LlmResponseReceived, ErrorRaised, etc.
- **Correlation IDs**: Each LLM request has correlation ID for tracing

---

## Important File Locations

### Domain Layer (`DreamGenClone.Domain/`)
```
Domain/
├── Contracts/                    # Domain service contracts
│   ├── IRolePlayWorkflowService.cs
│   ├── ISessionService.cs
│   └── IStoryWorkflowService.cs
├── ModelManager/                 # Model manager entities
├── StoryAnalysis/               # Story analysis entities
├── StoryParser/                 # Story parser entities
├── Templates/                   # Template entities
└── Templates/TemplateDefinition.cs
```

### Application Layer (`DreamGenClone.Application/`)
```
Application/
├── Abstractions/                # ICompletionClient, debug event sink
├── ModelManager/               # Model manager interfaces
├── Processing/                 # Queue abstractions
├── Sessions/                   # Session interfaces
├── StoryAnalysis/              # Story analysis interfaces & DTOs
├── StoryParser/                # Story parser interfaces & DTOs
├── Templates/                  # Template interfaces
└── Validation/                 # Session import validator
```

### Infrastructure Layer (`DreamGenClone.Infrastructure/`)
```
Infrastructure/
├── Configuration/               # Options classes (LmStudio, Persistence, etc.)
├── Logging/                    # Serilog setup
├── ModelManager/               # Model manager implementations
├── Models/                     # CompletionClient implementation
├── Persistence/                # SQLite persistence
├── Processing/                 # Queue implementation & worker
├── Storage/                    # Template image storage
├── StoryAnalysis/              # Story analysis implementations
└── StoryParser/                # Story parser implementations
```

### Web Layer (`DreamGenClone.Web/`)
```
Web/
├── Application/               # Application services (not Domain/Application layers)
│   ├── Assistants/
│   ├── Export/
│   ├── Import/
│   ├── ModelManager/
│   ├── Models/
│   ├── RolePlay/              # Role-play engine, continuation, branching
│   ├── Scenarios/
│   ├── Sessions/
│   ├── Story/
│   ├── StoryAnalysis/
│   └── StoryParser/
├── Components/
│   ├── Layout/                # MainLayout, NavMenu
│   ├── Pages/                 # All page components
│   ├── Scenarios/
│   ├── Shared/                # Shared components
│   └── Templates/
├── Domain/                    # Web-specific domain models
│   ├── Models/                # ModelSettings
│   ├── RolePlay/              # RolePlaySession, RolePlayInteraction, etc.
│   ├── Scenarios/             # Scenario and related entities
│   └── Story/                 # StorySession, StoryBlock
├── data/                      # SQLite database (gitignored)
├── Program.cs                 # Application startup & DI configuration
└── wwwroot/                   # Static assets (CSS, JS, lib)
```

---

## Navigation Routes (from NavMenu.razor)

| Route | Page |
|-------|------|
| `/` | Home |
| `/counter` | Counter (demo) |
| `/weather` | Weather (demo) |
| `/templates` | Templates management |
| `/scenarios` | Scenarios list |
| `/story` | Story Mode |
| `/storyparser` | Story Parser |
| `/processing-queue` | Processing Queue |
| `/storycollections` | Story Collections |
| `/profiles` | Profiles (theme/tone/style profiles) |
| `/roleplay` | Role-Play Mode list |
| `/roleplay/create` | Create new Role-Play session |
| `/roleplay/sessions` | Role-Play Sessions list |
| `/sessions` | All Sessions |
| `/model-manager` | Model Manager |

---

## Key Enums & Types

### RolePlaySessionStatus
- `NotStarted`
- `InProgress`

### BehaviorMode
- `TakeTurns` - Turn-based with configurable thresholds

### InteractionType
- `User` - User-generated interaction
- `Npc` - NPC/character interaction
- `Custom` - Custom character interaction
- `System` - System/instruction/narrative

### PromptIntent
- `Message` - Character dialogue/message
- `Narrative` - Scene narration
- `Instruction` - User instruction to the system

### ThemeTier
- `MustHave`
- `StronglyPrefer`
- `NiceToHave`
- `Neutral`
- `Dislike`
- `HardDealBreaker`

### ContinueAsActor
- `You`
- `Npc`
- `Custom`

### SubmissionSource
- Where the submission originated from (e.g., main input, overflow continue button, plus button)

### TurnState
- `Any`
- `UserTurn`
- `NpcTurn`

### CharacterPerspectiveMode
- `FirstPersonInternalMonologue`
- `ThirdPersonExternalOnly`
- (Others - check enum for full list)

---

## Configuration (appsettings.json structure)

```json
{
  "LmStudio": {
    "BaseUrl": "http://localhost:1234/v1"
  },
  "Persistence": {
    "DatabasePath": "data/dreamgenclone.db"
  },
  "StoryParser": {
    "TimeoutSeconds": 30,
    "MaxPageCount": 50,
    "ErrorModeDefault": "FailFast",
    "SupportedDomains": []
  },
  "StoryAnalysis": {
    // Story analysis settings
  },
  "ScenarioAdaptation": {
    // Scenario adaptation settings
  }
}
```

---

## Dependencies

### Key NuGet Packages
- **Blazor**: `Microsoft.AspNetCore.Components.WebAssembly.Server`
- **Database**: `Microsoft.EntityFrameworkCore.Sqlite`
- **HTML Parsing**: `AngleSharp`
- **Logging**: `Serilog.AspNetCore`
- **JSON**: `System.Text.Json`
- **HTTP**: `Microsoft.Extensions.Http`
- **SignalR**: `Microsoft.AspNetCore.SignalR`

### External APIs
- **LLM Provider**: OpenAI-compatible endpoints (LM Studio, local models, etc.)

---

## Development Notes

### Build & Run
```bash
dotnet build
dotnet run --project DreamGenClone.Web