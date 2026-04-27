# DreamGenClone — Backlog & Enhancement Ideas

Items here are high-level ideas.  Each item must go through **Design → Plan → Implement** before work begins.

---

## States

| State | Meaning |
|---|---|
| `new` | Idea captured, no design work started |
| `designed` | Design document or notes written, approach agreed |
| `planned` | Tasks broken down, effort estimated, ready to schedule |
| `implemented` | Code complete, awaiting integration test / review |
| `debugging` | Implemented but known defects being worked |
| `done` | Functionally complete and stable in dev |
| `done done` | Verified in production / end-to-end validated, closed |

---

## Backlog

| # | Title | State | Notes |
|---|---|---|---|
| B-001 | Fix and enhance the Question/Decision prompt | `new` | Decision prompts are not generating well-formed or contextually appropriate questions; needs redesign and testing |
| B-002 | Fix locations — scene location is incorrect or missing | `new` | Location injection into prompts is broken or not resolving correctly for active scenario context |
| B-003 | Test multiple themes active simultaneously | `new` | Validate selection, scoring, and narrative framing when multiple themes are competing or co-active |
| B-004 | Test completed single theme run multiple times | `new` | Ensure repeat-run penalty, score decay, and cooldown logic work correctly across multiple completions of the same theme |
| B-005 | Performance pass | `new` | Profile startup, per-interaction latency, and DB query cost; identify and fix top bottlenecks |
| B-006 | Ensure sex scenes are more detailed and explicit | `new` | Prompt framing and continuation guidance for climax-phase scenes needs to direct more explicit and descriptive output |
| B-007 | BUG: Characters mixing dialogue — wrong actor speaks wrong lines | `new` | The other-man character is given wife dialogue and vice versa; root cause likely in actor resolution or prompt assembly |
| B-008 | Change RP session start — allow single theme or theme profile override | `new` | On session create, user can pick a single RPTheme or a Theme Profile that overrides the scenario's default for that session |
| B-009 | UI cleanup — dashboard with recent activity | `new` | Replace current sessions list with a dashboard showing recent sessions, quick-resume, and activity summary |
| B-010 | Keep scroll position when new responses are added | `new` | Currently the page jumps; user should be able to read earlier content while new content loads below |
| B-011 | BUG: Reset phase does not restore stats to baseline-adjusted values | `new` | Stat pull toward baseline on Reset is not applying as designed; actual values after Reset do not match expected decay schedule |
| B-012 | HP2 Issue 3: Diagnostics readability — remaining tests and HP2 manual pass | `new` | Slice A code done; still needs tests for reason-translation and profile-name fallbacks, plus HP2 manual spot-check validation |
| B-013 | HP2 Issue 5: Writing style profile model simplification decision | `new` | Evaluate whether profile should be reduced to Name/Description/Example/RuleOfThumb only; decide and implement |
| B-014 | HP2 Issue 6: Narrative prompt intensity/style composition — validation and fix | `new` | Prompt must clearly reflect atmospheric intensity + selected writing-style behavior; needs concrete before/after captures and fix |
| B-015 | HP2 Issue 8: Stat keyword categories — complete CRUD UI and design pass | `new` | DB schema and seeding done; full design pass (schema review, migration strategy) and CRUD profile UI still outstanding |
| B-016 | Phase Ladder visualization | `new` | Show all narrative phases in a ladder/flow view with current-phase highlight and progress-to-next indicator; defined in HP2 enhancements backlog |
| B-017 | Question subsystem — on-demand invocation control | `new` | Allow user-triggered adaptive Q&A generation per character at any time via UI; trigger scope (per-character / current-speaker / both) to be decided |
| B-018 | Phase-aware question steering policy | `new` | Make adaptive Q&A generation phase-sensitive; BuildUp should favour theme/context steering over stat-pressure; define phase weighting matrix |
| B-019 | Universal direct-question + scene-location trigger — residual hardening | `new` | Slice 1–5 implemented; remaining: end-to-end validation, cadence-bypass edge cases, and HP2 manual spot-check confirmation |
