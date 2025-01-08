﻿namespace Lingarr.Core.Configuration;

public static class SettingKeys 
{
    public static class Integration
    {
        public const string RadarrUrl = "radarr_url";
        public const string RadarrApiKey = "radarr_api_key";
        public const string SonarrUrl = "sonarr_url";
        public const string SonarrApiKey = "sonarr_api_key";
        public const string RadarrSettingsCompleted = "radarr_settings_completed";
        public const string SonarrSettingsCompleted = "sonarr_settings_completed";
    }

    public static class Translation 
    {
        public const string ServiceType = "service_type";
        public const string DeeplApiKey = "deepl_api_key";
        
        public static class OpenAi 
        {
            public const string Model = "openai_model";
            public const string ApiKey = "openai_api_key";
        }

        public static class Anthropic 
        {
            public const string Model = "anthropic_model";
            public const string ApiKey = "anthropic_api_key";
            public const string Version = "anthropic_version";
        }

        public static class LocalAi 
        {
            public const string Model = "local_ai_model";
            public const string Endpoint = "local_ai_endpoint";
            public const string ApiKey = "local_ai_api_key";
        }
        
        public static class Custom
        {
            public const string Endpoint = "custom_endpoint";
        }

        public const string LibreTranslateUrl = "libretranslate_url";
        public const string SourceLanguages = "source_languages";
        public const string AiPrompt = "ai_prompt";
    }

    public static class Automation 
    {
        public const string AutomationEnabled = "automation_enabled";
        public const string TranslationSchedule = "translation_schedule";
        public const string MaxTranslationsPerRun = "max_translations_per_run";
        public const string TranslationCycle = "translation_cycle";
        public const string MovieSchedule = "movie_schedule";
        public const string ShowSchedule = "show_schedule";
    }
}