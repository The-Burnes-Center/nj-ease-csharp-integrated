using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace DocumentValidator.Services
{
    public class ConfigurationService
    {
        private readonly IConfiguration _configuration;

        public ConfigurationService()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("local.settings.json", optional: true)
                .AddEnvironmentVariables();

            _configuration = builder.Build();
            LoadEnvironmentVariables();
        }

        private void LoadEnvironmentVariables()
        {
            try
            {
                // Only load from file if variables aren't already set
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DI_ENDPOINT")) ||
                    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DI_KEY")))
                {
                    var settingsPath = Path.Combine(Directory.GetCurrentDirectory(), "local.settings.json");

                    // Check if settings file exists
                    if (File.Exists(settingsPath))
                    {
                        var settingsJson = File.ReadAllText(settingsPath);
                        var settings = JsonConvert.DeserializeObject<LocalSettings>(settingsJson);

                        if (settings?.Values != null)
                        {
                            foreach (var kvp in settings.Values)
                            {
                                Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
                            }
                            Console.WriteLine("Loaded environment variables from local.settings.json");
                        }
                    }
                }
            }
            catch (Exception error)
            {
                Console.WriteLine($"Error loading environment variables: {error.Message}");
            }
        }

        public DocumentIntelligenceConfig GetDocumentIntelligenceConfig()
        {
            var endpoint = Environment.GetEnvironmentVariable("DI_ENDPOINT");
            var key = Environment.GetEnvironmentVariable("DI_KEY");

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException("Missing Document Intelligence credentials. Please check your environment variables.");
            }

            return new DocumentIntelligenceConfig
            {
                Endpoint = endpoint,
                Key = key
            };
        }

        private class LocalSettings
        {
            public Dictionary<string, string>? Values { get; set; }
        }
    }

    public class DocumentIntelligenceConfig
    {
        public string Endpoint { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
    }
} 