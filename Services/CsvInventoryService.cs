using System.Globalization;
using System.Text;
using Inventario.Data;
using Inventario.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Services;

public record InventoryCsvRow(
    string Location,
    string BoxCode,
    string BoxName,
    string ItemName,
    string Category,
    decimal Quantity,
    string Unit,
    bool Consumable,
    decimal? MinQuantity,
    string Condition,
    string Retention,
    bool Sentimental,
    bool Obsolete,
    string Notes);

public class CsvInventoryService(InventoryDbContext db)
{
    public static readonly string[] Categories =
    [
        "Tecnología / cables",
        "Tecnología / adaptadores",
        "Tecnología / almacenamiento",
        "Tecnología / electrónica",
        "Electrónica / componentes",
        "Herramientas",
        "Consumibles",
        "Tornillería y piezas",
        "Documentación",
        "Recuerdos",
        "Cuarentena",
        "Otros"
    ];

    public static string Template()
    {
        var builder = new StringBuilder();
        builder.AppendLine(Header);
        builder.AppendLine("Taller,C01,Vídeo Legacy,Cables VGA,Tecnología / cables,6,uds,true,2,Usado,Conservar,false,true,Cables largos y cortos");
        builder.AppendLine("Despacho,C02,USB y Alimentación,Adaptadores USB-C,Tecnología / adaptadores,4,uds,false,,Bueno,Conservar,false,false,");
        return builder.ToString();
    }

    public async Task<string> ExportAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Items
            .AsNoTracking()
            .Include(i => i.Box)!.ThenInclude(b => b!.Location)
            .OrderBy(i => i.Box!.Code)
            .ThenBy(i => i.Name)
            .ToListAsync(cancellationToken);

        var builder = new StringBuilder();
        builder.AppendLine(Header);
        foreach (var item in rows)
        {
            builder.AppendLine(string.Join(",", new[]
            {
                Escape(item.Box?.Location?.Name),
                Escape(item.Box?.Code),
                Escape(item.Box?.Name),
                Escape(item.Name),
                Escape(item.Category),
                Escape(item.Quantity.ToString(CultureInfo.InvariantCulture)),
                Escape(item.Unit),
                Escape(item.Consumable ? "true" : "false"),
                Escape(item.MinQuantity?.ToString(CultureInfo.InvariantCulture)),
                Escape(item.Condition),
                Escape(item.Retention),
                Escape(item.Sentimental ? "true" : "false"),
                Escape(item.Obsolete ? "true" : "false"),
                Escape(item.Notes)
            }));
        }

        return builder.ToString();
    }

    public List<InventoryCsvRow> Parse(string csv)
    {
        var lines = ReadRows(csv).ToList();
        if (lines.Count < 2)
        {
            return [];
        }

        return lines.Skip(1)
            .Where(c => c.Any(v => !string.IsNullOrWhiteSpace(v)))
            .Select((columns, index) => ToRow(columns, index + 2))
            .ToList();
    }

    public async Task ImportAsync(IEnumerable<InventoryCsvRow> rows, CancellationToken cancellationToken)
    {
        foreach (var row in rows)
        {
            var location = await db.Locations.FirstOrDefaultAsync(x => x.Name == row.Location, cancellationToken);
            if (location is null)
            {
                location = new Location { Name = row.Location };
                db.Locations.Add(location);
                await db.SaveChangesAsync(cancellationToken);
            }

            var box = await db.Boxes.FirstOrDefaultAsync(x => x.Code == row.BoxCode, cancellationToken);
            if (box is null)
            {
                box = new Box { Code = row.BoxCode, Name = row.BoxName, LocationId = location.Id };
                db.Boxes.Add(box);
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                box.Name = string.IsNullOrWhiteSpace(row.BoxName) ? box.Name : row.BoxName;
                box.LocationId = location.Id;
            }

            var item = await db.Items.FirstOrDefaultAsync(x => x.BoxId == box.Id && x.Name == row.ItemName, cancellationToken);
            if (item is null)
            {
                db.Items.Add(new Item
                {
                    BoxId = box.Id,
                    Name = row.ItemName,
                    Category = row.Category,
                    Quantity = row.Quantity,
                    Unit = row.Unit,
                    Consumable = row.Consumable,
                    MinQuantity = row.MinQuantity,
                    Condition = row.Condition,
                    Retention = row.Retention,
                    Sentimental = row.Sentimental,
                    Obsolete = row.Obsolete,
                    Notes = row.Notes
                });
            }
            else
            {
                item.Category = row.Category;
                item.Quantity = row.Quantity;
                item.Unit = row.Unit;
                item.Consumable = row.Consumable;
                item.MinQuantity = row.MinQuantity;
                item.Condition = row.Condition;
                item.Retention = row.Retention;
                item.Sentimental = row.Sentimental;
                item.Obsolete = row.Obsolete;
                item.Notes = row.Notes;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private const string Header = "Location,BoxCode,BoxName,ItemName,Category,Quantity,Unit,Consumable,MinQuantity,Condition,Retention,Sentimental,Obsolete,Notes";

    private static InventoryCsvRow ToRow(IReadOnlyList<string> c, int line)
    {
        string Get(int i) => i < c.Count ? c[i].Trim() : "";
        var location = Required(Get(0), "Location", line);
        var boxCode = Required(Get(1), "BoxCode", line);
        var boxName = Required(Get(2), "BoxName", line);
        var itemName = Required(Get(3), "ItemName", line);
        var category = string.IsNullOrWhiteSpace(Get(4)) ? "Otros" : Get(4);
        var quantity = ParseDecimal(Get(5), 1);
        decimal? min = string.IsNullOrWhiteSpace(Get(8)) ? null : ParseDecimal(Get(8), 0);

        return new InventoryCsvRow(
            location,
            boxCode,
            boxName,
            itemName,
            category,
            quantity,
            Get(6),
            ParseBool(Get(7)),
            min,
            Get(9),
            Get(10),
            ParseBool(Get(11)),
            ParseBool(Get(12)),
            Get(13));
    }

    private static string Required(string value, string name, int line)
        => string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"Linea {line}: falta {name}.") : value;

    private static decimal ParseDecimal(string value, decimal fallback)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static bool ParseBool(string value)
        => value.Equals("true", StringComparison.OrdinalIgnoreCase)
           || value.Equals("1", StringComparison.OrdinalIgnoreCase)
           || value.Equals("si", StringComparison.OrdinalIgnoreCase)
           || value.Equals("yes", StringComparison.OrdinalIgnoreCase);

    private static string Escape(string? value)
    {
        value ??= "";
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static IEnumerable<List<string>> ReadRows(string csv)
    {
        using var reader = new StringReader(csv);
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;

        while (reader.Read() is var next && next != -1)
        {
            var ch = (char)next;
            if (ch == '"')
            {
                if (inQuotes && reader.Peek() == '"')
                {
                    reader.Read();
                    cell.Append('"');
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if ((ch == '\n' || ch == '\r') && !inQuotes)
            {
                if (ch == '\r' && reader.Peek() == '\n')
                {
                    reader.Read();
                }
                row.Add(cell.ToString());
                cell.Clear();
                yield return row;
                row = [];
            }
            else
            {
                cell.Append(ch);
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            yield return row;
        }
    }
}
