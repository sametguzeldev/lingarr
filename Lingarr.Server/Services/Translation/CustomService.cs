using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lingarr.Core.Configuration;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Integrations;
using Lingarr.Server.Services.Translation.Base;

namespace Lingarr.Server.Services.Translation;

public class CustomService : BaseLanguageService
{
    private readonly HttpClient _httpClient;
    private bool _initialized;
    private string? _endpoint;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public CustomService(ISettingService settings,
        HttpClient httpClient,
        ILogger<CustomService> logger) : base(settings, logger, "/app/Statics/custom_languages.json")
    {
        _httpClient = httpClient;
    }

    private async Task InitializeAsync()
    {
        if (_initialized) return;
        try
        {
            await _initLock.WaitAsync();
            if (_initialized) return;

            var settings = await _settings.GetSettings([
                SettingKeys.Translation.Custom.Endpoint
            ]);

            if (string.IsNullOrEmpty(settings[SettingKeys.Translation.Custom.Endpoint]))
            {
                throw new InvalidOperationException("Custom endpoint address is not configured.");
            }

            _endpoint = settings[SettingKeys.Translation.Custom.Endpoint];

            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public override async Task<string> TranslateAsync(string texti, string sourceLanguage, string targetLanguage,
        CancellationToken cancellationToken)
    {
        await InitializeAsync();

        if (string.IsNullOrEmpty(_endpoint))
        {
            throw new InvalidOperationException("Custom service was not properly initialized.");
        }

        var content = new StringContent(JsonSerializer.Serialize(new
        {
            text = texti
        }), Encoding.UTF8, "application/json");

        var response = await PostWithRetryAsync(_endpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Response Status Code: {StatusCode}", response.StatusCode);
            _logger.LogError("Response Content: {ResponseContent}",
                await response.Content.ReadAsStringAsync(cancellationToken));
            throw new TranslationException("Translation using Custom failed.");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var generateResponse = JsonSerializer.Deserialize<CustomServiceResponse>(responseBody);

        if (generateResponse == null || string.IsNullOrEmpty(generateResponse.TranslatedText))
        {
            throw new TranslationException("Invalid or empty response from generate API.");
        }

        return generateResponse.TranslatedText;
    }

    private async Task<HttpResponseMessage> PostWithRetryAsync(string endpoint, HttpContent content,
        CancellationToken cancellationToken, int maxRetryAttempts = 5, int delayMilliseconds = 5000)
    {
        var retryAttempt = 0;
        Exception lastException = null; // Capture the last exception to throw later if all retries fail

        while (retryAttempt < maxRetryAttempts)
        {
            try
            {
                // Attempt to send the HTTP request
                var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);

                // If the response indicates success, return it
                if (response.IsSuccessStatusCode)
                    return response;

                // Log non-success response
                _logger.LogWarning("Request to {Endpoint} failed with status code {StatusCode}.", endpoint,
                    response.StatusCode);

                // Decide whether to retry based on status code
                if (!ShouldRetry(response))
                    return response; // If not retryable, return the response immediately
            }
            catch (HttpRequestException ex) when (retryAttempt < maxRetryAttempts - 1)
            {
                lastException = ex;
                _logger.LogError(ex, "HTTP request to {Endpoint} failed. Attempt {RetryAttempt} of {MaxRetryAttempts}.",
                    endpoint, retryAttempt + 1, maxRetryAttempts);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested &&
                                                   retryAttempt < maxRetryAttempts - 1)
            {
                lastException = ex;
                _logger.LogError(ex,
                    "HTTP request to {Endpoint} timed out. Attempt {RetryAttempt} of {MaxRetryAttempts}.", endpoint,
                    retryAttempt + 1, maxRetryAttempts);
            }

            // Increment retry attempt
            retryAttempt++;

            if (retryAttempt < maxRetryAttempts)
            {
                // Wait before retrying (exponential backoff with jitter)
                var delay = TimeSpan.FromMilliseconds(delayMilliseconds * Math.Pow(2, retryAttempt)) +
                            TimeSpan.FromMilliseconds(new Random().Next(0, 100));
                await Task.Delay(delay, cancellationToken);
            }
        }

        // If retries are exhausted, throw the last captured exception
        if (lastException != null)
        {
            throw lastException;
        }

        // Fallback if there is no exception captured (unlikely scenario)
        throw new InvalidOperationException("Failed to execute HTTP request and no exception was captured.");
    }

    // Helper method to decide if a retry is necessary
    private static bool ShouldRetry(HttpResponseMessage response)
    {
        return response.StatusCode == HttpStatusCode.TooManyRequests || // 429
               ((int)response.StatusCode >= 500 && (int)response.StatusCode <= 599); // 5xx
    }
}