using System.Text;

namespace SmsIrCheck;

internal static class PhoneNumber
{
    public static string Compact(string mobile)
    {
        var builder = new StringBuilder(mobile.Length);
        foreach (var ch in mobile.Trim())
        {
            if (char.IsWhiteSpace(ch) || ch is '-' or '(' or ')')
                continue;
            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Accepts 09xxxxxxxxx, 9xxxxxxxxx, 989xxxxxxxxx, 00989xxxxxxxxx, and +989xxxxxxxxx.
    /// </summary>
    public static bool LooksIranian(string mobile)
    {
        var digits = new string(Compact(mobile).Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0098", StringComparison.Ordinal))
            digits = digits[4..];
        else if (digits.StartsWith("98", StringComparison.Ordinal) && digits.Length >= 12)
            digits = digits[2..];
        else if (digits.StartsWith('0'))
            digits = digits[1..];

        return digits.Length == 10 && digits[0] == '9';
    }
}
