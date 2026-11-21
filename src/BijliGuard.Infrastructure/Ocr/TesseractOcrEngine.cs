using System.Diagnostics;
using System.Text;
using BijliGuard.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BijliGuard.Infrastructure.Ocr;

/// <summary>
/// Runs the Tesseract command-line program on a temporary copy of the image. The image never leaves this server.
/// The program path comes from configuration (Ocr:TesseractPath). If it is missing, the engine reports unavailable.
/// </summary>
public sealed class TesseractOcrEngine(IConfiguration configuration, ILogger<TesseractOcrEngine> logger) : IOcrEngine
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public async Task<string?> RecognizeAsync(byte[] image, string contentType, bool digitsOnly, CancellationToken ct)
    {
        var program = configuration["Ocr:TesseractPath"] ?? "tesseract";
        if (program != "tesseract" && !File.Exists(program))
        {
            logger.LogWarning("Ocr.Unavailable {Reason}", "program_not_found");
            return null;
        }

        var extension = contentType == "image/png" ? ".png" : ".jpg";
        var tempFile = Path.Combine(Path.GetTempPath(), $"bijli-ocr-{Guid.NewGuid():N}{extension}");
        try
        {
            await File.WriteAllBytesAsync(tempFile, image, ct);

            var start = new ProcessStartInfo(program)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            // psm 6: a single block of text. It suits a bill better than automatic page layout.
            start.ArgumentList.Add(tempFile);
            start.ArgumentList.Add("stdout");
            start.ArgumentList.Add("--psm");
            start.ArgumentList.Add("6");
            if (digitsOnly)
            {
                start.ArgumentList.Add("-c");
                start.ArgumentList.Add("tessedit_char_whitelist=0123456789");
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                logger.LogWarning("Ocr.Unavailable {Reason}", "process_not_started");
                return null;
            }

            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cancel.CancelAfter(Timeout);
            var output = await process.StandardOutput.ReadToEndAsync(cancel.Token);
            await process.WaitForExitAsync(cancel.Token);

            if (process.ExitCode != 0)
            {
                logger.LogWarning("Ocr.Failed {ExitCode}", process.ExitCode);
                return null;
            }
            return output;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Ocr.Failed {ErrorType}", ex.GetType().Name);
            return null;
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // The temporary file is cleaned up by the OS later; nothing to report to the user.
        }
    }
}
