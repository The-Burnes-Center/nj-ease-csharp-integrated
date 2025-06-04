using DocumentValidator.Functions;
using DocumentValidator.Models;
using DocumentValidator.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;
using System.Text;
using System.Security.Claims;
using System.Net;

namespace DocumentValidator.Tests
{
    public class TestFunction
    {
        public static async Task RunValidationTest()
        {
            Console.WriteLine("Starting test execution of ValidateDocuments function...");

            // Load environment variables
            var configService = new ConfigurationService();
            try
            {
                var config = configService.GetDocumentIntelligenceConfig();
                Console.WriteLine($"Using Document Intelligence endpoint: {(!string.IsNullOrEmpty(config.Endpoint) ? "Configured" : "Missing")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Configuration error: {ex.Message}");
                return;
            }

            // Create logger factory
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
            });

            // Create dependencies
            var logger = loggerFactory.CreateLogger<ValidateDocumentsFunction>();
            var validatorService = new DocumentValidatorService();
            var pdfService = new PdfGeneratorService();

            // Create function instance
            var function = new ValidateDocumentsFunction(logger, validatorService, pdfService);

            // Load sample payload (you would need to create this file)
            var testPayloadPath = "Tests/testPayload.json";
            if (!File.Exists(testPayloadPath))
            {
                Console.WriteLine($"Test payload file not found: {testPayloadPath}");
                Console.WriteLine("Please create a test payload file with the following structure:");
                Console.WriteLine(@"{
  ""organizationname"": ""Example Corporation LLC"",
  ""FEIN"": ""123456789"",
  ""files"": [
    {
      ""FileName"": ""Document1.pdf"",
      ""FileContentBase64"": ""<base64-encoded-content>""
    }
  ]
}");
                return;
            }

            var samplePayloadJson = await File.ReadAllTextAsync(testPayloadPath);
            
            // Create mock HTTP request
            var requestData = new MockHttpRequestData(samplePayloadJson);

            try
            {
                // Execute the function
                var response = await function.Run(requestData);

                Console.WriteLine("Function executed successfully");
                Console.WriteLine($"Response status: {response.StatusCode}");

                // Read response body
                response.Body.Position = 0;
                var responseBody = await new StreamReader(response.Body).ReadToEndAsync();

                if (!string.IsNullOrEmpty(responseBody))
                {
                    var validationResponse = JsonConvert.DeserializeObject<ValidationResponse>(responseBody);

                    if (validationResponse?.Results != null)
                    {
                        Console.WriteLine("\nResults summary:");
                        for (int i = 0; i < validationResponse.Results.Count; i++)
                        {
                            var result = validationResponse.Results[i];
                            Console.WriteLine($"Document {i + 1}: {result.FileName}");
                            Console.WriteLine($"  Success: {result.Success}");
                            Console.WriteLine($"  Document Type: {result.DocumentType}");
                            Console.WriteLine($"  Missing Elements: {(result.MissingElements.Count > 0 ? string.Join(", ", result.MissingElements) : "None")}");
                        }

                        // Save consolidated PDF to file
                        if (!string.IsNullOrEmpty(validationResponse.ConsolidatedReportBase64))
                        {
                            var outputDir = "Tests/output";
                            Directory.CreateDirectory(outputDir);

                            var pdfPath = Path.Combine(outputDir, "Consolidated_Validation_Report.pdf");
                            var pdfBytes = Convert.FromBase64String(validationResponse.ConsolidatedReportBase64);
                            await File.WriteAllBytesAsync(pdfPath, pdfBytes);
                            Console.WriteLine($"\nConsolidated PDF Report saved to: {pdfPath}");
                        }

                        // Save full results to JSON file for inspection
                        var outputDir2 = "Tests/output";
                        Directory.CreateDirectory(outputDir2);
                        var resultsPath = Path.Combine(outputDir2, "results.json");
                        var resultsJson = JsonConvert.SerializeObject(validationResponse, Formatting.Indented);
                        await File.WriteAllTextAsync(resultsPath, resultsJson);
                        Console.WriteLine($"Full results saved to: {resultsPath}");
                    }
                }
            }
            catch (Exception error)
            {
                Console.WriteLine($"Error executing function: {error}");
            }
        }
    }

    // Mock HTTP request data for testing
    public class MockHttpRequestData : HttpRequestData
    {
        private readonly Stream _body;

        public MockHttpRequestData(string jsonContent) : base(null!)
        {
            _body = new MemoryStream(Encoding.UTF8.GetBytes(jsonContent));
        }

        public override Stream Body => _body;

        public override HttpHeadersCollection Headers => new HttpHeadersCollection();

        public override IReadOnlyCollection<IHttpCookie> Cookies => new List<IHttpCookie>();

        public override Uri Url => new Uri("http://localhost/api/validate-documents");

        public override IEnumerable<ClaimsIdentity> Identities => new List<ClaimsIdentity>();

        public override string Method => "POST";

        public override HttpResponseData CreateResponse()
        {
            return new MockHttpResponseData();
        }
    }

    public class MockHttpResponseData : HttpResponseData
    {
        private HttpHeadersCollection _headers;

        public MockHttpResponseData() : base(null!)
        {
            Body = new MemoryStream();
            _headers = new HttpHeadersCollection();
        }

        public override HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        public override HttpHeadersCollection Headers 
        { 
            get => _headers; 
            set => _headers = value; 
        }

        public override Stream Body { get; set; }

        public override HttpCookies Cookies => null!;
    }
} 