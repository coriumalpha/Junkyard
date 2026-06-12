using System.ComponentModel.DataAnnotations;
using Inventario.Data;
using Inventario.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages.Boxes;

public class EditModel(InventoryDbContext db) : PageModel
{
    [BindProperty]
    public BoxInput Input { get; set; } = new();

    public List<SelectListItem> Locations { get; private set; } = [];
    public List<SelectListItem> ParentBoxes { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var box = await db.Boxes.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (box is null)
        {
            return NotFound();
        }

        Input = new BoxInput
        {
            Id = box.Id,
            Code = box.Code,
            Name = box.Name,
            Description = box.Description,
            LocationId = box.LocationId,
            ParentBoxId = box.ParentBoxId ?? 0,
            Status = box.Status
        };
        await LoadSelects(box.Id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadSelects(Input.Id, cancellationToken);
            return Page();
        }

        var box = await db.Boxes.FindAsync([Input.Id], cancellationToken);
        if (box is null)
        {
            return NotFound();
        }

        box.Code = Input.Code.Trim().ToUpperInvariant();
        box.Name = Input.Name.Trim();
        box.Description = Input.Description;
        box.LocationId = Input.LocationId;
        box.ParentBoxId = Input.ParentBoxId == 0 ? null : Input.ParentBoxId;
        box.Status = Input.Status;
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage("/Boxes/Details", new { code = box.Code });
    }

    private async Task LoadSelects(int currentBoxId, CancellationToken cancellationToken)
    {
        Locations = await db.Locations.AsNoTracking().OrderBy(l => l.Name)
            .Select(l => new SelectListItem(l.Name, l.Id.ToString()))
            .ToListAsync(cancellationToken);

        var excluded = await GetDescendantIdsAsync(currentBoxId, cancellationToken);
        excluded.Add(currentBoxId);
        ParentBoxes = await db.Boxes.AsNoTracking()
            .Where(b => !excluded.Contains(b.Id))
            .OrderBy(b => b.Code)
            .Select(b => new SelectListItem($"{b.Code} · {b.Name}", b.Id.ToString()))
            .ToListAsync(cancellationToken);
        ParentBoxes.Insert(0, new SelectListItem("Ninguna: caja de primer nivel", "0"));
    }

    private async Task<HashSet<int>> GetDescendantIdsAsync(int boxId, CancellationToken cancellationToken)
    {
        var links = await db.Boxes.AsNoTracking()
            .Where(b => b.ParentBoxId != null)
            .Select(b => new { b.Id, ParentId = b.ParentBoxId!.Value })
            .ToListAsync(cancellationToken);
        var descendants = new HashSet<int>();
        var queue = new Queue<int>(links.Where(x => x.ParentId == boxId).Select(x => x.Id));
        while (queue.TryDequeue(out var id))
        {
            if (!descendants.Add(id))
            {
                continue;
            }

            foreach (var childId in links.Where(x => x.ParentId == id).Select(x => x.Id))
            {
                queue.Enqueue(childId);
            }
        }

        return descendants;
    }

    public class BoxInput
    {
        public int Id { get; set; }

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
