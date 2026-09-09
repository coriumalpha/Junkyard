using Inventario.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Data;

public static class SeedData
{
    public static void EnsureSeeded(InventoryDbContext db)
    {
        if (db.Locations.Any())
        {
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
        EnsureItemClassifications(db);
    }

    private static void EnsureItemClassifications(InventoryDbContext db)
    {
        var pila = EnsureClass(db, "Pila", InventoryMode.Fungible, "Consumibles de batería agrupados por formato.", "#48ffb0", "battery_full", 10);
        EnsureSubtype(db, pila, "AA", "uds", 12, 24, 10);
        EnsureSubtype(db, pila, "AAA", "uds", 12, 24, 20);
        EnsureSubtype(db, pila, "CR2032", "uds", 6, 12, 30);
        EnsureSubtype(db, pila, "18650", "uds", 4, 8, 40);

        EnsureClass(db, "Kit", InventoryMode.Kit, "Conjunto reutilizable compuesto por varios ítems.", "#8ad6ff", "category", 20);
        EnsureClass(db, "Lote", InventoryMode.Lot, "Agrupación temporal o lote de inventario.", "#ffc86b", "inventory", 30);
        EnsureClass(db, "Máquina", InventoryMode.Individual, "Equipo o máquina inventariable individualmente.", "#ff9bc8", "precision_manufacturing", 40);
        EnsureClass(db, "Disco", InventoryMode.Individual, "Soporte de almacenamiento inventariable individualmente.", "#c8f8d2", "album", 50);

        db.SaveChanges();
    }

    private static ItemClass EnsureClass(
        InventoryDbContext db,
        string name,
        InventoryMode mode,
        string description,
        string color,
        string icon,
        int sortOrder)
    {
        var itemClass = db.ItemClasses.FirstOrDefault(row => row.Name == name);
        if (itemClass is not null)
        {
            itemClass.InventoryMode = mode;
            itemClass.Description ??= description;
            itemClass.Color ??= color;
            itemClass.Icon ??= icon;
            itemClass.SortOrder ??= sortOrder;
            itemClass.IsActive = true;
            return itemClass;
        }

        itemClass = new ItemClass
        {
            Name = name,
            InventoryMode = mode,
            Description = description,
            Color = color,
            Icon = icon,
            SortOrder = sortOrder,
            IsActive = true
        };
        db.ItemClasses.Add(itemClass);
        db.SaveChanges();
        return itemClass;
    }

    private static void EnsureSubtype(
        InventoryDbContext db,
        ItemClass itemClass,
        string name,
        string unit,
        decimal minStock,
        decimal targetStock,
        int sortOrder)
    {
        var subtype = db.ItemSubtypes.FirstOrDefault(row => row.ItemClassId == itemClass.Id && row.Name == name);
        if (subtype is not null)
        {
            subtype.Unit ??= unit;
            subtype.MinStock ??= minStock;
            subtype.TargetStock ??= targetStock;
            subtype.SortOrder ??= sortOrder;
            subtype.IsActive = true;
            return;
        }

        db.ItemSubtypes.Add(new ItemSubtype
        {
            ItemClassId = itemClass.Id,
            Name = name,
            Unit = unit,
            MinStock = minStock,
            TargetStock = targetStock,
            SortOrder = sortOrder,
            IsActive = true
        });
    }

}
