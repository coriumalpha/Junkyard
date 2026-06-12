using System.Text;
using Inventario.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Inventario.Pages;

public class ExportModel(CsvInventoryService csv) : PageModel
{
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var content = await csv.ExportAsync(cancellationToken);
        return File(Encoding.UTF8.GetBytes(content), "text/csv", $"inventario-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }
}
