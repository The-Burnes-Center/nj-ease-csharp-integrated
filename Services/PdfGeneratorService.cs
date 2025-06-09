using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using DocumentValidator.Models;
using System.Text;
using System.Linq;

namespace DocumentValidator.Services
{
    public class PdfGeneratorService
    {
        // Modern color palette inspired by contemporary design systems
        private readonly XColor PrimaryBlue = XColor.FromArgb(37, 99, 235); // Modern blue (blue-600)
        private readonly XColor PrimaryDark = XColor.FromArgb(30, 64, 175); // Darker blue (blue-700)
        private readonly XColor SecondaryBlue = XColor.FromArgb(59, 130, 246); // Lighter blue (blue-500)
        private readonly XColor AccentGray = XColor.FromArgb(249, 250, 251); // Very light gray (gray-50)
        private readonly XColor BackgroundGray = XColor.FromArgb(243, 244, 246); // Light gray (gray-100)
        private readonly XColor BorderGray = XColor.FromArgb(229, 231, 235); // Border gray (gray-200)
        private readonly XColor SuccessGreen = XColor.FromArgb(16, 185, 129); // Modern green (emerald-500)
        private readonly XColor SuccessLight = XColor.FromArgb(209, 250, 229); // Light green background
        private readonly XColor ErrorRed = XColor.FromArgb(239, 68, 68); // Modern red (red-500)
        private readonly XColor ErrorLight = XColor.FromArgb(254, 226, 226); // Light red background
        private readonly XColor WarningOrange = XColor.FromArgb(245, 158, 11); // Modern orange (amber-500)
        private readonly XColor WarningLight = XColor.FromArgb(254, 243, 199); // Light orange background
        private readonly XColor TextPrimary = XColor.FromArgb(17, 24, 39); // Dark text (gray-900)
        private readonly XColor TextSecondary = XColor.FromArgb(75, 85, 99); // Medium text (gray-600)
        private readonly XColor TextMuted = XColor.FromArgb(156, 163, 175); // Light text (gray-400)

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
                
                // Modern font collection with better hierarchy
                var font = new XFont("Segoe UI", 11);
                var fontBold = new XFont("Segoe UI", 11, XFontStyle.Bold);
                var fontLarge = new XFont("Segoe UI", 24, XFontStyle.Bold); // Increased from 20
                var fontMedium = new XFont("Segoe UI", 16, XFontStyle.Bold); // Increased from 14
                var fontSmall = new XFont("Segoe UI", 9);
                var fontTiny = new XFont("Segoe UI", 8);

                var yPosition = 50; // Increased top margin

                // Modern flat header with clean design
                var headerRect = new XRect(0, 0, page.Width, 160);
                gfx.DrawRectangle(new XSolidBrush(PrimaryBlue), headerRect);

                // Add subtle drop shadow instead of gradient
                var shadowRect = new XRect(0, 160, page.Width, 4);
                gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(40, 0, 0, 0)), shadowRect);

                // Modern title with clean typography
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

                // Modern card design with matching background
                var summaryCardRect = new XRect(50, yPosition, page.Width - 100, 45);
                
                // Create rounded corner effect with AccentGray background to match detail pages
                DrawRoundedRectangle(gfx, summaryCardRect, 8, new XSolidBrush(AccentGray), new XPen(BorderGray, 1));
                
                // Add subtle drop shadow for depth
                var cardShadow = new XRect(52, yPosition + 2, page.Width - 100, 45);
                DrawRoundedRectangle(gfx, cardShadow, 8, new XSolidBrush(XColor.FromArgb(20, 0, 0, 0)), null);

                gfx.DrawString("Validation Summary", new XFont("Segoe UI", 15, XFontStyle.Bold), 
                    new XSolidBrush(PrimaryDark),
                    new XRect(65, yPosition + 12, page.Width - 130, 25), XStringFormats.TopLeft);
                yPosition += 65;

                // Enhanced table layout with better proportions
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

                // Modern flat table header
                var headerTableRect = new XRect(startX, yPosition, pageWidth, 35);
                DrawRoundedRectangle(gfx, headerTableRect, 6, new XSolidBrush(PrimaryBlue), null);

                var x = startX;
                for (int i = 0; i < headers.Length; i++)
                {
                    // Modern vertical separators
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

                // Draw enhanced table rows with modern flat styling
                for (int index = 0; index < validResults.Count; index++)
                {
                    var result = validResults[index];
                    var documentTypeName = FormatDocumentType(result.DocumentType);
                    
                    // Calculate row height to accommodate full text without truncation
                    var nameLines = WrapTextToWidth(gfx, result.FileName, font, columnWidths[0] - 24);
                    var typeLines = WrapTextToWidth(gfx, documentTypeName, font, columnWidths[1] - 24);
                    var maxLines = Math.Max(nameLines.Count, typeLines.Count);
                    
                    // Calculate row height based on actual line count with proper padding
                    var rowHeight = Math.Max(50, (int)(maxLines * font.Height + 20)); // Minimum 50px height, 20px padding

                    // Modern flat alternating row colors
                    var rowColor = index % 2 == 0 ? XColor.FromArgb(252, 253, 254) : XColors.White;
                    
                    var rowRect = new XRect(startX, yPosition, pageWidth, rowHeight);
                    DrawRoundedRectangle(gfx, rowRect, 4, new XSolidBrush(rowColor), new XPen(BorderGray, 0.5));

                    x = startX;

                    // Document Name column - display full text with proper wrapping
                    DrawWrappedTextToFit(gfx, result.FileName, font, new XSolidBrush(TextPrimary),
                        new XRect(x + 12, yPosition + 10, columnWidths[0] - 24, rowHeight - 20));
                    
                    // Modern vertical separator
                    gfx.DrawLine(new XPen(BorderGray, 0.5), 
                        x + (int)columnWidths[0], yPosition + 5, x + (int)columnWidths[0], yPosition + rowHeight - 5);
                    x += (int)columnWidths[0];

                    // Document Type column - display full text with proper wrapping
                    DrawWrappedTextToFit(gfx, documentTypeName, font, new XSolidBrush(TextPrimary),
                        new XRect(x + 12, yPosition + 10, columnWidths[1] - 24, rowHeight - 20));
                    
                    gfx.DrawLine(new XPen(BorderGray, 0.5), 
                        x + (int)columnWidths[1], yPosition + 5, x + (int)columnWidths[1], yPosition + rowHeight - 5);
                    x += (int)columnWidths[1];

                    // Modern Status column with flat rounded badges
                    var statusColor = result.Success ? SuccessGreen : ErrorRed;
                    var statusBgColor = result.Success ? SuccessLight : ErrorLight;
                    var statusText = result.Success ? "PASSED" : "FAILED";
                    
                    // Draw modern flat status badge with rounded corners
                    var statusBadgeRect = new XRect(x + 8, yPosition + 10, columnWidths[2] - 16, 22);
                    DrawRoundedRectangle(gfx, statusBadgeRect, 4, new XSolidBrush(statusBgColor), new XPen(statusColor, 1));
                    
                    // Center align text both horizontally and vertically within the badge
                    gfx.DrawString(statusText, new XFont("Segoe UI", 9, XFontStyle.Bold), new XSolidBrush(statusColor),
                        statusBadgeRect, XStringFormats.Center);
                    
                    gfx.DrawLine(new XPen(BorderGray, 0.5), 
                        x + (int)columnWidths[2], yPosition + 5, x + (int)columnWidths[2], yPosition + rowHeight - 5);
                    x += (int)columnWidths[2];

                    // Modern Issues column
                    var issuesText = result.MissingElements.Count > 0
                        ? $"{result.MissingElements.Count}"
                        : "None";
                    var issuesColor = result.MissingElements.Count > 0 ? new XSolidBrush(ErrorRed) : new XSolidBrush(SuccessGreen);
                    
                    gfx.DrawString(issuesText, new XFont("Segoe UI", 9, XFontStyle.Bold), issuesColor,
                        new XRect(x + 12, yPosition + 10, columnWidths[3] - 24, rowHeight - 20), XStringFormats.TopLeft);

                    yPosition += rowHeight;

                    // Check if we need a new page
                    if (yPosition > page.Height - 120)
                    {
                        page = document.AddPage();
                        gfx = XGraphics.FromPdfPage(page);
                        yPosition = 50;
                    }
                }

                // Modern information section with flat design
                yPosition += 30;
                var infoRect = new XRect(50, yPosition, page.Width - 100, 45);
                DrawRoundedRectangle(gfx, infoRect, 8, new XSolidBrush(WarningLight), new XPen(WarningOrange, 1));

                var infoText = "Detailed validation results for each document are provided on the following pages.";
                gfx.DrawString(infoText, new XFont("Segoe UI", 12, XFontStyle.Regular), 
                    new XSolidBrush(XColor.FromArgb(146, 64, 14)),
                    new XRect(65, yPosition + 14, page.Width - 130, 25), XStringFormats.TopLeft);

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

            var font = new XFont("Segoe UI", 11);
            var fontBold = new XFont("Segoe UI", 12, XFontStyle.Bold);
            var fontLarge = new XFont("Segoe UI", 20, XFontStyle.Bold); // Increased from 18
            var fontMedium = new XFont("Segoe UI", 15, XFontStyle.Bold); // Increased from 14

            var yPosition = 40; // Increased from 30

            // Modern header with reduced height
            var headerRect = new XRect(0, 0, page.Width, 80); // Reduced from 110 to 80
            gfx.DrawRectangle(new XSolidBrush(PrimaryBlue), headerRect);

            // Add subtle drop shadow
            var shadowRect = new XRect(0, 80, page.Width, 6); // Updated Y position
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(40, 0, 0, 0)), shadowRect);

            // Display document type as the header instead of generic title
            var documentTypeTitle = FormatDocumentType(result.DocumentType);
            gfx.DrawString(documentTypeTitle, fontLarge, XBrushes.White,
                new XRect(0, yPosition, page.Width, 35), XStringFormats.TopCenter);
            yPosition += 60; // Reduced spacing to account for shorter header

            // Modern section styling with flat design
            var sectionHeaderStyle = new XFont("Segoe UI", 14, XFontStyle.Bold);
            var sectionBorderColor = new XPen(BorderGray, 1);

            // Document Details section with modern flat card design
            var detailsRect = new XRect(50, yPosition, page.Width - 100, 30);
            DrawRoundedRectangle(gfx, detailsRect, 6, new XSolidBrush(AccentGray), sectionBorderColor);
            
            gfx.DrawString("Document Details", sectionHeaderStyle, new XSolidBrush(PrimaryDark),
                new XRect(65, yPosition + 6, page.Width - 130, 25), XStringFormats.TopLeft);
            yPosition += 40;

            // Document info with modern styling and better spacing
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

            // Modern Validation Summary section with flat design
            var summaryRect = new XRect(50, yPosition, page.Width - 100, 30);
            DrawRoundedRectangle(gfx, summaryRect, 6, new XSolidBrush(AccentGray), sectionBorderColor);
            
            gfx.DrawString("Validation Result", sectionHeaderStyle, new XSolidBrush(PrimaryDark),
                new XRect(65, yPosition + 6, page.Width - 130, 25), XStringFormats.TopLeft);
            yPosition += 40;

            // Modern overall result with flat background box
            var resultColor = result.Success ? SuccessGreen : ErrorRed;
            var resultBgColor = result.Success ? SuccessLight : ErrorLight;
            var resultText = result.Success ? "Passed" : "Failed";
            
            // Create flat background box for the result
            var resultBadgeRect = new XRect(65, yPosition, 160, 35);
            DrawRoundedRectangle(gfx, resultBadgeRect, 6, new XSolidBrush(resultBgColor), new XPen(resultColor, 1));
            
            // Center align text both horizontally and vertically within the box
            gfx.DrawString(resultText, new XFont("Segoe UI", 14, XFontStyle.Bold), new XSolidBrush(resultColor),
                resultBadgeRect, XStringFormats.Center);
            yPosition += 50;

            // Modern Document Statistics section with flat design
            var statsRect = new XRect(50, yPosition, page.Width - 100, 30);
            DrawRoundedRectangle(gfx, statsRect, 6, new XSolidBrush(AccentGray), sectionBorderColor);
            
            gfx.DrawString("Document Statistics", sectionHeaderStyle, new XSolidBrush(PrimaryDark),
                new XRect(65, yPosition + 6, page.Width - 130, 25), XStringFormats.TopLeft);
            yPosition += 40;

            // Modern statistics with enhanced formatting
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

            // Add detected organization info if available
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

            // Modern Issues or Success section with flat design
            if (!result.Success)
            {
                var issuesRect = new XRect(50, yPosition, page.Width - 100, 30);
                DrawRoundedRectangle(gfx, issuesRect, 6, new XSolidBrush(ErrorLight), new XPen(ErrorRed, 1));
                
                gfx.DrawString("Issues Detected", sectionHeaderStyle, new XSolidBrush(ErrorRed),
                    new XRect(65, yPosition + 6, page.Width - 130, 25), XStringFormats.TopLeft);
                yPosition += 40;

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
                var successRect = new XRect(50, yPosition, page.Width - 100, 50);
                DrawRoundedRectangle(gfx, successRect, 6, new XSolidBrush(SuccessLight), new XPen(SuccessGreen, 1));
                
                var successText = "Document validation completed successfully with no issues!";
                DrawWrappedText(gfx, successText, new XFont("Segoe UI", 13, XFontStyle.Bold), new XSolidBrush(SuccessGreen),
                    new XRect(65, yPosition + 16, page.Width - 130, 30), 65);
                yPosition += 60;
            }

            // Modern footer with flat background
            var footerRect = new XRect(0, page.Height - 50, page.Width, 50);
            gfx.DrawRectangle(new XSolidBrush(BackgroundGray), footerRect);
            gfx.DrawLine(new XPen(BorderGray, 1), 0, page.Height - 50, page.Width, page.Height - 50);
            
            var footerText = $"This report was automatically generated • © {DateTime.Now.Year} Document Validator";
            gfx.DrawString(footerText, new XFont("Segoe UI", 9), new XSolidBrush(TextMuted),
                new XRect(0, page.Height - 30, page.Width, 20), XStringFormats.TopCenter);
        }

        private void AddSkippedDocumentsPage(XGraphics gfx, PdfPage page, List<SkippedDocument> skippedDocuments, string organizationName)
        {
            var font = new XFont("Segoe UI", 11);
            var fontBold = new XFont("Segoe UI", 12, XFontStyle.Bold);
            var fontLarge = new XFont("Segoe UI", 20, XFontStyle.Bold);
            var fontMedium = new XFont("Segoe UI", 15, XFontStyle.Bold);

            var yPosition = 40;

            // Modern flat header with reduced height
            var headerRect = new XRect(0, 0, page.Width, 80); // Reduced from 110 to 80
            gfx.DrawRectangle(new XSolidBrush(WarningOrange), headerRect);

            // Add subtle drop shadow
            var shadowRect = new XRect(0, 80, page.Width, 6); // Updated Y position
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(40, 0, 0, 0)), shadowRect);

            gfx.DrawString("Uncategorized Documents", fontLarge, XBrushes.White,
                new XRect(0, yPosition, page.Width, 35), XStringFormats.TopCenter);
            yPosition += 60; // Reduced spacing for shorter header

            // Modern explanation section with flat card design
            var explanationRect = new XRect(50, yPosition, page.Width - 100, 60);
            DrawRoundedRectangle(gfx, explanationRect, 8, new XSolidBrush(WarningLight), new XPen(WarningOrange, 1));

            var explanationText = "The following documents could not be automatically classified. These documents require manual review to determine their type and verify compliance.";
            DrawWrappedText(gfx, explanationText, font, new XSolidBrush(XColor.FromArgb(146, 64, 14)),
                new XRect(65, yPosition + 15, page.Width - 130, 45), 85);
            yPosition += 80;

            // Enhanced table layout with modern proportions
            var pageWidth = page.Width - 100;
            var startX = 50;

            var columnWidths = new double[]
            {
                0.70 * pageWidth, // Document Name
                0.30 * pageWidth  // Reason Skipped
            };

            var headers = new[] { "Document Name", "Reason Skipped" };

            // Modern flat table header
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

            // Modern table rows with proper text wrapping to prevent truncation
            for (int index = 0; index < skippedDocuments.Count; index++)
            {
                var document = skippedDocuments[index];
                
                // Calculate row height to accommodate full text without truncation
                var nameLines = WrapTextToWidth(gfx, document.FileName, font, columnWidths[0] - 24);
                var reasonLines = WrapTextToWidth(gfx, document.Reason, font, columnWidths[1] - 24);
                var maxLines = Math.Max(nameLines.Count, reasonLines.Count);
                
                // Calculate row height based on actual line count with proper padding
                var rowHeight = Math.Max(50, (int)(maxLines * font.Height + 20)); // Minimum 50px height, 20px padding

                // Modern flat alternating row colors
                var rowColor = index % 2 == 0 ? XColor.FromArgb(254, 252, 246) : XColors.White;
                
                var rowRect = new XRect(startX, yPosition, pageWidth, rowHeight);
                DrawRoundedRectangle(gfx, rowRect, 4, new XSolidBrush(rowColor), new XPen(BorderGray, 0.5));

                x = startX;

                // Document Name column - display full text with proper wrapping
                DrawWrappedTextToFit(gfx, document.FileName, font, new XSolidBrush(TextPrimary),
                    new XRect(x + 12, yPosition + 10, columnWidths[0] - 24, rowHeight - 20));
                
                gfx.DrawLine(new XPen(BorderGray, 0.5), 
                    x + (int)columnWidths[0], yPosition + 5, x + (int)columnWidths[0], yPosition + rowHeight - 5);
                x += (int)columnWidths[0];

                // Reason column - display full text with proper wrapping to prevent truncation
                DrawWrappedTextToFit(gfx, document.Reason, font, new XSolidBrush(XColor.FromArgb(146, 64, 14)),
                    new XRect(x + 12, yPosition + 10, columnWidths[1] - 24, rowHeight - 20));

                yPosition += rowHeight;

                // Check if we need a new page
                if (yPosition > page.Height - 120)
                {
                    break;
                }
            }

            // Modern note section with flat styling
            yPosition += 30;
            var noteRect = new XRect(50, yPosition, page.Width - 100, 60);
            DrawRoundedRectangle(gfx, noteRect, 8, new XSolidBrush(XColor.FromArgb(254, 240, 138)), new XPen(WarningOrange, 1));

            var noteText = "Note: These documents should be manually reviewed to determine their document type and verify compliance requirements.";
            DrawWrappedText(gfx, noteText, new XFont("Segoe UI", 12, XFontStyle.Bold), 
                new XSolidBrush(XColor.FromArgb(146, 64, 14)),
                new XRect(65, yPosition + 16, page.Width - 130, 40), 75);

            // Modern footer with flat background
            var footerRect = new XRect(0, page.Height - 50, page.Width, 50);
            gfx.DrawRectangle(new XSolidBrush(BackgroundGray), footerRect);
            gfx.DrawLine(new XPen(BorderGray, 1), 0, page.Height - 50, page.Width, page.Height - 50);
            
            var footerText = $"This report was automatically generated • © {DateTime.Now.Year} Document Validator";
            gfx.DrawString(footerText, new XFont("Segoe UI", 9), new XSolidBrush(TextMuted),
                new XRect(0, page.Height - 30, page.Width, 20), XStringFormats.TopCenter);
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
                    // First word on the line
                    currentLine = word;
                }
                else 
                {
                    var potentialLine = currentLine + " " + word;
                    var textSize = gfx.MeasureString(potentialLine, font);
                    
                    // If adding this word would exceed the width
                    if (textSize.Width > maxWidth)
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

        private void DrawRoundedRectangle(XGraphics gfx, XRect rect, int cornerRadius, XBrush? fillBrush, XPen? borderPen)
        {
            // Draw main rectangle (fill)
            if (fillBrush != null)
            {
                gfx.DrawRectangle(fillBrush, rect);
            }
            
            // Draw border if specified
            if (borderPen != null)
            {
                gfx.DrawRectangle(borderPen, rect);
            }
            
            // For now, we'll use regular rectangles since PdfSharp doesn't support rounded rectangles natively
            // In a future enhancement, we could implement proper rounded corners using path drawing
        }
    }
} 