using System.ComponentModel.DataAnnotations.Schema;

namespace Inventario.Models;

public enum BoxStatus
{
    Active,
    Quarantine,
    Archived
}

public class Box
{
    public const string DefaultContainerType = "box";
    public const string CtPrefix = "CT-";

    private static readonly IReadOnlyDictionary<string, string> ContainerTypeLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["box"] = "Caja",
            ["subbox"] = "Subcaja",
            ["shelf"] = "Balda",
            ["drawer"] = "Cajón",
            ["rack"] = "Estantería",
            ["bag"] = "Bolsa",
            ["case"] = "Maletín",
            ["binder"] = "Archivador",
            ["lot"] = "Lote temporal",
            ["zone"] = "Zona física",
            ["other"] = "Otro soporte"
        };

    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string ContainerType { get; set; } = DefaultContainerType;
    public string? Description { get; set; }
    public int LocationId { get; set; }
    public Location? Location { get; set; }
    public int? ParentBoxId { get; set; }
    public Box? ParentBox { get; set; }
    [NotMapped]
    public string? EffectiveLocationName { get; set; }
    [NotMapped]
    public string? EffectiveLocationSourceLabel { get; set; }
    public BoxStatus Status { get; set; } = BoxStatus.Active;
    public string? CoverPhoto { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<Item> Items { get; set; } = [];
    public List<Box> ChildBoxes { get; set; } = [];

    public string ContainerTypeLabel => ContainerTypeLabelFor(ContainerType);

    public static IReadOnlyList<KeyValuePair<string, string>> AvailableContainerTypes() =>
        ContainerTypeLabels.ToList();

    public static string NormalizeContainerType(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return ContainerTypeLabels.ContainsKey(normalized) ? normalized : DefaultContainerType;
    }

    public static string ContainerTypeLabelFor(string? value)
    {
        var normalized = NormalizeContainerType(value);
        return ContainerTypeLabels[normalized];
    }

    public static string FormatCtCode(int sequence) => $"{CtPrefix}{sequence:000000}";

    public static bool TryParseCtSequence(string? value, out int sequence)
    {
        sequence = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var compact = new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        if (compact.StartsWith(CtPrefix, StringComparison.Ordinal))
        {
            compact = compact[CtPrefix.Length..];
        }
        else if (compact.StartsWith("CT", StringComparison.Ordinal))
        {
            compact = compact[2..];
            if (compact.StartsWith("-", StringComparison.Ordinal))
            {
                compact = compact[1..];
            }
        }

        return int.TryParse(compact, out sequence) && sequence > 0;
    }

    public static bool TryParseCanonicalCtSequence(string? value, out int sequence)
    {
        sequence = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var compact = new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        if (!compact.StartsWith(CtPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var digits = compact[CtPrefix.Length..];
        return digits.Length == 6
            && digits.All(char.IsDigit)
            && int.TryParse(digits, out sequence)
            && sequence > 0;
    }

    public static string NormalizePublicCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var compact = new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        if (TryParseCtSequence(compact, out var sequence))
        {
            return FormatCtCode(sequence);
        }

        return compact;
    }

    [NotMapped]
    public string LocationDisplay => EffectiveLocationName ?? Location?.Name ?? "Sin ubicación";
}
