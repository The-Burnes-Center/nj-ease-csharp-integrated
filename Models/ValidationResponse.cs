/*
 * ValidationResponse.cs - Response Models for Document Validation API
 * 
 * This file contains the data transfer objects (DTOs) used for outgoing HTTP responses
 * from the document validation API. These models define the structure of validation
 * results, document information, and consolidated reports returned to clients.
 * 
 * Response Architecture:
 * The API returns a comprehensive validation response containing organization metadata,
 * individual document validation results, skipped documents, and a consolidated PDF report.
 * 
 * JSON Response Structure:
 * {
 *   "organizationName": "Sample Company LLC",
 *   "fein": "12-3456789",
 *   "results": [...],
 *   "skippedDocuments": [...],
 *   "consolidatedReportBase64": "JVBERi0xLjQK..."
 * }
 */

using Newtonsoft.Json;

namespace DocumentValidator.Models
{
    /*
     * ValidationResponse Class
     * 
     * Primary response model containing complete validation results for all submitted documents.
     * Serves as the root object for the API response, aggregating all validation data
     * and providing a consolidated PDF report for download.
     * 
     * Design Principles:
     * - Comprehensive result aggregation from multiple document validations
     * - Separation of successful validations from skipped/failed documents
     * - Base64-encoded PDF report for immediate client access
     * - Preserved organization metadata for client verification
     */
    public class ValidationResponse
    {
        /*
         * Organization Name Property
         * 
         * Echoes back the organization name from the original request.
         * Allows clients to verify the response corresponds to the correct organization
         * and provides context for the validation results.
         * 
         * JSON Property: "organizationName"
         * Source: Copied from ValidationRequest.OrganizationName
         * Purpose: Response verification and report header information
         */
        [JsonProperty("organizationName")]
        public string OrganizationName { get; set; } = string.Empty;

        /*
         * Federal Employer Identification Number Property
         * 
         * Echoes back the FEIN from the original request for client verification.
         * Used in PDF report generation and provides audit trail for validation requests.
         * 
         * JSON Property: "fein"
         * Source: Copied from ValidationRequest.Fein
         * Purpose: Response verification and compliance documentation
         */
        [JsonProperty("fein")]
        public string Fein { get; set; } = string.Empty;

        /*
         * Validation Results Collection
         * 
         * Contains the complete validation results for all successfully processed documents.
         * Each result includes detailed validation findings, document metadata,
         * and specific issues or confirmations for document compliance.
         * 
         * JSON Property: "results"
         * Content: Array of ValidationResult objects
         * Includes: All documents that were successfully analyzed (both passed and failed validations)
         * Excludes: Documents that could not be processed due to format or technical issues
         */
        [JsonProperty("results")]
        public List<ValidationResult> Results { get; set; } = new List<ValidationResult>();

        /*
         * Skipped Documents Collection
         * 
         * Contains information about documents that could not be validated due to
         * technical issues, unsupported formats, or unrecognized document types.
         * Provides transparency about processing limitations and failure reasons.
         * 
         * JSON Property: "skippedDocuments"
         * Content: Array of SkippedDocument objects
         * Purpose: Client notification of processing issues and recommended actions
         */
        [JsonProperty("skippedDocuments")]
        public List<SkippedDocument> SkippedDocuments { get; set; } = new List<SkippedDocument>();

        /*
         * Consolidated Report Base64 Property
         * 
         * A professionally formatted PDF report containing comprehensive validation results
         * for all processed documents. The report includes summary tables, detailed findings,
         * and recommendations for each document type.
         * 
         * JSON Property: "consolidatedReportBase64"
         * Format: Base64-encoded PDF file
         * Content: Complete validation report with professional formatting
         * Usage: Can be directly decoded and saved as a PDF file by clients
         */
        [JsonProperty("consolidatedReportBase64")]
        public string ConsolidatedReportBase64 { get; set; } = string.Empty;
    }

    /*
     * ValidationResult Class
     * 
     * Represents the complete validation outcome for a single document.
     * Contains the validation decision, detailed findings, and comprehensive
     * document metadata extracted during the analysis process.
     * 
     * Validation Logic:
     * Each document is analyzed for compliance with specific regulatory requirements
     * based on its detected or specified type. Results include both pass/fail status
     * and detailed explanations of any issues found.
     */
    public class ValidationResult
    {
        /*
         * File Name Property
         * 
         * The original name of the validated file as provided in the request.
         * Used for identification and correlation with the original submission.
         * 
         * JSON Property: "fileName"
         * Source: Copied from FileData.FileName in the request
         * Purpose: Result identification and report organization
         */
        [JsonProperty("fileName")]
        public string FileName { get; set; } = string.Empty;

        /*
         * Document Type Property
         * 
         * The detected or determined type of the document after analysis.
         * Used to determine which validation rules to apply and how to
         * interpret the document content.
         * 
         * JSON Property: "documentType"
         * Possible Values: "tax-clearance-online", "tax-clearance-manual", 
         *                  "cert-formation", "cert-incorporation", "unknown", etc.
         * Detection: Automatic via keyword analysis and pattern matching
         * Fallback: "unknown" when document type cannot be determined
         */
        [JsonProperty("documentType")]
        public string DocumentType { get; set; } = string.Empty;

        /*
         * Success Property
         * 
         * Boolean indicator of overall validation success.
         * True when the document meets all compliance requirements for its type.
         * False when any validation rules fail or required elements are missing.
         * 
         * JSON Property: "success"
         * Determination: Based on MissingElements collection - success = empty collection
         * Purpose: Quick pass/fail assessment for automated processing
         */
        [JsonProperty("success")]
        public bool Success { get; set; }

        /*
         * Missing Elements Collection
         * 
         * Detailed list of specific compliance issues found during validation.
         * Each element represents a specific requirement that was not met,
         * providing granular feedback for document correction.
         * 
         * JSON Property: "missingElements"
         * Content: Array of descriptive strings identifying specific issues
         * Examples: "Required keyword: 'State of New Jersey'", "Signature is missing"
         * Purpose: Specific, actionable feedback for document compliance
         */
        [JsonProperty("missingElements")]
        public List<string> MissingElements { get; set; } = new List<string>();

        /*
         * Suggested Actions Collection
         * 
         * Recommended corrective actions for addressing identified compliance issues.
         * Provides practical guidance for obtaining compliant documents or
         * correcting existing submissions.
         * 
         * JSON Property: "suggestedActions"
         * Content: Array of actionable recommendation strings
         * Examples: "Obtain a more recent tax clearance certificate", 
         *           "Verify the certificate has been signed by an authorized official"
         * Purpose: Guidance for achieving document compliance
         */
        [JsonProperty("suggestedActions")]
        public List<string> SuggestedActions { get; set; } = new List<string>();

        /*
         * Document Information Property
         * 
         * Comprehensive metadata extracted from the document during analysis.
         * Includes technical details, content analysis results, and detected
         * organizational information for verification purposes.
         * 
         * JSON Property: "documentInfo"
         * Content: DocumentInfo object with detailed metadata
         * Purpose: Technical analysis results and verification data
         */
        [JsonProperty("documentInfo")]
        public DocumentInfo DocumentInfo { get; set; } = new DocumentInfo();
    }

    /*
     * DocumentInfo Class
     * 
     * Contains comprehensive metadata and analysis results extracted from a document
     * using Azure Document Intelligence. Provides technical details about the document
     * structure, content, and extracted organizational information.
     * 
     * Data Sources:
     * - Azure Document Intelligence prebuilt-document model
     * - Custom text analysis and pattern matching
     * - Language detection and confidence scoring
     */
    public class DocumentInfo
    {
        /*
         * Page Count Property
         * 
         * Total number of pages detected in the document.
         * Used for document verification and processing metrics.
         * 
         * JSON Property: "pageCount"
         * Source: Azure Document Intelligence page analysis
         * Purpose: Document structure verification and processing statistics
         */
        [JsonProperty("pageCount")]
        public int PageCount { get; set; }

        /*
         * Word Count Property
         * 
         * Total number of words detected across all pages of the document.
         * Calculated by aggregating word detection results from Azure Document Intelligence.
         * 
         * JSON Property: "wordCount"
         * Source: Aggregated from Azure Document Intelligence word detection
         * Purpose: Content density analysis and processing metrics
         */
        [JsonProperty("wordCount")]
        public int WordCount { get; set; }

        /*
         * Language Information Collection
         * 
         * Detected languages in the document with confidence scores.
         * Helps verify document origin and assists in international compliance scenarios.
         * 
         * JSON Property: "languageInfo"
         * Content: Array of LanguageInfo objects with language codes and confidence scores
         * Source: Azure Document Intelligence language detection
         * Purpose: Document origin verification and internationalization support
         */
        [JsonProperty("languageInfo")]
        public List<LanguageInfo> LanguageInfo { get; set; } = new List<LanguageInfo>();

        /*
         * Contains Handwriting Property
         * 
         * Boolean indicator of whether handwritten content was detected in the document.
         * Important for determining document authenticity and processing requirements.
         * 
         * JSON Property: "containsHandwriting"
         * Source: Azure Document Intelligence handwriting detection
         * Purpose: Authenticity verification and processing complexity assessment
         */
        [JsonProperty("containsHandwriting")]
        public bool ContainsHandwriting { get; set; }

        /*
         * Document Type Property
         * 
         * The final determined document type after analysis and classification.
         * This may differ from initial assumptions based on content analysis results.
         * 
         * JSON Property: "documentType"
         * Determination: Automatic classification via keyword analysis
         * Purpose: Validation rule selection and report categorization
         */
        [JsonProperty("documentType")]
        public string DocumentType { get; set; } = string.Empty;

        /*
         * Detected Organization Name Property
         * 
         * Organization name extracted from the document content, if detected.
         * Used for cross-verification against the provided organization name
         * to ensure document authenticity and proper association.
         * 
         * JSON Property: "detectedOrganizationName"
         * Source: Document content analysis and pattern matching
         * Purpose: Organization verification and authenticity checking
         * Nullable: May be null if no organization name could be reliably detected
         */
        [JsonProperty("detectedOrganizationName")]
        public string? DetectedOrganizationName { get; set; }
    }

    /*
     * LanguageInfo Class
     * 
     * Represents detected language information for document content.
     * Provides language identification and confidence scoring for
     * internationalization and document origin verification.
     */
    public class LanguageInfo
    {
        /*
         * Language Code Property
         * 
         * ISO language code for the detected language (e.g., "en-US", "es-ES").
         * Follows standard locale formatting for international compatibility.
         * 
         * JSON Property: "languageCode"
         * Format: ISO language-country code
         * Source: Azure Document Intelligence language detection
         */
        [JsonProperty("languageCode")]
        public string LanguageCode { get; set; } = string.Empty;

        /*
         * Confidence Property
         * 
         * Confidence score for the language detection, ranging from 0.0 to 1.0.
         * Higher values indicate greater certainty in the language identification.
         * 
         * JSON Property: "confidence"
         * Range: 0.0 (low confidence) to 1.0 (high confidence)
         * Source: Azure Document Intelligence confidence scoring
         */
        [JsonProperty("confidence")]
        public float Confidence { get; set; }
    }

    /*
     * SkippedDocument Class
     * 
     * Represents a document that could not be processed during validation.
     * Provides information about why the document was skipped and what
     * actions might resolve the issue.
     * 
     * Common Skip Reasons:
     * - Unrecognized document type
     * - Corrupted or invalid file format
     * - Processing errors or timeouts
     * - Insufficient content for analysis
     */
    public class SkippedDocument
    {
        /*
         * File Name Property
         * 
         * The original name of the skipped file for identification purposes.
         * Allows clients to identify which specific document encountered issues.
         * 
         * JSON Property: "fileName"
         * Source: Original FileData.FileName from request
         * Purpose: Issue identification and client notification
         */
        [JsonProperty("fileName")]
        public string FileName { get; set; } = string.Empty;

        /*
         * Reason Property
         * 
         * Human-readable explanation of why the document was skipped.
         * Provides specific information about the processing issue encountered
         * to help clients understand and potentially resolve the problem.
         * 
         * JSON Property: "reason"
         * Content: Descriptive explanation of the skip reason
         * Examples: "Document type could not be identified", 
         *           "Invalid file format", "Processing timeout"
         * Purpose: Client notification and troubleshooting guidance
         */
        [JsonProperty("reason")]
        public string Reason { get; set; } = string.Empty;
    }
} 