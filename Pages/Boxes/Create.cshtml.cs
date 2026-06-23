using System.ComponentModel.DataAnnotations;
using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
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
    public List<SelectListItem> ContainerTypes { get; private set; } = [];
    public SearchPickerModel ParentBoxPicker { get; private set; } = new();
    public string SuggestedCode { get; private set; } = Box.FormatCtCode(1);
    public string? InheritedLocationName { get; private set; }
    public string? InheritedLocationSourceLabel { get; private set; }
    public bool CanEditLocation => Input.ParentBoxId == 0;

    public async Task OnGetAsync(int? parentBoxId, CancellationToken cancellationToken)
    {
        if (parentBoxId is int id)
        {
            var parent = await db.Boxes.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
            if (parent is not null)
            {
                Input.ParentBoxId = parent.Id;
                var location = await BoxHierarchyService.ResolveLocationAsync(db, parent.Id, cancellationToken);
                Input.LocationId = location?.LocationId ?? parent.LocationId;
                InheritedLocationName = location?.LocationName;
                InheritedLocationSourceLabel = location?.SourceLabel;
            }
        }

        await LoadSelects(cancellationToken);
        SuggestedCode = await BoxCodeService.GetNextCtCodeAsync(db, cancellationToken);
        Input.Code = SuggestedCode;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var normalizedCode = string.IsNullOrWhiteSpace(Input.Code)
            ? await BoxCodeService.GetNextCtCodeAsync(db, cancellationToken)
            : Box.NormalizePublicCode(Input.Code);
        Input.Code = normalizedCode;

        if (await BoxCodeService.IsDuplicateAsync(db, normalizedCode, null, cancellationToken))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Code)}", "Ese CT ya existe.");
        }

        int? parentBoxId = Input.ParentBoxId == 0 ? null : Input.ParentBoxId;
        if (parentBoxId is not null)
        {
            var parentExists = await db.Boxes.AsNoTracking().AnyAsync(b => b.Id == parentBoxId.Value, cancellationToken);
            if (!parentExists)
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ParentBoxId)}", "El contenedor padre ya no existe.");
            }
        }
        else if (Input.LocationId <= 0)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.LocationId)}", "La ubicación es obligatoria para contenedores raíz.");
        }

        if (parentBoxId is not null)
        {
            var location = await BoxHierarchyService.ResolveLocationAsync(db, parentBoxId.Value, cancellationToken);
            if (location is not null && location.LocationId is int locationId)
            {
                Input.LocationId = locationId;
                InheritedLocationName = location.LocationName;
                InheritedLocationSourceLabel = location.SourceLabel;
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadSelects(cancellationToken);
            SuggestedCode = await BoxCodeService.GetNextCtCodeAsync(db, cancellationToken);
            if (string.IsNullOrWhiteSpace(Input.Code))
            {
                Input.Code = SuggestedCode;
            }
            return Page();
        }

        var box = new Box
        {
            Code = normalizedCode,
            Name = Input.Name.Trim(),
            ContainerType = Box.NormalizeContainerType(Input.ContainerType),
            Description = Input.Description,
            LocationId = Input.LocationId,
            ParentBoxId = parentBoxId,
            Status = Input.Status
        };
        db.Boxes.Add(box);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Code)}", "Ese CT ya existe.");
            await LoadSelects(cancellationToken);
            SuggestedCode = await BoxCodeService.GetNextCtCodeAsync(db, cancellationToken);
            if (string.IsNullOrWhiteSpace(Input.Code))
            {
                Input.Code = SuggestedCode;
            }
            return Page();
        }

        return RedirectToPage("/Boxes/Details", new { code = box.Code });
    }

    private async Task LoadSelects(CancellationToken cancellationToken)
    {
        ContainerTypes = Box.AvailableContainerTypes()
            .Select(option => new SelectListItem(option.Value, option.Key))
            .ToList();

        Locations = await db.Locations.AsNoTracking().OrderBy(l => l.Name)
            .Select(l => new SelectListItem(l.Name, l.Id.ToString()))
            .ToListAsync(cancellationToken);

        ParentBoxes = await db.Boxes.AsNoTracking().OrderBy(b => b.Code)
            .Select(b => new SelectListItem($"{b.Code} · {Box.ContainerTypeLabelFor(b.ContainerType)} · {b.Name}", b.Id.ToString()))
            .ToListAsync(cancellationToken);
        ParentBoxes.Insert(0, new SelectListItem("Ninguno: contenedor de primer nivel", "0"));
        if (Input.ParentBoxId > 0 && string.IsNullOrWhiteSpace(InheritedLocationName))
        {
            var location = await BoxHierarchyService.ResolveLocationAsync(db, Input.ParentBoxId, cancellationToken);
            InheritedLocationName = location?.LocationName;
            InheritedLocationSourceLabel = location?.SourceLabel;
        }
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
            NoneOptionHint = "Crea el contenedor en el nivel raíz.",
            NoneOptionValue = "0",
            NoneOptionIcon = "—",
            Options = await SearchPickerFactory.BuildBoxOptionsAsync(db, cancellationToken)
        };
    }

    public class BoxInput
    {
        [MaxLength(40)]
        public string? Code { get; set; }

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
