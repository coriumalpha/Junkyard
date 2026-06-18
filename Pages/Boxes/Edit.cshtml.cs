using System.ComponentModel.DataAnnotations;
using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
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
    public List<SelectListItem> ContainerTypes { get; private set; } = [];
    public SearchPickerModel ParentBoxPicker { get; private set; } = new();

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
            ContainerType = box.ContainerType,
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
        var normalizedCode = Box.NormalizePublicCode(Input.Code);
        Input.Code = normalizedCode;

        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Code)}", "El CT es obligatorio.");
        }
        else if (await BoxCodeService.IsDuplicateAsync(db, normalizedCode, Input.Id, cancellationToken))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Code)}", "Ese CT ya existe.");
        }

        int? parentBoxId = Input.ParentBoxId == 0 ? null : Input.ParentBoxId;
        var parentValidation = await BoxHierarchyService.ValidateParentAssignmentAsync(db, Input.Id, parentBoxId, cancellationToken);
        if (!parentValidation.IsValid)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ParentBoxId)}", parentValidation.ErrorMessage!);
        }

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

        box.Code = normalizedCode;
        box.Name = Input.Name.Trim();
        box.ContainerType = Box.NormalizeContainerType(Input.ContainerType);
        box.Description = Input.Description;
        box.LocationId = Input.LocationId;
        box.ParentBoxId = parentBoxId;
        box.Status = Input.Status;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Code)}", "Ese CT ya existe.");
            await LoadSelects(Input.Id, cancellationToken);
            return Page();
        }

        return RedirectToPage("/Boxes/Details", new { code = box.Code });
    }

    private async Task LoadSelects(int currentBoxId, CancellationToken cancellationToken)
    {
        ContainerTypes = Box.AvailableContainerTypes()
            .Select(option => new SelectListItem(option.Value, option.Key))
            .ToList();

        Locations = await db.Locations.AsNoTracking().OrderBy(l => l.Name)
            .Select(l => new SelectListItem(l.Name, l.Id.ToString()))
            .ToListAsync(cancellationToken);

        var excluded = await BoxHierarchyService.GetDescendantIdsAsync(db, currentBoxId, cancellationToken);
        excluded.Add(currentBoxId);
        ParentBoxes = await db.Boxes.AsNoTracking()
            .Where(b => !excluded.Contains(b.Id))
            .OrderBy(b => b.Code)
            .Select(b => new SelectListItem($"{b.Code} · {Box.ContainerTypeLabelFor(b.ContainerType)} · {b.Name}", b.Id.ToString()))
            .ToListAsync(cancellationToken);
        ParentBoxes.Insert(0, new SelectListItem("Ninguno: contenedor de primer nivel", "0"));
        ParentBoxPicker = new SearchPickerModel
        {
            InputName = "Input.ParentBoxId",
            InputId = "Input_ParentBoxId",
            Label = "Dentro de otro contenedor",
            Placeholder = "Buscar por CT, nombre, tipo, ubicación o padre...",
            SelectedValue = Input.ParentBoxId.ToString(),
            EmptyLabel = "Primer nivel",
            EmptyHint = "Este contenedor no estará dentro de otro.",
            ClearValue = "0",
            NoneOptionLabel = "Ninguno: contenedor de primer nivel",
            NoneOptionHint = "Mueve el contenedor al nivel raíz.",
            NoneOptionValue = "0",
            NoneOptionIcon = "—",
            Options = await SearchPickerFactory.BuildBoxOptionsAsync(db, cancellationToken, excluded)
        };
    }

    public class BoxInput
    {
        public int Id { get; set; }

        [Required, MaxLength(40)]
        public string Code { get; set; } = "";

        [Required, MaxLength(160)]
        public string Name { get; set; } = "";

        [Required, MaxLength(24)]
        public string ContainerType { get; set; } = Box.DefaultContainerType;

        public string? Description { get; set; }
        public int LocationId { get; set; }
        public int ParentBoxId { get; set; }
        public BoxStatus Status { get; set; } = BoxStatus.Active;
    }
}
