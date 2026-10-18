using System.Text;

namespace InterLan.Domain;

public static class GroupTextPolicy
{
    public const int MaxNameLength = 120;
    public const int MaxTopicLength = 500;

    public static string NormalizeName(string name)
    {
        var normalized = NormalizeRequired(name, "Group name", MaxNameLength);
        return normalized;
    }

    public static string? NormalizeTopic(string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return null;

        var normalized = topic
            .Normalize(NormalizationForm.FormC)
            .Trim();

        if (normalized.Length > MaxTopicLength)
            throw new ArgumentException(
                $"Group topic cannot exceed {MaxTopicLength} characters.",
                nameof(topic));

        RejectUnsafeControls(normalized, nameof(topic));
        return normalized;
    }

    private static string NormalizeRequired(
        string value,
        string label,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{label} is required.");

        var normalized = value
            .Normalize(NormalizationForm.FormC)
            .Trim();

        if (normalized.Length > maxLength)
            throw new ArgumentException(
                $"{label} cannot exceed {maxLength} characters.");

        RejectUnsafeControls(normalized, label);
        return normalized;
    }

    private static void RejectUnsafeControls(
        string value,
        string parameterName)
    {
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsControl(rune) &&
                rune.Value is not '\t' and not '\r' and not '\n')
            {
                throw new ArgumentException(
                    "Text contains unsupported control characters.",
                    parameterName);
            }
        }
    }
}
