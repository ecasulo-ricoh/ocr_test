namespace OCR_test.Services.Interfaces
{
    /// <summary>
    /// Servicio para logging especializado en archivos CSV para análisis de documentos problemáticos
    /// </summary>
    public interface ICsvLoggingService
    {
        /// <summary>
        /// Registra un documento con problemas en el CUIT (solo encontró 1 CUIT)
        /// </summary>
        Task LogCuitIssueAsync(int documentId, string foundCuit, string issueType, string details);

        /// <summary>
        /// Registra un documento con problemas de validación
        /// </summary>
        Task LogValidationIssueAsync(int documentId, string fieldName, string detectedValue, string validationError, string? expectedFormat = null);

        /// <summary>
        /// Registra un documento que falló completamente en el OCR
        /// </summary>
        Task LogOcrFailureAsync(int documentId, string errorMessage, string? ocrText = null);

        /// <summary>
        /// Registra un documento que falló en la actualización de DocuWare
        /// </summary>
        Task LogDocuWareUpdateFailureAsync(int documentId, string errorMessage, string? fieldsToUpdate = null);

        /// <summary>
        /// Registra estadísticas de un lote procesado
        /// </summary>
        Task LogBatchSummaryAsync(string batchId, int totalProcessed, int successful, int failed, int skipped, Dictionary<string, int> issueStats);

        /// <summary>
        /// Limpia archivos de log antiguos según la configuración
        /// </summary>
        Task CleanupOldLogsAsync();
    }
}