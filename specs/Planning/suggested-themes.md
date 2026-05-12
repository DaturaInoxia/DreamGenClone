# DreamGenClone — Suggested Themes

Theme ideas for the RP engine, organized by category. Each suggestion includes a proposed ID, label, weight, keywords, stat affinities, and rationale. Themes marked ✅ have been implemented.

---

## Current Theme Inventory

### Built-In Themes (ThemeCatalogService)

| ID | Label | Category | Weight | Status |
|---|---|---|---|---|
| `intimacy` | Intimacy | Emotional | 3 | ✅ Built-in |
| `trust-building` | Trust Building | Emotional | 3 | ✅ Built-in |
| `power-dynamics` | Power Dynamics | Power | 4 | ✅ Built-in |
| `jealousy-triangle` | Jealousy Triangle | Emotional | 4 | ✅ Built-in |
| `forbidden-risk` | Forbidden Risk | Power | 4 | ✅ Built-in |
| `confession` | Confession | Emotional | 3 | ✅ Built-in |
| `voyeurism` | Voyeurism | Power | 4 | ✅ Built-in |
| `infidelity` | Infidelity | Power | 4 | ✅ Built-in |
| `humiliation` | Humiliation | Power | 4 | ✅ Built-in |
| `dominance` | Dominance | Power | 4 | ✅ Built-in |

### Spec-Defined Themes (specs/v2/ThemeDefinitaions/)

| ID | Label | Category | Weight | Status |
|---|---|---|---|---|
| `infidelity-public-facade` | Infidelity with Public Facade | Power | 4 | ✅ In DB |
| `infidelity-public-facade-discovery` | Infidelity with Public Discovery | Power | 4 | ✅ In DB |
| `infidelity-brief-disappearance` | Infidelity with Brief Disappearance | Power | 3 | ✅ In DB |
| `threesome-spontaneous-exclusion` | Threesome with Spontaneous Exclusion | Power | 4 | ✅ In DB |
| `seduction` | Seduction | Taboo | 3 | ✅ In DB |

### Category Gaps

| Category | Current Count | Notes |
|---|---|---|
| **Emotional** | 3 built-in | Underrepresented vs Power |
| **Power** | 7 built-in + 4 spec | Dominant category |
| **Taboo** | 1 (seduction) | Newly opened, needs more |
| **Relational** | 0 | Completely empty |

---

## Suggested Themes

### Emotional Category (currently underrepresented)

| # | ID | Label | Weight | Keywords | Stat Affinities | Rationale |
|---|---|---|---|---|---|---|
| 1 | `first-time-awakening` | First Time / Awakening | 3 | curious, first, never before, nervous, explore, discover, innocent, new experience | Desire +2, Tension +2, Restraint -1, Connection +1 | Sexual or emotional awakening. A character experiencing something for the first time. High narrative tension from uncertainty and vulnerability. |
| 2 | `reunion` | Reunion | 3 | reconnect, years later, remember, used to, back then, old flame, never forgot | Connection +3, Tension +1, Desire +1 | Reconnecting with someone from the past. The weight of history and unresolved feelings. Natural build-up tension. |
| 3 | `reconciliation` | Reconciliation | 3 | forgive, make amends, second chance, work it out, sorry, make it right, try again | Connection +4, Tension -2, Restraint +1 | Repairing a damaged relationship. The emotional journey back toward trust. Counterpoint to infidelity/betrayal themes. |
| 4 | `growing-apart` | Growing Apart | 3 | distant, drift, don't talk, silence, separate, alone together, nothing left | Connection -3, Tension +2, Desire -1 | The quiet erosion of a relationship. Emotional distance without explicit betrayal. Sets up vulnerability for other themes. |
| 5 | `regret-guilt` | Regret & Guilt | 3 | regret, guilty, shouldn't have, mistake, what did I do, can't take back, ashamed | Tension +3, Connection -1, SelfRespect -2, Restraint +1 | Post-transgression emotional processing. The aftermath of crossing a line. Pairs naturally with infidelity and forbidden-risk. |

### Relational Category (currently empty)

| # | ID | Label | Weight | Keywords | Stat Affinities | Rationale |
|---|---|---|---|---|---|---|
| 6 | `competition-rivalry` | Competition & Rivalry | 4 | compete, rival, better than, win, outdo, prove, challenge, contest | Tension +3, Dominance +1, Connection -1 | Two characters actively competing for the same person or outcome. Action-oriented tension, distinct from jealousy-triangle (which is the *feeling* of jealousy — this is the *behavior* of competing). |
| 7 | `negotiation-consent` | Negotiation & Consent | 2 | agree, discuss, boundaries, okay with, comfortable, decide together, rules | Connection +2, Restraint +2, Tension -2 | Explicit discussion and agreement about what will happen. Creates a framework of safety that makes escalation more impactful. Low weight because it's usually a *setup* phase, not a driving theme. |
| 8 | `arranged-obligation` | Arranged / Obligation | 3 | arranged, expected, supposed to, duty, obligation, no choice, tradition | Tension +2, Restraint +2, Connection -1, Dominance -1 | A relationship or encounter driven by external obligation rather than desire. The tension between duty and personal feeling. |
| 9 | `mentor-student` | Mentor & Student | 3 | teach, learn, guide, show you, lesson, training, practice, instruction | Dominance +1, Connection +2, Desire +1 | A power dynamic rooted in knowledge/experience asymmetry. The authority of the mentor creates natural escalation pathways. |

### Taboo Category (needs expansion)

| # | ID | Label | Weight | Keywords | Stat Affinities | Rationale |
|---|---|---|---|---|---|---|
| 10 | `corruption-moral-decline` | Corruption / Moral Decline | 4 | cross the line, never thought I would, just this once, one more step, compromising, sliding, rationalize | Restraint -3, Desire +2, Tension +2, SelfRespect -2 | A character gradually crossing lines they previously wouldn't. Step-by-step moral erosion. The journey matters more than the destination. |
| 11 | `blackmail-coercion` | Blackmail & Coercion | 4 | blackmail, leverage, threat, force, no choice, have to, or else, compromise, evidence | Tension +4, Dominance -2, Restraint +1, Connection -3 | Leverage-based power dynamics. Distinct from dominance (which is about *claimed* power) — this is about *extorted* compliance. |
| 12 | `exhibitionism` | Exhibitionism | 4 | show off, display, watch me, look at me, see this, perform, show you, enjoy the view | Desire +2, Dominance +1, Restraint -2, Connection +1 | The performer side of being watched. Distinct from voyeurism (the *observer* side). The thrill comes from being seen, not from seeing. |
| 13 | `denial-edging` | Denial & Edging | 4 | not yet, wait, hold on, almost, stop, don't finish, beg, earn it, not allowed | Restraint +3, Tension +3, Desire +2, Dominance +1 | Prolonged restraint as a deliberate power tool. The controller decides when/if release happens. High tension from sustained near-completion. |
| 14 | `revenge` | Revenge | 4 | revenge, get even, payback, make you pay, hurt you back, spite, retaliate | Tension +3, Dominance +2, Connection -3, Desire -1 | Acting out of spite or retaliation. Emotionally driven rather than desire-driven. Creates volatile, unpredictable dynamics. |

### Power Category (expanding existing)

| # | ID | Label | Weight | Keywords | Stat Affinities | Rationale |
|---|---|---|---|---|---|---|
| 15 | `hotwife-cuckold` | Hotwife / Cuckold | 4 | hotwife, cuckold, bull, share, watch her, with him, while I watch, she deserves, his turn | Desire +2, Dominance -2, Tension +2, Connection +1 (hotwife) / -2 (cuckold) | Referenced extensively in `Husband Awareness.md` spec but has no theme. The husband's complicity ranges from enthusiastic (hotwife) to humiliated (cuckold). This is a major narrative pattern that deserves its own theme. |
| 16 | `secret-voyeur-discovery` | Secret Voyeur Discovery | 4 | spying, hidden camera, watching secretly, caught watching, I saw you looking, peeking, through the door, keyhole | Desire +2, Tension +3, Restraint +1, Connection -1 | Referenced in `infidelity-public-discovery.md` spec ("that's a different theme: secret-voyeur-discovery"). The act of secretly watching and the consequences of being discovered watching. Distinct from voyeurism (which is about the watching itself) — this adds the discovery/consequence layer. |

---

## Priority Ranking

Recommended implementation order based on narrative coverage and how often these patterns appear in existing specs:

| Priority | ID | Label | Why |
|---|---|---|---|
| ~~1~~ | ~~`seduction`~~ | ~~Seduction~~ | ~~Most fundamental missing narrative arc~~ ✅ Done |
| 2 | `hotwife-cuckold` | Hotwife / Cuckold | Already referenced in specs, major gap |
| 3 | `corruption-moral-decline` | Corruption / Moral Decline | Fills the Taboo category, strong narrative engine |
| 4 | `exhibitionism` | Exhibitionism | Natural pair with existing voyeurism |
| 5 | `first-time-awakening` | First Time / Awakening | Fills Emotional gap, high vulnerability/tension |
| 6 | `denial-edging` | Denial & Edging | Unique tension mechanic not covered by any existing theme |
| 7 | `blackmail-coercion` | Blackmail & Coercion | Distinct power dynamic from dominance |
| 8 | `competition-rivalry` | Competition & Rivalry | Action-oriented tension, distinct from jealousy |
| 9 | `reunion` | Reunion | Strong emotional setup theme |
| 10 | `secret-voyeur-discovery` | Secret Voyeur Discovery | Already named in specs as a separate theme |

Lower priority (valuable but more niche or better as setup/aftermath phases within other themes):

| Priority | ID | Label | Notes |
|---|---|---|---|
| 11 | `regret-guilt` | Regret & Guilt | Pairs with infidelity/forbidden-risk as aftermath |
| 12 | `reconciliation` | Reconciliation | Counterpoint to betrayal themes |
| 13 | `growing-apart` | Growing Apart | Setup theme that enables others |
| 14 | `negotiation-consent` | Negotiation & Consent | Usually a setup phase, not a driving theme |
| 15 | `arranged-obligation` | Arranged / Obligation | Niche scenario type |
| 16 | `mentor-student` | Mentor & Student | Overlaps with power-dynamics |
| 17 | `revenge` | Revenge | Emotionally volatile, niche |
