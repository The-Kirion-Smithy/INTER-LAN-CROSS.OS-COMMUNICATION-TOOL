using System.Text;

namespace InterLan.Domain;

public static class MessageTextPolicy
{
    public static string Normalize(string body, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Message body is required.", nameof(body));

        if (maxLength < 1)
            throw new ArgumentOutOfRangeException(nameof(maxLength));

        var normalized = body.Normalize(NormalizationForm.FormC).Trim();

        if (normalized.Length == 0)
            throw new ArgumentException("Message body is required.", nameof(body));

        if (normalized.Length > maxLength)
            throw new ArgumentException(
                $"Message body cannot exceed {maxLength} characters.",
                nameof(body));

        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsControl(rune) &&
                rune.Value is not '\t' and not '\r' and not '\n')
            {
                throw new ArgumentException(
                    "Message body contains unsupported control characters.",
                    nameof(body));
            }
        }

        return normalized;
    }
}
