using System.Globalization;
using System.Text;
using OCR_test.Services.Interfaces;

namespace OCR_test.Services.Implementations
{
    /// <summary>
    /// Implementación del servicio de logging CSV para análisis de documentos problemáticos
    /// </summary>
    public class CsvLoggingService : ICsvLoggingService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<CsvLoggingService> _logger;
        private readonly string _logsDirectory;
        private readonly bool _csvLoggingEnabled;
        private readonly int _maxFileSizeMB;
        private readonly int _retentionDays;

        public CsvLoggingService(IConfiguration configuration, ILogger<CsvLoggingService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            
            // Cargar configuración
            _logsDirectory = _configuration["CsvLogging:LogsDirectory"] ?? "./logs";
            _csvLoggingEnabled = _configuration.GetValue<bool>("CsvLogging:EnableCsvLogging", true);
            _maxFileSizeMB = _configuration.GetValue<int>("CsvLogging:MaxFileSizeMB", 10);
            _retentionDays = _configuration.GetValue<int>("CsvLogging:RetentionDays", 30);

            // Crear directorio si no existe
            if (_csvLoggingEnabled && !Directory.Exists(_logsDirectory))
            {
                Directory.CreateDirectory(_logsDirectory);
                _logger.LogInformation("?? Directorio de logs CSV creado: {Directory}", _logsDirectory);
            }
        }

        public async Task LogCuitIssueAsync(int documentId, string foundCuit, string issueType, string details)
        {
            if (!_csvLoggingEnabled) return;

            var fileName = "cuit_issues.csv";
            var headers = "Timestamp,DocumentId,FoundCuit,IssueType,Details,RequiresReview";
            var data = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss},{documentId},\"{foundCuit}\",\"{issueType}\",\"{details}\",True";

            await WriteToCSVAsync(fileName, headers, data);
            
            _logger.LogWarning("?? CUIT Issue logged: Doc {DocumentId}, CUIT: {Cuit}, Issue: {Issue}", 
                documentId, foundCuit, issueType);
        }

        public async Task LogValidationIssueAsync(int documentId, string fieldName, string detectedValue, string validationError, string? expectedFormat = null)
        {
            if (!_csvLoggingEnabled) return;

            var fileName = "validation_issues.csv";
            var headers = "Timestamp,DocumentId,FieldName,DetectedValue,ValidationError,ExpectedFormat,RequiresReview";
            var data = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss},{documentId},\"{fieldName}\",\"{detectedValue}\",\"{validationError}\",\"{expectedFormat ?? "N/A"}\",True";

            await WriteToCSVAsync(fileName, headers, data);
            
            _logger.LogWarning("?? Validation Issue logged: Doc {DocumentId}, Field: {Field}, Value: {Value}", 
                documentId, fieldName, detectedValue);
        }

        public async Task LogOcrFailureAsync(int documentId, string errorMessage, string? ocrText = null)
        {
            if (!_csvLoggingEnabled) return;

            var fileName = "ocr_failures.csv";
            var headers = "Timestamp,DocumentId,ErrorMessage,PartialOcrText,RequiresManualReview";
            var cleanOcrText = ocrText?.Replace("\"", "\"\"").Replace("\r\n", " ").Replace("\n", " ") ?? "";
            var truncatedOcrText = cleanOcrText.Length > 500 ? cleanOcrText.Substring(0, 500) + "..." : cleanOcrText;
            var data = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss},{documentId},\"{errorMessage}\",\"{truncatedOcrText}\",True";

            await WriteToCSVAsync(fileName, headers, data);
            
            _logger.LogError("?? OCR Failure logged: Doc {DocumentId}, Error: {Error}", documentId, errorMessage);
        }

        public async Task LogDocuWareUpdateFailureAsync(int documentId, string errorMessage, string? fieldsToUpdate = null)
        {
            if (!_csvLoggingEnabled) return;

            var fileName = "docuware_failures.csv";
            var headers = "Timestamp,DocumentId,ErrorMessage,FieldsToUpdate,RequiresManualUpdate";
            var data = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss},{documentId},\"{errorMessage}\",\"{fieldsToUpdate ?? "N/A"}\",True";

            await WriteToCSVAsync(fileName, headers, data);
            
            _logger.LogError("?? DocuWare Update Failure logged: Doc {DocumentId}, Error: {Error}", documentId, errorMessage);
        }

        public async Task LogBatchSummaryAsync(string batchId, int totalProcessed, int successful, int failed, int skipped, Dictionary<string, int> issueStats)
        {
            if (!_csvLoggingEnabled) return;

            var fileName = "batch_summaries.csv";
            var headers = "Timestamp,BatchId,TotalProcessed,Successful,Failed,Skipped,SuccessRate,CuitIssues,ValidationIssues,OcrFailures,DocuWareFailures";
            
            var successRate = totalProcessed > 0 ? Math.Round((double)successful / totalProcessed * 100, 2) : 0;
            var cuitIssues = issueStats.GetValueOrDefault("CuitIssues", 0);
            var validationIssues = issueStats.GetValueOrDefault("ValidationIssues", 0);
            var ocrFailures = issueStats.GetValueOrDefault("OcrFailures", 0);
            var docuWareFailures = issueStats.GetValueOrDefault("DocuWareFailures", 0);

            var data = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss},\"{batchId}\",{totalProcessed},{successful},{failed},{skipped},{successRate},{cuitIssues},{validationIssues},{ocrFailures},{docuWareFailures}";

            await WriteToCSVAsync(fileName, headers, data);
            
            _logger.LogInformation("?? Batch Summary logged: {BatchId}, Success Rate: {SuccessRate}%", batchId, successRate);
        }

        public async Task CleanupOldLogsAsync()
        {
            if (!_csvLoggingEnabled) return;

            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-_retentionDays);
                var files = Directory.GetFiles(_logsDirectory, "*.csv");
                var deletedCount = 0;

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTimeUtc < cutoffDate)
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                }

                if (deletedCount > 0)
                {
                    _logger.LogInformation("?? Cleanup completed: {Count} old log files deleted", deletedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during log cleanup");
            }
        }

        private async Task WriteToCSVAsync(string fileName, string headers, string data)
        {
            try
            {
                var filePath = Path.Combine(_logsDirectory, fileName);
                var fileExists = File.Exists(filePath);
                var fileSize = fileExists ? new FileInfo(filePath).Length / (1024 * 1024) : 0;

                // Rotar archivo si es muy grande
                if (fileExists && fileSize >= _maxFileSizeMB)
                {
                    var rotatedFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
                    var rotatedPath = Path.Combine(_logsDirectory, rotatedFileName);
                    File.Move(filePath, rotatedPath);
                    fileExists = false;
                    _logger.LogInformation("?? Log file rotated: {FileName} -> {RotatedFileName}", fileName, rotatedFileName);
                }

                // Escribir headers si es archivo nuevo
                if (!fileExists)
                {
                    await File.WriteAllTextAsync(filePath, headers + Environment.NewLine, Encoding.UTF8);
                }

                // Agregar datos
                await File.AppendAllTextAsync(filePath, data + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing to CSV file: {FileName}", fileName);
            }
        }

        /// <summary>
        /// Obtiene estadísticas de los archivos de log para monitoreo
        /// </summary>
        public async Task<Dictionary<string, object>> GetLogStatisticsAsync()
        {
            var stats = new Dictionary<string, object>();

            if (!_csvLoggingEnabled)
            {
                stats["enabled"] = false;
                return stats;
            }

            try
            {
                stats["enabled"] = true;
                stats["logsDirectory"] = _logsDirectory;
                stats["retentionDays"] = _retentionDays;

                var files = Directory.GetFiles(_logsDirectory, "*.csv");
                stats["totalFiles"] = files.Length;

                var fileStats = new List<object>();
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    var lineCount = await CountLinesAsync(file);
                    
                    fileStats.Add(new
                    {
                        fileName = Path.GetFileName(file),
                        sizeKB = Math.Round(fileInfo.Length / 1024.0, 2),
                        createdAt = fileInfo.CreationTimeUtc,
                        lastModified = fileInfo.LastWriteTimeUtc,
                        recordCount = lineCount - 1 // Excluir header
                    });
                }

                stats["files"] = fileStats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting log statistics");
                stats["error"] = ex.Message;
            }

            return stats;
        }

        private async Task<int> CountLinesAsync(string filePath)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(filePath);
                return lines.Length;
            }
            catch
            {
                return 0;
            }
        }
    }
}