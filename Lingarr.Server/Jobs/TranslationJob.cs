using Hangfire;
using Hangfire.Server;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Services;

namespace Lingarr.Server.Jobs;

public class TranslationJob
{
    private readonly ILogger<TranslationJob> _logger;
    private readonly ISettingService _settings;
    private readonly LingarrDbContext _dbContext;
    private readonly IProgressService _progressService;
    private readonly ISubtitleService _subtitleService;
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly ITranslationRequestService _translationRequestService;

    public TranslationJob(
        ILogger<TranslationJob> logger,
        ISettingService settings,
        LingarrDbContext dbContext,
        IProgressService progressService,
        ISubtitleService subtitleService,
        ITranslationServiceFactory translationServiceFactory,
        ITranslationRequestService translationRequestService)
    {
        _logger = logger;
        _settings = settings;
        _dbContext = dbContext;
        _progressService = progressService;
        _subtitleService = subtitleService;
        _translationServiceFactory = translationServiceFactory;
        _translationRequestService = translationRequestService;
    }

    [AutomaticRetry(Attempts = 5)]
    public async Task Execute(
        PerformContext context,
        TranslationRequest translationRequest,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await _translationRequestService.UpdateTranslationRequest(translationRequest,
                context.BackgroundJob.Id,
                TranslationStatus.InProgress);

            _logger.LogInformation("TranslateJob started for subtitle: |Green|{filePath}|/Green|",
                translationRequest.SubtitleToTranslate);

            var serviceType = await _settings.GetSetting("service_type") ?? "libretranslate";
            var translationService = _translationServiceFactory.CreateTranslationService(serviceType);
            var subtitleTranslator = new SubtitleTranslationService(translationService, _logger, _progressService);

            var subtitles = await _subtitleService.ReadSubtitles(request.SubtitleToTranslate);
            var translatedSubtitles =
                await subtitleTranslator.TranslateSubtitles(subtitles, request, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Translation cancelled for subtitle: {subtitlePath}",
                    request.SubtitleToTranslate);

                request.CompletedAt = DateTime.UtcNow;
                request.Status = TranslationStatus.Cancelled;
                await _dbContext.SaveChangesAsync();

                await _translationRequestService.UpdateActiveCount();
                await _progressService.Emit(request, 0, false);
                return;
            }

            var outputPath = _subtitleService.CreateFilePath(
                request.SubtitleToTranslate,
                request.TargetLanguage);

            await _subtitleService.WriteSubtitles(outputPath, translatedSubtitles);

            _logger.LogInformation("TranslateJob completed and created subtitle: |Green|{filePath}|/Green|",
                outputPath);
            await _progressService.Emit(request, 100, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during the translation job for subtitle: {filePath}",
                translationRequest.SubtitleToTranslate);

            // Check the number of remaining retries
            var retryCount = context.GetJobParameter<int>("RetryCount"); // Current retry count
            var maxRetries = context.GetJobParameter<int>("MaxRetryCount"); // Max retries allowed

            if (retryCount >= maxRetries)
            {
                // This is the last retry attempt; mark the translation request as failed
                var failedRequest = await _translationRequestService.UpdateTranslationRequest(
                    translationRequest,
                    context.BackgroundJob.Id,
                    TranslationStatus.Failed);

                failedRequest.CompletedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }

            throw; // Rethrow the exception to allow Hangfire to retry
        }
    }
}