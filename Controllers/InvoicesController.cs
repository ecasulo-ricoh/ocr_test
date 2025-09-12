using Microsoft.AspNetCore.Mvc;
using OCR_test.Models.DTOs.Invoice;
using OCR_test.Services.Interfaces;

namespace OCR_test.Controllers
{
    /// <summary>
    /// Controlador para análisis estructurado de facturas
    /// </summary>
    [ApiController]
    [Route("api/invoices")]
    [Produces("application/json")]
    public class InvoicesController : ControllerBase
    {
        private readonly IInvoiceAnalysisService _invoiceAnalysisService;
        private readonly IDocuWareConfigurationService _configService;
        private readonly ILogger<InvoicesController> _logger;

        public InvoicesController(
            IInvoiceAnalysisService invoiceAnalysisService,
            IDocuWareConfigurationService configService,
            ILogger<InvoicesController> logger)
        {
            _invoiceAnalysisService = invoiceAnalysisService;
            _configService = configService;
            _logger = logger;
        }

        /// <summary>
        /// Analiza un documento de DocuWare como factura con extracción de campos estructurados
        /// </summary>
        /// <param name="documentId">ID del documento</param>
        /// <param name="fileCabinetId">ID del FileCabinet (opcional)</param>
        /// <param name="language">Idioma para OCR (opcional, por defecto: eng+spa)</param>
        /// <param name="fastMode">Modo rápido - solo primera página (por defecto: true)</param>
        /// <returns>Análisis estructurado de la factura</returns>
        [HttpPost("analyze-document/{documentId}")]
        [ProducesResponseType(typeof(InvoiceAnalysisResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AnalyzeInvoiceDocument(
            int documentId,
            [FromQuery] string? fileCabinetId = null,
            [FromQuery] string? language = null,
            [FromQuery] bool fastMode = true)
        {
            try
            {
                var fcId = fileCabinetId ?? _configService.GetFileCabinetId();
                var modeText = fastMode ? "MODO RÁPIDO (solo primera página)" : "MODO COMPLETO (todas las páginas)";
                _logger.LogInformation("Iniciando análisis de factura para documento {DocumentId} en {Mode}", documentId, modeText);

                var analysisResult = await _invoiceAnalysisService.AnalyzeInvoiceFromDocumentAsync(
                    documentId, fcId, language, fastMode);

                // *** INFORMACIÓN ESPECÍFICA PARA FACTURAS ARGENTINAS ***
                var argentineInfo = new
                {
                    InvoiceType = analysisResult.InvoiceData?.Header?.ArgentineInvoiceType,
                    InvoiceCode = analysisResult.InvoiceData?.Header?.ArgentineInvoiceCode,
                    RequiresManualUpdate = analysisResult.InvoiceData?.Header?.RequiresManualUpdate ?? true,
                    UpdateRecommendation = analysisResult.InvoiceData?.Header?.RequiresManualUpdate == true
                        ? "??  ACCIÓN REQUERIDA: Actualizar manualmente el tipo de factura en DocuWare"
                        : "? Tipo de factura detectado correctamente"
                };

                var response = new
                {
                    analysisResult.Success,
                    analysisResult.Message,
                    Data = analysisResult.InvoiceData,
                    
                    // *** RESUMEN ESPECÍFICO PARA ARGENTINA ***
                    ArgentineInvoiceInfo = argentineInfo,
                    
                    // *** INFORMACIÓN DE RENDIMIENTO ***
                    PerformanceInfo = new
                    {
                        FastMode = fastMode,
                        Mode = modeText,
                        ProcessingTimeMs = analysisResult.ProcessingTimeMs,
                        PagesProcessed = 1 // Limitado a primera página
                    },
                    
                    OcrMetadata = analysisResult.OcrMetadata,
                    analysisResult.Warnings,
                    DocumentId = documentId,
                    FileCabinetId = fcId,
                    analysisResult.ProcessedAt,
                    RequestedAt = DateTime.UtcNow
                };

                var statusCode = analysisResult.Success ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError;
                return StatusCode(statusCode, response);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Documento {DocumentId} no encontrado para análisis de factura", documentId);
                return NotFound(new
                {
                    Success = false,
                    Message = ex.Message,
                    DocumentId = documentId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analizando factura del documento {DocumentId}", documentId);
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Error en análisis de factura",
                    Error = ex.Message,
                    DocumentId = documentId
                });
            }
        }

        /// <summary>
        /// Analiza un archivo subido como factura con extracción de campos estructurados
        /// </summary>
        /// <param name="file">Archivo de imagen o PDF de la factura</param>
        /// <param name="language">Idioma para OCR (opcional)</param>
        /// <param name="fastMode">Modo rápido - solo primera página (por defecto: true)</param>
        /// <returns>Análisis estructurado de la factura</returns>
        [HttpPost("analyze-upload")]
        [ProducesResponseType(typeof(InvoiceAnalysisResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AnalyzeInvoiceUpload(
            IFormFile file,
            [FromQuery] string? language = null,
            [FromQuery] bool fastMode = true)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Message = "No se proporcionó archivo o el archivo está vacío"
                    });
                }

                // Validar tipo de archivo
                var allowedTypes = new[] { "image/jpeg", "image/png", "image/tiff", "application/pdf" };
                if (!allowedTypes.Contains(file.ContentType.ToLower()))
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Message = $"Tipo de archivo no soportado: {file.ContentType}. Tipos permitidos: {string.Join(", ", allowedTypes)}"
                    });
                }

                var modeText = fastMode ? "MODO RÁPIDO (solo primera página)" : "MODO COMPLETO (todas las páginas)";
                _logger.LogInformation("Iniciando análisis de factura para archivo: {FileName} ({FileSize} bytes) en {Mode}", 
                    file.FileName, file.Length, modeText);

                using var stream = file.OpenReadStream();
                var analysisResult = await _invoiceAnalysisService.AnalyzeInvoiceFromStreamAsync(stream, language, fastMode);

                // *** INFORMACIÓN ESPECÍFICA PARA FACTURAS ARGENTINAS ***
                var argentineInfo = new
                {
                    InvoiceType = analysisResult.InvoiceData?.Header?.ArgentineInvoiceType,
                    InvoiceCode = analysisResult.InvoiceData?.Header?.ArgentineInvoiceCode,
                    RequiresManualUpdate = analysisResult.InvoiceData?.Header?.RequiresManualUpdate ?? true,
                    UpdateRecommendation = analysisResult.InvoiceData?.Header?.RequiresManualUpdate == true
                        ? "??  ACCIÓN REQUERIDA: Actualizar manualmente el tipo de factura en DocuWare"
                        : "? Tipo de factura detectado correctamente"
                };

                var response = new
                {
                    analysisResult.Success,
                    analysisResult.Message,
                    Data = analysisResult.InvoiceData,
                    
                    // *** RESUMEN ESPECÍFICO PARA ARGENTINA ***
                    ArgentineInvoiceInfo = argentineInfo,
                    
                    // *** INFORMACIÓN DE RENDIMIENTO ***
                    PerformanceInfo = new
                    {
                        FastMode = fastMode,
                        Mode = modeText,
                        ProcessingTimeMs = analysisResult.ProcessingTimeMs,
                        PagesProcessed = 1 // Limitado a primera página
                    },
                    
                    OcrMetadata = analysisResult.OcrMetadata,
                    analysisResult.Warnings,
                    FileInfo = new
                    {
                        FileName = file.FileName,
                        FileSize = file.Length,
                        ContentType = file.ContentType
                    },
                    analysisResult.ProcessedAt,
                    RequestedAt = DateTime.UtcNow
                };

                var statusCode = analysisResult.Success ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError;
                return StatusCode(statusCode, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analizando factura del archivo: {FileName}", file?.FileName);
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Error en análisis de factura",
                    Error = ex.Message,
                    FileName = file?.FileName
                });
            }
        }

        /// <summary>
        /// Analiza texto previamente extraído como factura
        /// </summary>
        /// <param name="request">Texto a analizar</param>
        /// <returns>Análisis estructurado del texto como factura</returns>
        [HttpPost("analyze-text")]
        [ProducesResponseType(typeof(InvoiceAnalysisResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult AnalyzeInvoiceText([FromBody] AnalyzeTextRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Text))
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Message = "El texto a analizar no puede estar vacío"
                    });
                }

                _logger.LogInformation("Iniciando análisis de texto como factura ({Length} caracteres)", 
                    request.Text.Length);

                var analysisResult = _invoiceAnalysisService.AnalyzeInvoiceFromText(request.Text);

                var response = new
                {
                    analysisResult.Success,
                    analysisResult.Message,
                    Data = analysisResult.InvoiceData,
                    analysisResult.Warnings,
                    TextLength = request.Text.Length,
                    ProcessedAt = DateTime.UtcNow,
                    RequestedAt = DateTime.UtcNow
                };

                var statusCode = analysisResult.Success ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError;
                return StatusCode(statusCode, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analizando texto como factura");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Error en análisis de texto",
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Obtiene los patrones de detección soportados
        /// </summary>
        /// <returns>Lista de patrones y tipos de campos soportados</returns>
        [HttpGet("supported-patterns")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetSupportedPatterns()
        {
            var patterns = new
            {
                // *** ESPECÍFICO PARA ARGENTINA ***
                ArgentineInvoiceTypes = new[]
                {
                    new { 
                        Type = "A", 
                        Code = "001", 
                        Description = "Factura A - Para responsables inscriptos",
                        DetectionPatterns = new[] { 
                            "Formato estándar: 'A CÓDIGO N° 001'",
                            "Formato separado: 'A\\nCÓDIGO No 001'",
                            "Con espacios variables: 'A    CODIGO   001'",
                            "Orden inverso: 'CÓDIGO 001\\nA'",
                            "Proximidad: 'A' cerca de '001' (hasta 50 caracteres)",
                            "Correlación: letra 'A' + código '001' por separado"
                        }
                    },
                    new { 
                        Type = "B", 
                        Code = "006", 
                        Description = "Factura B - Para consumidores finales",
                        DetectionPatterns = new[] { 
                            "Formato estándar: 'B CÓDIGO N° 006'",
                            "Formato separado: 'B\\nCÓDIGO No 006'",
                            "Con espacios variables: 'B    CODIGO   006'",
                            "Orden inverso: 'CÓDIGO 006\\nB'",
                            "Proximidad: 'B' cerca de '006' (hasta 50 caracteres)",
                            "Correlación: letra 'B' + código '006' por separado"
                        }
                    }
                },
                
                // *** ESTRATEGIAS DE DETECCIÓN ***
                DetectionStrategies = new[]
                {
                    new { 
                        Priority = 1, 
                        Strategy = "Patrón directo", 
                        Confidence = "95%",
                        Description = "Letra + código en la misma línea"
                    },
                    new { 
                        Priority = 2, 
                        Strategy = "Líneas separadas", 
                        Confidence = "90%",
                        Description = "Letra seguida de código en línea siguiente"
                    },
                    new { 
                        Priority = 3, 
                        Strategy = "Patrón flexible", 
                        Confidence = "88%",
                        Description = "Espacios y saltos de línea variables"
                    },
                    new { 
                        Priority = 4, 
                        Strategy = "Orden inverso", 
                        Confidence = "85%",
                        Description = "Código primero, letra después"
                    },
                    new { 
                        Priority = 5, 
                        Strategy = "Proximidad", 
                        Confidence = "82%",
                        Description = "Letra y código cercanos (hasta 50 caracteres)"
                    },
                    new { 
                        Priority = 6, 
                        Strategy = "Correlación", 
                        Confidence = "75%",
                        Description = "Detección independiente de letra y código"
                    }
                },
                
                Codes = new[]
                {
                    new { Type = "InvoiceNumber", Description = "Números de factura", Examples = new[] { "FAC-001234", "INV2024001" } },
                    new { Type = "CUIT", Description = "CUIT argentino", Examples = new[] { "20-12345678-9" } },
                    new { Type = "RFC", Description = "RFC mexicano", Examples = new[] { "XAXX010101000" } },
                    new { Type = "RUT", Description = "RUT chileno", Examples = new[] { "12.345.678-9" } }
                },
                Dates = new[]
                {
                    new { Format = "DD/MM/YYYY", Examples = new[] { "15/03/2024", "01-12-2023" } },
                    new { Format = "YYYY/MM/DD", Examples = new[] { "2024/03/15", "2023-12-01" } },
                    new { Format = "MMM DD, YYYY", Examples = new[] { "March 15, 2024", "Dec 01, 2023" } }
                },
                Numbers = new[]
                {
                    new { Type = "Total", Description = "Montos totales" },
                    new { Type = "Subtotal", Description = "Subtotales" },
                    new { Type = "Tax", Description = "Impuestos" },
                    new { Type = "Percentage", Description = "Porcentajes" }
                },
                SupportedLanguages = new[] { "spa", "eng", "spa+eng", "eng+spa" },
                SupportedFileTypes = new[] { "image/jpeg", "image/png", "image/tiff", "application/pdf" },
                
                // *** RECOMENDACIONES MEJORADAS PARA ARGENTINA ***
                BestPractices = new
                {
                    RecommendedLanguage = "spa+eng",
                    OptimalImageQuality = "300 DPI o superior",
                    ImportantNote = "Para facturas argentinas, es crítico detectar el tipo (A/B) para cumplimiento fiscal",
                    SupportedFormats = new[]
                    {
                        "Formato estándar: A CÓDIGO N° 001",
                        "Formato separado con saltos de línea",
                        "Espaciado variable entre elementos", 
                        "Orden inverso (código antes que letra)",
                        "Proximidad hasta 50 caracteres de distancia"
                    },
                    PerformanceOptimization = new
                    {
                        DefaultMode = "Modo rápido (fastMode=true)",
                        FastModeDescription = "Procesa solo la primera página para mayor velocidad",
                        FullModeDescription = "Procesa todas las páginas (más lento)",
                        Recommendation = "Usar modo rápido para facturas ya que el tipo (A/B) aparece en la primera página",
                        TypicalSpeedImprovement = "70-90% más rápido en PDFs multipágina"
                    }
                }
            };

            return Ok(new
            {
                Success = true,
                Message = "Patrones de detección soportados - Optimizado para facturas argentinas con procesamiento rápido",
                Data = patterns,
                RequestedAt = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Request para análisis de texto
    /// </summary>
    public class AnalyzeTextRequest
    {
        public required string Text { get; set; }
    }
}