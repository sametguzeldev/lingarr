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
        ILogger<LocalAiService> logger) : base(settings, logger, "/app/Statics/custom_languages.json")
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
        
        var response = await _httpClient.PostAsync(_endpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Response Status Code: {StatusCode}", response.StatusCode);
            _logger.LogError("Response Content: {ResponseContent}", await response.Content.ReadAsStringAsync(cancellationToken));
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
}