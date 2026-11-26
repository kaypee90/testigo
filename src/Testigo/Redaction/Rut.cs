namespace Testigo.Redaction;

/// <summary>
/// Chilean RUT/RUN check digits (módulo 11). Redaction treats anything shaped like a RUT as one,
/// valid or not, so a typo doesn't leak an identifier; the check digit is for telling real-looking
/// numbers apart, and for making sure synthetic data uses RUTs that fail it (requirements §10.4).
/// </summary>
public static class Rut
{
    /// <summary>The check digit (0-9 or K) for a RUT body.</summary>
    public static char CheckDigit(long body)
    {
        if (body <= 0) throw new ArgumentOutOfRangeException(nameof(body));
        var sum = 0;
        var factor = 2;
        while (body > 0)
        {
            sum += (int)(body % 10) * factor;
            body /= 10;
            factor = factor == 7 ? 2 : factor + 1;
        }

        var digit = 11 - sum % 11;
        return digit switch
        {
            11 => '0',
            10 => 'K',
            _ => (char)('0' + digit),
        };
    }

    /// <summary>Whether a RUT as written (with or without dots and dash) has a correct check digit.</summary>
    public static bool IsValid(string? rut)
    {
        if (!TryParse(rut, out var body, out var dv)) return false;
        return CheckDigit(body) == dv;
    }

    public static bool TryParse(string? rut, out long body, out char checkDigit)
    {
        body = 0;
        checkDigit = '\0';
        if (string.IsNullOrWhiteSpace(rut)) return false;
        var clean = new string(rut.Where(c => char.IsDigit(c) || c is 'k' or 'K').ToArray()).ToUpperInvariant();
        if (clean.Length is < 2 or > 9) return false;
        checkDigit = clean[^1];
        return long.TryParse(clean[..^1], out body) && body > 0;
    }
}
