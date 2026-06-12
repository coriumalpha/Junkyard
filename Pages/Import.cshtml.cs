using System.Text;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Inventario.Pages;

public class ImportModel(CsvInventoryService csv, IWebHostEnvironment env, IConfiguration config) : PageModel
{
    public List<InventoryCsvRow> PreviewRows { get; private set; } = [];
    public string? PendingKey { get; private set; }
    public string? Error { get; private set; }
    public string? Notice { get; private set; }

    [BindProperty]
    public IFormFile? CsvFile { get; set; }

    public IActionResult OnGetTemplate()
        => File(Encoding.UTF8.GetBytes(CsvInventoryService.Template()), "text/csv", "inventario-plantilla.csv");

    public void OnGet()
    {
        Notice = TempData["ImportNotice"] as string;
    }

    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
    {
        if (CsvFile is null || CsvFile.Length == 0)
        {
            Error = "Sube un CSV para previsualizar.";
            return Page();
        }

        try
        {
            using var reader = new StreamReader(CsvFile.OpenReadStream(), Encoding.UTF8);
            var content = await reader.ReadToEndAsync(cancellationToken);
            PreviewRows = csv.Parse(content);
            var root = DataPaths.ImportRoot(env, config);
            Directory.CreateDirectory(root);
            PendingKey = $"pending-{Guid.NewGuid():N}.csv";
            await System.IO.File.WriteAllTextAsync(Path.Combine(root, PendingKey), content, cancellationToken);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(string key, CancellationToken cancellationToken)
    {
        var path = Path.Combine(DataPaths.ImportRoot(env, config), Path.GetFileName(key));
        if (!System.IO.File.Exists(path))
        {
            Error = "No encuentro la previsualizacion pendiente. Vuelve a subir el CSV.";
            return Page();
        }

        try
        {
            var content = await System.IO.File.ReadAllTextAsync(path, cancellationToken);
            var rows = csv.Parse(content);
            await csv.ImportAsync(rows, cancellationToken);
            System.IO.File.Delete(path);
            TempData["ImportNotice"] = $"Importadas {rows.Count} filas.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return Page();
        }
    }
}
