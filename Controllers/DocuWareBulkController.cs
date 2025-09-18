using Microsoft.AspNetCore.Mvc;
using OCR_test.Models.DTOs.DocuWare;
using OCR_test.Services.Interfaces;

namespace OCR_test.Controllers
{
    /// <summary>
    /// Controlador para actualización masiva de documentos en DocuWare con OCR
    /// </summary>
    [ApiController]
    [Route("api/docuware")]
    [Produces("application/json")]
    public class DocuWareBulkController : ControllerBase
    {
        private readonly IDocuWareBulkUpdateService _bulkUpdateService;
        private readonly IDocuWareConfigurationService _configService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DocuWareBulkController> _logger;

        public DocuWareBulkController(
            IDocuWareBulkUpdateService bulkUpdateService,
            IDocuWareConfigurationService configService,
            IConfiguration configuration,
            ILogger<DocuWareBulkController> logger)
        {
            _bulkUpdateService = bulkUpdateService;
            _configService = configService;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Actualiza múltiples documentos en DocuWare usando OCR para extraer campos de facturas argentinas
        /// </summary>
        /// <param name="request">Parámetros de la actualización masiva</param>
        /// <returns>Resultado detallado de la operación masiva</returns>
        [HttpPost("bulk-update")]
        [ProducesResponseType(typeof(BulkUpdateResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> BulkUpdateDocuments([FromBody] BulkUpdateRequestDto request)
        {
            try
            {
                // ? USAR LÍMITE CONFIGURADO EN APPSETTINGS
                var maxDocumentLimit = _configuration.GetValue<int>("BulkUpdate:MaxDocumentLimit", 5000);

                // Validaciones
                if (request.DocumentCount <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "La cantidad de documentos debe ser mayor a 0"
                    });
                }

                if (request.DocumentCount > maxDocumentLimit)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"La cantidad máxima de documentos por lote es {maxDocumentLimit:N0} (configurado en appsettings.json)",
                        maxAllowed = maxDocumentLimit
                    });
                }

                var mode = request.DryRun ? "DRY-RUN (simulación)" : "ACTUALIZACIÓN REAL";
                var updateMode = request.OnlyUpdateEmptyFields ? "SOLO CAMPOS VACÍOS" : "SOBRESCRIBIR TODOS";
                var fileCabinetId = _configService.GetFileCabinetId();
                var language = "spa+eng";

                _logger.LogInformation("?? Iniciando procesamiento on-the-fly: {Count:N0} documentos en modo {Mode}, " +
                    "Actualización: {UpdateMode}, FileCabinet: {FileCabinetId}, Idioma: {Language}",
                    request.DocumentCount, mode, updateMode, fileCabinetId, language);

                // Crear request interno con valores por defecto
                var internalRequest = new BulkUpdateInternalRequestDto
                {
                    DocumentCount = request.DocumentCount,
                    DryRun = request.DryRun,
                    OnlyUpdateEmptyFields = request.OnlyUpdateEmptyFields,
                    FileCabinetId = fileCabinetId,
                    Language = language
                };

                // Ejecutar procesamiento on-the-fly
                var result = await _bulkUpdateService.BulkUpdateDocumentsAsync(internalRequest);

                // Preparar respuesta detallada
                var response = new
                {
                    success = result.Success,
                    message = result.Message,
                    summary = new
                    {
                        totalProcessed = result.TotalProcessed,
                        documentsModified = result.SuccessfulUpdates,
                        documentsWithErrors = result.FailedUpdates,
                        documentsWithoutChanges = result.SkippedDocuments,
                        modificationRate = result.TotalProcessed > 0
                            ? Math.Round((double)result.SuccessfulUpdates / result.TotalProcessed * 100, 2) 
                            : 0
                    },
                    metadata = new
                    {
                        mode = request.DryRun ? "DRY-RUN" : "REAL",
                        updateStrategy = request.OnlyUpdateEmptyFields ? "EMPTY_ONLY" : "OVERWRITE_ALL",
                        fileCabinetId = result.Metadata.FileCabinetId,
                        language = result.Metadata.Language,
                        startTime = result.Metadata.StartTime,
                        endTime = result.Metadata.EndTime,
                        totalTimeMs = result.Metadata.TotalProcessingTimeMs,
                        maxDocumentLimit = maxDocumentLimit,
                        performance = new
                        {
                            documentsPerSecond = Math.Round(result.Metadata.Performance.DocumentsPerSecond, 2),
                            averageOcrTimeMs = Math.Round(result.Metadata.Performance.AverageOcrTimeMs, 0),
                            averageUpdateTimeMs = Math.Round(result.Metadata.Performance.AverageUpdateTimeMs, 0)
                        }
                    },
                    details = result.Details.Select(d => new
                    {
                        documentId = d.DocumentId,
                        status = d.Status,
                        message = d.Message,
                        processingTimeMs = d.ProcessingTimeMs,
                        detectedFields = d.DetectedFields == null ? null : new
                        {
                            tipoFactura = d.DetectedFields.TipoFactura,
                            codigoFactura = d.DetectedFields.CodigoFactura,
                            nroFactura = d.DetectedFields.NroFactura,
                            fechaFactura = d.DetectedFields.FechaFactura,
                            cuitCliente = d.DetectedFields.CuitCliente,
                            confianza = Math.Round(d.DetectedFields.Confianza, 2)
                        },
                        updatedFields = d.UpdatedFields == null ? null : new
                        {
                            LETRA_DOCUMENTO = d.UpdatedFields.LETRA_DOCUMENTO,
                            CODIGO_DOCUMENTO = d.UpdatedFields.CODIGO_DOCUMENTO,
                            NDEG_FACTURA = d.UpdatedFields.NDEG_FACTURA,
                            DATE = d.UpdatedFields.DATE,
                            CUIT_CLIENTE = d.UpdatedFields.CUIT_CLIENTE
                        },
                        skippedFields = d.SkippedFields,
                        validationWarnings = d.ValidationWarnings,
                        errors = d.Errors
                    }).ToList(),
                    errors = result.Errors,
                    requestedAt = DateTime.UtcNow
                };

                var statusCode = result.Success ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError;

                // Log de resumen final
                _logger.LogInformation("?? Procesamiento on-the-fly finalizado. " +
                    "Modificados: {Modified}/{Total} ({ModificationRate}%), " +
                    "Errores: {Errors}, Sin cambios: {NoChanges}, " +
                    "Tiempo total: {TotalMs}ms, " +
                    "Velocidad: {DocsPerSec} docs/seg, " +
                    "Modo: {UpdateMode}",
                    result.SuccessfulUpdates, result.TotalProcessed,
                    Math.Round((double)result.SuccessfulUpdates / Math.Max(result.TotalProcessed, 1) * 100, 1),
                    result.FailedUpdates, result.SkippedDocuments,
                    result.Metadata.TotalProcessingTimeMs,
                    Math.Round(result.Metadata.Performance.DocumentsPerSecond, 1),
                    updateMode);

                return StatusCode(statusCode, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en procesamiento on-the-fly de documentos");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error interno en procesamiento on-the-fly",
                    error = ex.Message,
                    requestedAt = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Obtiene la lista de documentos que se procesarían (SOLO INFORMATIVO - no se usa en el procesamiento real)
        /// ?? El procesamiento real ahora funciona on-the-fly sin obtener listas previas
        /// </summary>
        /// <param name="count">Cantidad de documentos (máximo 100 para endpoint informativo)</param>
        /// <param name="fileCabinetId">ID del FileCabinet (opcional)</param>
        /// <returns>Lista de IDs de documentos encontrados</returns>
        [HttpGet("document-list")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetDocumentList(
            [FromQuery] int count,
            [FromQuery] string? fileCabinetId = null)
        {
            try
            {
                // Limitar a 100 para endpoint informativo
                var maxInformativeLimit = 100;

                if (count <= 0 || count > maxInformativeLimit)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"Para endpoint informativo, la cantidad debe estar entre 1 y {maxInformativeLimit}",
                        maxAllowed = maxInformativeLimit,
                        note = "El procesamiento real funciona on-the-fly sin límites de este endpoint"
                    });
                }

                var fcId = fileCabinetId ?? _configService.GetFileCabinetId();
                _logger.LogInformation("?? Obteniendo lista informativa de {Count} documentos del FileCabinet {FileCabinetId}", count, fcId);

                var documentIds = await _bulkUpdateService.GetDocumentListAsync(fcId, count);

                return Ok(new
                {
                    success = true,
                    message = $"Lista informativa de {documentIds.Count} documentos obtenida exitosamente",
                    note = "Esta es solo una muestra informativa. El procesamiento real funciona on-the-fly sin obtener listas previas.",
                    data = new
                    {
                        fileCabinetId = fcId,
                        requestedCount = count,
                        actualCount = documentIds.Count,
                        maxInformativeLimit = maxInformativeLimit,
                        processingMode = "ON_THE_FLY",
                        documentIds = documentIds
                    },
                    requestedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo lista informativa de documentos");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error obteniendo lista informativa de documentos",
                    error = ex.Message,
                    requestedAt = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Obtiene información sobre los campos que se actualizarán en DocuWare
        /// </summary>
        /// <returns>Información sobre el mapeo de campos</returns>
        [HttpGet("field-mapping")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetFieldMapping()
        {
            var maxDocumentLimit = _configuration.GetValue<int>("BulkUpdate:MaxDocumentLimit", 5000);

            var mapping = new
            {
                description = "Mapeo de campos OCR a campos DocuWare para facturas argentinas - Archivador Bimbo",
                processingMode = "ON_THE_FLY",
                maxDocumentLimit = maxDocumentLimit,
                ocrToDocuWareMapping = new object[]
                {
                    new { 
                        ocrField = "tipoFactura", 
                        docuWareField = "LETRA", 
                        description = "Tipo de factura (A, B o E)",
                        validValues = new[] { "A", "B", "E" },
                        example = "A"
                    },
                    new { 
                        ocrField = "codigoFactura", 
                        docuWareField = "CODIGO", 
                        description = "Código de factura (001 para A, 006 para B, 019 para E)",
                        validValues = new[] { "001", "006", "019" },
                        example = "001"
                    },
                    new { 
                        ocrField = "nroFactura", 
                        docuWareField = "NDEG_FACTURA", 
                        description = "Número de la factura",
                        format = "XXXXX-XXXXXXXX",
                        example = "00723-0019175"
                    },
                    new { 
                        ocrField = "fechaFactura", 
                        docuWareField = "DATE", 
                        description = "Fecha de la factura en formato DD/MM/yyyy",
                        format = "DD/MM/YYYY",
                        example = "11/04/2025"
                    },
                    new { 
                        ocrField = "cuitCliente", 
                        docuWareField = "CUIT_CLIENTE", 
                        description = "CUIT del cliente (SIEMPRE el segundo CUIT que aparece en el documento)",
                        format = "XX-XXXXXXXX-X",
                        example = "30-59036070-3"
                    }
                },
                processingFeatures = new[]
                {
                    "Procesamiento on-the-fly: encuentra y procesa documentos inmediatamente",
                    "Patrones optimizados para Tesseract sin preprocesamiento pesado",
                    "Detección inteligente de tipos A/B/E con auto-completado",
                    "Selección automática del segundo CUIT como cliente",
                    "Manejo de valores placeholder configurables"
                },
                notes = new[]
                {
                    "Solo se actualizan campos que tienen valores válidos detectados por OCR",
                    "El modo DRY-RUN permite simular actualizaciones sin modificar datos",
                    "Los documentos se buscan y procesan on-the-fly sin listas previas",
                    "Se requiere confianza mínima en la detección OCR para actualizar campos",
                    $"Límite máximo configurable: {maxDocumentLimit:N0} documentos por lote"
                }
            };

            return Ok(new
            {
                success = true,
                message = "Información de mapeo de campos optimizado para Bimbo",
                data = mapping,
                requestedAt = DateTime.UtcNow
            });
        }
    }
}