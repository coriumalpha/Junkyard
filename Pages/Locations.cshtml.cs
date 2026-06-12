using System.ComponentModel.DataAnnotations;
using Inventario.Data;
using Inventario.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages;

public class LocationsModel(InventoryDbContext db) : PageModel
{
    public List<Location> Locations { get; private set; } = [];

    [BindProperty]
    public LocationInput Input { get; set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Locations = await db.Locations
            .AsNoTracking()
            .Include(l => l.Boxes)
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await OnGetAsync(cancellationToken);
            return Page();
        }

        var location = Input.Id == 0
            ? new Location()
            : await db.Locations.FindAsync([Input.Id], cancellationToken);
        if (location is null)
        {
            return NotFound();
        }

        location.Name = Input.Name.Trim();
        location.Description = Input.Description;
        if (Input.Id == 0)
        {
            db.Locations.Add(location);
        }

        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        var location = await db.Locations.Include(l => l.Boxes).FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (location is null)
        {
            return NotFound();
        }

        if (location.Boxes.Count > 0)
        {
            var unassigned = await db.Locations.FirstOrDefaultAsync(l => l.Name == "Ubicación no asignada", cancellationToken);
            if (unassigned is null)
            {
                unassigned = new Location { Name = "Ubicación no asignada", Description = "Destino automático al archivar o eliminar ubicaciones." };
                db.Locations.Add(unassigned);
                await db.SaveChangesAsync(cancellationToken);
            }

            foreach (var box in location.Boxes)
            {
                box.LocationId = unassigned.Id;
            }
            var label = location.Boxes.Count == 1 ? "caja" : "cajas";
            TempData["Notice"] = $"Se movieron {location.Boxes.Count} {label} a Ubicación no asignada.";
        }

        db.Locations.Remove(location);
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage();
    }

    public class LocationInput
    {
        public int Id { get; set; }

        [Required, MaxLength(120)]
        public string Name { get; set; } = "";

        [MaxLength(600)]
        public string? Description { get; set; }
    }
}
