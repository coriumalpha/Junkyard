namespace Inventario.Models;

public class SearchPickerModel
{
    public string InputName { get; init; } = "";
    public string InputId { get; init; } = "";
    public string Label { get; init; } = "";
    public string Placeholder { get; init; } = "Buscar...";
    public string? SelectedValue { get; init; }
    public string EmptyLabel { get; init; } = "Sin seleccionar";
    public string? EmptyHint { get; init; }
    public string ClearValue { get; init; } = "";
    public string? NoneOptionLabel { get; init; }
    public string? NoneOptionHint { get; init; }
    public string NoneOptionValue { get; init; } = "";
    public string NoneOptionIcon { get; init; } = "—";
    public bool SubmitOnEnter { get; init; }
    public string? SubmitButtonSelector { get; init; }
    public bool Compact { get; set; }
    public List<SearchPickerOption> Options { get; init; } = [];
}

public class SearchPickerOption
{
    public string Value { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Meta { get; init; }
    public string? Detail { get; init; }
    public string? ThumbnailUrl { get; init; }
    public string Icon { get; init; } = "•";
    public List<string> Tags { get; init; } = [];
    public string SearchText { get; init; } = "";
}
