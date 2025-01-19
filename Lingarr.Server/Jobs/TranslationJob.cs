using System.Text;
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
    private readonly IConfiguration _configuration;

    public TranslationJob(
        ILogger<TranslationJob> logger,
        ISettingService settings,
        LingarrDbContext dbContext,
        IProgressService progressService,
        ISubtitleService subtitleService,
        ITranslationServiceFactory translationServiceFactory,
        ITranslationRequestService translationRequestService,
        IConfiguration configuration)
    {
        _logger = logger;
        _settings = settings;
        _dbContext = dbContext;
        _progressService = progressService;
        _subtitleService = subtitleService;
        _translationServiceFactory = translationServiceFactory;
        _translationRequestService = translationRequestService;
        _configuration = configuration;
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
            await SendNotificationToNtfy(
                $"The translation job has started for {translationRequest.Title}.", "info");

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

                await SendNotificationToNtfy(
                    $"The translation job was cancelled for {translationRequest.Title}.",
                    "warning");
                return;
            }

            var outputPath = _subtitleService.CreateFilePath(
                request.SubtitleToTranslate,
                request.TargetLanguage);

            await _subtitleService.WriteSubtitles(outputPath, translatedSubtitles);

            _logger.LogInformation("TranslateJob completed and created subtitle: |Green|{filePath}|/Green|",
                outputPath);
            await _progressService.Emit(request, 100, true);

            // Send notification on successful completion
            await SendNotificationToNtfy(
                $"The translation job for {translationRequest.Title} was completed successfully.",
                "info");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during the translation job for subtitle: {filePath}",
                translationRequest.SubtitleToTranslate);

            int retryCount = context.GetJobParameter<int>("RetryCount");
            int maxRetries = context.GetJobParameter<int>("MaxRetryCount");

            if (retryCount >= maxRetries)
            {
                var failedRequest = await _translationRequestService.UpdateTranslationRequest(
                    translationRequest,
                    context.BackgroundJob.Id,
                    TranslationStatus.Failed);

                failedRequest.CompletedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                // Send notification on failure
                await SendNotificationToNtfy(
                    $"The translation job for {translationRequest.Title} failed after {maxRetries} attempts.",
                    "warning");
            }

            throw; // Rethrow the exception to allow Hangfire to retry
        }
    }

    private async Task SendNotificationToNtfy(string message, string tags)
    {
        using var client = new HttpClient();
        const string endpoint = "https://ntfy.cthraxxi.com/lingarr"; // Replace with your ntfy endpoint
        var token = _configuration["Ntfy:AuthorizationToken"];

        // Add headers
        client.DefaultRequestHeaders.Add("Title", "Lingarr: AI Translation"); // Add Title header
        client.DefaultRequestHeaders.Add("Tags", tags); // Add Tags header
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                token); // Add Authorization token

        // The message is sent as the body
        var content = new StringContent(message, Encoding.UTF8, "text/plain");

        try
        {
            var response = await client.PostAsync(endpoint, content);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to send ntfy notification. Status Code: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while sending a notification to ntfy.");
        }
    }
}