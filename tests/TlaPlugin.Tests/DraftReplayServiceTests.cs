using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class DraftReplayServiceTests
{
    [Fact]
    public async Task ProcessesPendingDraftsAndStoresResult()
    {
        var dbPath = Path.GetTempFileName();
        try
        {
            var options = Options.Create(new PluginOptions
            {
                OfflineDraftConnectionString = $"Data Source={dbPath}"
            });

            var store = new OfflineDraftStore(options);
            var pipeline = new StubTranslationPipeline
            {
                TranslationFactory = request => PipelineExecutionResult.FromTranslation(new TranslationResult
                {
                    RawTranslatedText = request.Text,
                    TranslatedText = $"{request.Text}-完成",
                    SourceLanguage = "en",
                    TargetLanguage = request.TargetLanguage
                })
            };

            var service = new DraftReplayService(store, pipeline, NullLogger<DraftReplayService>.Instance);
            var record = store.SaveDraft(new OfflineDraftRequest
            {
                OriginalText = "需要翻译",
                TargetLanguage = "ja",
                TenantId = "contoso",
                UserId = "user"
            });
            store.MarkProcessing(record.Id);

            await service.ProcessPendingDraftsAsync(CancellationToken.None);

            var saved = Assert.Single(store.ListDrafts("user"));
            Assert.Equal(OfflineDraftStatus.Completed, saved.Status);
            Assert.Equal("需要翻译-完成", saved.ResultText);
            Assert.NotNull(saved.CompletedAt);
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task RetriesUntilDraftFails()
    {
        var dbPath = Path.GetTempFileName();
        try
        {
            var options = Options.Create(new PluginOptions
            {
                OfflineDraftConnectionString = $"Data Source={dbPath}"
            });

            var store = new OfflineDraftStore(options);
            var pipeline = new StubTranslationPipeline
            {
                ExceptionFactory = attempt => new InvalidOperationException($"boom-{attempt}")
            };

            var service = new DraftReplayService(store, pipeline, NullLogger<DraftReplayService>.Instance);
            var record = store.SaveDraft(new OfflineDraftRequest
            {
                OriginalText = "将失败",
                TargetLanguage = "fr",
                TenantId = "contoso",
                UserId = "retry"
            });
            store.MarkProcessing(record.Id);

            await service.ProcessPendingDraftsAsync(CancellationToken.None);
            var first = Assert.Single(store.ListDrafts("retry"));
            Assert.Equal(OfflineDraftStatus.Pending, first.Status);
            Assert.Equal(1, first.Attempts);
            Assert.Equal("boom-1", first.ErrorReason);

            await service.ProcessPendingDraftsAsync(CancellationToken.None);
            var second = Assert.Single(store.ListDrafts("retry"));
            Assert.Equal(OfflineDraftStatus.Pending, second.Status);
            Assert.Equal(2, second.Attempts);
            Assert.Equal("boom-2", second.ErrorReason);

            await service.ProcessPendingDraftsAsync(CancellationToken.None);
            var failed = Assert.Single(store.ListDrafts("retry"));
            Assert.Equal(OfflineDraftStatus.Failed, failed.Status);
            Assert.Equal(3, failed.Attempts);
            Assert.Equal("boom-3", failed.ErrorReason);
            Assert.NotNull(failed.CompletedAt);
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task CleanupRemovesExpiredDrafts()
    {
        var dbPath = Path.GetTempFileName();
        try
        {
            var options = Options.Create(new PluginOptions
            {
                OfflineDraftConnectionString = $"Data Source={dbPath}",
                DraftRetention = TimeSpan.FromHours(1)
            });

            var store = new OfflineDraftStore(options);
            var pipeline = new StubTranslationPipeline
            {
                TranslationFactory = request => PipelineExecutionResult.FromTranslation(new TranslationResult
                {
                    RawTranslatedText = request.Text,
                    TranslatedText = request.Text,
                    SourceLanguage = "en",
                    TargetLanguage = request.TargetLanguage
                })
            };

            var service = new DraftReplayService(store, pipeline, NullLogger<DraftReplayService>.Instance);
            var record = store.SaveDraft(new OfflineDraftRequest
            {
                OriginalText = "已完成",
                TargetLanguage = "de",
                TenantId = "contoso",
                UserId = "cleanup"
            });
            store.MarkProcessing(record.Id);
            await service.ProcessPendingDraftsAsync(CancellationToken.None);

            var completed = Assert.Single(store.ListDrafts("cleanup"));
            Assert.Equal(OfflineDraftStatus.Completed, completed.Status);

            // 使记录过期
            using var connection = new SqliteConnection(options.Value.OfflineDraftConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Drafts SET CreatedAt = $createdAt WHERE Id = $id";
            command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds());
            command.Parameters.AddWithValue("$id", completed.Id);
            command.ExecuteNonQuery();

            await service.ProcessPendingDraftsAsync(CancellationToken.None);

            Assert.Empty(store.ListDrafts("cleanup"));
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task ProcessesSegmentedDraftsWithRetryAndMerge()
    {
        var dbPath = Path.GetTempFileName();
        try
        {
            var options = Options.Create(new PluginOptions
            {
                OfflineDraftConnectionString = $"Data Source={dbPath}"
            });

            var store = new OfflineDraftStore(options);
            var jobId = Guid.NewGuid().ToString("N");

            store.EnqueueSegments(
                jobId,
                "contoso",
                "segment",
                "ja",
                new[]
                {
                    new TranslationSegment(0, "第一段"),
                    new TranslationSegment(1, "第二段")
                });

            var pipeline = new StubTranslationPipeline
            {
                ExceptionFactory = attempt => attempt == 1 ? new InvalidOperationException("boom-1") : null,
                TranslationFactory = request => PipelineExecutionResult.FromTranslation(new TranslationResult
                {
                    RawTranslatedText = request.Text,
                    TranslatedText = request.Text + "-完成",
                    SourceLanguage = "zh",
                    TargetLanguage = request.TargetLanguage
                })
            };

            var service = new DraftReplayService(store, pipeline, NullLogger<DraftReplayService>.Instance);

            await service.ProcessPendingDraftsAsync(CancellationToken.None);

            var afterFirst = store.GetDraftsByJob(jobId);
            var firstSegment = afterFirst.Single(segment => segment.SegmentIndex == 0);
            var secondSegment = afterFirst.Single(segment => segment.SegmentIndex == 1);

            Assert.Equal(OfflineDraftStatus.Pending, firstSegment.Status);
            Assert.Equal(1, firstSegment.Attempts);
            Assert.Equal("boom-1", firstSegment.ErrorReason);
            Assert.Null(firstSegment.ResultText);

            Assert.Equal(OfflineDraftStatus.Completed, secondSegment.Status);
            Assert.Equal("第二段-完成", secondSegment.ResultText);
            Assert.Null(secondSegment.AggregatedResult);

            await service.ProcessPendingDraftsAsync(CancellationToken.None);

            var merged = store.GetDraftsByJob(jobId);
            Assert.All(merged, segment =>
            {
                Assert.Equal(OfflineDraftStatus.Completed, segment.Status);
                Assert.Equal("第一段-完成第二段-完成", segment.ResultText);
                Assert.Equal("第一段-完成第二段-完成", segment.AggregatedResult);
                Assert.Null(segment.ErrorReason);
            });

            var attempts = merged.Single(segment => segment.SegmentIndex == 0).Attempts;
            Assert.Equal(2, attempts);
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    private sealed class StubTranslationPipeline : ITranslationPipeline
    {
        public Func<TranslationRequest, PipelineExecutionResult>? TranslationFactory { get; init; }
        public Func<int, Exception>? ExceptionFactory { get; init; }
        private int _callCount;

        public Task<DetectionResult> DetectAsync(LanguageDetectionRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<PipelineExecutionResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
            => TranslateAsync(request, (DetectionResult?)null, cancellationToken);

        public Task<PipelineExecutionResult> TranslateAsync(TranslationRequest request, DetectionResult? detection, CancellationToken cancellationToken)
        {
            _callCount++;
            if (ExceptionFactory is not null)
            {
                var exception = ExceptionFactory(_callCount);
                if (exception is not null)
                {
                    throw exception;
                }
            }

            var factory = TranslationFactory ?? (_ => PipelineExecutionResult.FromTranslation(new TranslationResult
            {
                RawTranslatedText = request.Text,
                TranslatedText = request.Text,
                SourceLanguage = "en",
                TargetLanguage = request.TargetLanguage
            }));
            return Task.FromResult(factory(request));
        }

        public OfflineDraftRecord SaveDraft(OfflineDraftRequest request)
            => throw new NotImplementedException();

        public OfflineDraftRecord MarkDraftProcessing(long draftId)
            => throw new NotImplementedException();

        public Task<RewriteResult> RewriteAsync(RewriteRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ReplyResult> PostReplyAsync(ReplyRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<ReplyResult> PostReplyAsync(ReplyRequest request, string finalText, string? toneApplied, CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }
}
