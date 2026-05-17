using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class RPPositionSeedService
{
    private static readonly SeedEntry[] SeedEntries =
    [
        new(
            "Missionary",
            "Low",
            "Face-to-face, man on top, traditional intimate position.",
            "The man lies atop the woman with both facing each other. Her legs may be flat or raised around his hips. He controls the pace and depth directly; eye contact and kissing are natural. Emotional intimacy runs high. Transition into this position by lying her back and settling between her thighs."),

        new(
            "Doggy Style",
            "Medium",
            "Woman on hands and knees, man enters from behind.",
            "The woman is on all fours â€” hands and knees â€” while the man kneels or stands behind her. He grips her hips for leverage and control. The angle allows deep penetration and strong rhythm. Eye contact is absent, making it more primal and intensity-forward. Hair pulling, back grabbing, and spoken commands fit naturally."),

        new(
            "Cowgirl",
            "Medium",
            "Woman straddles the man and controls movement, both facing each other.",
            "The man lies on his back as the woman straddles his hips, knees on either side. She sets the pace, depth, and angle entirely. Eye contact and chest contact are easy. She can lean forward for grinding or sit upright for bouncing. The position signals her confidence and willingness to take control of pleasure."),

        new(
            "Reverse Cowgirl",
            "Medium",
            "Woman straddles the man facing away, controlling movement.",
            "The woman straddles the man with her back turned to him, facing his feet. She controls the grind and bounce while he grips her hips or back. He watches from behind. The position signals boldness and a deliberate withholding of eye contact. Tension is high; connection is physical rather than emotional."),

        new(
            "Spooning",
            "Low",
            "Both lying on their sides, man enters from behind.",
            "Both partners lie on their sides in the same direction; the man curves behind her and enters from behind. His arm wraps around her torso or breast. Movement is shallow and rhythmically gentle. The position is tender and close â€” suited to a slow, intimate build-up or quiet early-morning encounters. Whispered dialogue fits well."),

        new(
            "Lotus",
            "Low",
            "Man sits cross-legged; woman sits in his lap facing him, legs wrapped around.",
            "The man sits cross-legged or with legs extended; the woman lowers onto him and wraps her legs around his waist. Faces are inches apart. Movement is rocking rather than thrusting â€” deep but measured. This is one of the most emotionally intimate positions; it is deliberate, slow, and full of eye contact and breath. Suited to high-connection moments."),

        new(
            "Standing",
            "Medium",
            "Both partners standing, typically man behind or face-to-face against a surface.",
            "The couple stands upright â€” either facing each other or him behind her. She may lean against a wall, counter, or door frame for support. The position is urgent and location-flexible; it signals spontaneity and desire that could not wait. Clothing may still be half on. Height differences may require adjustment or improvisation."),

        new(
            "Scissors",
            "Low",
            "Partners lie on their sides at an angle, legs interlocked.",
            "Both partners lie on their sides at a diagonal, legs scissored together so he can enter her from a side angle. The rhythm is a rocking grind rather than thrusting. Intimacy is close; hands are free to roam. The position is less forceful and more sensuous â€” suited to a slower, exploratory phase of a scene."),

        new(
            "Piledriver",
            "High",
            "Woman's legs pushed back over her head; man thrusts downward.",
            "She lies on her back with her legs pushed far back and over, hips lifting off the surface. He kneels or stands over her, thrusting downward at a steep angle. The position is anatomically intense and signals full surrender of control. Penetration is very deep. Eye contact is possible. Reserved for scenes with high desire and low self-respect alignment."),

        new(
            "Face-Sitting",
            "High",
            "Woman sits on or over the man's face for oral intimacy.",
            "The woman positions herself over the man's face â€” kneeling or fully sitting â€” for oral contact. She controls pressure, angle, and pace entirely. The position is one of the most dominant acts available to her; the man is beneath and serving. Her thighs frame his head. It can be used as a dominant power-display or as a reward sequence in escalated scenes."),

        new(
            "Side-by-Side (Face to Face)",
            "Low",
            "Partners lie facing each other on their sides for intimate, unhurried connection.",
            "Both partners lie on their sides facing each other, bodies pressed close. He enters her with her upper leg raised slightly. Thrusting depth is moderate; the position prizes intimacy and sustained eye contact over intensity. Kissing, whispering, and hand contact on face or hair are natural. Ideal for emotionally loaded or post-escalation tender scenes."),

        new(
            "Seated Lap",
            "Low",
            "Woman sits in the man's lap facing him while he is seated on a chair or edge.",
            "The man sits on a chair, bed edge, or couch while she lowers onto his lap facing him, legs wrapped around his waist or feet flat on the floor. She rides him at her chosen pace. Eye contact is constant. The setting implies intimacy and choice â€” she chose to come to him. The position works well for conversation woven into the encounter."),

        new(
            "Against the Wall",
            "Medium",
            "Woman pressed against a vertical surface; man enters from front or behind.",
            "She is pressed against a wall â€” face-first or back-first â€” while he enters from behind or lifts her slightly. The wall provides resistance for strong thrusting. The position is urgent and spatially opportunistic. Breath against the neck, hands on the wall, trapped sensations all read as intense. Works for hallways, showers, doorframes, or bedroom walls."),

        new(
            "Over the Edge",
            "Medium",
            "Woman bent over a surface edge (bed, table, counter); man behind.",
            "She bends forward over the edge of a surface â€” bed, desk, table, kitchen counter â€” while he stands behind her and enters. He can grip her hips, shoulders, or hair. The position is utilitarian in the best way: fast, direct, powerful. The furniture doubles as a staging prop. Location-setting lines about the surface add texture."),

        new(
            "Butterfly (Legs Raised)",
            "High",
            "Woman on her back, hips at edge of surface, legs raised and held.",
            "She lies at the edge of a bed or table with her hips near the edge; he stands or kneels and enters while holding or pushing her legs upward and back. The angle is steep and deep. He controls everything from stance. Her arms may reach back to grip the surface. The position pairs well with scenes where physical dominance is established through the geometric advantage."),

        new(
            "Bridge",
            "High",
            "Woman in a back-arch bridge position; man enters from above or kneels.",
            "She arches her back off the surface, supported on hands and feet or shoulders and feet, creating a bridge. He enters from a kneeling or standing angle. The position requires physical effort from her and signals complete openness and vulnerability of the body. Use it as a climactic physical display; narrative lines about effort and strain add realism."),

        new(
            "Prone Bone",
            "High",
            "Woman lies flat on her stomach; man lies atop her from behind.",
            "She lies fully flat on her stomach with legs together or slightly apart. He lies atop her from behind and enters with a downward-angled thrust. The tight leg position increases pressure and friction. Her face is in the pillow or turned to the side. His weight is over her. The position is primal and possessive; it limits her movement entirely."),

        new(
            "Wheelbarrow",
            "High",
            "Man holds woman's hips aloft while she supports on her hands.",
            "She is on her hands (or forearms) on the floor or surface; he stands behind her and lifts her hips to his level, holding her thighs or hips aloft. She is suspended from the waist down. The position is athletically demanding and brief by nature â€” suited to a burst of high-intensity action within a longer scene rather than a sustained sequence."),

        new(
            "Amazon",
            "High",
            "Woman straddles man who lies with knees bent; she faces him and leans forward.",
            "The man lies on his back with his knees bent and raised; she straddles him from above, leaning forward and anchoring against his raised legs. She controls depth and rhythm fully. The position reverses typical power geometry â€” she is anatomically on top and physically dominant. Use it when she is explicitly directing or claiming control of the encounter."),

        new(
            "Chair Straddle",
            "Medium",
            "Woman straddles the man seated in a chair, facing toward or away.",
            "He sits upright in a firm chair; she straddles him, either facing him with knees on the seat edges or facing away with feet on the floor. Depth of motion depends on her leg positioning. The chair back gives him something to grip. The scene has a domestic-opportunistic quality â€” a chair in a kitchen, study, or living room elevates normal settings."),

        new(
            "Kneeling Behind",
            "Medium",
            "Both kneeling; man enters from behind while she kneels upright.",
            "Both partners kneel upright on the surface â€” bed, floor, cushion. He enters from behind while she kneels with her back against his chest. He can reach around for additional contact. Eye contact is possible via glance back over the shoulder. The position is more vertical than doggy style and carries a different emotional register: closer, more encompassing."),

        new(
            "Standing Rear Entry",
            "Medium",
            "Both standing; man enters from behind while she bends slightly forward.",
            "Both partners stand. She bends forward slightly â€” hands on knees, a ledge, or hanging â€” while he enters from behind while standing. The height match matters; heels or a step may be used. The position is fast to transition into from standing and works in shower scenes, against kitchen counters, or in cramped spaces. Movement is driven by him with strong hip contact."),

        new(
            "Cradle",
            "Medium",
            "Man kneels; woman lies back with hips raised into his lap.",
            "She lies on her back with her hips lifted into his lap as he kneels between her thighs. He holds her lower back or hips elevated. Her legs may rest on his shoulders or wrap around his torso. The position combines deep penetration with his visual advantage over her; she is partially suspended and yielding. Pacing is his to dictate."),

        new(
            "69 (Mutual Oral)",
            "Medium",
            "Partners simultaneously give and receive oral stimulation, bodies reversed.",
            "Both partners align in opposite directions â€” one atop the other or side-by-side â€” so each can simultaneously access the other for oral contact. It is inherently reciprocal and high-engagement. Use it as a transitional beat between escalating stages rather than as a primary sustained sequence. Dialogue is impossible; sound and sensation dominate."),

        new(
            "Suspended (Lifted)",
            "High",
            "Man lifts the woman fully off the ground during intercourse.",
            "He lifts her fully off the ground, supporting her weight with his hands under her thighs or buttocks. She wraps her legs around his hips and arms around his shoulders or neck. He thrusts while bearing her full weight. The position is a physical display of strength and desire â€” brief, intense, and memorable. It works best as a charged transitional beat rather than a prolonged sequence.")
    ];

    private readonly IRPThemeService _rpThemeService;
    private readonly ILogger<RPPositionSeedService> _logger;

    public RPPositionSeedService(IRPThemeService rpThemeService, ILogger<RPPositionSeedService> logger)
    {
        _rpThemeService = rpThemeService;
        _logger = logger;
    }

    public async Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _rpThemeService.ListPositionsAsync(includeDisabled: true, cancellationToken);
        if (existing.Count == 0)
        {
            var sortOrder = 0;
            foreach (var entry in SeedEntries)
            {
                await _rpThemeService.SavePositionAsync(new RPPosition
                {
                    Name = entry.Name,
                    ShortDescription = entry.ShortDescription,
                    DetailedDescription = entry.DetailedDescription,
                    EscalationTier = entry.Tier,
                    SortOrder = sortOrder++,
                    IsEnabled = true
                }, cancellationToken);
            }
            _logger.LogInformation("Seeded RP position catalog: {Count} positions.", SeedEntries.Length);
            return;
        }

        // For existing records that have the default 'Low' tier, update to the configured tier.
        var tierByName = SeedEntries.ToDictionary(e => e.Name, e => e.Tier, StringComparer.OrdinalIgnoreCase);
        var updated = 0;
        foreach (var pos in existing)
        {
            if (!tierByName.TryGetValue(pos.Name, out var correctTier)) continue;
            if (string.Equals(pos.EscalationTier, correctTier, StringComparison.OrdinalIgnoreCase)) continue;
            pos.EscalationTier = correctTier;
            await _rpThemeService.SavePositionAsync(pos, cancellationToken);
            updated++;
        }
        if (updated > 0)
            _logger.LogInformation("Updated EscalationTier for {Count} existing RP positions.", updated);
        else
            _logger.LogDebug("RP position catalog tier check: all tiers already correct.");
    }

    private sealed record SeedEntry(string Name, string Tier, string ShortDescription, string DetailedDescription);
}
