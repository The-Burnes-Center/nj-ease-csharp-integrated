using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using DocumentValidator.Models;
using DocumentValidator.Services;

namespace DocumentValidator.Functions
{
    public class ValidateDocumentsFunction
    {
        private readonly ILogger<ValidateDocumentsFunction> _logger;
        private readonly DocumentValidatorService _validatorService;
        private readonly PdfGeneratorService _pdfService;

        public ValidateDocumentsFunction(ILogger<ValidateDocumentsFunction> logger, DocumentValidatorService validatorService, PdfGeneratorService pdfService)
        {
            _logger = logger;
            _validatorService = validatorService;
            _pdfService = pdfService;
        }

        [Function("ValidateDocuments")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "validate-documents")] HttpRequestData req)
        {
            _logger.LogInformation("Document validation function processed a request.");

            try
            {
                // Read request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                
                if (string.IsNullOrEmpty(requestBody))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteAsJsonAsync(new { error = "Request body is missing" });
                    return badRequestResponse;
                }

                // Parse JSON payload
                ValidationRequest? validationRequest;
                try
                {
                    validationRequest = JsonConvert.DeserializeObject<ValidationRequest>(requestBody);
                }
                catch (JsonException ex)
                {
                    _logger.LogError($"JSON parsing error: {ex.Message}");
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteAsJsonAsync(new { error = "Invalid JSON format" });
                    return badRequestResponse;
                }

                // Validate required fields
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

                // Process each file in the payload
                var results = new List<ValidationResult>();
                var validationTasks = new List<Task>();
                var skippedDocuments = new List<SkippedDocument>();

                foreach (var file in validationRequest.Files)
                {
                    // Validate file object
                    if (string.IsNullOrEmpty(file.FileName) || string.IsNullOrEmpty(file.FileContentBase64))
                    {
                        results.Add(new ValidationResult
                        {
                            FileName = file.FileName ?? "Unknown",
                            Error = "Missing required file properties (FileName or FileContentBase64)",
                            Success = false
                        });
                        continue;
                    }

                    // Set documentType to "unknown" since we're determining it from content
                    var documentType = "unknown";

                    // Decode base64 content to a buffer
                    byte[] fileBuffer;
                    try
                    {
                        fileBuffer = Convert.FromBase64String(file.FileContentBase64);
                    }
                    catch (FormatException)
                    {
                        results.Add(new ValidationResult
                        {
                            FileName = file.FileName,
                            Error = "Invalid base64 content",
                            Success = false
                        });
                        continue;
                    }

                    // Prepare form fields
                    var formFields = new Dictionary<string, string>
                    {
                        { "organizationName", validationRequest.OrganizationName },
                        { "fein", validationRequest.Fein }
                    };

                    // Add validation task
                    var task = ProcessDocumentAsync(file.FileName, fileBuffer, documentType, formFields, results, skippedDocuments);
                    validationTasks.Add(task);
                }

                // Wait for all validation processes to complete
                await Task.WhenAll(validationTasks);

                // Sort results by document type and then by filename
                results.Sort((a, b) =>
                {
                    if (a.DocumentType == b.DocumentType)
                        return string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
                    return string.Compare(a.DocumentType, b.DocumentType, StringComparison.OrdinalIgnoreCase);
                });

                // Generate consolidated PDF report for all documents
                var pdfBuffer = await _pdfService.GenerateConsolidatedReportAsync(results, validationRequest.OrganizationName, skippedDocuments);

                // Create response
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
                _logger.LogError($"Error in document validation function: {error}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = error.Message ?? "An unexpected error occurred" });
                return errorResponse;
            }
        }

        private async Task ProcessDocumentAsync(string fileName, byte[] fileBuffer, string documentType, Dictionary<string, string> formFields, List<ValidationResult> results, List<SkippedDocument> skippedDocuments)
        {
            try
            {
                var validationResult = await _validatorService.ValidateDocumentAsync(fileBuffer, documentType, formFields);

                // Add identified document to results, including unknown types
                results.Add(new ValidationResult
                {
                    FileName = fileName,
                    DocumentType = validationResult.DocumentInfo.DocumentType,
                    Success = validationResult.Success,
                    MissingElements = validationResult.MissingElements,
                    SuggestedActions = validationResult.SuggestedActions,
                    DocumentInfo = validationResult.DocumentInfo
                });

                // If document type is unknown, also add to skipped list for display in the last page
                if (validationResult.DocumentInfo.DocumentType == "unknown")
                {
                    skippedDocuments.Add(new SkippedDocument
                    {
                        FileName = fileName,
                        Reason = "Document type could not be identified",
                        TypeDetection = validationResult.DocumentInfo.TypeDetection
                    });
                }
            }
            catch (Exception error)
            {
                _logger.LogError($"Error processing document {fileName}: {error}");
                results.Add(new ValidationResult
                {
                    FileName = fileName,
                    DocumentType = "unknown",
                    Error = error.Message ?? "Unknown error during processing",
                    Success = false,
                    MissingElements = new List<string> { "Document processing failed" },
                    SuggestedActions = new List<string> { "Check if the document is valid and properly formatted" },
                    DocumentInfo = new DocumentInfo { PageCount = 0, WordCount = 0 }
                });
            }
        }
    }
} 