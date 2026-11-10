using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ThunderCat.Infrastructure;
using Jellyfin.Plugin.ThunderCat.Utils;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThunderCat.Providers
{
    internal sealed class SubtitleCatResolvedDownload
    {
        public SubtitleCatResolvedDownload(
            byte[] bytes,
            string format,
            string language,
            string variant,
            Uri detailUri,
            Uri downloadUri)
        {
            Bytes = bytes;
            Format = format;
            Language = language;
            Variant = variant;
            DetailUri = detailUri;
            DownloadUri = downloadUri;
        }

        public byte[] Bytes { get; }

        public string Format { get; }

        public string Language { get; }

        public string Variant { get; }

        public Uri DetailUri { get; }

        public Uri DownloadUri { get; }
    }

    internal sealed class SubtitleCatDownloadResolver
    {
        private static readonly UrlPolicy SubtitleCatPolicy =
            new UrlPolicy("subtitlecat.com", "www.subtitlecat.com");

        private readonly ILogger<SubtitleCatProvider> _logger;
        private readonly Func<Uri, CancellationToken, Task<string>> _getDetailAsync;
        private readonly Func<Uri, CancellationToken, Task<byte[]>> _getDownloadAsync;

        public SubtitleCatDownloadResolver(
            ILogger<SubtitleCatProvider> logger,
            Func<Uri, CancellationToken, Task<string>> getDetailAsync,
            Func<Uri, CancellationToken, Task<byte[]>> getDownloadAsync)
        {
            _logger = logger;
            _getDetailAsync = getDetailAsync;
            _getDownloadAsync = getDownloadAsync;
        }

        public async Task<SubtitleCatResolvedDownload> ResolveAsync(
            IReadOnlyList<Uri> candidateUris,
            string requestedLanguage,
            string? displayName,
            string? mediaCode,
            CancellationToken cancellationToken)
        {
            var candidates = DistinctUris(candidateUris);
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException("SubtitleCat returned no candidate detail pages.");
            }

            var languagePreferences = LanguageNormalizer.GetSubtitleCatLanguagePreferences(requestedLanguage);
            var detailHtml = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var failedDetails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Exception? lastError = null;

            foreach (var variant in languagePreferences)
            {
                for (var index = 0; index < candidates.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var detailUri = candidates[index];
                    var detailKey = detailUri.AbsoluteUri;
                    if (failedDetails.Contains(detailKey))
                    {
                        continue;
                    }

                    if (!detailHtml.TryGetValue(detailKey, out var html))
                    {
                        _logger.LogInformation(
                            "[SubtitleCat] Loading candidate detail: rank={Rank}, detailPath={DetailPath}.",
                            index + 1,
                            LogValue.Sanitize(detailUri.AbsolutePath));
                        try
                        {
                            html = await _getDetailAsync(detailUri, cancellationToken).ConfigureAwait(false);
                            detailHtml[detailKey] = html;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            failedDetails.Add(detailKey);
                            lastError = ex;
                            _logger.LogWarning(
                                ex,
                                "[SubtitleCat] Candidate detail failed: rank={Rank}, detailPath={DetailPath}, error={ErrorType}.",
                                index + 1,
                                LogValue.Sanitize(detailUri.AbsolutePath),
                                ex.GetType().Name);
                            continue;
                        }
                    }

                    var link = SubtitleCatHtmlParser.FindDownloadLinkForVariant(
                        html,
                        detailUri,
                        variant);
                    if (link == null)
                    {
                        continue;
                    }

                    try
                    {
                        UrlSafetyValidator.ValidateSyntax(link.Uri, SubtitleCatPolicy);
                        var format = SubtitleFormat.Normalize(null, link.Uri);
                        if (format == null)
                        {
                            throw new InvalidDataException(
                                "SubtitleCat returned an unsupported subtitle format.");
                        }

                        _logger.LogInformation(
                            "[SubtitleCat] Download attempt: item={ItemName}, mediaCode={MediaCode}, requestedLanguage={RequestedLanguage}, variant={Variant}, rank={Rank}, detailPath={DetailPath}, downloadPath={DownloadPath}, format={Format}.",
                            LogValue.Sanitize(displayName),
                            LogValue.Sanitize(mediaCode, 64),
                            LogValue.Sanitize(requestedLanguage, 32),
                            LogValue.Sanitize(link.Variant, 32),
                            index + 1,
                            LogValue.Sanitize(detailUri.AbsolutePath),
                            LogValue.Sanitize(link.Uri.AbsolutePath),
                            format);

                        var bytes = await _getDownloadAsync(link.Uri, cancellationToken).ConfigureAwait(false);
                        var inspection = SubtitleContentValidator.Inspect(bytes);
                        if (!inspection.IsSupportedText)
                        {
                            lastError = new InvalidDataException(
                                "SubtitleCat returned unusable subtitle content: " +
                                inspection.RejectionReason);
                            _logger.LogWarning(
                                "[SubtitleCat] Candidate rejected: variant={Variant}, rank={Rank}, detailPath={DetailPath}, reason={Reason}, replacementCharacters={ReplacementCharacters}, mojibakeMarkers={MojibakeMarkers}, bytes={Bytes}.",
                                LogValue.Sanitize(link.Variant, 32),
                                index + 1,
                                LogValue.Sanitize(detailUri.AbsolutePath),
                                inspection.RejectionReason,
                                inspection.ReplacementCharacterCount,
                                inspection.MojibakeMarkerCount,
                                bytes.Length);
                            continue;
                        }

                        bytes = SubtitleContentNormalizer.Normalize(bytes, format);
                        _logger.LogInformation(
                            "[SubtitleCat] Selected subtitle: item={ItemName}, mediaCode={MediaCode}, requestedLanguage={RequestedLanguage}, variant={Variant}, language={Language}, rank={Rank}, bytes={Bytes}, replacementCharacters=0.",
                            LogValue.Sanitize(displayName),
                            LogValue.Sanitize(mediaCode, 64),
                            LogValue.Sanitize(requestedLanguage, 32),
                            LogValue.Sanitize(link.Variant, 32),
                            link.Language,
                            index + 1,
                            bytes.Length);

                        return new SubtitleCatResolvedDownload(
                            bytes,
                            format,
                            link.Language,
                            link.Variant,
                            detailUri,
                            link.Uri);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (
                        ex is HttpRequestException ||
                        ex is IOException ||
                        ex is SecurityException ||
                        ex is TimeoutException)
                    {
                        lastError = ex;
                        _logger.LogWarning(
                            ex,
                            "[SubtitleCat] Candidate download failed: variant={Variant}, rank={Rank}, detailPath={DetailPath}, downloadPath={DownloadPath}, error={ErrorType}.",
                            LogValue.Sanitize(link.Variant, 32),
                            index + 1,
                            LogValue.Sanitize(detailUri.AbsolutePath),
                            LogValue.Sanitize(link.Uri.AbsolutePath),
                            ex.GetType().Name);
                    }
                }
            }

            throw new InvalidOperationException(
                "SubtitleCat did not return a usable subtitle from any candidate or language variant.",
                lastError);
        }

        private static List<Uri> DistinctUris(IReadOnlyList<Uri> candidateUris)
        {
            var result = new List<Uri>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var uri in candidateUris ?? Array.Empty<Uri>())
            {
                if (uri != null && seen.Add(uri.AbsoluteUri))
                {
                    result.Add(uri);
                }
            }

            return result;
        }
    }
}
