namespace OCR_test.Services.Interfaces
{
    /// <summary>
    /// Servicio para logging especializado en archivos CSV para an�lisis de documentos problem�ticos
    /// </summary>
    public interface ICsvLoggingService
    {
        /// <summary>
        /// Registra un documento con problemas en el CUIT (solo encontr� 1 CUIT)
        /// </summary>
        Task LogCuitIssueAsync(int documentId, string foundCuit, string issueType, string details);

        /// <summary>
        /// Registra un documento con problemas de validaci�n
        /// </summary>
        Task LogValidationIssueAsync(int documentId, string fieldName, string detectedValue, string validationError, string? expectedFormat = null);

        /// <summary>
        /// Registra un documento que fall� completamente en el OCR
        /// </summary>
        Task LogOcrFailureAsync(int documentId, string errorMessage, string? ocrText = null);

        /// <summary>
        /// Registra un documento que fall� en la actualizaci�n de DocuWare
        /// </summary>
        Task LogDocuWareUpdateFailureAsync(int documentId, string errorMessage, string? fieldsToUpdate = null);

        /// <summary>
        /// Registra estad�sticas de un lote procesado
        /// </summary>
        Task LogBatchSummaryAsync(string batchId, int totalProcessed, int successful, int failed, int skipped, Dictionary<string, int> issueStats);

        /// <summary>
        /// Limpia archivos de log antiguos seg�n la configuraci�n
        /// </summary>
        Task CleanupOldLogsAsync();
    }
}