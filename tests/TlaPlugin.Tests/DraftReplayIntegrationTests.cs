using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class DraftReplayIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private sealed record OfflineDraftListResponse(List<OfflineDraftRecord> Drafts);

    public DraftReplayIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BackgroundServiceProcessesDraftSuccessfully()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"draft-success-{Guid.NewGuid():N}.db");
        try
        {
            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.PostConfigure<PluginOptions>(options =>
                    {
                        options.OfflineDraftConnectionString = $"Data Source={dbPath}";
                        options.DraftReplayPollingInterval = TimeSpan.FromMilliseconds(100);
                        options.DraftReplayMaxAttempts = 5;
                    });

                    services.RemoveAll<ITranslationPipeline>();
                    services.AddSingleton<ITranslationPipeline>(sp => new TestTranslationPipeline(sp.GetRequiredService<OfflineDraftStore>())
                    {
                        TranslateFactory = (_, request) => PipelineExecutionResult.FromTranslation(new TranslationResult
                        {
                            RawTranslatedText = request.Text,
                            TranslatedText = $"{request.Text}-完成",
                            SourceLanguage = request.SourceLanguage ?? "en",
                            TargetLanguage = request.TargetLanguage
                        })
                    });
                });
            });

            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token");

            var draftRequest = new OfflineDraftRequest
            {
                OriginalText = "待翻译文本",
                TargetLanguage = "ja",
                TenantId = "contoso",
                UserId = "integration-success"
            };

            var createResponse = await client.PostAsJsonAsync("/api/offline-draft", draftRequest);
            createResponse.EnsureSuccessStatusCode();

            var completed = await WaitForDraftAsync(
                client,
                draftRequest.UserId,
                record => record.Status == OfflineDraftStatus.Completed,
                TimeSpan.FromSeconds(5));

            Assert.Equal(OfflineDraftStatus.Completed, completed.Status);
            Assert.Equal("integration-success", completed.UserId);
            Assert.Equal("待翻译文本-完成", completed.ResultText);
            Assert.NotNull(completed.CompletedAt);

            var pipeline = factory.Services.GetRequiredService<ITranslationPipeline>() as TestTranslationPipeline;
            Assert.NotNull(pipeline);
            Assert.True(pipeline!.CallCount >= 1);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task BackgroundServiceRetriesUntilDraftFails()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"draft-failure-{Guid.NewGuid():N}.db");
        try
        {
            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.PostConfigure<PluginOptions>(options =>
                    {
                        options.OfflineDraftConnectionString = $"Data Source={dbPath}";
                        options.DraftReplayPollingInterval = TimeSpan.FromMilliseconds(100);
                        options.DraftReplayMaxAttempts = 2;
                    });

                    services.RemoveAll<ITranslationPipeline>();
                    services.AddSingleton<ITranslationPipeline>(sp => new TestTranslationPipeline(sp.GetRequiredService<OfflineDraftStore>())
                    {
                        ExceptionFactory = attempt => new InvalidOperationException($"boom-{attempt}")
                    });
                });
            });

            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token");

            var draftRequest = new OfflineDraftRequest
            {
                OriginalText = "失败文本",
                TargetLanguage = "fr",
                TenantId = "contoso",
                UserId = "integration-failure"
            };

            var createResponse = await client.PostAsJsonAsync("/api/offline-draft", draftRequest);
            createResponse.EnsureSuccessStatusCode();

            var failed = await WaitForDraftAsync(
                client,
                draftRequest.UserId,
                record => record.Status == OfflineDraftStatus.Failed,
                TimeSpan.FromSeconds(5));

            Assert.Equal(OfflineDraftStatus.Failed, failed.Status);
            Assert.Equal(2, failed.Attempts);
            Assert.Equal("boom-2", failed.ErrorReason);
            Assert.NotNull(failed.CompletedAt);

            var pipeline = factory.Services.GetRequiredService<ITranslationPipeline>() as TestTranslationPipeline;
            Assert.NotNull(pipeline);
            Assert.Equal(2, pipeline!.CallCount);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    private static async Task<OfflineDraftRecord> WaitForDraftAsync(
        HttpClient client,
        string userId,
        Func<OfflineDraftRecord, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/api/offline-draft?userId={Uri.EscapeDataString(userId)}");
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<OfflineDraftListResponse>();
            var draft = payload?.Drafts.Count > 0 ? payload.Drafts[0] : null;
            if (draft is not null && predicate(draft))
            {
                return draft;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Draft for user '{userId}' did not reach the expected state in time.");
    }

    private sealed class TestTranslationPipeline : ITranslationPipeline
    {
        private readonly OfflineDraftStore _store;
        private int _callCount;

        public TestTranslationPipeline(OfflineDraftStore store)
        {
            _store = store;
        }

        public int CallCount => _callCount;

        public Func<int, TranslationRequest, PipelineExecutionResult>? TranslateFactory { get; init; }
        public Func<int, Exception>? ExceptionFactory { get; init; }

        public Task<DetectionResult> DetectAsync(LanguageDetectionRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<PipelineExecutionResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
            => TranslateAsync(request, null, cancellationToken);

        public Task<PipelineExecutionResult> TranslateAsync(TranslationRequest request, DetectionResult? detection, CancellationToken cancellationToken)
        {
            _callCount++;
            if (ExceptionFactory is not null)
            {
                throw ExceptionFactory(_callCount);
            }

            var factory = TranslateFactory ?? ((_, r) => PipelineExecutionResult.FromTranslation(new TranslationResult
            {
                RawTranslatedText = r.Text,
                TranslatedText = r.Text,
                SourceLanguage = r.SourceLanguage ?? "en",
                TargetLanguage = r.TargetLanguage
            }));

            return Task.FromResult(factory(_callCount, request));
        }

        public OfflineDraftRecord SaveDraft(OfflineDraftRequest request) => _store.SaveDraft(request);

        public OfflineDraftRecord MarkDraftProcessing(long draftId) => _store.MarkProcessing(draftId);

        public Task<RewriteResult> RewriteAsync(RewriteRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ReplyResult> PostReplyAsync(ReplyRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ReplyResult> PostReplyAsync(ReplyRequest request, string finalText, string? toneApplied, CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }
}
