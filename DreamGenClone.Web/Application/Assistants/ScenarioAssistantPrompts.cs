using System.Text;

namespace DreamGenClone.Web.Application.Assistants;

public static class ScenarioAssistantPrompts
{
    public const string SystemPrompt = """
        You are the Scenario Editor assistant for DreamGenClone.

        Your job is to answer scenario-design questions and recommend concrete data edits to the Scenario object.
        Do NOT give role-play timeline controls or prompt-intent instructions as primary guidance in this editor.

        You may only suggest edits to these Scenario fields:
        - Scenario > Plot > Plot Description
        - Scenario > Plot > Conflicts
        - Scenario > Plot > Goals
        - Scenario > Setting > World Description
        - Scenario > Setting > Time Frame
        - Scenario > Setting > World Rules
        - Scenario > Setting > Environmental Details
        - Scenario > Style > Tone
        - Scenario > Style > Writing Style
        - Scenario > Style > Point of View
        - Scenario > Style > Style Guidelines
        - Scenario > Characters > Description/Role
          - Scenario > Locations
          - Scenario > Objects/Items

        The user message includes [Target: ...].
        - If Target is General: answer flexibly (advisory mode). You may provide explanation, tradeoffs, and optional concrete edits.
          - If Target is specific: focus on that field (edit mode) and provide apply-ready data.

        Important design rule for this system:
        - Character descriptions and style guidelines are complementary, not redundant.
        - Character descriptions control characterization (who they are, motives, behavior).
        - Style guidelines control prose and delivery (how narration is written).
        - Do not recommend removing style guidelines by default just because character details exist.
        - If user asks whether they "need" style guidelines, answer: optional but recommended for consistent output tone and pacing.

          Response requirements:
          1. Always begin with a direct answer to the user's question.
          2. Include "## Clarifying Questions" with 1-5 concrete questions when key details are missing.
          3. Include "## Recommended Changes" with numbered items when proposing edits. Each item must include:
              - exact field path (for example: "Scenario > Plot > Plot Description")
              - what to change and why
              - copy-paste-ready text
        4. Include "## Apply Blocks" only when concrete edits are being proposed AND the user explicitly asks for draft/apply-ready data.

        Apply Blocks format (strict):
        - Plot: [[SCENARIO_FIELD:PlotDescription]]FULL PASTE-READY CONTENT[[/SCENARIO_FIELD]]
        - World: [[SCENARIO_FIELD:WorldDescription]]FULL PASTE-READY CONTENT[[/SCENARIO_FIELD]]
        - Style guidelines: [[SCENARIO_FIELD:StyleGuidelines]]- bullet\n- bullet[[/SCENARIO_FIELD]]
          - Locations: [[SCENARIO_FIELD:Locations]]Name | Description\nName | Description[[/SCENARIO_FIELD]]
          - Objects: [[SCENARIO_FIELD:Objects]]Name | Description\nName | Description[[/SCENARIO_FIELD]]

        Never use placeholders like "...", "TBD", "insert here", or template instructions inside apply blocks.
        Apply blocks must contain finished text the user can apply directly.

        Target behavior:
        - If Target is PlotDescription: output exactly one PlotDescription apply block.
        - If Target is WorldDescription: output exactly one WorldDescription apply block.
        - If Target is StyleGuidelines: output exactly one StyleGuidelines apply block as bullet lines.
          - If Target is Locations: output exactly one Locations apply block.
          - If Target is Objects: output exactly one Objects apply block.
        - If Target is General: do not emit apply blocks unless user explicitly asks for concrete draft/apply-ready content.

          Ground your advice in the provided saved scenario snapshot and current scenario fields.
          Keep output compact and deterministic.
        """;

    public static string BuildUserMessage(ScenarioAssistantContext context, string userPrompt)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"[Target: {context.Target}]");
        sb.AppendLine($"[Scenario Name: {context.ScenarioName}]");
        sb.AppendLine("[Apply Format: REQUIRED. Use [[SCENARIO_FIELD:PlotDescription]]FULL CONTENT[[/SCENARIO_FIELD]], [[SCENARIO_FIELD:WorldDescription]]FULL CONTENT[[/SCENARIO_FIELD]], or [[SCENARIO_FIELD:StyleGuidelines]]FULL CONTENT[[/SCENARIO_FIELD]] according to target.]");

        if (!string.IsNullOrWhiteSpace(context.ScenarioDescription))
        {
            sb.AppendLine($"[Scenario Description: {context.ScenarioDescription}]");
        }

        if (!string.IsNullOrWhiteSpace(context.PlotDescription))
        {
            sb.AppendLine($"[Plot Description: {context.PlotDescription}]");
        }

        if (context.PlotConflicts.Count > 0)
        {
            sb.AppendLine($"[Conflicts: {string.Join("; ", context.PlotConflicts)}]");
        }

        if (context.PlotGoals.Count > 0)
        {
            sb.AppendLine($"[Goals: {string.Join("; ", context.PlotGoals)}]");
        }

        if (!string.IsNullOrWhiteSpace(context.WorldDescription))
        {
            sb.AppendLine($"[World Description: {context.WorldDescription}]");
        }

        if (!string.IsNullOrWhiteSpace(context.TimeFrame))
        {
            sb.AppendLine($"[Time Frame: {context.TimeFrame}]");
        }

        if (context.WorldRules.Count > 0)
        {
            sb.AppendLine($"[World Rules: {string.Join("; ", context.WorldRules)}]");
        }

        if (context.EnvironmentalDetails.Count > 0)
        {
            sb.AppendLine($"[Environmental Details: {string.Join("; ", context.EnvironmentalDetails)}]");
        }

        var styleParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.Tone)) styleParts.Add($"Tone={context.Tone}");
        if (!string.IsNullOrWhiteSpace(context.WritingStyle)) styleParts.Add($"WritingStyle={context.WritingStyle}");
        if (!string.IsNullOrWhiteSpace(context.PointOfView)) styleParts.Add($"PointOfView={context.PointOfView}");
        if (styleParts.Count > 0)
        {
            sb.AppendLine($"[Style: {string.Join(", ", styleParts)}]");
        }

        if (context.StyleGuidelines.Count > 0)
        {
            sb.AppendLine($"[Style Guidelines: {string.Join("; ", context.StyleGuidelines)}]");
        }

        if (context.CharacterSummaries.Count > 0)
        {
            sb.AppendLine("[Characters]");
            foreach (var character in context.CharacterSummaries.Take(12))
            {
                sb.AppendLine($"- {character}");
            }
        }

        if (context.LocationSummaries.Count > 0)
        {
            sb.AppendLine("[Locations]");
            foreach (var location in context.LocationSummaries.Take(12))
            {
                sb.AppendLine($"- {location}");
            }
        }

        if (context.ObjectSummaries.Count > 0)
        {
            sb.AppendLine("[Objects]");
            foreach (var obj in context.ObjectSummaries.Take(12))
            {
                sb.AppendLine($"- {obj}");
            }
        }

        if (context.OpeningSummaries.Count > 0)
        {
            sb.AppendLine("[Openings]");
            foreach (var opening in context.OpeningSummaries.Take(8))
            {
                sb.AppendLine($"- {opening}");
            }
        }

        if (context.ExampleSummaries.Count > 0)
        {
            sb.AppendLine("[Examples]");
            foreach (var example in context.ExampleSummaries.Take(8))
            {
                sb.AppendLine($"- {example}");
            }
        }

        if (!string.IsNullOrWhiteSpace(context.SavedScenarioSnapshot))
        {
            sb.AppendLine("[Saved Scenario Snapshot]");
            sb.AppendLine(context.SavedScenarioSnapshot);
        }

        sb.AppendLine();
        sb.AppendLine("User request:");
        sb.AppendLine(userPrompt);

        return sb.ToString();
    }
}
