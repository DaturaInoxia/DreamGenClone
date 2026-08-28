#!/usr/bin/env python3
"""Build the model-agnostic position baseline catalog.

Reads the canonical juggernaut prompts (specs/image-generator-tests/juggernaut/
manifest.json) as the SDXL/Juggernaut variant, and authors the neutral scene +
Pony + Qwen variants per position. Writes one JSON per position under
baseline/positions/ plus baseline/manifest.json.

Regenerate:
  python build_baseline.py
"""
import hashlib
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
JUG = os.path.join(HERE, "juggernaut", "manifest.json")
OUT_DIR = os.path.join(HERE, "baseline", "positions")
MANIFEST = os.path.join(HERE, "baseline", "manifest.json")

# ---------------------------------------------------------------------------
# Authored per-position data. `id` must match the juggernaut manifest test id.
# - actors: 1M1F / 2F1M / 1F2M / 2F2M
# - closeup: True when the original is a penetration/action close-up
# - neutral: model-agnostic scene prose (no model-specific vocabulary)
# - pony: dense tag prompt (Pony V6 rules: quality string, rating, count tags)
# - qwen: source-image edit instruction (Qwen is an editor, not T2I)
# ---------------------------------------------------------------------------
POSITIONS = [
    dict(id="juggernaut-nsfw-69-test", actors="1M1F", closeup=False,
         neutral="Two naked adults on a bed in the 69 position: the woman on top facing the man's "
                 "feet, her head between his thighs taking his erect penis in her mouth, while the "
                 "man's head is between her thighs with his mouth on her vagina; mutual oral sex, "
                 "medium shot, correct anatomy, natural bodies and skin texture, soft warm light, "
                 "sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, 69 position, mutual oral sex, fellatio, cunnilingus, "
              "woman on top, penis in mouth, face sitting, nude, medium shot, correct anatomy, "
              "soft lighting, sharp focus",
         qwen="Reposition the two people into the 69 position on the bed: the woman on top facing "
              "the man's feet taking his erect penis into her mouth while the man's head is between "
              "her thighs performing cunnilingus. Keep both people's faces, hair, identity, skin, "
              "and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-cowgirl-test", actors="1M1F", closeup=False,
         neutral="Two naked adults on a bed: the man lying on his back, the woman straddling him "
                 "facing him in cowgirl position, lowering herself onto his erect penis which "
                 "penetrates her vagina; full genital contact, correct anatomy, natural bodies "
                 "and skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, cowgirl position, woman on top, riding, vaginal "
              "penetration, penis in vagina, man lying on back, nude, medium shot, correct anatomy, "
              "soft lighting, sharp focus",
         qwen="Change the scene so the woman straddles the man on top in cowgirl position, his "
              "erect penis penetrating her vagina. Keep both people's faces, hair, identity, "
              "bodies, skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-cowgirl-penetration-closeup-test", actors="1M1F", closeup=True,
         neutral="Two naked adults in cowgirl position on a bed; extreme close-up of their "
                 "pelvises: the man's erect penis visibly entering her vagina as she rides him, "
                 "sliding deep inside, clear penetration, correct anatomy, natural skin texture, "
                 "soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, cowgirl position, extreme close-up, penetration, "
              "penis in vagina, riding, nude, correct anatomy, soft lighting, sharp focus",
         qwen="Zoom to an extreme close-up of the couple's pelvises in cowgirl position with his "
              "erect penis clearly penetrating her vagina. Keep the people's identity, skin, and "
              "lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-missionary-test", actors="1M1F", closeup=False,
         neutral="Two naked adults on a bed in missionary position: the man on top face to face, "
                 "his erect penis penetrating her vagina; full genital contact, correct anatomy, "
                 "natural bodies and skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, missionary position, man on top, vaginal penetration, "
              "penis in vagina, face to face, nude, medium shot, correct anatomy, soft lighting, "
              "sharp focus",
         qwen="Reposition the couple into missionary position, the man on top penetrating her "
              "vagina face to face. Keep both people's faces, hair, identity, bodies, skin, and "
              "lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-missionary-penetration-closeup-test", actors="1M1F", closeup=True,
         neutral="Two naked adults in missionary position; extreme close-up of their pelvises: "
                 "the man's erect penis visibly entering her vagina, sliding deep, clear "
                 "penetration, correct anatomy, natural skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, missionary position, extreme close-up, penetration, "
              "penis in vagina, nude, correct anatomy, soft lighting, sharp focus",
         qwen="Zoom to an extreme close-up of the couple's pelvises in missionary position with "
              "his erect penis clearly penetrating her vagina. Keep identity, skin, and lighting "
              "exactly unchanged."),
    dict(id="juggernaut-nsfw-doggy-test", actors="1M1F", closeup=False,
         neutral="Two naked adults on a bed: the woman on her hands and knees with her back "
                 "arched, the man kneeling behind her, his erect penis entering her vagina from "
                 "behind with clear penetration; medium shot, correct anatomy, natural bodies and "
                 "skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, doggy style, from behind, vaginal penetration, "
              "penis in vagina, woman on hands and knees, nude, medium shot, correct anatomy, "
              "soft lighting, sharp focus",
         qwen="Change the scene to doggy style: the woman on her hands and knees with the man "
              "kneeling behind her penetrating her vagina from behind. Keep both people's faces, "
              "hair, identity, bodies, skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-doggy-penetration-closeup-test", actors="1M1F", closeup=True,
         neutral="Two naked adults in doggy style on a bed; close-up of the contact point from "
                 "behind: the man's erect penis visibly entering her vagina, sliding deep, clear "
                 "penetration, correct anatomy, natural skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, doggy style, from behind, close-up, penetration, "
              "penis in vagina, nude, correct anatomy, soft lighting, sharp focus",
         qwen="Zoom to a close-up from behind of the couple in doggy style with his erect penis "
              "clearly penetrating her vagina. Keep identity, skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-fellatio-test", actors="1M1F", closeup=False,
         neutral="Two naked adults: the woman kneeling in front of the standing man, taking his "
                 "erect penis into her mouth; fellatio, clear oral contact, correct anatomy, "
                 "natural bodies and skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, fellatio, blowjob, penis in mouth, woman kneeling, "
              "man standing, nude, medium shot, correct anatomy, soft lighting, sharp focus",
         qwen="Change the scene so the woman kneels in front of the standing man taking his erect "
              "penis into her mouth in fellatio. Keep both people's faces, hair, identity, bodies, "
              "skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-reverse-cowgirl-test", actors="1M1F", closeup=False,
         neutral="Two naked adults: the man lying on his back on the bed, the woman straddling "
                 "him facing away in reverse cowgirl, his erect penis deep inside her vagina as "
                 "she rides; medium shot, correct anatomy, natural bodies and skin texture, soft "
                 "warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, reverse cowgirl, woman on top facing away, riding, "
              "vaginal penetration, penis in vagina, nude, medium shot, correct anatomy, "
              "soft lighting, sharp focus",
         qwen="Reposition the woman to straddle the man facing away in reverse cowgirl, his erect "
              "penis deep inside her. Keep both people's faces, hair, identity, bodies, skin, and "
              "lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-reverse-cowgirl-penetration-closeup-test", actors="1M1F", closeup=True,
         neutral="Two naked adults in reverse cowgirl on a bed; close-up of the contact point: "
                 "the man's erect penis visibly entering her vagina from behind as she rides, "
                 "sliding deep, clear penetration, correct anatomy, natural skin texture, soft "
                 "warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, reverse cowgirl, close-up, penetration, penis in vagina, "
              "nude, correct anatomy, soft lighting, sharp focus",
         qwen="Zoom to a close-up of the contact point in reverse cowgirl with his erect penis "
              "clearly entering her vagina. Keep identity, skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-spooning-test", actors="1M1F", closeup=False,
         neutral="Two naked adults lying on their sides on a bed in the spooning position, both "
                 "facing the same direction, the man pressed close behind the woman, his erect "
                 "penis deep inside her vagina from behind, his hand on her hip; medium shot, "
                 "correct anatomy, natural bodies and skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, spooning, from behind, side position, vaginal penetration, "
              "penis in vagina, nude, medium shot, correct anatomy, soft lighting, sharp focus",
         qwen="Change the scene to spooning: both lying on their sides facing the same direction "
              "with the man behind penetrating her from behind. Keep both people's faces, hair, "
              "identity, bodies, skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-spooning-penetration-closeup-test", actors="1M1F", closeup=True,
         neutral="Two naked adults spooning on their sides on a bed; close-up of the contact "
                 "point: the man's erect penis visibly entering her vagina from behind, sliding "
                 "deep, clear penetration, correct anatomy, natural skin texture, soft warm light, "
                 "sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, spooning, from behind, close-up, penetration, "
              "penis in vagina, nude, correct anatomy, soft lighting, sharp focus",
         qwen="Zoom to a close-up of the contact point in spooning with his erect penis clearly "
              "entering her vagina from behind. Keep identity, skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-standing-test", actors="1M1F", closeup=False,
         neutral="Two naked adults standing facing each other: the woman's leg wrapped around the "
                 "man's hips, his erect penis penetrating her vagina; medium shot, correct "
                 "anatomy, natural bodies and skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, standing sex, standing penetration, vaginal penetration, "
              "penis in vagina, leg wrapped around hips, nude, medium shot, correct anatomy, "
              "soft lighting, sharp focus",
         qwen="Change the scene to standing sex: the couple standing facing each other, her leg "
              "wrapped around his hips, his erect penis penetrating her vagina. Keep both people's "
              "faces, hair, identity, bodies, skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-standing-penetration-closeup-test", actors="1M1F", closeup=True,
         neutral="Two naked adults standing facing each other, the woman's leg wrapped around the "
                 "man's hips; close-up of the contact point at the hip: his erect penis visibly "
                 "entering her vagina, sliding deep, clear penetration, correct anatomy, natural "
                 "skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, standing sex, close-up, penetration, penis in vagina, "
              "nude, correct anatomy, soft lighting, sharp focus",
         qwen="Zoom to a close-up at the hip of the standing couple with his erect penis clearly "
              "entering her vagina. Keep identity, skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-cumshot-facial-test", actors="1M1F", closeup=True,
         neutral="An adult man finishing on an adult woman's face; the man's face out of frame, "
                 "only his erect penis and lower torso visible; close-up of her face covered in "
                 "semen, her eyes closed; correct anatomy, natural skin texture, soft warm light, "
                 "sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, cumshot, facial, cum on face, semen on face, "
              "man's face out of frame, close-up, nude, correct anatomy, soft lighting, sharp focus",
         qwen="Change the scene so the man finishes on the woman's face, her face covered in "
              "semen. Keep her face, hair, identity, body, skin, and lighting exactly unchanged; "
              "keep the man's face out of frame."),
    dict(id="juggernaut-nsfw-cumshot-in-mouth-test", actors="1M1F", closeup=True,
         neutral="An adult man finishing in an adult woman's open mouth; the man's face out of "
                 "frame, only his erect penis and lower torso visible; close-up of her mouth "
                 "filled with semen, tongue out with cum dripping; correct anatomy, natural skin "
                 "texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, cumshot, cum in mouth, semen in mouth, tongue out, "
              "man's face out of frame, close-up, nude, correct anatomy, soft lighting, sharp focus",
         qwen="Change the scene so the man finishes in the woman's open mouth, cum dripping from "
              "her lips. Keep her face, hair, identity, body, skin, and lighting exactly unchanged; "
              "keep the man's face out of frame."),
    dict(id="juggernaut-nsfw-cumshot-on-body-test", actors="1M1F", closeup=True,
         neutral="An adult man finishing on an adult woman's chest; the man's face out of frame, "
                 "only his erect penis and lower torso visible; close-up of her breasts and "
                 "stomach covered in semen, cum dripping down her skin; correct anatomy, natural "
                 "skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, cumshot, cum on chest, cum on body, semen on body, "
              "man's face out of frame, close-up, nude, correct anatomy, soft lighting, sharp focus",
         qwen="Change the scene so the man finishes on the woman's chest, cum dripping down her "
              "skin. Keep her face, hair, identity, body, skin, and lighting exactly unchanged; "
              "keep the man's face out of frame."),
    dict(id="juggernaut-nsfw-cumshot-creampie-test", actors="1M1F", closeup=True,
         neutral="An adult man finishing inside an adult woman's vagina; the man's face out of "
                 "frame, only his erect penis and lower torso visible; close-up of his erect "
                 "penis deep inside her with semen flowing out around the base dripping onto her "
                 "thighs; correct anatomy, natural skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 1boy, 2people, creampie, cum inside, cum dripping, semen dripping, "
              "man's face out of frame, close-up, nude, correct anatomy, soft lighting, sharp focus",
         qwen="Change the scene so the man finishes inside the woman, semen flowing out around the "
              "base of his penis onto her thighs. Keep both people's bodies, identity, skin, and "
              "lighting exactly unchanged; keep the man's face out of frame."),
    # ---- 2F1M (two women, one man) ----
    dict(id="juggernaut-nsfw-mff-cowgirl-cunnilingus-test", actors="2F1M", closeup=False,
         neutral="Three naked adults on a bed: one woman straddling the man riding his erect "
                 "penis facing him while a second woman lies beneath her performing cunnilingus "
                 "on the first woman's vagina; medium shot, correct anatomy, natural bodies and "
                 "skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "2girls and 1boy, 3people, threesome, cowgirl, riding, cunnilingus, "
              "vaginal penetration, penis in vagina, nude, medium shot, correct anatomy, "
              "soft lighting, sharp focus",
         qwen="Change the scene to a threesome: one woman riding the man's erect penis while a "
              "second woman performs cunnilingus on her. Keep all people's faces, hair, identity, "
              "bodies, skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-mff-cowgirl-cunnilingus-closeup-test", actors="2F1M", closeup=True,
         neutral="Three naked adults; close-up of the central contact: the man's erect penis deep "
                 "inside the riding woman's vagina while the second woman's mouth is on her "
                 "clitoris; clear penetration, correct anatomy, natural skin texture, soft warm "
                 "light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "2girls and 1boy, 3people, threesome, close-up, cunnilingus, penetration, "
              "penis in vagina, nude, correct anatomy, soft lighting, sharp focus",
         qwen="Zoom to a close-up of the threesome's central contact. Keep all people's identity, "
              "skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-mff-cowgirl-facesitting-test", actors="2F1M", closeup=False,
         neutral="Three naked adults on a bed: the man lying on his back, the first woman riding "
                 "his erect penis in cowgirl while the second woman sits on the man's face facing "
                 "the first woman; medium shot, correct anatomy, natural bodies and skin texture, "
                 "soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "2girls and 1boy, 3people, threesome, cowgirl, riding, facesitting, "
              "vaginal penetration, penis in vagina, nude, medium shot, correct anatomy, "
              "soft lighting, sharp focus",
         qwen="Change the scene so one woman rides the man's erect penis while the second woman "
              "sits on his face. Keep all people's faces, hair, identity, bodies, skin, and "
              "lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-mff-double-fellatio-test", actors="2F1M", closeup=False,
         neutral="Three naked adults: the man standing, two women kneeling side by side in front "
                 "of him, both taking his erect penis into their mouths together; double fellatio, "
                 "medium shot, correct anatomy, natural bodies and skin texture, soft warm light, "
                 "sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "2girls and 1boy, 3people, double fellatio, blowjob, penis in mouth, two girls, "
              "nude, medium shot, correct anatomy, soft lighting, sharp focus",
         qwen="Change the scene to double fellatio: two women kneeling side by side taking the "
              "man's erect penis into their mouths together. Keep all people's faces, hair, "
              "identity, bodies, skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-mff-double-fellatio-closeup-test", actors="2F1M", closeup=True,
         neutral="Three naked adults; close-up of double fellatio: the man's erect penis with "
                 "both women's mouths on it together; correct anatomy, natural skin texture, soft "
                 "warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "2girls and 1boy, 3people, double fellatio, blowjob, penis in mouth, close-up, "
              "nude, correct anatomy, soft lighting, sharp focus",
         qwen="Zoom to a close-up of the man's penis with both women's mouths on it. Keep all "
              "people's identity, skin, and lighting exactly unchanged."),
    # ---- 1F2M (one woman, two men) ----
    dict(id="juggernaut-nsfw-mmf-cowgirl-fellatio-test", actors="1F2M", closeup=False,
         neutral="Three naked adults on a bed: the woman straddling the first man riding his "
                 "erect penis while leaning forward to take the second man's erect penis into her "
                 "mouth, cowgirl with fellatio; medium shot, correct anatomy, natural bodies and "
                 "skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 2boys, 3people, threesome, cowgirl, riding, fellatio, "
              "vaginal penetration, penis in vagina, penis in mouth, nude, medium shot, "
              "correct anatomy, soft lighting, sharp focus",
         qwen="Change the scene so the woman rides the first man's erect penis while taking the "
              "second man's erect penis into her mouth. Keep all people's faces, hair, identity, "
              "bodies, skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-mmf-double-penetration-test", actors="1F2M", closeup=False,
         neutral="Three naked adults on a bed: the woman on her hands and knees, one man kneeling "
                 "behind her penetrating her vagina while the second man kneels behind her "
                 "penetrating her anus, double penetration; medium shot, correct anatomy, natural "
                 "bodies and skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 2boys, 3people, double penetration, threesome, anal and vaginal "
              "penetration, from behind, nude, medium shot, correct anatomy, soft lighting, "
              "sharp focus",
         qwen="Change the scene to double penetration: one man penetrating her vagina while the "
              "second penetrates her anus from behind. Keep all people's faces, hair, identity, "
              "bodies, skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-mmf-double-penetration-closeup-test", actors="1F2M", closeup=True,
         neutral="Three naked adults; extreme close-up of the contact point: the first man's "
                 "erect penis entering her vagina while the second man's erect penis enters her "
                 "anus, both deep inside, clear double penetration, correct anatomy, natural skin "
                 "texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 2boys, 3people, double penetration, extreme close-up, anal and vaginal "
              "penetration, nude, correct anatomy, soft lighting, sharp focus",
         qwen="Zoom to an extreme close-up of the double penetration. Keep all people's identity, "
              "skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-mmf-spitroast-test", actors="1F2M", closeup=False,
         neutral="Three naked adults on a bed: the woman on all fours performing fellatio on the "
                 "man in front of her while the man behind penetrates her vagina, spitroast, the "
                 "front man holding her head; medium shot, correct anatomy, natural bodies and "
                 "skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 2boys, 3people, spitroast, threesome, fellatio, vaginal penetration, "
              "penis in mouth, penis in vagina, nude, medium shot, correct anatomy, soft lighting, "
              "sharp focus",
         qwen="Change the scene to a spitroast: the woman performing fellatio on the man in front "
              "while the man behind penetrates her vagina. Keep all people's faces, hair, "
              "identity, bodies, skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-mmf-spitroast-closeup-test", actors="1F2M", closeup=True,
         neutral="Three naked adults; close-up of a spitroast: the woman's mouth taking the front "
                 "man's erect penis while the rear man's erect penis enters her vagina from "
                 "behind; clear penetration, correct anatomy, natural skin texture, soft warm "
                 "light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 2boys, 3people, spitroast, close-up, fellatio, vaginal penetration, "
              "penis in mouth, penis in vagina, nude, correct anatomy, soft lighting, sharp focus",
         qwen="Zoom to a close-up of the spitroast. Keep all people's identity, skin, and lighting "
              "exactly unchanged."),
    # ---- 2F2M (orgy) ----
    dict(id="juggernaut-nsfw-orgy-four-way-test", actors="2F2M", closeup=False,
         neutral="Four naked adults in a group orgy on a bed, four distinct separate bodies: the "
                 "first woman straddling the first man riding his erect penis while the second "
                 "woman kneels performing fellatio on the second man's erect penis; medium shot, "
                 "correct anatomy, natural bodies and skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "2girls and 2boys, 4people, orgy, group sex, cowgirl, riding, fellatio, "
              "vaginal penetration, penis in vagina, penis in mouth, nude, medium shot, "
              "correct anatomy, soft lighting, sharp focus",
         qwen="Change the scene to a four-person orgy as described. Keep all people's faces, "
              "hair, identity, bodies, skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-orgy-double-penetration-test", actors="2F2M", closeup=False,
         neutral="Four naked adults in a group orgy on a bed, four distinct separate bodies: the "
                 "first woman on her hands and knees double penetrated by the two men while the "
                 "second woman kneels performing fellatio on one of the men; medium shot, correct "
                 "anatomy, natural bodies and skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "2girls and 2boys, 4people, orgy, group sex, double penetration, fellatio, "
              "anal and vaginal penetration, penis in mouth, nude, medium shot, correct anatomy, "
              "soft lighting, sharp focus",
         qwen="Change the scene to a four-person orgy with double penetration as described. Keep "
              "all people's faces, hair, identity, bodies, skin, and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-orgy-four-way-closeup-test", actors="2F2M", closeup=True,
         neutral="Four naked adults; close-up of a group orgy's central action: a man's erect "
                 "penis entering a woman's vagina while another man's erect penis enters her anus "
                 "and a second woman performs fellatio; clear penetration, correct anatomy, "
                 "natural skin texture, soft warm light, sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "2girls and 2boys, 4people, orgy, group sex, close-up, double penetration, fellatio, "
              "anal and vaginal penetration, penis in mouth, nude, correct anatomy, soft lighting, "
              "sharp focus",
         qwen="Zoom to a close-up of the orgy's central action. Keep all people's identity, skin, "
              "and lighting exactly unchanged."),
    dict(id="juggernaut-nsfw-cumshot-double-facial-test", actors="2M1F", closeup=True,
         neutral="Exactly two adult men standing over one kneeling adult woman, both men's faces "
                 "out of frame; both finishing on the woman's face, close-up of her face covered "
                 "in semen from both; correct anatomy, natural skin texture, soft warm light, "
                 "sharp focus.",
         pony="score_9, score_8_up, score_7_up, score_6_up, score_5_up, score_4_up, rating_explicit, "
              "1girl and 2boys, 3people, double cumshot, facial, cum on face, cumshot, "
              "men's faces out of frame, close-up, nude, correct anatomy, soft lighting, sharp focus",
         qwen="Change the scene so two men finish on the kneeling woman's face. Keep her face, "
              "hair, identity, body, skin, and lighting exactly unchanged; keep the men's faces "
              "out of frame."),
]

# ----- helpers -------------------------------------------------------------
def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main():
    with open(JUG, encoding="utf-8") as f:
        jug = {t["id"]: t for t in json.load(f)["tests"]}

    os.makedirs(OUT_DIR, exist_ok=True)
    manifest_positions = []

    for p in POSITIONS:
        src = jug.get(p["id"])
        if src is None:
            print(f"WARN: {p['id']} not in juggernaut manifest; using null SDXL variant")
        entry = {
            "id": p["id"],
            "title": p["id"].replace("juggernaut-nsfw-", "").replace("-test", "").replace("-", " ").strip(),
            "actors": p["actors"],
            "closeup": p["closeup"],
            "neutralScene": p["neutral"],
            "negative": src["negative"] if src else "",
            "variants": {
                "sdxl-juggernaut": src["prompt"] if src else "",
                "pony": p["pony"],
                "qwen-edit": p["qwen"],
            },
            "settings": {
                "seed": src["seed"] if src else None,
                "steps": src["steps"] if src else 30,
                "cfg": src["cfg"] if src else 5.0,
                "sampler": src["sampler"] if src else "dpmpp_2m_sde",
                "scheduler": src["scheduler"] if src else "karras",
                "denoise": 1.0,
                "width": src["width"] if src else 1024,
                "height": src["height"] if src else 1024,
            },
            "source": {"suite": "juggernaut", "workflow": f"juggernaut/prompts/{p['id']}.json"},
        }
        path = os.path.join(OUT_DIR, p["id"] + ".json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(entry, f, indent=2, ensure_ascii=False)
        manifest_positions.append({
            "id": p["id"], "path": f"positions/{p['id']}.json",
            "actors": p["actors"], "closeup": p["closeup"],
            "bytes": os.path.getsize(path), "sha256": sha256(path),
        })
        print("wrote", os.path.relpath(path, HERE))

    manifest = {
        "suite": "baseline-positions",
        "purpose": "Model-agnostic sexual position/act prompt catalog. Each entry carries a "
                   "neutral scene description plus per-model variants (SDXL/Juggernaut natural "
                   "language, Pony V6 tags, Qwen edit instruction). Consumed by model-specific "
                   "test suites and the app prompt builders.",
        "actorsLegend": {"1M1F": "1 man + 1 woman", "2F1M": "2 women + 1 man",
                         "1F2M": "1 woman + 2 men", "2F2M": "2 women + 2 men",
                         "2M1F": "2 men + 1 woman"},
        "count": len(POSITIONS),
        "positions": manifest_positions,
    }
    with open(MANIFEST, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"manifest written: {len(POSITIONS)} positions -> baseline/manifest.json")


if __name__ == "__main__":
    main()
