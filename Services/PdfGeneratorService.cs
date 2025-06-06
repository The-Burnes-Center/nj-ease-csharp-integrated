using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using DocumentValidator.Models;
using System.Text;
using System.Linq;

namespace DocumentValidator.Services
{
    public class PdfGeneratorService
    {
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
                var font = new XFont("Arial", 12);
                var fontBold = new XFont("Arial", 12, XFontStyle.Bold);
                var fontLarge = new XFont("Arial", 18, XFontStyle.Bold);

                var yPosition = 50;

                // Add report header
                gfx.DrawString("NJ EASE Document Validation Report", fontLarge, XBrushes.Black,
                    new XRect(0, yPosition, page.Width, 30), XStringFormats.TopCenter);
                yPosition += 40;

                gfx.DrawString($"Organization Name: {organizationName}", font, XBrushes.Black,
                    new XRect(0, yPosition, page.Width, 20), XStringFormats.TopCenter);
                yPosition += 20;

                gfx.DrawString($"Date: {DateTime.Now:d}", font, XBrushes.Black,
                    new XRect(0, yPosition, page.Width, 20), XStringFormats.TopCenter);
                yPosition += 40;

                // Add summary table header
                gfx.DrawString("Validation Summary", fontBold, XBrushes.Black,
                    new XRect(50, yPosition, page.Width - 100, 20), XStringFormats.TopLeft);
                yPosition += 30;

                // Calculate table layout
                var pageWidth = page.Width - 100;
                var startX = 50;

                var columnWidths = new double[]
                {
                    0.35 * pageWidth, // Document Name
                    0.35 * pageWidth, // Document Type
                    0.15 * pageWidth, // Status
                    0.15 * pageWidth  // Issues
                };

                var headers = new[] { "Document Name", "Document Type", "Status", "Issues" };

                // Draw table header
                var headerRect = new XRect(startX, yPosition, pageWidth, 20);
                gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(224, 224, 224)), headerRect);

                var x = startX;
                for (int i = 0; i < headers.Length; i++)
                {
                    gfx.DrawString(headers[i], fontBold, XBrushes.Black,
                        new XRect(x + 5, yPosition + 5, columnWidths[i] - 10, 15), XStringFormats.TopLeft);
                    x += (int)columnWidths[i];
                }
                yPosition += 20;

                // Draw table rows
                for (int index = 0; index < validResults.Count; index++)
                {
                    var result = validResults[index];
                    var documentTypeName = FormatDocumentType(result.DocumentType);
                    
                    // Calculate dynamic row height based on text wrapping needs
                    var rowHeight = CalculateRequiredRowHeight(result.FileName, documentTypeName, 30, 20, font.Height);

                    // Draw alternating row colors
                    var rowColor = index % 2 == 0 ? XColor.FromArgb(249, 249, 249) : XColors.White;
                    var rowRect = new XRect(startX, yPosition, pageWidth, rowHeight);
                    gfx.DrawRectangle(new XSolidBrush(rowColor), rowRect);

                    x = startX;

                    // Document Name column - use word wrapping instead of truncation
                    DrawWrappedText(gfx, result.FileName, font, XBrushes.Black,
                        new XRect(x + 5, yPosition + 5, columnWidths[0] - 10, rowHeight - 10), 30);
                    x += (int)columnWidths[0];

                    // Document Type column - use word wrapping instead of truncation
                    DrawWrappedText(gfx, documentTypeName, font, XBrushes.Black,
                        new XRect(x + 5, yPosition + 5, columnWidths[1] - 10, rowHeight - 10), 20);
                    x += (int)columnWidths[1];

                    // Status column
                    var statusColor = result.Success ? XColors.Green : XColors.Red;
                    var statusText = result.Success ? "PASSED" : "FAILED";
                    gfx.DrawString(statusText, fontBold, new XSolidBrush(statusColor),
                        new XRect(x + 5, yPosition + 5, columnWidths[2] - 10, rowHeight - 10), XStringFormats.TopLeft);
                    x += (int)columnWidths[2];

                    // Issues column
                    var issuesText = result.MissingElements.Count > 0
                        ? $"{result.MissingElements.Count} issue{(result.MissingElements.Count > 1 ? "s" : "")}"
                        : "None";
                    gfx.DrawString(issuesText, font, XBrushes.Black,
                        new XRect(x + 5, yPosition + 5, columnWidths[3] - 10, rowHeight - 10), XStringFormats.TopLeft);

                    yPosition += rowHeight;

                    // Check if we need a new page
                    if (yPosition > page.Height - 100)
                    {
                        page = document.AddPage();
                        gfx = XGraphics.FromPdfPage(page);
                        yPosition = 50;
                    }
                }

                // Add information about detailed pages
                yPosition += 20;
                var infoText = "Detailed validation results for each document are provided on the following pages.";
                gfx.DrawString(infoText, font, XBrushes.Black,
                    new XRect(0, yPosition, page.Width, 20), XStringFormats.TopCenter);

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

            var font = new XFont("Arial", 10);
            var fontBold = new XFont("Arial", 12, XFontStyle.Bold);
            var fontLarge = new XFont("Arial", 16, XFontStyle.Bold);

            var yPosition = 50;

            // Add header
            gfx.DrawString("Document Validation Details", fontLarge, XBrushes.Black,
                new XRect(0, yPosition, page.Width, 30), XStringFormats.TopCenter);
            yPosition += 40;

            // Add document and organization info
            gfx.DrawString("Document Details:", fontBold, XBrushes.Black,
                new XRect(50, yPosition, page.Width - 100, 20), XStringFormats.TopLeft);
            yPosition += 20;

            gfx.DrawString($"Document Name: {result.FileName}", font, XBrushes.Black,
                new XRect(50, yPosition, page.Width - 100, 15), XStringFormats.TopLeft);
            yPosition += 15;

            gfx.DrawString($"Organization Name: {organizationName}", font, XBrushes.Black,
                new XRect(50, yPosition, page.Width - 100, 15), XStringFormats.TopLeft);
            yPosition += 15;

            gfx.DrawString($"Document Type: {FormatDocumentType(result.DocumentType)}", font, XBrushes.Black,
                new XRect(50, yPosition, page.Width - 100, 15), XStringFormats.TopLeft);
            yPosition += 25;

            // Add validation result summary
            gfx.DrawString("Validation Summary:", fontBold, XBrushes.Black,
                new XRect(50, yPosition, page.Width - 100, 20), XStringFormats.TopLeft);
            yPosition += 20;

            // Overall result
            var resultColor = result.Success ? XColors.Green : XColors.Red;
            var resultText = $"Overall Result: {(result.Success ? "PASSED" : "FAILED")}";
            gfx.DrawString(resultText, fontBold, new XSolidBrush(resultColor),
                new XRect(50, yPosition, page.Width - 100, 15), XStringFormats.TopLeft);
            yPosition += 25;

            // Document statistics
            gfx.DrawString("Document Statistics:", fontBold, XBrushes.Black,
                new XRect(50, yPosition, page.Width - 100, 20), XStringFormats.TopLeft);
            yPosition += 20;

            gfx.DrawString($"Page Count: {result.DocumentInfo.PageCount}", font, XBrushes.Black,
                new XRect(50, yPosition, page.Width - 100, 15), XStringFormats.TopLeft);
            yPosition += 15;

            gfx.DrawString($"Word Count: {result.DocumentInfo.WordCount}", font, XBrushes.Black,
                new XRect(50, yPosition, page.Width - 100, 15), XStringFormats.TopLeft);
            yPosition += 15;

            gfx.DrawString($"Contains Handwriting: {(result.DocumentInfo.ContainsHandwriting ? "Yes" : "No")}", font, XBrushes.Black,
                new XRect(50, yPosition, page.Width - 100, 15), XStringFormats.TopLeft);
            yPosition += 15;

            if (result.DocumentInfo.LanguageInfo.Any())
            {
                var langInfo = result.DocumentInfo.LanguageInfo.First();
                gfx.DrawString($"Primary Language: {langInfo.LanguageCode} (Confidence: {(langInfo.Confidence * 100):F1}%)", font, XBrushes.Black,
                    new XRect(50, yPosition, page.Width - 100, 15), XStringFormats.TopLeft);
                yPosition += 15;
            }

            if (!string.IsNullOrEmpty(result.DocumentInfo.DetectedOrganizationName))
            {
                gfx.DrawString($"Detected Organization Name: {result.DocumentInfo.DetectedOrganizationName}", font, XBrushes.Black,
                    new XRect(50, yPosition, page.Width - 100, 15), XStringFormats.TopLeft);
                yPosition += 15;
            }

            yPosition += 10;

            // Issues if any
            if (!result.Success)
            {
                gfx.DrawString("Issues Detected:", fontBold, XBrushes.Red,
                    new XRect(50, yPosition, page.Width - 100, 20), XStringFormats.TopLeft);
                yPosition += 20;

                for (int i = 0; i < result.MissingElements.Count; i++)
                {
                    gfx.DrawString($"{i + 1}. {result.MissingElements[i]}", font, XBrushes.Black,
                        new XRect(50, yPosition, page.Width - 100, 15), XStringFormats.TopLeft);
                    yPosition += 15;
                }

                yPosition += 10;

                if (result.SuggestedActions.Any())
                {
                    gfx.DrawString("Suggested Actions:", fontBold, XBrushes.Black,
                        new XRect(50, yPosition, page.Width - 100, 20), XStringFormats.TopLeft);
                    yPosition += 20;

                    for (int i = 0; i < result.SuggestedActions.Count; i++)
                    {
                        gfx.DrawString($"{i + 1}. {result.SuggestedActions[i]}", font, XBrushes.Black,
                            new XRect(50, yPosition, page.Width - 100, 15), XStringFormats.TopLeft);
                        yPosition += 15;
                    }
                }
            }
            else
            {
                gfx.DrawString("Document validation completed successfully with no issues.", fontBold, XBrushes.Green,
                    new XRect(50, yPosition, page.Width - 100, 20), XStringFormats.TopLeft);
                yPosition += 20;
            }

            // Add footer
            var footerText = $"This report was automatically generated. © {DateTime.Now.Year} Document Validator";
            gfx.DrawString(footerText, new XFont("Arial", 8), XBrushes.Black,
                new XRect(0, page.Height - 30, page.Width, 20), XStringFormats.TopCenter);
        }

        private void AddSkippedDocumentsPage(XGraphics gfx, PdfPage page, List<SkippedDocument> skippedDocuments, string organizationName)
        {
            var font = new XFont("Arial", 10);
            var fontBold = new XFont("Arial", 12, XFontStyle.Bold);
            var fontLarge = new XFont("Arial", 16, XFontStyle.Bold);

            var yPosition = 50;

            // Add header
            gfx.DrawString("Uncategorized Documents", fontLarge, XBrushes.Black,
                new XRect(0, yPosition, page.Width, 30), XStringFormats.TopCenter);
            yPosition += 40;

            // Add explanation
            var explanationText = "The following documents could not be automatically classified. These documents require manual review to determine their type and verify compliance.";
            gfx.DrawString(explanationText, font, XBrushes.Black,
                new XRect(50, yPosition, page.Width - 100, 40), XStringFormats.TopLeft);
            yPosition += 60;

            // Calculate table layout
            var pageWidth = page.Width - 100;
            var startX = 50;

            var columnWidths = new double[]
            {
                0.65 * pageWidth, // Document Name
                0.35 * pageWidth  // Reason Skipped
            };

            var headers = new[] { "Document Name", "Reason Skipped" };

            // Draw table header
            var headerRect = new XRect(startX, yPosition, pageWidth, 20);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(224, 224, 224)), headerRect);

            var x = startX;
            for (int i = 0; i < headers.Length; i++)
            {
                gfx.DrawString(headers[i], fontBold, XBrushes.Black,
                    new XRect(x + 5, yPosition + 5, columnWidths[i] - 10, 15), XStringFormats.TopLeft);
                x += (int)columnWidths[i];
            }
            yPosition += 20;

            // Draw table rows
            for (int index = 0; index < skippedDocuments.Count; index++)
            {
                var document = skippedDocuments[index];
                
                // Calculate dynamic row height based on text wrapping needs
                var rowHeight = CalculateRequiredRowHeight(document.FileName, document.Reason, 40, 30, font.Height);

                // Draw alternating row colors
                var rowColor = index % 2 == 0 ? XColor.FromArgb(249, 249, 249) : XColors.White;
                var rowRect = new XRect(startX, yPosition, pageWidth, rowHeight);
                gfx.DrawRectangle(new XSolidBrush(rowColor), rowRect);

                x = startX;

                // Document Name column - use word wrapping instead of truncation
                DrawWrappedText(gfx, document.FileName, font, XBrushes.Black,
                    new XRect(x + 5, yPosition + 5, columnWidths[0] - 10, rowHeight - 10), 40);
                x += (int)columnWidths[0];

                // Reason column - use word wrapping instead of truncation
                DrawWrappedText(gfx, document.Reason, font, XBrushes.Black,
                    new XRect(x + 5, yPosition + 5, columnWidths[1] - 10, rowHeight - 10), 30);

                yPosition += rowHeight;

                // Check if we need a new page
                if (yPosition > page.Height - 100)
                {
                    // This is a simplified version - in a full implementation you'd need to handle page breaks properly
                    break;
                }
            }

            // Add note about manual processing
            yPosition += 20;
            var noteText = "Note: These documents should be manually reviewed to determine their document type and verify compliance requirements.";
            gfx.DrawString(noteText, fontBold, new XSolidBrush(XColor.FromArgb(255, 102, 0)),
                new XRect(50, yPosition, page.Width - 100, 40), XStringFormats.TopLeft);
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

            var words = text.Split(' ');
            var lines = new List<string>();
            var currentLine = "";

            foreach (var word in words)
            {
                if (string.IsNullOrEmpty(currentLine))
                {
                    currentLine = word;
                }
                else if ((currentLine + " " + word).Length <= maxCharsPerLine)
                {
                    currentLine += " " + word;
                }
                else
                {
                    lines.Add(currentLine);
                    currentLine = word;
                }
            }

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
            return Math.Max((int)(maxLines * lineHeight + 10), 35); // Minimum height of 35
        }
    }
} 