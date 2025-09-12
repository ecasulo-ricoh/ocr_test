using Microsoft.AspNetCore.Mvc;
using OCR_test.Models.DTOs.DocuWare;
using OCR_test.Services.Interfaces;

namespace OCR_test.Controllers
{
    /// <summary>
    /// Controlador para operaciones con documentos de DocuWare y OCR
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
        /// Obtiene información de un documento específico
        /// </summary>
        /// <param name="documentId">ID del documento</param>
        /// <param name="fileCabinetId">ID del FileCabinet (opcional)</param>
        /// <returns>Información del documento</returns>
        [HttpGet("{documentId}")]
        [ProducesResponseType(typeof(DocumentDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetDocument(int documentId, [FromQuery] string? fileCabinetId = null)
        {
            try
            {
                var fcId = fileCabinetId ?? _configService.GetFileCabinetId();
                _logger.LogInformation("Obteniendo información del documento {DocumentId}", documentId);

                var document = await _documentService.GetDocumentAsync(documentId, fcId);

                return Ok(new
                {
                    Success = true,
                    Message = $"Documento {documentId} obtenido exitosamente",
                    Data = document,
                    FileCabinetId = fcId,
                    RequestedAt = DateTime.UtcNow
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Documento {DocumentId} no encontrado", documentId);
                return NotFound(new
                {
                    Success = false,
                    Message = ex.Message,
                    DocumentId = documentId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo documento {DocumentId}", documentId);
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Error interno del servidor",
                    Error = ex.Message
                });
            }
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
        /// Descarga un documento
        /// </summary>
        /// <param name="documentId">ID del documento</param>
        /// <param name="fileCabinetId">ID del FileCabinet (opcional)</param>
        /// <returns>Archivo para descarga</returns>
        [HttpGet("{documentId}/download")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DownloadDocument(int documentId, [FromQuery] string? fileCabinetId = null)
        {
            try
            {
                var fcId = fileCabinetId ?? _configService.GetFileCabinetId();
                _logger.LogInformation("Descargando documento {DocumentId}", documentId);

                var downloadInfo = await _documentService.DownloadDocumentAsync(documentId, fcId);

                return File(
                    downloadInfo.Content,
                    downloadInfo.ContentType,
                    downloadInfo.FileName);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Documento {DocumentId} no encontrado para descarga", documentId);
                return NotFound(new
                {
                    Success = false,
                    Message = ex.Message,
                    DocumentId = documentId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error descargando documento {DocumentId}", documentId);
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Error descargando documento",
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Extrae texto de un documento usando OCR
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

        /// <summary>
        /// Extrae texto de un archivo subido usando OCR
        /// </summary>
        /// <param name="file">Archivo de imagen o PDF</param>
        /// <param name="language">Idioma para OCR (opcional)</param>
        /// <returns>Texto extraído del archivo</returns>
        [HttpPost("ocr/upload")]
        [ProducesResponseType(typeof(OcrResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ExtractTextFromUpload(
            IFormFile file,
            [FromQuery] string? language = null)
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

                _logger.LogInformation("Iniciando OCR para archivo subido: {FileName} ({FileSize} bytes)", 
                    file.FileName, file.Length);

                using var stream = file.OpenReadStream();
                var ocrResult = await _ocrService.ExtractTextFromStreamAsync(stream, language);

                var statusCode = ocrResult.Success ? StatusCodes.Status200OK : StatusCodes.Status500InternalServerError;

                return StatusCode(statusCode, new
                {
                    ocrResult.Success,
                    ocrResult.Message,
                    Data = new
                    {
                        FileName = file.FileName,
                        FileSize = file.Length,
                        ocrResult.ExtractedText,
                        ocrResult.Confidence,
                        ocrResult.Language,
                        ocrResult.ProcessingTimeMs,
                        ocrResult.ProcessedAt
                    },
                    RequestedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en OCR de archivo subido: {FileName}", file?.FileName);
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