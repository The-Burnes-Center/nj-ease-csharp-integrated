using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using DocumentValidator.Models;
using System.Text;
using System.Linq;

namespace DocumentValidator.Services
{
    /// <summary>
    /// Service for generating professional PDF reports from document validation results.
    /// Creates two types of reports: consolidated reports for multiple documents and 
    /// individual document validation summaries.
    /// 
    /// Features:
    /// - Modern flat design with semantic color coding
    /// - Responsive text wrapping and dynamic row sizing
    /// - Professional table layouts with status indicators
    /// - Detailed validation breakdowns with corrective actions
    /// - Support for uncategorized documents requiring manual review
    /// </summary>
    public class PdfGeneratorService
    {
        // Color palette for professional document presentation
        // Primary colors for headers and branding
        private readonly XColor PrimaryBlue = XColor.FromArgb(37, 99, 235);
        private readonly XColor PrimaryDark = XColor.FromArgb(30, 64, 175);
        private readonly XColor SecondaryBlue = XColor.FromArgb(59, 130, 246);

        // Background and layout colors
        private readonly XColor AccentGray = XColor.FromArgb(249, 250, 251);
        private readonly XColor BackgroundGray = XColor.FromArgb(243, 244, 246);
        private readonly XColor BorderGray = XColor.FromArgb(229, 231, 235);

        // Status colors for validation results
        private readonly XColor SuccessGreen = XColor.FromArgb(16, 185, 129);
        private readonly XColor SuccessLight = XColor.FromArgb(209, 250, 229);
        private readonly XColor ErrorRed = XColor.FromArgb(239, 68, 68);
        private readonly XColor ErrorLight = XColor.FromArgb(254, 226, 226);
        private readonly XColor WarningOrange = XColor.FromArgb(245, 158, 11);
        private readonly XColor WarningLight = XColor.FromArgb(254, 243, 199);

        // Text colors for content hierarchy
        private readonly XColor TextPrimary = XColor.FromArgb(17, 24, 39);
        private readonly XColor TextSecondary = XColor.FromArgb(75, 85, 99);
        private readonly XColor TextMuted = XColor.FromArgb(156, 163, 175);

        /// <summary>
        /// Generates a consolidated PDF report containing validation results for multiple documents.
        /// Includes an executive summary table and detailed pages for each document.
        /// 
        /// Report structure:
        /// 1. Summary page with organization header and validation table
        /// 2. Individual detail pages for each validated document
        /// 3. Uncategorized documents page (if any exist)
        /// 
        /// The table uses dynamic row sizing to accommodate varying document name lengths
        /// and includes color-coded status badges for immediate result recognition.
        /// </summary>
        /// <param name="validationResults">List of validation results from processed documents</param>
        /// <param name="organizationName">Organization name for report header</param>
        /// <param name="skippedDocuments">Documents that couldn't be automatically processed</param>
        /// <returns>PDF report as byte array</returns>
        /// <exception cref="InvalidOperationException">Thrown when no valid results provided</exception>
        public async Task<byte[]> GenerateConsolidatedReportAsync(List<ValidationResult> validationResults, string organizationName, List<SkippedDocument> skippedDocuments)
        {
            return await Task.Run(() =>
            {
                // Filter to only include valid results with known document types
                var validResults = validationResults.Where(result =>
                    result != null && !string.IsNullOrEmpty(result.FileName) &&
                    !string.IsNullOrEmpty(result.DocumentType) && result.DocumentInfo != null &&
                    result.DocumentType != "unknown").ToList();

                if (validResults.Count == 0 && skippedDocuments.Count == 0)
                {
                    throw new InvalidOperationException("No valid document validation results to generate report");
                }

                // Initialize PDF document and graphics
                var document = new PdfDocument();
                var page = document.AddPage();
                var gfx = XGraphics.FromPdfPage(page);
                
                // Define font hierarchy
                var font = new XFont("Segoe UI", 11);
                var fontBold = new XFont("Segoe UI", 11, XFontStyle.Bold);
                var fontLarge = new XFont("Segoe UI", 24, XFontStyle.Bold);
                var fontMedium = new XFont("Segoe UI", 16, XFontStyle.Bold);
                var fontSmall = new XFont("Segoe UI", 9);
                var fontTiny = new XFont("Segoe UI", 8);

                var yPosition = 50;

                // Create header section with blue background
                var headerRect = new XRect(0, 0, page.Width, 160);
                gfx.DrawRectangle(new XSolidBrush(PrimaryBlue), headerRect);

                // Add drop shadow
                var shadowRect = new XRect(0, 160, page.Width, 4);
                gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(40, 0, 0, 0)), shadowRect);

                // Draw header text
                gfx.DrawString("NJ EASE Document Validation Report", fontLarge, XBrushes.White,
                    new XRect(0, yPosition, page.Width, 40), XStringFormats.TopCenter);
                yPosition += 50;

                gfx.DrawString($"Organization: {organizationName}", new XFont("Segoe UI", 14, XFontStyle.Regular), 
                    new XSolidBrush(XColor.FromArgb(240, 245, 251)),
                    new XRect(0, yPosition, page.Width, 25), XStringFormats.TopCenter);
                yPosition += 30;

                gfx.DrawString($"Generated: {DateTime.Now:MMMM dd, yyyy 'at' hh:mm tt}", 
                    new XFont("Segoe UI", 10), new XSolidBrush(XColor.FromArgb(219, 234, 254)),
                    new XRect(0, yPosition, page.Width, 15), XStringFormats.TopCenter);
                yPosition += 50;

                // Create summary card
                var summaryCardRect = new XRect(50, yPosition, page.Width - 100, 45);
                DrawRoundedRectangle(gfx, summaryCardRect, 8, new XSolidBrush(AccentGray), new XPen(BorderGray, 1));
                
                var cardShadow = new XRect(52, yPosition + 2, page.Width - 100, 45);
                DrawRoundedRectangle(gfx, cardShadow, 8, new XSolidBrush(XColor.FromArgb(20, 0, 0, 0)), null);

                gfx.DrawString("Validation Summary", new XFont("Segoe UI", 15, XFontStyle.Bold), 
                    new XSolidBrush(PrimaryDark),
                    new XRect(65, yPosition + 12, page.Width - 130, 25), XStringFormats.TopLeft);
                yPosition += 65;

                // Set up table layout with responsive column widths
                var pageWidth = page.Width - 100;
                var startX = 50;

                var columnWidths = new double[]
                {
                    0.38 * pageWidth, // Document Name
                    0.36 * pageWidth, // Document Type  
                    0.14 * pageWidth, // Status
                    0.12 * pageWidth  // Issues
                };

                var headers = new[] { "Document Name", "Document Type", "Status", "Issues" };

                // Draw table header
                var headerTableRect = new XRect(startX, yPosition, pageWidth, 35);
                DrawRoundedRectangle(gfx, headerTableRect, 6, new XSolidBrush(PrimaryBlue), null);

                var x = startX;
                for (int i = 0; i < headers.Length; i++)
                {
                    // Add column separators
                    if (i > 0)
                    {
                        gfx.DrawLine(new XPen(XColor.FromArgb(80, 255, 255, 255), 1), 
                            x, yPosition + 8, x, yPosition + 27);
                    }
                    
                    gfx.DrawString(headers[i], new XFont("Segoe UI", 11, XFontStyle.Bold), XBrushes.White,
                        new XRect(x + 12, yPosition + 10, columnWidths[i] - 24, 20), XStringFormats.TopLeft);
                    x += (int)columnWidths[i];
                }
                yPosition += 35;

                // Draw table rows with dynamic height based on content
                for (int index = 0; index < validResults.Count; index++)
                {
                    var result = validResults[index];
                    var documentTypeName = FormatDocumentType(result.DocumentType);
                    
                    // Calculate row height based on text wrapping requirements
                    var nameLines = WrapTextToWidth(gfx, result.FileName, font, columnWidths[0] - 24);
                    var typeLines = WrapTextToWidth(gfx, documentTypeName, font, columnWidths[1] - 24);
                    var maxLines = Math.Max(nameLines.Count, typeLines.Count);
                    
                    var rowHeight = Math.Max(50, (int)(maxLines * font.Height + 20));

                    // Alternate row colors
                    var rowColor = index % 2 == 0 ? XColor.FromArgb(252, 253, 254) : XColors.White;
                    
                    var rowRect = new XRect(startX, yPosition, pageWidth, rowHeight);
                    DrawRoundedRectangle(gfx, rowRect, 4, new XSolidBrush(rowColor), new XPen(BorderGray, 0.5));

                    x = startX;

                    // Document Name column
                    DrawWrappedTextToFit(gfx, result.FileName, font, new XSolidBrush(TextPrimary),
                        new XRect(x + 12, yPosition + 10, columnWidths[0] - 24, rowHeight - 20));
                    
                    gfx.DrawLine(new XPen(BorderGray, 0.5), 
                        x + (int)columnWidths[0], yPosition + 5, x + (int)columnWidths[0], yPosition + rowHeight - 5);
                    x += (int)columnWidths[0];

                    // Document Type column
                    DrawWrappedTextToFit(gfx, documentTypeName, font, new XSolidBrush(TextPrimary),
                        new XRect(x + 12, yPosition + 10, columnWidths[1] - 24, rowHeight - 20));
                    
                    gfx.DrawLine(new XPen(BorderGray, 0.5), 
                        x + (int)columnWidths[1], yPosition + 5, x + (int)columnWidths[1], yPosition + rowHeight - 5);
                    x += (int)columnWidths[1];

                    // Status column with colored badge
                    var statusColor = result.Success ? SuccessGreen : ErrorRed;
                    var statusBgColor = result.Success ? SuccessLight : ErrorLight;
                    var statusText = result.Success ? "PASSED" : "FAILED";
                    
                    var statusBadgeRect = new XRect(x + 8, yPosition + 10, columnWidths[2] - 16, 22);
                    DrawRoundedRectangle(gfx, statusBadgeRect, 4, new XSolidBrush(statusBgColor), new XPen(statusColor, 1));
                    
                    gfx.DrawString(statusText, new XFont("Segoe UI", 9, XFontStyle.Bold), new XSolidBrush(statusColor),
                        statusBadgeRect, XStringFormats.Center);
                    
                    gfx.DrawLine(new XPen(BorderGray, 0.5), 
                        x + (int)columnWidths[2], yPosition + 5, x + (int)columnWidths[2], yPosition + rowHeight - 5);
                    x += (int)columnWidths[2];

                    // Issues column
                    var issuesText = result.MissingElements.Count > 0
                        ? $"{result.MissingElements.Count}"
                        : "None";
                    var issuesColor = result.MissingElements.Count > 0 ? new XSolidBrush(ErrorRed) : new XSolidBrush(SuccessGreen);
                    
                    gfx.DrawString(issuesText, new XFont("Segoe UI", 9, XFontStyle.Bold), issuesColor,
                        new XRect(x + 12, yPosition + 10, columnWidths[3] - 24, rowHeight - 20), XStringFormats.TopLeft);

                    yPosition += rowHeight;

                    // Check for page break
                    if (yPosition > page.Height - 120)
                    {
                        page = document.AddPage();
                        gfx = XGraphics.FromPdfPage(page);
                        yPosition = 50;
                    }
                }

                // Add information section
                yPosition += 30;
                var infoRect = new XRect(50, yPosition, page.Width - 100, 45);
                DrawRoundedRectangle(gfx, infoRect, 8, new XSolidBrush(WarningLight), new XPen(WarningOrange, 1));

                var infoText = "Detailed validation results for each document are provided on the following pages.";
                gfx.DrawString(infoText, new XFont("Segoe UI", 12, XFontStyle.Regular), 
                    new XSolidBrush(XColor.FromArgb(146, 64, 14)),
                    new XRect(65, yPosition + 14, page.Width - 130, 25), XStringFormats.TopLeft);

                // Add detailed pages for each document
                foreach (var result in validResults)
                {
                    page = document.AddPage();
                    gfx = XGraphics.FromPdfPage(page);
                    AddDetailedValidationPage(gfx, page, result, organizationName);
                }

                // Add skipped documents page if needed
                if (skippedDocuments.Count > 0)
                {
                    page = document.AddPage();
                    gfx = XGraphics.FromPdfPage(page);
                    AddSkippedDocumentsPage(gfx, page, skippedDocuments, organizationName);
                }

                // Convert to byte array
                using var stream = new MemoryStream();
                document.Save(stream);
                return stream.ToArray();
            });
        }

        /// <summary>
        /// Generates a single-page PDF report for an individual document validation result.
        /// Contains document details, validation status, statistics, and any issues found.
        /// </summary>
        /// <param name="validationResult">Validation result data for the document</param>
        /// <param name="documentName">Name of the document being validated</param>
        /// <param name="organizationName">Organization name for report context</param>
        /// <returns>PDF report as byte array</returns>
        public async Task<byte[]> GenerateValidationSummaryAsync(ValidationResult validationResult, string documentName, string organizationName)
        {
            return await Task.Run(() =>
            {
                var document = new PdfDocument();
                var page = document.AddPage();
                var gfx = XGraphics.FromPdfPage(page);

                AddDetailedValidationPage(gfx, page, validationResult, organizationName);

                using var stream = new MemoryStream();
                document.Save(stream);
                return stream.ToArray();
            });
        }

        /// <summary>
        /// Creates a detailed validation page for a single document.
        /// Includes document header, details section, validation results, statistics,
        /// and issues/actions sections as appropriate.
        /// </summary>
        /// <param name="gfx">Graphics context for drawing</param>
        /// <param name="page">PDF page to draw on</param>
        /// <param name="result">Validation result containing document data</param>
        /// <param name="organizationName">Organization name for context</param>
        private void AddDetailedValidationPage(XGraphics gfx, PdfPage page, ValidationResult result, string organizationName)
        {
            if (result?.DocumentInfo == null) return;

            var font = new XFont("Segoe UI", 11);
            var fontBold = new XFont("Segoe UI", 12, XFontStyle.Bold);
            var fontLarge = new XFont("Segoe UI", 20, XFontStyle.Bold);
            var fontMedium = new XFont("Segoe UI", 15, XFontStyle.Bold);

            var yPosition = 40;

            // Create header with document type as title
            var headerRect = new XRect(0, 0, page.Width, 80);
            gfx.DrawRectangle(new XSolidBrush(PrimaryBlue), headerRect);

            var shadowRect = new XRect(0, 80, page.Width, 6);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(40, 0, 0, 0)), shadowRect);

            var documentTypeTitle = FormatDocumentType(result.DocumentType);
            gfx.DrawString(documentTypeTitle, fontLarge, XBrushes.White,
                new XRect(0, yPosition, page.Width, 35), XStringFormats.TopCenter);
            yPosition += 60;

            var sectionHeaderStyle = new XFont("Segoe UI", 14, XFontStyle.Bold);
            var sectionBorderColor = new XPen(BorderGray, 1);

            // Document Details section
            var detailsRect = new XRect(50, yPosition, page.Width - 100, 30);
            DrawRoundedRectangle(gfx, detailsRect, 6, new XSolidBrush(AccentGray), sectionBorderColor);
            
            gfx.DrawString("Document Details", sectionHeaderStyle, new XSolidBrush(PrimaryDark),
                new XRect(65, yPosition + 6, page.Width - 130, 25), XStringFormats.TopLeft);
            yPosition += 40;

            // Document information
            var infoItems = new[]
            {
                $"Document Name: {result.FileName}",
                $"Organization Name: {organizationName}",
                $"Document Type: {FormatDocumentType(result.DocumentType)}"
            };

            foreach (var item in infoItems)
            {
                var itemLines = WrapText(item, 80);
                var itemHeight = itemLines.Count * font.Height;
                
                DrawWrappedText(gfx, item, font, new XSolidBrush(TextPrimary),
                    new XRect(65, yPosition, page.Width - 130, itemHeight), 80);
                yPosition += (int)itemHeight + 5;
            }
            yPosition += 20;

            // Validation Result section
            var summaryRect = new XRect(50, yPosition, page.Width - 100, 30);
            DrawRoundedRectangle(gfx, summaryRect, 6, new XSolidBrush(AccentGray), sectionBorderColor);
            
            gfx.DrawString("Validation Result", sectionHeaderStyle, new XSolidBrush(PrimaryDark),
                new XRect(65, yPosition + 6, page.Width - 130, 25), XStringFormats.TopLeft);
            yPosition += 40;

            // Display result with colored badge
            var resultColor = result.Success ? SuccessGreen : ErrorRed;
            var resultBgColor = result.Success ? SuccessLight : ErrorLight;
            var resultText = result.Success ? "Passed" : "Failed";
            
            var resultBadgeRect = new XRect(65, yPosition, 160, 35);
            DrawRoundedRectangle(gfx, resultBadgeRect, 6, new XSolidBrush(resultBgColor), new XPen(resultColor, 1));
            
            gfx.DrawString(resultText, new XFont("Segoe UI", 14, XFontStyle.Bold), new XSolidBrush(resultColor),
                resultBadgeRect, XStringFormats.Center);
            yPosition += 50;

            // Document Statistics section
            var statsRect = new XRect(50, yPosition, page.Width - 100, 30);
            DrawRoundedRectangle(gfx, statsRect, 6, new XSolidBrush(AccentGray), sectionBorderColor);
            
            gfx.DrawString("Document Statistics", sectionHeaderStyle, new XSolidBrush(PrimaryDark),
                new XRect(65, yPosition + 6, page.Width - 130, 25), XStringFormats.TopLeft);
            yPosition += 40;

            // Display statistics
            var statItems = new[]
            {
                $"Page Count: {result.DocumentInfo.PageCount}",
                $"Word Count: {result.DocumentInfo.WordCount:N0}",
                $"Contains Handwriting: {(result.DocumentInfo.ContainsHandwriting ? "Yes" : "No")}"
            };

            foreach (var item in statItems)
            {
                var itemLines = WrapText(item, 80);
                var itemHeight = itemLines.Count * font.Height;
                
                DrawWrappedText(gfx, item, font, new XSolidBrush(TextSecondary),
                    new XRect(65, yPosition, page.Width - 130, itemHeight), 80);
                yPosition += (int)itemHeight + 5;
            }

            // Add detected organization if available
            if (!string.IsNullOrEmpty(result.DocumentInfo.DetectedOrganizationName))
            {
                var orgText = $"Detected Organization: {result.DocumentInfo.DetectedOrganizationName}";
                var orgLines = WrapText(orgText, 80);
                var orgHeight = orgLines.Count * font.Height;
                
                DrawWrappedText(gfx, orgText, font, new XSolidBrush(TextSecondary),
                    new XRect(65, yPosition, page.Width - 130, orgHeight), 80);
                yPosition += (int)orgHeight + 5;
            }

            yPosition += 25;

            // Issues and Success sections
            if (!result.Success)
            {
                // Issues section
                var issuesRect = new XRect(50, yPosition, page.Width - 100, 30);
                DrawRoundedRectangle(gfx, issuesRect, 6, new XSolidBrush(ErrorLight), new XPen(ErrorRed, 1));
                
                gfx.DrawString("Issues Detected", sectionHeaderStyle, new XSolidBrush(ErrorRed),
                    new XRect(65, yPosition + 6, page.Width - 130, 25), XStringFormats.TopLeft);
                yPosition += 40;

                // List issues
                for (int i = 0; i < result.MissingElements.Count; i++)
                {
                    var issueText = $"• {result.MissingElements[i]}";
                    var issueLines = WrapText(issueText, 75);
                    var issueHeight = issueLines.Count * font.Height;
                    
                    DrawWrappedText(gfx, issueText, font, new XSolidBrush(TextPrimary),
                        new XRect(75, yPosition, page.Width - 150, issueHeight), 75);
                    yPosition += (int)issueHeight + 5;
                }

                yPosition += 20;

                // Suggested Actions if available
                if (result.SuggestedActions.Any())
                {
                    var actionsRect = new XRect(50, yPosition, page.Width - 100, 30);
                    DrawRoundedRectangle(gfx, actionsRect, 6, new XSolidBrush(WarningLight), new XPen(WarningOrange, 1));
                    
                    gfx.DrawString("Suggested Actions", sectionHeaderStyle, new XSolidBrush(XColor.FromArgb(146, 64, 14)),
                        new XRect(65, yPosition + 6, page.Width - 130, 25), XStringFormats.TopLeft);
                    yPosition += 40;

                    for (int i = 0; i < result.SuggestedActions.Count; i++)
                    {
                        var actionText = $"• {result.SuggestedActions[i]}";
                        var actionLines = WrapText(actionText, 75);
                        var actionHeight = actionLines.Count * font.Height;
                        
                        DrawWrappedText(gfx, actionText, font, new XSolidBrush(TextPrimary),
                            new XRect(75, yPosition, page.Width - 150, actionHeight), 75);
                        yPosition += (int)actionHeight + 5;
                    }
                }
            }
            else
            {
                // Success section
                var successRect = new XRect(50, yPosition, page.Width - 100, 50);
                DrawRoundedRectangle(gfx, successRect, 6, new XSolidBrush(SuccessLight), new XPen(SuccessGreen, 1));
                
                var successText = "Document validation completed successfully with no issues!";
                DrawWrappedText(gfx, successText, new XFont("Segoe UI", 13, XFontStyle.Bold), new XSolidBrush(SuccessGreen),
                    new XRect(65, yPosition + 16, page.Width - 130, 30), 65);
                yPosition += 60;
            }

            // Footer
            var footerRect = new XRect(0, page.Height - 50, page.Width, 50);
            gfx.DrawRectangle(new XSolidBrush(BackgroundGray), footerRect);
            gfx.DrawLine(new XPen(BorderGray, 1), 0, page.Height - 50, page.Width, page.Height - 50);
            
            var footerText = $"This report was automatically generated • © {DateTime.Now.Year} Document Validator";
            gfx.DrawString(footerText, new XFont("Segoe UI", 9), new XSolidBrush(TextMuted),
                new XRect(0, page.Height - 30, page.Width, 20), XStringFormats.TopCenter);
        }

        /// <summary>
        /// Creates a page showing documents that couldn't be automatically categorized.
        /// Includes explanation, table of documents with reasons, and next steps guidance.
        /// </summary>
        /// <param name="gfx">Graphics context for drawing</param>
        /// <param name="page">PDF page to draw on</param>
        /// <param name="skippedDocuments">List of documents requiring manual review</param>
        /// <param name="organizationName">Organization name for context</param>
        private void AddSkippedDocumentsPage(XGraphics gfx, PdfPage page, List<SkippedDocument> skippedDocuments, string organizationName)
        {
            var font = new XFont("Segoe UI", 11);
            var fontBold = new XFont("Segoe UI", 12, XFontStyle.Bold);
            var fontLarge = new XFont("Segoe UI", 20, XFontStyle.Bold);
            var fontMedium = new XFont("Segoe UI", 15, XFontStyle.Bold);

            var yPosition = 40;

            // Header with warning theme
            var headerRect = new XRect(0, 0, page.Width, 80);
            gfx.DrawRectangle(new XSolidBrush(WarningOrange), headerRect);

            var shadowRect = new XRect(0, 80, page.Width, 6);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(40, 0, 0, 0)), shadowRect);

            gfx.DrawString("Uncategorized Documents", fontLarge, XBrushes.White,
                new XRect(0, yPosition, page.Width, 35), XStringFormats.TopCenter);
            yPosition += 60;

            // Explanation section
            var explanationRect = new XRect(50, yPosition, page.Width - 100, 60);
            DrawRoundedRectangle(gfx, explanationRect, 8, new XSolidBrush(WarningLight), new XPen(WarningOrange, 1));

            var explanationText = "The following documents could not be automatically classified. These documents require manual review to determine their type and verify compliance.";
            DrawWrappedText(gfx, explanationText, font, new XSolidBrush(XColor.FromArgb(146, 64, 14)),
                new XRect(65, yPosition + 15, page.Width - 130, 45), 85);
            yPosition += 80;

            // Table setup
            var pageWidth = page.Width - 100;
            var startX = 50;

            var columnWidths = new double[]
            {
                0.70 * pageWidth, // Document Name
                0.30 * pageWidth  // Reason Skipped
            };

            var headers = new[] { "Document Name", "Reason Skipped" };

            // Table header
            var headerTableRect = new XRect(startX, yPosition, pageWidth, 35);
            DrawRoundedRectangle(gfx, headerTableRect, 6, new XSolidBrush(WarningOrange), null);

            var x = startX;
            for (int i = 0; i < headers.Length; i++)
            {
                if (i > 0)
                {
                    gfx.DrawLine(new XPen(XColor.FromArgb(80, 255, 255, 255), 1), 
                        x, yPosition + 8, x, yPosition + 27);
                }
                
                gfx.DrawString(headers[i], new XFont("Segoe UI", 12, XFontStyle.Bold), XBrushes.White,
                    new XRect(x + 12, yPosition + 10, columnWidths[i] - 24, 20), XStringFormats.TopLeft);
                x += (int)columnWidths[i];
            }
            yPosition += 35;

            // Table rows
            for (int index = 0; index < skippedDocuments.Count; index++)
            {
                var document = skippedDocuments[index];
                
                // Calculate row height based on content
                var nameLines = WrapTextToWidth(gfx, document.FileName, font, columnWidths[0] - 24);
                var reasonLines = WrapTextToWidth(gfx, document.Reason, font, columnWidths[1] - 24);
                var maxLines = Math.Max(nameLines.Count, reasonLines.Count);
                
                var rowHeight = Math.Max(50, (int)(maxLines * font.Height + 20));

                // Alternate row colors
                var rowColor = index % 2 == 0 ? XColor.FromArgb(254, 252, 246) : XColors.White;
                
                var rowRect = new XRect(startX, yPosition, pageWidth, rowHeight);
                DrawRoundedRectangle(gfx, rowRect, 4, new XSolidBrush(rowColor), new XPen(BorderGray, 0.5));

                x = startX;

                // Document Name column
                DrawWrappedTextToFit(gfx, document.FileName, font, new XSolidBrush(TextPrimary),
                    new XRect(x + 12, yPosition + 10, columnWidths[0] - 24, rowHeight - 20));
                
                gfx.DrawLine(new XPen(BorderGray, 0.5), 
                    x + (int)columnWidths[0], yPosition + 5, x + (int)columnWidths[0], yPosition + rowHeight - 5);
                x += (int)columnWidths[0];

                // Reason column
                DrawWrappedTextToFit(gfx, document.Reason, font, new XSolidBrush(XColor.FromArgb(146, 64, 14)),
                    new XRect(x + 12, yPosition + 10, columnWidths[1] - 24, rowHeight - 20));

                yPosition += rowHeight;

                // Page break check
                if (yPosition > page.Height - 120)
                {
                    break;
                }
            }

            // Note section
            yPosition += 30;
            var noteRect = new XRect(50, yPosition, page.Width - 100, 60);
            DrawRoundedRectangle(gfx, noteRect, 8, new XSolidBrush(XColor.FromArgb(254, 240, 138)), new XPen(WarningOrange, 1));

            var noteText = "Note: These documents should be manually reviewed to determine their document type and verify compliance requirements.";
            DrawWrappedText(gfx, noteText, new XFont("Segoe UI", 12, XFontStyle.Bold), 
                new XSolidBrush(XColor.FromArgb(146, 64, 14)),
                new XRect(65, yPosition + 16, page.Width - 130, 40), 75);

            // Footer
            var footerRect = new XRect(0, page.Height - 50, page.Width, 50);
            gfx.DrawRectangle(new XSolidBrush(BackgroundGray), footerRect);
            gfx.DrawLine(new XPen(BorderGray, 1), 0, page.Height - 50, page.Width, page.Height - 50);
            
            var footerText = $"This report was automatically generated • © {DateTime.Now.Year} Document Validator";
            gfx.DrawString(footerText, new XFont("Segoe UI", 9), new XSolidBrush(TextMuted),
                new XRect(0, page.Height - 30, page.Width, 20), XStringFormats.TopCenter);
        }

        /// <summary>
        /// Converts internal document type codes to user-friendly display names.
        /// Maps technical identifiers like "tax-clearance-online" to readable names
        /// like "Tax Clearance Certificate (Online)".
        /// </summary>
        /// <param name="documentType">Internal document type identifier</param>
        /// <returns>Formatted display name for the document type</returns>
        private string FormatDocumentType(string documentType)
        {
            var documentTypeMap = new Dictionary<string, string>
            {
                { "tax-clearance-online", "Tax Clearance Certificate (Online)" },
                { "tax-clearance-manual", "Tax Clearance Certificate (Manual)" },
                { "cert-alternative-name", "Certificate of Alternate Name" },
                { "cert-trade-name", "Certificate of Trade Name" },
                { "cert-formation", "Certificate of Formation" },
                { "cert-formation-independent", "Certificate of Formation (Independent)" },
                { "cert-good-standing-long", "Certificate of Good Standing (Long)" },
                { "cert-good-standing-short", "Certificate of Good Standing (Short)" },
                { "operating-agreement", "Operating Agreement" },
                { "cert-incorporation", "Certificate of Incorporation" },
                { "irs-determination", "IRS Determination Letter" },
                { "bylaws", "Corporate Bylaws" },
                { "cert-authority", "Certificate of Authority" },
                { "cert-authority-auto", "Certificate of Authority (Automatic)" },
                { "unknown", "Unknown Document Type" }
            };

            return documentTypeMap.TryGetValue(documentType, out var formatted) ? formatted : documentType;
        }

        /// <summary>
        /// Wraps text at word boundaries to fit within specified character limits.
        /// Splits text into lines that don't exceed maxCharsPerLine, breaking at spaces
        /// to maintain word integrity.
        /// </summary>
        /// <param name="text">Text to wrap</param>
        /// <param name="maxCharsPerLine">Maximum characters allowed per line</param>
        /// <returns>List of text lines formatted for display</returns>
        private List<string> WrapText(string text, int maxCharsPerLine)
        {
            if (string.IsNullOrEmpty(text))
                return new List<string> { "" };

            var words = text.Split(' ');
            var lines = new List<string>();
            var currentLine = "";

            foreach (var word in words)
            {
                if (string.IsNullOrEmpty(currentLine))
                {
                    currentLine = word;
                }
                else 
                {
                    var potentialLine = currentLine + " " + word;
                    
                    if (potentialLine.Length > maxCharsPerLine)
                    {
                        lines.Add(currentLine);
                        currentLine = word;
                    }
                    else
                    {
                        currentLine = potentialLine;
                    }
                }
            }

            if (!string.IsNullOrEmpty(currentLine))
            {
                lines.Add(currentLine);
            }

            return lines.Any() ? lines : new List<string> { "" };
        }

        /// <summary>
        /// Renders wrapped text within a rectangular area.
        /// Uses WrapText to break content into lines, then draws each line
        /// with appropriate vertical spacing.
        /// </summary>
        /// <param name="gfx">Graphics context for drawing</param>
        /// <param name="text">Text content to render</param>
        /// <param name="font">Font to use for text</param>
        /// <param name="brush">Color/style for text</param>
        /// <param name="rect">Rectangle defining the drawing area</param>
        /// <param name="maxCharsPerLine">Character limit for line wrapping</param>
        private void DrawWrappedText(XGraphics gfx, string text, XFont font, XBrush brush, XRect rect, int maxCharsPerLine)
        {
            var lines = WrapText(text, maxCharsPerLine);
            var lineHeight = font.Height;
            var currentY = rect.Y;

            for (int i = 0; i < lines.Count && currentY + lineHeight <= rect.Y + rect.Height; i++)
            {
                gfx.DrawString(lines[i], font, brush, new XRect(rect.X, currentY, rect.Width, lineHeight), XStringFormats.TopLeft);
                currentY += lineHeight;
            }
        }

        /// <summary>
        /// Calculates the height needed for a table row based on text content.
        /// Determines how many lines each text column needs, then returns height
        /// for the column requiring the most space, with minimum height enforcement.
        /// </summary>
        /// <param name="text1">First column text</param>
        /// <param name="text2">Second column text</param>
        /// <param name="maxChars1">Character limit for first column</param>
        /// <param name="maxChars2">Character limit for second column</param>
        /// <param name="lineHeight">Height of a single line of text</param>
        /// <returns>Required row height in pixels</returns>
        private int CalculateRequiredRowHeight(string text1, string text2, int maxChars1, int maxChars2, double lineHeight)
        {
            var lines1 = WrapText(text1, maxChars1).Count;
            var lines2 = WrapText(text2, maxChars2).Count;
            var maxLines = Math.Max(lines1, lines2);
            
            var calculatedHeight = (int)(maxLines * lineHeight + 12);
            return Math.Max(calculatedHeight, 40);
        }

        /// <summary>
        /// Wraps text using precise font metrics for pixel-perfect width control.
        /// Unlike character-based wrapping, this uses actual measured text width
        /// to determine line breaks, ensuring content fits exactly within boundaries.
        /// </summary>
        /// <param name="gfx">Graphics context for measuring text</param>
        /// <param name="text">Text to wrap</param>
        /// <param name="font">Font for measuring text width</param>
        /// <param name="maxWidth">Maximum pixel width for each line</param>
        /// <returns>List of text lines that fit within width constraints</returns>
        private List<string> WrapTextToWidth(XGraphics gfx, string text, XFont font, double maxWidth)
        {
            if (string.IsNullOrEmpty(text))
                return new List<string> { "" };

            var words = text.Split(' ');
            var lines = new List<string>();
            var currentLine = "";

            foreach (var word in words)
            {
                if (string.IsNullOrEmpty(currentLine))
                {
                    currentLine = word;
                }
                else 
                {
                    var potentialLine = currentLine + " " + word;
                    var textSize = gfx.MeasureString(potentialLine, font);
                    
                    if (textSize.Width > maxWidth)
                    {
                        lines.Add(currentLine);
                        currentLine = word;
                    }
                    else
                    {
                        currentLine = potentialLine;
                    }
                }
            }

            if (!string.IsNullOrEmpty(currentLine))
            {
                lines.Add(currentLine);
            }

            return lines.Any() ? lines : new List<string> { "" };
        }

        /// <summary>
        /// Renders text with precise width-based wrapping within a rectangle.
        /// Uses WrapTextToWidth for accurate line breaks based on actual font metrics,
        /// then draws each line with proper vertical spacing.
        /// </summary>
        /// <param name="gfx">Graphics context for drawing</param>
        /// <param name="text">Text content to render</param>
        /// <param name="font">Font for text rendering</param>
        /// <param name="brush">Color/style for text</param>
        /// <param name="rect">Rectangle defining the drawing area</param>
        private void DrawWrappedTextToFit(XGraphics gfx, string text, XFont font, XBrush brush, XRect rect)
        {
            var lines = WrapTextToWidth(gfx, text, font, rect.Width);
            var lineHeight = font.Height;
            var currentY = rect.Y;

            foreach (var line in lines)
            {
                gfx.DrawString(line, font, brush, new XRect(rect.X, currentY, rect.Width, lineHeight), XStringFormats.TopLeft);
                currentY += lineHeight;
            }
        }

        /// <summary>
        /// Draws a rectangle with optional fill and border.
        /// Currently implements standard rectangles. The cornerRadius parameter
        /// is reserved for future rounded corner implementation when library support improves.
        /// </summary>
        /// <param name="gfx">Graphics context for drawing</param>
        /// <param name="rect">Rectangle dimensions and position</param>
        /// <param name="cornerRadius">Reserved for future rounded corner support</param>
        /// <param name="fillBrush">Optional fill color/pattern</param>
        /// <param name="borderPen">Optional border style</param>
        private void DrawRoundedRectangle(XGraphics gfx, XRect rect, int cornerRadius, XBrush? fillBrush, XPen? borderPen)
        {
            if (fillBrush != null)
            {
                gfx.DrawRectangle(fillBrush, rect);
            }
            
            if (borderPen != null)
            {
                gfx.DrawRectangle(borderPen, rect);
            }
        }
    }
} 