using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// P1 of the holistic pipeline: one typed plan owns what a still-image prompt means. These are the invariants that make
/// the later family renderers safe — a fact appears once, an operator edit reaches the plan and nothing else does, and
/// the frame states what it CONTAINS rather than trusting a shot-size word.
/// </summary>
public sealed class PromptPlanBuilderTests
{
    [Fact]
    public void Build_StatesEachSubjectOnceWithItsOwnFacts()
    {
        var plan = Build();

        var subject = Assert.Single(plan.Subjects);
        Assert.Equal("p0", subject.ProfileKey);
        Assert.Equal("Becky", subject.Name);
        Assert.Equal("Nude, bare all over", subject.Wardrobe);
        Assert.Equal("bent over at the waist at workbench", subject.WorldPose);
        Assert.Equal("the tin wall", subject.Facing);
        Assert.Equal("Center Foreground", subject.FramePlacement);
        // The appearance is carried as prose only when no reference supplies it (B-166 D10).
        Assert.Equal("Fair skin, brown bun", subject.Appearance);
    }

    /// <summary>
    /// The invariant that kills the whole duplicate-restatement family: one fact, one slot. A second subject entry, a
    /// second wardrobe, or the action repeated as a subject fact would all show up here.
    /// </summary>
    [Fact]
    public void Build_NoFactAppearsTwice()
    {
        var plan = Build();

        Assert.Single(plan.Subjects.Select(subject => subject.ProfileKey).Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.Single(plan.Subjects.Select(subject => subject.Wardrobe));
        Assert.Single(plan.Subjects.Select(subject => subject.WorldPose));
        // The action sentence is its own slot and is never also a subject fact.
        Assert.DoesNotContain(plan.Subjects, subject =>
            string.Equals(subject.WorldPose, plan.ActionLine, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_ContactsNameBothOwners()
    {
        var plan = Build();

        var contact = Assert.Single(plan.Contacts);
        Assert.Equal("Becky", contact.FromName);
        Assert.Equal("teeth", contact.Part);
        Assert.Equal("Becky", contact.ToName);
        Assert.Equal("forearm", contact.TargetPart);
        Assert.True(contact.IsSelfContact);
    }

    [Fact]
    public void Build_HolderBodyIsDerivedFromContactsNotFromTheNoteString()
    {
        var plan = Build();

        Assert.NotNull(plan.HolderBody);
        Assert.Equal(["hands"], plan.HolderBody!.Parts);
        Assert.Equal("Trousers open at the waist", plan.HolderBody.Wardrobe);
    }

    [Fact]
    public void Build_FrameStatesItsInclusionAndNotAShotSizeDerivedExtent()
    {
        var plan = Build();

        Assert.Equal("close shot", plan.Frame.ShotSize);
        Assert.Equal(FrameInclusionExtent.WholeBody, plan.Frame.Inclusion);
    }

    [Fact]
    public void Build_AnOperatorWardrobeEditReachesThePlan()
    {
        var overrides = Overrides(("character:p0.clothing", "Shirt open, skirt gone"));
        var plan = Build(overrides);

        Assert.Equal("Shirt open, skirt gone", Assert.Single(plan.Subjects).Wardrobe);
        Assert.Contains("character:p0.clothing", plan.OperatorEditedKeys);
    }

    /// <summary>
    /// The extent is DERIVED by default: the whole person, for every shot size. P1b adds the operator's own row
    /// (<c>character:{key}.framing.extent</c>) so a deliberate "waist up" is a recorded fact rather than a shot-size
    /// side effect.
    /// </summary>
    [Fact]
    public void Build_ExtentIsTheDerivedWholeBodyDefault()
        => Assert.Equal(FrameInclusionExtent.WholeBody, Assert.Single(Build().Subjects).InFrameExtent);

    [Fact]
    public void Build_AnOperatorExtentEditReachesThePlan()
    {
        var plan = Build(Overrides(("character:p0.framing.extent", "waist up")));

        Assert.Equal("waist up", Assert.Single(plan.Subjects).InFrameExtent);
        Assert.Equal("waist up", plan.Frame.Inclusion);
        Assert.Contains("character:p0.framing.extent", plan.OperatorEditedKeys);
    }

    /// <summary>
    /// The extent row is a finite set, and anything outside it fails by name: a value the pipeline has no vocabulary for
    /// would otherwise reach a prompt as words the model has to interpret.
    /// </summary>
    [Fact]
    public void Build_AnExtentOutsideTheRecordedVocabulary_FailsFast()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Build(Overrides(("character:p0.framing.extent", "knees to eyebrows"))));

        Assert.Contains("knees to eyebrows", error.Message, StringComparison.Ordinal);
        Assert.Contains(FrameInclusionExtent.WholeBody, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtentRow_IsASelectableBriefElement()
        => Assert.True(StillBriefElements.IsSelectable("character:p0.framing.extent"));

    /// <summary>
    /// The coverage set is what P5 will enforce. It has to name every fact class the prompt must carry, or a renderer
    /// can drop one and still pass.
    /// </summary>
    [Fact]
    public void Build_MustAppearNamesEveryRequiredFactClass()
    {
        var plan = Build();

        Assert.Contains("subject-count:1", plan.MustAppear);
        Assert.Contains("wardrobe:Becky", plan.MustAppear);
        Assert.Contains("setting-kind", plan.MustAppear);
        Assert.Contains("lighting", plan.MustAppear);
        Assert.Contains("holder-body", plan.MustAppear);
        Assert.Contains("frame", plan.MustAppear);
    }

    [Fact]
    public void Build_UserNameNeverAppearsInThePlanAsAnIdentifier()
    {
        var plan = Build();
        var json = PromptPlanBuilder.ToJson(plan);

        Assert.DoesNotContain("\"p0\"", json.Replace("{\"key\":\"p0\"", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        // The name is what a prompt may use; the key is only ever an identifier field.
        Assert.Contains("Becky", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// P1's acceptance gate for the P3 switch: every fact the still brief's fact sheet states must have a typed home in
    /// the plan. The store is derived from the sheet's OWN labels, so a fact class added to the sheet later fails this
    /// test instead of quietly disappearing from the prompt when the renderer starts reading the plan instead.
    /// </summary>
    [Fact]
    public void EveryFactTheSheetStates_HasATypedHomeInThePlan()
    {
        var snapshot = ScenePromptOverridesApplier.ApplyOperatorProjectionEdits(EffectiveSnapshot, null);
        var labels = FactSheetLabels(StillBriefFactSheet.Render(snapshot, null));
        var plan = Build();

        Assert.NotEmpty(labels);
        // Guard against a vacuous pass: the fixture must exercise BOTH sheet shapes (a top-level fact and a cast fact).
        Assert.Contains("PLACE", labels);
        Assert.Contains("wardrobe", labels);
        Assert.All(labels, label => Assert.True(
            FactHomes.ContainsKey(label) || FactsNotOnThePlan.ContainsKey(label),
            $"The fact sheet states '{label}', but the plan has no slot for it. Add the fact to PromptPlan (and its "
            + "renderer) or record here why a still image cannot show it."));

        foreach (var label in labels)
        {
            if (!FactHomes.TryGetValue(label, out var home)) continue;
            Assert.False(
                string.IsNullOrWhiteSpace(home.Read(plan)),
                $"The fact sheet states '{label}' but {home.Slot} on the plan is empty, so a renderer reading the plan "
                + "would drop the fact.");
        }
    }

    /// <summary>
    /// The plan states every fact the sheet states ONCE, and the two exceptions are facts a still image cannot show —
    /// not facts we forgot.
    /// </summary>
    private static readonly Dictionary<string, string> FactsNotOnThePlan = new(StringComparer.Ordinal)
    {
        ["visible to them"] = "Who a person can SEE is not observable in a photograph; the cast membership it implies is "
            + "already applied to PromptPlan.Subjects.",
        ["involvement"] = "Active/observer decides whether the person is in the picture at all, which PromptPlan.Subjects "
            + "has already applied; it is not itself a depictable fact."
    };

    /// <summary>Fact-sheet label → the plan slot that carries it. The probe returns null when the plan lost the fact.</summary>
    private static readonly Dictionary<string, (string Slot, Func<PromptPlan, string?> Read)> FactHomes =
        new(StringComparer.Ordinal)
        {
            ["PLACE"] = ("Setting", plan => plan.Setting),
            ["TIME OF DAY"] = ("TimeOfDay", plan => plan.TimeOfDay),
            ["TEMPORAL ANCHOR"] = ("TemporalAnchor", plan => plan.TemporalAnchor),
            ["LIGHTING"] = ("Lighting", plan => plan.Lighting),
            ["ENVIRONMENT"] = ("Atmosphere", plan => plan.Atmosphere),
            ["MOOD"] = ("Mood", plan => plan.Mood),
            ["OBJECTS"] = ("Objects", plan => plan.Objects.Count == 0 ? null : string.Join("; ", plan.Objects)),
            ["EVENT"] = ("ActionLine", plan => plan.ActionLine),
            ["CONTACTS"] = ("Contacts", plan => plan.Contacts.Count == 0 ? null : string.Join("; ", plan.Contacts.Select(contact => contact.Part))),
            ["body"] = ("Subjects[].WorldPose", plan => SubjectFact(plan, subject => subject.WorldPose)),
            ["facing"] = ("Subjects[].Facing", plan => SubjectFact(plan, subject => subject.Facing)),
            // `stance` is the same world pose recorded on a second field; the plan keeps one, which is the whole point.
            ["stance"] = ("Subjects[].WorldPose", plan => SubjectFact(plan, subject => subject.WorldPose)),
            ["action"] = ("Subjects[].Action", plan => SubjectFact(plan, subject => subject.Action)),
            ["sightline"] = ("Subjects[].Sightline", plan => SubjectFact(plan, subject => subject.Sightline)),
            ["wardrobe"] = ("Subjects[].Wardrobe", plan => SubjectFact(plan, subject => subject.Wardrobe)),
            ["place"] = ("Subjects[].Place", plan => SubjectFact(plan, subject => subject.Place))
        };

    private static string? SubjectFact(PromptPlan plan, Func<PromptPlanSubject, string?> read)
        => plan.Subjects.Count == 1 ? read(plan.Subjects[0]) : null;

    /// <summary>The labels the sheet actually emitted — top-level ("LIGHTING: …") and cast ("  wardrobe: …").</summary>
    private static IReadOnlyList<string> FactSheetLabels(string sheet)
    {
        var labels = new List<string>();
        foreach (var line in sheet.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (line.StartsWith("- ", StringComparison.Ordinal)) continue;
            var topLevel = System.Text.RegularExpressions.Regex.Match(line, "^([A-Z][A-Z ]+): ");
            if (topLevel.Success)
            {
                labels.Add(topLevel.Groups[1].Value);
                continue;
            }

            var cast = System.Text.RegularExpressions.Regex.Match(line, "^  ([a-z][a-z ]+): ");
            if (cast.Success)
            {
                labels.Add(cast.Groups[1].Value);
            }
        }

        return labels.Distinct(StringComparer.Ordinal).ToList();
    }

    private static PromptPlan Build(ScenePromptOverrides? overrides = null)
        => PromptPlanBuilder.Build(
            // Same contract as the job handler: the builder receives the snapshot WITH the operator's edits applied.
            ScenePromptOverridesApplier.ApplyOperatorProjectionEdits(EffectiveSnapshot, overrides),
            Projection(overrides),
            Blocking(),
            overrides);

    private static ScenePromptOverrides Overrides(params (string Key, string Value)[] fields) => new()
    {
        Fields = fields
            .Select(field => new ScenePromptFieldOverride { ElementKey = field.Key, Value = field.Value })
            .ToList()
    };

    /// <summary>The projection an operator-edited brief produces, built through the real resolver rather than hand-rolled.</summary>
    private static SceneImagePovProjection.Projection Projection(ScenePromptOverrides? overrides = null)
        => SceneImagePovProjection.Resolve(
            Brief,
            "Dean",
            Blocking(),
            overrides,
            cameraHolderPartsMode: CameraHolderPartsMode.Derived);

    private static CompiledMediaBrief Brief => new(
        Id: "brief-plan",
        MediaKind: MediaProductionKind.StillImage,
        TargetProfileId: "canonical-still",
        TargetProfileVersion: "3",
        FamilyKey: "canonical",
        CompilerKey: "deterministic",
        CompilerVersion: "1",
        ProviderRequestContractVersion: "canonical-request-v1",
        Lineage: new CompiledMediaLineage("cat1", "beat1", "plan1", 1, "momset1", 1, "m2", "enr1", 3),
        CanonicalSourceIds: ["src1"],
        SemanticInputSnapshotJson: EffectiveSnapshot,
        ProviderRequestSnapshotJson: """{"contract":"canonical-request-v1"}""",
        RequiredIntentCoverageJson: """{"entries":[]}""",
        Status: MediaCompilerStatus.Complete,
        ErrorCode: null,
        ErrorMessage: null,
        CreatedUtc: DateTime.UtcNow.AddMinutes(-1),
        CompletedUtc: DateTime.UtcNow);

    private const string EffectiveSnapshot = """
        {"frozenState":{
          "characters":[
            {"profileKey":"p0","characterId":"c0","name":"Becky","involvement":"active",
             "physicalLocation":"At the workbench inside the shed","position":"Bent over the workbench",
             "actionOrObservation":"Biting her own forearm","sightline":"down toward her own forearm",
             "visibleCharacterNames":["Dean"],"clothing":"Nude, bare all over","appearance":"Fair skin, brown bun",
             "at":{"anchor":"workbench","posture":"bent over at the waist"},"facing":{"toward":"the tin wall"}},
            {"profileKey":"p1","characterId":"c1","name":"Dean","involvement":"active",
             "physicalLocation":"Behind Becky at the workbench","position":"Standing directly behind Becky",
             "actionOrObservation":"Holding his position","sightline":"down toward Becky's back",
             "visibleCharacterNames":["Becky"],"clothing":"Trousers open at the waist",
             "at":{"anchor":"shed floor behind the workbench","posture":"standing"},"facing":{"toward":"Becky's back"}}],
          "location":"Shed behind the trailers","timeOfDay":"Morning","lighting":"Bars of slat light",
          "environment":"Shaded shed interior","mood":"Clenched","objects":["workbench"],
          "contacts":[{"from":"p1","part":"hands","to":"p0","targetPart":"thighs","manner":"clenching tightly"},
                      {"from":"p0","part":"teeth","to":"p0","targetPart":"forearm","manner":"sunk into"}],
          "event":{"anchor":"workbench","line":"Becky climaxes while Dean remains pressed to her."}}}
        """;

    private static SceneBlocking Blocking() => new(
        "1",
        [new SceneBlockingSubject("p0", new FramePosition(FrameHorizontal.Center, FrameDepth.Foreground), "the person in the CENTER in the foreground")],
        new SceneBlockingCamera("high angle, viewed from above", "close shot", null, null, CameraHeights.Above),
        ["p0"],
        "Close overhead of the workbench");
}
