using System.Text.RegularExpressions;

namespace MojTermin.Api.Application.Validation;

public static partial class RegionalPhone
{
    [GeneratedRegex(@"^\+\d{6,15}$", RegexOptions.Compiled)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^0\d{7,11}$", RegexOptions.Compiled)]
    private static partial Regex LocalPhoneRegex();

    public static string Normalize(string phone) =>
        phone
            .Trim()
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("/", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty);

    public static bool IsValid(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return false;
        }

        var normalized = Normalize(phone);
        return PhoneRegex().IsMatch(normalized) || LocalPhoneRegex().IsMatch(normalized);
    }
}
