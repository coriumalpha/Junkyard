using Inventario.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Data;

public static class SeedData
{
    public static void EnsureSeeded(InventoryDbContext db)
    {
        if (db.Locations.Any())
        {
            NormalizeLabels(db);
            return;
        }

        var taller = new Location { Name = "Taller", Description = "Zona de herramientas, electrónica y consumibles." };
        var despacho = new Location { Name = "Despacho", Description = "Informática, documentación y piezas pequeñas." };
        db.Locations.AddRange(taller, despacho);
        db.SaveChanges();

        var boxes = new[]
        {
            new Box { Code = "CT-000-001", Name = "Vídeo Legacy", Description = "Cables y adaptadores de vídeo antiguos.", LocationId = taller.Id },
            new Box { Code = "CT-000-002", Name = "USB y Alimentación", Description = "Cargadores, hubs y adaptadores USB.", LocationId = despacho.Id },
            new Box { Code = "CT-000-003", Name = "Tornillería M3", Description = "Piezas pequeñas para impresión 3D y electrónica.", LocationId = taller.Id }
        };
        db.Boxes.AddRange(boxes);
        db.SaveChanges();

        db.Items.AddRange(
            new Item { BoxId = boxes[0].Id, Name = "Cables VGA", Category = "Tecnología / cables", Quantity = 6, Unit = "uds", Condition = "Usado", Retention = "Conservar", Obsolete = true, Notes = "Varios largos. Mantener por equipos legacy." },
            new Item { BoxId = boxes[0].Id, Name = "Adaptadores VGA-DVI", Category = "Tecnología / adaptadores", Quantity = 3, Unit = "uds", Condition = "Bueno" },
            new Item { BoxId = boxes[1].Id, Name = "Adaptadores USB-C", Category = "Tecnología / adaptadores", Quantity = 4, Unit = "uds", Condition = "Bueno" },
            new Item { BoxId = boxes[2].Id, Name = "Tornillos M3x8", Category = "Tornillería y piezas", Quantity = 18, Unit = "uds", Consumable = true, MinQuantity = 25, Notes = "Comprar más para montajes." });
        db.SaveChanges();
    }

    private static void NormalizeLabels(InventoryDbContext db)
    {
        foreach (var box in db.Boxes.IgnoreQueryFilters())
        {
            box.Name = box.Name
                .Replace("Video", "Vídeo")
                .Replace("Alimentacion", "Alimentación")
                .Replace("Tornilleria", "Tornillería");
            box.Description = box.Description?
                .Replace("video", "vídeo")
                .Replace("pequenas", "pequeñas")
                .Replace("impresion", "impresión")
                .Replace("electronica", "electrónica");
        }

        foreach (var item in db.Items.IgnoreQueryFilters())
        {
            item.Category = item.Category
                .Replace("Tecnologia", "Tecnología")
                .Replace("Tornilleria", "Tornillería")
                .Replace("Documentacion", "Documentación");
            item.Notes = item.Notes?.Replace("mas", "más");
        }

        foreach (var location in db.Locations)
        {
            location.Description = location.Description?
                .Replace("electronica", "electrónica")
                .Replace("Informatica", "Informática")
                .Replace("documentacion", "documentación")
                .Replace("pequenas", "pequeñas");
        }

        db.SaveChanges();
    }
}
