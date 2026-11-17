using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Isolates per-file metadata parsing during a library scan. A single corrupt, unsupported or
/// temporarily locked container must never fault the whole scan: previously that exception
/// escaped to the fire-and-forget refresh and left the library empty and the UI stuck loading.
/// </summary>
public static class LibraryParseGuard
{
    /// <summary>
    /// Runs <paramref name="parse"/> and reports whether it completed. Cancellation is never
    /// swallowed — it means the scan itself was stopped, not that one file was bad.
    /// </summary>
    public static async Task<bool> TryParseAsync(
        Func<Task> parse,
        ILogger logger,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            await parse();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The scan was cancelled: propagate so batch processing unwinds.
            throw;
        }
        catch (Exception e)
        {
            // A bad file is best-effort. Log and let the caller fall back to its cached object
            // (or skip the file) instead of aborting the scan. Even a failing logger must not
            // turn a skipped file into a faulted scan.
            try
            {
                logger.LogWarning(e, "Failed to read metadata for '{Path}'; skipping this file.", path);
            }
            catch (Exception)
            {
                // Diagnostics must never become the exception that escapes the callback.
            }

            return false;
        }
    }
}
