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

                // Modern header with sophisticated gradient - increased height
                var headerRect = new XRect(0, 0, page.Width, 160); // Increased from 140 to 160
                var headerBrush = new XLinearGradientBrush(
                    new XPoint(0, 0), new XPoint(0, 160), // Updated to match new height
                    PrimaryBlue, PrimaryDark);
                gfx.DrawRectangle(headerBrush, headerRect);

                // Add subtle shadow effect to header
                var shadowRect = new XRect(0, 160, page.Width, 8); // Updated Y position
                var shadowBrush = new XLinearGradientBrush(
                    new XPoint(0, 160), new XPoint(0, 168), // Updated positions
                    XColor.FromArgb(100, 0, 0, 0), XColor.FromArgb(0, 0, 0, 0));
                gfx.DrawRectangle(shadowBrush, shadowRect);

                // Modern title with better spacing
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
                yPosition += 50; // Increased spacing to account for taller header

                // Modern summary section with enhanced card design
                var summaryCardRect = new XRect(50, yPosition, page.Width - 100, 45); // More padding
                var summaryBrush = new XLinearGradientBrush(
                    new XPoint(0, yPosition), new XPoint(0, yPosition + 45),
                    XColors.White, AccentGray);
                gfx.DrawRectangle(summaryBrush, summaryCardRect);
                
                // Subtle border with rounded corner effect
                gfx.DrawRectangle(new XPen(BorderGray, 2), summaryCardRect);
                
                // Add subtle inner shadow
                var innerShadowRect = new XRect(52, yPosition + 2, page.Width - 104, 41);
                gfx.DrawRectangle(new XPen(XColor.FromArgb(30, 0, 0, 0), 1), innerShadowRect);

                gfx.DrawString("Validation Summary", new XFont("Segoe UI", 15, XFontStyle.Bold), 
                    new XSolidBrush(PrimaryDark),
                    new XRect(65, yPosition + 12, page.Width - 130, 25), XStringFormats.TopLeft);
                yPosition += 65;

                // Enhanced table layout with better proportions
                var pageWidth = page.Width - 100; // Increased margins
                var startX = 50;

                var columnWidths = new double[]
                {
                    0.38 * pageWidth, // Document Name
                    0.36 * pageWidth, // Document Type  
                    0.14 * pageWidth, // Status
                    0.12 * pageWidth  // Issues
                };

                var headers = new[] { "Document Name", "Document Type", "Status", "Issues" };

                // Modern table header with sophisticated styling
                var headerTableRect = new XRect(startX, yPosition, pageWidth, 35); // Increased height
                var tableBrush = new XLinearGradientBrush(
                    new XPoint(0, yPosition), new XPoint(0, yPosition + 35),
                    PrimaryBlue, PrimaryDark);
                gfx.DrawRectangle(tableBrush, headerTableRect);

                var x = startX;
                for (int i = 0; i < headers.Length; i++)
                {
                    // Modern vertical separators with transparency
                    if (i > 0)
                    {
                        gfx.DrawLine(new XPen(XColor.FromArgb(120, 255, 255, 255), 1.5), 
                            x, yPosition + 5, x, yPosition + 30);
                    }
                    
                    gfx.DrawString(headers[i], new XFont("Segoe UI", 11, XFontStyle.Bold), XBrushes.White,
                        new XRect(x + 12, yPosition + 10, columnWidths[i] - 24, 20), XStringFormats.TopLeft);
                    x += (int)columnWidths[i];
                }
                yPosition += 35;

                // Draw enhanced table rows with modern styling and proper text wrapping
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

                    // Modern alternating row colors with subtle gradients
                    var rowColor1 = index % 2 == 0 ? XColor.FromArgb(252, 253, 254) : XColors.White;
                    var rowColor2 = index % 2 == 0 ? BackgroundGray : XColor.FromArgb(254, 254, 254);
                    
                    var rowRect = new XRect(startX, yPosition, pageWidth, rowHeight);
                    var rowBrush = new XLinearGradientBrush(
                        new XPoint(0, yPosition), new XPoint(0, yPosition + rowHeight),
                        rowColor1, rowColor2);
                    gfx.DrawRectangle(rowBrush, rowRect);
                    gfx.DrawRectangle(new XPen(BorderGray, 0.5), rowRect);

                    x = startX;

                    // Document Name column - display full text with proper wrapping
                    DrawWrappedTextToFit(gfx, result.FileName, font, new XSolidBrush(TextPrimary),
                        new XRect(x + 12, yPosition + 10, columnWidths[0] - 24, rowHeight - 20));
                    
                    // Modern vertical separator
                    gfx.DrawLine(new XPen(BorderGray, 0.5), 
                        x + (int)columnWidths[0], yPosition, x + (int)columnWidths[0], yPosition + rowHeight);
                    x += (int)columnWidths[0];

                    // Document Type column - display full text with proper wrapping
                    DrawWrappedTextToFit(gfx, documentTypeName, font, new XSolidBrush(TextPrimary),
                        new XRect(x + 12, yPosition + 10, columnWidths[1] - 24, rowHeight - 20));
                    
                    gfx.DrawLine(new XPen(BorderGray, 0.5), 
                        x + (int)columnWidths[1], yPosition, x + (int)columnWidths[1], yPosition + rowHeight);
                    x += (int)columnWidths[1];

                    // Modern Status column with enhanced badges
                    var statusColor = result.Success ? SuccessGreen : ErrorRed;
                    var statusBgColor = result.Success ? SuccessLight : ErrorLight;
                    var statusText = result.Success ? "PASSED" : "FAILED";
                    var statusBorderColor = result.Success ? SuccessGreen : ErrorRed;
                    
                    // Draw modern status badge with rounded corners effect
                    var statusBadgeRect = new XRect(x + 8, yPosition + 10, columnWidths[2] - 16, 22);
                    gfx.DrawRectangle(new XSolidBrush(statusBgColor), statusBadgeRect);
                    gfx.DrawRectangle(new XPen(statusBorderColor, 1.5), statusBadgeRect);
                    
                    // Add subtle inner highlight
                    var statusHighlight = new XRect(x + 9, yPosition + 11, columnWidths[2] - 18, 1);
                    gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(50, 255, 255, 255)), statusHighlight);
                    
                    // Center align text both horizontally and vertically within the badge
                    gfx.DrawString(statusText, new XFont("Segoe UI", 9, XFontStyle.Bold), new XSolidBrush(statusColor),
                        statusBadgeRect, XStringFormats.Center);
                    
                    gfx.DrawLine(new XPen(BorderGray, 0.5), 
                        x + (int)columnWidths[2], yPosition, x + (int)columnWidths[2], yPosition + rowHeight);
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

                // Modern information section
                yPosition += 30;
                var infoRect = new XRect(50, yPosition, page.Width - 100, 45);
                var infoBrush = new XLinearGradientBrush(
                    new XPoint(0, yPosition), new XPoint(0, yPosition + 45),
                    WarningLight, XColor.FromArgb(252, 211, 77));
                gfx.DrawRectangle(infoBrush, infoRect);
                gfx.DrawRectangle(new XPen(WarningOrange, 2), infoRect);

                // Add modern accent line
                gfx.DrawRectangle(new XSolidBrush(WarningOrange), 
                    new XRect(50, yPosition, page.Width - 100, 4));

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
            var headerBrush = new XLinearGradientBrush(
                new XPoint(0, 0), new XPoint(0, 80), // Updated to match new height
                PrimaryBlue, PrimaryDark);
            gfx.DrawRectangle(headerBrush, headerRect);

            // Add subtle shadow
            var shadowRect = new XRect(0, 80, page.Width, 6); // Updated Y position
            var shadowBrush = new XLinearGradientBrush(
                new XPoint(0, 80), new XPoint(0, 86), // Updated positions
                XColor.FromArgb(80, 0, 0, 0), XColor.FromArgb(0, 0, 0, 0));
            gfx.DrawRectangle(shadowBrush, shadowRect);

            // Display document type as the header instead of generic title
            var documentTypeTitle = FormatDocumentType(result.DocumentType);
            gfx.DrawString(documentTypeTitle, fontLarge, XBrushes.White,
                new XRect(0, yPosition, page.Width, 35), XStringFormats.TopCenter);
            yPosition += 60; // Reduced spacing to account for shorter header

            // Modern section styling
            var sectionHeaderStyle = new XFont("Segoe UI", 14, XFontStyle.Bold); // Increased from 13
            var sectionBgBrush = new XLinearGradientBrush(
                new XPoint(0, 0), new XPoint(0, 30),
                AccentGray, BackgroundGray);
            var sectionBorderColor = new XPen(BorderGray, 1.5); // Increased border width

            // Document Details section with modern card design
            var detailsRect = new XRect(50, yPosition, page.Width - 100, 30); // Increased margins
            gfx.DrawRectangle(sectionBgBrush, detailsRect);
            gfx.DrawRectangle(sectionBorderColor, detailsRect);
            
            // Add subtle accent line
            gfx.DrawRectangle(new XSolidBrush(PrimaryBlue), 
                new XRect(50, yPosition, page.Width - 100, 3));
            
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
                yPosition += (int)itemHeight + 5; // Increased spacing
            }
            yPosition += 20;

            // Modern Validation Summary section
            var summaryRect = new XRect(50, yPosition, page.Width - 100, 30);
            gfx.DrawRectangle(sectionBgBrush, summaryRect);
            gfx.DrawRectangle(sectionBorderColor, summaryRect);
            
            gfx.DrawRectangle(new XSolidBrush(PrimaryBlue), 
                new XRect(50, yPosition, page.Width - 100, 3));
            
            gfx.DrawString("Validation Result", sectionHeaderStyle, new XSolidBrush(PrimaryDark),
                new XRect(65, yPosition + 6, page.Width - 130, 25), XStringFormats.TopLeft);
            yPosition += 40;

            // Modern overall result with background gradient box
            var resultColor = result.Success ? SuccessGreen : ErrorRed;
            var resultBgColor = result.Success ? SuccessLight : ErrorLight;
            var resultText = result.Success ? "Passed" : "Failed";
            
            // Create background gradient box for the result
            var resultBadgeRect = new XRect(65, yPosition, 160, 35);
            var resultBadgeBrush = new XLinearGradientBrush(
                new XPoint(0, yPosition), new XPoint(0, yPosition + 35),
                resultBgColor, result.Success ? XColor.FromArgb(187, 247, 208) : XColor.FromArgb(252, 165, 165));
            gfx.DrawRectangle(resultBadgeBrush, resultBadgeRect);
            gfx.DrawRectangle(new XPen(resultColor, 2), resultBadgeRect);
            
            // Add subtle highlight effect
            var highlightRect = new XRect(67, yPosition + 2, 156, 2);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(60, 255, 255, 255)), highlightRect);
            
            // Center align text both horizontally and vertically within the box
            gfx.DrawString(resultText, new XFont("Segoe UI", 14, XFontStyle.Bold), new XSolidBrush(resultColor),
                resultBadgeRect, XStringFormats.Center);
            yPosition += 50;

            // Modern Document Statistics section
            var statsRect = new XRect(50, yPosition, page.Width - 100, 30);
            gfx.DrawRectangle(sectionBgBrush, statsRect);
            gfx.DrawRectangle(sectionBorderColor, statsRect);
            
            gfx.DrawRectangle(new XSolidBrush(PrimaryBlue), 
                new XRect(50, yPosition, page.Width - 100, 3));
            
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

            // Modern Issues or Success section
            if (!result.Success)
            {
                var issuesRect = new XRect(50, yPosition, page.Width - 100, 30);
                var issuesBrush = new XLinearGradientBrush(
                    new XPoint(0, yPosition), new XPoint(0, yPosition + 30),
                    ErrorLight, XColor.FromArgb(254, 202, 202));
                gfx.DrawRectangle(issuesBrush, issuesRect);
                gfx.DrawRectangle(new XPen(ErrorRed, 1.5), issuesRect);
                
                gfx.DrawRectangle(new XSolidBrush(ErrorRed), 
                    new XRect(50, yPosition, page.Width - 100, 3));
                
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
                    var actionsBrush = new XLinearGradientBrush(
                        new XPoint(0, yPosition), new XPoint(0, yPosition + 30),
                        WarningLight, XColor.FromArgb(252, 211, 77));
                    gfx.DrawRectangle(actionsBrush, actionsRect);
                    gfx.DrawRectangle(new XPen(WarningOrange, 1.5), actionsRect);
                    
                    gfx.DrawRectangle(new XSolidBrush(WarningOrange), 
                        new XRect(50, yPosition, page.Width - 100, 3));
                    
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
                var successBrush = new XLinearGradientBrush(
                    new XPoint(0, yPosition), new XPoint(0, yPosition + 50),
                    SuccessLight, XColor.FromArgb(187, 247, 208));
                gfx.DrawRectangle(successBrush, successRect);
                gfx.DrawRectangle(new XPen(SuccessGreen, 2), successRect);
                
                // Add modern accent and highlight
                gfx.DrawRectangle(new XSolidBrush(SuccessGreen), 
                    new XRect(50, yPosition, page.Width - 100, 4));
                var successHighlight = new XRect(52, yPosition + 6, page.Width - 104, 2);
                gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(80, 255, 255, 255)), successHighlight);
                
                var successText = "Document validation completed successfully with no issues!";
                DrawWrappedText(gfx, successText, new XFont("Segoe UI", 13, XFontStyle.Bold), new XSolidBrush(SuccessGreen),
                    new XRect(65, yPosition + 16, page.Width - 130, 30), 65);
                yPosition += 60;
            }

            // Modern footer with gradient background
            var footerRect = new XRect(0, page.Height - 50, page.Width, 50);
            var footerBrush = new XLinearGradientBrush(
                new XPoint(0, page.Height - 50), new XPoint(0, page.Height),
                BackgroundGray, AccentGray);
            gfx.DrawRectangle(footerBrush, footerRect);
            gfx.DrawLine(new XPen(BorderGray, 1.5), 0, page.Height - 50, page.Width, page.Height - 50);
            
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

            // Modern header with sophisticated gradient - increased height for consistency
            var headerRect = new XRect(0, 0, page.Width, 110); // Increased from 90 to 110
            var headerBrush = new XLinearGradientBrush(
                new XPoint(0, 0), new XPoint(0, 110), // Updated to match new height
                WarningOrange, XColor.FromArgb(252, 211, 77));
            gfx.DrawRectangle(headerBrush, headerRect);

            // Add subtle shadow
            var shadowRect = new XRect(0, 110, page.Width, 6); // Updated Y position
            var shadowBrush = new XLinearGradientBrush(
                new XPoint(0, 110), new XPoint(0, 116), // Updated positions
                XColor.FromArgb(80, 0, 0, 0), XColor.FromArgb(0, 0, 0, 0));
            gfx.DrawRectangle(shadowBrush, shadowRect);

            gfx.DrawString("Uncategorized Documents", fontLarge, XBrushes.White,
                new XRect(0, yPosition, page.Width, 35), XStringFormats.TopCenter);
            yPosition += 80; // Increased spacing to account for taller header

            // Modern explanation section with card design
            var explanationRect = new XRect(50, yPosition, page.Width - 100, 60);
            var explanationBrush = new XLinearGradientBrush(
                new XPoint(0, yPosition), new XPoint(0, yPosition + 60),
                WarningLight, XColor.FromArgb(252, 211, 77));
            gfx.DrawRectangle(explanationBrush, explanationRect);
            gfx.DrawRectangle(new XPen(WarningOrange, 2), explanationRect);

            // Add modern accent line
            gfx.DrawRectangle(new XSolidBrush(WarningOrange), 
                new XRect(50, yPosition, page.Width - 100, 4));

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

            // Modern table header with sophisticated styling
            var headerTableRect = new XRect(startX, yPosition, pageWidth, 35);
            var tableBrush = new XLinearGradientBrush(
                new XPoint(0, yPosition), new XPoint(0, yPosition + 35),
                WarningOrange, XColor.FromArgb(252, 211, 77));
            gfx.DrawRectangle(tableBrush, headerTableRect);
            
            // Subtle top border for premium look
            gfx.DrawRectangle(new XPen(XColor.FromArgb(217, 119, 6), 3), 
                new XRect(startX, yPosition, pageWidth, 3));

            var x = startX;
            for (int i = 0; i < headers.Length; i++)
            {
                if (i > 0)
                {
                    gfx.DrawLine(new XPen(XColor.FromArgb(150, 255, 255, 255), 1.5), 
                        x, yPosition + 5, x, yPosition + 30);
                }
                
                gfx.DrawString(headers[i], new XFont("Segoe UI", 12, XFontStyle.Bold), XBrushes.White,
                    new XRect(x + 12, yPosition + 10, columnWidths[i] - 24, 20), XStringFormats.TopLeft);
                x += (int)columnWidths[i];
            }
            yPosition += 35;

            // Modern table rows with enhanced styling
            for (int index = 0; index < skippedDocuments.Count; index++)
            {
                var document = skippedDocuments[index];
                
                var rowHeight = CalculateRequiredRowHeight(document.FileName, document.Reason, 55, 35, font.Height);

                // Modern alternating row colors with subtle gradients
                var rowColor1 = index % 2 == 0 ? XColor.FromArgb(254, 252, 246) : XColors.White;
                var rowColor2 = index % 2 == 0 ? XColor.FromArgb(252, 248, 227) : XColor.FromArgb(254, 254, 254);
                
                var rowRect = new XRect(startX, yPosition, pageWidth, rowHeight);
                var rowBrush = new XLinearGradientBrush(
                    new XPoint(0, yPosition), new XPoint(0, yPosition + rowHeight),
                    rowColor1, rowColor2);
                gfx.DrawRectangle(rowBrush, rowRect);
                gfx.DrawRectangle(new XPen(BorderGray, 0.5), rowRect);

                x = startX;

                // Document Name column with modern styling
                DrawWrappedText(gfx, document.FileName, font, new XSolidBrush(TextPrimary),
                    new XRect(x + 12, yPosition + 8, columnWidths[0] - 24, rowHeight - 16), 55);
                
                gfx.DrawLine(new XPen(BorderGray, 0.5), 
                    x + (int)columnWidths[0], yPosition, x + (int)columnWidths[0], yPosition + rowHeight);
                x += (int)columnWidths[0];

                // Reason column with modern styling
                DrawWrappedText(gfx, document.Reason, font, new XSolidBrush(XColor.FromArgb(146, 64, 14)),
                    new XRect(x + 12, yPosition + 8, columnWidths[1] - 24, rowHeight - 16), 35);

                yPosition += rowHeight;

                // Check if we need a new page
                if (yPosition > page.Height - 120)
                {
                    break;
                }
            }

            // Modern note section with enhanced styling
            yPosition += 30;
            var noteRect = new XRect(50, yPosition, page.Width - 100, 60);
            var noteBrush = new XLinearGradientBrush(
                new XPoint(0, yPosition), new XPoint(0, yPosition + 60),
                XColor.FromArgb(254, 240, 138), XColor.FromArgb(253, 224, 71));
            gfx.DrawRectangle(noteBrush, noteRect);
            gfx.DrawRectangle(new XPen(WarningOrange, 2), noteRect);

            // Add modern accent and highlight
            gfx.DrawRectangle(new XSolidBrush(WarningOrange), 
                new XRect(50, yPosition, page.Width - 100, 4));
            var noteHighlight = new XRect(52, yPosition + 6, page.Width - 104, 2);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(60, 255, 255, 255)), noteHighlight);

            var noteText = "Note: These documents should be manually reviewed to determine their document type and verify compliance requirements.";
            DrawWrappedText(gfx, noteText, new XFont("Segoe UI", 12, XFontStyle.Bold), 
                new XSolidBrush(XColor.FromArgb(146, 64, 14)),
                new XRect(65, yPosition + 16, page.Width - 130, 40), 75);

            // Modern footer with gradient background
            var footerRect = new XRect(0, page.Height - 50, page.Width, 50);
            var footerBrush = new XLinearGradientBrush(
                new XPoint(0, page.Height - 50), new XPoint(0, page.Height),
                BackgroundGray, AccentGray);
            gfx.DrawRectangle(footerBrush, footerRect);
            gfx.DrawLine(new XPen(BorderGray, 1.5), 0, page.Height - 50, page.Width, page.Height - 50);
            
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
    }
} 