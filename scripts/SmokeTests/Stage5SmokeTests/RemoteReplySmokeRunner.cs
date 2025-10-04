using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TlaPlugin.Models;

internal static class RemoteReplySmokeRunner
{
    static readonly JsonSerializerOptions RemoteSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true
    };

    internal static async Task<int> RunAsync(
        string baseUrl,
        TranslationRequest translationRequest,
        string tone,
        IReadOnlyList<string> additionalTargets,
        CancellationToken cancellationToken,
        HttpMessageHandler? messageHandler = null)
    {
        if (string.IsNullOrWhiteSpace(translationRequest.UserAssertion))
        {
            Console.Error.WriteLine("远程模式需要提供用户断言 (--assertion <jwt>)。");
            return 13;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            Console.Error.WriteLine($"无效的 baseUrl: {baseUrl}");
            return 14;
        }

        using var httpClient = messageHandler is null
            ? new HttpClient { BaseAddress = baseUri }
            : new HttpClient(messageHandler) { BaseAddress = baseUri };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", translationRequest.UserAssertion);

        Console.WriteLine($"远程 API 模式已启用，目标服务: {baseUri}");

        HttpResponseMessage translateResponse;
        try
        {
            translateResponse = await httpClient.PostAsJsonAsync("/api/translate", translationRequest, RemoteSerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("调用远程 /api/translate 失败: " + ex.Message);
            return 3;
        }

        if (!translateResponse.IsSuccessStatusCode)
        {
            return await ReportRemoteFailureAsync(translateResponse, "/api/translate", 3, cancellationToken).ConfigureAwait(false);
        }

        TranslationResult? translation;
        try
        {
            translation = await translateResponse.Content.ReadFromJsonAsync<TranslationResult>(RemoteSerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("解析远程翻译响应失败: " + ex.Message);
            return 3;
        }

        if (translation is null)
        {
            Console.Error.WriteLine("远程翻译响应为空。");
            return 3;
        }

        Console.WriteLine("远程翻译完成:");
        Console.WriteLine($"  ModelId:       {translation.ModelId}");
        Console.WriteLine($"  TargetLanguage: {translation.TargetLanguage}");
        Console.WriteLine($"  LatencyMs:     {translation.LatencyMs}");
        Console.WriteLine($"  CostUsd:       {translation.CostUsd}");
        Console.WriteLine($"  Text:          {translation.TranslatedText}");

        if (additionalTargets.Count > 0)
        {
            if (translation.AdditionalTranslations.Count == 0)
            {
                Console.Error.WriteLine("远程翻译响应缺少附加语种输出。");
                return 8;
            }

            foreach (var language in additionalTargets)
            {
                if (!translation.AdditionalTranslations.ContainsKey(language))
                {
                    Console.Error.WriteLine($"远程翻译未包含 {language} 的附加译文。");
                    return 9;
                }
            }
        }

        var replyRequest = new ReplyRequest
        {
            ThreadId = translationRequest.ThreadId,
            ReplyText = translation.TranslatedText,
            Text = translation.TranslatedText,
            TenantId = translationRequest.TenantId,
            UserId = translationRequest.UserId,
            ChannelId = translationRequest.ChannelId,
            UiLocale = translation.UiLocale,
            LanguagePolicy = new ReplyLanguagePolicy
            {
                TargetLang = translation.TargetLanguage,
                Tone = tone
            },
            AdditionalTargetLanguages = new List<string>(additionalTargets),
            UserAssertion = translationRequest.UserAssertion
        };

        HttpResponseMessage replyResponse;
        try
        {
            replyResponse = await httpClient.PostAsJsonAsync("/api/reply", replyRequest, RemoteSerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("调用远程 /api/reply 失败: " + ex.Message);
            return 4;
        }

        if (!replyResponse.IsSuccessStatusCode)
        {
            return await ReportRemoteFailureAsync(replyResponse, "/api/reply", 4, cancellationToken).ConfigureAwait(false);
        }

        ReplyResult? replyResult;
        try
        {
            replyResult = await replyResponse.Content.ReadFromJsonAsync<ReplyResult>(RemoteSerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("解析远程回帖响应失败: " + ex.Message);
            return 4;
        }

        if (replyResult is null)
        {
            Console.Error.WriteLine("远程回帖响应为空。");
            return 4;
        }

        Console.WriteLine("远程回帖完成:");
        Console.WriteLine($"  MessageId: {replyResult.MessageId}");
        Console.WriteLine($"  Status:    {replyResult.Status}");
        Console.WriteLine($"  Language:  {replyResult.Language}");

        UsageMetricsReport? metricsReport;
        try
        {
            using var metricsResponse = await httpClient.GetAsync("/api/metrics", cancellationToken).ConfigureAwait(false);
            if (!metricsResponse.IsSuccessStatusCode)
            {
                return await ReportRemoteFailureAsync(metricsResponse, "/api/metrics", 5, cancellationToken).ConfigureAwait(false);
            }

            metricsReport = await metricsResponse.Content.ReadFromJsonAsync<UsageMetricsReport>(RemoteSerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("调用远程 /api/metrics 失败: " + ex.Message);
            return 5;
        }

        if (metricsReport is null)
        {
            Console.Error.WriteLine("远程指标响应为空。");
            return 5;
        }

        var tenantMetrics = metricsReport.Tenants.FirstOrDefault(t => string.Equals(t.TenantId, translationRequest.TenantId, StringComparison.OrdinalIgnoreCase));
        if (tenantMetrics is null || tenantMetrics.Translations == 0)
        {
            Console.Error.WriteLine("远程指标中未找到当前租户的成功记录。");
            return 5;
        }

        Console.WriteLine("使用指标摘要 (/api/metrics):");
        Console.WriteLine(JsonSerializer.Serialize(metricsReport, PrettyJsonOptions));
        Console.WriteLine();

        JsonArray? auditArray;
        try
        {
            using var auditResponse = await httpClient.GetAsync("/api/audit", cancellationToken).ConfigureAwait(false);
            if (!auditResponse.IsSuccessStatusCode)
            {
                return await ReportRemoteFailureAsync(auditResponse, "/api/audit", 6, cancellationToken).ConfigureAwait(false);
            }

            var payload = await auditResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            auditArray = JsonNode.Parse(payload) as JsonArray;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("调用远程 /api/audit 失败: " + ex.Message);
            return 6;
        }

        if (auditArray is null || auditArray.Count == 0)
        {
            Console.Error.WriteLine("远程审计日志为空。");
            return 6;
        }

        var hasTenantLog = auditArray
            .Select(node => node as JsonObject)
            .Any(entry => string.Equals(entry?["tenantId"]?.GetValue<string>(), translationRequest.TenantId, StringComparison.OrdinalIgnoreCase));

        if (!hasTenantLog)
        {
            Console.Error.WriteLine("远程审计日志未记录当前租户。");
            return 6;
        }

        Console.WriteLine("审计记录样例:");
        foreach (var entry in auditArray)
        {
            if (entry is JsonObject obj)
            {
                Console.WriteLine(obj.ToJsonString(PrettyJsonOptions));
            }
            else
            {
                Console.WriteLine(entry?.ToJsonString(PrettyJsonOptions) ?? "null");
            }
        }

        return 0;
    }

    static async Task<int> ReportRemoteFailureAsync(HttpResponseMessage response, string endpoint, int defaultExitCode, CancellationToken cancellationToken)
    {
        var exitCode = MapRemoteStatusToExitCode(response.StatusCode, defaultExitCode);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Console.Error.WriteLine($"远程 {endpoint} 返回 {(int)response.StatusCode} {response.StatusCode}，退出码 {exitCode}。");
        if (!string.IsNullOrWhiteSpace(payload))
        {
            Console.Error.WriteLine(payload);
        }

        return exitCode;
    }

    static int MapRemoteStatusToExitCode(HttpStatusCode statusCode, int fallback)
        => statusCode switch
        {
            HttpStatusCode.Unauthorized => 21,
            HttpStatusCode.Forbidden => 22,
            HttpStatusCode.TooManyRequests => 23,
            HttpStatusCode.PaymentRequired => 24,
            HttpStatusCode.ServiceUnavailable => 25,
            _ => fallback
        };
}
