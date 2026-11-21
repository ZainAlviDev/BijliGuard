namespace BijliGuard.Application.Abstractions;

/// <summary>
/// Reads the text stored inside a PDF (for example a bill downloaded from the company's website).
/// This is not OCR: scanned PDFs without a text layer return null, and the user is asked for a photo instead.
/// </summary>
public interface IPdfTextReader
{
    /// <summary>The text of the first pages, or null when the file is not a readable PDF or has no text.</summary>
    string? ReadText(byte[] pdf);
}
