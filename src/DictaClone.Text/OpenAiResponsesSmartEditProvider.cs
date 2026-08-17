using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DictaClone.Core.Contracts;
using DictaClone.Core.Dictation;

namespace DictaClone.Text;

public sealed class OpenAiResponsesSmartEditProvider : ISmartEditProvider
{
    public const string ApiKeySecretName = "smart-edit/openai-api-key";
    public const int MaximumInstructionCharacters = 10_000;
    public const int MaximumSelectionCharacters = 200_000;
    private const int MaximumResponseBytes = 1_048_576;
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(2);
    private readonly HttpClient _httpClient;
    private readonly ISecretStore _secretStore;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public OpenAiResponsesSmartEditProvider(
        HttpClient httpClient,
        ISecretStore secretStore,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _delay = delay ?? Task.Delay;
    }

    public async Task<string> EditAsync(
        SmartEditRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ProviderSettings.Enabled)
        {
            throw new SmartEditNotConfiguredException();
        }

        if (request.Instruction.Length > MaximumInstructionCharacters ||
            request.SelectedText?.Length > MaximumSelectionCharacters)
        {
            throw new SmartEditRequestTooLargeException();
        }

        string? apiKey = await _secretStore
            .ReadAsync(ApiKeySecretName, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new SmartEditNotConfiguredException();
        }

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                timeout.CancelAfter(request.ProviderSettings.RequestTimeout);
                using HttpRequestMessage message = CreateRequest(request, apiKey);
                using HttpResponseMessage response = await _httpClient
                    .SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return await ReadResultAsync(response, timeout.Token)
                        .ConfigureAwait(false);
                }

                TimeSpan? retryAfter = GetRetryAfter(response);
                bool retryable = response.StatusCode == (HttpStatusCode)429 ||
                    (int)response.StatusCode >= 500;
                if (retryable &&
                    attempt < request.ProviderSettings.MaximumRetries)
                {
                    await _delay(
                        BoundRetryDelay(retryAfter, attempt),
                        timeout.Token).ConfigureAwait(false);
                    continue;
                }

                throw MapFailure(response.StatusCode, retryAfter);
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new SmartEditTimeoutException(exception);
            }
            catch (HttpRequestException)
                when (attempt < request.ProviderSettings.MaximumRetries)
            {
                await _delay(
                    BoundRetryDelay(null, attempt),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                throw new SmartEditUnavailableException(exception);
            }
        }
    }

    private static HttpRequestMessage CreateRequest(SmartEditRequest request,
        string apiKey)
    {
        SmartEditPrompt prompt = SmartEditPromptBuilder.Build(request);
        var message = new HttpRequestMessage(
            HttpMethod.Post,
            request.ProviderSettings.Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            apiKey.Trim());
        message.Content = JsonContent.Create(new ResponsesRequest(
            request.ProviderSettings.Model.Trim(),
            prompt.Instructions,
            prompt.Input,
            Store: false,
            new ReasoningOptions("low")));
        return message;
    }

    private static async Task<string> ReadResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            {
                throw new SmartEditResponseException();
            }

            await response.Content.LoadIntoBufferAsync(
                MaximumResponseBytes,
                cancellationToken).ConfigureAwait(false);
            await using Stream content = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(
                content,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("output_text", out JsonElement outputText) &&
                outputText.ValueKind == JsonValueKind.String)
            {
                return RequireResult(outputText.GetString());
            }

            if (root.TryGetProperty("output", out JsonElement output) &&
                output.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in output.EnumerateArray())
                {
                    if (!item.TryGetProperty("content", out JsonElement contentItems) ||
                        contentItems.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (JsonElement contentItem in contentItems.EnumerateArray())
                    {
                        if (contentItem.TryGetProperty("type", out JsonElement type) &&
                            type.GetString() == "output_text" &&
                            contentItem.TryGetProperty("text", out JsonElement text) &&
                            text.ValueKind == JsonValueKind.String)
                        {
                            return RequireResult(text.GetString());
                        }
                    }
                }
            }
        }
        catch (JsonException exception)
        {
            throw new SmartEditResponseException(exception);
        }
        catch (HttpRequestException exception)
        {
            throw new SmartEditResponseException(exception);
        }

        throw new SmartEditResponseException();
    }

    private static string RequireResult(string? value)
    {
        string result = value?.Trim() ?? string.Empty;
        return result.Length == 0
            ? throw new SmartEditResponseException()
            : result;
    }

    private static Exception MapFailure(
        HttpStatusCode statusCode,
        TimeSpan? retryAfter) => statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new SmartEditAuthenticationException(),
            (HttpStatusCode)429 => new SmartEditRateLimitException(retryAfter),
            _ when (int)statusCode >= 500 =>
                new SmartEditUnavailableException(),
            _ => new SmartEditResponseException(),
        };

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? value = response.Headers.RetryAfter;
        if (value?.Delta is TimeSpan delta)
        {
            return delta;
        }

        if (value?.Date is DateTimeOffset date)
        {
            return date - DateTimeOffset.UtcNow;
        }

        return null;
    }

    private static TimeSpan BoundRetryDelay(TimeSpan? requested, int attempt)
    {
        TimeSpan fallback = TimeSpan.FromMilliseconds(200 * (attempt + 1));
        TimeSpan delay = requested is null || requested <= TimeSpan.Zero
            ? fallback
            : requested.Value;
        return delay > MaximumRetryDelay ? MaximumRetryDelay : delay;
    }

    private sealed record ResponsesRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("instructions")] string Instructions,
        [property: JsonPropertyName("input")] string Input,
        [property: JsonPropertyName("store")] bool Store,
        [property: JsonPropertyName("reasoning")] ReasoningOptions Reasoning);

    private sealed record ReasoningOptions(
        [property: JsonPropertyName("effort")] string Effort);
}
