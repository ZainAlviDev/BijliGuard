namespace BijliGuard.Application.Abstractions;

/// <summary>Reads printed or handwritten text from an image. Implemented locally (Tesseract) so bills never leave the server.</summary>
public interface IOcrEngine
{
    /// <summary>The text found in the image, or null when the engine is not available.</summary>
    /// <param name="digitsOnly">Read only the digits 0-9. Used for meter photos, where any other character is noise.</param>
    Task<string?> RecognizeAsync(byte[] image, string contentType, bool digitsOnly, CancellationToken ct);
}
