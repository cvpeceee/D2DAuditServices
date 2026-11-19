using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Enterprise;

public sealed class ConfigurableAssistantProvider(
    HttpClient http,
    IConfiguration configuration,
    ILogger<ConfigurableAssistantProvider> logger) : IAssistantProvider
{
    public async Task<AssistantReply> AnswerAsync(
        string question,
        IReadOnlyList<AssistantFact> facts,
        string scopeLabel,
        CancellationToken cancellationToken)
    {
        var deterministic = GroundedAssistant.Answer(question, facts, scopeLabel);
        var endpoint = First(configuration["AI_ENDPOINT"], configuration["AI:Endpoint"]);
        var apiKey = First(configuration["AI_API_KEY"], configuration["AI:ApiKey"]);
        var model = First(configuration["AI_MODEL"], configuration["AI:Model"]);

        if (string.IsNullOrWhiteSpace(endpoint)
            || string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(model)
            || !SafeUrl.IsHttpOrHttps(endpoint))
        {
            return deterministic;
        }

        try
        {
            var contextLines = facts.Take(40).Select(item => $"[{item.Kind}:{item.Code}] {item.Title}: {item.Detail}");
            var payload = new
            {
                model,
                temperature = 0.1,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = $"You are D2D Audit Services compliance assistant scoped strictly to {scopeLabel}. Answer ONLY from the provided workspace records. Cite codes in brackets. If information is not in the records, say so plainly.",
                    },
                    new
                    {
                        role = "user",
                        content = "Records:\n" + string.Join('\n', contextLines) + "\n\nQuestion: " + question,
                    },
                },
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("AI assistant endpoint returned {StatusCode}; falling back to grounded retrieval.", (int)response.StatusCode);
                return deterministic;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                var answer = (content.GetString() ?? "").Trim();
                if (answer.Length > 0)
                {
                    return new AssistantReply(answer, "llm", deterministic.Citations, deterministic.SuggestedRoutes);
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "AI assistant request failed; falling back to grounded retrieval.");
        }

        return deterministic;
    }

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
