/*
 * Program.cs - Azure Functions Application Entry Point
 * 
 * This file serves as the entry point for the AI Document Validator Azure Functions application.
 * It configures the function host, sets up dependency injection, and initializes all required services.
 * 
 * The application is built using the .NET 8.0 isolated worker model for Azure Functions,
 * which provides better performance and maintainability compared to the in-process model.
 * 
 * Architecture Overview:
 * - Uses Microsoft.Extensions.Hosting for application hosting
 * - Implements dependency injection for service management
 * - Registers all core services as singletons for optimal performance
 * 
 * Service Dependencies:
 * - DocumentValidatorService: Core validation logic for document processing
 * - PdfGeneratorService: PDF report generation and formatting
 * - ConfigurationService: Environment and configuration management
 */

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using DocumentValidator.Services;

/*
 * Application Host Configuration
 * 
 * Creates and configures the Azure Functions host with all necessary services.
 * Uses the builder pattern for clean, maintainable configuration setup.
 */
var host = new HostBuilder()
    /*
     * ConfigureFunctionsWorkerDefaults()
     * Sets up the default configuration for Azure Functions worker runtime,
     * including JSON serialization, logging, and function execution context.
     */
    .ConfigureFunctionsWorkerDefaults()
    /*
     * Service Registration
     * Registers all application services with the dependency injection container.
     * All services are registered as singletons for performance optimization
     * since they are stateless and thread-safe.
     */
    .ConfigureServices(services =>
    {
        /*
         * DocumentValidatorService Registration
         * Core service responsible for document validation logic.
         * Handles document analysis, type detection, and validation rule execution.
         */
        services.AddSingleton<DocumentValidatorService>();
        
        /*
         * PdfGeneratorService Registration  
         * Service responsible for generating PDF reports from validation results.
         * Creates comprehensive, professionally formatted validation reports.
         */
        services.AddSingleton<PdfGeneratorService>();
        
        /*
         * ConfigurationService Registration
         * Service for managing application configuration and environment variables.
         * Handles Azure Document Intelligence credentials and other settings.
         */
        services.AddSingleton<ConfigurationService>();
    })
    .Build();

/*
 * Application Startup
 * Starts the Azure Functions host and begins listening for HTTP requests.
 * This call blocks until the application is shut down.
 */
host.Run(); 