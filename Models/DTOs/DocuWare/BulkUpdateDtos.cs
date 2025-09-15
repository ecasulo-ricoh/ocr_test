namespace OCR_test.Models.DTOs.DocuWare
{
    /// <summary>
    /// DTO para solicitud de actualizaci�n masiva de documentos
    /// </summary>
    public class BulkUpdateRequestDto
    {
        /// <summary>
        /// Cantidad de documentos a procesar (empezando desde el m�s reciente)
        /// </summary>
        public int DocumentCount { get; set; }

        /// <summary>
        /// Modo dry-run: si es true, solo simula los cambios sin aplicarlos
        /// </summary>
        public bool DryRun { get; set; } = true;

        /// <summary>
        /// Si es true, solo actualiza campos que est�n vac�os en DocuWare (por defecto: true)
        /// Si es false, sobrescribe todos los campos detectados por OCR
        /// </summary>
        public bool OnlyUpdateEmptyFields { get; set; } = true;
    }

    /// <summary>
    /// DTO interno para solicitud de actualizaci�n masiva con todos los par�metros
    /// </summary>
    public class BulkUpdateInternalRequestDto
    {
        /// <summary>
        /// Cantidad de documentos a procesar (empezando desde el m�s reciente)
        /// </summary>
        public int DocumentCount { get; set; }

        /// <summary>
        /// Modo dry-run: si es true, solo simula los cambios sin aplicarlos
        /// </summary>
        public bool DryRun { get; set; } = true;

        /// <summary>
        /// Si es true, solo actualiza campos que est�n vac�os en DocuWare
        /// </summary>
        public bool OnlyUpdateEmptyFields { get; set; } = true;

        /// <summary>
        /// FileCabinet ID espec�fico
        /// </summary>
        public string FileCabinetId { get; set; } = "";

        /// <summary>
        /// Idioma para OCR
        /// </summary>
        public string Language { get; set; } = "spa+eng";
    }

    /// <summary>
    /// DTO para resultado de actualizaci�n masiva
    /// </summary>
    public class BulkUpdateResultDto
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        
        /// <summary>
        /// Total de documentos procesados
        /// </summary>
        public int TotalProcessed { get; set; }
        
        /// <summary>
        /// Documentos actualizados exitosamente
        /// </summary>
        public int SuccessfulUpdates { get; set; }
        
        /// <summary>
        /// Documentos que fallaron en la actualizaci�n
        /// </summary>
        public int FailedUpdates { get; set; }
        
        /// <summary>
        /// Documentos omitidos (sin cambios necesarios)
        /// </summary>
        public int SkippedDocuments { get; set; }
        
        /// <summary>
        /// Detalles de cada documento procesado
        /// </summary>
        public List<DocumentUpdateDetailDto> Details { get; set; } = new();
        
        /// <summary>
        /// Resumen de errores encontrados
        /// </summary>
        public List<string> Errors { get; set; } = new();
        
        /// <summary>
        /// Informaci�n de procesamiento
        /// </summary>
        public BulkUpdateMetadataDto Metadata { get; set; } = new();
    }

    /// <summary>
    /// DTO para detalle de actualizaci�n de un documento espec�fico
    /// </summary>
    public class DocumentUpdateDetailDto
    {
        public int DocumentId { get; set; }
        public string Status { get; set; } = ""; // "Success", "Failed", "Skipped", "NoChanges"
        public string? Message { get; set; }
        
        /// <summary>
        /// Campos detectados por OCR
        /// </summary>
        public DocumentOcrFieldsDto? DetectedFields { get; set; }
        
        /// <summary>
        /// Campos que se actualizar�an/actualizaron en DocuWare
        /// </summary>
        public DocumentUpdateFieldsDto? UpdatedFields { get; set; }
        
        /// <summary>
        /// Campos que se omitieron por validaci�n
        /// </summary>
        public List<string> SkippedFields { get; set; } = new();
        
        /// <summary>
        /// Advertencias de validaci�n
        /// </summary>
        public List<string> ValidationWarnings { get; set; } = new();
        
        /// <summary>
        /// Tiempo de procesamiento en milisegundos
        /// </summary>
        public long ProcessingTimeMs { get; set; }
        
        /// <summary>
        /// Errores espec�ficos del documento
        /// </summary>
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// DTO para campos detectados por OCR
    /// </summary>
    public class DocumentOcrFieldsDto
    {
        public string? TipoFactura { get; set; }
        public string? CodigoFactura { get; set; }
        public string? NroFactura { get; set; }
        public string? FechaFactura { get; set; }
        public string? CuitCliente { get; set; }
        public float Confianza { get; set; }
    }

    /// <summary>
    /// DTO para campos que se actualizar�n en DocuWare
    /// </summary>
    public class DocumentUpdateFieldsDto
    {
        public string? LETRA_DOCUMENTO { get; set; }
        public string? CODIGO_DOCUMENTO { get; set; }
        public string? NDEG_FACTURA { get; set; }
        public string? DATE { get; set; }
        public string? CUIT_CLIENTE { get; set; }
        // RAZON_SOCIAL intencionalmente omitido seg�n los requerimientos
    }

    /// <summary>
    /// DTO para metadatos de actualizaci�n masiva
    /// </summary>
    public class BulkUpdateMetadataDto
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public long TotalProcessingTimeMs { get; set; }
        public string FileCabinetId { get; set; } = "";
        public bool DryRunMode { get; set; }
        public bool OnlyUpdateEmptyFields { get; set; }
        public int RequestedDocumentCount { get; set; }
        public string Language { get; set; } = "spa+eng"; // Valor por defecto
        
        /// <summary>
        /// Estad�sticas de rendimiento
        /// </summary>
        public PerformanceStatsDto Performance { get; set; } = new();
    }

    /// <summary>
    /// DTO para estad�sticas de rendimiento
    /// </summary>
    public class PerformanceStatsDto
    {
        public double AverageOcrTimeMs { get; set; }
        public double AverageUpdateTimeMs { get; set; }
        public double DocumentsPerSecond { get; set; }
        public long TotalOcrTimeMs { get; set; }
        public long TotalUpdateTimeMs { get; set; }
    }
}