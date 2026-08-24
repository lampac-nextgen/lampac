using Shared.Services;
using System.Net;
using System.Net.Http;

namespace Music;

public static class MusicHttp
{
    public static HttpClient CreateClient(string providerId, MusicProxyPurpose purpose = MusicProxyPurpose.Api)
        => new(new RoutingHandler(providerId, purpose));

    public static HttpClient GetTransport(MusicProxyLease lease)
    {
        HttpClientHandler handler = null;

        if (lease?.Enabled == true)
        {
            handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
                Proxy = lease.Proxy,
                UseProxy = true,
                ServerCertificateCustomValidationCallback = Http.AlwaysAllowCertificate
            };
        }

        return FriendlyHttp.MessageClient(
            "base",
            handler,
            out _,
            allowAutoRedirect: true,
            findNoRedirectClient: true);
    }

    public static bool IsProxyFailureStatus(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.ProxyAuthenticationRequired
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    public static bool IsProxyFailure(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is HttpRequestException)
                return true;
        }

        return false;
    }

    sealed class RoutingHandler : HttpMessageHandler
    {
        readonly string providerId;
        readonly MusicProxyPurpose purpose;

        public RoutingHandler(string providerId, MusicProxyPurpose purpose)
        {
            this.providerId = providerId;
            this.purpose = purpose;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var lease = MusicProxyService.Acquire(providerId, purpose);
            var transport = GetTransport(lease);
            using var routedRequest = await CloneRequestAsync(request, cancellationToken).ConfigureAwait(false);

            try
            {
                var response = await transport.SendAsync(routedRequest, cancellationToken).ConfigureAwait(false);

                if (lease.Enabled && IsProxyFailureStatus(response.StatusCode))
                    lease.Failure();
                else
                    lease.Success();

                return response;
            }
            catch (Exception ex)
            {
                if (lease.Enabled && IsProxyFailure(ex))
                    lease.Failure();

                throw;
            }
        }

        static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = request.Version,
                VersionPolicy = request.VersionPolicy
            };

            foreach (var header in request.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

            foreach (var option in request.Options)
                clone.Options.Set(new HttpRequestOptionsKey<object>(option.Key), option.Value);

            if (request.Content != null)
            {
                clone.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
                foreach (var header in request.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }
}
