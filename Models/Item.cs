namespace Inventario.Models;

public class Item
{
    public const string ItPrefix = "IT-";

    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int? BoxId { get; set; }
    public Box? Box { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Otros";
    public decimal Quantity { get; set; } = 1;
    public string? Condition { get; set; }
    public string? Retention { get; set; }
    public bool Sentimental { get; set; }
    public bool Obsolete { get; set; }
    public bool Consumable { get; set; }
    public decimal? MinQuantity { get; set; }
    public string? Unit { get; set; }
    public string? Notes { get; set; }
    public string? CoverPhoto { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ItemTag> ItemTags { get; set; } = [];

    public static string FormatItCode(int sequence) => $"{ItPrefix}{sequence / 1000:000}-{sequence % 1000:000}";

    public static bool TryParseItSequence(string? value, out int sequence)
    {
        sequence = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var compact = new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        if (compact.StartsWith(ItPrefix, StringComparison.Ordinal))
        {
            compact = compact[ItPrefix.Length..];
        }
        else if (compact.StartsWith("IT", StringComparison.Ordinal))
        {
            compact = compact[2..];
        }

        compact = compact.Replace("-", "");
        return compact.Length > 0
            && compact.All(char.IsDigit)
            && int.TryParse(compact, out sequence)
            && sequence > 0;
    }

    public static string NormalizePublicCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return TryParseItSequence(value, out var sequence)
            ? FormatItCode(sequence)
            : new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
    }
}
