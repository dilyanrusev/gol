using GameOfLife.Core;
using GameOfLife.Core.Rle;
using GameOfLife.Web.Simulation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace GameOfLife.Web.Pages;

public class EditorModel(SimulationLoop loop, IOptions<GameOfLifeOptions> options) : PageModel
{
    public const int GridSize = 100;
    public const int MaxRleLength = 1024 * 1024;

    [BindProperty] public string Rle { get; set; } = string.Empty;
    [BindProperty] public string? Name { get; set; }

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Rle))
        {
            Error = "Draw at least one cell or paste an RLE pattern.";
            return Page();
        }
        if (Rle.Length > MaxRleLength)
        {
            Error = "The pattern text is too long.";
            return Page();
        }

        try
        {
            var pattern = RleParser.Parse(Rle, options.Value.MaxPopulation);
            if (pattern.Width > GridSize || pattern.Height > GridSize)
            {
                Error = $"The pattern must fit in {GridSize} x {GridSize} cells; this one is {pattern.Width} x {pattern.Height}.";
                return Page();
            }
            if (!string.IsNullOrWhiteSpace(Name))
                pattern = pattern with { Name = Name.Trim() };

            await loop.LoadAsync(pattern);
            TempData["Message"] = $"Loaded {pattern.Cells.Count} cells as the initial state. Press Start to run it.";
            return RedirectToPage("/Index");
        }
        catch (FormatException ex)
        {
            Error = $"Could not read the pattern: {ex.Message}";
            return Page();
        }
        catch (EditInProgressException ex)
        {
            Error = ex.Message;
            return Page();
        }
    }
}
