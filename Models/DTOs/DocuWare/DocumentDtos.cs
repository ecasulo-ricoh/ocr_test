namespace OCR_test.Models.DTOs.DocuWare
{
    /// <summary>
    /// DTO para información de documento
    /// </summary>
    public class DocumentDto
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public long? FileSize { get; set; }
        public string? ContentType { get; set; }
        public List<DocumentFieldDto> Fields { get; set; } = new();
    }

    /// <summary>
    /// DTO para campos de documento
    /// </summary>
    public class DocumentFieldDto
    {
        public required string FieldName { get; set; }
        public string? DisplayName { get; set; }
        public required string Type { get; set; }
        public object? Value { get; set; }
        public bool IsSystemField { get; set; }
        public DateTime? LastModified { get; set; }
    }

    /// <summary>
    /// DTO para descarga de documento
    /// </summary>
    public class DocumentDownloadDto
    {
        public required Stream Content { get; set; }
        public required string ContentType { get; set; }
        public required string FileName { get; set; }
        public long? ContentLength { get; set; }
    }

    /// <summary>
    /// DTO para respuesta de operación
    /// </summary>
    public class OperationResultDto
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public object? Data { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// DTO para resultado de OCR
    /// </summary>
    public class OcrResultDto
    {
        public bool Success { get; set; }
        public string? ExtractedText { get; set; }
        public float Confidence { get; set; }
        public string? Language { get; set; }
        public string? Message { get; set; }
        public DateTime ProcessedAt { get; set; }
        public long ProcessingTimeMs { get; set; }
    }
}