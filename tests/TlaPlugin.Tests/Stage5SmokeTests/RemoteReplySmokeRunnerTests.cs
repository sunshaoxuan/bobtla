using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TlaPlugin.Models;
using Xunit;

namespace TlaPlugin.Tests.Stage5SmokeTests;

public class RemoteReplySmokeRunnerTests
{
    [Fact]
    public async Task RunAsync_PrintsMetricsSummary_WhenRemoteFlowSucceeds()
    {
        var handler = new FakeRemoteHandler();
        var translationRequest = new TranslationRequest
        {
            Text = "hello",
            TargetLanguage = "ja",
            TenantId = "contoso.onmicrosoft.com",
            UserId = "user1",
            ThreadId = "thread-id",
            Tone = TranslationRequest.DefaultTone,
            UiLocale = "ja-JP",
            AdditionalTargetLanguages = new List<string> { "en-US" },
            UserAssertion = "fake-assertion"
        };

        var originalOut = Console.Out;
        var originalErr = Console.Error;
        var stdout = new StringWriter(new StringBuilder());
        var stderr = new StringWriter(new StringBuilder());
        Console.SetOut(stdout);
        Console.SetError(stderr);

        try
        {
            var exitCode = await RemoteReplySmokeRunner.RunAsync(
                "https://remote.example",
                translationRequest,
                "polite",
                translationRequest.AdditionalTargetLanguages,
                CancellationToken.None,
                handler);

            Assert.Equal(0, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }

        var output = stdout.ToString();
        Assert.Contains("使用指标摘要:", output);
        Assert.Contains("\"tenantId\": \"contoso.onmicrosoft.com\"", output);
        Assert.Contains("审计记录样例:", output);
        Assert.True(string.IsNullOrEmpty(stderr.ToString()), "Expected stderr to be empty on success.");
    }

    sealed class FakeRemoteHandler : HttpMessageHandler
    {
        static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;
            if (request.Method == HttpMethod.Post && string.Equals(path, "/api/translate", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, new TranslationResult
                {
                    ModelId = "stub-model",
                    TargetLanguage = "ja",
                    TranslatedText = "テスト",
                    UiLocale = "ja-JP",
                    LatencyMs = 123,
                    CostUsd = 0.01m,
                    AdditionalTranslations = new Dictionary<string, string>
                    {
                        ["en-US"] = "Test"
                    }
                }));
            }

            if (request.Method == HttpMethod.Post && string.Equals(path, "/api/reply", StringComparison.OrdinalIgnoreCase))
            {
                var reply = new ReplyResult("message-1", "Created")
                {
                    Language = "ja"
                };
                return Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, reply));
            }

            if (request.Method == HttpMethod.Get && string.Equals(path, "/api/metrics", StringComparison.OrdinalIgnoreCase))
            {
                var metrics = new UsageMetricsReport(
                    new UsageMetricsOverview(1, 0.01m, 123, Array.Empty<UsageFailureSnapshot>()),
                    new[]
                    {
                        new UsageMetricsSnapshot(
                            "contoso.onmicrosoft.com",
                            1,
                            0.01m,
                            123,
                            DateTimeOffset.UtcNow,
                            new[] { new ModelUsageSnapshot("stub-model", 1, 0.01m) },
                            Array.Empty<UsageFailureSnapshot>())
                    });
                return Task.FromResult(CreateJsonResponse(HttpStatusCode.OK, metrics));
            }

            if (request.Method == HttpMethod.Get && string.Equals(path, "/api/audit", StringComparison.OrdinalIgnoreCase))
            {
                var payload = "[{\"tenantId\":\"contoso.onmicrosoft.com\",\"status\":\"Success\"}]";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        static HttpResponseMessage CreateJsonResponse<T>(HttpStatusCode statusCode, T value)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = JsonContent.Create(value, options: SerializerOptions)
            };
        }
    }
}
