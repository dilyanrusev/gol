using System.Globalization;
using System.Text;
using GameOfLife.Core;
using GameOfLife.Core.Rle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameOfLife.Web.Pages;

public class IndexModel(SimulationLoop loop) : PageModel
{
    public const long MaxUploadBytes = 4 * 1024 * 1024;

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public int DefaultGridSize => Viewport.DefaultSize;
    public int MinGridSize => Viewport.MinSize;
    public int MaxGridSize => Viewport.MaxSize;
    public int MinSpeed => SimulationLoop.MinGenerationsPerSecond;
    public int MaxSpeed => SimulationLoop.MaxGenerationsPerSecond;
    public int CurrentSpeed => loop.Current.GenerationsPerSecond;

    public void OnGet()
    {
    }

    /// <summary>Uploads an .rle file and makes it the new seed. The simulation is paused afterwards.</summary>
    public async Task<IActionResult> OnPostUploadAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            Error = "Choose an .rle file to upload.";
            return RedirectToPage();
        }
        if (file.Length > MaxUploadBytes)
        {
            Error = $"The file is larger than {MaxUploadBytes / 1024 / 1024} MB.";
            return RedirectToPage();
        }

        try
        {
            using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
            var pattern = RleParser.Parse(await reader.ReadToEndAsync());
            await loop.LoadAsync(pattern);
            Message = $"Loaded {pattern.Name ?? Path.GetFileName(file.FileName)} ({pattern.Cells.Count} cells). Press Start to run it.";
        }
        catch (FormatException ex)
        {
            Error = $"Could not read the RLE file: {ex.Message}";
        }
        catch (EditInProgressException ex)
        {
            Error = ex.Message;
        }
        return RedirectToPage();
    }

    /// <summary>Downloads the current universe as an .rle file.</summary>
    public IActionResult OnGetExport()
    {
        var snapshot = loop.Current;
        try
        {
            var pattern = Pattern.FromUniverseCells(snapshot.Cells, $"Generation {snapshot.Generation}") with
            {
                Comments = new[] { $"Saved from the Game of Life server at generation {snapshot.Generation}, population {snapshot.Population}." },
            };
            var text = RleWriter.Write(pattern);
            var fileName = string.Create(CultureInfo.InvariantCulture, $"life-gen-{snapshot.Generation}.rle");
            return File(Encoding.UTF8.GetBytes(text), "text/plain; charset=utf-8", fileName);
        }
        catch (InvalidOperationException ex)
        {
            Error = ex.Message;
            return RedirectToPage();
        }
    }
}
