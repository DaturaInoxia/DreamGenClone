# B-162 — Subject placement and scale control (findings + integration plan)

**Origin.** Operator session 2026-10-13. Roughly two hours and a dozen renders went into one establishing
shot: two people at depth, one at his trailer, one walking toward the clothesline, correct relative sizes.
The operator was explicit that the *image was not the point* — the point was learning what the app must
provide so a user gets this in one or two attempts instead of twelve. This document records what was
measured and what should be built. Evidence ids are real rows in the live dev DB.

---

## 1. What was measured

### 1.1 The bound location plate's camera sets the baseline scale

A reference-conditioned render reproduces the **camera distance of the reference image**. Proven by
changing exactly one variable — the bound plate — with an identical prompt, identical 1024x1024, identical
model, same production group:

| Bound plate | Result |
|---|---|
| `Couple_Other_Front_Close` (`c54505b6`) | figures at ~60% / ~40% of frame height (`029930d4`) |
| `Couple_Other_Front` (`cb14a8e8`) | **both figures small and distant** (`3f139fa8`) |
| no plate bound at all | small figures (`2395e0e8`) |

And the "close" plate is not a photograph of a closer place — it is a **four-generation zoom derivative**,
proven from `SceneAssetImages.SourceImageId`:

```
cb14a8e8  Couple_Other_Front           1024x1024  "Wide-angle photograph of the fronts of two
                                                   trailers parked about forty feet apart"  <- wide root
  -> a046db96                           1024x686  (crop)
  -> 88b12765                           1024x686
  -> b199502e                           1248x832  "front view eye-level shot close-up"
  -> c54505b6  Couple_Other_Front_Close  1248x832  "keep setting and identity unchanged, zoom in closer"
```

### 1.2 Depth is controllable by naming a LANDMARK, not by adjectives

All reachable **from the same close plate**:

| Landmark named in the prompt | Result |
|---|---|
| "deep in the BACKGROUND at the back of the yard near the shed" | small, at the back (`2c4cbdc1`) |
| "a MIDDLE DISTANCE ... halfway between the camera and the shed" | ~40% height, mid-lawn (`0b37cdd1`) |
| "at the base of the steps of the trailer with the covered deck" | large (`029930d4`) |

The operator's own first failure (`b6de18b6`) is explained by this: it asked for "far back" while pinning
the subject to a **near** object ("by the chair on the deck on the patio"). A distance **adjective** is
weak; a distance **landmark** is strong, and naming a near landmark silently cancels the adjective.

### 1.3 Relative size between two subjects is NOT controllable in one create prompt

Three formulations, three failures, all on the same scene:

| Attempt | Formulation | Measured |
|---|---|---|
| `e5f46a0d` | "she is much further away than the man" | her ~23% vs his ~21% |
| `e1602fcf` | "... appears clearly smaller ... about two thirds of his height" | her ~23% vs his ~21% |
| `8fb075e3` | "MUCH FURTHER ... only about half as tall in the picture as he does" | her ~23% vs his ~21% |

Absolute depth is controllable (1.2). **Relative size between two named subjects is not.**

### 1.4 The model defaults to a clear sightline

| Ask | Result |
|---|---|
| "the pole stands directly in front of her" (moderate) | **not honoured** — she was moved *beside* the pole; nothing crossed her (`e5f46a0d`) |
| "she is PARTLY HIDDEN BEHIND the clothesline ... the pole crosses in front of her body" (strong, names the object) | **landed** — she is at the pole with it crossing her right side (`e1602fcf`) |
| "beyond the clothesline ... the line stands between her and the camera" (relative) | **broke** — subject displaced left, stray limb artifact at the line (`d327f0b2`) |

So occlusion is achievable but unreliable, and the default behaviour is to relocate a subject into clear
space. This is what caused the repeated lateral drift the operator noticed.

### 1.5 The EDIT path does per-subject scale where create does not

`e1602fcf` -> edit intent *"Move the woman ... so that she appears about half her current size ...
Change nothing else: keep the man at the trailer on the left exactly as he is"* -> **`c0f69aec`**:

- woman **~23% -> ~14-15%** of frame height, now **clearly smaller than the man** (the goal create could not meet)
- the clothesline pole now **crosses in front of her** (occlusion, which create only sometimes managed)
- trailers, shed, clothesline, light, colours and framing **preserved**

The editor's vision compiler turned that intent into a grounded instruction with an explicit **targets**
list ("woman walking middle of yard: middle of the image"; "man at trailer on left: left edge of the
image") and an explicit **preserve** list.

### 1.6 Phantom subjects

A deliberately sparse prompt produced a **third person** (`0b37cdd1`). Saying *"with only two people in the
picture"* fixed it and held across later renders.

---

## 2. What the app already has

1. **Editor -> Camera tab — an ORBIT / REFRAME pass, NOT a placement control.**
   **OPERATOR CORRECTION 2026-10-14, and this document previously got it wrong:** the Camera tab's
   Distance / Azimuth / Elevation exist to **rotate the image around a subject** ("Orbits the subject to a
   picked angle"), i.e. it re-shoots an existing picture from a new angle around a person or object. It
   does **not** place people at a depth in a scene, and it is not the answer to "put her in the
   background". It is driven by a multi-angle camera LoRA whose grammar
   (`<sks> front view eye-level shot medium shot`) is assembled and sent unchanged, never through the
   vision compiler. Useful for re-angling a shot; **not** a substitute for the placement controls in 3.2.
2. **Grounded edit compile** (1.5): targets by position + Changes + **Preserve** lists.
3. **Lineage view** (before/after + source record) — the iteration UX.

---

## 3. Integration recommendations, ranked

1. **(WITHDRAWN 2026-10-14 — this was my error.)** I previously recommended reusing the editor's Camera
   pass in the Composer as a framing control. The operator corrected it: that pass **orbits the camera
   around a subject** in an existing image; it does not place people at depth. The Composer's real gap is
   not a camera control but **placement and scale**, which is recommendation 2 (landmarks) and 4 (the
   per-subject adjust), below.
2. **A landmark picker driven by `LocationImageAnalysis` anchors (B-160).** Per subject: pick an anchor
   ("the clothesline pole", "the steps of the right trailer", "the shed") and a relation
   (**at / beside / behind / in front of**). Store it as structured data and emit it into the prompt from
   one owner. This is the single highest-value change: it replaces "hope the prose lands" with a placement
   the operator chose and the render can be checked against. **Measured to be the actual mechanism that
   controls depth** (1.2).
3. **Expose the plate's implied camera distance and its zoom lineage** in the plate picker — show that a
   plate is a zoom derivative of another plate — and **warn when a close/zoomed plate is bound to a moment
   the blocking says is an establishing shot**. `LocationImageAnalysis` already reads the anchors' depth
   positions, so the plate's implied scale is derivable from data already stored.
4. **A per-subject "Adjust" action on a finished composition** that routes to the edit path with the
   subject as target, a near/far/smaller/larger axis, and an automatic preserve list for the other
   subjects and the setting. Measured to work where create cannot (1.5). This is what makes "quickly get
   the result they want" achievable without a full re-roll.
5. **Warn on the near/far mismatch**: if the blocking (or the operator's framing) wants distance while the
   prompt names a near landmark, say so **before** spending the render. This is exactly the operator's
   original failure and it is detectable from data already present.
6. **Phantom-subject guard**: whenever the cast is known, emit the count ("exactly N people") and check the
   prompt for a second mention of the same person.
7. **Per-subject clause LOCKING for iteration.** What made the successful renders possible was keeping one
   subject's clause **verbatim** while changing the other. The app should let the operator lock a clause and
   re-roll only the rest — today that is a manual copy-paste discipline.

### 3.1 Where the control belongs

The three things the operator cares about map onto three different mechanisms, and the app should say which
one it is using rather than presenting them as one knob:

| What the operator wants | Mechanism | Status |
|---|---|---|
| a subject nearer or further within the frame | **landmark + depth clause** (1.2) | prose only; needs 3.2 |
| a subject smaller or larger relative to another | **edit pass** on a finished composition (1.5) | exists in the **editor**; needs 3.4 |
| re-angle an existing shot around a subject | editor Camera pass (orbit) | exists; **not** a placement tool |

---

## 4. The literal prompts that worked (operator asked "how did you prompt to get this")

The working renders were all produced by **replacing the compiled prompt** with hand-written prose. The
compiled prompt cannot do this — see 4.4 for exactly why.

### 4.1 Small in the background (`2c4cbdc1`)

```
A photorealistic photograph of the trailer park yard on a bright morning, in muted neutral tones. Both
figures are FAR AWAY from the camera, deep in the BACKGROUND at the back of the yard near the shed, tiny in
the frame. At the back on the right a woman walks mid-step carrying a woven laundry basket on her hip. At
the back on the left a man stands still holding a coffee mug. The empty lawn and the clothesline in the
foreground fill most of the picture, and neither figure is more than a small fraction of the frame height.
```

### 4.2 Mid-distance (`0b37cdd1`)

```
A photorealistic photograph of the trailer park yard on a bright morning, in muted neutral tones. Two
figures are at a MIDDLE DISTANCE from the camera, standing in the middle of the yard about halfway between
the camera and the shed at the back, neither close to the camera nor far away. On the right of the middle of
the lawn a woman walks mid-step carrying a woven laundry basket on her hip. A little to her left a man
stands still holding a coffee mug. The clothesline stands at the same middle distance. The near lawn fills
the lower part of the picture, and the shed, the trees and the two trailers sit behind the figures. Each
figure is about a third of the frame height.
```

### 4.3 Behind an object — occlusion (`e1602fcf`, the one that worked)

```
...with only two people in the picture. On the left, standing in the open sliding doorway of his trailer, a
man stands still holding a coffee mug, at the left edge of the frame. In the middle of the frame, a few
steps beyond the clothesline, a woman walks mid-step down the yard toward the camera, a woven laundry basket
carried on her hip, facing the camera. She is PARTLY HIDDEN BEHIND the clothesline: its centre pole and one
of its arms cross in front of her body, so her legs and the lower part of the basket are occluded by the
pole, and the line crosses her torso. The pole is nearer to the camera than she is and partially blocks her.
She is further away than the man and reads smaller than him in the frame. The near lawn is empty and fills
the lower part of the picture.
```

**The four things that make occlusion work, all four required:**

1. Name the object exactly ("its centre pole and one of its arms").
2. Give the **part-to-part** relationship ("the line crosses her torso").
3. Name the **occluded body parts** ("her legs and the lower part of the basket").
4. State the **depth order explicitly** ("the pole is nearer to the camera than she is").

The failed variants, for contrast:
- *"the clothesline pole stands directly in front of her, between her and the camera"* -> the model
  **silently moved her beside the pole** and crossed nothing (`e5f46a0d`).
- *"beyond the clothesline ... the line stands between her and the camera"* -> subject displaced left plus a
  **stray limb artifact** at the line (`d327f0b2`).

### 4.4 Why the studio's compiled prompt cannot do any of this

The compiled prompt for the same moment (`442ec3d2`, generated by **Generate Prompt**) reads, in its own
words:

- *"one woman in the **center foreground** and one man in the **left background**"* — labels a place, states
  **no distance and no scale**;
- *"**The composition holds the woman large in the center foreground** and the man smaller at the left
  edge"* — it **actively asks for the woman to be large**, which is the opposite of an establishing shot;
- *"The camera sits at eye level in a **medium shot**"* — a medium shot fills the frame with the subject;
- the man is pinned to *"stands at an open slider"* — a **near object**, and per 1.2 a near landmark
  silently overrides any distance adjective.

So the generated prompt is not "missing a phrase"; three of its own sentences pull against a distant
establishing shot. **This is the product gap**: the operator should not have to overwrite the compiled
prompt with hand-written prose to get a wide shot. The controls in 3.2/3.4 have to produce the distance
statement and stop the compiler asserting "large".

### 4.5 The edit that fixed relative size (`c0f69aec`)

Typed into the editor's **Edit intent**:

```
Move the woman walking in the middle of the yard further away from the camera so that she appears about
half her current size in the picture, smaller and deeper into the yard. Change nothing else: keep the man at
the trailer on the left exactly as he is, and keep both trailers, the shed, the clothesline, the light, the
colours and the overall framing exactly as they are.
```

The editor compiled it to:

```
The woman walking in the middle of the yard should appear smaller and further away from the camera, making
her about half her current size. The man standing near the trailer on the left should remain exactly as he
is. Both trailers, the shed, clothesline, light, colors, and overall framing must be kept unchanged.
```

...and it **worked** where three create attempts failed. The compiler's value is that it emits an explicit
**targets** list ("woman walking middle of yard: middle of the image") and a **preserve** list — the
"change nothing else" half is what holds the other subject and the setting still.

---

## 5. Interim rules for anyone writing these prompts today

1. **Landmark, not adjective.** Name a place at the distance you want.
2. **Absolute, not relative.** "at the back of the yard near the shed" beats "beyond the clothesline".
3. **One clause per figure**, each with its own landmark — never one shared "they are both far back".
4. **Declare the shared distance once** ("both at the same distance from the camera").
5. **Say the count** ("only two people in the picture").
6. **Give the foreground to the environment** ("the near lawn is empty and fills the lower part").
7. **Keep the clause that already worked verbatim** when adjusting the other figure.
8. **Do not chase relative size in a create prompt** — measured three times as not working; use an edit.

---

## 6. Prompt-integrity defects — why "each render is a little off" (2026-10-16)

**Operator report:** three images from the same prompt and references on group `dfda210c`
(`3d321d7d`, `4df45ce9`, `1b1ef120`); face wrong in one, clothing wrong in another, direction and
location not landing, the clothesline object off — *"how each one is just a little off but enough that I
need to generate another."*

**What was measured.** The three were queued as a batch. Their `PromptSnapshot` hashes are **identical**,
their `AppliedReferenceBindingsJson` hashes are **identical**, and the sent prompt recorded in each
`NativeReferenceRenderSubmitted` debug event is **byte-identical**. Settings are identical and
`SettingsJson.Seed` is **`null`**, so every render draws a random seed. Pairwise pixel difference between
the finished images is a mean absolute channel difference of **31-34 / 255** — genuinely different draws,
not near-duplicates.

So the inputs are fixed and the variance is the sampler's. **But the reason the variance is damaging is
that the prompt asks for contradictory things, and each draw resolves the contradictions differently.**
That is the finding: it is not bad luck, it is unresolved conflict being settled randomly.

**The four contradictions in that prompt, all visible in the exact text the model received:**

1. **A malformed tag — `<image1` with no closing `>`**: *"her head `<image1` is turned to the left"*.
   The face reference is therefore never reliably bound to the head sentence, which is the operator's
   "the face is not correct". **`FinalImagePromptPlanBuilder.PromptTagIssues` cannot see this**: its
   pattern is `<image(\d+)>`, which requires the closing bracket, so a broken tag produces **no warning at
   all** and the render proceeds. A silently ignored tag is exactly the failure this guard exists to catch.
2. **The LOCATION tag used in the sentence about the PERSON**: *"one woman mid-step in a sunlit backyard
   `<image4>`"* — while the same prompt's clause states `<image4>` "is the LOCATION reference: this image
   IS the room ... do not replace it with the setting of any other reference image". One sentence asks the
   room to supply a woman; the other forbids it. The sampler picks a side per draw.
3. **Clothing described in words AND supplied by a reference, with the words over-claiming**: *"She is
   wearing cut off jean shorts and a tank top and is exactly what `<image3>` supplies"* — but the bound
   wardrobe reference is **"Jean Shorts"** only, so the sentence claims the whole outfit (shorts **and**
   tank) comes from a reference that holds half of it. Meanwhile the clause asserts the opposite of the
   prose: *"They are not described in words anywhere here"*. That is the drift the app already has a rule
   and a mechanism for (`ReferenceBindingPromptRemoval`) — a hand-written prompt bypasses both.
4. **Three different depths for one person.** The placement override says p0 = **RIGHT / BACKGROUND**
   (rendering as "the person on the RIGHT in the background, **farthest from the camera**"); the operator's
   prose says *"placed on the right side **in between the deck on the right and the clothesline in the
   middle**"* (midground, not farthest); and the camera clause says *"a wide establishing shot ... someone
   further away is smaller"*. The subject landed mid-frame and large in every draw, which is the sampler
   breaking the tie rather than honouring any one of the three.

**Consequence for the operator's workflow:** re-rolling re-rolls the *conflict resolution*, not the
quality. Making the prompt self-consistent is worth more than more candidates, because a self-consistent
prompt converges instead of scattering.

### 6.1 App gaps this exposes, ranked

1. **Malformed-tag detection.** Catch `<image1` (no `>`), `<image>`, `<Image1>`, `<image 1>`, and an
   unterminated tag at end of text. Today all of these are invisible. Cheapest, highest-value fix in this
   list.
2. **Tag-role vs sentence-subject check.** Flag a tag used in a sentence whose subject contradicts its
   role — specifically a **Location** tag inside a sentence that describes a person's appearance or
   placement. The role of every tag is already known to the plan owner, so this is a comparison, not a guess.
3. **Prose-vs-reference duplication check, applied to a HAND-WRITTEN prompt.** If a Face/Body/Wardrobe
   reference is bound and the operator's prose describes that element, say so. The vocabulary and the rule
   already exist (`ReferenceBindingPromptRemoval`, "do not describe what a reference supplies"); they are
   only applied to the compiled brief, so overwriting the prompt silently opts out of them.
4. **Placement coherence check.** The blocking override, the operator's prose and the camera clause all
   state a depth. When they disagree, say which one is which rather than letting the sampler choose.
5. **A tag legend and tag-insert buttons beside the prompt box.** The `<imageN>` numbering is assigned by
   **send order** (pack images first), so it cannot be guessed safely: here 1=Face, 2=Body, 3=Wardrobe,
   4=Location. Showing that mapping live — and letting the operator click to insert a correct tag —
   removes the class of error in defect 1 entirely.
6. **Seed control** ("random" vs a fixed seed, and "hold this seed"). Once the prompt is self-consistent,
   the remaining per-draw variance can only be studied against a fixed seed; without one, every re-roll
   moves both the prompt-interpretation and the noise, so no single change can be attributed.
7. **Prompt-integrity lint before spending a render** — run 1-4 as one pass and show the findings next to
   the reference map the preview already displays.

**What the operator can do today:** read the reference map in the prompt preview while typing (it lists
each `<imageN>` and what it supplies) and make the prose agree with it; never describe in words what a
reference supplies; and keep one subject's clause fixed while varying the other.

### 6.2 Two subjects cannot share a frame position — a representation limit, not a picture limit

Operator question, 2026-10-16: *"also why cant the be the same?"* — asked of the refusal

> The placement override puts p0 and p1 in the same frame position (Left/Background). Give them different
> positions: one place cannot belong to two people.

**Is two people in one place impossible?** No. Two figures standing together on the left of a yard shot is
an ordinary photograph, and for a couple it is the *expected* framing. The refusal is about the app's
vocabulary, not about the world.

**Where the refusal lives.** `SceneBlockingResolver.ApplyOverride` groups the moved subjects by `Position`
and throws on any group larger than one. Its comment justifies the throw by analogy — *"for the same reason
the resolver refuses it."*

**The analogy does not hold.** `SceneBlockingResolver.Resolve` never refuses a collision, because it cannot
produce one: it seats subjects one per rung of the staging profile's **placement ladder**, so N subjects
take N distinct rungs by construction. The resolver's real invariant is "N rungs for N subjects" — a seating
rule. It is not evidence that one place holds one person, and the override guard inherited a rule that does
not mean what it was borrowed for.

**What genuinely breaks on a collision.** `RoleText` is a pure function of `(horizontal, depth)` —
`DescribeRole` — so two subjects in one position get a **byte-identical** string:
`"the person on the LEFT in the background, farthest from the camera"`. That string is the only thing each
reference clause uses to identify its subject (`ReferenceRoleClauses`: *"… is the person on the LEFT in the
background, farthest from the camera — their face, features, hair and skin tone come from this image."*).
Two identical sentences on two different images leave the model no way to tell which image is which person,
and actively invite it to **merge** them into one figure — the limb-fusion / duplicate-subject defect class
recorded in §6.

**Decision (operator, 2026-10-16): keep the refusal.** Recorded here so the option is not re-litigated as a
bug. If it is ever revisited, the shape is: allow a shared position and append a *configured* distinguishing
relation to both role texts (for example `"standing just behind p0"`), never a hardcoded phrase — and keep
the refusal for the case that still cannot be described (a shared position with no relation available),
failing fast and naming the missing setting.

---

## 7. Non-goals
- Do not "fix" 1.3 by prompt wording. Three measured failures say the route is an edit or a control, not prose.
- Do not rely on occlusion in create; it is a coin flip that can also produce an artifact.
- Do not add a second framing UI in the Composer alongside the editor's Camera pass — one control, reused.
