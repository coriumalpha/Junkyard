using System.ComponentModel.DataAnnotations;
using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages.Items;

public class CreateModel(InventoryDbContext db) : PageModel
{
    [BindProperty]
    public ItemInput Input { get; set; } = new();

    public List<SelectListItem> Boxes { get; private set; } = [];
    public SearchPickerModel BoxPicker { get; private set; } = new();
    public string[] Categories => CsvInventoryService.Categories;

    public async Task OnGetAsync(int? boxId, CancellationToken cancellationToken)
    {
        Input.BoxId = boxId ?? 0;
        await LoadBoxes(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadBoxes(cancellationToken);
            return Page();
        }

        var item = new Item
        {
            BoxId = Input.BoxId == 0 ? null : Input.BoxId,
            Name = Input.Name.Trim(),
            Category = Input.Category,
            Quantity = Input.Quantity,
            Unit = Input.Unit,
            Condition = Input.Condition,
            Retention = Input.Retention,
            Consumable = Input.Consumable,
            MinQuantity = Input.MinQuantity,
            Sentimental = Input.Sentimental,
            Obsolete = Input.Obsolete,
            Notes = Input.Notes
        };
        db.Items.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        if (item.BoxId is int boxId)
        {
            var boxCode = await db.Boxes.Where(b => b.Id == boxId).Select(b => b.Code).FirstAsync(cancellationToken);
            return RedirectToPage("/Boxes/Details", new { code = boxCode });
        }
        return RedirectToPage("/Items/Index");
    }

    private async Task LoadBoxes(CancellationToken cancellationToken)
    {
        Boxes = await db.Boxes.AsNoTracking().OrderBy(b => b.Code)
            .Select(b => new SelectListItem($"{b.Code} · {b.Name}", b.Id.ToString()))
            .ToListAsync(cancellationToken);
        Boxes.Insert(0, new SelectListItem("Sin caja / huérfano", "0"));

        BoxPicker = new SearchPickerModel
        {
            InputName = "Input.BoxId",
            InputId = "Input_BoxId",
            Label = "Caja",
            Placeholder = "Buscar por CT, nombre, tipo, ubicación o padre...",
            SelectedValue = Input.BoxId.ToString(),
            EmptyLabel = "Sin caja / huérfano",
            EmptyHint = "El ítem quedará fuera de contenedor.",
            ClearValue = "0",
            NoneOptionLabel = "Sin caja / huérfano",
            NoneOptionHint = "Guarda el ítem sin contenedor asignado.",
            NoneOptionValue = "0",
            NoneOptionIcon = "—",
            Options = await SearchPickerFactory.BuildBoxOptionsAsync(db, cancellationToken)
        };
    }

    public class ItemInput
    {
        public int BoxId { get; set; }

        [Required, MaxLength(180)]
        public string Name { get; set; } = "";

        [Required]
        public string Category { get; set; } = "Otros";

        public decimal Quantity { get; set; } = 1;
        public string? Unit { get; set; } = "uds";
        public string? Condition { get; set; }
        public string? Retention { get; set; }
        public bool Consumable { get; set; }
        public decimal? MinQuantity { get; set; }
        public bool Sentimental { get; set; }
        public bool Obsolete { get; set; }
        public string? Notes { get; set; }
    }
}
