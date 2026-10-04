"""Give every prompt-catalog field a standard camera clause (framing + angle).

Scope: the prompt catalogs under `specs/image-generator-tests` whose manifests declare `positions` — i.e. the catalogs
whose files carry per-model prompt text (`baseline`, `sfw-baseline`, `character-lora-distance`). Evidence/ComfyUI payload
suites are NOT touched: they are frozen records of a run, not prompts to author. A new authoring catalog is added to
SUITES below, because the guard discovers catalogs dynamically and this script does not.

Rules, applied per position:
  * framing: kept when the text already names one; otherwise taken from the position's own `closeup` flag
    (close-up when true, medium shot when false).
  * angle: kept when the text already names one; otherwise the position's STANDARD angle from ANGLE_BY_POSITION
    (bed acts at bed height, contact-point shots at hip height, everything else at eye level).
  * direction: never invented. Naming a camera direction re-composes the shot, so it is only ever left as written.
  * Pony takes the danbooru `eye level` tag beside its other view tags — its documented failure mode is silently
    defaulting to overhead/top-down when no angle tag is given.
  * prose dialects (`expected`, `neutralScene`, biglust/juggernaut/flux/qwen/krea2) take the phrase "<framing> at <height>",
    placed at the end of the prompt's own camera phrase when it has one, inside FLUX's labelled `Camera:` sentence,
    and otherwise in front of the optics tail (`35mm`, `sharp focus`).
  * EDIT variants (`qwen-edit-2511`, `qwen-image-2.1-edit`) are untouched: an edit instruction preserves the
    existing framing rather than restating it.

Usage:
    python normalize-suite-camera.py                    # dry run, prints the planned insertion with its context
    python normalize-suite-camera.py --apply            # writes the files (raw-text replacement, formatting preserved)
    python normalize-suite-camera.py --show <position>  # full before/after text for one position

The guard is `DreamGenClone.Tests/RolePlay/PromptSuiteCameraClauseTests.cs`: it fails if any catalog prompt names no
framing and/or angle, so this script is a one-time (or new-position) pass rather than something the catalogs depend on.
Its `FRAMING`/`ANGLE` patterns are the same standard set the guard carries — when a prompt is phrased with a camera word
neither list has, EXTEND both (a graded form of a listed word counts as that word's statement: "slightly above" is "from
above" said more precisely) rather than rewriting the author's wording.
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SUITES = ["baseline", "sfw-baseline", "character-lora-distance"]
PROSE_FIELDS = ("expected", "neutralScene", "biglust", "juggernaut", "flux", "qwen-image-2.1", "krea2")
EDIT_VARIANTS = {"qwen-edit-2511", "qwen-image-2.1-edit"}

FRAMING = re.compile(
    r"extreme close-?up|close-?up|close shot|macro shot|macro|medium close[- ]?up|medium close shot|"
    r"medium full shot|medium shot|full[- ]body|full shot|wide shot|cowboy shot|upper body|knees[- ]up|"
    r"headshot|portrait|framed (?:close|tight|tightly|on|at|between|from|across)",
    re.I,
)
ANGLE = re.compile(
    r"eye level|camera angle|low camera|high camera|low angle|high angle|overhead|top[- ]down|from above|"    r"slightly above|slightly below|"    r"from below|from floor|floor level|ground level|bed height|hip height|chest height|shoulder height|"
    r"waist height|table height|counter height|knee height|worm|bird",
    re.I,
)
FRAMING_TAG = re.compile(
    r"extreme close-?up|close-?up|medium shot|full body|wide shot|cowboy shot|upper body|portrait",
    re.I,
)
# Pony's view tags: the angle belongs beside these, not beside whatever framing tag happens to come first.
VIEW_TAG = re.compile(
    r"front view|back view|rear view|side view|from behind|from the side|from the front|from above|from below|"
    r"three quarter view|three-quarter view|profile|over the shoulder|eye level|low angle|high angle|overhead|pov",
    re.I,
)
# The FLUX dialect labels its camera sentence, so the clause belongs inside it rather than in the lighting run.
FLUX_CAMERA = re.compile(r"Camera:\s*([^.]*)\.", re.I)

# Where the prompt states its camera: an UNAMBIGUOUS framing word. Deliberately narrower than FRAMING, which is an
# audit test: body facts like "her knees up" or "her full body" are not camera statements and must never be an
# insertion point.
CAMERA_PHRASE = re.compile(
    r"(?:extreme close-?up|medium close-?up|medium close shot|medium full shot|close-?up|close shot|"
    r"medium shot|full body shot|full shot|wide shot|cowboy shot|upper body shot|upper body|headshot|portrait|"
    r"macro shot|macro)",
    re.I,
)
STRICT_DIRECTION = re.compile(
    r"(?:from behind|from the side|from between|from above|from below|from directly|from the front|from across|"
    r"three[- ]quarter[a-z ]*?angle|side view|rear view|front view|back view|over[- ]the[- ]shoulder|"
    r"facing away|facing the camera)"
    r"(?:\s+(?:her|his|their|the)\b[^.,;]{0,30})?",
    re.I,
)
# Only the OPTICS are a style tail. Body descriptors ("unretouched skin", "natural body proportions") appear in the
# middle of a caption and must never be used as an insertion point.
OPTICS = re.compile(
    r"\b(?:35mm|50mm|85mm|sharp focus|shallow depth of field|film grain|8K|highly detailed|crystal clear)\b",
    re.I,
)

# The standard angle per position. Bed-frame acts read at bed height, contact-point shots at the height of the
# contact, and everything shot from a standing eye line reads at eye level (the neutral every family defaults to when
# it is told nothing, and the one Pony must be given explicitly or it goes overhead).
ANGLE_BY_POSITION = {
    "69": "at bed height",
    "cowgirl": "at bed height",
    "cowgirl-penetration-closeup": "at bed height",
    "missionary": "at bed height",
    "missionary-penetration-closeup": "at bed height",
    "doggy": "at bed height",
    "doggy-penetration-closeup": "at bed height",
    "reverse-cowgirl": "at bed height",
    "reverse-cowgirl-penetration-closeup": "at bed height",
    "spooning": "at bed height",
    "spooning-penetration-closeup": "at bed height",
    "standing-penetration-closeup": "at hip height",
    "erotic-masturbating-on-back": "at bed height",
    "erotic-legs-spread": "at bed height",
    "erotic-hands-and-knees": "at bed height",
    "flash-upskirt-sitting": "at hip height",
}
DEFAULT_ANGLE = "at eye level"


def piece_for(position_id: str, closeup: bool, text: str) -> str:
    """The camera wording to add to one prose field: the height, the framing, or both, or '' when it is complete."""
    has_framing, has_angle = bool(FRAMING.search(text)), bool(ANGLE.search(text))
    if has_framing and has_angle:
        return ""
    angle = ANGLE_BY_POSITION.get(position_id, DEFAULT_ANGLE)
    if has_framing:
        return angle
    if has_angle:
        return _framing_for(closeup)
    return f"{_framing_for(closeup)} {angle}"


def _framing_for(closeup: bool) -> str:
    return "close-up" if closeup else "medium shot"


def _camera_phrase_end(body: str) -> int | None:
    """Where the prompt's own camera phrase ends, or None when the piece belongs elsewhere.

    The phrase is the framing word plus the longest run of up to four following words that ENDS at a clause boundary
    (comma, period or end of text). Requiring the boundary is what keeps "medium close shot from between her knees" an
    anchor while "Medium shot from a raised three-quarter angle" is not — the latter has no boundary inside the run, so
    it is not treated as a camera phrase at all and the piece goes to the style tail instead.
    """
    match = CAMERA_PHRASE.search(body)
    if not match:
        direction = STRICT_DIRECTION.search(body)
        return direction.end() if direction else None

    for extra in range(4, -1, -1):
        trial = match.end()
        consumed = re.match(r"(?:\s+\S+){0,%d}" % extra, body[trial:])
        trial += consumed.end()
        if trial >= len(body) or body[trial] in ",.;":
            return trial
    return None


def insert_prose(text: str, piece: str) -> str:
    body = text.rstrip()
    if not piece:
        return text
    if piece.lower() in body.lower():
        return text

    # FLUX states its camera in a labelled sentence. Put it there, and if the field has no such sentence yet, CLOSE
    # the caption with one: the dialect's own shape, rather than the angle buried in its style list.
    flux_shape = FLUX_CAMERA.search(body) or re.search(r"(?:Critical style|Context|Primary subject):", body, re.I)
    if flux_shape:
        existing = FLUX_CAMERA.search(body)
        if existing:
            end = existing.end(1)
            return f"{body[:end]}, {piece}{body[end:]}"
        framing = CAMERA_PHRASE.search(body)
        camera_line = f"{framing.group(0)} {piece}" if framing and not FRAMING.match(piece) else piece
        closing = "" if body.endswith(".") else "."
        return f"{body}{closing} Camera: {camera_line}."

    end = _camera_phrase_end(body)
    if end is not None:
        return f"{body[:end]}, {piece}{body[end:]}"

    optics = OPTICS.search(body)
    if optics and optics.start() > 0:
        cut = max(
            body.rfind(",", 0, optics.start()),
            body.rfind(";", 0, optics.start()),
            body.rfind(".", 0, optics.start()),
        )
        # A period boundary means the optics open a NEW sentence: attach the piece to the first list item inside it
        # ("Shallow depth of field with sharp focus on the contact point, at bed height, 35mm macro lens.").
        if cut > 0 and body[cut] == ".":
            comma = body.find(",", optics.end())
            if comma > 0:
                return f"{body[: comma + 1]} {piece},{body[comma + 1 :]}"
        elif cut > 0:
            return f"{body[: cut + 1]} {piece},{body[cut + 1 :]}"
        head = body[: optics.start()].rstrip().rstrip(",")
        return f"{head}, {piece}, {body[optics.start():]}"

    # Nothing to attach to: a framing-and-angle piece closes the caption as its own sentence.
    if body.endswith("."):
        return f"{body[:-1].rstrip().rstrip(',')}. {piece[0].upper()}{piece[1:]}."
    if FRAMING.match(piece):
        return f"{body.rstrip(',')}. {piece[0].upper()}{piece[1:]}."
    return f"{body}, {piece}"


def insert_pony(text: str) -> str:
    """Pony: add the explicit angle tag, and a framing tag when the prompt names none."""
    body = text.rstrip().rstrip(".")
    tags = []
    if not ANGLE.search(body):
        tags.append("eye level")
    if not FRAMING_TAG.search(body):
        tags.append("full body" if "full body" in body.lower() else "medium shot")

    for tag in tags:
        views = list(VIEW_TAG.finditer(body))
        if views:
            last = views[-1]
            after = body[last.end() :]
            body = f"{body[: last.end()]}, {tag}{after}" if after.startswith(",") else f"{body[: last.end()]}, {tag}, {after.lstrip(', ')}"
            continue
        match = FRAMING_TAG.search(body)
        if match:
            after = body[match.end() :]
            body = f"{body[: match.end()]}, {tag}{after}" if after.startswith(",") else f"{body[: match.end()]}, {tag}, {after.lstrip(', ')}"
            continue
        tail = re.search(r"\b(?:sharp focus|natural skin texture)\b", body, re.I)
        body = f"{body[: tail.start()].rstrip().rstrip(',')}, {tag}, {body[tail.start():]}" if tail and tail.start() > 0 else f"{body}, {tag}"
    return body if body == text.rstrip() else body + ("." if text.rstrip().endswith(".") else "")


def main() -> int:
    apply = "--apply" in sys.argv
    show = sys.argv[sys.argv.index("--show") + 1] if "--show" in sys.argv else None
    changed_files = 0
    changes = []

    for suite in SUITES:
        base = ROOT / "specs" / "image-generator-tests" / suite
        manifest = json.loads((base / "manifest.json").read_text(encoding="utf-8"))
        for entry in manifest.get("positions") or []:
            path = base / entry["path"]
            raw = path.read_text(encoding="utf-8")
            data = json.loads(raw)
            # From the POSITION FILE, which is where the flag actually lives and the file the importer reads. Reading it
            # off the manifest entry gave every close-up position in a manifest that does not repeat the flag a "medium
            # shot" framing - a close rung handed a medium framing is the opposite of its own intent, and the manifest
            # is not the record of it (nothing keeps a duplicated flag in step).
            closeup = bool(data.get("closeup"))
            edits = []
            for field in PROSE_FIELDS:
                old = data.get(field) if field in ("expected", "neutralScene") else (data.get("variants") or {}).get(field)
                if not old:
                    continue
                new = insert_prose(old, piece_for(entry["id"], closeup, old))
                if new != old:
                    edits.append((field, old, new))
            pony = (data.get("variants") or {}).get("pony")
            if pony:
                new_pony = insert_pony(pony)
                if new_pony != pony:
                    edits.append(("pony", pony, new_pony))

            if not edits:
                continue
            changed_files += 1
            for field, old, new in edits:
                encoded_old = json.dumps(old)
                if raw.count(encoded_old) != 1:
                    print(f"  !! {path.name} {field}: {raw.count(encoded_old)} occurrences in the raw file, skipped")
                    continue
                raw = raw.replace(encoded_old, json.dumps(new))
                changes.append((suite, entry["id"], field, old, new))
                if show == entry["id"]:
                    print(f"--- {suite}/{entry['id']} {field}\nBEFORE: {old}\nAFTER : {new}\n")

            if apply:
                json.loads(raw)  # must still parse before it is written
                path.write_text(raw, encoding="utf-8")

    for suite, position, field, old, new in changes:
        prefix = 0
        while prefix < min(len(old), len(new)) and old[prefix] == new[prefix]:
            prefix += 1
        window = new[max(0, prefix - 70) : prefix + 90].replace("\n", " ")
        print(f"{suite:<12} {position:<34} {field:<14} ...{window}...")
    print(f"\n{len(changes)} field(s) across {changed_files} file(s); {'APPLIED' if apply else 'DRY RUN (pass --apply)'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
