using Inventario.Data;
using Inventario.Models;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Inventario.Pages;

public class PendientesModel(InventoryDbContext db) : PageModel
{
    public List<InventoryActionRow> OpenActions { get; private set; } = [];
    public List<InventoryActionRow> CompletedActions { get; private set; } = [];
    public List<SelectListItem> PriorityFilters { get; private set; } = [];
    public string Query { get; private set; } = "";
    public int? PriorityFilter { get; private set; }
    public bool ShowCompleted { get; private set; }
    public int? SelectedBoxId { get; private set; }
    public int? SelectedItemId { get; private set; }
    public SearchPickerModel BoxPicker { get; private set; } = new();
    public SearchPickerModel ItemPicker { get; private set; } = new();

    [BindProperty]
    public NewActionInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string? q, int? priority, bool showCompleted, int? boxId, int? itemId, CancellationToken cancellationToken)
    {
        await LoadPageStateAsync(q, priority, showCompleted, boxId, itemId, preserveInput: false, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(string? q, int? priority, bool showCompleted, int? boxId, int? itemId, CancellationToken cancellationToken)
    {
        if (!await ValidateLinkedEntityAsync(cancellationToken))
        {
            await LoadPageStateAsync(q, priority, showCompleted, boxId, itemId, preserveInput: true, cancellationToken);
            return Page();
        }

        if (!ModelState.IsValid)
        {
            await LoadPageStateAsync(q, priority, showCompleted, boxId, itemId, preserveInput: true, cancellationToken);
            return Page();
        }

        var action = new InventoryAction
        {
            Title = Input.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim(),
            Kind = Input.Kind,
            Priority = Math.Clamp(Input.Priority, 1, 5),
            LinkedEntityType = Input.LinkedEntityType,
            LinkedEntityId = Input.LinkedEntityType switch
            {
                InventoryActionLinkedEntityType.Box => Input.BoxId,
                InventoryActionLinkedEntityType.Item => Input.ItemId,
                _ => null
            }
        };

        db.InventoryActions.Add(action);
        await db.SaveChangesAsync(cancellationToken);
        TempData["InventoryActionMessage"] = "Acción guardada.";
        return RedirectToPage(new { q, priority, showCompleted, boxId, itemId });
    }

    public async Task<IActionResult> OnPostCompleteAsync(int id, string? q, int? priority, bool showCompleted, int? boxId, int? itemId, CancellationToken cancellationToken)
    {
        var action = await db.InventoryActions.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (action is null)
        {
            return NotFound();
        }

        action.Status = InventoryActionStatus.Completed;
        action.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage(new { q, priority, showCompleted, boxId, itemId });
    }

    public async Task<IActionResult> OnPostReopenAsync(int id, string? q, int? priority, bool showCompleted, int? boxId, int? itemId, CancellationToken cancellationToken)
    {
        var action = await db.InventoryActions.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (action is null)
        {
            return NotFound();
        }

        action.Status = InventoryActionStatus.Open;
        action.CompletedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage(new { q, priority, showCompleted, boxId, itemId });
    }

    private async Task LoadPageStateAsync(string? q, int? priority, bool showCompleted, int? boxId, int? itemId, bool preserveInput, CancellationToken cancellationToken)
    {
        Query = (q ?? "").Trim();
        PriorityFilter = priority;
        ShowCompleted = showCompleted;
        SelectedBoxId = boxId;
        SelectedItemId = itemId;
        if (!preserveInput)
        {
            Input.LinkedEntityType = itemId is int ? InventoryActionLinkedEntityType.Item : boxId is int ? InventoryActionLinkedEntityType.Box : Input.LinkedEntityType;
            Input.BoxId = boxId;
            Input.ItemId = itemId;
        }

        PriorityFilters = Enumerable.Range(1, 5)
            .Select(value => new SelectListItem(value.ToString(), value.ToString(), PriorityFilter == value))
            .ToList();

        var query = db.InventoryActions.AsNoTracking().AsQueryable();
        query = query.Where(action => action.Kind == InventoryActionKind.Task);
        if (!string.IsNullOrWhiteSpace(Query))
        {
            var term = Query.ToLowerInvariant();
            query = query.Where(action =>
                action.Title.ToLower().Contains(term) ||
                (action.Description != null && action.Description.ToLower().Contains(term)));
        }

        if (PriorityFilter is int selectedPriority)
        {
            query = query.Where(action => action.Priority == selectedPriority);
        }

        var actions = await query
            .OrderBy(action => action.Status == InventoryActionStatus.Completed)
            .ThenByDescending(action => action.Priority)
            .ThenByDescending(action => action.CreatedAt)
            .ToListAsync(cancellationToken);

        var boxIds = actions
            .Where(action => action.LinkedEntityType == InventoryActionLinkedEntityType.Box && action.LinkedEntityId.HasValue)
            .Select(action => action.LinkedEntityId!.Value)
            .Distinct()
            .ToList();
        var itemIds = actions
            .Where(action => action.LinkedEntityType == InventoryActionLinkedEntityType.Item && action.LinkedEntityId.HasValue)
            .Select(action => action.LinkedEntityId!.Value)
            .Distinct()
            .ToList();

        var boxes = boxIds.Count == 0
            ? new Dictionary<int, Box>()
            : await db.Boxes.AsNoTracking()
                .Where(box => boxIds.Contains(box.Id))
                .ToDictionaryAsync(box => box.Id, cancellationToken);
        var items = itemIds.Count == 0
            ? new Dictionary<int, Item>()
            : await db.Items.AsNoTracking()
                .Include(item => item.Box)
                .Where(item => itemIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, cancellationToken);

        InventoryActionRow ToRow(InventoryAction action)
        {
            var linkedLabel = action.LinkedEntityType switch
            {
                InventoryActionLinkedEntityType.Box when action.LinkedEntityId is int linkedBoxId && boxes.TryGetValue(linkedBoxId, out var box) => $"{box.Code} · {box.Name}",
                InventoryActionLinkedEntityType.Item when action.LinkedEntityId is int linkedItemId && items.TryGetValue(linkedItemId, out var item) => item.Box is null ? item.Name : $"{item.Name} · {item.Box.Code}",
                _ => "Sin vínculo"
            };
            var linkedUrl = action.LinkedEntityType switch
            {
                InventoryActionLinkedEntityType.Box when action.LinkedEntityId is int linkedBoxId && boxes.TryGetValue(linkedBoxId, out var box) => Url.Page("/Boxes/Details", null, new { code = box.Code }),
                InventoryActionLinkedEntityType.Item when action.LinkedEntityId is int linkedItemId && items.TryGetValue(linkedItemId, out var item) => Url.Page("/Items/Edit", null, new { id = item.Id }),
                _ => null
            };
            return new InventoryActionRow(action.Id, action.Title, action.Description, action.Priority, action.Status, linkedLabel, linkedUrl);
        }

        OpenActions = actions.Where(action => action.Status == InventoryActionStatus.Open).Select(ToRow).ToList();
        CompletedActions = actions.Where(action => action.Status == InventoryActionStatus.Completed).Select(ToRow).ToList();

        await LoadPickersAsync(Input.BoxId, Input.ItemId, cancellationToken);
    }

    private async Task<bool> ValidateLinkedEntityAsync(CancellationToken cancellationToken)
    {
        if (Input.LinkedEntityType == InventoryActionLinkedEntityType.Box)
        {
            if (Input.BoxId is not int boxId || !await db.Boxes.IgnoreQueryFilters().AnyAsync(box => box.Id == boxId, cancellationToken))
            {
                ModelState.AddModelError(nameof(Input.BoxId), "Selecciona un contenedor válido.");
                return false;
            }
        }

        if (Input.LinkedEntityType == InventoryActionLinkedEntityType.Item)
        {
            if (Input.ItemId is not int itemId || !await db.Items.IgnoreQueryFilters().AnyAsync(item => item.Id == itemId, cancellationToken))
            {
                ModelState.AddModelError(nameof(Input.ItemId), "Selecciona un ítem válido.");
                return false;
            }
        }

        return true;
    }

    private async Task LoadPickersAsync(int? boxId, int? itemId, CancellationToken cancellationToken)
    {
        BoxPicker = new SearchPickerModel
        {
            InputName = nameof(NewActionInput.BoxId),
            InputId = nameof(NewActionInput.BoxId),
            Label = "Contenedor",
            Placeholder = "Buscar CT, nombre, tipo, ubicación o padre...",
            SelectedValue = boxId?.ToString(),
            EmptyLabel = "Sin contenedor",
            EmptyHint = "Sólo úsalo si la acción cuelga de una caja concreta.",
            ClearValue = "",
            Options = await SearchPickerFactory.BuildBoxOptionsAsync(db, cancellationToken)
        };

        ItemPicker = new SearchPickerModel
        {
            InputName = nameof(NewActionInput.ItemId),
            InputId = nameof(NewActionInput.ItemId),
            Label = "Ítem",
            Placeholder = "Buscar por nombre, categoría o contenedor...",
            SelectedValue = itemId?.ToString(),
            EmptyLabel = "Sin ítem",
            EmptyHint = "Sólo úsalo si la acción cuelga de un objeto concreto.",
            ClearValue = "",
            Options = await SearchPickerFactory.BuildItemOptionsAsync(db, cancellationToken)
        };
    }

    public record InventoryActionRow(int Id, string Title, string? Description, int Priority, InventoryActionStatus Status, string LinkedLabel, string? LinkedUrl);

    public class NewActionInput
    {
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(180)]
        public string Title { get; set; } = "";
        [System.ComponentModel.DataAnnotations.StringLength(1000)]
        public string? Description { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public InventoryActionKind Kind { get; set; } = InventoryActionKind.Task;
        [System.ComponentModel.DataAnnotations.Range(1, 5)]
        public int Priority { get; set; } = 3;
        public InventoryActionLinkedEntityType LinkedEntityType { get; set; } = InventoryActionLinkedEntityType.None;
        public int? BoxId { get; set; }
        public int? ItemId { get; set; }
    }
}
