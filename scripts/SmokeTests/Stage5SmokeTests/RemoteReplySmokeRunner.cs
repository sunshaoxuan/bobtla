using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TlaPlugin.Models;

public static class RemoteReplySmokeRunner
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> RunAsync(
        string baseUrl,
        TranslationRequest translationRequest,
        string? tone,
        IEnumerable<string> additionalLanguages,
        CancellationToken cancellationToken,
        HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("baseUrl 不能为空。", nameof(baseUrl));
        }

        if (translationRequest is null)
        {
            throw new ArgumentNullException(nameof(translationRequest));
        }

        if (string.IsNullOrWhiteSpace(translationRequest.UserAssertion))
        {
            throw new ArgumentException("远程模式需要用户断言以填充 Authorization 头。", nameof(translationRequest));
        }

        using var httpClient = handler is null
            ? new HttpClient()
            : new HttpClient(handler, disposeHandler: false);

        httpClient.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", translationRequest.UserAssertion);

        var translateResponse = await httpClient.PostAsJsonAsync("/api/translate", translationRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(translateResponse, "POST /api/translate", cancellationToken).ConfigureAwait(false);

        var translation = await translateResponse.Content.ReadFromJsonAsync<TranslationResult>(SerializerOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("远程 /api/translate 返回了空响应。");

        Console.WriteLine("[Remote] /api/translate 调用成功:");
        Console.WriteLine($"  ModelId:   {translation.ModelId}");
        Console.WriteLine($"  Language:  {translation.TargetLanguage}");
        Console.WriteLine($"  Latency:   {translation.LatencyMs} ms");
        Console.WriteLine($"  CostUsd:   {translation.CostUsd:F4}");
        Console.WriteLine($"  Response:  {translation.TranslatedText}");

        var replyRequest = BuildReplyRequest(translationRequest, translation, tone, additionalLanguages);
        var replyResponse = await httpClient.PostAsJsonAsync("/api/reply", replyRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(replyResponse, "POST /api/reply", cancellationToken).ConfigureAwait(false);

        var reply = await replyResponse.Content.ReadFromJsonAsync<ReplyResult>(SerializerOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("远程 /api/reply 返回了空响应。");

        Console.WriteLine();
        Console.WriteLine("[Remote] /api/reply 调用成功:");
        Console.WriteLine($"  MessageId: {reply.MessageId}");
        Console.WriteLine($"  Status:    {reply.Status}");
        Console.WriteLine($"  Language:  {reply.Language}");
        Console.WriteLine($"  Tone:      {reply.ToneApplied ?? tone ?? TranslationRequest.DefaultTone}");

        var metrics = await httpClient.GetFromJsonAsync<UsageMetricsReport>("/api/metrics", SerializerOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("远程 /api/metrics 返回了空响应。");

        var auditRaw = await httpClient.GetStringAsync("/api/audit", cancellationToken).ConfigureAwait(false);
        using var auditDocument = JsonDocument.Parse(auditRaw);

        Console.WriteLine();
        Console.WriteLine("使用指标摘要:");
        Console.WriteLine(JsonSerializer.Serialize(metrics, SerializerOptions));

        Console.WriteLine();
        Console.WriteLine("审计记录样例:");
        Console.WriteLine(JsonSerializer.Serialize(auditDocument.RootElement, SerializerOptions));

        return 0;
    }

    private static ReplyRequest BuildReplyRequest(
        TranslationRequest translationRequest,
        TranslationResult translation,
        string? tone,
        IEnumerable<string> additionalLanguages)
    {
        var finalTone = tone ?? translationRequest.Tone ?? TranslationRequest.DefaultTone;
        var languages = additionalLanguages?.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            ?? new List<string>();

        var replyRequest = new ReplyRequest
        {
            ThreadId = translationRequest.ThreadId ?? string.Empty,
            ReplyText = translation.TranslatedText,
            Text = translationRequest.Text,
            EditedText = translation.TranslatedText,
            TenantId = translationRequest.TenantId,
            UserId = translationRequest.UserId,
            ChannelId = translationRequest.ChannelId,
            Language = translationRequest.TargetLanguage,
            UiLocale = translationRequest.UiLocale,
            LanguagePolicy = new ReplyLanguagePolicy
            {
                TargetLang = translationRequest.TargetLanguage,
                Tone = finalTone
            },
            AdditionalTargetLanguages = languages
        };

        return replyRequest;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new HttpRequestException($"{operation} 失败，状态码 {(int)response.StatusCode}: {content}");
    }
}
