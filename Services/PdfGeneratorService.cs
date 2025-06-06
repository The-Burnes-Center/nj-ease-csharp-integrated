using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using DocumentValidator.Models;
using System.Text;
using System.Linq;

namespace DocumentValidator.Services
{
    public class PdfGeneratorService
    {
        // Professional color scheme
        private readonly XColor PrimaryBlue = XColor.FromArgb(41, 98, 155);
        private readonly XColor SecondaryBlue = XColor.FromArgb(52, 144, 220);
        private readonly XColor AccentGray = XColor.FromArgb(248, 249, 250);
        private readonly XColor BorderGray = XColor.FromArgb(206, 212, 218);
        private readonly XColor SuccessGreen = XColor.FromArgb(40, 167, 69);
        private readonly XColor WarningRed = XColor.FromArgb(220, 53, 69);
        private readonly XColor TextDark = XColor.FromArgb(33, 37, 41);
        private readonly XColor TextMuted = XColor.FromArgb(108, 117, 125);

        public async Task<byte[]> GenerateConsolidatedReportAsync(List<ValidationResult> validationResults, string organizationName, List<SkippedDocument> skippedDocuments)
        {
            return await Task.Run(() =>
            {
                // Only process valid results that are not unknown document types
                var validResults = validationResults.Where(result =>
                    result != null && !string.IsNullOrEmpty(result.FileName) &&
                    !string.IsNullOrEmpty(result.DocumentType) && result.DocumentInfo != null &&
                    result.DocumentType != "unknown").ToList();

                if (validResults.Count == 0 && skippedDocuments.Count == 0)
                {
                    throw new InvalidOperationException("No valid document validation results to generate report");
                }

                // Create a PDF document
                var document = new PdfDocument();
                var page = document.AddPage();
                var gfx = XGraphics.FromPdfPage(page);
                
                // Enhanced font collection
                var font = new XFont("Arial", 11);
                var fontBold = new XFont("Arial", 11, XFontStyle.Bold);
                var fontLarge = new XFont("Arial", 20, XFontStyle.Bold);
                var fontMedium = new XFont("Arial", 14, XFontStyle.Bold);
                var fontSmall = new XFont("Arial", 9);

                var yPosition = 40;

                // Add elegant header with background
                var headerRect = new XRect(0, 0, page.Width, 130);
                var headerBrush = new XLinearGradientBrush(
                    new XPoint(0, 0), new XPoint(0, 130),
                    PrimaryBlue, SecondaryBlue);
                gfx.DrawRectangle(headerBrush, headerRect);

                // Add report header with white text on blue background
                gfx.DrawString("NJ EASE Document Validation Report", fontLarge, XBrushes.White,
                    new XRect(0, yPosition, page.Width, 35), XStringFormats.TopCenter);
                yPosition += 45;

                gfx.DrawString($"Organization: {organizationName}", new XFont("Arial", 13, XFontStyle.Bold), XBrushes.White,
                    new XRect(0, yPosition, page.Width, 20), XStringFormats.TopCenter);
                yPosition += 25;

                gfx.DrawString($"Generated: {DateTime.Now:MMMM dd, yyyy 'at' hh:mm tt}", fontSmall, new XSolidBrush(XColor.FromArgb(240, 248, 255)),
                    new XRect(0, yPosition, page.Width, 15), XStringFormats.TopCenter);
                yPosition += 40;

                // Add summary section with enhanced styling
                var summaryHeaderRect = new XRect(40, yPosition, page.Width - 80, 35);
                gfx.DrawRectangle(new XSolidBrush(AccentGray), summaryHeaderRect);
                gfx.DrawRectangle(new XPen(BorderGray, 1), summaryHeaderRect);

                gfx.DrawString("VALIDATION SUMMARY", fontMedium, new XSolidBrush(PrimaryBlue),
                    new XRect(50, yPosition + 8, page.Width - 100, 20), XStringFormats.TopLeft);
                yPosition += 45;

                // Calculate table layout with enhanced spacing
                var pageWidth = page.Width - 80;
                var startX = 40;

                var columnWidths = new double[]
                {
                    0.40 * pageWidth, // Document Name - increased from 35%
                    0.38 * pageWidth, // Document Type - increased from 35%
                    0.12 * pageWidth, // Status - decreased from 15%
                    0.10 * pageWidth  // Issues - decreased from 15%
                };

                var headers = new[] { "Document Name", "Document Type", "Status", "Issues" };

                // Draw enhanced table header with gradient
                var headerTableRect = new XRect(startX, yPosition, pageWidth, 25);
                var tableBrush = new XLinearGradientBrush(
                    new XPoint(0, yPosition), new XPoint(0, yPosition + 25),
                    PrimaryBlue, SecondaryBlue);
                gfx.DrawRectangle(tableBrush, headerTableRect);
                gfx.DrawRectangle(new XPen(BorderGray, 1.5), headerTableRect);

                var x = startX;
                for (int i = 0; i < headers.Length; i++)
                {
                    // Add vertical separators
                    if (i > 0)
                    {
                        gfx.DrawLine(new XPen(XColor.FromArgb(255, 255, 255, 100), 1), 
                            x, yPosition + 3, x, yPosition + 22);
                    }
                    
                    gfx.DrawString(headers[i], new XFont("Arial", 10, XFontStyle.Bold), XBrushes.White,
                        new XRect(x + 8, yPosition + 6, columnWidths[i] - 16, 15), XStringFormats.TopLeft);
                    x += (int)columnWidths[i];
                }
                yPosition += 25;

                // Draw enhanced table rows
                for (int index = 0; index < validResults.Count; index++)
                {
                    var result = validResults[index];
                    var documentTypeName = FormatDocumentType(result.DocumentType);
                    
                    // Calculate dynamic row height based on text wrapping needs
                    var rowHeight = CalculateRequiredRowHeight(result.FileName, documentTypeName, 45, 40, font.Height);

                    // Enhanced alternating row colors with subtle gradients
                    var rowColor1 = index % 2 == 0 ? XColor.FromArgb(252, 253, 254) : XColors.White;
                    var rowColor2 = index % 2 == 0 ? XColor.FromArgb(248, 249, 250) : XColor.FromArgb(254, 254, 254);
                    
                    var rowRect = new XRect(startX, yPosition, pageWidth, rowHeight);
                    var rowBrush = new XLinearGradientBrush(
                        new XPoint(0, yPosition), new XPoint(0, yPosition + rowHeight),
                        rowColor1, rowColor2);
                    gfx.DrawRectangle(rowBrush, rowRect);
                    gfx.DrawRectangle(new XPen(BorderGray, 0.5), rowRect);

                    x = startX;

                    // Document Name column with enhanced styling
                    DrawWrappedText(gfx, result.FileName, font, new XSolidBrush(TextDark),
                        new XRect(x + 8, yPosition + 6, columnWidths[0] - 16, rowHeight - 12), 45);
                    
                    // Add subtle vertical separator
                    gfx.DrawLine(new XPen(BorderGray, 0.5), 
                        x + (int)columnWidths[0], yPosition, x + (int)columnWidths[0], yPosition + rowHeight);
                    x += (int)columnWidths[0];

                    // Document Type column with enhanced styling
                    DrawWrappedText(gfx, documentTypeName, font, new XSolidBrush(TextDark),
                        new XRect(x + 8, yPosition + 6, columnWidths[1] - 16, rowHeight - 12), 40);
                    
                    gfx.DrawLine(new XPen(BorderGray, 0.5), 
                        x + (int)columnWidths[1], yPosition, x + (int)columnWidths[1], yPosition + rowHeight);
                    x += (int)columnWidths[1];

                    // Enhanced Status column with badges
                    var statusColor = result.Success ? SuccessGreen : WarningRed;
                    var statusBgColor = result.Success ? 
                        XColor.FromArgb(212, 237, 218) : XColor.FromArgb(248, 215, 218);
                    var statusText = result.Success ? "PASSED" : "FAILED";
                    
                    // Draw status badge background
                    var statusBadgeRect = new XRect(x + 6, yPosition + 8, columnWidths[2] - 12, 18);
                    gfx.DrawRectangle(new XSolidBrush(statusBgColor), statusBadgeRect);
                    gfx.DrawRectangle(new XPen(statusColor, 1), statusBadgeRect);
                    
                    gfx.DrawString(statusText, new XFont("Arial", 9, XFontStyle.Bold), new XSolidBrush(statusColor),
                        new XRect(x + 8, yPosition + 9, columnWidths[2] - 16, 15), XStringFormats.TopCenter);
                    
                    gfx.DrawLine(new XPen(BorderGray, 0.5), 
                        x + (int)columnWidths[2], yPosition, x + (int)columnWidths[2], yPosition + rowHeight);
                    x += (int)columnWidths[2];

                    // Enhanced Issues column
                    var issuesText = result.MissingElements.Count > 0
                        ? $"{result.MissingElements.Count} ISSUE{(result.MissingElements.Count > 1 ? "S" : "")}"
                        : "NONE";
                    var issuesColor = result.MissingElements.Count > 0 ? new XSolidBrush(WarningRed) : new XSolidBrush(SuccessGreen);
                    
                    gfx.DrawString(issuesText, new XFont("Arial", 9, XFontStyle.Bold), issuesColor,
                        new XRect(x + 8, yPosition + 6, columnWidths[3] - 16, rowHeight - 12), XStringFormats.TopLeft);

                    yPosition += rowHeight;

                    // Check if we need a new page
                    if (yPosition > page.Height - 120)
                    {
                        page = document.AddPage();
                        gfx = XGraphics.FromPdfPage(page);
                        yPosition = 50;
                    }
                }

                // Add enhanced information section
                yPosition += 25;
                var infoRect = new XRect(40, yPosition, page.Width - 80, 35);
                gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 243, 205)), infoRect);
                gfx.DrawRectangle(new XPen(XColor.FromArgb(255, 193, 7), 1), infoRect);

                var infoText = "Detailed validation results for each document are provided on the following pages.";
                gfx.DrawString(infoText, new XFont("Arial", 11, XFontStyle.Bold), new XSolidBrush(XColor.FromArgb(133, 100, 4)),
                    new XRect(50, yPosition + 10, page.Width - 100, 20), XStringFormats.TopLeft);

                // Add detailed validation results for each document on separate pages
                foreach (var result in validResults)
                {
                    page = document.AddPage();
                    gfx = XGraphics.FromPdfPage(page);
                    AddDetailedValidationPage(gfx, page, result, organizationName);
                }

                // Add skipped documents page if any documents were skipped
                if (skippedDocuments.Count > 0)
                {
                    page = document.AddPage();
                    gfx = XGraphics.FromPdfPage(page);
                    AddSkippedDocumentsPage(gfx, page, skippedDocuments, organizationName);
                }

                // Save to memory stream
                using var stream = new MemoryStream();
                document.Save(stream);
                return stream.ToArray();
            });
        }

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

        private void AddDetailedValidationPage(XGraphics gfx, PdfPage page, ValidationResult result, string organizationName)
        {
            if (result?.DocumentInfo == null) return;

            var font = new XFont("Arial", 11);
            var fontBold = new XFont("Arial", 12, XFontStyle.Bold);
            var fontLarge = new XFont("Arial", 18, XFontStyle.Bold);
            var fontMedium = new XFont("Arial", 14, XFontStyle.Bold);

            var yPosition = 30;

            // Enhanced header with gradient background
            var headerRect = new XRect(0, 0, page.Width, 80);
            var headerBrush = new XLinearGradientBrush(
                new XPoint(0, 0), new XPoint(0, 80),
                PrimaryBlue, SecondaryBlue);
            gfx.DrawRectangle(headerBrush, headerRect);

            gfx.DrawString("Document Validation Details", fontLarge, XBrushes.White,
                new XRect(0, yPosition, page.Width, 30), XStringFormats.TopCenter);
            yPosition += 60;

            // Enhanced section styling
            var sectionHeaderStyle = new XFont("Arial", 13, XFontStyle.Bold);
            var sectionBgColor = new XSolidBrush(AccentGray);
            var sectionBorderColor = new XPen(BorderGray, 1);

            // Document Details section
            var detailsRect = new XRect(40, yPosition, page.Width - 80, 25);
            gfx.DrawRectangle(sectionBgColor, detailsRect);
            gfx.DrawRectangle(sectionBorderColor, detailsRect);
            gfx.DrawString("DOCUMENT DETAILS", sectionHeaderStyle, new XSolidBrush(PrimaryBlue),
                new XRect(50, yPosition + 5, page.Width - 100, 20), XStringFormats.TopLeft);
            yPosition += 35;

            // Document info with enhanced styling
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
                
                DrawWrappedText(gfx, item, font, new XSolidBrush(TextDark),
                    new XRect(50, yPosition, page.Width - 100, itemHeight), 80);
                yPosition += (int)itemHeight + 3; // Small spacing between items
            }
            yPosition += 15;

            // Validation Summary section
            var summaryRect = new XRect(40, yPosition, page.Width - 80, 25);
            gfx.DrawRectangle(sectionBgColor, summaryRect);
            gfx.DrawRectangle(sectionBorderColor, summaryRect);
            gfx.DrawString("VALIDATION SUMMARY", sectionHeaderStyle, new XSolidBrush(PrimaryBlue),
                new XRect(50, yPosition + 5, page.Width - 100, 20), XStringFormats.TopLeft);
            yPosition += 35;

            // Enhanced overall result with badge
            var resultColor = result.Success ? SuccessGreen : WarningRed;
            var resultBgColor = result.Success ? 
                XColor.FromArgb(212, 237, 218) : XColor.FromArgb(248, 215, 218);
            var resultText = result.Success ? "VALIDATION PASSED" : "VALIDATION FAILED";
            
            var resultBadgeRect = new XRect(50, yPosition, 200, 25);
            gfx.DrawRectangle(new XSolidBrush(resultBgColor), resultBadgeRect);
            gfx.DrawRectangle(new XPen(resultColor, 2), resultBadgeRect);
            
            gfx.DrawString(resultText, fontBold, new XSolidBrush(resultColor),
                new XRect(55, yPosition + 5, 190, 20), XStringFormats.TopLeft);
            yPosition += 40;

            // Document Statistics section
            var statsRect = new XRect(40, yPosition, page.Width - 80, 25);
            gfx.DrawRectangle(sectionBgColor, statsRect);
            gfx.DrawRectangle(sectionBorderColor, statsRect);
            gfx.DrawString("DOCUMENT STATISTICS", sectionHeaderStyle, new XSolidBrush(PrimaryBlue),
                new XRect(50, yPosition + 5, page.Width - 100, 20), XStringFormats.TopLeft);
            yPosition += 35;

            var statsItems = new[]
            {
                $"Page Count: {result.DocumentInfo.PageCount}",
                $"Word Count: {result.DocumentInfo.WordCount:N0}",
                $"Contains Handwriting: {(result.DocumentInfo.ContainsHandwriting ? "Yes" : "No")}"
            };

            foreach (var item in statsItems)
            {
                var itemLines = WrapText(item, 80);
                var itemHeight = itemLines.Count * font.Height;
                
                DrawWrappedText(gfx, item, font, new XSolidBrush(TextDark),
                    new XRect(50, yPosition, page.Width - 100, itemHeight), 80);
                yPosition += (int)itemHeight + 3;
            }

            if (result.DocumentInfo.LanguageInfo.Any())
            {
                var langInfo = result.DocumentInfo.LanguageInfo.First();
                var langText = $"Primary Language: {langInfo.LanguageCode} (Confidence: {(langInfo.Confidence * 100):F1}%)";
                var langLines = WrapText(langText, 80);
                var langHeight = langLines.Count * font.Height;
                
                DrawWrappedText(gfx, langText, font, new XSolidBrush(TextDark),
                    new XRect(50, yPosition, page.Width - 100, langHeight), 80);
                yPosition += (int)langHeight + 3;
            }

            if (!string.IsNullOrEmpty(result.DocumentInfo.DetectedOrganizationName))
            {
                var orgText = $"Detected Organization: {result.DocumentInfo.DetectedOrganizationName}";
                var orgLines = WrapText(orgText, 80);
                var orgHeight = orgLines.Count * font.Height;
                
                DrawWrappedText(gfx, orgText, font, new XSolidBrush(TextDark),
                    new XRect(50, yPosition, page.Width - 100, orgHeight), 80);
                yPosition += (int)orgHeight + 3;
            }

            yPosition += 20;

            // Issues or Success section
            if (!result.Success)
            {
                var issuesRect = new XRect(40, yPosition, page.Width - 80, 25);
                gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(248, 215, 218)), issuesRect);
                gfx.DrawRectangle(new XPen(WarningRed, 1), issuesRect);
                gfx.DrawString("ISSUES DETECTED", sectionHeaderStyle, new XSolidBrush(WarningRed),
                    new XRect(50, yPosition + 5, page.Width - 100, 20), XStringFormats.TopLeft);
                yPosition += 35;

                for (int i = 0; i < result.MissingElements.Count; i++)
                {
                    var issueText = $"• {result.MissingElements[i]}";
                    var issueLines = WrapText(issueText, 75);
                    var issueHeight = issueLines.Count * font.Height;
                    
                    DrawWrappedText(gfx, issueText, font, new XSolidBrush(TextDark),
                        new XRect(60, yPosition, page.Width - 120, issueHeight), 75);
                    yPosition += (int)issueHeight + 3;
                }

                yPosition += 15;

                if (result.SuggestedActions.Any())
                {
                    var actionsRect = new XRect(40, yPosition, page.Width - 80, 25);
                    gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 243, 205)), actionsRect);
                    gfx.DrawRectangle(new XPen(XColor.FromArgb(255, 193, 7), 1), actionsRect);
                    gfx.DrawString("SUGGESTED ACTIONS", sectionHeaderStyle, new XSolidBrush(XColor.FromArgb(133, 100, 4)),
                        new XRect(50, yPosition + 5, page.Width - 100, 20), XStringFormats.TopLeft);
                    yPosition += 35;

                    for (int i = 0; i < result.SuggestedActions.Count; i++)
                    {
                        var actionText = $"• {result.SuggestedActions[i]}";
                        var actionLines = WrapText(actionText, 75);
                        var actionHeight = actionLines.Count * font.Height;
                        
                        DrawWrappedText(gfx, actionText, font, new XSolidBrush(TextDark),
                            new XRect(60, yPosition, page.Width - 120, actionHeight), 75);
                        yPosition += (int)actionHeight + 3;
                    }
                }
            }
            else
            {
                var successRect = new XRect(40, yPosition, page.Width - 80, 40);
                gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(212, 237, 218)), successRect);
                gfx.DrawRectangle(new XPen(SuccessGreen, 2), successRect);
                
                var successText = "Document validation completed successfully with no issues!";
                DrawWrappedText(gfx, successText, fontBold, new XSolidBrush(SuccessGreen),
                    new XRect(50, yPosition + 12, page.Width - 100, 25), 65);
                yPosition += 50;
            }

            // Enhanced footer
            var footerRect = new XRect(0, page.Height - 40, page.Width, 40);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(248, 249, 250)), footerRect);
            gfx.DrawLine(new XPen(BorderGray, 1), 0, page.Height - 40, page.Width, page.Height - 40);
            
            var footerText = $"This report was automatically generated • © {DateTime.Now.Year} Document Validator";
            gfx.DrawString(footerText, new XFont("Arial", 9), new XSolidBrush(TextMuted),
                new XRect(0, page.Height - 25, page.Width, 20), XStringFormats.TopCenter);
        }

        private void AddSkippedDocumentsPage(XGraphics gfx, PdfPage page, List<SkippedDocument> skippedDocuments, string organizationName)
        {
            var font = new XFont("Arial", 11);
            var fontBold = new XFont("Arial", 12, XFontStyle.Bold);
            var fontLarge = new XFont("Arial", 18, XFontStyle.Bold);
            var fontMedium = new XFont("Arial", 14, XFontStyle.Bold);

            var yPosition = 30;

            // Enhanced header with gradient background
            var headerRect = new XRect(0, 0, page.Width, 80);
            var headerBrush = new XLinearGradientBrush(
                new XPoint(0, 0), new XPoint(0, 80),
                XColor.FromArgb(255, 193, 7), XColor.FromArgb(255, 235, 59));
            gfx.DrawRectangle(headerBrush, headerRect);

            gfx.DrawString("Uncategorized Documents", fontLarge, XBrushes.White,
                new XRect(0, yPosition, page.Width, 30), XStringFormats.TopCenter);
            yPosition += 60;

            // Enhanced explanation section
            var explanationRect = new XRect(40, yPosition, page.Width - 80, 50);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 243, 205)), explanationRect);
            gfx.DrawRectangle(new XPen(XColor.FromArgb(255, 193, 7), 1), explanationRect);

            var explanationText = "The following documents could not be automatically classified. These documents require manual review to determine their type and verify compliance.";
            DrawWrappedText(gfx, explanationText, font, new XSolidBrush(XColor.FromArgb(133, 100, 4)),
                new XRect(50, yPosition + 8, page.Width - 100, 40), 85);
            yPosition += 70;

            // Calculate table layout
            var pageWidth = page.Width - 80;
            var startX = 40;

            var columnWidths = new double[]
            {
                0.70 * pageWidth, // Document Name - increased from 65%
                0.30 * pageWidth  // Reason Skipped - decreased from 35% but with better wrapping
            };

            var headers = new[] { "Document Name", "Reason Skipped" };

            // Enhanced table header
            var headerTableRect = new XRect(startX, yPosition, pageWidth, 25);
            var tableBrush = new XLinearGradientBrush(
                new XPoint(0, yPosition), new XPoint(0, yPosition + 25),
                XColor.FromArgb(255, 193, 7), XColor.FromArgb(255, 235, 59));
            gfx.DrawRectangle(tableBrush, headerTableRect);
            gfx.DrawRectangle(new XPen(BorderGray, 1.5), headerTableRect);

            var x = startX;
            for (int i = 0; i < headers.Length; i++)
            {
                if (i > 0)
                {
                    gfx.DrawLine(new XPen(XColor.FromArgb(255, 255, 255, 150), 1), 
                        x, yPosition + 3, x, yPosition + 22);
                }
                
                gfx.DrawString(headers[i], new XFont("Arial", 11, XFontStyle.Bold), XBrushes.White,
                    new XRect(x + 8, yPosition + 6, columnWidths[i] - 16, 15), XStringFormats.TopLeft);
                x += (int)columnWidths[i];
            }
            yPosition += 25;

            // Enhanced table rows
            for (int index = 0; index < skippedDocuments.Count; index++)
            {
                var document = skippedDocuments[index];
                
                var rowHeight = CalculateRequiredRowHeight(document.FileName, document.Reason, 55, 35, font.Height);

                // Enhanced alternating row colors
                var rowColor1 = index % 2 == 0 ? XColor.FromArgb(254, 252, 246) : XColors.White;
                var rowColor2 = index % 2 == 0 ? XColor.FromArgb(252, 248, 227) : XColor.FromArgb(254, 254, 254);
                
                var rowRect = new XRect(startX, yPosition, pageWidth, rowHeight);
                var rowBrush = new XLinearGradientBrush(
                    new XPoint(0, yPosition), new XPoint(0, yPosition + rowHeight),
                    rowColor1, rowColor2);
                gfx.DrawRectangle(rowBrush, rowRect);
                gfx.DrawRectangle(new XPen(BorderGray, 0.5), rowRect);

                x = startX;

                // Document Name column
                DrawWrappedText(gfx, document.FileName, font, new XSolidBrush(TextDark),
                    new XRect(x + 8, yPosition + 6, columnWidths[0] - 16, rowHeight - 12), 55);
                
                gfx.DrawLine(new XPen(BorderGray, 0.5), 
                    x + (int)columnWidths[0], yPosition, x + (int)columnWidths[0], yPosition + rowHeight);
                x += (int)columnWidths[0];

                // Reason column
                DrawWrappedText(gfx, document.Reason, font, new XSolidBrush(XColor.FromArgb(133, 100, 4)),
                    new XRect(x + 8, yPosition + 6, columnWidths[1] - 16, rowHeight - 12), 35);

                yPosition += rowHeight;

                // Check if we need a new page
                if (yPosition > page.Height - 120)
                {
                    break;
                }
            }

            // Enhanced note section
            yPosition += 25;
            var noteRect = new XRect(40, yPosition, page.Width - 80, 50);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(248, 215, 218)), noteRect);
            gfx.DrawRectangle(new XPen(WarningRed, 1), noteRect);
            
            var noteText = "Note: These documents should be manually reviewed to determine their document type and verify compliance requirements.";
            DrawWrappedText(gfx, noteText, fontBold, new XSolidBrush(WarningRed),
                new XRect(50, yPosition + 8, page.Width - 100, 40), 80);
        }

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

        private List<string> WrapText(string text, int maxCharsPerLine)
        {
            if (string.IsNullOrEmpty(text))
                return new List<string> { "" };

            // Handle very long single words by allowing them to exceed the line limit slightly
            var words = text.Split(' ');
            var lines = new List<string>();
            var currentLine = "";

            foreach (var word in words)
            {
                if (string.IsNullOrEmpty(currentLine))
                {
                    // First word on the line
                    currentLine = word;
                }
                else 
                {
                    var potentialLine = currentLine + " " + word;
                    
                    // If adding this word would exceed the limit
                    if (potentialLine.Length > maxCharsPerLine)
                    {
                        // If current line is not empty, finish it and start new line
                        lines.Add(currentLine);
                        currentLine = word;
                    }
                    else
                    {
                        // Word fits, add it to current line
                        currentLine = potentialLine;
                    }
                }
            }

            // Add the last line if it has content
            if (!string.IsNullOrEmpty(currentLine))
            {
                lines.Add(currentLine);
            }

            return lines.Any() ? lines : new List<string> { "" };
        }

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

        private int CalculateRequiredRowHeight(string text1, string text2, int maxChars1, int maxChars2, double lineHeight)
        {
            var lines1 = WrapText(text1, maxChars1).Count;
            var lines2 = WrapText(text2, maxChars2).Count;
            var maxLines = Math.Max(lines1, lines2);
            
            // Ensure minimum height for readability, with better spacing
            var calculatedHeight = (int)(maxLines * lineHeight + 12); // Increased padding from 10 to 12
            return Math.Max(calculatedHeight, 40); // Increased minimum height from 35 to 40
        }
    }
} 