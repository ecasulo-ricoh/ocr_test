using Microsoft.AspNetCore.Mvc;
using OCR_test.Services.Interfaces;

namespace OCR_test.Controllers
{
    /// <summary>
    /// Controlador para gesti�n de reportes CSV y estad�sticas de procesamiento
    /// </summary>
    [ApiController]
    [Route("api/reports")]
    [Produces("application/json")]
    public class ReportsController : ControllerBase
    {
        private readonly ICsvLoggingService _csvLoggingService;
        private readonly ILogger<ReportsController> _logger;

        public ReportsController(
            ICsvLoggingService csvLoggingService,
            ILogger<ReportsController> logger)
        {
            _csvLoggingService = csvLoggingService;
            _logger = logger;
        }

        /// <summary>
        /// Obtiene estad�sticas de los archivos de log CSV
        /// </summary>
        /// <returns>Estad�sticas detalladas de los reportes</returns>
        [HttpGet("csv-statistics")]
        [ProducesResponseType(typeof(Dictionary<string, object>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetCsvStatistics()
        {
            try
            {
                _logger.LogInformation("?? Obteniendo estad�sticas de reportes CSV...");

                var statistics = await _csvLoggingService.GetLogStatisticsAsync();

                return Ok(new
                {
                    Success = true,
                    Message = "Estad�sticas de reportes CSV obtenidas exitosamente",
                    Data = statistics,
                    RequestedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error obteniendo estad�sticas de reportes CSV");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Error obteniendo estad�sticas de reportes CSV",
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Ejecuta limpieza de archivos de log antiguos
        /// </summary>
        /// <returns>Resultado de la operaci�n de limpieza</returns>
        [HttpPost("cleanup-logs")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CleanupLogs()
        {
            try
            {
                _logger.LogInformation("?? Iniciando limpieza de logs antiguos...");

                await _csvLoggingService.CleanupOldLogsAsync();

                return Ok(new
                {
                    Success = true,
                    Message = "Limpieza de logs completada exitosamente",
                    ExecutedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error durante la limpieza de logs");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Error durante la limpieza de logs",
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Obtiene informaci�n sobre los tipos de reportes disponibles
        /// </summary>
        /// <returns>Informaci�n de los reportes CSV disponibles</returns>
        [HttpGet("available-reports")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetAvailableReports()
        {
            var reports = new[]
            {
                new
                {
                    Name = "cuit_issues.csv",
                    Description = "Documentos con problemas de CUIT (solo encontr� 1 CUIT o formato inv�lido)",
                    Columns = new[] { "Timestamp", "DocumentId", "FoundCuit", "IssueType", "Details", "RequiresReview" }
                },
                new
                {
                    Name = "validation_issues.csv",
                    Description = "Documentos con problemas de validaci�n en campos espec�ficos",
                    Columns = new[] { "Timestamp", "DocumentId", "FieldName", "DetectedValue", "ValidationError", "ExpectedFormat", "RequiresReview" }
                },
                new
                {
                    Name = "ocr_failures.csv",
                    Description = "Documentos que fallaron completamente en el proceso de OCR",
                    Columns = new[] { "Timestamp", "DocumentId", "ErrorMessage", "PartialOcrText", "RequiresManualReview" }
                },
                new
                {
                    Name = "docuware_failures.csv",
                    Description = "Documentos que fallaron en la actualizaci�n de DocuWare",
                    Columns = new[] { "Timestamp", "DocumentId", "ErrorMessage", "FieldsToUpdate", "RequiresManualUpdate" }
                },
                new
                {
                    Name = "batch_summaries.csv",
                    Description = "Res�menes estad�sticos de cada lote procesado",
                    Columns = new[] { "Timestamp", "BatchId", "TotalProcessed", "Successful", "Failed", "Skipped", "SuccessRate", "CuitIssues", "ValidationIssues", "OcrFailures", "DocuWareFailures" }
                }
            };

            return Ok(new
            {
                Success = true,
                Message = "Informaci�n de reportes disponibles",
                Data = new
                {
                    ReportsLocation = "./logs/csv_reports",
                    TotalReportTypes = reports.Length,
                    Reports = reports,
                    Features = new[]
                    {
                        "Tracking autom�tico de errores durante procesamiento masivo",
                        "Detecci�n espec�fica de problemas con CUIT",
                        "Registro de validaciones fallidas por campo",
                        "An�lisis de fallos de OCR con texto parcial",
                        "Seguimiento de errores de actualizaci�n en DocuWare",
                        "Estad�sticas por lote con tasas de �xito",
                        "Soporte para nuevo tipo de factura E (c�digo 019)"
                    }
                },
                RequestedAt = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Endpoint de salud para el sistema de reportes
        /// </summary>
        /// <returns>Estado del sistema de reportes</returns>
        [HttpGet("health")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetReportsHealth()
        {
            try
            {
                var statistics = await _csvLoggingService.GetLogStatisticsAsync();
                var enabled = statistics.ContainsKey("enabled") && (bool)statistics["enabled"];

                return Ok(new
                {
                    Success = true,
                    ReportsEnabled = enabled,
                    Message = enabled ? "Sistema de reportes CSV operativo" : "Sistema de reportes CSV deshabilitado",
                    CheckedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    Success = false,
                    ReportsEnabled = false,
                    Message = "Error verificando estado del sistema de reportes",
                    Error = ex.Message,
                    CheckedAt = DateTime.UtcNow
                });
            }
        }
    }
}