using System.ComponentModel.DataAnnotations;
using Inventario.Data;
using Inventario.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages.Boxes;

public class CreateModel(InventoryDbContext db) : PageModel
{
    [BindProperty]
    public BoxInput Input { get; set; } = new();

    public List<SelectListItem> Locations { get; private set; } = [];
    public List<SelectListItem> ParentBoxes { get; private set; } = [];

    public async Task OnGetAsync(int? parentBoxId, CancellationToken cancellationToken)
    {
        if (parentBoxId is int id)
        {
            var parent = await db.Boxes.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
            if (parent is not null)
            {
                Input.ParentBoxId = parent.Id;
                Input.LocationId = parent.LocationId;
            }
        }

        await LoadSelects(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadSelects(cancellationToken);
            return Page();
        }

        var box = new Box
        {
            Code = Input.Code.Trim().ToUpperInvariant(),
            Name = Input.Name.Trim(),
            Description = Input.Description,
            LocationId = Input.LocationId,
            ParentBoxId = Input.ParentBoxId == 0 ? null : Input.ParentBoxId,
            Status = Input.Status
        };
        db.Boxes.Add(box);
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage("/Boxes/Details", new { code = box.Code });
    }

    private async Task LoadSelects(CancellationToken cancellationToken)
    {
        Locations = await db.Locations.AsNoTracking().OrderBy(l => l.Name)
            .Select(l => new SelectListItem(l.Name, l.Id.ToString()))
            .ToListAsync(cancellationToken);

        ParentBoxes = await db.Boxes.AsNoTracking().OrderBy(b => b.Code)
            .Select(b => new SelectListItem($"{b.Code} · {b.Name}", b.Id.ToString()))
            .ToListAsync(cancellationToken);
        ParentBoxes.Insert(0, new SelectListItem("Ninguna: caja de primer nivel", "0"));
    }

    public class BoxInput
    {
        [Required, MaxLength(40)]
        public string Code { get; set; } = "";

        [Required, MaxLength(160)]
        public string Name { get; set; } = "";

        public string? Description { get; set; }
        public int LocationId { get; set; }
        public int ParentBoxId { get; set; }
        public BoxStatus Status { get; set; } = BoxStatus.Active;
    }
}
