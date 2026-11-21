using System.Text;
using BijliGuard.Application.Abstractions;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace BijliGuard.Infrastructure.Pdf;

/// <summary>Reads embedded text with PdfPig. Runs in-process, so bills never leave the server.</summary>
public sealed class PdfPigTextReader(ILogger<PdfPigTextReader> logger) : IPdfTextReader
{
    // A bill's billing values are on the first page. Reading more pages costs time without adding anything.
    private const int MaxPages = 3;

    public string? ReadText(byte[] pdf)
    {
        try
        {
            using var document = PdfDocument.Open(pdf);
            var text = new StringBuilder();
            foreach (var page in document.GetPages().Take(MaxPages))
            {
                text.AppendLine(page.Text);
            }
            var result = text.ToString().Trim();
            return result.Length == 0 ? null : result;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Pdf.ReadFailed {ErrorType}", ex.GetType().Name);
            return null;
        }
    }
}
