namespace OCR_test.Models.DTOs.Invoice
{
    /// <summary>
    /// DTO para resultado de an�lisis de factura
    /// </summary>
    public class InvoiceAnalysisResultDto
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public InvoiceDataDto? InvoiceData { get; set; }
        public OcrMetadataDto? OcrMetadata { get; set; }
        public List<string> Warnings { get; set; } = new();
        public DateTime ProcessedAt { get; set; }
        public long ProcessingTimeMs { get; set; }
    }

    /// <summary>
    /// DTO para datos estructurados de factura
    /// </summary>
    public class InvoiceDataDto
    {
        // Informaci�n del emisor
        public CompanyInfoDto? Issuer { get; set; }
        
        // Informaci�n del receptor
        public CompanyInfoDto? Recipient { get; set; }
        
        // Datos de la factura
        public InvoiceHeaderDto? Header { get; set; }
        
        // L�neas de detalle
        public List<InvoiceLineDto> Lines { get; set; } = new();
        
        // Totales
        public InvoiceTotalsDto? Totals { get; set; }
        
        // C�digos y referencias
        public List<ExtractedCodeDto> Codes { get; set; } = new();
        
        // Fechas detectadas
        public List<ExtractedDateDto> Dates { get; set; } = new();
        
        // N�meros detectados
        public List<ExtractedNumberDto> Numbers { get; set; } = new();
    }

    /// <summary>
    /// DTO para informaci�n de empresa
    /// </summary>
    public class CompanyInfoDto
    {
        public string? Name { get; set; }
        public string? TaxId { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public float Confidence { get; set; }
    }

    /// <summary>
    /// DTO para encabezado de factura
    /// </summary>
    public class InvoiceHeaderDto
    {
        public string? InvoiceNumber { get; set; }
        public DateTime? InvoiceDate { get; set; }
        public DateTime? DueDate { get; set; }
        public string? Currency { get; set; }
        public string? PaymentTerms { get; set; }
        
        // *** ESPEC�FICO PARA FACTURAS ARGENTINAS ***
        /// <summary>
        /// Tipo de factura argentina: "A" o "B"
        /// </summary>
        public string? ArgentineInvoiceType { get; set; }
        
        /// <summary>
        /// C�digo de la factura argentina: "001" para tipo A, "006" para tipo B
        /// </summary>
        public string? ArgentineInvoiceCode { get; set; }
        
        /// <summary>
        /// Indica si se requiere actualizaci�n manual en DocuWare
        /// </summary>
        public bool RequiresManualUpdate { get; set; }
        
        public float Confidence { get; set; }
    }

    /// <summary>
    /// DTO para l�nea de factura
    /// </summary>
    public class InvoiceLineDto
    {
        public int LineNumber { get; set; }
        public string? Description { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? LineTotal { get; set; }
        public string? TaxRate { get; set; }
        public float Confidence { get; set; }
    }

    /// <summary>
    /// DTO para totales de factura
    /// </summary>
    public class InvoiceTotalsDto
    {
        public decimal? Subtotal { get; set; }
        public decimal? TaxAmount { get; set; }
        public decimal? Total { get; set; }
        public string? Currency { get; set; }
        public float Confidence { get; set; }
    }

    /// <summary>
    /// DTO para c�digo extra�do
    /// </summary>
    public class ExtractedCodeDto
    {
        public required string Type { get; set; } // "InvoiceNumber", "TaxId", "ProductCode", etc.
        public required string Value { get; set; }
        public string? Pattern { get; set; }
        public float Confidence { get; set; }
        public TextLocationDto? Location { get; set; }
    }

    /// <summary>
    /// DTO para fecha extra�da
    /// </summary>
    public class ExtractedDateDto
    {
        public required string Type { get; set; } // "InvoiceDate", "DueDate", etc.
        public DateTime? ParsedDate { get; set; }
        public required string OriginalText { get; set; }
        public string? Format { get; set; }
        public float Confidence { get; set; }
        public TextLocationDto? Location { get; set; }
    }

    /// <summary>
    /// DTO para n�mero extra�do
    /// </summary>
    public class ExtractedNumberDto
    {
        public required string Type { get; set; } // "Amount", "Quantity", "Percentage", etc.
        public decimal? ParsedValue { get; set; }
        public required string OriginalText { get; set; }
        public string? Currency { get; set; }
        public float Confidence { get; set; }
        public TextLocationDto? Location { get; set; }
    }

    /// <summary>
    /// DTO para ubicaci�n de texto
    /// </summary>
    public class TextLocationDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int PageNumber { get; set; }
    }

    /// <summary>
    /// DTO para metadatos de OCR
    /// </summary>
    public class OcrMetadataDto
    {
        public string? ExtractedText { get; set; }
        public float OverallConfidence { get; set; }
        public string? Language { get; set; }
        public int PageCount { get; set; }
        public List<PageAnalysisDto> Pages { get; set; } = new();
    }

    /// <summary>
    /// DTO para an�lisis por p�gina
    /// </summary>
    public class PageAnalysisDto
    {
        public int PageNumber { get; set; }
        public string? Text { get; set; }
        public float Confidence { get; set; }
        public int WordCount { get; set; }
        public List<DetectedFieldDto> DetectedFields { get; set; } = new();
    }

    /// <summary>
    /// DTO para campo detectado
    /// </summary>
    public class DetectedFieldDto
    {
        public required string FieldType { get; set; }
        public required string Value { get; set; }
        public float Confidence { get; set; }
        public TextLocationDto? Location { get; set; }
    }
}