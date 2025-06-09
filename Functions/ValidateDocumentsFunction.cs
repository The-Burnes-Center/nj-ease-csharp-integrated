/*
 * ValidateDocumentsFunction.cs - Azure Functions HTTP Trigger for Document Validation
 * 
 * This file contains the main Azure Functions HTTP trigger that serves as the entry point
 * for the document validation API. It handles incoming HTTP requests, validates request data,
 * processes documents through the validation pipeline, and returns comprehensive results.
 * 
 * API Endpoint: POST /api/validate-documents
 * Authentication: Function-level authorization key required
 * Content-Type: application/json
 * 
 * Request Flow:
 * 1. HTTP request validation and parsing
 * 2. Request body deserialization and validation
 * 3. Parallel document processing and validation
 * 4. Result aggregation and PDF report generation
 * 5. Response serialization and return
 * 
 * Error Handling:
 * - Comprehensive validation of request structure and content
 * - Graceful handling of processing errors with detailed feedback
 * - Proper HTTP status codes for different error scenarios
 * - Detailed logging for troubleshooting and monitoring
 * 
 * Performance Considerations:
 * - Parallel processing of multiple documents
 * - Asynchronous operations throughout the pipeline
 * - Memory-efficient handling of large file uploads
 * - Optimized PDF generation for quick response times
 */

using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using DocumentValidator.Models;
using DocumentValidator.Services;

namespace DocumentValidator.Functions
{
    /*
     * ValidateDocumentsFunction Class
     * 
     * Azure Functions class containing the HTTP trigger for document validation.
     * Orchestrates the entire validation workflow from request reception to response generation.
     * 
     * Design Principles:
     * - Single responsibility for HTTP request handling
     * - Dependency injection for service access
     * - Comprehensive error handling and logging
     * - Asynchronous processing for optimal performance
     * 
     * Dependencies:
     * - ILogger: For comprehensive application logging
     * - DocumentValidatorService: Core document validation logic
     * - PdfGeneratorService: PDF report generation
     */
    public class ValidateDocumentsFunction
    {
        /*
         * Logger Instance
         * 
         * Provides structured logging capabilities for the function.
         * Used for tracking request processing, errors, and performance metrics.
         */
        private readonly ILogger<ValidateDocumentsFunction> _logger;
        
        /*
         * Document Validator Service Instance
         * 
         * Core service responsible for document analysis and validation.
         * Handles Azure Document Intelligence integration and validation rule execution.
         */
        private readonly DocumentValidatorService _validatorService;
        
        /*
         * PDF Generator Service Instance
         * 
         * Service responsible for generating comprehensive PDF reports
         * from validation results with professional formatting.
         */
        private readonly PdfGeneratorService _pdfService;

        /*
         * ValidateDocumentsFunction Constructor
         * 
         * Initializes the function with required dependencies through dependency injection.
         * All services are injected as singletons for optimal performance.
         * 
         * Parameters:
         * - logger: Structured logging service for the function
         * - validatorService: Core document validation service
         * - pdfService: PDF report generation service
         */
        public ValidateDocumentsFunction(ILogger<ValidateDocumentsFunction> logger, DocumentValidatorService validatorService, PdfGeneratorService pdfService)
        {
            _logger = logger;
            _validatorService = validatorService;
            _pdfService = pdfService;
        }

        /*
         * Run Method - Main HTTP Trigger Entry Point
         * 
         * Azure Functions HTTP trigger method that handles incoming validation requests.
         * Processes the complete document validation workflow from request to response.
         * 
         * HTTP Configuration:
         * - Authorization Level: Function (requires function key)
         * - HTTP Methods: POST only
         * - Route: "validate-documents"
         * - Content-Type: application/json expected
         * 
         * Processing Workflow:
         * 1. Request validation and parsing
         * 2. JSON deserialization and structure validation
         * 3. Required field validation
         * 4. Parallel document processing
         * 5. Result aggregation and sorting
         * 6. PDF report generation
         * 7. Response creation and serialization
         * 
         * Returns: HttpResponseData with ValidationResponse JSON or error details
         * 
         * Error Responses:
         * - 400 Bad Request: Invalid request structure or missing required fields
         * - 500 Internal Server Error: Unexpected processing errors
         */
        [Function("ValidateDocuments")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "validate-documents")] HttpRequestData req)
        {
            /*
             * Request Processing Initiation
             * 
             * Logs the start of request processing for monitoring and debugging.
             * Provides audit trail for API usage tracking.
             */
            _logger.LogInformation("Document validation function processed a request.");

            try
            {
                /*
                 * Request Body Reading and Validation
                 * 
                 * Reads the complete HTTP request body as a string for JSON parsing.
                 * This approach allows for comprehensive validation before processing.
                 */
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                
                /*
                 * Empty Request Body Check
                 * 
                 * Validates that the request contains a body before attempting to parse.
                 * Returns appropriate error response for empty requests.
                 */
                if (string.IsNullOrEmpty(requestBody))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteAsJsonAsync(new { error = "Request body is missing" });
                    return badRequestResponse;
                }

                /*
                 * JSON Deserialization and Validation
                 * 
                 * Attempts to deserialize the request body into a ValidationRequest object.
                 * Includes comprehensive error handling for malformed JSON.
                 */
                ValidationRequest? validationRequest;
                try
                {
                    validationRequest = JsonConvert.DeserializeObject<ValidationRequest>(requestBody);
                }
                catch (JsonException ex)
                {
                    /*
                     * JSON Parsing Error Handling
                     * 
                     * Logs JSON parsing errors and returns descriptive error response.
                     * Helps clients identify and correct JSON formatting issues.
                     */
                    _logger.LogError($"JSON parsing error: {ex.Message}");
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteAsJsonAsync(new { error = "Invalid JSON format" });
                    return badRequestResponse;
                }

                /*
                 * Required Fields Validation
                 * 
                 * Validates that all required fields are present and not empty.
                 * Ensures the request contains sufficient data for document validation.
                 * 
                 * Required Fields:
                 * - OrganizationName: Must be present and non-empty
                 * - Fein: Must be present and non-empty
                 * - Files: Must be present and contain at least one file
                 */
                if (validationRequest == null || 
                    string.IsNullOrEmpty(validationRequest.OrganizationName) || 
                    string.IsNullOrEmpty(validationRequest.Fein) || 
                    validationRequest.Files == null || 
                    validationRequest.Files.Count == 0)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteAsJsonAsync(new 
                    { 
                        error = "Invalid request format. Required fields: organizationname, FEIN, and files array." 
                    });
                    return badRequestResponse;
                }

                /*
                 * Processing Collections Initialization
                 * 
                 * Initializes collections for storing validation results and processing tasks.
                 * Separates successful validations from skipped documents for comprehensive reporting.
                 */
                var results = new List<ValidationResult>();
                var validationTasks = new List<Task>();
                var skippedDocuments = new List<SkippedDocument>();

                /*
                 * File Processing Loop
                 * 
                 * Iterates through all submitted files and creates validation tasks.
                 * Performs preliminary validation before adding to processing queue.
                 */
                foreach (var file in validationRequest.Files)
                {
                    /*
                     * File Object Validation
                     * 
                     * Validates that each file object contains required properties.
                     * Creates error results for files with missing or invalid data.
                     */
                    if (string.IsNullOrEmpty(file.FileName) || string.IsNullOrEmpty(file.FileContentBase64))
                    {
                        results.Add(new ValidationResult
                        {
                            FileName = file.FileName ?? "Unknown",
                            Success = false,
                            MissingElements = new List<string> { "Missing required file properties (FileName or FileContentBase64)" },
                            SuggestedActions = new List<string> { "Ensure both FileName and FileContentBase64 are provided" },
                            DocumentInfo = new DocumentInfo { PageCount = 0, WordCount = 0 }
                        });
                        continue;
                    }

                    /*
                     * Document Type Initialization
                     * 
                     * Sets the initial document type to "unknown" for automatic detection.
                     * The validation service will determine the actual document type during processing.
                     */
                    var documentType = "unknown";

                    /*
                     * Base64 Content Decoding
                     * 
                     * Attempts to decode the base64-encoded file content into a byte array.
                     * Includes error handling for invalid base64 format.
                     */
                    byte[] fileBuffer;
                    try
                    {
                        fileBuffer = Convert.FromBase64String(file.FileContentBase64);
                    }
                    catch (FormatException)
                    {
                        /*
                         * Base64 Decoding Error Handling
                         * 
                         * Creates error result for files with invalid base64 encoding.
                         * Provides guidance for correcting the encoding issue.
                         */
                        results.Add(new ValidationResult
                        {
                            FileName = file.FileName,
                            DocumentType = "unknown",
                            Success = false,
                            MissingElements = new List<string> { "Invalid base64 content" },
                            SuggestedActions = new List<string> { "Verify the file content is properly base64 encoded" },
                            DocumentInfo = new DocumentInfo { PageCount = 0, WordCount = 0 }
                        });
                        continue;
                    }

                    /*
                     * Form Fields Preparation
                     * 
                     * Prepares the organization metadata for validation processing.
                     * This data is used for cross-verification against document content.
                     */
                    var formFields = new Dictionary<string, string>
                    {
                        { "organizationName", validationRequest.OrganizationName },
                        { "fein", validationRequest.Fein }
                    };

                    /*
                     * Validation Task Creation
                     * 
                     * Creates an asynchronous validation task for the current file.
                     * Tasks are added to a collection for parallel execution.
                     */
                    var task = ProcessDocumentAsync(file.FileName, fileBuffer, documentType, formFields, results, skippedDocuments);
                    validationTasks.Add(task);
                }

                /*
                 * Parallel Task Execution
                 * 
                 * Executes all validation tasks in parallel for optimal performance.
                 * Waits for all tasks to complete before proceeding to result processing.
                 */
                await Task.WhenAll(validationTasks);

                /*
                 * Result Sorting and Organization
                 * 
                 * Sorts validation results by document type and then by filename.
                 * Provides consistent ordering for better user experience in reports.
                 */
                results.Sort((a, b) =>
                {
                    if (a.DocumentType == b.DocumentType)
                        return string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
                    return string.Compare(a.DocumentType, b.DocumentType, StringComparison.OrdinalIgnoreCase);
                });

                /*
                 * Consolidated PDF Report Generation
                 * 
                 * Generates a comprehensive PDF report containing all validation results.
                 * The report includes summary tables, detailed findings, and recommendations.
                 */
                var pdfBuffer = await _pdfService.GenerateConsolidatedReportAsync(results, validationRequest.OrganizationName, skippedDocuments);

                /*
                 * Success Response Creation
                 * 
                 * Creates the final HTTP response containing validation results and PDF report.
                 * Includes all processed results, skipped documents, and metadata.
                 */
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new ValidationResponse
                {
                    OrganizationName = validationRequest.OrganizationName,
                    Fein = validationRequest.Fein,
                    Results = results,
                    SkippedDocuments = skippedDocuments,
                    ConsolidatedReportBase64 = Convert.ToBase64String(pdfBuffer)
                });

                return response;
            }
            catch (Exception error)
            {
                /*
                 * Global Error Handling
                 * 
                 * Catches any unhandled exceptions and returns an appropriate error response.
                 * Logs the complete error for troubleshooting and monitoring.
                 */
                _logger.LogError($"Error in document validation function: {error}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = error.Message ?? "An unexpected error occurred" });
                return errorResponse;
            }
        }

        /*
         * ProcessDocumentAsync Method
         * 
         * Asynchronous method for processing individual documents through the validation pipeline.
         * Handles the complete validation workflow for a single document including error handling.
         * 
         * Processing Workflow:
         * 1. Document validation through DocumentValidatorService
         * 2. Result object creation and population
         * 3. Skipped document handling for unknown types
         * 4. Error handling for processing failures
         * 
         * Parameters:
         * - fileName: Original name of the file being processed
         * - fileBuffer: Decoded byte array of the file content
         * - documentType: Initial document type (typically "unknown" for auto-detection)
         * - formFields: Organization metadata for validation cross-checking
         * - results: Collection to add successful validation results
         * - skippedDocuments: Collection to add documents that couldn't be processed
         * 
         * Thread Safety:
         * This method is designed for parallel execution and safely adds results
         * to thread-safe collections.
         */
        private async Task ProcessDocumentAsync(string fileName, byte[] fileBuffer, string documentType, Dictionary<string, string> formFields, List<ValidationResult> results, List<SkippedDocument> skippedDocuments)
        {
            try
            {
                /*
                 * Document Validation Execution
                 * 
                 * Calls the core validation service to analyze the document.
                 * This includes Azure Document Intelligence analysis, type detection,
                 * and compliance validation based on document type.
                 */
                var validationResult = await _validatorService.ValidateDocumentAsync(fileBuffer, documentType, formFields);

                /*
                 * Validation Result Creation
                 * 
                 * Creates a comprehensive validation result object containing
                 * all analysis findings, metadata, and recommendations.
                 */
                results.Add(new ValidationResult
                {
                    FileName = fileName,
                    DocumentType = validationResult.DocumentInfo.DocumentType,
                    Success = validationResult.Success,
                    MissingElements = validationResult.MissingElements,
                    SuggestedActions = validationResult.SuggestedActions,
                    DocumentInfo = validationResult.DocumentInfo
                });

                /*
                 * Unknown Document Type Handling
                 * 
                 * Adds documents with unknown types to the skipped documents collection.
                 * This provides transparency about documents that couldn't be classified
                 * and guides users on potential resolution actions.
                 */
                if (validationResult.DocumentInfo.DocumentType == "unknown")
                {
                    skippedDocuments.Add(new SkippedDocument
                    {
                        FileName = fileName,
                        Reason = "Document type could not be identified"
                    });
                }
            }
            catch (Exception error)
            {
                /*
                 * Document Processing Error Handling
                 * 
                 * Handles errors that occur during individual document processing.
                 * Creates error results that provide information about the failure
                 * while allowing processing of other documents to continue.
                 */
                _logger.LogError($"Error processing document {fileName}: {error}");
                results.Add(new ValidationResult
                {
                    FileName = fileName,
                    DocumentType = "unknown",
                    Success = false,
                    MissingElements = new List<string> { "Document processing failed" },
                    SuggestedActions = new List<string> { "Check if the document is valid and properly formatted" },
                    DocumentInfo = new DocumentInfo { PageCount = 0, WordCount = 0 }
                });
            }
        }
    }
} 