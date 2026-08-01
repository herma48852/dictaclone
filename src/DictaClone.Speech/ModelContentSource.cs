using System.Net.Http.Headers;

namespace DictaClone.Speech;

public interface IModelContentSource
{
    Task CopyToAsync(
        Uri source,
        Stream destination,
        IProgress<long>? progress,
        CancellationToken cancellationToken);
}

public sealed class HttpModelContentSource : IModelContentSource, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public HttpModelContentSource()
        : this(new HttpClient(), ownsClient: true)
    {
    }

    public HttpModelContentSource(HttpClient httpClient)
        : this(httpClient, ownsClient: false)
    {
    }

    private HttpModelContentSource(HttpClient httpClient, bool ownsClient)
    {
        _httpClient = httpClient ??
            throw new ArgumentNullException(nameof(httpClient));
        _ownsClient = ownsClient;
    }

    public async Task CopyToAsync(
        Uri source,
        Stream destination,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        using HttpResponseMessage response = await _httpClient
            .GetAsync(
                source,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        ValidateContentType(response.Content.Headers.ContentType);

        await using Stream content = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var buffer = new byte[128 * 1024];
        long received = 0;
        int read;

        while ((read = await content.ReadAsync(
                   buffer,
                   cancellationToken)
               .ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken)
                .ConfigureAwait(false);
            received += read;
            progress?.Report(received);
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private static void ValidateContentType(MediaTypeHeaderValue? contentType)
    {
        if (contentType?.MediaType?.StartsWith(
                "text/html",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new InvalidDataException(
                "The model endpoint returned HTML instead of a model file.");
        }
    }
}
