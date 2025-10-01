using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 背景任务，用于重新执行离线草稿的翻译请求。
/// </summary>
public class DraftReplayService : BackgroundService
{
    private const int MaxBatchSize = 10;
    private readonly OfflineDraftStore _store;
    private readonly ITranslationPipeline _pipeline;
    private readonly ILogger<DraftReplayService> _logger;
    private readonly TimeSpan _pollingInterval;
    private readonly int _maxAttempts;

    public DraftReplayService(
        OfflineDraftStore store,
        ITranslationPipeline pipeline,
        ILogger<DraftReplayService> logger)
    {
        _store = store;
        _pipeline = pipeline;
        _logger = logger;
        _pollingInterval = TimeSpan.FromSeconds(3);
        _maxAttempts = 3;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processedAny = false;
            try
            {
                processedAny = await ProcessPendingDraftsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while replaying offline drafts.");
            }

            if (!processedAny)
            {
                try
                {
                    await Task.Delay(_pollingInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    public async Task<bool> ProcessPendingDraftsAsync(CancellationToken cancellationToken)
    {
        var pending = _store.GetPendingDrafts(MaxBatchSize);
        if (pending.Count == 0)
        {
            _store.Cleanup();
            return false;
        }

        var processed = false;
        foreach (var draft in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attempts = _store.BeginProcessing(draft.Id);
            try
            {
                var request = new TranslationRequest
                {
                    Text = draft.OriginalText,
                    TargetLanguage = draft.TargetLanguage,
                    TenantId = draft.TenantId,
                    UserId = draft.UserId
                };

                var result = await _pipeline.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
                if (result.Translation is { } translation)
                {
                    _store.MarkCompleted(draft.Id, translation.TranslatedText);
                }
                else if (result.RequiresLanguageSelection)
                {
                    _store.MarkFailed(draft.Id, "Language confirmation required.", finalFailure: true);
                }
                else if (result.RequiresGlossaryResolution)
                {
                    _store.MarkFailed(draft.Id, "Glossary decision required.", finalFailure: true);
                }
                else
                {
                    var final = attempts >= _maxAttempts;
                    _store.MarkFailed(draft.Id, "Translation pipeline did not return a result.", final);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var final = attempts >= _maxAttempts;
                _store.MarkFailed(draft.Id, ex.Message, final);
                if (final)
                {
                    _logger.LogError(ex, "Failed to replay draft {DraftId} after {Attempts} attempts.", draft.Id, attempts);
                }
                else
                {
                    _logger.LogWarning(ex, "Failed to replay draft {DraftId}. Will retry.", draft.Id);
                }
            }

            processed = true;
        }

        _store.Cleanup();
        return processed;
    }
}
