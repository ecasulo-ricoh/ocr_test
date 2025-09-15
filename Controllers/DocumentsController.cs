using Microsoft.AspNetCore.Mvc;
using OCR_test.Models.DTOs.DocuWare;
using OCR_test.Services.Interfaces;

namespace OCR_test.Controllers
{
    /// <summary>
    /// Controlador simplificado para operaciones esenciales con documentos de DocuWare y OCR
    /// </summary>
    [ApiController]
    [Route("api/documents")]
    [Produces("application/json")]
    public class DocumentsController : ControllerBase
    {
        private readonly IDocuWareDocumentService _documentService;
        private readonly IOcrService _ocrService;
        private readonly IDocuWareConfigurationService _configService;
        private readonly ILogger<DocumentsController> _logger;

        public DocumentsController(
            IDocuWareDocumentService documentService,
            IOcrService ocrService,
            IDocuWareConfigurationService configService,
            ILogger<DocumentsController> logger)
        {
            _documentService = documentService;
            _ocrService = ocrService;
            _configService = configService;
            _logger = logger;
        }

        /// <summary>
        /// Visualiza un documento en el navegador
        /// </summary>
        /// <param name="documentId">ID del documento</param>
        /// <param name="fileCabinetId">ID del FileCabinet (opcional)</param>
        /// <returns>Stream del documento para visualización</returns>
        [HttpGet("view/{documentId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ViewDocument(int documentId, [FromQuery] string? fileCabinetId = null)
        {
            try
            {
                var fcId = fileCabinetId ?? _configService.GetFileCabinetId();
                _logger.LogInformation("Preparando visualización del documento {DocumentId}", documentId);

                var viewInfo = await _documentService.ViewDocumentAsync(documentId, fcId);

                // Configurar headers para visualización en navegador
                Response.Headers.Append("Content-Disposition", "inline");
                
                return File(
                    viewInfo.Content,
                    viewInfo.ContentType,
                    enableRangeProcessing: true);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Documento {DocumentId} no encontrado para visualización", documentId);
                return NotFound(new
                {
                    Success = false,
                    Message = ex.Message,
                    DocumentId = documentId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error visualizando documento {DocumentId}", documentId);
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Error preparando documento para visualización",
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Extrae texto de un documento usando OCR (endpoint principal para OCR con docid)
        /// </summary>
        /// <param name="documentId">ID del documento</param>
        /// <param name="fileCabinetId">ID del FileCabinet (opcional)</param>
        /// <param name="language">Idioma para OCR (opcional, por defecto: eng+spa)</param>
        /// <returns>Texto extraído del documento</returns>
        [HttpPost("{documentId}/ocr")]
        [ProducesResponseType(typeof(OcrResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ExtractTextFromDocument(
            int documentId, 
            [FromQuery] string? fileCabinetId = null,
            [FromQuery] string? language = null)
        {
            try
            {
                var fcId = fileCabinetId ?? _configService.GetFileCabinetId();
                _logger.LogInformation("Iniciando OCR para documento {DocumentId} con idioma {Language}", 
                    documentId, language ?? "default");

                var ocrResult = await _ocrService.ExtractTextFromDocumentAsync(documentId, fcId);

                var statusCode = ocrResult.Success ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError;

                return StatusCode(statusCode, new
                {
                    ocrResult.Success,
                    ocrResult.Message,
                    Data = new
                    {
                        DocumentId = documentId,
                        ocrResult.ExtractedText,
                        ocrResult.Confidence,
                        ocrResult.Language,
                        ocrResult.ProcessingTimeMs,
                        ocrResult.ProcessedAt
                    },
                    FileCabinetId = fcId,
                    RequestedAt = DateTime.UtcNow
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Documento {DocumentId} no encontrado para OCR", documentId);
                return NotFound(new
                {
                    Success = false,
                    Message = ex.Message,
                    DocumentId = documentId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en OCR del documento {DocumentId}", documentId);
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Error en procesamiento OCR",
                    Error = ex.Message
                });
            }
        }
    }
}