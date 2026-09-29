# 078 — A face pose's controls do nothing in the pose editor

## Report

Operator, 2026-09-28, verbatim: **"body poses to move face poses do nothing"**.

Opening a library pose that carries a face and no usable body ("Open in editor") and pressing the pose editor's
rotation arrows changed nothing at all. The same arrows on a body pose worked. Earlier in the same session the
operator reported the same symptom more broadly — *"none of the buttons do anything"* — which turned out to be a
**separate** cause (see "Not this defect" below).

## Analysis

Traced in the panel, the proxy and the save path.

### The turn

`PoseAuthorPanel.CurrentPose()` routes a head pose to the face path:

```csharp
if (_storedPose is { } headPose && PoseFaceProxy.IsHeadPose(headPose))
    raw = PoseFaceProxy.Project(headPose, _head, StudioOptions.Value);
```

and the signature is decisive:

```csharp
public static PosePerson Project(PosePerson pose, PoseHeadRotation head, PoseStudioOptions settings)
```

**There is no `PoseView` parameter.** The proxy reads the head and cannot see a view at all. Meanwhile the Body step —
which is the tab the panel **opens on** (`_activeTab = "body"`) — wrote only `_view`:

```csharp
private async Task RotateAsync(PoseRotationAxis axis, int steps)
{
    DiscardDrags();
    _view = PoseProjection.Step(_view, axis, steps, StudioOptions.Value);   // ignored by the face path
    ...
}
```

So on a face pose every control in the default step was *mathematically incapable* of changing the picture. Only the
Head step, which writes `_head`, responded. The operator was pressing the arrows in front of them.

The readout made it worse rather than better: it printed `_view`, so a face pose showed a permanent
`0° yaw · 0° pitch · 0° roll` while the head turned underneath it — a working control reading as a dead one.

### The preview

`PoseHeadControls.RefreshPreviewAsync` rendered `LibraryService.ProjectAuthoredPose(BodyView, Head)` — **the rig's
mannequin head** — for every pose. On a face pose the two steps therefore showed two different heads turning, one of
them a mannequin the operator had never loaded.

### The save

```csharp
Keypoints: _rotations is null && _draggedPose is null ? null : PoseOnScreen()
```

A face pose is loaded (`_storedPose` non-null) and has **no rig rotations** (`_rotations` null, because `Loaded` is null
for a head pose). So the gate produced `Keypoints: null`, and `SaveAuthoredPoseAsync` took its rig branch
(`ProjectAuthoredPose(request.View, request.Head)`) — writing a standing **mannequin** with the head angle on it, in
place of the face that had just been turned. Even saving an *untouched* face pose did this.

### Root cause, one sentence

The head path was bolted on with **turn + preview** only; the other two paths a body pose has — **named views** and
**save** — stayed on the rig, and the step's own controls were left writing a value the head path never reads.

### Not this defect (same session, different cause)

*"none of the buttons do anything"* was **the Blazor circuit**, not pose code. The reconnect overlay is
`#components-reconnect-modal` with the dialog inside a **shadow root**, so light-DOM queries see an empty div and
`document.elementFromPoint` reports a zero-height DIV over the button. Read `host.shadowRoot` to see it; it said
"Failed to rejoin. Please retry or reload the page." The app is up (HTTP 200) and the pose code is fine — the tab's
circuit had died when the app was restarted underneath it. Reload fixes it.

## Plan

1. `PoseAuthorPanel` — ONE decision (`TurningTheHead`), and every Body-step control routed through it: `RotateAsync`
   steps the **head**, `SetViewAsync` maps the named view onto the head, the readout names the angle that turned, and
   `IsCurrent` asks the thing that is turning (which also keeps `PoseEdited()` honest).
2. `PoseFaceProxy.AsHeadRotation(PoseView)` — the ONE seam that states how a named body view means the same thing for
   a head (Front straight ahead, a profile 90° off).
3. `PoseHeadControls` — a `Face` parameter, so the close-up shows the pose being edited and is framed on the FACE's own
   points (one frame, computed from the head at rest, reused across angles).
4. Save — gate on "is a pose loaded" rather than "does it have rig rotations", and pass `Origin: "head-turned"` so the
   keywords and provenance do not default to calling a turned face "dragged".

## Resolution

| File | Change |
|---|---|
| `PoseFaceProxy.cs` | added `AsHeadRotation(PoseView)` |
| `PoseAuthorPanel.razor` | `TurningTheHead`, `TurnYaw/TurnPitch/TurnRoll`, routed `RotateAsync` + `SetViewAsync`, head-aware `IsCurrent`, honest readout, conditional notice copy, head-aware Save gate + `Origin`, hands the face to the Head step |
| `PoseHeadControls.razor` | `Face` parameter; `PoseAt`/`HeadAtRest`; frame measured on the face's own points and reused; copy states which is shown |
| `PoseFaceProxyTests.cs` | `AsHeadRotation_KeepsTheAnglesItIsGiven`, `EveryBodyStepTarget_ChangesAFacePose`, `TheBodyStepAndTheHeadStep_AgreeOnTheNamesTheyShare` |
| `PoseAuthorPanelHeadRoutingContractTests.cs` | new — pins the wiring, which no behavioural test can see |

No change to the body path: `TurnYaw` reduces to `_view.YawDegrees` when `TurningTheHead` is false, `Face` is null, and
the Save gate is equivalent for a loaded body pose (it always had rotations).

**Build**: 0 errors. **Pose suites**: 250 passed / 2 failed. Both failures are pre-existing and not from this work —
`PoseTestCharacterConditioningContractTests` asserts on `PoseLibraryPage.razor`, which a concurrent agent's
character-conditioning feature has not yet implemented (`ListPackOwnersAsync`, `IdentityService.ReadAssetBytesAsync`).

**Not yet verified in the running app**: the app must be restarted to pick up the change, and the operator starts it.

## Validated

- [ ] pending — awaiting the operator restarting the app and confirming the face pose turns.
