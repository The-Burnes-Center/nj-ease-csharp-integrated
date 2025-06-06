using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using DocumentValidator.Models;
using DocumentValidator.Services;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DocumentValidator.Services
{
    public class DocumentValidatorService
    {
        private readonly ConfigurationService _configService;

        public DocumentValidatorService()
        {
            _configService = new ConfigurationService();
        }

        // Helper function to normalize organization names for better matching
        private string NormalizeOrganizationName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var normalized = name.ToLower().Trim();

            // Remove common punctuation and extra spaces
            normalized = Regex.Replace(normalized, @"[,\.]", "");
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            // Define abbreviation mappings (abbreviation -> full form)
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

            // Replace abbreviations with full forms
            foreach (var kvp in abbreviationMap)
            {
                // More robust pattern to ensure we only match actual entity type abbreviations
                var abbrPattern = new Regex($@"\b{Regex.Escape(kvp.Key)}\.?(?=\s|$|[,;])", RegexOptions.IgnoreCase);
                normalized = abbrPattern.Replace(normalized, kvp.Value);
            }

            return normalized;
        }

        // Helper function to check if two organization names match (accounting for abbreviations)
        private bool OrganizationNamesMatch(string name1, string name2)
        {
            if (string.IsNullOrWhiteSpace(name1) || string.IsNullOrWhiteSpace(name2)) return false;

            var normalized1 = NormalizeOrganizationName(name1);
            var normalized2 = NormalizeOrganizationName(name2);

            // Direct match after normalization
            if (normalized1 == normalized2) return true;

            // Check if one contains the other (for partial matches)
            if (normalized1.Contains(normalized2) || normalized2.Contains(normalized1))
            {
                // Extract entity types to ensure we're not matching different entity types
                var entity1 = GetEntityType(normalized1);
                var entity2 = GetEntityType(normalized2);

                // Allow match only if:
                // 1. Both have the same entity type, or
                // 2. One has no entity type (partial name), or  
                // 3. One is a more specific version of the other
                if (entity1 == entity2 || 
                    entity1 == null || 
                    entity2 == null ||
                    (entity1 != null && entity2 != null && (entity1.Contains(entity2) || entity2.Contains(entity1))))
                {
                    return true;
                }

                // Different entity types should not match
                return false;
            }

            // More restrictive core business name matching
            var core1 = RemoveEntitySuffixes(normalized1);
            var core2 = RemoveEntitySuffixes(normalized2);

            if (!string.IsNullOrWhiteSpace(core1) && !string.IsNullOrWhiteSpace(core2) && 
                core1.Length > 2 && core2.Length > 2 && core1 == core2)
            {
                var entity1 = GetEntityType(normalized1);
                var entity2 = GetEntityType(normalized2);

                // Only match core names if entity types are the same or compatible
                if (entity1 == entity2 || 
                    entity1 == null || 
                    entity2 == null ||
                    // Allow some compatible entity types
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

        private string RemoveEntitySuffixes(string name)
        {
            return Regex.Replace(name, @"\b(limited liability company|incorporated|corporation|company|limited|limited partnership|limited liability partnership|professional limited liability company|professional corporation|professional association|professional limited company)\b", "", RegexOptions.IgnoreCase).Trim();
        }

        public async Task<DocumentValidation> ValidateDocumentAsync(byte[] buffer, string documentType, Dictionary<string, string> formFields)
        {
            try
            {
                // Get configuration from config service
                var config = _configService.GetDocumentIntelligenceConfig();

                // Create the Document Intelligence Client
                var client = new DocumentAnalysisClient(new Uri(config.Endpoint), new AzureKeyCredential(config.Key));

                // Analyze the document - using prebuilt-document for more advanced structure analysis
                using var stream = new MemoryStream(buffer);
                var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-document", stream);
                var result = operation.Value;

                // Extract content and document elements
                var content = result.Content ?? string.Empty;
                var pages = result.Pages ?? new List<DocumentPage>();
                var languages = result.Languages ?? new List<DocumentLanguage>();
                var styles = result.Styles ?? new List<DocumentStyle>();
                var tables = result.Tables ?? new List<DocumentTable>();
                var keyValuePairs = result.KeyValuePairs ?? new List<DocumentKeyValuePair>();

                // Prepare lowercase content for analysis
                var contentLower = content.ToLower();

                // Detect document type from content
                var (detectedType, scores) = DetectDocumentTypeWithScoresAsync(content, contentLower, pages, keyValuePairs);

                // Use provided documentType if detection fails
                var finalDocumentType = detectedType != "unknown" ? detectedType : documentType;

                // Validate based on detected document type
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

                // Prepare document info
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
                    DetectedOrganizationName = validationResults.DetectedOrganizationName,
                    OriginalDocumentType = documentType != finalDocumentType ? documentType : null,
                    TypeDetection = new TypeDetection
                    {
                        TopScores = scores.Take(3).ToList(),
                        DetectionConfidence = scores.Any() ? (scores.First().Score / (double)scores.First().Threshold).ToString("F2") : "0"
                    }
                };

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

        private DocumentValidationResult ValidateDocumentByType(DocumentValidationOptions options)
        {
            var contentLower = options.Content.ToLower();

            // Handle auto-detect case
            if (options.DocumentType == "auto-detect")
            {
                return new DocumentValidationResult
                {
                    MissingElements = new List<string> { "Document type could not be automatically detected" },
                    SuggestedActions = new List<string> { "Try specifying a document type manually" }
                };
            }

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

        private DocumentValidationResult ValidateTaxClearanceOnline(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs, Dictionary<string, string> formFields)
        {
            var missingElements = new List<string>();
            var suggestedActions = new List<string>();
            string? detectedOrganizationName = null;

            // Looking for organization name before "BUSINESS ASSISTANCE OR INCENTIVE" line
            var lines = content.Split('\n');
            var businessAssistanceIndex = Array.FindIndex(lines, line =>
                line.Contains("BUSINESS ASSISTANCE OR INCENTIVE") ||
                line.Contains("CLEARANCE CERTIFICATE"));

            if (businessAssistanceIndex > 0)
            {
                // Look for org name in lines before the business assistance line (typically 1-4 lines above)
                for (int i = Math.Max(0, businessAssistanceIndex - 5); i < businessAssistanceIndex; i++)
                {
                    var line = lines[i].Trim();
                    // Skip empty lines, dates, or lines with less than 3 characters
                    if (!string.IsNullOrWhiteSpace(line) && line.Length > 3 && !Regex.IsMatch(line, @"^\d{1,2}\/\d{1,2}\/\d{4}$"))
                    {
                        // Skip lines that have typical headers or metadata
                        if (!line.ToLower().Contains("state of") &&
                            !line.ToLower().Contains("department of") &&
                            !line.ToLower().Contains("division of") &&
                            !line.ToLower().Contains("governor") &&
                            !Regex.IsMatch(line, @"^attn:", RegexOptions.IgnoreCase))
                        {
                            // Found a potential organization name line
                            detectedOrganizationName = line;
                            // If it's all caps, it's very likely the org name
                            if (line == line.ToUpper() && line.Length > 5)
                            {
                                break; // We're confident this is the org name
                            }
                        }
                    }
                }
            }

            // Fallback: If still no org name, try key-value pairs
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

            // Check for organization name match if provided
            if (formFields.ContainsKey("organizationName") && !string.IsNullOrEmpty(detectedOrganizationName))
            {
                if (!OrganizationNamesMatch(formFields["organizationName"], detectedOrganizationName))
                {
                    missingElements.Add("Organization name doesn't match the one on the certificate");
                    suggestedActions.Add("Verify that the correct organization name was entered");
                }
            }

            // Check for required keywords
            if (!contentLower.Contains("clearance certificate"))
            {
                missingElements.Add("Required keyword: 'Clearance Certificate'");
            }

            // Check for Serial#
            var hasSerial = contentLower.Contains("serial#") ||
                           contentLower.Contains("serial #") ||
                           contentLower.Contains("serial number") ||
                           Regex.IsMatch(content, @"serial[\s#]*:?\s*\d+", RegexOptions.IgnoreCase);

            if (!hasSerial)
            {
                missingElements.Add("Serial Number is missing");
                suggestedActions.Add("Verify this is an online-generated certificate with a Serial Number");
            }

            // Check for State of New Jersey
            if (!contentLower.Contains("state of new jersey") &&
                !contentLower.Contains("new jersey"))
            {
                missingElements.Add("Required keyword: 'State of New Jersey'");
            }

            // Check for Department of Treasury
            if (!contentLower.Contains("department of the treasury"))
            {
                missingElements.Add("Required keyword: Department of the Treasury");
            }

            // Check for Division of Taxation
            if (!contentLower.Contains("division of taxation"))
            {
                missingElements.Add("Required keyword: Division of Taxation");
            }

            // Check for Applicant ID or FEIN
            string? detectedId = null;

            // Look for Applicant ID patterns in content
            var applicantIdMatch = Regex.Match(content, @"applicant\s+id[#:]?\s*:?\s*(.*?)(?=\r|\n|$)", RegexOptions.IgnoreCase);
            if (applicantIdMatch.Success && !string.IsNullOrWhiteSpace(applicantIdMatch.Groups[1].Value))
            {
                detectedId = applicantIdMatch.Groups[1].Value.Trim();
            }

            // If not found yet, check key-value pairs
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

        private DocumentValidationResult ValidateTaxClearanceManual(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs, Dictionary<string, string> formFields)
        {
            var missingElements = new List<string>();
            var suggestedActions = new List<string>();
            string? detectedOrganizationName = null;

            // Looking for organization name before "BUSINESS ASSISTANCE OR INCENTIVE" line
            var lines = content.Split('\n');
            var businessAssistanceIndex = Array.FindIndex(lines, line =>
                line.Contains("BUSINESS ASSISTANCE OR INCENTIVE") ||
                line.Contains("CLEARANCE CERTIFICATE"));

            if (businessAssistanceIndex > 0)
            {
                // Look for org name in lines before the business assistance line (typically 1-4 lines above)
                for (int i = Math.Max(0, businessAssistanceIndex - 5); i < businessAssistanceIndex; i++)
                {
                    var line = lines[i].Trim();
                    // Skip empty lines, dates, or lines with less than 3 characters
                    if (!string.IsNullOrWhiteSpace(line) && line.Length > 3 && !Regex.IsMatch(line, @"^\d{1,2}\/\d{1,2}\/\d{4}$"))
                    {
                        // Skip lines that have typical headers or metadata
                        if (!line.ToLower().Contains("state of") &&
                            !line.ToLower().Contains("department of") &&
                            !line.ToLower().Contains("division of") &&
                            !line.ToLower().Contains("governor") &&
                            !Regex.IsMatch(line, @"^attn:", RegexOptions.IgnoreCase))
                        {
                            // Found a potential organization name line
                            detectedOrganizationName = line;
                            // If it's all caps, it's very likely the org name
                            if (line == line.ToUpper() && line.Length > 5)
                            {
                                break; // We're confident this is the org name
                            }
                        }
                    }
                }
            }

            // Fallback: If still no org name, try key-value pairs
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

            // Check for organization name match if provided
            if (formFields.ContainsKey("organizationName") && !string.IsNullOrEmpty(detectedOrganizationName))
            {
                if (!OrganizationNamesMatch(formFields["organizationName"], detectedOrganizationName))
                {
                    missingElements.Add("Organization name doesn't match the one on the certificate");
                    suggestedActions.Add("Verify that the correct organization name was entered");
                }
            }

            // Check for required keywords
            if (!contentLower.Contains("clearance certificate"))
            {
                missingElements.Add("Required keyword: 'Clearance Certificate'");
            }

            // Check for State of New Jersey
            if (!contentLower.Contains("state of new jersey"))
            {
                missingElements.Add("Required keyword: 'State of New Jersey'");
            }

            // Check for BATC Manual indication - this is a REQUIRED check for manual certificates
            if (!contentLower.Contains("batc") && !contentLower.Contains("manual"))
            {
                missingElements.Add("Required keyword: 'BATC - Manual'");
                suggestedActions.Add("Verify this is a manually generated tax clearance certificate");
            }

            // Check for Department of Treasury
            if (!contentLower.Contains("department of the treasury"))
            {
                missingElements.Add("Required keyword: Department of the Treasury");
            }

            // Check for Division of Taxation
            if (!contentLower.Contains("division of taxation"))
            {
                missingElements.Add("Required keyword: Division of Taxation");
            }

            // Check for Applicant ID or FEIN
            string? detectedId = null;

            // Look for Applicant ID patterns in content
            var applicantIdMatch = Regex.Match(content, @"applicant\s+id[#:]?\s*:?\s*(.*?)(?=\r|\n|$)", RegexOptions.IgnoreCase);
            if (applicantIdMatch.Success && !string.IsNullOrWhiteSpace(applicantIdMatch.Groups[1].Value))
            {
                detectedId = applicantIdMatch.Groups[1].Value.Trim();
            }

            // If not found yet, check key-value pairs
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

        private DocumentValidationResult ValidateCertificateAlternativeName(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs, Dictionary<string, string> formFields)
        {
            var missingElements = new List<string>();
            var suggestedActions = new List<string>();
            string? detectedOrganizationName = null;

            // Check for required elements and extract organization name
            var hasCertificateKeyword = contentLower.Contains("certificate of alternate name") || 
                                       contentLower.Contains("certificate of renewal of alternate name") || 
                                       contentLower.Contains("registration of alternate name");

            if (!hasCertificateKeyword)
            {
                missingElements.Add("Required keyword: 'Certificate of Alternate Name'");
            }
            else
            {
                // Find the organization name that appears after the certificate keywords
                int certIndex = -1;
                string certKeyword = "";

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
                    // Get the text after the certificate keyword
                    var textAfterCert = content.Substring(certIndex + certKeyword.Length);

                    // Split into lines and find the organization name
                    var lines = textAfterCert.Split('\n');
                    for (int i = 0; i < Math.Min(5, lines.Length); i++)
                    {
                        var line = lines[i].Trim();
                        // Skip empty lines, dates, or lines with less than 3 characters
                        if (!string.IsNullOrWhiteSpace(line) && line.Length > 3 && !Regex.IsMatch(line, @"^\d{1,2}[\/-]\d{1,2}[\/-]\d{2,4}$"))
                        {
                            // Skip lines that have typical headers or metadata
                            if (!line.ToLower().Contains("state of") &&
                                !line.ToLower().Contains("department of") &&
                                !line.ToLower().Contains("division of") &&
                                !line.ToLower().Contains("new jersey") &&
                                !line.ToLower().Contains("treasury") &&
                                !line.ToLower().Contains("revenue"))
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

            // Check for Division of Revenue in the top center
            var hasDivisionOfRevenue = contentLower.Contains("division of revenue");
            if (!hasDivisionOfRevenue)
            {
                missingElements.Add("Required keyword: 'Division of Revenue'");
                suggestedActions.Add("Verify document has been issued by the Division of Revenue");
            }

            // Check for date stamp by Dept. of Treasury
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

        private DocumentValidationResult ValidateCertificateOfTradeName(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs)
        {
            var missingElements = new List<string>();
            var suggestedActions = new List<string>();

            // Check for required elements
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

        private DocumentValidationResult ValidateCertificateOfFormation(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs, Dictionary<string, string> formFields)
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

            // Check for required elements
            if (!contentLower.Contains("certificate of formation"))
            {
                missingElements.Add("Required keyword: 'Certificate of Formation'");
            }

            // Check for NJ Department/Treasury references
            if (!contentLower.Contains("new jersey department of the treasury") &&
                !contentLower.Contains("new jersey") &&
                !contentLower.Contains("division of revenue"))
            {
                missingElements.Add("Certificate is not issued by the NJ Department of the Treasury");
                suggestedActions.Add("Verify certificate is issued by the NJ Department of the Treasury");
            }

            // Check for signature of state official
            var hasSignature = Regex.IsMatch(content, @"signature|signed|authorized representative", RegexOptions.IgnoreCase) ||
                              Regex.IsMatch(content, @"state treasurer|organizer|treasurer", RegexOptions.IgnoreCase);

            if (!hasSignature)
            {
                missingElements.Add("Signature of authorized state official is missing");
                suggestedActions.Add("Verify document has been signed by an authorized state official");
            }

            // Check for presence of any date
            var hasDate = CheckForDatePresence(content);
            if (!hasDate)
            {
                missingElements.Add("Document must contain a date");
                suggestedActions.Add("Verify that the document includes a stamped date");
            }

            // Check for verification info
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

            // Check for business purpose section
            var hasBusinessPurpose = contentLower.Contains("business purpose") &&
                                    (contentLower.Contains("purpose of the company") ||
                                     Regex.IsMatch(contentLower, @"purpose.*is", RegexOptions.IgnoreCase));

            if (!hasBusinessPurpose)
            {
                missingElements.Add("Business purpose section is missing");
                suggestedActions.Add("Verify the agreement defines a business purpose");
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
                              contentLower.Contains("trustees");

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
            var hasDivision = contentLower.Contains("division of taxation");

            if (!hasDivision)
            {
                missingElements.Add("Required keyword: 'Division of Taxation'");
                suggestedActions.Add("Verify the certificate is issued by the Division of Taxation");
            }

            // Detect organization name
            var authorizationLine = "this authorization is good only for the named person at the location specified herein this authorization is null and void if any change of ownership or address is effected.";
            var authorizationIndex = contentLower.IndexOf(authorizationLine);

            if (authorizationIndex == -1)
            {
                authorizationLine = "address.";
                authorizationIndex = contentLower.IndexOf(authorizationLine);
            }

            if (authorizationIndex != -1)
            {
                // Get the text after the authorization line
                var textAfterAuthorization = content.Substring(authorizationIndex + authorizationLine.Length);

                // Split into lines and find the organization name
                var lines = textAfterAuthorization.Split('\n');
                for (int i = 0; i < Math.Min(3, lines.Length); i++)
                {
                    var line = lines[i].Trim();
                    // Skip empty lines or lines with less than 3 characters
                    if (!string.IsNullOrWhiteSpace(line) && line.Length > 3 && !Regex.IsMatch(line, @"^\d{1,2}[\/-]\d{1,2}[\/-]\d{2,4}$"))
                    {
                        // Skip lines that have typical metadata
                        if (!line.ToLower().Contains("tax registration") &&
                            !line.ToLower().Contains("tax effective date") &&
                            !line.ToLower().Contains("document locator") &&
                            !line.ToLower().Contains("date issued"))
                        {
                            detectedOrganizationName = line;
                            break;
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

        private bool CheckDateWithinSixMonths(string content)
        {
            if (string.IsNullOrEmpty(content) || content.Length < 10) return false;

            var now = DateTime.Now;
            var sixMonthsAgo = now.AddMonths(-6);

            // Match numeric date formats
            var numericDateRegex = new Regex(@"(\d{1,2})[\/\-\.](\d{1,2})[\/\-\.](\d{4})", RegexOptions.IgnoreCase);
            var numericMatches = numericDateRegex.Matches(content).Take(10);

            foreach (Match match in numericMatches)
            {
                if (int.TryParse(match.Groups[1].Value, out int part1) &&
                    int.TryParse(match.Groups[2].Value, out int part2) &&
                    int.TryParse(match.Groups[3].Value, out int year))
                {
                    // Try MM/DD/YYYY
                    try
                    {
                        var dateMMDDYYYY = new DateTime(year, part1, part2);
                        if (dateMMDDYYYY >= sixMonthsAgo && dateMMDDYYYY <= now)
                            return true;
                    }
                    catch (ArgumentOutOfRangeException) { }

                    // Try DD/MM/YYYY
                    try
                    {
                        var dateDDMMYYYY = new DateTime(year, part2, part1);
                        if (dateDDMMYYYY >= sixMonthsAgo && dateDDMMYYYY <= now)
                            return true;
                    }
                    catch (ArgumentOutOfRangeException) { }
                }
            }

            // Match written date formats
            var monthNames = new[] { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };
            var monthPattern = string.Join("|", monthNames);
            var writtenDateRegex = new Regex($@"({monthPattern})\s+(\d{{1,2}})(?:st|nd|rd|th)?[,\s]*?(\d{{4}})|(\d{{1,2}})(?:st|nd|rd|th)?\s+({monthPattern})[,\s]*?(\d{{4}})", RegexOptions.IgnoreCase);
            var writtenMatches = writtenDateRegex.Matches(content).Take(10);

            foreach (Match match in writtenMatches)
            {
                int month, day, year;

                if (!string.IsNullOrEmpty(match.Groups[1].Value))
                {
                    // Format: "January 15, 2023"
                    month = Array.IndexOf(monthNames, match.Groups[1].Value.ToLower()) + 1;
                    day = int.Parse(match.Groups[2].Value);
                    year = int.Parse(match.Groups[3].Value);
                }
                else
                {
                    // Format: "15 January 2023"
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

        private bool CheckForDatePresence(string content)
        {
            if (string.IsNullOrEmpty(content) || content.Length < 10) return false;

            // Check for numeric dates
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
                        if (year >= 1900 && year <= 2100 &&
                            num1 >= 1 && num1 <= 31 &&
                            num2 >= 1 && num2 <= 31)
                        {
                            return true;
                        }
                    }
                }
            }

            // Check for written dates
            var monthNames = new[] { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };
            var monthPattern = string.Join("|", monthNames);
            var writtenDateRegex = new Regex($@"({monthPattern})\s+(\d{{1,2}})(?:st|nd|rd|th)?[,\s]*?(\d{{4}})|(\d{{1,2}})(?:st|nd|rd|th)?\s+({monthPattern})[,\s]*?(\d{{4}})", RegexOptions.IgnoreCase);

            if (writtenDateRegex.IsMatch(content))
            {
                return true;
            }

            // Match ordinal date formats like "13th day of May, 2023"
            var ordinalDateRegex = new Regex(@"(\d{1,2})(st|nd|rd|th)?\s+day\s+of\s+(\w+)[,\s]*(\d{4})", RegexOptions.IgnoreCase);
            var ordinalMatches = ordinalDateRegex.Matches(content);

            if (ordinalMatches.Count > 0)
            {
                return true;
            }

            // Match year-only formats like "2023" or "©2023" (but be more specific to avoid false positives)
            var yearOnlyRegex = new Regex(@"(?:©\s*|copyright\s*|adopted\s*|effective\s*|revised\s*|amended\s*|dated\s*|year\s*)(\d{4})", RegexOptions.IgnoreCase);
            var yearMatches = yearOnlyRegex.Matches(content);

            if (yearMatches.Count > 0)
            {
                return true;
            }

            return false;
        }

        private (string detectedType, List<DocumentTypeScore> scores) DetectDocumentTypeWithScoresAsync(string content, string contentLower, IReadOnlyList<DocumentPage> pages, IReadOnlyList<DocumentKeyValuePair> keyValuePairs)
        {
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

            var scores = documentTypePatterns.Select(docType => new DocumentTypeScore
            {
                Type = docType.Type,
                Score = CalculateScore(docType, contentLower),
                Threshold = docType.Threshold
            }).OrderByDescending(s => s.Score).ToList();

            var bestMatch = scores.FirstOrDefault(score => score.Score >= score.Threshold);
            var detectedType = bestMatch?.Type ?? "unknown";

            // Special case for distinguishing between tax clearance types if we detected it as a general tax clearance
            if (detectedType == "unknown" && contentLower.Contains("clearance certificate") && contentLower.Contains("division of taxation"))
            {
                // Check for online-specific indicators
                var manualIndicators = new[] { "manual", "batc" };
                var isManual = manualIndicators.Any(indicator => contentLower.Contains(indicator)) || pages.Count <= 2;

                detectedType = isManual ? "tax-clearance-manual" : "tax-clearance-online";
            }

            return (detectedType, scores);
        }

        private int CalculateScore(DocumentTypePattern docType, string contentLower)
        {
            return docType.Keywords.Where(keyword => contentLower.Contains(keyword.Text.ToLower())).Sum(keyword => keyword.Weight);
        }
    }

    // Supporting classes
    public class DocumentValidationOptions
    {
        public string DocumentType { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public IReadOnlyList<DocumentPage> Pages { get; set; } = new List<DocumentPage>();
        public IReadOnlyList<DocumentLanguage> Languages { get; set; } = new List<DocumentLanguage>();
        public IReadOnlyList<DocumentStyle> Styles { get; set; } = new List<DocumentStyle>();
        public IReadOnlyList<DocumentTable> Tables { get; set; } = new List<DocumentTable>();
        public IReadOnlyList<DocumentKeyValuePair> KeyValuePairs { get; set; } = new List<DocumentKeyValuePair>();
        public Dictionary<string, string> FormFields { get; set; } = new Dictionary<string, string>();
    }

    public class DocumentValidationResult
    {
        public List<string> MissingElements { get; set; } = new List<string>();
        public List<string> SuggestedActions { get; set; } = new List<string>();
        public string? DetectedOrganizationName { get; set; }
    }

    public class DocumentValidation
    {
        public bool Success { get; set; }
        public List<string> MissingElements { get; set; } = new List<string>();
        public List<string> SuggestedActions { get; set; } = new List<string>();
        public DocumentInfo DocumentInfo { get; set; } = new DocumentInfo();
    }

    public class DocumentTypePattern
    {
        public string Type { get; set; } = string.Empty;
        public List<KeywordWeight> Keywords { get; set; } = new List<KeywordWeight>();
        public int Threshold { get; set; }
    }

    public class KeywordWeight
    {
        public string Text { get; set; } = string.Empty;
        public int Weight { get; set; }
    }
} 