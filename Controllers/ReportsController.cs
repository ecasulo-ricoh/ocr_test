using Microsoft.AspNetCore.Mvc;
using OCR_test.Services.Interfaces;

namespace OCR_test.Controllers
{
    /// <summary>
    /// Controlador para gestión de reportes CSV y estadísticas de procesamiento
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
        /// Obtiene estadísticas de los archivos de log CSV
        /// </summary>
        /// <returns>Estadísticas detalladas de los reportes</returns>
        [HttpGet("csv-statistics")]
        [ProducesResponseType(typeof(Dictionary<string, object>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetCsvStatistics()
        {
            try
            {
                _logger.LogInformation("?? Obteniendo estadísticas de reportes CSV...");

                var statistics = await _csvLoggingService.GetLogStatisticsAsync();

                return Ok(new
                {
                    Success = true,
                    Message = "Estadísticas de reportes CSV obtenidas exitosamente",
                    Data = statistics,
                    RequestedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error obteniendo estadísticas de reportes CSV");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Error obteniendo estadísticas de reportes CSV",
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Ejecuta limpieza de archivos de log antiguos
        /// </summary>
        /// <returns>Resultado de la operación de limpieza</returns>
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
        /// Obtiene información sobre los tipos de reportes disponibles
        /// </summary>
        /// <returns>Información de los reportes CSV disponibles</returns>
        [HttpGet("available-reports")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetAvailableReports()
        {
            var reports = new[]
            {
                new
                {
                    Name = "cuit_issues.csv",
                    Description = "Documentos con problemas de CUIT (solo encontró 1 CUIT o formato inválido)",
                    Columns = new[] { "Timestamp", "DocumentId", "FoundCuit", "IssueType", "Details", "RequiresReview" }
                },
                new
                {
                    Name = "validation_issues.csv",
                    Description = "Documentos con problemas de validación en campos específicos",
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
                    Description = "Documentos que fallaron en la actualización de DocuWare",
                    Columns = new[] { "Timestamp", "DocumentId", "ErrorMessage", "FieldsToUpdate", "RequiresManualUpdate" }
                },
                new
                {
                    Name = "batch_summaries.csv",
                    Description = "Resúmenes estadísticos de cada lote procesado",
                    Columns = new[] { "Timestamp", "BatchId", "TotalProcessed", "Successful", "Failed", "Skipped", "SuccessRate", "CuitIssues", "ValidationIssues", "OcrFailures", "DocuWareFailures" }
                }
            };

            return Ok(new
            {
                Success = true,
                Message = "Información de reportes disponibles",
                Data = new
                {
                    ReportsLocation = "./logs/csv_reports",
                    TotalReportTypes = reports.Length,
                    Reports = reports,
                    Features = new[]
                    {
                        "Tracking automático de errores durante procesamiento masivo",
                        "Detección específica de problemas con CUIT",
                        "Registro de validaciones fallidas por campo",
                        "Análisis de fallos de OCR con texto parcial",
                        "Seguimiento de errores de actualización en DocuWare",
                        "Estadísticas por lote con tasas de éxito",
                        "Soporte para nuevo tipo de factura E (código 019)"
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