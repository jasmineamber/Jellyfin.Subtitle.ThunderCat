using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Subtitle.ThunderCat.Infrastructure
{
    internal static class SafeHttpClient
    {
        private const int MaxRedirects = 3;
        private static readonly HttpClient Client = CreateClient();


        public static async Task<string> GetStringAsync(
            Uri uri,
            UrlPolicy policy,
            int maxBytes,
            TimeSpan timeout,
            int maxAttempts,
            string userAgent,
            CancellationToken cancellationToken)
        {
            var bytes = await GetBytesAsync(uri, policy, maxBytes, timeout, maxAttempts, userAgent, cancellationToken)
                .ConfigureAwait(false);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        public static async Task<byte[]> GetBytesAsync(
            Uri uri,
            UrlPolicy policy,
            int maxBytes,
            TimeSpan timeout,
            int maxAttempts,
            string userAgent,
            CancellationToken cancellationToken)
        {
            if (maxBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBytes));
            }

            if (maxAttempts <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            }

            using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutSource.CancelAfter(timeout);
                Exception? lastError = null;

                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        return await SendWithRedirectsAsync(
                                uri,
                                policy,
                                maxBytes,
                                userAgent,
                                timeoutSource.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OperationCanceledException ex)
                    {
                        throw new TimeoutException("The upstream request exceeded its total timeout.", ex);
                    }
                    catch (Exception ex) when (IsTransient(ex) && attempt < maxAttempts)
                    {
                        lastError = ex;
                        var delay = TimeSpan.FromMilliseconds((250 * (1 << (attempt - 1))) + GetJitterMilliseconds());
                        try
                        {
                            await Task.Delay(delay, timeoutSource.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (OperationCanceledException timeoutException)
                        {
                            throw new TimeoutException("The upstream request exceeded its total timeout.", timeoutException);
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        break;
                    }
                }

                throw lastError ?? new HttpRequestException("The upstream request failed.");
            }
        }

        private static async Task<byte[]> SendWithRedirectsAsync(
            Uri initialUri,
            UrlPolicy policy,
            int maxBytes,
            string userAgent,
            CancellationToken cancellationToken)
        {
            var currentUri = initialUri;

            for (var redirectCount = 0; redirectCount <= MaxRedirects; redirectCount++)
            {
                await UrlSafetyValidator.ValidateAsync(currentUri, policy, cancellationToken).ConfigureAwait(false);

                using (var request = new HttpRequestMessage(HttpMethod.Get, currentUri))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

                    using (var response = await Client.SendAsync(
                               request,
                               HttpCompletionOption.ResponseHeadersRead,
                               cancellationToken).ConfigureAwait(false))
                    {
                        var statusCode = (int)response.StatusCode;
                        if (IsRedirect(statusCode))
                        {
                            if (redirectCount == MaxRedirects || response.Headers.Location == null)
                            {
                                throw new HttpRequestException("The upstream returned an invalid redirect chain.");
                            }

                            currentUri = response.Headers.Location.IsAbsoluteUri
                                ? response.Headers.Location
                                : new Uri(currentUri, response.Headers.Location);
                            continue;
                        }

                        if (IsTransientStatus(response.StatusCode))
                        {
                            throw new TransientHttpException(response.StatusCode);
                        }

                        if (!response.IsSuccessStatusCode)
                        {
                            throw new HttpRequestException($"The upstream returned HTTP {statusCode}.");
                        }

                        if (response.Content.Headers.ContentLength.HasValue &&
                            response.Content.Headers.ContentLength.Value > maxBytes)
                        {
                            throw new InvalidDataException("The upstream response exceeds the configured size limit.");
                        }

                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        {
                            return await ReadBoundedAsync(stream, maxBytes, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }

            throw new HttpRequestException("The upstream returned too many redirects.");
        }

        private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
        {
            using (var output = new MemoryStream(Math.Min(maxBytes, 64 * 1024)))
            {
                var buffer = new byte[16 * 1024];
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return output.ToArray();
                    }

                    if (output.Length + read > maxBytes)
                    {
                        throw new InvalidDataException("The upstream response exceeds the configured size limit.");
                    }

                    output.Write(buffer, 0, read);
                }
            }
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseCookies = false
            };

            return new HttpClient(handler, true)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        private static bool IsRedirect(int statusCode) =>
            statusCode == 301 || statusCode == 302 || statusCode == 303 || statusCode == 307 || statusCode == 308;

        private static bool IsTransientStatus(HttpStatusCode statusCode)
        {
            var code = (int)statusCode;
            return code == 408 || code == 429 || code >= 500;
        }

        private static bool IsTransient(Exception exception) =>
            exception is HttpRequestException ||
            exception is SocketException ||
            (exception is IOException && !(exception is InvalidDataException));

        private static int GetJitterMilliseconds()
        {
            var bytes = new byte[1];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return bytes[0] % 101;
        }

        private sealed class TransientHttpException : HttpRequestException
        {
            public TransientHttpException(HttpStatusCode statusCode)
                : base($"The upstream returned transient HTTP {(int)statusCode}.")
            {
            }
        }
    }
}
