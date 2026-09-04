using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using A2D.AlertMigrator.Application.Remote;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace A2D.AlertMigrator.Infrastructure.Remote;

public sealed class ResilientRemoteHttpClientFactory : IRemoteHttpClientFactory
{
    private const string ClientName = "A2D.Remote";
    private readonly object _gate = new();
    private ServiceProvider? _serviceProvider;
    private string? _lastError;
    private bool _disposed;

    public ResilientRemoteHttpClientFactory(RemoteHttpClientOptions options)
    {
        Configure(options);
    }

    public string? LastError
    {
        get
        {
            lock (_gate)
            {
                return _lastError;
            }
        }
    }

    public void Configure(RemoteHttpClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureValid();

        var services = new ServiceCollection();
        var clientBuilder = services
            .AddHttpClient(ClientName, client => ConfigureClient(client, options))
            .ConfigurePrimaryHttpMessageHandler(() => CreatePrimaryHandler(options));

        clientBuilder.AddStandardResilienceHandler(resilience =>
        {
            var attemptTimeout = TimeSpan.FromSeconds(
                Math.Min(options.RequestTimeoutSeconds - 1, 30));
            resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
            resilience.AttemptTimeout.Timeout = attemptTimeout;
            resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(
                Math.Max(30, attemptTimeout.TotalSeconds * 2));
            resilience.Retry.MaxRetryAttempts = Math.Max(1, options.RetryCount);
            resilience.Retry.Delay = TimeSpan.FromMilliseconds(options.RetryDelayMilliseconds);
            resilience.Retry.BackoffType = DelayBackoffType.Exponential;
            resilience.Retry.UseJitter = true;
            resilience.Retry.DisableForUnsafeHttpMethods();
            if (options.RetryCount == 0)
            {
                resilience.Retry.ShouldHandle = static _ => ValueTask.FromResult(false);
            }
        });

        var newProvider = services.BuildServiceProvider();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var previousProvider = _serviceProvider;
            _serviceProvider = newProvider;
            _lastError = null;
            previousProvider?.Dispose();
        }
    }

    public HttpClient CreateClient() => CreateUnboundClient();

    public async Task<RemoteConnectionTestResult> TestConnectionAsync(
        RemoteConnectionTestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var client = CreateUnboundClient();
            using var message = new HttpRequestMessage(ToHttpMethod(request.Method), request.TestAddress);
            ApplyAuthentication(message, request);
            using var response = await client.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var numericStatus = (int)response.StatusCode;
            var expected = numericStatus == request.ExpectedStatusCode;
            var outcome = RemoteHttpResponseClassifier.Classify(response.StatusCode, request.ExpectedStatusCode);
            var redirectLocation = ResolveRedirectLocation(request.TestAddress, response.Headers.Location);
            var resultMessage = DescribeHttpResult(
                outcome,
                numericStatus,
                response.ReasonPhrase,
                request.ExpectedStatusCode,
                redirectLocation);

            SetLastError(IsFailure(outcome) ? resultMessage : null);
            return new RemoteConnectionTestResult(
                request.Platform,
                outcome,
                TransportSucceeded: true,
                response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                resultMessage,
                ExpectedStatusReceived: expected,
                HttpVersion: $"HTTP/{response.Version}",
                RedirectLocation: redirectLocation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            const string message = "Teste cancelado pelo usuário.";
            SetLastError(message);
            return new RemoteConnectionTestResult(
                request.Platform,
                RemoteConnectionTestOutcome.Cancelled,
                TransportSucceeded: false,
                StatusCode: null,
                stopwatch.ElapsedMilliseconds,
                message);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TimeoutException
            or OperationCanceledException
            or AuthenticationException
            or InvalidOperationException)
        {
            var message = DescribeFailure(exception);
            SetLastError(message);
            return new RemoteConnectionTestResult(
                request.Platform,
                RemoteConnectionTestOutcome.TransportError,
                TransportSucceeded: false,
                StatusCode: null,
                stopwatch.ElapsedMilliseconds,
                message);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _serviceProvider?.Dispose();
            _serviceProvider = null;
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    private static void ConfigureClient(HttpClient client, RemoteHttpClientOptions options)
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestVersion = HttpVersion.Version20;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("A2D.AlertMigrator", "1.0"));

        foreach (var header in options.CustomHeaders)
        {
            if (!client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value))
            {
                throw new ArgumentException($"O cabeçalho HTTP '{header.Key}' não pode ser aplicado.");
            }
        }
    }

    private HttpClient CreateUnboundClient()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var factory = _serviceProvider?.GetRequiredService<IHttpClientFactory>()
                ?? throw new InvalidOperationException("O HTTP Client remoto não foi inicializado.");
            return factory.CreateClient(ClientName);
        }
    }

    private static void ApplyAuthentication(
        HttpRequestMessage message,
        RemoteConnectionTestRequest request)
    {
        switch (request.AuthenticationMode)
        {
            case RemoteAuthenticationMode.None:
                return;
            case RemoteAuthenticationMode.DynatraceApiToken:
                message.Headers.Authorization = new AuthenticationHeaderValue("Api-Token", request.Secret);
                return;
            case RemoteAuthenticationMode.BearerToken:
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.Secret);
                return;
            case RemoteAuthenticationMode.Basic:
                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{request.Username}:{request.Secret}"));
                message.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private static HttpMethod ToHttpMethod(RemoteTestMethod method) => method switch
    {
        RemoteTestMethod.Get => HttpMethod.Get,
        RemoteTestMethod.Head => HttpMethod.Head,
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    private static SocketsHttpHandler CreatePrimaryHandler(RemoteHttpClientOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(options.ConnectTimeoutSeconds),
            MaxConnectionsPerServer = options.MaxConnectionsPerServer,
            PooledConnectionLifetime = TimeSpan.FromMinutes(options.PooledConnectionLifetimeMinutes),
            UseCookies = false,
            UseProxy = options.ProxyMode != RemoteProxyMode.Disabled,
            DefaultProxyCredentials = options.UseDefaultProxyCredentials
                ? CredentialCache.DefaultCredentials
                : null
        };

        if (options.ProxyMode == RemoteProxyMode.Custom)
        {
            handler.Proxy = new WebProxy(options.CustomProxyAddress!);
        }

        handler.SslOptions.CertificateRevocationCheckMode = options.CheckCertificateRevocation
            ? X509RevocationMode.Online
            : X509RevocationMode.NoCheck;

        switch (options.TlsValidationMode)
        {
            case CertificateValidationMode.SystemTrust:
                break;
            case CertificateValidationMode.CustomCertificateAuthority:
                var rootCertificate = X509CertificateLoader.LoadCertificateFromFile(
                    Path.GetFullPath(options.CustomCertificateAuthorityPath!));
                handler.SslOptions.RemoteCertificateValidationCallback =
                    (_, certificate, _, errors) => ValidateWithCustomRoot(
                        certificate,
                        rootCertificate,
                        options.CheckCertificateRevocation,
                        errors);
                break;
            case CertificateValidationMode.Sha256Pinning:
                var expectedHash = RemoteHttpClientOptions.NormalizeSha256(options.PinnedCertificateSha256);
                handler.SslOptions.RemoteCertificateValidationCallback =
                    (_, certificate, _, errors) => CertificateMatchesPin(certificate, expectedHash, errors);
                break;
            case CertificateValidationMode.DangerousAcceptAny:
                handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options));
        }

        return handler;
    }

    private static bool ValidateWithCustomRoot(
        X509Certificate? certificate,
        X509Certificate2 rootCertificate,
        bool checkRevocation,
        SslPolicyErrors errors)
    {
        if (certificate is null
            || errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch)
            || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
        {
            return false;
        }

        using var serverCertificate = X509CertificateLoader.LoadCertificate(
            certificate.Export(X509ContentType.Cert));
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(rootCertificate);
        chain.ChainPolicy.RevocationMode = checkRevocation
            ? X509RevocationMode.Online
            : X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        return chain.Build(serverCertificate);
    }

    private static bool CertificateMatchesPin(
        X509Certificate? certificate,
        string expectedHash,
        SslPolicyErrors errors)
    {
        if (certificate is null
            || errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch)
            || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
        {
            return false;
        }

        using var serverCertificate = X509CertificateLoader.LoadCertificate(
            certificate.Export(X509ContentType.Cert));
        var actualHash = serverCertificate.GetCertHashString(HashAlgorithmName.SHA256);
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actualHash),
            Convert.FromHexString(expectedHash));
    }

    private void SetLastError(string? error)
    {
        lock (_gate)
        {
            _lastError = error;
        }
    }

    private static string DescribeFailure(Exception exception)
    {
        var details = exception.GetBaseException().Message;
        return exception is OperationCanceledException
            ? "O HTTP Client excedeu o timeout configurado."
            : $"Falha de transporte HTTP/TLS: {details}";
    }

    private static bool IsFailure(RemoteConnectionTestOutcome outcome) => outcome is
        RemoteConnectionTestOutcome.AuthenticationRejected or
        RemoteConnectionTestOutcome.AccessDenied or
        RemoteConnectionTestOutcome.HttpError or
        RemoteConnectionTestOutcome.TransportError;

    private static string ResolveRedirectLocation(Uri requestAddress, Uri? location)
    {
        if (location is null)
        {
            return string.Empty;
        }

        return location.IsAbsoluteUri
            ? location.AbsoluteUri
            : new Uri(requestAddress, location).AbsoluteUri;
    }

    private static string DescribeHttpResult(
        RemoteConnectionTestOutcome outcome,
        int statusCode,
        string? reasonPhrase,
        int expectedStatusCode,
        string redirectLocation) => outcome switch
    {
        RemoteConnectionTestOutcome.Success =>
            "O endpoint respondeu com o status esperado. Transporte e chamada autenticada foram concluídos.",
        RemoteConnectionTestOutcome.SuccessWithUnexpectedStatus =>
            $"A API respondeu com sucesso HTTP {statusCode}, embora o status configurado seja {expectedStatusCode}.",
        RemoteConnectionTestOutcome.Redirect when redirectLocation.Length > 0 =>
            $"O servidor respondeu HTTP {statusCode} e indicou outra URL. O redirecionamento não foi seguido para proteger a credencial.",
        RemoteConnectionTestOutcome.Redirect =>
            $"O servidor respondeu HTTP {statusCode}, mas não informou o destino do redirecionamento.",
        RemoteConnectionTestOutcome.AuthenticationRejected =>
            "O servidor rejeitou a credencial. Confirme o tipo de autenticação, o token, a conta e o ambiente.",
        RemoteConnectionTestOutcome.AccessDenied =>
            "A conexão foi aceita, mas o servidor negou acesso. Revise os escopos do token, a política IAM e a propriedade do objeto.",
        _ =>
            $"A API respondeu HTTP {statusCode} ({reasonPhrase ?? "sem descrição"}); o status configurado é {expectedStatusCode}."
    };

    private static string GetPlatformLabel(RemotePlatform platform) => platform switch
    {
        RemotePlatform.Dynatrace => "Dynatrace",
        RemotePlatform.AppDynamics => "AppDynamics",
        _ => platform.ToString()
    };
}
