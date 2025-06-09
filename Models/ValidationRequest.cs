/*
 * ValidationRequest.cs - Request Models for Document Validation API
 * 
 * This file contains the data transfer objects (DTOs) used for incoming HTTP requests
 * to the document validation API. These models define the expected JSON structure
 * for validation requests and handle deserialization from client requests.
 * 
 * API Contract:
 * The validation API expects a JSON payload containing organization information
 * and an array of base64-encoded document files to be validated.
 * 
 * JSON Structure Example:
 * {
 *   "organizationname": "Sample Company LLC",
 *   "FEIN": "12-3456789",
 *   "files": [
 *     {
 *       "FileName": "tax-clearance.pdf",
 *       "FileContentBase64": "JVBERi0xLjQKJYqP..."
 *     }
 *   ]
 * }
 */

using Newtonsoft.Json;

namespace DocumentValidator.Models
{
    /*
     * ValidationRequest Class
     * 
     * Primary request model for the document validation API endpoint.
     * Contains organization metadata and collection of files to validate.
     * 
     * Design Notes:
     * - Uses Newtonsoft.Json attributes for custom JSON property mapping
     * - Property names are case-sensitive and must match API specification
     * - All properties have default values to prevent null reference exceptions
     */
    public class ValidationRequest
    {
        /*
         * Organization Name Property
         * 
         * The legal name of the organization whose documents are being validated.
         * This value is used for cross-referencing against document content to verify
         * that the submitted documents belong to the correct organization.
         * 
         * JSON Property: "organizationname" (lowercase, no spaces)
         * Validation: Required field, cannot be empty
         * Usage: Compared against detected organization names in document content
         */
        [JsonProperty("organizationname")]
        public string OrganizationName { get; set; } = string.Empty;

        /*
         * Federal Employer Identification Number (FEIN) Property
         * 
         * The organization's tax identification number assigned by the IRS.
         * Used for validation against tax-related documents and cross-referencing
         * applicant IDs found in certificates and clearances.
         * 
         * JSON Property: "FEIN" (uppercase)
         * Format: Can include or exclude hyphens (e.g., "12-3456789" or "123456789")
         * Validation: Required field, used for document authenticity verification
         * Usage: Last 3 digits compared against applicant IDs in tax clearance certificates
         */
        [JsonProperty("FEIN")]
        public string Fein { get; set; } = string.Empty;

        /*
         * Files Collection Property
         * 
         * Array of document files to be validated, each containing file metadata
         * and base64-encoded content. Supports multiple document validation in a single request.
         * 
         * JSON Property: "files"
         * Content Type: Array of FileData objects
         * Validation: Required field, must contain at least one file
         * Processing: Each file is validated independently and results are aggregated
         */
        [JsonProperty("files")]
        public List<FileData> Files { get; set; } = new List<FileData>();
    }

    /*
     * FileData Class
     * 
     * Represents an individual document file within a validation request.
     * Contains the file name and base64-encoded content for processing.
     * 
     * Supported File Formats:
     * - PDF documents (primary format)
     * - Image files (PNG, JPEG, TIFF) via Azure Document Intelligence OCR
     * - Other formats supported by Azure Document Intelligence
     * 
     * Size Limitations:
     * - Maximum file size is limited by Azure Functions payload limits (100MB)
     * - Base64 encoding increases size by approximately 33%
     */
    public class FileData
    {
        /*
         * File Name Property
         * 
         * The original name of the uploaded file, including file extension.
         * Used for identification in validation reports and error messages.
         * 
         * JSON Property: "FileName"
         * Validation: Required field, used for result identification
         * Usage: Displayed in PDF reports and response objects
         * Best Practice: Include descriptive names for better report clarity
         */
        [JsonProperty("FileName")]
        public string FileName { get; set; } = string.Empty;

        /*
         * File Content Base64 Property
         * 
         * The complete file content encoded as a base64 string.
         * This encoding allows binary file data to be transmitted via JSON.
         * 
         * JSON Property: "FileContentBase64"
         * Encoding: Standard base64 encoding without padding or line breaks
         * Validation: Required field, must be valid base64 format
         * Processing: Decoded to byte array for Azure Document Intelligence analysis
         * 
         * Security Note:
         * File content is processed in memory and not persisted to disk.
         * All data is discarded after validation completes.
         */
        [JsonProperty("FileContentBase64")]
        public string FileContentBase64 { get; set; } = string.Empty;
    }
} 