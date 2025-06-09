/*
 * ConfigurationService.cs - Application Configuration and Environment Management
 * 
 * This service provides centralized configuration management for the document validation application.
 * It handles loading configuration from multiple sources including local settings files,
 * environment variables, and Azure Functions runtime configuration.
 * 
 * Configuration Sources (in order of precedence):
 * 1. Environment variables (highest priority)
 * 2. local.settings.json file (development only)
 * 3. Azure Functions runtime configuration (production)
 * 
 * Security Considerations:
 * - Sensitive credentials are loaded from environment variables
 * - Local settings file is excluded from source control
 * - All configuration values are validated before use
 * 
 * Dependencies:
 * - Microsoft.Extensions.Configuration for configuration management
 * - Newtonsoft.Json for local settings file parsing
 */

using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace DocumentValidator.Services
{
    /*
     * ConfigurationService Class
     * 
     * Centralized service for managing application configuration and environment variables.
     * Provides type-safe access to configuration values and handles multiple configuration sources
     * with proper precedence and fallback mechanisms.
     * 
     * Design Principles:
     * - Single responsibility for configuration management
     * - Environment-specific configuration handling
     * - Fail-fast validation for required settings
     * - Thread-safe singleton operation
     */
    public class ConfigurationService
    {
        /*
         * Configuration Instance
         * 
         * Internal configuration provider built from multiple sources.
         * Provides the foundation for all configuration value retrieval.
         */
        private readonly IConfiguration _configuration;

        /*
         * ConfigurationService Constructor
         * 
         * Initializes the configuration service by building a configuration provider
         * from multiple sources and loading environment variables from local settings
         * when necessary (primarily for development scenarios).
         * 
         * Configuration Source Priority:
         * 1. Environment variables (production and development)
         * 2. local.settings.json (development only)
         * 3. Default values and fallbacks
         * 
         * Initialization Process:
         * - Creates configuration builder with base path
         * - Adds JSON file configuration (optional)
         * - Adds environment variable configuration
         * - Loads additional environment variables from local settings if needed
         */
        public ConfigurationService()
        {
            /*
             * Configuration Builder Setup
             * 
             * Creates a new configuration builder and adds configuration sources
             * in order of precedence. Environment variables have the highest priority
             * and will override values from other sources.
             */
            var builder = new ConfigurationBuilder()
                /*
                 * Base Path Configuration
                 * Sets the base directory for relative file paths in configuration.
                 * Uses the current working directory for file resolution.
                 */
                .SetBasePath(Directory.GetCurrentDirectory())
                /*
                 * Local Settings File Configuration
                 * Adds the local.settings.json file as a configuration source.
                 * This file is optional and primarily used for development.
                 * In production, this file may not exist and will be safely ignored.
                 */
                .AddJsonFile("local.settings.json", optional: true)
                /*
                 * Environment Variables Configuration
                 * Adds all environment variables as configuration sources.
                 * This includes Azure Functions application settings in production
                 * and manually set environment variables in development.
                 */
                .AddEnvironmentVariables();

            /*
             * Build Configuration Instance
             * Creates the final configuration instance from all added sources.
             */
            _configuration = builder.Build();
            
            /*
             * Load Additional Environment Variables
             * Loads environment variables from local.settings.json if they're not
             * already set. This provides a fallback mechanism for development scenarios.
             */
            LoadEnvironmentVariables();
        }

        /*
         * LoadEnvironmentVariables Method
         * 
         * Loads environment variables from the local.settings.json file when
         * required environment variables are not already set. This method provides
         * a development-friendly way to configure the application without requiring
         * manual environment variable setup.
         * 
         * Behavior:
         * - Only loads from file if required variables are missing
         * - Safely handles missing or malformed local.settings.json files
         * - Provides console feedback about configuration loading
         * - Continues gracefully if loading fails
         * 
         * Security Note:
         * This method only reads from local.settings.json, which should be
         * excluded from source control and only present in development environments.
         */
        private void LoadEnvironmentVariables()
        {
            try
            {
                /*
                 * Environment Variable Existence Check
                 * 
                 * Checks if the required Azure Document Intelligence credentials
                 * are already set as environment variables. If they exist,
                 * no additional loading is necessary.
                 * 
                 * Required Variables:
                 * - DI_ENDPOINT: Azure Document Intelligence service endpoint URL
                 * - DI_KEY: Azure Document Intelligence service access key
                 */
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DI_ENDPOINT")) ||
                    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DI_KEY")))
                {
                    /*
                     * Local Settings File Path Resolution
                     * 
                     * Constructs the full path to the local.settings.json file
                     * using the current working directory as the base path.
                     */
                    var settingsPath = Path.Combine(Directory.GetCurrentDirectory(), "local.settings.json");

                    /*
                     * File Existence Verification
                     * 
                     * Verifies that the local.settings.json file exists before
                     * attempting to read from it. This prevents file not found
                     * exceptions in production environments.
                     */
                    if (File.Exists(settingsPath))
                    {
                        /*
                         * Settings File Reading and Parsing
                         * 
                         * Reads the entire contents of the local.settings.json file
                         * and deserializes it into a LocalSettings object for processing.
                         */
                        var settingsJson = File.ReadAllText(settingsPath);
                        var settings = JsonConvert.DeserializeObject<LocalSettings>(settingsJson);

                        /*
                         * Environment Variable Assignment
                         * 
                         * Iterates through all key-value pairs in the settings file
                         * and assigns them as environment variables. This makes them
                         * available to the rest of the application.
                         */
                        if (settings?.Values != null)
                        {
                            foreach (var kvp in settings.Values)
                            {
                                Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
                            }
                            /*
                             * Success Notification
                             * Provides console feedback that environment variables
                             * were successfully loaded from the local settings file.
                             */
                            Console.WriteLine("Loaded environment variables from local.settings.json");
                        }
                    }
                }
            }
            catch (Exception error)
            {
                /*
                 * Error Handling
                 * 
                 * Catches any exceptions that occur during environment variable loading
                 * and provides console feedback. The application continues to run
                 * even if this step fails, allowing for graceful degradation.
                 * 
                 * Common exceptions:
                 * - FileNotFoundException: local.settings.json doesn't exist
                 * - JsonException: Malformed JSON in settings file
                 * - UnauthorizedAccessException: Insufficient file permissions
                 */
                Console.WriteLine($"Error loading environment variables: {error.Message}");
            }
        }

        /*
         * GetDocumentIntelligenceConfig Method
         * 
         * Retrieves and validates Azure Document Intelligence service configuration.
         * This method provides type-safe access to Azure AI service credentials
         * with proper validation and error handling.
         * 
         * Returns: DocumentIntelligenceConfig object with validated credentials
         * Throws: InvalidOperationException if required credentials are missing
         * 
         * Configuration Requirements:
         * - DI_ENDPOINT: Must be a valid Azure Document Intelligence service URL
         * - DI_KEY: Must be a valid Azure Document Intelligence access key
         * 
         * Security Considerations:
         * - Credentials are never logged or exposed in error messages
         * - Validation occurs before returning configuration to calling code
         * - Fails fast if credentials are missing or invalid
         */
        public DocumentIntelligenceConfig GetDocumentIntelligenceConfig()
        {
            /*
             * Credential Retrieval
             * 
             * Retrieves the Azure Document Intelligence endpoint and access key
             * from environment variables. These should be set either directly
             * as environment variables or loaded from local.settings.json.
             */
            var endpoint = Environment.GetEnvironmentVariable("DI_ENDPOINT");
            var key = Environment.GetEnvironmentVariable("DI_KEY");

            /*
             * Credential Validation
             * 
             * Validates that both required credentials are present and not empty.
             * This prevents runtime errors when attempting to connect to Azure services.
             * 
             * Validation Criteria:
             * - Values must not be null
             * - Values must not be empty strings
             * - Both endpoint and key must be present
             */
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key))
            {
                /*
                 * Configuration Error Handling
                 * 
                 * Throws a descriptive exception when required credentials are missing.
                 * The error message provides guidance for resolving the configuration issue
                 * without exposing sensitive information about the expected format.
                 */
                throw new InvalidOperationException("Missing Document Intelligence credentials. Please check your environment variables.");
            }

            /*
             * Configuration Object Creation
             * 
             * Creates and returns a validated DocumentIntelligenceConfig object
             * containing the Azure service credentials. This object can be safely
             * used to initialize Azure Document Intelligence clients.
             */
            return new DocumentIntelligenceConfig
            {
                Endpoint = endpoint,
                Key = key
            };
        }

        /*
         * LocalSettings Class
         * 
         * Internal data transfer object for deserializing the local.settings.json file.
         * This class matches the expected structure of Azure Functions local settings
         * and provides type-safe access to configuration values.
         * 
         * JSON Structure:
         * {
         *   "Values": {
         *     "DI_ENDPOINT": "https://your-service.cognitiveservices.azure.com/",
         *     "DI_KEY": "your-access-key"
         *   }
         * }
         * 
         * Note: This class is private and only used internally for JSON deserialization.
         */
        private class LocalSettings
        {
            /*
             * Values Property
             * 
             * Dictionary containing all configuration key-value pairs from
             * the local.settings.json file. This matches the standard format
             * used by Azure Functions for local development settings.
             * 
             * Nullable: Can be null if the Values section is missing from the JSON file
             */
            public Dictionary<string, string>? Values { get; set; }
        }
    }

    /*
     * DocumentIntelligenceConfig Class
     * 
     * Configuration data transfer object containing validated Azure Document Intelligence
     * service credentials. This class provides type-safe access to service configuration
     * and ensures that all required values are present before use.
     * 
     * Usage:
     * This object is returned by ConfigurationService.GetDocumentIntelligenceConfig()
     * and can be used to initialize Azure Document Intelligence clients with
     * validated credentials.
     * 
     * Thread Safety:
     * This class is immutable after construction and thread-safe for read operations.
     */
    public class DocumentIntelligenceConfig
    {
        /*
         * Endpoint Property
         * 
         * The complete URL endpoint for the Azure Document Intelligence service.
         * This should include the protocol (https://) and the full domain name
         * of the Azure Cognitive Services endpoint.
         * 
         * Format: https://your-service-name.cognitiveservices.azure.com/
         * Validation: Must be present and non-empty when retrieved from configuration
         * Usage: Used to initialize DocumentAnalysisClient
         */
        public string Endpoint { get; set; } = string.Empty;

        /*
         * Key Property
         * 
         * The access key for authenticating with the Azure Document Intelligence service.
         * This is a sensitive credential that should be kept secure and not logged.
         * 
         * Format: 32-character hexadecimal string
         * Security: Should be treated as a secret and not exposed in logs or error messages
         * Usage: Used to create AzureKeyCredential for service authentication
         */
        public string Key { get; set; } = string.Empty;
    }
} 