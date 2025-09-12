using OCR_test.Models.DTOs.DocuWare;
using OCR_test.Services.Interfaces;
using System.Diagnostics;
using Tesseract;
using PDFtoImage;

namespace OCR_test.Services.Implementations
{
    /// <summary>
    /// Implementación del servicio de OCR usando Tesseract
    /// </summary>
    public class OcrService : IOcrService
    {
        private readonly IDocuWareDocumentService _documentService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<OcrService> _logger;

        public OcrService(
            IDocuWareDocumentService documentService,
            IConfiguration configuration,
            ILogger<OcrService> logger)
        {
            _documentService = documentService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<OcrResultDto> ExtractTextFromStreamAsync(Stream stream, string? language = null)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                var dataPath = _configuration["Tesseract:DataPath"] ?? "./tessdata";
                var defaultLanguage = _configuration["Tesseract:Language"] ?? "eng";
                var lang = language ?? defaultLanguage;

                _logger.LogInformation("Iniciando OCR con idioma: {Language}, DataPath: {DataPath}", lang, dataPath);

                using var engine = new TesseractEngine(dataPath, lang, EngineMode.Default);
                
                // Convertir stream a array de bytes
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                var fileBytes = memoryStream.ToArray();

                string extractedText = "";
                float totalConfidence = 0;
                int pageCount = 0;

                // Detectar si es PDF o imagen
                if (IsPdf(fileBytes))
                {
                    _logger.LogInformation("Detectado PDF, convirtiendo a imágenes...");
                    
                    // Convertir PDF a imágenes
                    using var pdfStream = new MemoryStream(fileBytes);
                    var images = Conversion.ToImages(pdfStream);
                    
                    foreach (var image in images)
                    {
                        pageCount++;
                        _logger.LogInformation("Procesando página {PageNumber} del PDF...", pageCount);
                        
                        // Convertir SKBitmap a array de bytes
                        using var imageStream = new MemoryStream();
                        image.Encode(imageStream, SkiaSharp.SKEncodedImageFormat.Png, 100);
                        var imageBytes = imageStream.ToArray();
                        
                        // Procesar con Tesseract
                        using var img = Pix.LoadFromMemory(imageBytes);
                        using var page = engine.Process(img);
                        
                        var pageText = page.GetText();
                        var pageConfidence = page.GetMeanConfidence();
                        
                        extractedText += $"\n--- Página {pageCount} ---\n{pageText}";
                        totalConfidence += pageConfidence;
                        
                        _logger.LogInformation("Página {PageNumber} procesada: {CharCount} caracteres, confianza: {Confidence:F2}%", 
                            pageCount, pageText.Length, pageConfidence);
                    }
                    
                    totalConfidence = pageCount > 0 ? totalConfidence / pageCount : 0;
                }
                else
                {
                    _logger.LogInformation("Detectado imagen, procesando directamente...");
                    
                    // Procesar como imagen directamente
                    using var img = Pix.LoadFromMemory(fileBytes);
                    using var page = engine.Process(img);
                    
                    extractedText = page.GetText();
                    totalConfidence = page.GetMeanConfidence();
                    pageCount = 1;
                }

                stopwatch.Stop();

                _logger.LogInformation("OCR completado en {ElapsedMs}ms. Páginas: {PageCount}, Confianza promedio: {Confidence:F2}%", 
                    stopwatch.ElapsedMilliseconds, pageCount, totalConfidence);

                return new OcrResultDto
                {
                    Success = true,
                    ExtractedText = extractedText,
                    Confidence = totalConfidence,
                    Language = lang,
                    Message = $"Texto extraído exitosamente de {pageCount} página(s) con {totalConfidence:F2}% de confianza promedio",
                    ProcessedAt = DateTime.UtcNow,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error en OCR: {Error}", ex.Message);

                return new OcrResultDto
                {
                    Success = false,
                    ExtractedText = null,
                    Confidence = 0,
                    Language = language,
                    Message = $"Error en OCR: {ex.Message}",
                    ProcessedAt = DateTime.UtcNow,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
        }

        public async Task<OcrResultDto> ExtractTextFromDocumentAsync(int documentId, string? fileCabinetId = null)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                _logger.LogInformation("Iniciando OCR para documento {DocumentId}", documentId);

                // Obtener el documento desde DocuWare
                var documentStream = await _documentService.ViewDocumentAsync(documentId, fileCabinetId);
                
                // Realizar OCR
                var ocrResult = await ExtractTextFromStreamAsync(documentStream.Content);
                
                // Actualizar información del documento en el resultado
                ocrResult.Message = $"OCR completado para documento {documentId}: {ocrResult.Message}";
                
                return ocrResult;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error en OCR del documento {DocumentId}: {Error}", documentId, ex.Message);

                return new OcrResultDto
                {
                    Success = false,
                    ExtractedText = null,
                    Confidence = 0,
                    Language = null,
                    Message = $"Error en OCR del documento {documentId}: {ex.Message}",
                    ProcessedAt = DateTime.UtcNow,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
        }

        /// <summary>
        /// Determina si el archivo es un PDF basándose en los bytes iniciales
        /// </summary>
        private static bool IsPdf(byte[] fileBytes)
        {
            if (fileBytes == null || fileBytes.Length < 4)
                return false;

            // Los PDFs empiezan con "%PDF"
            return fileBytes[0] == 0x25 && fileBytes[1] == 0x50 && fileBytes[2] == 0x44 && fileBytes[3] == 0x46;
        }
    }
}