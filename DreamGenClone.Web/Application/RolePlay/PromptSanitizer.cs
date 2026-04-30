namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Sanitizes raw operator-authored SceneDirective text before injection into the LLM system prompt.
/// </summary>
public static class PromptSanitizer
{
    private const int MaxSceneDirectiveLength = 2000;

    private static readonly string[] InjectionTokenPrefixes =
    [
        "SYSTEM:", "system:", "USER:", "user:", "ASSISTANT:", "assistant:", "[INST]", "</s>", "###", "<|"
    ];

    public static string SanitizeSceneDirective(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        // Truncate to maximum allowed length
        var text = input.Length > MaxSceneDirectiveLength
            ? input[..MaxSceneDirectiveLength]
            : input;

        // Remove null bytes and non-printable control characters except \n, \r, \t
        var chars = new char[text.Length];
        var writeIndex = 0;
        foreach (var c in text)
        {
            if (c == '\n' || c == '\r' || c == '\t' || (c >= '\x20' && c != '\x7f'))
            {
                chars[writeIndex++] = c;
            }
        }
        text = new string(chars, 0, writeIndex);

        // Remove lines starting with known LLM role/injection tokens
        var lines = text.Split('\n');
        var safeLines = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart('\r').TrimStart();
            var isInjectionLine = false;
            foreach (var prefix in InjectionTokenPrefixes)
            {
                if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
                {
                    isInjectionLine = true;
                    break;
                }
            }
            if (!isInjectionLine)
            {
                safeLines.Add(line.TrimEnd());
            }
        }

        return string.Join('\n', safeLines).TrimEnd();
    }
}
