using Screenbox.Core.Helpers;

namespace Screenbox.Core.Models;

/// <summary>
/// Everything the user supplied in the "open online link" dialog.
/// </summary>
/// <param name="Url">The normalized URL to open.</param>
/// <param name="UserAgent">Optional <c>User-Agent</c> override for hotlink protected streams.</param>
/// <param name="Referer">Optional <c>Referer</c> value.</param>
/// <param name="Headers">Optional extra HTTP headers, one <c>Name: Value</c> pair per line.</param>
public sealed record OpenUrlRequest(
    string Url,
    string UserAgent = "",
    string Referer = "",
    string Headers = "")
{
    /// <summary>
    /// Gets whether any anti-hotlink field was filled in.
    /// </summary>
    public bool HasNetworkOptions =>
        !string.IsNullOrWhiteSpace(UserAgent) ||
        !string.IsNullOrWhiteSpace(Referer) ||
        !string.IsNullOrWhiteSpace(Headers);

    /// <summary>
    /// Gets the FFmpeg AVFormat options that carry the anti-hotlink fields.
    /// </summary>
    public string[] ToFFmpegOptions() => StreamOptionsHelper.ToFFmpegOptions(UserAgent, Referer, Headers);
}
