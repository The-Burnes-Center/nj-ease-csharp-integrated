# Azure Function Document Validator (C#)

This project provides an Azure Function written in C# that validates documents using Azure AI Document Intelligence and generates PDF summaries of validation results. The function is designed to be triggered by Power Automate flows and integrated with Dynamics 365 CRM.

## Overview

The Document Validator Azure Function processes documents sent from a Power Automate flow and validates them based on document type. The function:

1. Receives a JSON payload containing organization details and document files (base64-encoded)
2. Analyzes each document using Azure AI Document Intelligence
3. Applies validation rules specific to each document type
4. Generates a PDF summary report for each document
5. Returns validation results and PDF reports as base64-encoded strings

## Prerequisites

- [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)
- [Azure Functions Core Tools](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local)
- [Azure Account](https://azure.microsoft.com/free/) with an active subscription
- [Azure Form Recognizer/Document Intelligence](https://azure.microsoft.com/services/form-recognizer/) resource
- [Power Automate](https://flow.microsoft.com/) access for trigger setup

## Project Structure

```
.
├── Models/
│   ├── ValidationRequest.cs         # Request data models
│   └── ValidationResponse.cs        # Response data models
├── Services/
│   ├── ConfigurationService.cs      # Configuration management
│   ├── DocumentValidatorService.cs  # Document validation logic
│   └── PdfGeneratorService.cs       # PDF report generation
├── Functions/
│   └── ValidateDocumentsFunction.cs # Azure Function implementation
├── Tests/
│   ├── TestFunction.cs              # Test runner
│   └── testPayload.json             # Sample input for testing
├── Program.cs                       # Application entry point
├── DocumentValidator.csproj         # Project file
├── host.json                        # Function app configuration
├── local.settings.json              # Local environment settings
└── README.md                        # This file
```

## Setup

1. Clone this repository:
   ```
   git clone <repository-url>
   cd document-validator-azure-function-csharp
   ```

2. Install dependencies:
   ```
   dotnet restore
   ```

3. Configure settings:

   Edit `local.settings.json` with your Azure Document Intelligence credentials:
   ```json
   {
     "IsEncrypted": false,
     "Values": {
       "AzureWebJobsStorage": "UseDevelopmentStorage=true",
       "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
       "DI_ENDPOINT": "your-document-intelligence-endpoint",
       "DI_KEY": "your-document-intelligence-key"
     }
   }
   ```

## Local Testing

1. Create test documents and update `Tests/testPayload.json` as needed.

2. Run the test:
   ```
   dotnet run --project Tests/TestFunction.cs
   ```

3. Or run the Azure Function locally:
   ```
   func start
   ```

4. Review generated PDF reports in the `Tests/output` directory.

## Deployment to Azure

1. Build the project:
   ```
   dotnet build
   ```

2. Publish the function to Azure:
   ```
   func azure functionapp publish <your-function-app-name>
   ```

3. Configure application settings in the Azure portal:
   - DI_ENDPOINT
   - DI_KEY 

## Power Automate Integration

1. Create a Power Automate flow that triggers your Azure Function.

2. Configure the HTTP action to send JSON payload to the function endpoint:
   ```
   {
     "organizationname": "Example Corporation LLC",
     "FEIN": "123456789",
     "files": [
       {
         "FileName": "Document1.pdf",
         "FileContentBase64": "<base64-encoded-content>"
       },
       ...
     ]
   }
   ```

3. Process the function response in your flow to:
   - Parse validation results
   - Save generated PDF reports
   - Update Dynamics 365 CRM records

## Input Format

The function expects a JSON object with the following structure:

```json
{
  "organizationname": "Organization Name",
  "FEIN": "Tax ID",
  "files": [
    {
      "FileName": "document.pdf",
      "FileContentBase64": "base64-encoded-content"
    }
  ]
}
```

## Output Format

The function returns a JSON object with the following structure:

```json
{
  "organizationName": "Organization Name",
  "fein": "Tax ID",
  "results": [
    {
      "fileName": "document.pdf",
      "documentType": "tax-clearance-online",
      "success": true,
      "missingElements": [],
      "suggestedActions": [],
      "documentInfo": {
        "pageCount": 1,
        "wordCount": 100,
        "languageInfo": [...],
        "containsHandwriting": false,
        "documentType": "tax-clearance-online",
        "detectedOrganizationName": "Organization Name"
      }
    }
  ],
  "skippedDocuments": [],
  "consolidatedReportBase64": "base64-encoded-pdf"
}
```

## Supported Document Types

The function supports validation for the following document types:

- `tax-clearance-online` - Tax Clearance Certificate (Online)
- `tax-clearance-manual` - Tax Clearance Certificate (Manual)
- `cert-alternative-name` - Certificate of Alternative Name
- `cert-trade-name` - Certificate of Trade Name
- `cert-formation` - Certificate of Formation
- `cert-good-standing-long` - Certificate of Good Standing (Long Form)
- `cert-good-standing-short` - Certificate of Good Standing (Short Form)
- `operating-agreement` - Operating Agreement
- `cert-incorporation` - Certificate of Incorporation
- `irs-determination` - IRS Determination Letter
- `bylaws` - Corporate Bylaws
- `cert-authority` - Certificate of Authority

## Development

### Building the Project

```bash
dotnet build
```

### Running Tests

```bash
dotnet run --project Tests/TestFunction.cs
```

### Running Locally

```bash
func start
```

## Troubleshooting

- **Missing environment variables**: Check that DI_ENDPOINT and DI_KEY are properly configured.
- **Document analysis errors**: Verify that document files are valid PDFs or images.
- **Validation failures**: Check the validation logic for specific document types in `DocumentValidatorService.cs`.
- **PDF generation issues**: Ensure PdfSharpCore dependencies are properly installed.

## Migration from Node.js

This C# version maintains the same functionality as the original Node.js implementation:

- All document validation logic has been preserved
- PDF generation produces identical reports
- API interface remains unchanged
- Configuration and deployment process is similar

## License

This project is licensed under the MIT License.