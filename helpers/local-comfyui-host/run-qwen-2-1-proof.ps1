<#
.SYNOPSIS
  Run the Qwen-Image-2.1 qualification cells against the LOCAL ComfyUI host.

.DESCRIPTION
  Runs from the dev box. Builds the official Comfy-Org Qwen-Image-2.1 graph in API format
  (UNETLoader -> [QwenImage21Cache] -> KSampler + TextEncodeQwenImage21 + VAELoader) and submits
  it over the ComfyUI HTTP API, mirroring the published templates:

    t2i    : github.com/Comfy-Org/workflow_templates/.../image_qwen_image_2_1_t2i.json
    image  : github.com/Comfy-Org/workflow_templates/.../image_qwen_image_2_1_image_edit.json

  Cells
    t2i    1024x1024 text-to-image portrait (generation path).
    rgba   transparent-background cutout using the documented RGBA prompt wrapper.
    edit1  bedroom-a harmonized base + Becky's approved pack face (1 reference).
    edit2  the same base + Becky AND Dean (2 references in ONE pass) - the B-126 case that
           makes Qwen-Image-Edit-2511 tile a third face into the frame.

  Reference inputs: `TextEncodeQwenImage21` declares ONE autogrow input whose sub-inputs are
  declared as `images.image_1`..`images.image_16` (outer id + template name, expanded in
  comfy_api/latest/_io.py; /object_info shows only the autogrow placeholder). ComfyUI re-nests
  them into an `images` dict before calling execute(). Flat `image_1` kwargs raise a TypeError at
  execute, and a hand-built `images` dict is silently IGNORED - both leave the encoder with no
  references and no error. The edit cells are therefore validated by output size: the encoder's
  latent follows `image_1`, so a square 1024x1024 output on the 1216x832 target proves the
  reference set was dropped.
  Every face reference is SHA-256 verified against the approved-pack binding before upload.

.PARAMETER Cells
  Subset of t2i, rgba, edit1, edit2. Default: all four.

.PARAMETER ComfyUiUrl
  ComfyUI base URL. Default http://192.168.0.11:8188 (WOOD-GAME-MAIN LAN).

.PARAMETER OutRoot
  Git-ignored output root. Default artifacts/tmp/qwen-2-1.

.PARAMETER Seed
  Fixed seed for every cell. Default 20260922.

.PARAMETER Steps
  Sampler steps. Default 25 (the template's starting value; the 2.1 official pipeline uses 40-50).

.PARAMETER Resolution
  TextEncodeQwenImage21 `resolution` (total pixel budget, not width/height). Default 1024.

.EXAMPLE
  powershell -ExecutionPolicy RemoteSigned -File helpers/local-comfyui-host/run-qwen-2-1-proof.ps1 -Cells t2i,rgba
#>
[CmdletBinding()]
param(
    [string[]]$Cells = @('t2i', 'rgba', 'edit1', 'edit2'),
    [string]$ComfyUiUrl = 'http://192.168.0.11:8188',
    [string]$OutRoot = 'artifacts/tmp/qwen-2-1',
    [int]$Seed = 20260922,
    [int]$Steps = 25,
    [int]$Resolution = 1024,
    [int]$TimeoutSec = 2400,
    [string]$UnetName = 'qwen_image_2.1_int8_convrot.safetensors',
    [string]$ClipName = 'qwen3vl_8b_int8_convrot.safetensors',
    [string]$VaeName = 'qwen_image_2.1_vae_bf16.safetensors',
    [string]$TargetImage = 'specs/image-generator-tests/dual-base-location/runs/dual-location-local-fast/06-harmonized-bedroom-a/img-local-bedroom-a_0.png',
    [string]$IdentityRoot = 'DreamGenClone.Web/data/scene-images'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repoRoot
$base = $ComfyUiUrl.TrimEnd('/')

function Fail($msg) { throw $msg }

# The approved-pack face assets, taken verbatim from the committed bindings that B-126 used
# (specs/image-generator-tests/dual-base-location/runs/dual-location-local-fast/07-identity/bindings-A.json).
$references = @{
    becky = @{
        CharacterName = 'Becky'
        RelativePath  = 'identity/f58f959a-8050-4388-a219-99d2df3446a1/D9ED3C9881A8DBDC5A6962071DEFDF98.png'
        Sha256        = '5E4F3477BF8A73C6B698373A3F0F138E7865900A2D55F74794D2B470E8E9D343'
        View          = 'ProfileLeft'
    }
    dean  = @{
        CharacterName = 'Dean'
        RelativePath  = 'identity/faee1ec0-1cf3-459e-97d2-ad59717c41ba/d5dfe951cc314de9be3be39af39e0781.png'
        Sha256        = 'A81D91B54537F30E0BE8E618628D4E2671924B4D8CA0C30C0EA180BDFDEEB466'
        View          = 'ProfileRight'
    }
    # Location references from the dual-base-location run (1216x832). These are the exact files the
    # old pipeline used as LocationReference in its composite + harmonize stages - so a one-call
    # generation can be compared against the multi-stage result on the same inputs.
    bedroom = @{
        CharacterName = 'bedroom location (locref-bedroom.png)'
        RelativePath  = 'locref-bedroom.png'
        Root          = 'specs/image-generator-tests/dual-base-location/runs/dual-location-local-fast/refs'
        Sha256        = '96B9BA7791DE4CB1ED121E045E8110BE8FCFF45DB6D718EE2E1B4C20A7BF8075'
        View          = 'n/a (location)'
    }
    outdoors = @{
        CharacterName = 'outdoors location (locref-outdoors.png)'
        RelativePath  = 'locref-outdoors.png'
        Root          = 'specs/image-generator-tests/dual-base-location/runs/dual-location-local-fast/refs'
        Sha256        = '9D02E517ABE9D220AADED2496FA1A1AA46DAED3884BC14E15825C7461154AE5B'
        View          = 'n/a (location)'
    }
    # Pose proof input (2026-09-23): the committed standing stance skeleton the body panel already
    # conditions SDXL on. It is a black frame with white joint dots and coloured limb lines - a
    # ControlNet conditioning map, NOT a photograph - which is the point of the poseSkel cell.
    skeletonStanding = @{
        CharacterName = 'standing OpenPose skeleton (pose-library/standing.png)'
        RelativePath  = 'standing.png'
        Root          = 'DreamGenClone.Web/wwwroot/pose-library'
        Sha256        = 'D99E07DCC8950187809E995DE452A0B25F42D31A59E9447E4109134B2860E4F1'
        View          = 'n/a (pose skeleton)'
    }
    # The FRONTAL face of the same character (Becky). The earlier pose cells used the ProfileLeft view,
    # which is angle-mismatched against a frontal request; the canonical pack face is the front view, so
    # the app-shaped cells use this one.
    # UPDATED 2026-09-25 to the CURRENT approved canonical face (pack v9 2d13c667, BodyComplete) - the
    # path the app's SceneImageReferenceAssets row actually serves today.
    beckyFront = @{
        CharacterName = 'Becky frontal face (pack v9 2d13c667 canonical Front)'
        RelativePath  = 'identity/de351eb3-69d3-421a-a762-79ae8ee183ed/282f5b91ba7d4dba85140ae006aa06ee.png'
        Sha256        = '00C1BDB0F8AF1B64E66BF37F4F6B935C77FC19EAADD9C04C0B159418FCFB5DDA'
        View          = 'Front'
    }
    # Dean's CURRENT approved canonical face (pack v9 1157b60f, FaceOnly). Dean has NO approved body
    # reference - his body is prompt-synthesized; only the face conditions.
    deanFront = @{
        CharacterName = 'Dean frontal face (pack v9 1157b60f canonical Front)'
        RelativePath  = 'identity/faee1ec0-1cf3-459e-97d2-ad59717c41ba/03da6dbdece34144b682365378591df2.png'
        Sha256        = '9622D608CDB190703953E5970402FE3520B55AC189EA518BBAFEDED4E3E61E11'
        View          = 'Front'
    }
    # The shed LOCATION reference (2026-09-25), generated by the shedLoc cell on the host and
    # reviewed: empty tin-walled shed, workbench along the wall, blue last light - matches the
    # moment's location/lighting fields. Pinned so the production-shaped cells verify it like any
    # other reference (the loc3 finding: the location ref's own size anchors the output frame).
    shedLocation = @{
        CharacterName = 'shed location reference (shedLoc cell output)'
        RelativePath  = 'shedLoc/result_0.png'
        Root          = 'artifacts/tmp/qwen-2-1'
        Sha256        = '7847CD8C5B6245FFB5CDCCF220C417D6F8FE6F7FD69D7457284CDFAFDA4091AB'
        View          = 'n/a (location)'
    }
    # The photoreal plate poseRef consumes, produced by the posePlate cell (verified 2026-09-23:
    # a clean upright frontal figure, arms at sides, feet apart - the stance the skeleton encodes).
    # Pinned here so the runner keeps verifying every reference instead of trusting a generated file.
    posePlate = @{
        CharacterName = 'photoreal pose plate (posePlate cell output)'
        RelativePath  = 'posePlate/result_0.png'
        Root          = 'artifacts/tmp/qwen-2-1'
        Sha256        = '06DA715AC525AD09D50DDF7BFF1578606E3D322791523F95CE2F4C5CFE8E3424'
        View          = 'n/a (pose plate)'
    }
    # Confirmation skeleton: identical wording and seed to poseSkel, ONLY the skeleton changes. If
    # the pose follows this one too, the pose is coming from the skeleton image rather than the seed.
    skeletonKneeling = @{
        CharacterName = 'kneeling OpenPose skeleton (pose-library/kneeling.png)'
        RelativePath  = 'kneeling.png'
        Root          = 'DreamGenClone.Web/wwwroot/pose-library'
        Sha256        = '0E14567222F06D64DF52E04FDEB991759C8520659FF259530A8042F34B5E298E'
        View          = 'n/a (pose skeleton)'
    }
    # ---- ANGLE PROOF inputs (2026-09-23) ----------------------------------------------------
    # A pose EXTRACTED from a known-good render (DWPose on
    # scene-images/assets/ab27c7ca23394071bcb35c148bcc770e.png, a clean upright frontal body), then
    # rotated in-plane by 45 degrees and fitted back into the 1024x1536 canvas. Both directions are
    # pinned so a null result cannot be blamed on the sign of the rotation.
    #
    # What this tests: an OpenPose skeleton encodes limb positions, NOT camera azimuth, so an in-plane
    # rotation produces a TILTED figure rather than a body turned about its vertical axis. The angle
    # cells measure whether 2.1 reads a tilted skeleton as a turn (useful) or as a tilt/lean (not).
    skeletonFit34Ccw = @{
        CharacterName = 'pose from ab27c7ca, rotated +45 deg in-plane (fitted)'
        RelativePath  = 'skeleton-fit-34-left-ccw.png'
        Root          = 'artifacts/tmp/qwen-2-1-pose'
        Sha256        = 'B73DB934263A118034765F7C87B53E26543435ECD0A6741747D777261BB102F0'
        View          = 'n/a (pose skeleton)'
    }
    skeletonFit34Cw = @{
        CharacterName = 'pose from ab27c7ca, rotated -45 deg in-plane (fitted)'
        RelativePath  = 'skeleton-fit-34-left-cw.png'
        Root          = 'artifacts/tmp/qwen-2-1-pose'
        Sha256        = '75422B910C3C0856056620590CE8B1EE2C512300637C8C0502763D616A6FE573'
        View          = 'n/a (pose skeleton)'
    }
    # Becky's THREE-QUARTER-LEFT pack face (the view that matches the requested angle, per the
    # committed angle-matching finding: a mismatched reference view does not transfer). This is the
    # approved pack v8 asset, i.e. the face-isolated crop the app would actually send today.
    becky34LeftFace = @{
        CharacterName = 'Becky 3/4-left pack face (pack v8 ThreeQuarterLeft)'
        RelativePath  = 'identity/de351eb3-69d3-421a-a762-79ae8ee183ed/456abbf779f544fcbf00d88a0c6f7c4d.png'
        Sha256        = '3A5E658BD5CA40EB842AAE46745E5983F6ACC8D193A71EA2120CE1575536A0E2'
        View          = 'ThreeQuarterLeft'
    }
    # The angle skeleton, ANNOTATED with DWPose from the plate34Left output (a real 3/4-left figure).
    # This replaces the rejected rotation approach: an in-plane rotation is only a tilt, and a world-3D
    # yaw shears the figure by ~27 cm because MediaPipe's monocular z runs 0.49 m along the body
    # (artifacts/tmp/qwen-2-1-pose/depth_gradient.py). The annotated skeleton carries the 3/4 view as a
    # fact: asymmetric shoulder line, staggered feet, offset hands.
    angleSkel34Left = @{
        CharacterName = 'annotated 3/4-left skeleton (DWPose on plate34Left)'
        RelativePath  = 'skeleton-angle-34-left.png'
        Root          = 'artifacts/tmp/qwen-2-1-pose'
        Sha256        = '854ECED5E6EEE031DA76FEBDD831641477CCBA7D8965486E5987A51639ED4109'
        View          = 'n/a (pose skeleton)'
    }
    # REGENERATED 2026-09-23. The original file here was DWPose-annotated from the old `plate34Right`, but it
    # was MEASURED to be the same body pose as the 3/4-left skeleton (chamfer same-orientation 7.13 px vs
    # mirrored 14.58 px: the un-mirrored match was twice as good), so case 13 rendered a LEFT-facing turn and
    # its "mirrored turn" verdict had never actually been measured. `plate34Right.json` was strengthened (the
    # turn had to be stated as a view, with the far arm hidden behind the torso) and its seed changed; this is
    # the DWPose annotation of that plate. It measures same-orientation 16.63 px vs 7.19 px mirrored, i.e. a
    # genuine right turn and NOT a clone of the left.
    angleSkel34Right = @{
        CharacterName = 'annotated 3/4-right skeleton (DWPose on the regenerated plate34Right)'
        RelativePath  = 'skeleton-angle-34-right.png'
        Root          = 'artifacts/tmp/qwen-2-1-pose'
        Sha256        = 'D2F5562132EAFB1BA64B25FC2C2DACC6557245A860A04AD173DCD3A93353C19E'
        View          = 'n/a (pose skeleton)'
    }
    angleSkelProfileLeft = @{
        CharacterName = 'annotated profile-left skeleton (DWPose on plateProfileLeft)'
        RelativePath  = 'skeleton-angle-profile-left.png'
        Root          = 'artifacts/tmp/qwen-2-1-pose'
        Sha256        = '34B4540B05E004ADDB8385F2C91BE040BCFCC1113BA551F34603BBD1156E13A0'
        View          = 'n/a (pose skeleton)'
    }
    angleSkelProfileRight = @{
        CharacterName = 'annotated profile-right skeleton (DWPose on plateProfileRight)'
        RelativePath  = 'skeleton-angle-profile-right.png'
        Root          = 'artifacts/tmp/qwen-2-1-pose'
        Sha256        = 'BF6219E132854EEC5228461093DF6F63253B4A683DC7D5D6EDA06CB827377A79'
        View          = 'n/a (pose skeleton)'
    }
    # The BACK view's skeleton (operator request, 2026-09-24): DWPose-annotated from plateBack, which is a genuine
    # full back view (back of the head, no face visible). Note what the annotation does with a hidden face: the face
    # keypoints come out as a clump at the top of the head, because there IS no face to find. That is recorded here
    # rather than smoothed over - it is why the back render is proved before the app offers it.
    angleSkelBack = @{
        CharacterName = 'annotated back-view skeleton (DWPose on plateBack)'
        RelativePath  = 'skeleton-angle-back.png'
        Root          = 'artifacts/tmp/qwen-2-1-pose'
        Sha256        = 'E94AEAB7D349C2F9F97C62623D1AD3FDA40C7CB0D0386D472C5521C42389D855'
        View          = 'n/a (pose skeleton)'
    }
    # The ACCEPTED Clothed Front body render the operator accepted for Becky
    # (scene-images/assets/ab27c7ca23394071bcb35c148bcc770e.png). Case 03 measured that a photoreal
    # full body in a reference slot is REPRODUCED rather than used as a pose donor - which for
    # "the same body, another angle" is the behaviour we want: the body travels, the skeleton steers the
    # angle and the face reference refines identity.
    beckyFrontBody = @{
        CharacterName = 'Becky accepted Clothed Front body render (ab27c7ca)'
        RelativePath  = 'assets/ab27c7ca23394071bcb35c148bcc770e.png'
        Sha256        = '4E3F9B29EF06A00A1735D573DA8F50D802D9BF3966B1C9CDF5A2A07C328D0913'
        View          = 'Front body (accepted)'
    }
}

# NOTE: do not name this $cells - PowerShell variables are case-insensitive, so it would
# silently overwrite the $Cells parameter and the loop would iterate DictionaryEntry objects.
$cellDefs = @{
    t2i = @{
        Kind        = 'text2image'
        Description = '1024x1024 text-to-image portrait (generation path)'
        Prompt      = 'Full-body editorial photograph of a young woman with long dark wavy hair, standing relaxed beside a tall industrial window in a sunlit loft apartment, wearing a simple charcoal knit sweater and dark jeans, three-quarter view, soft directional daylight from the window, shallow depth of field, natural skin texture, photorealistic, shot on 85mm'
    }
    rgba = @{
        Kind        = 'text2image'
        Description = 'transparent-background RGBA cutout (native alpha)'
        Prompt      = 'This is an RGBA format image with transparency. A full-body cutout of a female fantasy ranger in worn leather armour, arms relaxed at her sides, neutral studio pose, even soft lighting. The image has an alpha channel and a transparent background.'
    }
    edit1 = @{
        Kind        = 'imageedit'
        Description = 'bedroom-a base + Becky approved face (1 reference)'
        Refs        = @('becky')
        Prompt      = 'Replace the face of the woman on the image right in <image1> with the face of the woman in <image2>. Preserve her identity, skin tone, hair colour and hairstyle, her head angle and the existing lighting. Keep her body, pose and clothing unchanged, and keep the man, the room and the composition exactly as they are. Do not add any additional people.'
    }
    edit2 = @{
        Kind        = 'imageedit'
        Description = 'bedroom-a base + Becky AND Dean in ONE pass (2 references) - the B-126 case'
        Refs        = @('becky', 'dean')
        Prompt      = 'Replace the face of the woman on the image right in <image1> with the face of the woman in <image2>, and replace the face of the man on the image left in <image1> with the face of the man in <image3>. Preserve both identities, skin tones, hair colour and hairstyle, their head angles and the existing lighting. Keep both bodies, poses and clothing unchanged, and keep the room and the composition exactly as they are. Do not add any additional people.'
    }
    # edit2b: identical graph to edit2, ONLY the instruction changed. edit2's wording said both
    # "replace the face with <imageN>" AND "preserve both identities, skin tones, hair colour" -
    # a self-contradiction that pushes the model toward minimal change. This cell carries the
    # action-only phrasing so the instruction variable is isolated.
    edit2b = @{
        Kind        = 'imageedit'
        Description = 'bedroom-a + Becky AND Dean, ACTION-ONLY instruction (isolates the wording variable)'
        Refs        = @('becky', 'dean')
        Prompt      = 'Replace the face of the woman on the image right with the face of the woman in <image2>. Replace the face of the man on the image left with the face of the man in <image3>. Match each reference face exactly - bone structure, nose, jawline, eyes and facial hair. Keep both bodies, poses, clothing and the room as they are.'
    }
    # dean1: one character per edit (the shape B-126 proved necessary for the 2511 editor path),
    # so Dean's transfer can be judged without a second reference competing for attention.
    dean1 = @{
        Kind        = 'imageedit'
        Description = 'bedroom-a + Dean ONLY (one character per edit), ACTION-ONLY instruction'
        Refs        = @('dean')
        Prompt      = 'Replace the face of the man on the image left with the face of the man in <image2>. Match the reference face exactly - bone structure, nose, jawline, eyes and facial hair. Keep his body, pose, clothing and the room as they are.'
    }
    # deancu: the decisive close-up test. Same model/graph/settings as dean1, but image_1 is a tight
    # crop of the man's head (252x348) so the face occupies the frame instead of ~200 px in a 1216px
    # scene. Run with -TargetImage artifacts\tmp\qwen-2-1\man-head-crop.png. RESULT 2026-09-23:
    # identity TRANSFERS here (the target's full beard is replaced by Dean's stubble+fade), while
    # dean1 on the wide frame did nothing - so the limiting factor is the target face's share of
    # the frame, not the instruction or the reference wiring.
    deancu = @{
        Kind        = 'imageedit'
        Description = 'CLOSE-UP of the man head + Dean, ACTION-ONLY instruction (decisive identity test)'
        Refs        = @('dean')
        Prompt      = 'Replace the face of the man in <image1> with the face of the man in <image2>. Match the reference face exactly - bone structure, nose, jawline, eyes and facial hair. Keep his pose, clothing and the background as they are.'
    }
    # gen2b: confirmation of the generation-with-references shape on a different seed AND a pose the
    # references do not match (both refs are profiles; this asks for two people facing the camera),
    # which also probes the "reference head angle leaks into the output" caveat.
    gen2b = @{
        Kind        = 'text2image'
        Description = 'GENERATE a 2-person cafe scene from Becky + Dean refs, both facing camera (angle-mismatch probe)'
        Refs        = @('becky', 'dean')
        Prompt      = 'Photorealistic photograph of two people sitting across a small table in a cafe, both facing the camera. The woman is the woman from <image1>, the man is the man from <image2>. Keep both faces exactly as they appear in the reference images, including hair and facial hair. Warm indoor light, shallow depth of field, shot on 50mm.'
    }
    # loc3 / loc3r: LOCATION + FACES in ONE creation prompt. Same three inputs the old
    # dual-base-location pipeline consumed in separate stages (LocationReference + two Studio figures
    # -> composite -> harmonize -> per-character identity passes). Output size matches the 1216x832
    # location reference. loc3 puts the location first, loc3r puts it last, to measure whether the
    # slot order matters.
    loc3 = @{
        Kind        = 'text2image'
        Description = 'LOCATION (bedroom) + both faces in ONE generation, location as image_1'
        Refs        = @('bedroom', 'becky', 'dean')
        Size        = @(1216, 832)
        Prompt      = 'Photorealistic photograph of two people standing side by side in the bedroom shown in <image1>. Reproduce that room faithfully: the wooden bed with the grey textured throw, both bedside lamps lit with warm light, the tall windows with cream curtains, the pale carpet and wall colour, and the same camera angle and framing. The woman is the woman from <image2> and the man is the man from <image3>. Keep both faces exactly as they appear in their reference images, including hair and facial hair, and keep their head angles as in the references. Full body, natural scale, consistent lighting with the room.'
    }
    loc3r = @{
        Kind        = 'text2image'
        Description = 'LOCATION (bedroom) + both faces in ONE generation, location as image_3'
        Refs        = @('becky', 'dean', 'bedroom')
        Size        = @(1216, 832)
        Prompt      = 'Photorealistic photograph of two people standing side by side in the bedroom shown in <image3>. Reproduce that room faithfully: the wooden bed with the grey textured throw, both bedside lamps lit with warm light, the tall windows with cream curtains, the pale carpet and wall colour, and the same camera angle and framing. The woman is the woman from <image1> and the man is the man from <image2>. Keep both faces exactly as they appear in their reference images, including hair and facial hair, and keep their head angles as in the references. Full body, natural scale, consistent lighting with the room.'
    }
    gen2 = @{
        Kind        = 'text2image'
        Description = 'GENERATE a 2-person bedroom scene from Becky + Dean references (no edit target)'
        Refs        = @('becky', 'dean')
        Prompt      = 'Photorealistic editorial photograph of two people standing side by side in a sunlit loft bedroom. The woman from <image1> stands on the left with her arms relaxed, the man from <image2> stands on the right facing her. Keep both faces exactly as they appear in the reference images, including hair and facial hair. Soft daylight from a tall window, shallow depth of field, shot on 85mm.'
    }
    # ---- POSE PROOF (2026-09-23) --------------------------------------------------------------
    # Does Qwen-Image-2.1 take a pose from a REFERENCE IMAGE, and does it take one from an OpenPose
    # SKELETON? The skeleton route is the one that works on SDXL/FLUX through a ControlNet adapter
    # (thibaud-openpose-xl2 / flux-openpose-controlnet); 2.1 has NO ControlNet weights published
    # (HF search 'Qwen-Image-2.1-ControlNet' = 0 results; Comfy-Org/Qwen-Image-2.1 ships only
    # diffusion_models/text_encoders/vae), so a skeleton could only work through the model's own
    # reference slots - which is exactly what this proof measures rather than assumes.
    #
    # posePlate generates the photoreal plate that poseRef consumes. It is generated first, and its
    # checksum is pinned into $references afterwards (two-run protocol) rather than relaxing the
    # runner's SHA verification for a generated file.
    posePlate = @{
        Kind        = 'text2image'
        Description = 'POSE PLATE: photoreal person in the standing stance the skeleton encodes (no references)'
        Size        = @(832, 1216)
        Prompt      = 'Photorealistic full-body photograph of a person standing upright facing the camera, arms relaxed and hanging at their sides with a small gap from the body, feet about shoulder width apart, neutral expression, wearing a plain light-grey t-shirt and dark trousers, plain white seamless studio background, even soft lighting, sharp focus, full body head to feet, shot on 50mm'
    }
    # poseTextRef: the pose in WORDS ONLY, with the identity reference present. Same stance, same
    # wardrobe, same framing as the plate cells so the only variable is how the pose is carried.
    poseTextRef = @{
        Kind        = 'text2image'
        Description = 'POSE PROOF baseline: stance described in WORDS, identity reference present'
        Refs        = @('becky')
        Size        = @(832, 1216)
        Prompt      = 'Photorealistic full-body photograph of the woman from <image1>, standing upright facing the camera with her arms relaxed and hanging at her sides with a small gap from her body, and her feet about shoulder width apart. Keep her face, hair, hair colour and skin tone exactly as in <image1>. She wears a plain light-grey t-shirt and dark trousers. Plain white seamless studio background, even soft lighting, sharp focus, full body head to feet, shot on 50mm.'
    }
    # poseRef / poseSkel: IDENTICAL wording, only <image1> differs (photoreal plate vs OpenPose
    # skeleton). The pose is never named, so any pose in the output comes from that one image.
    poseRef = @{
        Kind        = 'text2image'
        Description = 'POSE PROOF: photoreal plate as <image1>, pose NOT named in words'
        Refs        = @('posePlate', 'becky')
        Size        = @(832, 1216)
        Prompt      = 'Photorealistic full-body photograph of the woman from <image2>, standing in the exact pose shown in <image1> - the same stance, the same limb positions and the same body angles. Keep her face, hair, hair colour and skin tone exactly as in <image2>. She wears a plain light-grey t-shirt and dark trousers. Plain white seamless studio background, even soft lighting, sharp focus, full body head to feet, shot on 50mm.'
    }
    poseSkel = @{
        Kind        = 'text2image'
        Description = 'POSE PROOF: OpenPose skeleton as <image1>, pose NOT named in words (same wording as poseRef)'
        Refs        = @('skeletonStanding', 'becky')
        Size        = @(832, 1216)
        Prompt      = 'Photorealistic full-body photograph of the woman from <image2>, standing in the exact pose shown in <image1> - the same stance, the same limb positions and the same body angles. Keep her face, hair, hair colour and skin tone exactly as in <image2>. She wears a plain light-grey t-shirt and dark trousers. Plain white seamless studio background, even soft lighting, sharp focus, full body head to feet, shot on 50mm.'
    }
    poseSkelKneel = @{
        Kind        = 'text2image'
        Description = 'POSE PROOF CONFIRMATION: KNEELING skeleton, identical wording and seed to poseSkel'
        Refs        = @('skeletonKneeling', 'becky')
        Size        = @(832, 1216)
        Prompt      = 'Photorealistic full-body photograph of the woman from <image2>, standing in the exact pose shown in <image1> - the same stance, the same limb positions and the same body angles. Keep her face, hair, hair colour and skin tone exactly as in <image2>. She wears a plain light-grey t-shirt and dark trousers. Plain white seamless studio background, even soft lighting, sharp focus, full body head to feet, shot on 50mm.'
    }
    # ---- APP-SHAPED COMPOSITION PROOF (2026-09-23) ----------------------------------------------
    # The shape the application would send: LOCATION + POSE + IDENTITY in ONE native-reference call,
    # with the frontal (canonical) face rather than the angle-mismatched profile. The wide cell matches
    # the location reference's own frame; the tall cell keeps the person large enough to judge identity
    # at the ~200 px face size the wide scene renders were measured to lose identity at.
    appComposeWide = @{
        Kind        = 'text2image'
        Description = 'APP SHAPE: location + pose + identity in ONE call, 1216x832 (location frame)'
        Refs        = @('bedroom', 'skeletonStanding', 'beckyFront')
        Size        = @(1216, 832)
        Prompt      = 'Photorealistic photograph of a woman standing in the bedroom shown in <image1>, in the exact pose shown in <image2> - the same stance, the same limb positions and the same body angles. Her face, hair, hair colour and skin tone are exactly those of the woman in <image3>. Reproduce the room faithfully: the wooden bed with the grey textured throw, the bedside lamps lit with warm light, the tall windows with cream curtains, the pale carpet and wall colour, and the same camera angle and framing. She wears a plain light-grey t-shirt and dark jeans. Keep her build and scale natural for the room. Sharp focus, natural skin texture, shot on 50mm.'
    }
    appComposeTall = @{
        Kind        = 'text2image'
        Description = 'APP SHAPE: location + pose + identity in ONE call, 832x1216 (portrait frame, larger subject)'
        Refs        = @('bedroom', 'skeletonStanding', 'beckyFront')
        Size        = @(832, 1216)
        Prompt      = 'Photorealistic photograph of a woman standing in the bedroom shown in <image1>, in the exact pose shown in <image2> - the same stance, the same limb positions and the same body angles. Her face, hair, hair colour and skin tone are exactly those of the woman in <image3>. Reproduce the room faithfully: the wooden bed with the grey textured throw, the bedside lamps lit with warm light, the tall windows with cream curtains, the pale carpet and wall colour, and the same camera angle and framing. She wears a plain light-grey t-shirt and dark jeans. Keep her build and scale natural for the room. Sharp focus, natural skin texture, shot on 50mm.'
    }
    # Slot-order probe: the FACE is image_1 instead of the location. Measured elsewhere that slot 1
    # anchors the frame and that a photoreal person in slot 1 is reproduced wholesale, so this checks
    # whether a face-first order starves the location or the pose.
    appComposeOrder = @{
        Kind        = 'text2image'
        Description = 'APP SHAPE slot-order probe: FACE as <image1>, then pose, then location (1216x832)'
        Refs        = @('beckyFront', 'skeletonStanding', 'bedroom')
        Size        = @(1216, 832)
        Prompt      = 'Photorealistic photograph of a woman standing in the bedroom shown in <image3>, in the exact pose shown in <image2> - the same stance, the same limb positions and the same body angles. Her face, hair, hair colour and skin tone are exactly those of the woman in <image1>. Reproduce the room faithfully: the wooden bed with the grey textured throw, the bedside lamps lit with warm light, the tall windows with cream curtains, the pale carpet and wall colour, and the same camera angle and framing. She wears a plain light-grey t-shirt and dark jeans. Keep her build and scale natural for the room. Sharp focus, natural skin texture, shot on 50mm.'
    }
    appPoseIdFront = @{
        Kind        = 'text2image'
        Description = 'APP SHAPE isolation: pose skeleton + FRONTAL identity, no location (832x1216)'
        Refs        = @('skeletonStanding', 'beckyFront')
        Size        = @(832, 1216)
        Prompt      = 'Photorealistic full-body photograph of the woman in <image2>, standing in the exact pose shown in <image1> - the same stance, the same limb positions and the same body angles. Her face, hair, hair colour and skin tone are exactly those of the woman in <image2>: the same eyes, nose, mouth, jawline and freckles. She wears a plain light-grey t-shirt and dark jeans. Plain white seamless studio background, even soft lighting, sharp focus, full body head to feet, shot on 50mm.'
    }
    appPoseIdKneel = @{
        Kind        = 'text2image'
        Description = 'APP SHAPE isolation: KNEELING skeleton + FRONTAL identity, no location (832x1216)'
        Refs        = @('skeletonKneeling', 'beckyFront')
        Size        = @(832, 1216)
        Prompt      = 'Photorealistic full-body photograph of the woman in <image2>, standing in the exact pose shown in <image1> - the same stance, the same limb positions and the same body angles. Her face, hair, hair colour and skin tone are exactly those of the woman in <image2>: the same eyes, nose, mouth, jawline and freckles. She wears a plain light-grey t-shirt and dark jeans. Plain white seamless studio background, even soft lighting, sharp focus, full body head to feet, shot on 50mm.'
    }
    # ---- ANGLE PROOF (2026-09-23): can a 3/4 body view come from a tilted skeleton? ----------------
    # The body panel's problem case: it has an accepted FRONT body and wants a three-quarter-left view.
    # Today that is an edit/rotation of the accepted source. This asks whether 2.1 can GENERATE the
    # angle instead, from (a) a pose rotated 3/4 left + (b) the ANGLE-MATCHED pack face, with the body
    # prompt the app already compiles. Size is the app's body view size (1024x1536).
    #
    # The two cells differ ONLY in the sign of the rotation, so they also answer "which way does the
    # model read a positive rotation?" instead of leaving it as a guess.
    bodyAngle34Ccw = @{
        Kind        = 'text2image'
        Description = 'ANGLE PROOF: pose rotated +45 (fitted) + 3/4-left face + body prompt (1024x1536)'
        Refs        = @('skeletonFit34Ccw', 'becky34LeftFace')
        Size        = @(1024, 1536)
        Prompt      = 'Photorealistic full-body photograph of the woman in <image2>, her body turned three-quarters to her left, standing in the exact pose shown in <image1> - the same stance, the same limb positions and the same body angles. Her face, hair, hair colour and skin tone are exactly those of the woman in <image2>. Body: an average frame, an hourglass figure with a narrow waist, slightly soft with a little roundness, evenly distributed, no visible muscle, soft, with no muscle definition, an average bust, average waist, average hips, an average rear, tattoo of a tree on left calf. Wearing T-Shirt and 3/4 Jeans. Whole body in frame and unobstructed, head to feet, natural skin texture, soft even lighting, sharp focus, 35mm photograph.'
    }
    bodyAngle34Cw = @{
        Kind        = 'text2image'
        Description = 'ANGLE PROOF control: pose rotated -45 (fitted) + 3/4-left face (same wording/seed)'
        Refs        = @('skeletonFit34Cw', 'becky34LeftFace')
        Size        = @(1024, 1536)
        Prompt      = 'Photorealistic full-body photograph of the woman in <image2>, her body turned three-quarters to her left, standing in the exact pose shown in <image1> - the same stance, the same limb positions and the same body angles. Her face, hair, hair colour and skin tone are exactly those of the woman in <image2>. Body: an average frame, an hourglass figure with a narrow waist, slightly soft with a little roundness, evenly distributed, no visible muscle, soft, with no muscle definition, an average bust, average waist, average hips, an average rear, tattoo of a tree on left calf. Wearing T-Shirt and 3/4 Jeans. Whole body in frame and unobstructed, head to feet, natural skin texture, soft even lighting, sharp focus, 35mm photograph.'
    }
    # ---- ANGLE SKELETON SOURCE PLATE (2026-09-23) ----------------------------------------------
    # Measured first, and the rotation approach was REJECTED on the numbers (depth_gradient.py):
    # MediaPipe world z varies 0.49 m along the body (ears -0.29 m, ankles +0.20 m), so a 45-degree yaw
    # injects ~27 cm of shear - the head and feet slide apart and the figure comes out diagonal. An
    # in-plane rotation of the drawing is merely a tilt. A planar (z=0) yaw is stable but only narrows
    # the figure; it carries almost no turn information.
    #
    # So the angle skeleton is ANNOTATED FROM A REAL IMAGE in the target view, which is how every
    # OpenPose dataset is built. This cell produces that image; run DWPose on its output to get the
    # skeleton. The plate is used OFFLINE only and is never sent to Qwen as a reference - which is what
    # keeps case 03's rule (a photoreal full body in a slot is reproduced wholesale) out of the picture.
    plate34Left = @{
        Kind        = 'text2image'
        Description = 'ANGLE SKELETON SOURCE: photoreal figure turned 3/4 to her left (1024x1536, no refs)'
        Size        = @(1024, 1536)
        Prompt      = 'Photorealistic full-body photograph of a woman standing upright with her body turned three-quarters to her left, arms relaxed and hanging at her sides with a small gap from her body, feet about shoulder width apart, neutral expression, wearing a plain light-grey t-shirt and dark jeans, plain white seamless studio background, even soft lighting, sharp focus, full body head to feet, shot on 50mm'
    }
    # The remaining angle plates, same recipe: one photoreal image per view, used OFFLINE as the DWPose
    # annotation source. They are never sent to Qwen as references.
    plate34Right = @{
        Kind        = 'text2image'
        Description = 'ANGLE SKELETON SOURCE: photoreal figure turned 3/4 to her right (1024x1536, no refs)'
        Size        = @(1024, 1536)
        Prompt      = 'Photorealistic full-body photograph of a woman standing upright, photographed from a camera placed 45 degrees to her right so her body is seen in a clear three-quarter view with her right shoulder and right arm nearer to the camera and her body turned away to her left, her head turned to look at the camera, arms relaxed and hanging at her sides with a small gap from her body, feet about shoulder width apart, wearing a plain light-grey t-shirt and dark jeans, plain white seamless studio background, even soft lighting, sharp focus, full body head to feet, shot on 50mm'
    }
    plateProfileLeft = @{
        Kind        = 'text2image'
        Description = 'ANGLE SKELETON SOURCE: photoreal figure in full left profile (1024x1536, no refs)'
        Size        = @(1024, 1536)
        Prompt      = 'Photorealistic full-body photograph of a woman standing upright photographed directly from her left side so her body is seen edge-on in full left profile, her nose pointing to the left of the frame, arms relaxed and hanging at her sides, feet about shoulder width apart, wearing a plain light-grey t-shirt and dark jeans, plain white seamless studio background, even soft lighting, sharp focus, full body head to feet, shot on 50mm'
    }
    plateProfileRight = @{
        Kind        = 'text2image'
        Description = 'ANGLE SKELETON SOURCE: photoreal figure in full right profile (1024x1536, no refs)'
        Size        = @(1024, 1536)
        Prompt      = 'Photorealistic full-body photograph of a woman standing upright photographed directly from her right side so her body is seen edge-on in full right profile, her nose pointing to the right of the frame, arms relaxed and hanging at her sides, feet about shoulder width apart, wearing a plain light-grey t-shirt and dark jeans, plain white seamless studio background, even soft lighting, sharp focus, full body head to feet, shot on 50mm'
    }
    # The BACK view (operator request, 2026-09-24). Same recipe: a plate for DWPose to annotate, used offline only.
    # The wording has to make "no face" explicit - from behind, the head is the back of the head and the face is not
    # visible at all - because a model asked for a "back view" will otherwise turn the head to look at the camera, and
    # the skeleton that comes off that plate would carry a turned head into every back view the app renders.
    plateBack = @{
        Kind        = 'text2image'
        Description = 'ANGLE SKELETON SOURCE: photoreal figure photographed from directly behind, full back view (1024x1536, no refs)'
        Size        = @(1024, 1536)
        Prompt      = 'Photorealistic full-body photograph of a woman standing upright photographed from directly behind her, a full back view: only her back is visible, the back of her head, her shoulders, back, backside and the backs of her legs, her face is not visible at all and she is not looking at the camera, her head facing straight forward away from the camera, arms relaxed and hanging at her sides with a small gap from her body, feet about shoulder width apart, wearing a plain light-grey t-shirt and dark jeans, plain white seamless studio background, even soft lighting, sharp focus, full body head to feet, shot on 50mm'
    }
    bodyAngle34Real = @{        Kind        = 'text2image'
        Description = 'ANGLE PROOF: ANNOTATED 3/4-left skeleton + 3/4-left face + body prompt (1024x1536)'
        Refs        = @('angleSkel34Left', 'becky34LeftFace')
        Size        = @(1024, 1536)
        Prompt      = 'Photorealistic full-body photograph of the woman in <image2>, her body turned three-quarters to her left, standing in the exact pose shown in <image1> - the same stance, the same limb positions and the same body angles. Her face, hair, hair colour and skin tone are exactly those of the woman in <image2>. Body: an average frame, an hourglass figure with a narrow waist, slightly soft with a little roundness, evenly distributed, no visible muscle, soft, with no muscle definition, an average bust, average waist, average hips, an average rear, tattoo of a tree on left calf. Wearing T-Shirt and 3/4 Jeans. Whole body in frame and unobstructed, head to feet, natural skin texture, soft even lighting, sharp focus, 35mm photograph.'
    }
    # Isolation: the SAME wording and seed with the skeleton removed, so whatever angle appears can only
    # have come from the words plus the angle-matched face. This is the cell that decides whether an
    # angle-skeleton library is needed at all.
    bodyAngle34NoSkel = @{
        Kind        = 'text2image'
        Description = 'ANGLE PROOF isolation: NO skeleton, 3/4-left face + words only (same seed)'
        Refs        = @('becky34LeftFace')
        Size        = @(1024, 1536)
        Prompt      = 'Photorealistic full-body photograph of the woman in <image1>, her body turned three-quarters to her left, standing upright with her arms relaxed at her sides and her feet about shoulder width apart. Her face, hair, hair colour and skin tone are exactly those of the woman in <image1>. Body: an average frame, an hourglass figure with a narrow waist, slightly soft with a little roundness, evenly distributed, no visible muscle, soft, with no muscle definition, an average bust, average waist, average hips, an average rear, tattoo of a tree on left calf. Wearing T-Shirt and 3/4 Jeans. Whole body in frame and unobstructed, head to feet, natural skin texture, soft even lighting, sharp focus, 35mm photograph.'
    }
    # The operator's own proposal: the ACCEPTED FRONT as the body reference, turned by the angle skeleton.
    # frontPose keeps identity on the body reference alone; frontPoseFace adds the angle-matched face as a
    # third slot. The pair isolates what the face reference still contributes once the body is supplied.
    bodyAngle34FrontPose = @{
        Kind        = 'text2image'
        Description = 'ANGLE PROOF: accepted FRONT body + 3/4-left skeleton (no face reference)'
        Refs        = @('beckyFrontBody', 'angleSkel34Left')
        Size        = @(1024, 1536)
        Prompt      = 'Photorealistic full-body photograph of the woman in <image1>, her body turned three-quarters to her left, standing in the exact pose shown in <image2> - the same stance, the same limb positions and the same body angles. Keep her build, her proportions, her skin tone and her tattoo of a tree on her left calf exactly as in <image1>. She wears T-Shirt and 3/4 Jeans. Whole body in frame and unobstructed, head to feet, natural skin texture, soft even lighting, sharp focus, 35mm photograph.'
    }
    bodyAngle34FrontPoseFace = @{
        Kind        = 'text2image'
        Description = 'ANGLE PROOF: accepted FRONT body + 3/4-left skeleton + 3/4-left FACE'
        Refs        = @('beckyFrontBody', 'angleSkel34Left', 'becky34LeftFace')
        Size        = @(1024, 1536)
        Prompt      = 'Photorealistic full-body photograph of the woman in <image1>, her body turned three-quarters to her left, standing in the exact pose shown in <image2> - the same stance, the same limb positions and the same body angles. Keep her build, her proportions, her skin tone and her tattoo of a tree on her left calf exactly as in <image1>. Her face, hair, hair colour and hairstyle are exactly those of the woman in <image3>. She wears T-Shirt and 3/4 Jeans. Whole body in frame and unobstructed, head to feet, natural skin texture, soft even lighting, sharp focus, 35mm photograph.'
    }
    # The remaining angles, each with the SAME recommended shape (accepted front body + that angle's
    # skeleton) so every canonical non-front view has a verified recipe from the same mechanism.
    bodyAngle34RightFromFront = @{
        Kind        = 'text2image'
        Description = 'ANGLE PROOF: accepted FRONT body + 3/4-RIGHT skeleton (1024x1536)'
        Refs        = @('beckyFrontBody', 'angleSkel34Right')
        Size        = @(1024, 1536)
        Prompt      = 'Photorealistic full-body photograph of the woman in <image1>, her body turned three-quarters to her right, standing in the exact pose shown in <image2> - the same stance, the same limb positions and the same body angles. Keep her build, her proportions, her skin tone and her tattoo of a tree on her left calf exactly as in <image1>. She wears T-Shirt and 3/4 Jeans. Whole body in frame and unobstructed, head to feet, natural skin texture, soft even lighting, sharp focus, 35mm photograph.'
    }
    bodyProfileLeftFromFront = @{
        Kind        = 'text2image'
        Description = 'ANGLE PROOF: accepted FRONT body + PROFILE-LEFT skeleton (1024x1536)'
        Refs        = @('beckyFrontBody', 'angleSkelProfileLeft')
        Size        = @(1024, 1536)
        Prompt      = 'Photorealistic full-body photograph of the woman in <image1> photographed from her left side so her body is seen edge-on in full left profile, standing in the exact pose shown in <image2> - the same stance, the same limb positions and the same body angles. Keep her build, her proportions, her skin tone and her tattoo of a tree on her left calf exactly as in <image1>. She wears T-Shirt and 3/4 Jeans. Whole body in frame and unobstructed, head to feet, natural skin texture, soft even lighting, sharp focus, 35mm photograph.'
    }
    bodyProfileRightFromFront = @{
        Kind        = 'text2image'
        Description = 'ANGLE PROOF: accepted FRONT body + PROFILE-RIGHT skeleton (1024x1536)'
        Refs        = @('beckyFrontBody', 'angleSkelProfileRight')
        Size        = @(1024, 1536)
        Prompt      = 'Photorealistic full-body photograph of the woman in <image1> photographed from her right side so her body is seen edge-on in full right profile, standing in the exact pose shown in <image2> - the same stance, the same limb positions and the same body angles. Keep her build, her proportions, her skin tone and her tattoo of a tree on her left calf exactly as in <image1>. She wears T-Shirt and 3/4 Jeans. Whole body in frame and unobstructed, head to feet, natural skin texture, soft even lighting, sharp focus, 35mm photograph.'
    }
    # The BACK view (operator request, 2026-09-24), in the same recommended shape as the other angles. TWO things are
    # being proved here, and the second is the reason this cell exists rather than being assumed from the four angles:
    #   1. the accepted front body + the back skeleton produces a true back view (a back is not "another 45 degrees" -
    #      the whole front of the body becomes invisible, and the front body reference is a picture OF that front);
    #   2. NO FACE appears. The reference body has a visible face, the plate has none, and the skeleton's face keypoints
    #      are a clump - so "does the model turn her head to show the face it was shown" is a real risk, not a detail.
    bodyBackFromFront = @{
        Kind        = 'text2image'
        Description = 'ANGLE PROOF: accepted FRONT body + BACK skeleton, face must NOT appear (1024x1536)'
        Refs        = @('beckyFrontBody', 'angleSkelBack')
        Size        = @(1024, 1536)
        Prompt      = 'Photorealistic full-body photograph of the woman in <image1> photographed from directly behind her, a full back view: only her back is visible - the back of her head, her shoulders, her back, her backside and the backs of her legs - standing in the exact pose shown in <image2>, the same stance, the same limb positions and the same body angles. Her face is not visible at all and she is not looking at the camera. Keep her build, her proportions, her skin tone and her tattoo of a tree on her left calf exactly as in <image1>. She wears T-Shirt and 3/4 Jeans. Whole body in frame and unobstructed, head to feet, natural skin texture, soft even lighting, sharp focus, 35mm photograph.'
    }
    # ---- PRODUCTION MOMENT PROOF (2026-09-25): shed location + BOTH identities in ONE call -------
    # Production group 7627eb78 (session 8bc36efb, interaction 32244bb9, moment m1 "Still Joined at
    # the Workbench Start", POV Omniscient, IdentityPolicy Required) has NO bound references: every
    # SceneImages row for it is RenderMode=PromptOnly with assetId=null on every typed reference.
    # That is why 2.1, FLUX, BigLust and Juggernaut all rendered malformed entangled bodies - every
    # render was plain prompt-only t2i of two nude figures, the worst case for every model.
    # Cells:
    #   shedLoc        the LOCATION REFERENCE itself (empty shed, 1216x832 - the locref convention;
    #                  output size matches the location ref's own size, per the loc3 finding).
    #   genShedClothed location + BOTH canonical front faces in ONE t2i call, both figures CLOTHED in
    #                  the supine-on-bench geometry - the benign mechanism proof (refs upload, room
    #                  transfer, both identities, slot order, pose anchoring).
    #   genShed        the production-moment shape itself (the compiled prompt with ref naming and
    #                  the anchored pose). NOT run by the agent - the operator runs it (explicit).
    shedLoc = @{
        Kind        = 'text2image'
        Description = 'LOCATION REFERENCE: empty tin-walled shed with workbench at last light (1216x832, no refs)'
        Size        = @(1216, 832)
        Prompt      = 'Photorealistic 35mm wide photograph of the empty interior of a dim tin-walled shed at the last light of day. A heavy wooden workbench with rough worn boards runs along the wall, dusty grit scattered across the plank floor, corrugated tin walls, a few hand tools hanging, thin blue dusk light filtering through gaps in the boards, deep shadow in the corners. No people. Natural textures, dim ambient light, shot on 35mm.'
    }
    # genShedClothed (2026-09-25): the MECHANISM proof for the production moment. Location ref in
    # slot 1 (anchors the frame, per the loc3/loc3r finding), both canonical CLOTHED face refs in
    # slots 2/3, both figures in the supine/kneeling workbench geometry with every limb anchored
    # (the foot-to-face fix: one location per limb, stated relative to the bench). CLOTHED on
    # purpose - the mechanism is validated without rendering the explicit production prompt.
    genShedClothed = @{
        Kind        = 'text2image'
        Description = 'PRODUCTION SHAPE (clothed): shed location + Becky + Dean faces, anchored bench pose (1216x832)'
        Refs        = @('shedLocation', 'beckyFront', 'deanFront')
        Size        = @(1216, 832)
        Prompt      = 'Photorealistic 35mm wide photograph of two people in the tin-walled shed shown in <image1>: reproduce that shed faithfully - the wooden workbench along the wall, the dusty grit on the floor, the corrugated tin walls and the thin blue last light through the gaps. The woman is the woman from <image2> and the man is the man from <image3>; keep both faces exactly as they appear in their reference images, including hair. She lies on her back along the workbench boards fully clothed in a t-shirt and jeans, her head at the far end of the bench, her knees bent and her feet resting flat on the boards well below her shoulders, her arms resting back along the boards. He stands on the floor beside the bench, facing her, looking down at her, fully clothed in a shirt and trousers, a light sheen of sweat on his face. Natural skin texture, dim ambient light, shot on 35mm.'
    }
    # RESULT (2026-09-25): genShedClothed rendered ONE figure - the man was absent. Room fidelity,
    # frame anchoring (1216x832) and the anchored pose all passed; the SECOND identity did not appear.
    # Hypothesis: image_1 anchors the composition (loc3 finding), and the ref in slot 1 is an EMPTY
    # room - so the model composed from the empty-shed frame and dropped the second person. This cell
    # is the loc3r-style ordering probe: FACES first, location last. Same prompt and seed otherwise,
    # so the slot order is the only variable.
    genShedClothedFacesFirst = @{
        Kind        = 'text2image'
        Description = 'PRODUCTION SHAPE (clothed) slot-order probe: FACES in slots 1-2, shed location in slot 3 (1216x832)'
        Refs        = @('beckyFront', 'deanFront', 'shedLocation')
        Size        = @(1216, 832)
        Prompt      = 'Photorealistic 35mm wide photograph of two people in the tin-walled shed shown in <image3>: reproduce that shed faithfully - the wooden workbench along the wall, the dusty grit on the floor, the corrugated tin walls and the thin blue last light through the gaps. The woman is the woman from <image1> and the man is the man from <image2>; keep both faces exactly as they appear in their reference images, including hair. She lies on her back along the workbench boards fully clothed in a t-shirt and jeans, her head at the far end of the bench, her knees bent and her feet resting flat on the boards well below her shoulders, her arms resting back along the boards. He stands on the floor beside the bench, facing her, looking down at her, fully clothed in a shirt and trousers, a light sheen of sweat on his face. Natural skin texture, dim ambient light, shot on 35mm.'
    }
    # ---- DIAGNOSTIC BATTERY (2026-09-25) -------------------------------------------------------
    # Measured problems on the two cells above:
    #  (a) GRAIN: edge energy 51.7 vs 17.3 for the location ref - the two-person renders are ~3x
    #      noisier. Step count 25 is below the 2.1 official 40-50, AND the shedLocation ref is
    #      itself a very high-frequency image (rusty tin, sawdust over the whole floor) which the
    #      native-reference path reproduces wholesale.
    #  (b) MINIATURE FIGURE: the woman is ~1/7 of frame height. The composition is anchored by an
    #      EMPTY ROOM reference, so the room is the subject and the people are small props. Moving
    #      the faces to slots 1-2 did NOT fix it.
    #  (c) MISSING MAN: only one figure rendered in BOTH orderings, though both face refs verified
    #      and uploaded. Needs isolating - is it the empty-room ref suppressing the second person?
    # Each cell below removes ONE variable.
    #
    # shedLocClean: a LOW-NOISE location ref - smooth flat tin, sparse grit, brighter ambient. If the
    # grain follows the reference, this fixes it; if the grain stays, it is the sampler/step count.
    shedLocClean = @{
        Kind        = 'text2image'
        Description = 'LOCATION REFERENCE v2 (low-noise): clean flat tin shed, sparse grit, brighter (1216x832, no refs)'
        Size        = @(1216, 832)
        Prompt      = 'Photorealistic 35mm wide photograph of the clean empty interior of a tin-walled shed in soft daylight. Smooth flat galvanised tin walls with only light surface wear, a solid wooden workbench with clean unworn boards along the wall, bare swept plank floor with a small scatter of sawdust, a couple of hand tools hanging neatly. Even soft ambient daylight from a doorway, gentle shadow, minimal texture contrast, no clutter. No people. Sharp focus, clean image, shot on 35mm.'
    }
    # genTwoFacesNoLoc: the MISSING-MAN isolation. NO location reference at all - the shed is only
    # described in words. If two figures appear, the empty-room ref was suppressing the second person;
    # if the man is STILL missing, the problem is the two-reference identity path itself.
    genTwoFacesNoLoc = @{
        Kind        = 'text2image'
        Description = 'ISOLATION: Becky + Dean faces ONLY, no location ref, shed in words, medium shot (1216x832)'
        Refs        = @('beckyFront', 'deanFront')
        Size        = @(1216, 832)
        Prompt      = 'Photorealistic medium shot photograph inside a dim tin-walled shed at last light, the two figures filling the frame. The woman is the woman from <image1> and the man is the man from <image2>; keep both faces exactly as they appear in their reference images, including hair. She lies on her back along a rough wooden workbench, fully clothed in a t-shirt and jeans, her head at the far end of the bench, her knees bent and her feet resting flat on the boards well below her shoulders, her arms resting back along the boards. He stands beside the bench in the foreground, close to the camera, facing her and looking down at her, fully clothed in a shirt and trousers, a light sheen of sweat on his face. Dusty grit on the floor, corrugated tin walls, thin blue dusk light through the gaps. Natural skin texture, dim ambient light, shot on 35mm.'
    }
    # genShedTight: the SCALE fix with the location ref kept - same three refs, but the framing asks
    # for a medium shot with the figures filling the frame instead of a "wide" room shot, and the
    # square frame removes the letterbox that made the room dominate.
    genShedTight = @{
        Kind        = 'text2image'
        Description = 'SCALE FIX: shed location + both faces, MEDIUM shot figures fill frame, square (1216x1216)'
        Refs        = @('shedLocation', 'beckyFront', 'deanFront')
        Size        = @(1216, 1216)
        Prompt      = 'Photorealistic medium shot photograph inside the dim tin-walled shed shown in <image1>, the two people large in the frame and filling it edge to edge - the wooden workbench, the corrugated tin walls and the thin blue last light are visible around them. The woman is the woman from <image2> and the man is the man from <image3>; keep both faces exactly as they appear in their reference images, including hair. She lies on her back along the workbench boards, fully clothed in a t-shirt and jeans, her head at the right of frame, her knees bent and her feet resting flat on the boards well below her shoulders, her arms resting back along the boards. He stands beside the bench in the left foreground, facing her and looking down at her, fully clothed in a shirt and trousers, a light sheen of sweat on his face. Close to the subjects, natural skin texture, dim ambient light, shot on 35mm.'
    }
}

# --------------------------------------------------------------- host preflight
$hostInfo = $null
try { $hostInfo = Invoke-RestMethod -Uri "$base/system_stats" -TimeoutSec 30 }
catch { Fail "Local ComfyUI is not reachable at $base : $($_.Exception.Message)" }
Write-Host "Host     : $base (ComfyUI $($hostInfo.system.comfyui_version), $($hostInfo.devices[0].name), $([math]::Round($hostInfo.devices[0].vram_free/1GB,2)) GB VRAM free)"

$oi = Invoke-RestMethod -Uri "$base/object_info" -TimeoutSec 180
$known = $oi.PSObject.Properties.Name
foreach ($cls in @('UNETLoader', 'CLIPLoader', 'VAELoader', 'KSampler', 'VAEDecode', 'SaveImage', 'TextEncodeQwenImage21', 'QwenImage21Cache', 'LoadImage')) {
    if ($known -notcontains $cls) { Fail "Host $base is missing required node class '$cls'. Run helpers/local-comfyui-host/provision-qwen-image-2-1.ps1 first." }
}
Write-Host "Preflight: Qwen-Image-2.1 node classes present." -ForegroundColor Green

# Reference inputs. The node declares ONE autogrow input (`images`) whose sub-inputs are named
# image_1..image_16 in the node source:
#   comfy_extras/nodes_qwen.py -> io.Autogrow.Input("images",
#       template=io.Autogrow.TemplateNames(io.Image.Input("image"),
#                                          names=[f"image_{i}" for i in range(1, 17)], min=0))
# /object_info lists only the `images` placeholder, and ComfyUI's prompt validator SILENTLY
# IGNORES unknown input names (verified 2026-09-22 with a bogus name -> HTTP 200), so the names
# cannot be discovered by probing. They are pinned here from the declaration, and the cells are
# validated at run time by the produced image's size (the encoder's latent follows image_1).
$encSchema = $oi.TextEncodeQwenImage21.input
$hasAutogrow = @($encSchema.required.PSObject.Properties.Name) -contains 'images'
if (-not $hasAutogrow) {
    Fail "TextEncodeQwenImage21 no longer advertises the 'images' autogrow input - re-read comfy_extras/nodes_qwen.py before trusting this runner."
}
Write-Host "Reference inputs: image_1..image_16 (autogrow 'images'), 10 max named by the templates"

# Confirm the loaders actually list the model files (fail fast, do not let ComfyUI guess).
foreach ($pair in @(@{ L = 'UNETLoader'; F = $UnetName; K = 'unet_name' }, @{ L = 'CLIPLoader'; F = $ClipName; K = 'clip_name' }, @{ L = 'VAELoader'; F = $VaeName; K = 'vae_name' })) {
    $listed = @($oi.($pair.L).input.required.($pair.K)[0])
    if ($listed -notcontains $pair.F) { Fail "$($pair.L) does not list '$($pair.F)'. Installed options: $($listed -join ', ')" }
}

# ------------------------------------------------------------------- helpers
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds([Math]::Max(300, $TimeoutSec + 120))

function Upload-Image([string]$filePath, [string]$uploadName) {
    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $filePath))
    $multipart = New-Object System.Net.Http.MultipartFormDataContent
    $content = New-Object System.Net.Http.ByteArrayContent (, $bytes)
    $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('image/png')
    $multipart.Add($content, 'image', $uploadName)
    $multipart.Add((New-Object System.Net.Http.StringContent 'true'), 'overwrite')
    $resp = $client.PostAsync("$base/upload/image", $multipart).Result
    $body = $resp.Content.ReadAsStringAsync().Result
    if (-not $resp.IsSuccessStatusCode) { Fail "Upload of '$uploadName' failed: $($resp.StatusCode) $body" }
    $parsed = $body | ConvertFrom-Json
    if ($parsed.subfolder) { return "$($parsed.subfolder)/$($parsed.name)" }
    return $parsed.name
}

function New-QwenGraph {
    param(
        [string]$Kind,
        [string]$Prompt,
        [string]$UploadedTarget,
        [string[]]$UploadedRefs,
        [int]$Resolution,
        [int]$Width,
        [int]$Height,
        [int]$Steps,
        [int]$Seed
    )
    $g = [ordered]@{}
    $g['1'] = @{ class_type = 'UNETLoader'; inputs = @{ unet_name = $UnetName; weight_dtype = 'default' } }
    $g['2'] = @{ class_type = 'CLIPLoader'; inputs = @{ clip_name = $ClipName; type = 'qwen_image'; device = 'default' } }
    $g['3'] = @{ class_type = 'VAELoader'; inputs = @{ vae_name = $VaeName } }

    $encodeInputs = @{ clip = @('2', 0); prompt = $Prompt; negative_prompt = ''; resolution = $Resolution }
    $modelSource = @('1', 0)

    if ($Kind -eq 'imageedit') {
        $g['10'] = @{ class_type = 'LoadImage'; inputs = @{ image = $UploadedTarget; upload = 'image' } }
        $encodeInputs['vae'] = @('3', 0)
        # Autogrow references are declared as `<input id>.<template name>`, i.e. images.image_1..
        # (_io.py: expected_id = finalize_prefix(curr_prefix, name); "prefix gets added in
        # parse_class_inputs"). ComfyUI re-nests them into the `images` dict before calling
        # execute(). Two wrong shapes produce NO error and silently drop every reference (verified
        # on the host 2026-09-22): flat `image_1` kwargs -> TypeError at execute; a dict under
        # `images` -> matches no declared input, is ignored, and the encoder falls back to a
        # resolution x resolution latent. The edit cells are therefore size-validated below.
        $encodeInputs['images.image_1'] = @('10', 0)
        for ($i = 0; $i -lt $UploadedRefs.Count; $i++) {
            $nodeId = "$(20 + $i)"
            $g[$nodeId] = @{ class_type = 'LoadImage'; inputs = @{ image = $UploadedRefs[$i]; upload = 'image' } }
            $encodeInputs["images.image_$($i + 2)"] = @($nodeId, 0)
        }
        $g['4'] = @{ class_type = 'TextEncodeQwenImage21'; inputs = $encodeInputs }
        # Official edit graph: the image_1-derived latent is the sampler's latent, via QwenImage21Cache
        # (prefix KV cache) between the UNET and the sampler.
        $g['5'] = @{ class_type = 'QwenImage21Cache'; inputs = @{ model = @('1', 0); device = 'auto'; dtype = 'default' } }
        $modelSource = @('5', 0)
        $latent = @('4', 2)
    }
    else {
        $g['4'] = @{ class_type = 'TextEncodeQwenImage21'; inputs = $encodeInputs }
        # Generation WITH references: no edit target, the references are simply the first N image
        # slots and the latent comes from EmptyLatentImage (the graph the t2i template uses when
        # nothing is connected). This is the 2.1-only shape: build the scene with the identity
        # references present instead of generating a base and repairing faces afterwards.
        if ($UploadedRefs.Count -gt 0) {
            $encodeInputs['vae'] = @('3', 0)
            for ($i = 0; $i -lt $UploadedRefs.Count; $i++) {
                $nodeId = "$(20 + $i)"
                $g[$nodeId] = @{ class_type = 'LoadImage'; inputs = @{ image = $UploadedRefs[$i]; upload = 'image' } }
                $encodeInputs["images.image_$($i + 1)"] = @($nodeId, 0)
            }
        }
        $g['5'] = @{ class_type = 'EmptyLatentImage'; inputs = @{ width = $Width; height = $Height; batch_size = 1 } }
        $latent = @('5', 0)
    }

    $g['6'] = @{ class_type = 'KSampler'; inputs = @{
            model = $modelSource; positive = @('4', 0); negative = @('4', 1); latent_image = $latent
            seed = $Seed; steps = $Steps; cfg = 1.0; sampler_name = 'euler'; scheduler = 'simple'; denoise = 1.0
        } }
    $g['7'] = @{ class_type = 'VAEDecode'; inputs = @{ samples = @('6', 0); vae = @('3', 0) } }
    $g['8'] = @{ class_type = 'SaveImage'; inputs = @{ images = @('7', 0); filename_prefix = 'qwen21' } }
    return $g
}

function Invoke-Cell {
    param([string]$Name, [hashtable]$Cell, [int]$Cells_Seed, [int]$Cells_Steps)

    $outDir = Join-Path $OutRoot $Name
    if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
    Write-Host ""
    Write-Host "== $Name - $($Cell.Description)" -ForegroundColor Cyan
    Write-Host "PROMPT (verbatim):"
    Write-Host $Cell.Prompt

    $uploadedTarget = $null
    $uploadedRefs = @()
    if ($Cell.Kind -eq 'imageedit') {
        if (-not (Test-Path $TargetImage)) { Fail "Edit target not found: $TargetImage" }
        $uploadedTarget = Upload-Image -filePath $TargetImage -uploadName 'qwen21-target.png'
    }
    # References are uploaded for ANY cell that declares them - edit cells AND generation-with-
    # references cells. Scoping this to edit cells (the original bug) lets a text2image cell
    # "succeed" as a plain t2i with no references at all, which looks like a capability result.
    if ($Cell.ContainsKey('Refs') -and @($Cell.Refs).Count -gt 0) {
        foreach ($key in $Cell.Refs) {
            $r = $references[$key]
            $root = $IdentityRoot
            if ($r.ContainsKey('Root')) { $root = $r.Root }
            $full = Join-Path $root $r.RelativePath
            if (-not (Test-Path $full)) { Fail "Identity reference not found: $full" }
            $hash = (Get-FileHash -Path $full -Algorithm SHA256).Hash
            if ($hash -ne $r.Sha256) { Fail "SHA-256 mismatch for $($r.CharacterName): expected $($r.Sha256), got $hash. Refusing to test with an unverified reference." }
            $uploadedRefs += (Upload-Image -filePath $full -uploadName "qwen21-$key.png")
            Write-Host "  reference '$key' ($($r.CharacterName), $($r.View)) verified and uploaded"
        }
    }

    $graphWidth = $Resolution
    $graphHeight = $Resolution
    if ($Cell.ContainsKey('Size')) { $graphWidth = [int]$Cell.Size[0]; $graphHeight = [int]$Cell.Size[1] }
    $graph = New-QwenGraph -Kind $Cell.Kind -Prompt $Cell.Prompt -UploadedTarget $uploadedTarget -UploadedRefs $uploadedRefs `
        -Resolution $Resolution -Width $graphWidth -Height $graphHeight -Steps $Cells_Steps -Seed $Cells_Seed

    $graph | ConvertTo-Json -Depth 30 | Set-Content -Path (Join-Path $outDir 'request.json') -Encoding UTF8

    $payload = @{ prompt = $graph; client_id = 'dreamgen-qwen21-proof' } | ConvertTo-Json -Depth 30
    $content = New-Object System.Net.Http.StringContent $payload
    $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/json')
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $queueResp = $client.PostAsync("$base/prompt", $content).Result
    $queueBody = $queueResp.Content.ReadAsStringAsync().Result
    if (-not $queueResp.IsSuccessStatusCode) { Fail "ComfyUI rejected the graph ($([int]$queueResp.StatusCode)): $queueBody" }
    $queued = $queueBody | ConvertFrom-Json
    if ($queued.error) { Fail "ComfyUI returned an error: $($queued.error | ConvertTo-Json -Depth 8)" }
    $promptId = $queued.prompt_id
    Write-Host "Queued prompt_id=$promptId"

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $entry = $null
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 5
        try { $hist = Invoke-RestMethod -Uri "$base/history/$promptId" -TimeoutSec 60 } catch { continue }
        if ($hist.PSObject.Properties.Name -contains $promptId) { $entry = $hist.$promptId; break }
    }
    if ($null -eq $entry) { Fail "Timed out after ${TimeoutSec}s waiting for prompt_id $promptId." }
    $sw.Stop()
    if ($entry.status.status_str -eq 'error') {
        Write-Host ($entry.status | ConvertTo-Json -Depth 10)
        Fail "Workflow error for prompt_id $promptId"
    }
    if ($entry.status.status_str -ne 'success') { Fail "Unexpected status '$($entry.status.status_str)' for prompt_id $promptId" }

    $saved = @()
    $images = @()
    foreach ($nodeOut in $entry.outputs.PSObject.Properties) {
        foreach ($img in $nodeOut.Value.images) { if ($img) { $images += , $img } }
    }
    $idx = 0
    foreach ($img in $images) {
        $query = "filename=$([uri]::EscapeDataString($img.filename))"
        if ($img.subfolder) { $query += "&subfolder=$([uri]::EscapeDataString($img.subfolder))" }
        if ($img.type) { $query += "&type=$([uri]::EscapeDataString($img.type))" }
        $ext = [IO.Path]::GetExtension($img.filename); if (-not $ext) { $ext = '.png' }
        $outPath = Join-Path $outDir ("result_{0}{1}" -f $idx, $ext)
        [IO.File]::WriteAllBytes($outPath, $client.GetByteArrayAsync("$base/view?$query").Result)
        $saved += (Resolve-Path $outPath).Path
        $idx++
    }

    $outWidth = $Resolution
    $outHeight = $Resolution
    if ($Cell.Kind -eq 'imageedit') { $outWidth = 'from image_1'; $outHeight = 'from image_1' }
    elseif ($Cell.ContainsKey('Size')) { $outWidth = [int]$Cell.Size[0]; $outHeight = [int]$Cell.Size[1] }

    $meta = [ordered]@{
        cell          = $Name
        description   = $Cell.Description
        kind          = $Cell.Kind
        prompt        = $Cell.Prompt
        negative      = ''
        seed          = $Cells_Seed
        steps         = $Cells_Steps
        cfg           = 1.0
        sampler       = 'euler'
        scheduler     = 'simple'
        resolution    = $Resolution
        width         = $outWidth
        height        = $outHeight
        unet          = $UnetName
        text_encoder  = $ClipName
        vae           = $VaeName
        prompt_id     = $promptId
        wallClockSec  = [math]::Round($sw.Elapsed.TotalSeconds, 1)
        comfyui       = $hostInfo.system.comfyui_version
        images        = $saved
    }
    if ($Cell.Kind -eq 'imageedit') {
        $meta['target_image'] = $TargetImage
        $meta['references'] = @($Cell.Refs | ForEach-Object { $references[$_].CharacterName + ' (' + $references[$_].View + ')' })
    }
    ($meta | ConvertTo-Json -Depth 6) | Set-Content -Path (Join-Path $outDir 'meta.json') -Encoding UTF8
    foreach ($p in $saved) { Write-Host "SAVED:$p" }
    Write-Host "   $([math]::Round($sw.Elapsed.TotalSeconds,1)) s wall clock" -ForegroundColor Green
    return $saved
}

# ---------------------------------------------------------------------- run
$results = @()
foreach ($name in $Cells) {
    if (-not $cellDefs.ContainsKey($name)) { Fail "Unknown cell '$name'. Valid cells: $($cellDefs.Keys -join ', ')" }
    $results += Invoke-Cell -Name $name -Cell $cellDefs[$name] -Cells_Seed $Seed -Cells_Steps $Steps
}

$client.Dispose()
Write-Host ""
Write-Host "PASS: $(($results | Measure-Object).Count) image(s) saved under $OutRoot" -ForegroundColor Green
Write-Host "Now visually review EVERY image before drawing any conclusion."
