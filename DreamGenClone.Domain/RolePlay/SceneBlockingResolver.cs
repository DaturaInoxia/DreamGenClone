namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Derives <see cref="SceneBlocking"/> from a Still brief's moment data and a persisted
/// <see cref="ImageStagingProfile"/>.
///
/// <para>
/// Pure and deterministic: no clock, no database, no LLM. Everything it needs is passed in, so the rule that decides
/// who stands where can be read line by line and tested against real briefs. The rationale is carried into the prose
/// verbatim — this resolver only extracts a structured camera from framing words the profile recognises, and falls
/// back to the profile's declared default camera when it recognises none.
/// </para>
///
/// <para>
/// Fail-fast is the whole posture: a subject with no key, an involvement that is neither active nor observer, or more
/// subjects than the profile's ladder can seat are all refusals that name the offender. It never invents a placement,
/// because a guessed placement is indistinguishable from a chosen one in the finished image.
/// </para>
/// </summary>
public static class SceneBlockingResolver
{
    /// <summary>The involvement value a beat marks an acting subject with.</summary>
    public const string ActiveInvolvement = "active";

    /// <summary>The involvement value a beat marks a watching subject with.</summary>
    public const string ObserverInvolvement = "observer";

    public static SceneBlocking Resolve(SceneBlockingInput input, ImageStagingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(profile);
        ImageStagingProfileValidation.Validate(profile);

        var subjects = input.Subjects ?? [];
        if (subjects.Count == 0)
        {
            throw new InvalidOperationException(
                $"Staging profile '{profile.DisplayName}' cannot frame a scene with no subjects: the brief supplied "
                + "no participants to place.");
        }

        var ladder = ImageStagingProfileValidation.ReadPlacementLadder(profile);

        // Classify once: involvement decides whether a subject is foreground or background, and guessing it would
        // decide the frame. Actives are seated first so the acting subject takes the head SEAT (the LEFT of a pair);
        // within each group the brief's own order is preserved, which keeps the result stable for a given brief.
        var seated = new List<(SceneBlockingSubjectInput Subject, bool IsObserver)>(subjects.Count);
        foreach (var subject in subjects)
        {
            seated.Add((subject, IsObserver(subject, profile)));
        }

        seated = [.. seated.Where(entry => !entry.IsObserver), .. seated.Where(entry => entry.IsObserver)];

        if (seated.Count > ladder.Count)
        {
            throw new InvalidOperationException(
                $"Staging profile '{profile.DisplayName}' declares {ladder.Count} frame position(s) but the scene has "
                + $"{seated.Count} subjects, so '{seated[ladder.Count].Subject.SubjectKey}' cannot be placed. Add a "
                + "position to the profile's placement ladder on /profiles, or reduce the cast.");
        }

        var seats = SeatsFor(seated.Count, ladder, profile);

        var placed = new List<SceneBlockingSubject>(seated.Count);
        for (var index = 0; index < seated.Count; index++)
        {
            var entry = seated[index];
            var horizontal = seats[index];
            var depth = entry.IsObserver ? profile.ObserverDepth : profile.ActiveDepth;
            placed.Add(new SceneBlockingSubject(
                entry.Subject.SubjectKey,
                new FramePosition(horizontal, depth),
                DescribeRole(horizontal, depth)));
        }

        var camera = ResolveCamera(input.Rationale, profile);

        var layering = placed
            .OrderBy(subject => DepthRank(subject.Position.Depth))
            .ThenBy(subject => LadderRank(ladder, subject.Position.Horizontal))
            .Select(subject => subject.SubjectKey)
            .ToList();

        return new SceneBlocking(
            profile.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            placed,
            camera,
            layering,
            input.Rationale?.Trim() ?? string.Empty);
    }

    private static bool IsObserver(SceneBlockingSubjectInput subject, ImageStagingProfile profile)
    {
        if (string.IsNullOrWhiteSpace(subject.SubjectKey))
        {
            throw new InvalidOperationException(
                $"Staging profile '{profile.DisplayName}' received a participant with no subject key, so its reference "
                + "bindings could not be tied to a person.");
        }

        if (string.Equals(subject.Involvement, ActiveInvolvement, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(subject.Involvement, ObserverInvolvement, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        throw new InvalidOperationException(
            $"Participant '{subject.SubjectKey}' declares involvement '{subject.Involvement}', which is neither "
            + $"'{ActiveInvolvement}' nor '{ObserverInvolvement}'. A guessed involvement would decide whether this "
            + "subject is foreground, so it is refused instead.");
    }

    /// <summary>
    /// The horizontals this frame's <paramref name="count"/> subjects are seated on, in seat order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A frame reads as balanced when its subjects are spread about the axis, and the spread depends on HOW MANY there
    /// are: one subject owns the CENTRE, a PAIR owns the opposite edges, and three fill LEFT/CENTRE/RIGHT. Walking the
    /// profile's ladder in declaration order instead gave a pair CENTRE then LEFT (operator report, 2026-10-22): both
    /// bodies crowded into one half of a side view with the other half empty, while the two reference clauses pointed
    /// into the same place — which is the ambiguity the blocking exists to remove.
    /// </para>
    /// <para>
    /// The ladder still decides WHICH positions the frame may use; only the choice among them belongs to the frame's
    /// geometry rather than to the profile's editing order. A profile whose ladder cannot supply the spread a scene
    /// needs is refused by name rather than seated off-centre or doubled up.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<FrameHorizontal> SeatsFor(
        int count,
        IReadOnlyList<FrameHorizontal> ladder,
        ImageStagingProfile profile)
    {
        var spread = FrameSpread(count);

        foreach (var horizontal in spread)
        {
            if (!ladder.Contains(horizontal))
            {
                throw new InvalidOperationException(
                    $"Staging profile '{profile.DisplayName}' places {count} subject(s) but its placement ladder "
                    + $"({string.Join(", ", ladder)}) omits {horizontal}, which is where {count} subject(s) balance in "
                    + "the frame. Add it to the profile's placement ladder on /profiles.");
            }
        }

        return spread;
    }

    /// <summary>
    /// The horizontals <paramref name="count"/> people occupy in a balanced frame, in seat order: one owns the centre,
    /// a pair owns the opposite edges, three fill the frame. The cast size decides this; a profile's ladder only decides
    /// which of these positions it permits.
    /// </summary>
    public static IReadOnlyList<FrameHorizontal> FrameSpread(int count) => count switch
    {
        1 => [FrameHorizontal.Center],
        2 => [FrameHorizontal.Left, FrameHorizontal.Right],
        3 => [FrameHorizontal.Left, FrameHorizontal.Center, FrameHorizontal.Right],
        _ => throw new InvalidOperationException(
            $"A frame cannot balance {count} subjects: it has three horizontal positions (Left, Center, Right), so at "
            + "most three people can be placed.")
    };

    /// <summary>
    /// The same frame with only the people it actually SHOWS still seated in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seats belong to the people in the picture, and who is in the picture is a fact about the POV, not about the
    /// brief: a participant POV is the camera (never in its own shot), and an Omniscient still keeps a remote observer
    /// out of frame. The compile cannot know either, so it seats the whole cast; the seat a person ends up on must then
    /// be re-fitted once the frame's cast is known, or two people's seats describe one person standing at the LEFT edge
    /// of a frame they are alone in (measured 2026-10-23: "&lt;image1&gt; is the person on the LEFT in the foreground"
    /// for a single-subject POV frame, and CENTER + LEFT for a two-person side view).
    /// </para>
    /// <para>
    /// The seat ORDER is preserved — the acting subject keeps the head seat — and the seat SET is the frame's own
    /// spread. The frame always has a centre, so a lone remaining subject is centred even when the compiled ladder
    /// never used it.
    /// </para>
    /// </remarks>
    public static SceneBlocking ReseatForCast(SceneBlocking blocking, IReadOnlyCollection<string> inFrameSubjectKeys)
    {
        ArgumentNullException.ThrowIfNull(blocking);
        ArgumentNullException.ThrowIfNull(inFrameSubjectKeys);

        var inFrame = inFrameSubjectKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = blocking.Subjects
            .Where(subject => inFrame.Contains(subject.SubjectKey))
            .ToList();

        if (kept.Count == blocking.Subjects.Count)
        {
            return blocking;
        }

        if (kept.Count == 0)
        {
            return blocking with { Subjects = [], Layering = [] };
        }

        if (kept.Count > 3)
        {
            throw new InvalidOperationException(
                $"Brief '{blocking.ProfileVersion}' places {kept.Count} people in one frame, but a frame has three "
                + "horizontal positions (Left, Center, Right), so they cannot be seated without doubling up.");
        }

        var spread = FrameSpread(kept.Count);
        var seats = new List<SceneBlockingSubject>(kept.Count);
        for (var index = 0; index < kept.Count; index++)
        {
            var subject = kept[index];
            seats.Add(subject with
            {
                Position = subject.Position with { Horizontal = spread[index] },
                RoleText = DescribeRole(spread[index], subject.Position.Depth)
            });
        }

        return blocking with
        {
            ProfileVersion = $"{blocking.ProfileVersion}+frame",
            Subjects = seats,
            Layering = [.. seats
                .OrderBy(subject => DepthRank(subject.Position.Depth))
                .ThenBy(subject => LadderRank(spread, subject.Position.Horizontal))
                .Select(subject => subject.SubjectKey)]
        };
    }

    private static string DescribeRole(FrameHorizontal horizontal, FrameDepth depth)
    {
        var across = horizontal switch
        {
            FrameHorizontal.Left => "on the LEFT",
            FrameHorizontal.Center => "in the CENTER",
            FrameHorizontal.Right => "on the RIGHT",
            _ => throw new InvalidOperationException($"Frame position '{horizontal}' is not a place a subject can stand.")
        };

        // Depth states a DISTANCE, not an absolute size (B-161). "In the background" on its own was read as "a little
        // behind" — the subject still rendered at full height a few feet from the camera, on the lawn rather than at
        // the open slider he was described at (operator report, 2026-10-13). But naming a size here is wrong too: how
        // large someone renders is the SHOT's business, so "large in the frame" contradicts a wide framing and pushes
        // the model to keep a distant subject readable — measured on the next render, where a man told to be "far to
        // the left, small in the background" still came out at half height. Depth therefore says where someone stands
        // relative to the camera, and the frame's OWN camera clause says what that means for scale.
        var into = depth switch
        {
            FrameDepth.Foreground => " in the foreground, nearest the camera",
            FrameDepth.Midground => " in the midground, at a middle distance from the camera",
            FrameDepth.Background => " in the background, farthest from the camera",
            _ => string.Empty
        };

        return $"the person {across}{into}";
    }

    private static SceneBlockingCamera ResolveCamera(string? rationale, ImageStagingProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(rationale))
        {
            foreach (var keyword in ImageStagingProfileValidation.ReadCameraKeywordMap(profile))
            {
                if (rationale.Contains(keyword.Keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return new SceneBlockingCamera(
                        string.IsNullOrWhiteSpace(keyword.Angle) ? profile.DefaultCameraAngle : keyword.Angle!,
                        string.IsNullOrWhiteSpace(keyword.ShotSize) ? profile.DefaultShotSize : keyword.ShotSize!,
                        keyword.Side,
                        string.IsNullOrWhiteSpace(profile.OmniscientShotSize) ? null : profile.OmniscientShotSize.Trim(),
                        CameraHeights.IsDefined(profile.DefaultCameraHeight)
                            ? CameraHeights.Require(profile.DefaultCameraHeight, $"Staging profile '{profile.DisplayName}'")
                            : null);
                }
            }
        }

        return new SceneBlockingCamera(
            profile.DefaultCameraAngle,
            profile.DefaultShotSize,
            null,
            string.IsNullOrWhiteSpace(profile.OmniscientShotSize) ? null : profile.OmniscientShotSize.Trim(),
            CameraHeights.IsDefined(profile.DefaultCameraHeight)
                ? CameraHeights.Require(profile.DefaultCameraHeight, $"Staging profile '{profile.DisplayName}'")
                : null);
    }

    private static int DepthRank(FrameDepth depth) => depth switch
    {
        FrameDepth.Foreground => 0,
        FrameDepth.Midground => 1,
        FrameDepth.Background => 2,
        _ => 3
    };

    private static int LadderRank(IReadOnlyList<FrameHorizontal> ladder, FrameHorizontal horizontal)
    {
        for (var index = 0; index < ladder.Count; index++)
        {
            if (ladder[index] == horizontal)
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Applies an operator's placement correction to a SNAPSHOTTED blocking (B-157 T7.1).
    ///
    /// <para>
    /// Only the subjects the operator named move; the camera, the rationale and the subjects they did not touch stay
    /// exactly as the brief recorded them — except the camera, which an operator may also correct (B-161): the shot
    /// size decides how large every subject renders, so a depth word the operator changed beneath a "medium shot" that
    /// fills the frame with the subject would still produce a full-height person on the lawn (reported 2026-10-13).
    /// The result carries its version plus an operator marker, because provenance must never read as "the staging
    /// profile placed them here" when a person did.
    /// </para>
    ///
    /// <para>
    /// Two subjects may not end up in one position: an override that would collide is refused, for the same reason the
    /// resolver refuses it — the clause would claim one place belongs to two people.
    /// </para>
    /// </summary>
    public static SceneBlocking ApplyOverride(SceneBlocking blocking, SceneBlockingOverride? overrides)
    {
        ArgumentNullException.ThrowIfNull(blocking);
        if (overrides is null || overrides.IsEmpty)
        {
            return blocking;
        }

        var camera = ApplyCameraOverride(blocking.Camera, overrides.Camera);

        // Last entry wins: an operator who changed their mind twice meant the second one.
        var changes = new Dictionary<string, SceneBlockingSubjectOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in overrides.Subjects)
        {
            if (string.IsNullOrWhiteSpace(change.SubjectKey))
            {
                throw new InvalidOperationException("A placement override must name the subject it moves.");
            }

            changes[change.SubjectKey] = change;
        }

        var moved = new List<SceneBlockingSubject>(blocking.Subjects.Count);
        foreach (var subject in blocking.Subjects)
        {
            if (!changes.TryGetValue(subject.SubjectKey, out var change))
            {
                moved.Add(subject);
                continue;
            }

            var horizontal = change.Horizontal is { } chosenHorizontal and not FrameHorizontal.Unknown
                ? chosenHorizontal
                : subject.Position.Horizontal;
            var depth = change.Depth is { } chosenDepth and not FrameDepth.Unknown
                ? chosenDepth
                : subject.Position.Depth;
            moved.Add(subject with
            {
                Position = new FramePosition(horizontal, depth),
                RoleText = DescribeRole(horizontal, depth)
            });
        }

        var collision = moved
            .GroupBy(subject => subject.Position)
            .FirstOrDefault(group => group.Count() > 1);
        if (collision is not null)
        {
            throw new InvalidOperationException(
                $"The placement override puts {string.Join(" and ", collision.Select(subject => subject.SubjectKey))} in "
                + $"the same frame position ({collision.Key.Horizontal}/{collision.Key.Depth}). Give them different "
                + "positions: one place cannot belong to two people.");
        }

        return blocking with
        {
            ProfileVersion = $"{blocking.ProfileVersion}+operator",
            Subjects = moved,
            Camera = camera,
            // Foreground before background, keeping the compiled order within each depth (the sort is stable).
            Layering = [.. moved.OrderBy(subject => DepthRank(subject.Position.Depth)).Select(subject => subject.SubjectKey)]
        };
    }

    /// <summary>
    /// The frame as a PROMPT should describe it: the operator's corrections applied, and every subject's role text
    /// rendered in the CURRENT depth vocabulary (B-161).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONE entry point for both halves of a prompt — the prose the pre-processor writes and the authoritative block the
    /// render appends — so one frame cannot be described two ways. Before this, the render applied the override and the
    /// draft did not, which is how a prompt came to assert the profile's "medium shot" in prose and the operator's
    /// framing below it.
    /// </para>
    ///
    /// <para>
    /// The role text is re-rendered from each subject's POSITION, never re-derived: the position is what the brief
    /// decided and stays untouched, while the vocabulary that describes it belongs to this code. A brief compiled
    /// before depth stated a distance would otherwise keep "in the background" with nothing about scale — the wording
    /// that was read as "a little behind" and produced a full-height person on the lawn (reported 2026-10-13).
    /// </para>
    /// </remarks>
    public static SceneBlocking ApplyForPrompt(
        SceneBlocking blocking,
        SceneBlockingOverride? overrides,
        string? omniscientShotSize = null,
        string? omniscientAngle = null)
    {
        ArgumentNullException.ThrowIfNull(blocking);

        var effective = ApplyOverride(blocking, overrides);

        // B-166 D7: an Omniscient still is the narrative external view, so it leans wide/establishing. The lean is a
        // CONFIGURED value (the staging profile's OmniscientShotSize), and it applies only when the operator named no
        // framing for this render — a deliberate framing always beats the POV default. Nothing is invented when the
        // caller supplies none.
        if (!string.IsNullOrWhiteSpace(omniscientShotSize)
            && string.IsNullOrWhiteSpace(overrides?.Camera?.ShotSize))
        {
            effective = effective with
            {
                Camera = effective.Camera with { ShotSize = omniscientShotSize.Trim() }
            };
        }

        // The Omniscient view has an ANGLE of its own too (operator 2026-10-26: "Omniscient POV needs its own camera
        // angle; it is not Dean's view or Becky's view"). The operator's choice was applied at RENDER time only, so the
        // caption was written from the compiled angle ("medium shot at eye level") while the render used the chosen one
        // — two cameras in one request, which is the defect class B-161 removed for shot size.
        if (!string.IsNullOrWhiteSpace(omniscientAngle)
            && string.IsNullOrWhiteSpace(overrides?.Camera?.Angle))
        {
            effective = effective with
            {
                Camera = effective.Camera with { Angle = omniscientAngle.Trim() }
            };
        }

        var refreshed = effective.Subjects
            .Select(subject => subject with
            {
                RoleText = DescribeRole(subject.Position.Horizontal, subject.Position.Depth)
            })
            .ToList();

        // Unchanged means UNCHANGED, instance included: a frame already described in this vocabulary is passed straight
        // through rather than rebuilt, so nothing downstream sees a spurious difference.
        return refreshed.SequenceEqual(effective.Subjects)
            ? effective
            : effective with { Subjects = refreshed };
    }

    /// <summary>
    /// The camera with the operator's framing applied, or the compiled camera untouched when they chose none.
    /// </summary>
    /// <remarks>
    /// A blank shot size is REFUSED rather than folded back into the compiled value: it would mean the operator asked
    /// for a framing and silently got the profile's default instead, which is the substitution this codebase forbids.
    /// The angle is only replaced when they named one, so changing the shot never restates the angle.
    /// </remarks>
    private static SceneBlockingCamera ApplyCameraOverride(
        SceneBlockingCamera camera,
        SceneBlockingCameraOverride? overrides)
    {
        if (overrides is null)
        {
            return camera;
        }

        if (string.IsNullOrWhiteSpace(overrides.ShotSize))
        {
            throw new InvalidOperationException(
                "A framing override must name the shot size it applies: an empty one would leave the render on the "
                + "profile's default while appearing to have been overridden.");
        }

        return camera with
        {
            ShotSize = overrides.ShotSize.Trim(),
            // A chosen HEIGHT states the aim that height implies unless the operator also named the angle: "floor level"
            // and "low angle" are one instruction, and letting them disagree is how a frame says two things at once.
            Angle = !string.IsNullOrWhiteSpace(overrides.Angle)
                ? overrides.Angle.Trim()
                : CameraHeights.IsDefined(overrides.Height)
                    ? AngleForPitch(CameraHeights.PitchOf(CameraHeights.Require(overrides.Height, "The framing override")))
                    : camera.Angle,
            Height = CameraHeights.IsDefined(overrides.Height)
                ? CameraHeights.Require(overrides.Height, "The framing override")
                : camera.Height
        };
    }

    /// <summary>The camera-angle wording for a vertical aim, in the vocabulary the framing line already uses.</summary>
    private static string AngleForPitch(string pitch) => pitch switch
    {
        "up" => "low angle, viewed from below",
        "down" => "high angle, viewed from above",
        "level" => "eye level",
        _ => throw new InvalidOperationException($"Camera pitch '{pitch}' is not one of up, level or down.")
    };
}
