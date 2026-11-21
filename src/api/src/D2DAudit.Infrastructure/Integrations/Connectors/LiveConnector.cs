using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;

namespace D2DAudit.Infrastructure.Integrations.Connectors;

/// <summary>A connector failure with a stable, secret-free error code.</summary>
public sealed class ConnectorException : Exception
{
    public ConnectorException()
        : this("connector_failed")
    {
    }

    public ConnectorException(string code)
        : base(code)
    {
        Code = code;
    }

    public ConnectorException(string code, Exception innerException)
        : base(code, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Base class for adapters that call a vendor's documented REST API.</summary>
public abstract class LiveConnector(IHttpClientFactory factory) : IConnector
{
    public const string ClientName = "connectors";

    public abstract string Key { get; }

    public virtual async Task<Result<string>> TestAsync(IReadOnlyDictionary<string, string> config, CancellationToken cancellationToken)
    {
        var collected = await CollectAsync(config, cancellationToken);
        return collected.IsSuccess
            ? Result<string>.Ok($"Connected. {collected.Value!.Count} resources are visible.")
            : Result<string>.Fail(collected.Error!);
    }

    public async Task<Result<IReadOnlyList<NormalizedResource>>> CollectAsync(IReadOnlyDictionary<string, string> config, CancellationToken cancellationToken)
    {
        try
        {
            var call = new ConnectorCall(Key, factory.CreateClient(ClientName), config);
            var resources = await FetchAsync(call, cancellationToken);
            return Result<IReadOnlyList<NormalizedResource>>.Ok(resources);
        }
        catch (ConnectorException exception)
        {
            return Result<IReadOnlyList<NormalizedResource>>.Fail(exception.Code);
        }
        catch (HttpRequestException exception)
        {
            return Result<IReadOnlyList<NormalizedResource>>.Fail(exception.StatusCode is { } status ? "http_" + (int)status : "network_error");
        }
        catch (JsonException)
        {
            return Result<IReadOnlyList<NormalizedResource>>.Fail("invalid_response");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<IReadOnlyList<NormalizedResource>>.Fail("timeout");
        }
    }

    protected abstract Task<IReadOnlyList<NormalizedResource>> FetchAsync(ConnectorCall call, CancellationToken cancellationToken);
}

/// <summary>One collection run: config access, safe URLs, authenticated JSON calls and resource building.</summary>
public sealed class ConnectorCall(string provider, HttpClient client, IReadOnlyDictionary<string, string> config)
{
    public const int MaxPages = 20;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Provider => provider;

    public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;

    public string Require(string key) =>
        Optional(key) ?? throw new ConnectorException("missing_" + key);

    public string? Optional(string key) =>
        config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    /// <summary>Reads a host name or https URL from config and returns a safe origin without a trailing slash.</summary>
    public string BaseUrl(string key, string? fallback = null) =>
        Https(Optional(key) ?? fallback ?? throw new ConnectorException("missing_" + key));

    public static string Https(string raw)
    {
        var value = raw.Trim().TrimEnd('/');
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }

        if (!value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || !SafeUrl.IsPublicHttpOrHttps(value))
        {
            throw new ConnectorException("unsafe_url");
        }

        return value;
    }

    public static string Escape(string value) => Uri.EscapeDataString(value.Trim());

    /// <summary>Only follows pagination links that stay on the configured origin so credentials never leave it.</summary>
    public static string? Within(string? url, string origin) =>
        url is not null && url.StartsWith(origin + "/", StringComparison.OrdinalIgnoreCase) ? url : null;

    public static string Join(params string?[] parts) =>
        string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));

    public static Action<HttpRequestMessage> Bearer(string token) =>
        request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public static Action<HttpRequestMessage> Scheme(string scheme, string token) =>
        request => request.Headers.Authorization = new AuthenticationHeaderValue(scheme, token);

    public static Action<HttpRequestMessage> Basic(string user, string password) =>
        request => request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password)));

    public static Action<HttpRequestMessage> Headers(params (string Name, string Value)[] headers) =>
        request =>
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        };

    public async Task<T> GetAsync<T>(string url, Action<HttpRequestMessage> auth, CancellationToken cancellationToken) =>
        (await SendAsync<T>(HttpMethod.Get, url, null, auth, cancellationToken)).Body;

    public Task<(T Body, HttpResponseHeaders Headers)> GetPageAsync<T>(string url, Action<HttpRequestMessage> auth, CancellationToken cancellationToken) =>
        SendAsync<T>(HttpMethod.Get, url, null, auth, cancellationToken);

    public async Task<T> PostJsonAsync<T>(string url, object body, Action<HttpRequestMessage> auth, CancellationToken cancellationToken) =>
        (await PostJsonPageAsync<T>(url, body, auth, cancellationToken)).Body;

    public Task<(T Body, HttpResponseHeaders Headers)> PostJsonPageAsync<T>(string url, object body, Action<HttpRequestMessage> auth, CancellationToken cancellationToken) =>
        SendAsync<T>(HttpMethod.Post, url, JsonContent.Create(body, body.GetType(), options: Json), auth, cancellationToken);

    public async Task<T> PostFormAsync<T>(string url, IReadOnlyDictionary<string, string> form, Action<HttpRequestMessage> auth, CancellationToken cancellationToken) =>
        (await SendAsync<T>(HttpMethod.Post, url, new FormUrlEncodedContent(form), auth, cancellationToken)).Body;

    /// <summary>Reads an XML response (for vendors such as Qualys) with DTD processing disabled.</summary>
    public async Task<System.Xml.Linq.XDocument> GetXmlAsync(string url, Action<HttpRequestMessage> auth, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Get, url, null, auth);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        using var response = await client.SendAsync(request, cancellationToken);
        Ensure(response);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var settings = new System.Xml.XmlReaderSettings { Async = true, DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = System.Xml.XmlReader.Create(stream, settings);
        try
        {
            return await System.Xml.Linq.XDocument.LoadAsync(reader, System.Xml.Linq.LoadOptions.None, cancellationToken);
        }
        catch (System.Xml.XmlException exception)
        {
            throw new ConnectorException("invalid_response", exception);
        }
    }

    /// <summary>Returns false on 404 so callers can probe optional sub-resources such as branch protection.</summary>
    public async Task<bool> ExistsAsync(string url, Action<HttpRequestMessage> auth, CancellationToken cancellationToken)
    {
        using var request = Build(HttpMethod.Get, url, null, auth);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        Ensure(response);
        return true;
    }

    public NormalizedResource Resource(string kind, string id, string name, params (string Key, object? Value)[] attributes)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in attributes)
        {
            var text = Text(value);
            if (text is not null)
            {
                values[key] = text.Length > 300 ? text[..300] : text;
            }
        }

        var externalId = provider + ":" + id;
        var label = string.IsNullOrWhiteSpace(name) ? id : name;
        return new NormalizedResource(
            provider,
            kind,
            externalId.Length > 200 ? externalId[..200] : externalId,
            label.Length > 300 ? label[..300] : label,
            values);
    }

    public int DaysSince(DateTimeOffset? at) =>
        at is null ? 9999 : Math.Max(0, (int)(Now - at.Value).TotalDays);

    /// <summary>Parses an RFC 8288 Link header and returns the rel="next" target.</summary>
    public static string? NextLink(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        foreach (var pieces in values.SelectMany(value => value.Split(',')).Select(part => part.Split(';')))
        {
            if (pieces.Length > 1 && pieces.Skip(1).Any(piece => piece.Trim().Equals("rel=\"next\"", StringComparison.OrdinalIgnoreCase)))
            {
                return pieces[0].Trim().TrimStart('<').TrimEnd('>');
            }
        }

        return null;
    }

    public static string? Header(HttpResponseHeaders headers, string name) =>
        headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    /// <summary>Reads a loosely typed vendor value (string, number, bool or an object with the given property).</summary>
    public static string? Read(JsonElement element, string? property = null)
    {
        if (property is not null && element.ValueKind == JsonValueKind.Object)
        {
            return element.TryGetProperty(property, out var inner) ? Read(inner) : null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static string? Text(object? value) => value switch
    {
        null => null,
        bool flag => flag ? "true" : "false",
        string text => text,
        DateTimeOffset at => at.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private async Task<(T Body, HttpResponseHeaders Headers)> SendAsync<T>(
        HttpMethod method,
        string url,
        HttpContent? content,
        Action<HttpRequestMessage> auth,
        CancellationToken cancellationToken)
    {
        using var request = Build(method, url, content, auth);
        using var response = await client.SendAsync(request, cancellationToken);
        Ensure(response);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var body = await JsonSerializer.DeserializeAsync<T>(stream, Json, cancellationToken)
            ?? throw new ConnectorException("invalid_response");
        return (body, response.Headers);
    }

    private static HttpRequestMessage Build(HttpMethod method, string url, HttpContent? content, Action<HttpRequestMessage> auth)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || !SafeUrl.IsPublicHttpOrHttps(url))
        {
            throw new ConnectorException("unsafe_url");
        }

        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        auth(request);
        return request;
    }

    private static void Ensure(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new ConnectorException("http_" + (int)response.StatusCode);
        }
    }
}
