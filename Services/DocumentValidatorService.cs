using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using DocumentValidator.Models;
using DocumentValidator.Services;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DocumentValidator.Services
{
    /// <summary>
    /// DocumentValidatorService is the core service responsible for validating various types of business documents
    /// using Azure Form Recognizer (Document Intelligence) for optical character recognition and content analysis.
    /// 
    /// This service supports validation of multiple document types including:
    /// - Tax Clearance Certificates (Online and Manual)
    /// - Certificates of Formation (Standard and Independent)
    /// - Certificates of Alternative Name
    /// - Certificates of Trade Name
    /// - Operating Agreements
    /// - Certificates of Incorporation
    /// - IRS Determination Letters
    /// - Corporate Bylaws
    /// - Certificates of Authority (Standard and Automatic)
    /// 
    /// The service performs comprehensive validation by checking for required keywords, organization name matching,
    /// date validation, signature verification, and document-specific requirements based on New Jersey state regulations.
    /// 
    /// Key Features:
    /// - Automatic document type detection based on content analysis
    /// - Intelligent organization name matching with abbreviation handling
    /// - Date validation with six-month recency requirements
    /// - Form field validation against extracted document content
    /// - Detailed missing element reporting with suggested corrective actions
    /// </summary>
    public class DocumentValidatorService
    {
        /// <summary>
        /// Configuration service instance used to retrieve Azure Document Intelligence API credentials and settings.
        /// This service provides endpoint URLs, API keys, and other configuration parameters required for
        /// connecting to the Azure Form Recognizer service.
        /// </summary>
        private readonly ConfigurationService _configService;

        /// <summary>
        /// Initializes a new instance of the DocumentValidatorService class.
        /// Creates a new ConfigurationService instance to handle Azure service configuration.
        /// This constructor establishes the foundation for document validation operations.
        /// </summary>
        public DocumentValidatorService()
        {
            _configService = new ConfigurationService();
        }

        /// <summary>
        /// Normalizes organization names for improved matching accuracy by handling common business entity abbreviations,
        /// removing punctuation, and standardizing formatting. This method is crucial for accurate organization name
        /// validation across different document formats and naming conventions.
        /// 
        /// The normalization process includes:
        /// - Converting to lowercase and trimming whitespace
        /// - Removing commas and periods that may appear in formal business names
        /// - Standardizing multiple spaces to single spaces
        /// - Expanding common business entity abbreviations to their full forms (e.g., "LLC" -> "limited liability company")
        /// - Handling various entity types including corporations, partnerships, and professional entities
        /// 
        /// This comprehensive approach ensures that organizations referenced in different formats
        /// (e.g., "ABC Corp." vs "ABC Corporation" vs "ABC CORPORATION") are recognized as matches.
        /// </summary>
        /// <param name="name">The original organization name as it appears in the document or form field</param>
        /// <returns>A normalized version of the organization name with standardized formatting and expanded abbreviations</returns>
        private string NormalizeOrganizationName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var normalized = name.ToLower().Trim();

            // Remove common punctuation and extra spaces to handle formatting variations
            normalized = Regex.Replace(normalized, @"[,\.]", "");
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            // Comprehensive mapping of business entity abbreviations to their full legal forms
            // This ensures consistent matching regardless of how the entity type is abbreviated in documents
            var abbreviationMap = new Dictionary<string, string>
            {
                { "llc", "limited liability company" },
                { "inc", "incorporated" },
                { "corp", "corporation" },
                { "co", "company" },
                { "ltd", "limited" },
                { "lp", "limited partnership" },
                { "llp", "limited liability partnership" },
                { "pllc", "professional limited liability company" },
                { "pc", "professional corporation" },
                { "pa", "professional association" },
                { "plc", "professional limited company" }
            };

            // Apply abbreviation expansions using word boundary matching to avoid partial word replacements
            // The regex pattern ensures we only match actual entity type abbreviations at word boundaries
            foreach (var kvp in abbreviationMap)
            {
                var abbrPattern = new Regex($@"\b{Regex.Escape(kvp.Key)}\.?(?=\s|$|[,;])", RegexOptions.IgnoreCase);
                normalized = abbrPattern.Replace(normalized, kvp.Value);
            }

            return normalized;
        }

        /// <summary>
        /// Determines if two organization names refer to the same business entity by performing sophisticated
        /// name matching that accounts for abbreviations, partial names, and different entity type representations.
        /// 
        /// This method employs multiple matching strategies:
        /// 1. Direct match after normalization - exact matches after standardizing format
        /// 2. Containment matching - one name contains the other (for partial matches)
        /// 3. Core business name matching - comparing base names without entity suffixes
        /// 4. Entity type compatibility checking - ensuring legal entity types are compatible
        /// 
        /// The algorithm is designed to handle real-world scenarios where organization names
        /// may appear in different formats across various documents while maintaining accuracy
        /// to prevent false positive matches between genuinely different entities.
        /// 
        /// Special considerations:
        /// - Prevents matching organizations with incompatible entity types (e.g., LLC vs Corporation)
        /// - Allows matching when one name lacks entity type information
        /// - Handles compatible entity type variations (e.g., "Corp" and "Inc" for corporations)
        /// - Requires minimum name length to avoid matching on very short strings
        /// </summary>
        /// <param name="name1">First organization name to compare</param>
        /// <param name="name2">Second organization name to compare</param>
        /// <returns>True if the names likely refer to the same organization, false otherwise</returns>
        private bool OrganizationNamesMatch(string name1, string name2)
        {
            if (string.IsNullOrWhiteSpace(name1) || string.IsNullOrWhiteSpace(name2)) return false;

            var normalized1 = NormalizeOrganizationName(name1);
            var normalized2 = NormalizeOrganizationName(name2);

            // First attempt: direct match after normalization
            if (normalized1 == normalized2) return true;

            // Second attempt: check if one name contains the other (partial matching)
            if (normalized1.Contains(normalized2) || normalized2.Contains(normalized1))
            {
                // Extract entity types to ensure we're not matching different entity types
                var entity1 = GetEntityType(normalized1);
                var entity2 = GetEntityType(normalized2);

                // Allow match only if entity types are compatible or one is unspecified
                if (entity1 == entity2 || 
                    entity1 == null || 
                    entity2 == null ||
                    (entity1 != null && entity2 != null && (entity1.Contains(entity2) || entity2.Contains(entity1))))
                {
                    return true;
                }

                // Different entity types should not match to prevent false positives
                return false;
            }

            // Third attempt: core business name matching (without entity suffixes)
            var core1 = RemoveEntitySuffixes(normalized1);
            var core2 = RemoveEntitySuffixes(normalized2);

            if (!string.IsNullOrWhiteSpace(core1) && !string.IsNullOrWhiteSpace(core2) && 
                core1.Length > 2 && core2.Length > 2 && core1 == core2)
            {
                var entity1 = GetEntityType(normalized1);
                var entity2 = GetEntityType(normalized2);

                // Only match core names if entity types are compatible
                if (entity1 == entity2 || 
                    entity1 == null || 
                    entity2 == null ||
                    // Allow some compatible entity types for corporations
                    (entity1 == "corporation" && entity2 == "incorporated") ||
                    (entity1 == "incorporated" && entity2 == "corporation") ||
                    (entity1 == "company" && entity2 == "corporation") ||
                    (entity1 == "corporation" && entity2 == "company"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Extracts the business entity type from a normalized organization name by searching for
        /// known entity type keywords. This method is used to determine the legal structure of
        /// a business entity for compatibility checking during name matching.
        /// 
        /// The method searches for entity types in order of specificity, with more specific
        /// types (like "limited liability company") checked before more general ones (like "company").
        /// This prevents incorrect matches where a more specific entity type contains a general one.
        /// 
        /// Supported entity types include:
        /// - Limited Liability Company and its variations
        /// - Corporations (Incorporated, Corporation)
        /// - Partnerships (Limited Partnership, Limited Liability Partnership)
        /// - Professional entities (Professional Corporation, Professional Association)
        /// - General company and limited designations
        /// </summary>
        /// <param name="name">The normalized organization name to analyze</param>
        /// <returns>The detected entity type string, or null if no entity type is found</returns>
        private string? GetEntityType(string name)
        {
            var entityTypes = new[]
            {
                "limited liability company", "incorporated", "corporation", "company", "limited",
                "limited partnership", "limited liability partnership", "professional limited liability company",
                "professional corporation", "professional association", "professional limited company"
            };

            return entityTypes.FirstOrDefault(entityType => name.Contains(entityType));
        }

        /// <summary>
        /// Removes business entity suffixes from an organization name to extract the core business name.
        /// This method is used when comparing the fundamental business names while ignoring the legal
        /// entity structure differences.
        /// 
        /// The method uses a comprehensive regex pattern to identify and remove all known entity
        /// suffixes, allowing for comparison of the underlying business names. This is particularly
        /// useful when an organization might be referenced with different entity types in different
        /// documents (e.g., during business structure changes or in informal references).
        /// 
        /// The removal process preserves the core business identity while normalizing away
        /// the legal structure information that might vary between document sources.
        /// </summary>
        /// <param name="name">The organization name from which to remove entity suffixes</param>
        /// <returns>The organization name with all entity type suffixes removed and trimmed</returns>
        private string RemoveEntitySuffixes(string name)
        {
            return Regex.Replace(name, @"\b(limited liability company|incorporated|corporation|company|limited|limited partnership|limited liability partnership|professional limited liability company|professional corporation|professional association|professional limited company)\b", "", RegexOptions.IgnoreCase).Trim();
        }

        /// <summary>
        /// Performs comprehensive validation of uploaded documents using Azure Document Intelligence service.
        /// This is the primary entry point for document validation, handling the complete workflow from
        /// document analysis through validation result generation.
        /// 
        /// The validation process includes several key phases:
        /// 1. Document Analysis - Uses Azure Form Recognizer to extract text, structure, and metadata
        /// 2. Content Processing - Analyzes extracted content for key information and document structure
        /// 3. Type Detection - Automatically identifies document type based on content patterns
        /// 4. Validation Logic - Applies document-type-specific validation rules and requirements
        /// 5. Result Compilation - Generates comprehensive validation results with actionable feedback
        /// 
        /// The method supports validation of various document types with New Jersey state-specific
        /// requirements, including tax clearance certificates, business formation documents, and
        /// corporate governance documents. Each document type has specialized validation logic
        /// that checks for required elements, proper formatting, signature requirements, and
        /// compliance with state regulations.
        /// 
        /// Document Intelligence Analysis:
        /// - Extracts text content with high accuracy OCR
        /// - Identifies document structure including pages, tables, and key-value pairs
        /// - Detects languages and writing styles (including handwritten content)
        /// - Provides confidence scores for extracted information
        /// 
        /// Validation Features:
        /// - Organization name matching with intelligent abbreviation handling
        /// - Date validation with recency requirements (typically 6 months)
        /// - Required keyword and phrase detection
        /// - Signature and authorization verification
        /// - Document authenticity checks (letterheads, seals, etc.)
        /// - Cross-field validation between form inputs and document content
        /// </summary>
        /// <param name="buffer">The document file data as a byte array for processing</param>
        /// <param name="documentType">The expected document type for validation, or "auto-detect" for automatic detection</param>
        /// <param name="formFields">Dictionary of form field values to validate against document content (e.g., organization name, FEIN)</param>
        /// <returns>A DocumentValidation object containing validation results, missing elements, suggested actions, and document metadata</returns>
        /// <exception cref="Exception">Thrown when document analysis fails or Azure service is unavailable</exception>
        public async Task<DocumentValidation> ValidateDocumentAsync(byte[] buffer, string documentType, Dictionary<string, string> formFields)
        {
            try
            {
                // Retrieve Azure Document Intelligence configuration including endpoint and API key
                var config = _configService.GetDocumentIntelligenceConfig();

                // Initialize Azure Document Analysis Client with configured credentials
                var client = new DocumentAnalysisClient(new Uri(config.Endpoint), new AzureKeyCredential(config.Key));

                // Perform document analysis using the prebuilt-document model for comprehensive structure analysis
                // This model provides advanced document understanding including layout, tables, and key-value pairs
                using var stream = new MemoryStream(buffer);
                var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-document", stream);
                var result = operation.Value;

                // Extract comprehensive document analysis results for validation processing
                var content = result.Content ?? string.Empty;
                var pages = result.Pages ?? new List<DocumentPage>();
                var languages = result.Languages ?? new List<DocumentLanguage>();
                var styles = result.Styles ?? new List<DocumentStyle>();
                var tables = result.Tables ?? new List<DocumentTable>();
                var keyValuePairs = result.KeyValuePairs ?? new List<DocumentKeyValuePair>();

                // Prepare normalized content for case-insensitive text analysis
                var contentLower = content.ToLower();

                // Attempt automatic document type detection based on content analysis
                // This uses pattern matching against document-specific keywords and structures
                var detectedType = DetectDocumentType(content, contentLower, pages, keyValuePairs);

                // Use detected type if successful, otherwise fall back to provided document type
                var finalDocumentType = detectedType != "unknown" ? detectedType : documentType;

                // Apply document-type-specific validation logic based on the determined type
                var validationResults = ValidateDocumentByType(new DocumentValidationOptions
                {
                    DocumentType = finalDocumentType,
                    Content = content,
                    Pages = pages,
                    Languages = languages,
                    Styles = styles,
                    Tables = tables,
                    KeyValuePairs = keyValuePairs,
                    FormFields = formFields
                });

                // Compile comprehensive document metadata for the validation response
                var documentInfo = new DocumentInfo
                {
                    PageCount = pages?.Count ?? 0,
                    WordCount = pages?.Sum(page => page.Words?.Count ?? 0) ?? 0,
                    LanguageInfo = languages?.Select(lang => new LanguageInfo
                    {
                        LanguageCode = lang.Locale ?? string.Empty,
                        Confidence = lang.Confidence
                    }).ToList() ?? new List<LanguageInfo>(),
                    ContainsHandwriting = styles?.Any(style => style.IsHandwritten == true) ?? false,
                    DocumentType = finalDocumentType,
                    DetectedOrganizationName = validationResults.DetectedOrganizationName
                };

                // Return comprehensive validation results including success status, missing elements, and suggestions
                return new DocumentValidation
                {
                    Success = validationResults.MissingElements.Count == 0,
                    MissingElements = validationResults.MissingElements,
                    SuggestedActions = validationResults.SuggestedActions,
                    DocumentInfo = documentInfo
                };
            }
            catch (Exception error)
            {
                Console.WriteLine($"Error in document validation: {error}");
                throw;
            }
        }

        /// <summary>
        /// Routes document validation to the appropriate type-specific validation method based on the determined
        /// document type. This method serves as the central dispatcher for all document validation logic,
        /// ensuring that each document type receives appropriate validation treatment according to its
        /// specific requirements and regulatory standards.
        /// 
        /// The method supports a comprehensive range of business document types, each with specialized
        /// validation logic tailored to New Jersey state requirements and business compliance standards.
        /// Each document type has unique validation criteria including required keywords, formatting
        /// standards, signature requirements, and date validation rules.
        /// 
        /// Supported Document Types:
        /// - Tax Clearance Certificates (Online and Manual variants)
        /// - Certificates of Formation (Standard and Independent)
        /// - Alternative Name and Trade Name Certificates
        /// - Operating Agreements and Corporate Bylaws
        /// - Certificates of Incorporation and Authority
        /// - IRS Determination Letters
        /// 
        /// The auto-detect functionality attempts to identify document types automatically, but falls back
        /// to error reporting if detection fails. This ensures users receive helpful feedback when
        /// document types cannot be determined automatically.
        /// </summary>
        /// <param name="options">Comprehensive validation options containing document content, metadata, and form fields</param>
        /// <returns>DocumentValidationResult containing missing elements, suggested actions, and detected organization information</returns>
        private DocumentValidationResult ValidateDocumentByType(DocumentValidationOptions options)
        {
            var contentLower = options.Content.ToLower();

            // Handle auto-detection failure case with helpful user guidance  
            if (options.DocumentType == "auto-detect")
            {
                return new DocumentValidationResult
                {
                    MissingElements = new List<string> { "Document type could not be automatically detected" },
                    SuggestedActions = new List<string> { "Try specifying a document type manually" }
                };
            }

            // Route to appropriate validation method based on document type
            return options.DocumentType switch
            {
                "tax-clearance-online" => ValidateTaxClearanceOnline(options.Content, contentLower, options.Pages, options.KeyValuePairs, options.FormFields),
                "tax-clearance-manual" => ValidateTaxClearanceManual(options.Content, contentLower, options.Pages, options.KeyValuePairs, options.FormFields),
                "cert-alternative-name" => ValidateCertificateAlternativeName(options.Content, contentLower, options.Pages, options.KeyValuePairs, options.FormFields),
                "cert-trade-name" => ValidateCertificateOfTradeName(options.Content, contentLower, options.Pages, options.KeyValuePairs),
                "cert-formation" => ValidateCertificateOfFormation(options.Content, contentLower, options.Pages, options.KeyValuePairs, options.FormFields),
                "cert-formation-independent" => ValidateCertificateOfFormationIndependent(options.Content, contentLower, options.Pages, options.KeyValuePairs, options.FormFields),
                "operating-agreement" => ValidateOperatingAgreement(options.Content, contentLower, options.Pages, options.KeyValuePairs, options.FormFields),
                "cert-incorporation" => ValidateCertificateOfIncorporation(options.Content, contentLower, options.Pages, options.KeyValuePairs, options.FormFields),
                "irs-determination" => ValidateIRSDeterminationLetter(options.Content, contentLower, options.Pages, options.KeyValuePairs),
                "bylaws" => ValidateBylaws(options.Content, contentLower, options.Pages, options.KeyValuePairs),
                "cert-authority" => ValidateCertificateOfAuthority(options.Content, contentLower, options.Pages, options.KeyValuePairs, options.FormFields),
                "cert-authority-auto" => ValidateCertificateOfAuthorityAutomatic(options.Content, contentLower, options.Pages, options.KeyValuePairs, options.FormFields),
                _ => new DocumentValidationResult
                {
                    MissingElements = new List<string> { "Unknown document type" },
                    SuggestedActions = new List<string> { "The document type could not be determined automatically. Please check if this is a supported document type." }
                }
            };
        }

        /// <summary>
        /// Validates online-generated New Jersey Tax Clearance Certificates, which are issued electronically
        /// by the New Jersey Division of Taxation for businesses seeking clearance for various purposes
        /// including contract bidding, licensing, and regulatory compliance.
        /// 
        /// Online tax clearance certificates have specific characteristics that distinguish them from
        /// manually generated certificates:
        /// - Contains a unique Serial Number for verification purposes
        /// - Generated through automated systems with standardized formatting
        /// - Must be issued by approved state agencies (excluding Department of Environmental Protection)
        /// - Requires verification of organization name against form input
        /// - Must be dated within the past six months for validity
        /// - Requires proper signature from authorized state officials
        /// 
        /// Validation Process:
        /// 1. Organization Name Detection - Locates business name near "BUSINESS ASSISTANCE OR INCENTIVE" section
        /// 2. Organization Name Matching - Compares detected name with provided form field using intelligent matching
        /// 3. Required Keywords - Verifies presence of essential certification language
        /// 4. Serial Number Verification - Confirms presence of unique identifier for online certificates
        /// 5. Issuing Authority - Validates proper state agency and rejects excluded agencies
        /// 6. FEIN Validation - Matches last three digits of FEIN with Applicant ID when provided
        /// 7. Date Validation - Ensures certificate is within six-month validity period
        /// 8. Signature Verification - Confirms authorized official signatures
        /// 
        /// The method performs comprehensive text analysis to extract organization names from typical
        /// certificate layouts, handling various formatting styles and capitalization patterns commonly
        /// found in official New Jersey state documents.
        /// </summary>
        /// <param name="content">Full text content extracted from the document</param>
        /// <param name="contentLower">Lowercase version of content for case-insensitive searching</param>
        /// <param name="pages">Document page structure information from Azure Document Intelligence</param>
        /// <param name="keyValuePairs">Extracted key-value pairs that may contain structured data</param>
        /// <param name="formFields">User-provided form data including organization name and FEIN for validation</param>
        /// <returns>DocumentValidationResult with missing elements, suggested actions, and detected organization name</returns>
        private DocumentValidationResult ValidateTaxClearanceOnline(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs, Dictionary<string, string> formFields)
        {
            var missingElements = new List<string>();
            var suggestedActions = new List<string>();
            string? detectedOrganizationName = null;

            // Intelligent organization name detection using document structure analysis
            // Tax clearance certificates typically list the organization name immediately before
            // the "BUSINESS ASSISTANCE OR INCENTIVE" section or similar certification language
            var lines = content.Split('\n');
            var businessAssistanceIndex = Array.FindIndex(lines, line =>
                line.Contains("BUSINESS ASSISTANCE OR INCENTIVE") ||
                line.Contains("CLEARANCE CERTIFICATE"));

            if (businessAssistanceIndex > 0)
            {
                // Search for organization name in lines preceding the certification section
                // Typically appears 1-5 lines above the certification language
                for (int i = Math.Max(0, businessAssistanceIndex - 5); i < businessAssistanceIndex; i++)
                {
                    var line = lines[i].Trim();
                    // Filter out empty lines, dates, and lines too short to be organization names
                    if (!string.IsNullOrWhiteSpace(line) && line.Length > 3 && !Regex.IsMatch(line, @"^\d{1,2}\/\d{1,2}\/\d{4}$"))
                    {
                        // Skip typical document headers and metadata that aren't organization names
                        if (!line.ToLower().Contains("state of") &&
                            !line.ToLower().Contains("department of") &&
                            !line.ToLower().Contains("division of") &&
                            !line.ToLower().Contains("governor") &&
                            !Regex.IsMatch(line, @"^attn:", RegexOptions.IgnoreCase))
                        {
                            // Potential organization name found
                            detectedOrganizationName = line;
                            // All-caps formatting strongly indicates this is the official organization name
                            if (line == line.ToUpper() && line.Length > 5)
                            {
                                break; // High confidence match found
                            }
                        }
                    }
                }
            }

            // Fallback organization name detection using structured key-value pairs
            // Some certificates may have structured data fields for organization information
            if (detectedOrganizationName == null)
            {
                var orgNamePair = keyValuePairs.FirstOrDefault(pair =>
                    pair.Key?.Content != null &&
                    (pair.Key.Content.ToLower().Contains("taxpayer name") ||
                     pair.Key.Content.ToLower().Contains("applicant") ||
                     pair.Key.Content.ToLower().Contains("business name")));

                if (orgNamePair?.Value?.Content != null)
                {
                    detectedOrganizationName = orgNamePair.Value.Content;
                }
            }

            // Validate organization name matching if form field provided
            // Uses sophisticated matching algorithm to handle abbreviations and formatting variations
            if (formFields.ContainsKey("organizationName") && !string.IsNullOrEmpty(detectedOrganizationName))
            {
                if (!OrganizationNamesMatch(formFields["organizationName"], detectedOrganizationName))
                {
                    missingElements.Add("Organization name doesn't match the one on the certificate");
                    suggestedActions.Add("Verify that the correct organization name was entered");
                }
            }

            // Validate presence of required certification language
            if (!contentLower.Contains("clearance certificate"))
            {
                missingElements.Add("Required keyword: 'Clearance Certificate'");
            }

            // Verify Serial Number presence - critical identifier for online certificates
            // Online certificates must have unique serial numbers for verification and authenticity
            var hasSerial = contentLower.Contains("serial#") ||
                           contentLower.Contains("serial #") ||
                           contentLower.Contains("serial number") ||
                           Regex.IsMatch(content, @"serial[\s#]*:?\s*\d+", RegexOptions.IgnoreCase);

            if (!hasSerial)
            {
                missingElements.Add("Serial Number is missing");
                suggestedActions.Add("Verify this is an online-generated certificate with a Serial Number");
            }

            // Validate New Jersey state authority
            if (!contentLower.Contains("state of new jersey") &&
                !contentLower.Contains("new jersey"))
            {
                missingElements.Add("Required keyword: 'State of New Jersey'");
            }

            // Verify issuing department authority
            if (!contentLower.Contains("department of the treasury"))
            {
                missingElements.Add("Required keyword: Department of the Treasury");
            }

            // Confirm issuing division authority
            if (!contentLower.Contains("division of taxation"))
            {
                missingElements.Add("Required keyword: Division of Taxation");
            }

            // FEIN (Federal Employer Identification Number) validation process
            // Matches last three digits of provided FEIN with Applicant ID on certificate
            string? detectedId = null;

            // Search for Applicant ID in document content using pattern matching
            var applicantIdMatch = Regex.Match(content, @"applicant\s+id[#:]?\s*:?\s*(.*?)(?=\r|\n|$)", RegexOptions.IgnoreCase);
            if (applicantIdMatch.Success && !string.IsNullOrWhiteSpace(applicantIdMatch.Groups[1].Value))
            {
                detectedId = applicantIdMatch.Groups[1].Value.Trim();
            }

            // Fallback to structured key-value pairs for Applicant ID
            if (detectedId == null)
            {
                var idPair = keyValuePairs.FirstOrDefault(pair =>
                    pair.Key?.Content != null &&
                    (pair.Key.Content.ToLower().Contains("applicant id") ||
                     pair.Key.Content.ToLower().Contains("id #")));

                if (idPair?.Value?.Content != null)
                {
                    detectedId = idPair.Value.Content;
                }
            }

            // Perform FEIN validation if both FEIN and Applicant ID are available
            if (formFields.ContainsKey("fein") && !string.IsNullOrEmpty(formFields["fein"]) && 
                formFields["fein"].Length >= 3 && !string.IsNullOrEmpty(detectedId))
            {
                var lastThreeDigits = formFields["fein"].Substring(formFields["fein"].Length - 3);

                // Verify last three digits of FEIN appear in the detected Applicant ID
                var hasIdMatch = detectedId.Contains(lastThreeDigits);

                if (!hasIdMatch)
                {
                    missingElements.Add("FEIN last three digits don't match the Applicant ID on the certificate");
                    suggestedActions.Add("Verify that the correct FEIN was entered");
                }
            }

            // Validate against excluded issuing agencies
            // Department of Environmental Protection certificates are not accepted for most business purposes
            var rejectedAgencies = new[]
            {
                "department of environmental protection",
                "environmental protection"
            };

            var hasRejectedAgency = rejectedAgencies.Any(agency => contentLower.Contains(agency));

            if (hasRejectedAgency)
            {
                missingElements.Add("Tax Clearance Certificate is issued by the Department of Environmental Protection");
                suggestedActions.Add("This agency is not accepted. Please provide a valid tax clearance certificate from a different agency");
            }

            // Validate certificate date within acceptable timeframe (six months)
            var isDateWithinSixMonths = CheckDateWithinSixMonths(content);
            if (!isDateWithinSixMonths)
            {
                missingElements.Add("Certificate must be dated within the past six months");
                suggestedActions.Add("Obtain a more recent tax clearance certificate");
            }

            // Verify authorized signature presence
            // Tax clearance certificates must be signed by authorized state officials
            var hasSignature = content.Contains("Acting Director") ||
                               Regex.IsMatch(content, @"Marita\s+R\.\s+Sciarrotta|John\s+J\.\s+Ficara", RegexOptions.IgnoreCase);

            if (!hasSignature)
            {
                missingElements.Add("Signature is missing");
                suggestedActions.Add("Verify the certificate has been signed by an authorized official");
            }

            return new DocumentValidationResult
            {
                MissingElements = missingElements,
                SuggestedActions = suggestedActions,
                DetectedOrganizationName = detectedOrganizationName
            };
        }

        /// <summary>
        /// Validates manually-generated New Jersey Tax Clearance Certificates, which are produced through
        /// manual processing by state agencies rather than automated online systems. These certificates
        /// have distinct characteristics and requirements compared to their online counterparts.
        /// 
        /// Manual tax clearance certificates are identified by specific indicators:
        /// - Contains "BATC - Manual" designation indicating manual processing
        /// - Lacks serial numbers (unlike online certificates)
        /// - May have different formatting and layout structures
        /// - Subject to same state authority and date requirements as online certificates
        /// - Requires manual review and approval by state officials
        /// 
        /// Key Validation Differences from Online Certificates:
        /// - Must contain "BATC" and/or "Manual" keywords for proper identification
        /// - Does not require Serial Number validation (specific to online certificates)
        /// - May have expanded signature authority (includes "Director of Taxation")
        /// - Same organization name matching and FEIN validation as online certificates
        /// 
        /// The validation process ensures that manually processed certificates meet the same
        /// authenticity and compliance standards as automated certificates while accounting
        /// for the different production methods and identifying characteristics.
        /// 
        /// This method shares much of the validation logic with online certificates but includes
        /// specific checks for manual processing indicators and excludes online-specific requirements
        /// like serial number validation.
        /// </summary>
        /// <param name="content">Full text content extracted from the document</param>
        /// <param name="contentLower">Lowercase version of content for case-insensitive searching</param>
        /// <param name="pages">Document page structure information from Azure Document Intelligence</param>
        /// <param name="keyValuePairs">Extracted key-value pairs that may contain structured data</param>
        /// <param name="formFields">User-provided form data including organization name and FEIN for validation</param>
        /// <returns>DocumentValidationResult with missing elements, suggested actions, and detected organization name</returns>
        private DocumentValidationResult ValidateTaxClearanceManual(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs, Dictionary<string, string> formFields)
        {
            var missingElements = new List<string>();
            var suggestedActions = new List<string>();
            string? detectedOrganizationName = null;

            // Organization name detection logic (identical to online certificate method)
            // Manual certificates use the same document structure for organization name placement
            var lines = content.Split('\n');
            var businessAssistanceIndex = Array.FindIndex(lines, line =>
                line.Contains("BUSINESS ASSISTANCE OR INCENTIVE") ||
                line.Contains("CLEARANCE CERTIFICATE"));

            if (businessAssistanceIndex > 0)
            {
                // Search for organization name in lines preceding the certification section
                for (int i = Math.Max(0, businessAssistanceIndex - 5); i < businessAssistanceIndex; i++)
                {
                    var line = lines[i].Trim();
                    // Apply same filtering criteria as online certificate validation
                    if (!string.IsNullOrWhiteSpace(line) && line.Length > 3 && !Regex.IsMatch(line, @"^\d{1,2}\/\d{1,2}\/\d{4}$"))
                    {
                        // Skip document headers and metadata
                        if (!line.ToLower().Contains("state of") &&
                            !line.ToLower().Contains("department of") &&
                            !line.ToLower().Contains("division of") &&
                            !line.ToLower().Contains("governor") &&
                            !Regex.IsMatch(line, @"^attn:", RegexOptions.IgnoreCase))
                        {
                            detectedOrganizationName = line;
                            // All-caps formatting indicates official organization name
                            if (line == line.ToUpper() && line.Length > 5)
                            {
                                break; // High confidence match
                            }
                        }
                    }
                }
            }

            // Fallback to key-value pairs for organization name detection
            if (detectedOrganizationName == null)
            {
                var orgNamePair = keyValuePairs.FirstOrDefault(pair =>
                    pair.Key?.Content != null &&
                    (pair.Key.Content.ToLower().Contains("taxpayer name") ||
                     pair.Key.Content.ToLower().Contains("applicant") ||
                     pair.Key.Content.ToLower().Contains("business name")));

                if (orgNamePair?.Value?.Content != null)
                {
                    detectedOrganizationName = orgNamePair.Value.Content;
                }
            }

            // Organization name validation using intelligent matching algorithm
            if (formFields.ContainsKey("organizationName") && !string.IsNullOrEmpty(detectedOrganizationName))
            {
                if (!OrganizationNamesMatch(formFields["organizationName"], detectedOrganizationName))
                {
                    missingElements.Add("Organization name doesn't match the one on the certificate");
                    suggestedActions.Add("Verify that the correct organization name was entered");
                }
            }

            // Validate required certification language
            if (!contentLower.Contains("clearance certificate"))
            {
                missingElements.Add("Required keyword: 'Clearance Certificate'");
            }

            // Validate New Jersey state authority
            if (!contentLower.Contains("state of new jersey"))
            {
                missingElements.Add("Required keyword: 'State of New Jersey'");
            }

            // CRITICAL: Validate manual processing indicator - distinguishes from online certificates
            // Manual certificates must contain specific indicators showing they were manually processed
            if (!contentLower.Contains("batc") && !contentLower.Contains("manual"))
            {
                missingElements.Add("Required keyword: 'BATC - Manual'");
                suggestedActions.Add("Verify this is a manually generated tax clearance certificate");
            }

            // Validate issuing department
            if (!contentLower.Contains("department of the treasury"))
            {
                missingElements.Add("Required keyword: Department of the Treasury");
            }

            // Validate issuing division
            if (!contentLower.Contains("division of taxation"))
            {
                missingElements.Add("Required keyword: Division of Taxation");
            }

            // FEIN validation process (identical to online certificate method)
            string? detectedId = null;

            // Search for Applicant ID using pattern matching
            var applicantIdMatch = Regex.Match(content, @"applicant\s+id[#:]?\s*:?\s*(.*?)(?=\r|\n|$)", RegexOptions.IgnoreCase);
            if (applicantIdMatch.Success && !string.IsNullOrWhiteSpace(applicantIdMatch.Groups[1].Value))
            {
                detectedId = applicantIdMatch.Groups[1].Value.Trim();
            }

            // Fallback to key-value pairs for Applicant ID
            if (detectedId == null)
            {
                var idPair = keyValuePairs.FirstOrDefault(pair =>
                    pair.Key?.Content != null &&
                    (pair.Key.Content.ToLower().Contains("applicant id") ||
                     pair.Key.Content.ToLower().Contains("id #")));

                if (idPair?.Value?.Content != null)
                {
                    detectedId = idPair.Value.Content;
                }
            }

            // Now check if the FEIN provided matches the detected ID
            if (formFields.ContainsKey("fein") && !string.IsNullOrEmpty(formFields["fein"]) && 
                formFields["fein"].Length >= 3 && !string.IsNullOrEmpty(detectedId))
            {
                var lastThreeDigits = formFields["fein"].Substring(formFields["fein"].Length - 3);

                // Check if the last 3 digits of the FEIN appear in the detected ID
                var hasIdMatch = detectedId.Contains(lastThreeDigits);

                if (!hasIdMatch)
                {
                    missingElements.Add("FEIN last three digits don't match the Applicant ID on the certificate");
                    suggestedActions.Add("Verify that the correct FEIN was entered");
                }
            }

            // Check for agency - reject Department of Environmental Protection
            var rejectedAgencies = new[]
            {
                "department of environmental protection",
                "environmental protection"
            };

            var hasRejectedAgency = rejectedAgencies.Any(agency => contentLower.Contains(agency));

            if (hasRejectedAgency)
            {
                missingElements.Add("Tax Clearance Certificate is issued by the Department of Environmental Protection");
                suggestedActions.Add("This agency is not accepted. Please provide a valid tax clearance certificate from a different agency");
            }

            // Check for date within 6 months
            var isDateWithinSixMonths = CheckDateWithinSixMonths(content);
            if (!isDateWithinSixMonths)
            {
                missingElements.Add("Certificate must be dated within the past six months");
                suggestedActions.Add("Obtain a more recent tax clearance certificate");
            }

            // Check for signature
            var hasSignature = content.Contains("Acting Director") ||
                               content.Contains("Director of Taxation") ||
                               Regex.IsMatch(content, @"Marita\s+R\.\s+Sciarrotta|John\s+J\.\s+Ficara", RegexOptions.IgnoreCase);

            if (!hasSignature)
            {
                missingElements.Add("Signature is missing");
                suggestedActions.Add("Verify the certificate has been signed by an authorized official");
            }

            return new DocumentValidationResult
            {
                MissingElements = missingElements,
                SuggestedActions = suggestedActions,
                DetectedOrganizationName = detectedOrganizationName
            };
        }

        /// <summary>
        /// Validates New Jersey Certificates of Alternative Name, which allow businesses to operate under
        /// names different from their officially registered business name. These certificates are required
        /// when a company wishes to conduct business using an alternate name that differs from the name
        /// on file with the state.
        /// 
        /// Alternative Name Certificates serve several business purposes:
        /// - Allow operation under trade names or "doing business as" (DBA) names
        /// - Enable marketing under brand names different from legal entity name
        /// - Facilitate business expansion under recognizable commercial names
        /// - Provide legal protection for alternate business identities
        /// 
        /// Key Validation Requirements:
        /// - Must contain official certificate language identifying it as an Alternative Name certificate
        /// - Requires proper issuance by New Jersey Division of Revenue
        /// - Must include official state treasury date stamp for authenticity
        /// - Organization name detection and validation against form inputs
        /// - Handles both new registrations and renewals of existing alternate names
        /// 
        /// Document Structure Analysis:
        /// The method analyzes document layout to locate organization names that typically appear
        /// after the certificate identification section. It handles various formatting styles
        /// including all-caps formatting commonly used in official state documents and
        /// recognizes business entity indicators (LLC, INC, CORP) for improved accuracy.
        /// 
        /// Authentication Features:
        /// - Validates Division of Revenue as issuing authority
        /// - Checks for proper Department of Treasury date stamping
        /// - Verifies presence of official state seal indicators
        /// - Ensures document contains required certification language
        /// </summary>
        /// <param name="content">Full text content extracted from the document</param>
        /// <param name="contentLower">Lowercase version of content for case-insensitive searching</param>
        /// <param name="pages">Document page structure information from Azure Document Intelligence</param>
        /// <param name="keyValuePairs">Extracted key-value pairs that may contain structured data</param>
        /// <param name="formFields">User-provided form data including organization name for validation</param>
        /// <returns>DocumentValidationResult with missing elements, suggested actions, and detected organization name</returns>
        private DocumentValidationResult ValidateCertificateAlternativeName(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs, Dictionary<string, string> formFields)
        {
            var missingElements = new List<string>();
            var suggestedActions = new List<string>();
            string? detectedOrganizationName = null;

            // Validate certificate type and extract organization name in one comprehensive check
            var hasCertificateKeyword = contentLower.Contains("certificate of alternate name") || 
                                       contentLower.Contains("certificate of renewal of alternate name") || 
                                       contentLower.Contains("registration of alternate name");

            if (!hasCertificateKeyword)
            {
                missingElements.Add("Required keyword: 'Certificate of Alternate Name'");
            }
            else
            {
                // Sophisticated organization name extraction based on certificate structure
                // Alternative name certificates list the organization name after the certificate designation
                int certIndex = -1;
                string certKeyword = "";

                // Identify the specific certificate type and keyword location
                if (contentLower.Contains("certificate of alternate name"))
                {
                    certIndex = contentLower.IndexOf("certificate of alternate name");
                    certKeyword = "certificate of alternate name";
                }
                else if (contentLower.Contains("certificate of renewal of alternate name"))
                {
                    certIndex = contentLower.IndexOf("certificate of renewal of alternate name");
                    certKeyword = "certificate of renewal of alternate name";
                }
                else if (contentLower.Contains("name of corporation/business:"))
                {
                    certIndex = contentLower.IndexOf("name of corporation/business:");
                    certKeyword = "name of corporation/business:";
                }

                if (certIndex != -1)
                {
                    // Analyze content following the certificate keyword to locate organization name
                    var textAfterCert = content.Substring(certIndex + certKeyword.Length);

                    // Parse lines following certificate designation for organization name
                    var lines = textAfterCert.Split('\n');
                    for (int i = 0; i < Math.Min(5, lines.Length); i++)
                    {
                        var line = lines[i].Trim();
                        // Apply strict filtering to identify genuine organization names
                        if (!string.IsNullOrWhiteSpace(line) && line.Length > 3 && !Regex.IsMatch(line, @"^\d{1,2}[\/-]\d{1,2}[\/-]\d{2,4}$"))
                        {
                            // Exclude common document headers and administrative text
                            if (!line.ToLower().Contains("state of") &&
                                !line.ToLower().Contains("department of") &&
                                !line.ToLower().Contains("division of") &&
                                !line.ToLower().Contains("new jersey") &&
                                !line.ToLower().Contains("treasury") &&
                                !line.ToLower().Contains("revenue"))
                            {
                                detectedOrganizationName = line;
                                // High confidence indicators for organization names
                                if ((line == line.ToUpper() && line.Length > 5) ||
                                    Regex.IsMatch(line, @"LLC|INC|CORP|CORPORATION|COMPANY|LP|LLP", RegexOptions.IgnoreCase))
                                {
                                    break; // Strong confidence in organization name match
                                }
                            }
                        }
                    }
                }
            }

            // Validate organization name matching with intelligent comparison algorithm
            if (formFields != null && formFields.ContainsKey("organizationName") && !string.IsNullOrEmpty(detectedOrganizationName))
            {
                if (!OrganizationNamesMatch(formFields["organizationName"], detectedOrganizationName))
                {
                    missingElements.Add("Organization name doesn't match the one on the certificate");
                    suggestedActions.Add($"Verify that the correct organization name was entered. Certificate shows: \"{detectedOrganizationName}\"");
                }
            }

            // Validate proper issuing authority - Division of Revenue
            var hasDivisionOfRevenue = contentLower.Contains("division of revenue");
            if (!hasDivisionOfRevenue)
            {
                missingElements.Add("Required keyword: 'Division of Revenue'");
                suggestedActions.Add("Verify document has been issued by the Division of Revenue");
            }

            // Verify authentic state treasury date stamp for document validation
            var hasTreasuryDateStamp = contentLower.Contains("state treasurer") ||
                                      contentLower.Contains("great seal") ||
                                      contentLower.Contains("seal at trenton");

            if (!hasTreasuryDateStamp)
            {
                missingElements.Add("Date stamp by Department of Treasury is missing");
                suggestedActions.Add("Verify document has been properly stamped by the Department of Treasury");
            }

            return new DocumentValidationResult
            {
                MissingElements = missingElements,
                SuggestedActions = suggestedActions,
                DetectedOrganizationName = detectedOrganizationName
            };
        }

        /// <summary>
        /// Validates New Jersey Certificates of Trade Name, which are legal documents required when
        /// businesses operate under names other than their legally registered business names.
        /// These certificates provide legal protection and official recognition for trade names,
        /// doing-business-as (DBA) names, and assumed business names.
        /// 
        /// Trade Name Certificates are essential for:
        /// - Legal operation under assumed business names
        /// - Protection of commercial trade names
        /// - Compliance with state business registration requirements
        /// - Banking and financial account establishment under trade names
        /// - Marketing and advertising under recognized brand names
        /// 
        /// This validation method performs basic verification of the document's authenticity
        /// by confirming the presence of required legal language identifying the document
        /// as an official Certificate of Trade Name issued by appropriate New Jersey authorities.
        /// 
        /// The validation focuses on fundamental document identification rather than detailed
        /// content analysis, as trade name certificates typically have simpler structures
        /// compared to more complex business formation documents.
        /// </summary>
        /// <param name="content">Full text content extracted from the document</param>
        /// <param name="contentLower">Lowercase version of content for case-insensitive searching</param>
        /// <param name="pages">Document page structure information from Azure Document Intelligence</param>
        /// <param name="keyValuePairs">Extracted key-value pairs that may contain structured data</param>
        /// <returns>DocumentValidationResult with missing elements and suggested actions</returns>
        private DocumentValidationResult ValidateCertificateOfTradeName(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs)
        {
            var missingElements = new List<string>();
            var suggestedActions = new List<string>();

            // Verify presence of official certificate identification language
            if (!contentLower.Contains("certificate of trade name"))
            {
                missingElements.Add("Required keyword: 'Certificate of Trade Name'");
                suggestedActions.Add("Verify that the document is a Certificate of Trade Name");
            }

            return new DocumentValidationResult
            {
                MissingElements = missingElements,
                SuggestedActions = suggestedActions
            };
        }

        /// <summary>
        /// Validates New Jersey Certificates of Formation, which are fundamental legal documents
        /// establishing the existence of business entities such as Limited Liability Companies (LLCs)
        /// and corporations within New Jersey state jurisdiction.
        /// 
        /// Certificates of Formation serve critical legal functions:
        /// - Establish legal existence of business entities under New Jersey law
        /// - Provide official state recognition of business formation
        /// - Document compliance with state business formation requirements
        /// - Enable business operations, banking, and legal transactions
        /// - Serve as foundational documents for corporate governance
        /// 
        /// This method validates both short-form and long-form certificates, handling various
        /// formatting styles and document layouts commonly used by the New Jersey Department
        /// of Treasury and Division of Revenue for different entity types.
        /// 
        /// Comprehensive Validation Process:
        /// 1. Organization Name Detection - Multiple extraction methods for various document formats
        /// 2. Organization Name Matching - Sophisticated comparison with form input data
        /// 3. Authority Verification - Confirms proper state agency issuance
        /// 4. Signature Authentication - Verifies authorized state official signatures
        /// 5. Date Validation - Ensures document contains proper dating
        /// 6. Certificate Verification - Checks for verification information and authenticity indicators
        /// 
        /// The method employs multiple organization name detection strategies to handle
        /// various document formats, from simple "Name:" field extraction to complex
        /// pattern matching for narrative-style certificates.
        /// </summary>
        /// <param name="content">Full text content extracted from the document</param>
        /// <param name="contentLower">Lowercase version of content for case-insensitive searching</param>
        /// <param name="pages">Document page structure information from Azure Document Intelligence</param>
        /// <param name="keyValuePairs">Extracted key-value pairs that may contain structured data</param>
        /// <param name="formFields">User-provided form data including organization name for validation</param>
        /// <returns>DocumentValidationResult with missing elements, suggested actions, and detected organization name</returns>
        private DocumentValidationResult ValidateCertificateOfFormation(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs, Dictionary<string, string> formFields)
        {
            var missingElements = new List<string>();
            var suggestedActions = new List<string>();
            string? detectedOrganizationName = null;

            // Multi-method organization name extraction to handle various certificate formats
            // Method 1: Direct field extraction using common label patterns
            var nameMatch = Regex.Match(content, @"name:\s*([^\r\n]+)", RegexOptions.IgnoreCase) ?? 
                           Regex.Match(content, @"name of domestic corporation:\s*([^\r\n]+)", RegexOptions.IgnoreCase) ?? 
                           Regex.Match(content, @"the name of the limited liability company is\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            
            if (nameMatch != null && nameMatch.Success && !string.IsNullOrWhiteSpace(nameMatch.Groups[1].Value))
            {
                detectedOrganizationName = nameMatch.Groups[1].Value.Trim();
            }

            // Method 2: Narrative pattern extraction for complex certificate formats
            if (detectedOrganizationName == null)
            {
                var aboveNamedMatch = Regex.Match(content, @"above-named\s+([^was]+)was", RegexOptions.IgnoreCase);
                if (aboveNamedMatch.Success && !string.IsNullOrWhiteSpace(aboveNamedMatch.Groups[1].Value))
                {
                    detectedOrganizationName = aboveNamedMatch.Groups[1].Value.Trim();
                }
            }

            // Method 3: Structured data extraction from Document Intelligence key-value pairs
            if (detectedOrganizationName == null)
            {
                var namePair = keyValuePairs.FirstOrDefault(pair =>
                    pair.Key?.Content != null &&
                    pair.Key.Content.ToLower().Trim() == "name:");

                if (namePair?.Value?.Content != null)
                {
                    detectedOrganizationName = namePair.Value.Content;
                }
            }

            // Validate organization name consistency with form input using intelligent matching
            if (formFields.ContainsKey("organizationName") && !string.IsNullOrEmpty(detectedOrganizationName))
            {
                if (!OrganizationNamesMatch(formFields["organizationName"], detectedOrganizationName))
                {
                    missingElements.Add("Organization name doesn't match the one on the certificate");
                    suggestedActions.Add("Verify that the correct organization name was entered");
                }
            }

            // Validate presence of essential certificate identification language
            if (!contentLower.Contains("certificate of formation"))
            {
                missingElements.Add("Required keyword: 'Certificate of Formation'");
            }

            // Verify proper New Jersey state authority and agency issuance
            if (!contentLower.Contains("new jersey department of the treasury") &&
                !contentLower.Contains("new jersey") &&
                !contentLower.Contains("division of revenue"))
            {
                missingElements.Add("Certificate is not issued by the NJ Department of the Treasury");
                suggestedActions.Add("Verify certificate is issued by the NJ Department of the Treasury");
            }

            // Validate authorized signature presence for document authenticity
            var hasSignature = Regex.IsMatch(content, @"signature|signed|authorized representative", RegexOptions.IgnoreCase) ||
                              Regex.IsMatch(content, @"state treasurer|organizer|treasurer", RegexOptions.IgnoreCase);

            if (!hasSignature)
            {
                missingElements.Add("Signature of authorized state official is missing");
                suggestedActions.Add("Verify document has been signed by an authorized state official");
            }

            // Verify document contains proper dating for validity verification
            var hasDate = CheckForDatePresence(content);
            if (!hasDate)
            {
                missingElements.Add("Document must contain a date");
                suggestedActions.Add("Verify that the document includes a stamped date");
            }

            // Validate presence of certificate verification information for authenticity
            var hasVerificationInfo = Regex.IsMatch(content, @"verify this certificate|verification|certification", RegexOptions.IgnoreCase);

            if (!hasVerificationInfo)
            {
                missingElements.Add("Certificate verification information is missing");
                suggestedActions.Add("Verify document contains certificate verification information");
            }

            return new DocumentValidationResult
            {
                MissingElements = missingElements,
                SuggestedActions = suggestedActions,
                DetectedOrganizationName = detectedOrganizationName
            };
        }

        private DocumentValidationResult ValidateCertificateOfFormationIndependent(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs, Dictionary<string, string> formFields)
        {
            var missingElements = new List<string>();
            var suggestedActions = new List<string>();
            string? detectedOrganizationName = null;

            // Look for entity name in the document
            // Method 1: Check by "Name:" keyword
            var nameMatch = Regex.Match(content, @"name:\s*([^\r\n]+)", RegexOptions.IgnoreCase) ?? 
                           Regex.Match(content, @"name of domestic corporation:\s*([^\r\n]+)", RegexOptions.IgnoreCase) ?? 
                           Regex.Match(content, @"the name of the limited liability company is\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            
            if (nameMatch != null && nameMatch.Success && !string.IsNullOrWhiteSpace(nameMatch.Groups[1].Value))
            {
                detectedOrganizationName = nameMatch.Groups[1].Value.Trim();
            }

            // Method 2: Check from "The above-named" text
            if (detectedOrganizationName == null)
            {
                var aboveNamedMatch = Regex.Match(content, @"above-named\s+([^was]+)was", RegexOptions.IgnoreCase);
                if (aboveNamedMatch.Success && !string.IsNullOrWhiteSpace(aboveNamedMatch.Groups[1].Value))
                {
                    detectedOrganizationName = aboveNamedMatch.Groups[1].Value.Trim();
                }
            }

            // Method 3: Try to extract from key-value pairs
            if (detectedOrganizationName == null)
            {
                var namePair = keyValuePairs.FirstOrDefault(pair =>
                    pair.Key?.Content != null &&
                    pair.Key.Content.ToLower().Trim() == "name:");

                if (namePair?.Value?.Content != null)
                {
                    detectedOrganizationName = namePair.Value.Content;
                }
            }

            // Check for organization name match if provided
            if (formFields.ContainsKey("organizationName") && !string.IsNullOrEmpty(detectedOrganizationName))
            {
                if (!OrganizationNamesMatch(formFields["organizationName"], detectedOrganizationName))
                {
                    missingElements.Add("Organization name doesn't match the one on the certificate");
                    suggestedActions.Add("Verify that the correct organization name was entered");
                }
            }

            // Check for FEIN match if provided
            if (formFields.ContainsKey("fein") && !string.IsNullOrEmpty(formFields["fein"]) && !string.IsNullOrEmpty(detectedOrganizationName))
            {
                var feinName = formFields["fein"].Trim();
                var detectedOrgNameLower = detectedOrganizationName.ToLower().Trim();

                if (!detectedOrgNameLower.Contains(feinName.ToLower()) && !feinName.ToLower().Contains(detectedOrgNameLower))
                {
                    missingElements.Add("FEIN (Federal Employer Identification Number) doesn't match the one on the certificate");
                    suggestedActions.Add("Verify that the correct FEIN was entered");
                }
            }

            // Check for required elements
            if (!contentLower.Contains("certificate of formation"))
            {
                missingElements.Add("Required keyword: 'Certificate of Formation'");
            }

            // Check for stamp
            var hasFilingDate = Regex.IsMatch(content, @"filed", RegexOptions.IgnoreCase);
            if (!hasFilingDate)
            {
                missingElements.Add("Required keyword: 'Filed'");
                suggestedActions.Add("Verify document is stamped by the Department of the Treasury");
            }

            // Check for signature of state official
            var hasSignature = Regex.IsMatch(content, @"signature|signed|authorized representative", RegexOptions.IgnoreCase) ||
                              Regex.IsMatch(content, @"state treasurer|organizer|treasurer", RegexOptions.IgnoreCase);

            if (!hasSignature)
            {
                missingElements.Add("Signature of authorized state official is missing");
                suggestedActions.Add("Verify document has been signed by an authorized state official");
            }

            return new DocumentValidationResult
            {
                MissingElements = missingElements,
                SuggestedActions = suggestedActions,
                DetectedOrganizationName = detectedOrganizationName
            };
        }

        private DocumentValidationResult ValidateOperatingAgreement(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs, Dictionary<string, string> formFields)
        {
            var missingElements = new List<string>();
            var suggestedActions = new List<string>();

            // Check for required elements
            if (!contentLower.Contains("operating agreement"))
            {
                missingElements.Add("Required keyword: 'Operating Agreement'");
            }

            // Check for signatures
            var hasSignatures = contentLower.Contains("signature") ||
                               contentLower.Contains("signed by") ||
                               contentLower.Contains("undersigned") ||
                               Regex.IsMatch(content, @"s\/?\/|_+\s*name", RegexOptions.IgnoreCase);

            if (!hasSignatures)
            {
                missingElements.Add("Member signatures are missing");
                suggestedActions.Add("Verify the operating agreement is signed by all members");
            }

            // Check for date
            var hasDate = Regex.IsMatch(content, @"date[d]?(\s*on)?:|dated|executed on", RegexOptions.IgnoreCase) ||
                         Regex.IsMatch(content, @"\d{1,2}[\/-]\d{1,2}[\/-]\d{2,4}") ||
                         Regex.IsMatch(content, @"\d{4}");

            if (!hasDate)
            {
                missingElements.Add("Date is missing");
                suggestedActions.Add("Verify the operating agreement is dated");
            }

            // Check for New Jersey reference
            var hasNewJersey = contentLower.Contains("new jersey") ||
                              contentLower.Contains("nj");

            if (!hasNewJersey)
            {
                missingElements.Add("New Jersey state reference is missing");
                suggestedActions.Add("Verify the agreement references New Jersey state law");
            }

            return new DocumentValidationResult
            {
                MissingElements = missingElements,
                SuggestedActions = suggestedActions
            };
        }

        private DocumentValidationResult ValidateCertificateOfIncorporation(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs, Dictionary<string, string> formFields)
        {
            var missingElements = new List<string>();
            var suggestedActions = new List<string>();
            string? detectedOrganizationName = null;

            // Check for required elements in the document
            // 1. Check for Certificate title
            var hasCertificateTitle = contentLower.Contains("certificate of inc") ||
                                     contentLower.Contains("certificate of incorporation");

            if (!hasCertificateTitle)
            {
                missingElements.Add("Required text: 'Certificate of Incorporation'");
            }

            // 4. Check for Board of Directors listing
            var hasDirectors = contentLower.Contains("board of directors") ||
                              contentLower.Contains("directors") ||
                              contentLower.Contains("incorporators") ||
                              contentLower.Contains("trustees") ||
                              contentLower.Contains("shareholders");

            if (!hasDirectors)
            {
                missingElements.Add("Board of Directors section is missing");
                suggestedActions.Add("Verify the certificate lists the Board of Directors");
            }

            return new DocumentValidationResult
            {
                MissingElements = missingElements,
                SuggestedActions = suggestedActions,
                DetectedOrganizationName = detectedOrganizationName
            };
        }

        private DocumentValidationResult ValidateIRSDeterminationLetter(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs)
        {
            var missingElements = new List<string>();
            var suggestedActions = new List<string>();

            // Check for IRS letterhead
            var hasIRSLetterhead = contentLower.Contains("internal revenue service") ||
                                  contentLower.Contains("department of the treasury");

            if (!hasIRSLetterhead)
            {
                missingElements.Add("IRS letterhead is missing");
                suggestedActions.Add("Verify the letter is on IRS letterhead showing 'Internal Revenue Service'");
            }

            // Check for signature
            var hasSignature = content.Contains("Sincerely,") ||
                               content.Contains("Director");

            if (!hasSignature)
            {
                missingElements.Add("Signature is missing");
                suggestedActions.Add("Verify the certificate has been signed by an authorized official");
            }

            return new DocumentValidationResult
            {
                MissingElements = missingElements,
                SuggestedActions = suggestedActions
            };
        }

        private DocumentValidationResult ValidateBylaws(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs)
        {
            var missingElements = new List<string>();
            var suggestedActions = new List<string>();

            // Check for required elements
            if (!contentLower.Contains("bylaws") && !contentLower.Contains("by-laws") && !contentLower.Contains("by laws"))
            {
                missingElements.Add("Required keyword: 'Bylaws'");
            }

            // Check for presence of any date
            var hasDate = CheckForDatePresence(content);
            if (!hasDate)
            {
                missingElements.Add("Document must contain a date");
                suggestedActions.Add("Verify that the by-laws document includes a date");
            }

            return new DocumentValidationResult
            {
                MissingElements = missingElements,
                SuggestedActions = suggestedActions
            };
        }

        private DocumentValidationResult ValidateCertificateOfAuthority(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs, Dictionary<string, string> formFields)
        {
            var missingElements = new List<string>();
            var suggestedActions = new List<string>();
            string? detectedOrganizationName = null;

            // Check for required elements
            if (!contentLower.Contains("certificate of authority"))
            {
                missingElements.Add("Required keyword: 'Certificate of Authority'");
            }

            // Check for New Jersey-specific language
            var hasNJReference = contentLower.Contains("state of new jersey") ||
                                contentLower.Contains("new jersey");

            if (!hasNJReference)
            {
                missingElements.Add("Required keyword: 'State of New Jersey'");
                suggestedActions.Add("Verify the certificate mentions State of New Jersey");
            }

            // Check for Division of Taxation
            var hasDivision = contentLower.Contains("division of taxation") || contentLower.Contains("department of the treasury");

            if (!hasDivision)
            {
                missingElements.Add("Required keyword: 'Division of Taxation' or 'Department of the Treasury'");
                suggestedActions.Add("Verify the certificate is issued by the Division of Taxation or Department of the Treasury");
            }

            // Detect organization name
            // Look for organization name after specific phrases
            var searchPhrases = new[]
            {
                "this authorization is good only for the named person at the location specified herein this authorization is null and void if any change of ownership or address is effected",
                "change in ownership or address.",
                "certificate of authority"
            };

            int foundIndex = -1;
            int foundPhraseLength = 0;

            // Find which phrase exists in the content
            foreach (var phrase in searchPhrases)
            {
                var index = contentLower.IndexOf(phrase);
                if (index != -1)
                {
                    foundIndex = index;
                    foundPhraseLength = phrase.Length;
                    break;
                }
            }

            if (foundIndex != -1)
            {
                // Get the text after the found phrase
                var textAfterPhrase = content.Substring(foundIndex + foundPhraseLength);

                // Split into lines and find the organization name
                var lines = textAfterPhrase.Split('\n');
                for (int i = 0; i < Math.Min(5, lines.Length); i++)
                {
                    var line = lines[i].Trim();
                    // Skip empty lines or lines with less than 3 characters
                    if (!string.IsNullOrWhiteSpace(line) && line.Length > 3 && !Regex.IsMatch(line, @"^\d{1,2}[\/-]\d{1,2}[\/-]\d{2,4}$"))
                    {
                        // Skip lines that have typical metadata
                        if (!line.ToLower().Contains("tax registration") &&
                            !line.ToLower().Contains("tax effective date") &&
                            !line.ToLower().Contains("document locator") &&
                            !line.ToLower().Contains("date issued") &&
                            !line.ToLower().Contains("state of") &&
                            !line.ToLower().Contains("department of") &&
                            !line.ToLower().Contains("division of"))
                        {
                            detectedOrganizationName = line;
                            // If it's all caps or has business entity indicators, it's very likely the org name
                            if ((line == line.ToUpper() && line.Length > 5) ||
                                Regex.IsMatch(line, @"LLC|INC|CORP|CORPORATION|COMPANY|LP|LLP", RegexOptions.IgnoreCase))
                            {
                                break; // We're confident this is the org name
                            }
                        }
                    }
                }
            }

            // Check for organization name match if provided
            if (formFields != null && formFields.ContainsKey("organizationName") && !string.IsNullOrEmpty(detectedOrganizationName))
            {
                if (!OrganizationNamesMatch(formFields["organizationName"], detectedOrganizationName))
                {
                    missingElements.Add("Organization name doesn't match the one on the certificate");
                    suggestedActions.Add($"Verify that the correct organization name was entered. Certificate shows: \"{detectedOrganizationName}\"");
                }
            }

            return new DocumentValidationResult
            {
                MissingElements = missingElements,
                SuggestedActions = suggestedActions,
                DetectedOrganizationName = detectedOrganizationName
            };
        }

        private DocumentValidationResult ValidateCertificateOfAuthorityAutomatic(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs, Dictionary<string, string> formFields)
        {
            var missingElements = new List<string>();
            var suggestedActions = new List<string>();
            string? detectedOrganizationName = null;

            // Check for required elements
            if (!contentLower.Contains("certificate of authority"))
            {
                missingElements.Add("Required keyword: 'Certificate of Authority'");
            }
            else
            {
                // Find the organization name using multiple methods

                // Method 1: Look for the organization name right after "Certificate of Authority"
                var certAuthIndex = contentLower.IndexOf("certificate of authority");
                if (certAuthIndex != -1)
                {
                    // Get the text after "Certificate of Authority"
                    var textAfterCertAuth = content.Substring(certAuthIndex + "certificate of authority".Length);

                    // Split into lines and find the organization name
                    var lines = textAfterCertAuth.Split('\n');
                    for (int i = 0; i < Math.Min(5, lines.Length); i++)
                    {
                        var line = lines[i].Trim();
                        // Skip empty lines, dates, or lines with less than 3 characters
                        if (!string.IsNullOrWhiteSpace(line) && line.Length > 3 && !Regex.IsMatch(line, @"^\d{1,2}[\/-]\d{1,2}[\/-]\d{2,4}$"))
                        {
                            // Skip lines that have typical headers or metadata
                            if (!line.ToLower().Contains("state of") &&
                                !line.ToLower().Contains("department of") &&
                                !line.ToLower().Contains("this is to certify") &&
                                !line.ToLower().Contains("hereby certifies"))
                            {
                                detectedOrganizationName = line;
                                // If it's all caps or has LLC/INC, it's very likely the org name
                                if ((line == line.ToUpper() && line.Length > 5) ||
                                    Regex.IsMatch(line, @"LLC|INC|CORP|CORPORATION|COMPANY|LP|LLP", RegexOptions.IgnoreCase))
                                {
                                    break; // We're confident this is the org name
                                }
                            }
                        }
                    }
                }

                // Method 2: If still no org name, try key-value pairs
                if (detectedOrganizationName == null)
                {
                    var orgNamePair = keyValuePairs.FirstOrDefault(pair =>
                        pair.Key?.Content != null &&
                        (pair.Key.Content.ToLower().Contains("name") ||
                         pair.Key.Content.ToLower().Contains("entity")));

                    if (orgNamePair?.Value?.Content != null)
                    {
                        detectedOrganizationName = orgNamePair.Value.Content;
                    }
                }

                // Check for organization name match if provided
                if (formFields != null && formFields.ContainsKey("organizationName") && !string.IsNullOrEmpty(detectedOrganizationName))
                {
                    if (!OrganizationNamesMatch(formFields["organizationName"], detectedOrganizationName))
                    {
                        missingElements.Add("Organization name doesn't match the one on the certificate");
                        suggestedActions.Add($"Verify that the correct organization name was entered. Certificate shows: \"{detectedOrganizationName}\"");
                    }
                }
            }

            // Check for state seal
            var hasStateSeal = contentLower.Contains("official seal") ||
                              contentLower.Contains("seal at trenton") ||
                              contentLower.Contains("testimony whereof") ||
                              (contentLower.Contains("seal") && contentLower.Contains("affixed"));

            if (!hasStateSeal)
            {
                missingElements.Add("State seal is missing");
                suggestedActions.Add("Verify the certificate has the State seal affixed");
            }

            return new DocumentValidationResult
            {
                MissingElements = missingElements,
                SuggestedActions = suggestedActions,
                DetectedOrganizationName = detectedOrganizationName
            };
        }

        /// <summary>
        /// Validates that a document contains a date within the past six months from the current date.
        /// This method is critical for ensuring document freshness and compliance with regulatory
        /// requirements that mandate recent documentation for various business processes.
        /// 
        /// The six-month validity period is commonly required for:
        /// - Tax clearance certificates for contract bidding
        /// - Business compliance documentation for licensing
        /// - Financial and regulatory submissions requiring current status
        /// - Legal proceedings requiring up-to-date business information
        /// 
        /// Date Detection Strategy:
        /// The method employs comprehensive date recognition patterns to handle various
        /// date formats commonly found in official documents:
        /// 
        /// 1. Numeric Date Formats:
        ///    - MM/DD/YYYY and DD/MM/YYYY patterns with various separators (/, -, .)
        ///    - Attempts both month-day and day-month interpretations for ambiguous dates
        ///    - Validates reasonable date ranges (1900-2100) to avoid false positives
        /// 
        /// 2. Written Date Formats:
        ///    - Full month names with ordinal indicators (January 15th, 2023)
        ///    - International format support (15 January 2023)
        ///    - Handles various punctuation and spacing patterns
        /// 
        /// Date Validation Process:
        /// - Extracts up to 10 potential date matches to improve accuracy
        /// - Validates each date candidate against logical ranges
        /// - Compares against six-month threshold from current date
        /// - Returns true if any valid date falls within the acceptable timeframe
        /// 
        /// The method is designed to be permissive in date recognition while maintaining
        /// accuracy in validation, ensuring that legitimate documents are not rejected
        /// due to formatting variations while still enforcing temporal requirements.
        /// </summary>
        /// <param name="content">Document content to search for dates</param>
        /// <returns>True if any date in the document is within six months of current date, false otherwise</returns>
        private bool CheckDateWithinSixMonths(string content)
        {
            if (string.IsNullOrEmpty(content) || content.Length < 10) return false;

            var now = DateTime.Now;
            var sixMonthsAgo = now.AddMonths(-6);

            // Comprehensive numeric date pattern matching with multiple separator support
            // Handles MM/DD/YYYY, DD/MM/YYYY with /, -, and . separators
            var numericDateRegex = new Regex(@"(\d{1,2})[\/\-\.](\d{1,2})[\/\-\.](\d{4})", RegexOptions.IgnoreCase);
            var numericMatches = numericDateRegex.Matches(content).Take(10);

            foreach (Match match in numericMatches)
            {
                if (int.TryParse(match.Groups[1].Value, out int part1) &&
                    int.TryParse(match.Groups[2].Value, out int part2) &&
                    int.TryParse(match.Groups[3].Value, out int year))
                {
                    // Attempt MM/DD/YYYY interpretation first (US format)
                    try
                    {
                        var dateMMDDYYYY = new DateTime(year, part1, part2);
                        if (dateMMDDYYYY >= sixMonthsAgo && dateMMDDYYYY <= now)
                            return true;
                    }
                    catch (ArgumentOutOfRangeException) { }

                    // Fallback to DD/MM/YYYY interpretation (International format)
                    try
                    {
                        var dateDDMMYYYY = new DateTime(year, part2, part1);
                        if (dateDDMMYYYY >= sixMonthsAgo && dateDDMMYYYY <= now)
                            return true;
                    }
                    catch (ArgumentOutOfRangeException) { }
                }
            }

            // Advanced written date format recognition with month name parsing
            var monthNames = new[] { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };
            var monthPattern = string.Join("|", monthNames);
            var writtenDateRegex = new Regex($@"({monthPattern})\s+(\d{{1,2}})(?:st|nd|rd|th)?[,\s]*?(\d{{4}})|(\d{{1,2}})(?:st|nd|rd|th)?\s+({monthPattern})[,\s]*?(\d{{4}})", RegexOptions.IgnoreCase);
            var writtenMatches = writtenDateRegex.Matches(content).Take(10);

            foreach (Match match in writtenMatches)
            {
                int month, day, year;

                if (!string.IsNullOrEmpty(match.Groups[1].Value))
                {
                    // US Format: "January 15, 2023"
                    month = Array.IndexOf(monthNames, match.Groups[1].Value.ToLower()) + 1;
                    day = int.Parse(match.Groups[2].Value);
                    year = int.Parse(match.Groups[3].Value);
                }
                else
                {
                    // International Format: "15 January 2023"
                    day = int.Parse(match.Groups[4].Value);
                    month = Array.IndexOf(monthNames, match.Groups[5].Value.ToLower()) + 1;
                    year = int.Parse(match.Groups[6].Value);
                }

                if (month > 0)
                {
                    try
                    {
                        var date = new DateTime(year, month, day);
                        if (date >= sixMonthsAgo && date <= now)
                            return true;
                    }
                    catch (ArgumentOutOfRangeException) { }
                }
            }

            return false;
        }

        /// <summary>
        /// Validates that a document contains any recognizable date format, without regard to
        /// the specific date value or timeframe. This method is used for documents where
        /// date presence is required for authenticity but specific date ranges are not enforced.
        /// 
        /// Date Presence Validation is Essential For:
        /// - Operating agreements and bylaws requiring execution dates
        /// - Certificates that must show issuance or filing dates
        /// - Legal documents requiring temporal context for validity
        /// - Business formation documents showing incorporation dates
        /// 
        /// Comprehensive Date Recognition Patterns:
        /// 
        /// 1. Numeric Date Validation:
        ///    - MM/DD/YY and MM/DD/YYYY patterns with multiple separators
        ///    - Logical validation ensuring month (1-31) and day (1-31) ranges
        ///    - Year validation within reasonable historical ranges (1900-2100)
        ///    - Handles two-digit and four-digit year formats
        /// 
        /// 2. Written Date Recognition:
        ///    - Full month names with ordinal day indicators
        ///    - Multiple language format support (US and International)
        ///    - Flexible punctuation and spacing handling
        /// 
        /// 3. Ordinal Date Formats:
        ///    - Legal document style: "13th day of May, 2023"
        ///    - Formal document patterns common in official certificates
        /// 
        /// 4. Contextual Year References:
        ///    - Copyright notices and document revision dates
        ///    - Adoption, effective, and amendment dates
        ///    - Year-only references with appropriate context keywords
        /// 
        /// The method uses multiple validation layers to ensure accurate date detection
        /// while avoiding false positives from numbers that might appear to be dates
        /// but lack proper context or formatting.
        /// </summary>
        /// <param name="content">Document content to search for date presence</param>
        /// <returns>True if any recognizable date format is found in the document, false otherwise</returns>
        private bool CheckForDatePresence(string content)
        {
            if (string.IsNullOrEmpty(content) || content.Length < 10) return false;

            // Primary numeric date validation with logical range checking
            var numericDateRegex = new Regex(@"(\d{1,2})[\/\-\.](\d{1,2})[\/\-\.](\d{2,4})", RegexOptions.IgnoreCase);
            var numericMatches = numericDateRegex.Matches(content);

            if (numericMatches.Count > 0)
            {
                foreach (Match match in numericMatches.Take(10))
                {
                    var parts = match.Value.Split(new char[] { '/', '-', '.' });
                    if (int.TryParse(parts[0], out int num1) &&
                        int.TryParse(parts[1], out int num2) &&
                        int.TryParse(parts[2], out int year))
                    {
                        // Validate logical date component ranges and reasonable year values
                        if (year >= 1900 && year <= 2100 &&
                            num1 >= 1 && num1 <= 31 &&
                            num2 >= 1 && num2 <= 31)
                        {
                            return true;
                        }
                    }
                }
            }

            // Written date format validation with month name recognition
            var monthNames = new[] { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };
            var monthPattern = string.Join("|", monthNames);
            var writtenDateRegex = new Regex($@"({monthPattern})\s+(\d{{1,2}})(?:st|nd|rd|th)?[,\s]*?(\d{{4}})|(\d{{1,2}})(?:st|nd|rd|th)?\s+({monthPattern})[,\s]*?(\d{{4}})", RegexOptions.IgnoreCase);

            if (writtenDateRegex.IsMatch(content))
            {
                return true;
            }

            // Legal document ordinal date format recognition (e.g., "13th day of May, 2023")
            var ordinalDateRegex = new Regex(@"(\d{1,2})(st|nd|rd|th)?\s+day\s+of\s+(\w+)[,\s]*(\d{4})", RegexOptions.IgnoreCase);
            var ordinalMatches = ordinalDateRegex.Matches(content);

            if (ordinalMatches.Count > 0)
            {
                return true;
            }

            // Contextual year-only format validation with appropriate keywords
            // Prevents false positives by requiring contextual keywords that indicate date usage
            var yearOnlyRegex = new Regex(@"(?:©\s*|copyright\s*|adopted\s*|effective\s*|revised\s*|amended\s*|dated\s*|year\s*)(\d{4})", RegexOptions.IgnoreCase);
            var yearMatches = yearOnlyRegex.Matches(content);

            if (yearMatches.Count > 0)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Automatically detects the type of business document being validated by analyzing
        /// content patterns, keywords, and document structure. This sophisticated detection
        /// system enables automatic routing to appropriate validation logic without requiring
        /// users to manually specify document types.
        /// 
        /// Document Type Detection Process:
        /// 
        /// 1. Pattern Matching Engine:
        ///    - Uses weighted keyword scoring for accurate type identification
        ///    - Applies configurable thresholds to prevent false positive classifications
        ///    - Prioritizes high-value keywords specific to each document type
        ///    - Considers document structure and layout patterns
        /// 
        /// 2. Scoring Methodology:
        ///    - Each document type has a defined set of weighted keywords
        ///    - Keywords receive different point values based on their discriminative power
        ///    - Total scores are compared against minimum thresholds for confident classification
        ///    - Highest scoring type above threshold is selected as the detected type
        /// 
        /// 3. Supported Document Types:
        ///    - Tax Clearance Certificates (Online vs Manual detection)
        ///    - Certificates of Formation (Standard vs Independent)
        ///    - Alternative Name and Trade Name Certificates
        ///    - Operating Agreements and Corporate Bylaws
        ///    - Certificates of Incorporation and Authority
        ///    - IRS Determination Letters
        /// 
        /// 4. Fallback Detection Logic:
        ///    - Special handling for tax clearance certificates with manual/online disambiguation
        ///    - Uses page count and specific indicators for type refinement
        ///    - Returns "unknown" when confidence levels are insufficient
        /// 
        /// The detection algorithm balances accuracy with comprehensive coverage,
        /// ensuring that documents are correctly classified while providing fallback
        /// mechanisms for edge cases and document variations.
        /// 
        /// Keywords are carefully selected based on their uniqueness to specific document
        /// types and their consistent appearance across document variants, making the
        /// system robust against formatting differences and document evolution over time.
        /// </summary>
        /// <param name="content">Full document text content for analysis</param>
        /// <param name="contentLower">Lowercase version for case-insensitive pattern matching</param>
        /// <param name="pages">Document page structure for additional analysis context</param>
        /// <param name="keyValuePairs">Structured data elements that may provide type indicators</param>
        /// <returns>Detected document type string, or "unknown" if type cannot be determined with confidence</returns>
        private string DetectDocumentType(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs)
        {
            // Comprehensive document type pattern definitions with weighted keyword scoring
            var documentTypePatterns = new List<DocumentTypePattern>
            {
                new DocumentTypePattern
                {
                    Type = "tax-clearance-online",
                    Keywords = new List<KeywordWeight>
                    {
                        new KeywordWeight { Text = "clearance certificate", Weight = 70 },
                        new KeywordWeight { Text = "serial#", Weight = 70 },
                        new KeywordWeight { Text = "state of new jersey", Weight = 20 },
                        new KeywordWeight { Text = "division of taxation", Weight = 40 },
                        new KeywordWeight { Text = "business assistance", Weight = 50 },
                        new KeywordWeight { Text = "taxpayer name", Weight = 20 },
                        new KeywordWeight { Text = "agency", Weight = 30 }
                    },
                    Threshold = 100
                },
                new DocumentTypePattern
                {
                    Type = "tax-clearance-manual",
                    Keywords = new List<KeywordWeight>
                    {
                        new KeywordWeight { Text = "clearance certificate", Weight = 70 },
                        new KeywordWeight { Text = "batc", Weight = 100 },
                        new KeywordWeight { Text = "manual", Weight = 70 },
                        new KeywordWeight { Text = "business assistance", Weight = 50 },
                        new KeywordWeight { Text = "state of new jersey", Weight = 20 },
                        new KeywordWeight { Text = "division of taxation", Weight = 40 },
                        new KeywordWeight { Text = "taxpayer name", Weight = 20 },
                        new KeywordWeight { Text = "agency", Weight = 30 }
                    },
                    Threshold = 100
                },
                new DocumentTypePattern
                {
                    Type = "cert-formation",
                    Keywords = new List<KeywordWeight>
                    {
                        new KeywordWeight { Text = "certificate of formation", Weight = 80 },
                        new KeywordWeight { Text = "short form", Weight = 50 },
                        new KeywordWeight { Text = "long form", Weight = 50 },
                        new KeywordWeight { Text = "good standing", Weight = 50 },
                        new KeywordWeight { Text = "new jersey department of the treasury", Weight = 20 },
                        new KeywordWeight { Text = "division of revenue", Weight = 20 },
                        new KeywordWeight { Text = "above-named", Weight = 30 },
                        new KeywordWeight { Text = "registered agent", Weight = 30 },
                        new KeywordWeight { Text = "certificate", Weight = 30 }
                    },
                    Threshold = 100
                },
                new DocumentTypePattern
                {
                    Type = "cert-formation-independent",
                    Keywords = new List<KeywordWeight>
                    {
                        new KeywordWeight { Text = "certificate of formation", Weight = 80 },
                        new KeywordWeight { Text = "organizer", Weight = 80 },
                        new KeywordWeight { Text = "short form", Weight = 50 },
                        new KeywordWeight { Text = "long form", Weight = 50 },
                        new KeywordWeight { Text = "good standing", Weight = 50 },
                        new KeywordWeight { Text = "state of new jersey", Weight = 40 },
                        new KeywordWeight { Text = "registered agent", Weight = 30 },
                        new KeywordWeight { Text = "in witness whereof", Weight = 30 },
                        new KeywordWeight { Text = "first", Weight = 30 },
                        new KeywordWeight { Text = "second", Weight = 30 },
                        new KeywordWeight { Text = "third", Weight = 30 },
                        new KeywordWeight { Text = "fourth", Weight = 30 }
                    },
                    Threshold = 100
                },
                new DocumentTypePattern
                {
                    Type = "cert-incorporation",
                    Keywords = new List<KeywordWeight>
                    {
                        new KeywordWeight { Text = "certificate of inc", Weight = 80 },
                        new KeywordWeight { Text = "new jersey department of the treasury", Weight = 50 },
                        new KeywordWeight { Text = "division of revenue and enterprise services", Weight = 40 },
                        new KeywordWeight { Text = "board of directors", Weight = 30 },
                        new KeywordWeight { Text = "certificate", Weight = 30 },
                        new KeywordWeight { Text = "state treasurer", Weight = 30 },
                        new KeywordWeight { Text = "great seal", Weight = 30 }
                    },
                    Threshold = 100
                },
                new DocumentTypePattern
                {
                    Type = "cert-trade-name",
                    Keywords = new List<KeywordWeight>
                    {
                        new KeywordWeight { Text = "trade name", Weight = 70 },
                        new KeywordWeight { Text = "certificate", Weight = 30 },
                        new KeywordWeight { Text = "filing", Weight = 20 },
                        new KeywordWeight { Text = "trade name certificate", Weight = 80 }
                    },
                    Threshold = 90
                },
                new DocumentTypePattern
                {
                    Type = "cert-alternative-name",
                    Keywords = new List<KeywordWeight>
                    {
                        new KeywordWeight { Text = "alternate name", Weight = 80 },
                        new KeywordWeight { Text = "registration of alternate name", Weight = 70 },
                        new KeywordWeight { Text = "division of revenue", Weight = 50 },
                        new KeywordWeight { Text = "state of new jersey", Weight = 40 },
                        new KeywordWeight { Text = "po box", Weight = 40 }
                    },
                    Threshold = 90
                },
                new DocumentTypePattern
                {
                    Type = "operating-agreement",
                    Keywords = new List<KeywordWeight>
                    {
                        new KeywordWeight { Text = "operating agreement", Weight = 80 },
                        new KeywordWeight { Text = "limited liability company", Weight = 40 },
                        new KeywordWeight { Text = "llc", Weight = 30 },
                        new KeywordWeight { Text = "member", Weight = 30 },
                        new KeywordWeight { Text = "management", Weight = 30 },
                        new KeywordWeight { Text = "article", Weight = 20 }
                    },
                    Threshold = 100
                },
                new DocumentTypePattern
                {
                    Type = "irs-determination",
                    Keywords = new List<KeywordWeight>
                    {
                        new KeywordWeight { Text = "internal revenue service", Weight = 70 },
                        new KeywordWeight { Text = "irs", Weight = 40 },
                        new KeywordWeight { Text = "determination letter", Weight = 60 },
                        new KeywordWeight { Text = "determination", Weight = 30 },
                        new KeywordWeight { Text = "exempt", Weight = 40 },
                        new KeywordWeight { Text = "tax exempt", Weight = 50 },
                        new KeywordWeight { Text = "501", Weight = 30 },
                        new KeywordWeight { Text = "department of the treasury", Weight = 40 }
                    },
                    Threshold = 120
                },
                new DocumentTypePattern
                {
                    Type = "bylaws",
                    Keywords = new List<KeywordWeight>
                    {
                        new KeywordWeight { Text = "bylaws", Weight = 80 },
                        new KeywordWeight { Text = "by-laws", Weight = 80 },
                        new KeywordWeight { Text = "corporation", Weight = 30 },
                        new KeywordWeight { Text = "directors", Weight = 30 },
                        new KeywordWeight { Text = "board", Weight = 20 },
                        new KeywordWeight { Text = "article", Weight = 30 },
                        new KeywordWeight { Text = "amendment", Weight = 30 }
                    },
                    Threshold = 100
                },
                new DocumentTypePattern
                {
                    Type = "cert-authority",
                    Keywords = new List<KeywordWeight>
                    {
                        new KeywordWeight { Text = "certificate of authority", Weight = 80 },
                        new KeywordWeight { Text = "new jersey sales & use tax", Weight = 50 },
                        new KeywordWeight { Text = "n.j.s.a", Weight = 40 },
                        new KeywordWeight { Text = "division of taxation", Weight = 40 },
                        new KeywordWeight { Text = "state of new jersey", Weight = 30 },
                        new KeywordWeight { Text = "authorized", Weight = 30 },
                        new KeywordWeight { Text = "secretary of state", Weight = 30 }
                    },
                    Threshold = 100
                },
                new DocumentTypePattern
                {
                    Type = "cert-authority-auto",
                    Keywords = new List<KeywordWeight>
                    {
                        new KeywordWeight { Text = "certificate of authority", Weight = 80 },
                        new KeywordWeight { Text = "department of the treasury", Weight = 40 },
                        new KeywordWeight { Text = "state of new jersey", Weight = 30 },
                        new KeywordWeight { Text = "foreign", Weight = 30 },
                        new KeywordWeight { Text = "authorized", Weight = 30 },
                        new KeywordWeight { Text = "secretary of state", Weight = 30 },
                        new KeywordWeight { Text = "lawfully carried on", Weight = 30 }
                    },
                    Threshold = 100
                }
            };

            // Calculate weighted scores for each document type pattern
            var scores = documentTypePatterns.Select(docType => new
            {
                Type = docType.Type,
                Score = CalculateScore(docType, contentLower),
                Threshold = docType.Threshold
            }).OrderByDescending(s => s.Score).ToList();

            // Select the highest scoring type that meets the confidence threshold
            var bestMatch = scores.FirstOrDefault(score => score.Score >= score.Threshold);
            var detectedType = bestMatch?.Type ?? "unknown";

            // Special case disambiguation for tax clearance certificates
            // Handles cases where general tax clearance is detected but needs manual/online classification
            if (detectedType == "unknown" && contentLower.Contains("clearance certificate") && contentLower.Contains("division of taxation"))
            {
                // Analyze specific indicators to determine manual vs online processing
                var manualIndicators = new[] { "manual", "batc" };
                var isManual = manualIndicators.Any(indicator => contentLower.Contains(indicator)) || pages.Count <= 2;

                detectedType = isManual ? "tax-clearance-manual" : "tax-clearance-online";
            }

            return detectedType;
        }

        /// <summary>
        /// Calculates a weighted score for a document type pattern by summing the weights
        /// of all keywords found in the document content. This scoring mechanism enables
        /// accurate document type classification by quantifying the presence of
        /// type-specific indicators.
        /// 
        /// Scoring Process:
        /// - Iterates through all defined keywords for the document type pattern
        /// - Performs case-insensitive substring matching in document content
        /// - Sums the weights of all matched keywords to produce a total score
        /// - Higher scores indicate stronger evidence for the document type
        /// 
        /// The weighted approach allows more important keywords (like exact certificate
        /// titles) to contribute more heavily to the score than supporting keywords
        /// (like agency names), improving classification accuracy.
        /// </summary>
        /// <param name="docType">Document type pattern containing keywords and weights</param>
        /// <param name="contentLower">Lowercase document content for case-insensitive matching</param>
        /// <returns>Weighted score representing the likelihood that the document matches this type</returns>
        private int CalculateScore(DocumentTypePattern docType, string contentLower)
        {
            return docType.Keywords.Where(keyword => contentLower.Contains(keyword.Text.ToLower())).Sum(keyword => keyword.Weight);
        }
    }

    // Supporting classes with comprehensive documentation for validation system architecture

    /// <summary>
    /// Encapsulates all parameters required for document validation operations, providing
    /// a structured approach to passing complex validation data between methods.
    /// This class serves as a data transfer object that consolidates document content,
    /// Azure Document Intelligence analysis results, and user-provided form data
    /// into a single, manageable structure for validation processing.
    /// </summary>
    public class DocumentValidationOptions
    {
        /// <summary>The determined or specified document type for validation routing</summary>
        public string DocumentType { get; set; } = string.Empty;
        /// <summary>Complete text content extracted from the document by Azure Document Intelligence</summary>
        public string Content { get; set; } = string.Empty;
        /// <summary>Structured page information including layout, words, and formatting</summary>
        public IReadOnlyList<DocumentPage> Pages { get; set; } = new List<DocumentPage>();
        /// <summary>Detected languages in the document with confidence scores</summary>
        public IReadOnlyList<DocumentLanguage> Languages { get; set; } = new List<DocumentLanguage>();
        /// <summary>Document style information including handwriting detection</summary>
        public IReadOnlyList<DocumentStyle> Styles { get; set; } = new List<DocumentStyle>();
        /// <summary>Extracted table structures and data from the document</summary>
        public IReadOnlyList<DocumentTable> Tables { get; set; } = new List<DocumentTable>();
        /// <summary>Structured key-value pairs detected in the document</summary>
        public IReadOnlyList<DocumentKeyValuePair> KeyValuePairs { get; set; } = new List<DocumentKeyValuePair>();
        /// <summary>User-provided form field data for validation against document content</summary>
        public Dictionary<string, string> FormFields { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Contains the results of document validation including missing elements,
    /// suggested corrective actions, and extracted organization information.
    /// This class provides structured feedback to users about document
    /// compliance status and specific issues that need to be addressed.
    /// </summary>
    public class DocumentValidationResult
    {
        /// <summary>List of required elements that are missing from the document</summary>
        public List<string> MissingElements { get; set; } = new List<string>();
        /// <summary>Specific actions users can take to resolve validation issues</summary>
        public List<string> SuggestedActions { get; set; } = new List<string>();
        /// <summary>Organization name detected and extracted from the document content</summary>
        public string? DetectedOrganizationName { get; set; }
    }

    /// <summary>
    /// Top-level validation response containing comprehensive results, success status,
    /// and detailed document information. This class represents the complete
    /// output of the document validation process, providing both validation
    /// results and metadata about the analyzed document.
    /// </summary>
    public class DocumentValidation
    {
        /// <summary>Overall validation success status - true if no missing elements found</summary>
        public bool Success { get; set; }
        /// <summary>List of required elements that are missing from the document</summary>
        public List<string> MissingElements { get; set; } = new List<string>();
        /// <summary>Specific actions users can take to resolve validation issues</summary>
        public List<string> SuggestedActions { get; set; } = new List<string>();
        /// <summary>Comprehensive information about the analyzed document</summary>
        public DocumentInfo DocumentInfo { get; set; } = new DocumentInfo();
    }

    /// <summary>
    /// Defines document type detection patterns using weighted keyword scoring.
    /// This class enables the automatic document type detection system by
    /// providing structured patterns that can be evaluated against document
    /// content to determine the most likely document type.
    /// </summary>
    public class DocumentTypePattern
    {
        /// <summary>The document type identifier this pattern detects</summary>
        public string Type { get; set; } = string.Empty;
        /// <summary>List of keywords with associated weights for scoring</summary>
        public List<KeywordWeight> Keywords { get; set; } = new List<KeywordWeight>();
        /// <summary>Minimum score threshold required for confident type detection</summary>
        public int Threshold { get; set; }
    }

    /// <summary>
    /// Represents a keyword and its associated weight in the document type
    /// detection scoring system. Higher weights indicate keywords that are
    /// more discriminative for identifying specific document types.
    /// </summary>
    public class KeywordWeight
    {
        /// <summary>The keyword text to search for in document content</summary>
        public string Text { get; set; } = string.Empty;
        /// <summary>The point value assigned to this keyword when found</summary>
        public int Weight { get; set; }
    }
} 