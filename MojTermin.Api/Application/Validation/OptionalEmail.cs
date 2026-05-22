using System.ComponentModel.DataAnnotations;

namespace MojTermin.Api.Application.Validation;

public static class OptionalEmail
{
    private static readonly EmailAddressAttribute Validator = new();

    public static string? Normalize(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim();

    public static bool IsValid(string? email)
    {
        var normalized = Normalize(email);
        return normalized is null || Validator.IsValid(normalized);
    }
}
