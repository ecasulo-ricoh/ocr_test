using OCR_test.Models.DTOs.Invoice;
using OCR_test.Services.Interfaces;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OCR_test.Services.Implementations
{
    /// <summary>
    /// Implementación del servicio de análisis de facturas
    /// </summary>
    public class InvoiceAnalysisService : IInvoiceAnalysisService
    {
        private readonly IOcrService _ocrService;
        private readonly ILogger<InvoiceAnalysisService> _logger;

        // Patrones de expresiones regulares para diferentes campos
        private static readonly Dictionary<string, Regex> _patterns = new()
        {
            // Números de factura
            ["InvoiceNumber"] = new Regex(@"(?:factura|invoice|n[úu]mero|number|no\.?)\s*:?\s*([A-Z0-9\-]+)", RegexOptions.IgnoreCase),
            ["InvoiceNumberAlt"] = new Regex(@"([A-Z]{1,3}\d{6,12})", RegexOptions.IgnoreCase),
            
            // RFC / Tax ID (México)
            ["RFC"] = new Regex(@"\b([A-ZÑ&]{3,4}\d{6}[A-Z0-9]{3})\b", RegexOptions.IgnoreCase),
            
            // CUIT (Argentina)
            ["CUIT"] = new Regex(@"\b(\d{2}-\d{8}-\d{1})\b"),
            
            // RUT (Chile)
            ["RUT"] = new Regex(@"(\d{1,2}\.\d{3}\.\d{3}-[\dkK])", RegexOptions.IgnoreCase),
            
            // *** PATRONES MEJORADOS PARA FACTURAS ARGENTINAS ***
            // Adaptados para el formato específico: CODIGO N" 001
            
            // Tipo de factura A con código 001 - Patrones básicos
            ["InvoiceTypeA"] = new Regex(@"([A])\s*(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*1\b", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            ["InvoiceTypeACode"] = new Regex(@"(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*1\s*.*?([A])", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            ["InvoiceTypeALetter"] = new Regex(@"([A])\s*(?:\n|\r\n?|\s)*(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*1", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            
            // Tipo de factura B con código 006 - Patrones básicos
            ["InvoiceTypeB"] = new Regex(@"([B])\s*(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*6\b", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            ["InvoiceTypeBCode"] = new Regex(@"(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*6\s*.*?([B])", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            ["InvoiceTypeBLetter"] = new Regex(@"([B])\s*(?:\n|\r\n?|\s)*(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*6", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            
            // *** NUEVOS PATRONES ESPECÍFICOS PARA EL FORMATO DETECTADO ***
            // Para capturar "A FACTURA" seguido de "CODIGO N" 001" en cualquier parte del texto
            
            // Patrón específico para "A FACTURA" + "CODIGO N" 001"
            ["InvoiceTypeAFactura"] = new Regex(@"([A])\s+FACTURA[\s\S]*?(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*1", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            
            // Patrón específico para "B FACTURA" + "CODIGO N" 006"
            ["InvoiceTypeBFactura"] = new Regex(@"([B])\s+FACTURA[\s\S]*?(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*6", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            
            // Patrones para detectar "GRUPO A" específicamente
            ["InvoiceTypeAGrupo"] = new Regex(@"GRUPO\s+([A])[\s\S]*?(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*1", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            ["InvoiceTypeBGrupo"] = new Regex(@"GRUPO\s+([B])[\s\S]*?(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*6", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            
            // Patrón flexible para A seguido de "CODIGO N" 001" con saltos de línea y espacios variables
            ["InvoiceTypeAFlexible"] = new Regex(@"([A])\s*[\r\n\s]*(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*1", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            
            // Patrón flexible para B seguido de "CODIGO N" 006" con saltos de línea y espacios variables  
            ["InvoiceTypeBFlexible"] = new Regex(@"([B])\s*[\r\n\s]*(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*6", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            
            // Patrones inversos - Código primero, letra después
            ["InvoiceTypeAReverse"] = new Regex(@"(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*1\s*[\r\n\s]*([A])", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            ["InvoiceTypeBReverse"] = new Regex(@"(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*6\s*[\r\n\s]*([B])", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            
            // Patrones muy flexibles para detectar A/B cerca de 001/006 (dentro de cierto rango de caracteres)
            ["InvoiceTypeAProximity"] = new Regex(@"([A])[\s\S]{0,100}(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*1", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            ["InvoiceTypeBProximity"] = new Regex(@"([B])[\s\S]{0,100}(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*6", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            
            // Patrones para detectar códigos cerca de letras (proximidad inversa)
            ["InvoiceTypeAProximityReverse"] = new Regex(@"(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*1[\s\S]{0,100}([A])", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            ["InvoiceTypeBProximityReverse"] = new Regex(@"(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*6[\s\S]{0,100}([B])", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            
            // *** PATRONES SIMPLIFICADOS PARA DETECCIÓN BÁSICA ***
            // Solo buscar A o B seguido de cualquier variación de código + 001/006
            ["InvoiceTypeASimple"] = new Regex(@"\b([A])\b[\s\S]{0,200}(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*1", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            ["InvoiceTypeBSimple"] = new Regex(@"\b([B])\b[\s\S]{0,200}(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*0*6", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            
            // Patrones más flexibles para detectar A/B independientemente del código
            ["LetterAAlone"] = new Regex(@"(?:^|\s|\n|GRUPO\s+|FACTURA\s*)([A])(?:\s|\n|$|\s+FACTURA)", RegexOptions.Multiline),
            ["LetterBAlone"] = new Regex(@"(?:^|\s|\n|GRUPO\s+|FACTURA\s*)([B])(?:\s|\n|$|\s+FACTURA)", RegexOptions.Multiline),
            ["Code001"] = new Regex(@"(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*(0*1)\b", RegexOptions.IgnoreCase),
            ["Code006"] = new Regex(@"(?:codigo|código|cod\.?|code)\s*(?:n[°º""\*]?|no\.?|number)\s*(0*6)\b", RegexOptions.IgnoreCase),
            
            // Fechas en diferentes formatos
            ["DateDDMMYYYY"] = new Regex(@"\b(\d{1,2}[\/\-\.]\d{1,2}[\/\-\.]\d{4})\b"),
            ["DateYYYYMMDD"] = new Regex(@"\b(\d{4}[\/\-\.]\d{1,2}[\/\-\.]\d{1,2})\b"),
            ["DateMMMDDYYYY"] = new Regex(@"\b([A-Za-z]{3,9}\s+\d{1,2},?\s+\d{4})\b"),
            
            // Montos con moneda
            ["Amount"] = new Regex(@"(?:\$|USD|EUR|MXN|ARS|CLP)?\s*(\d{1,3}(?:,\d{3})*(?:\.\d{2})?)", RegexOptions.IgnoreCase),
            ["AmountWithCurrency"] = new Regex(@"(\$|USD|EUR|MXN|ARS|CLP)\s*(\d{1,3}(?:,\d{3})*(?:\.\d{2})?)", RegexOptions.IgnoreCase),
            
            // Porcentajes
            ["Percentage"] = new Regex(@"(\d{1,3}(?:\.\d{1,2})?)\s*%"),
            
            // Teléfonos
            ["Phone"] = new Regex(@"(?:\+?\d{1,3}[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}"),
            
            // Emails
            ["Email"] = new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b"),
            
            // Códigos de producto
            ["ProductCode"] = new Regex(@"\b([A-Z0-9]{3,15})\b"),
            
            // Total, subtotal, etc.
            ["Total"] = new Regex(@"(?:total|sum|amount)\s*:?\s*(?:\$|USD|EUR|MXN|ARS|CLP)?\s*(\d{1,3}(?:,\d{3})*(?:\.\d{2})?)", RegexOptions.IgnoreCase),
            ["Subtotal"] = new Regex(@"(?:subtotal|sub-total)\s*:?\s*(?:\$|USD|EUR|MXN|ARS|CLP)?\s*(\d{1,3}(?:,\d{3})*(?:\.\d{2})?)", RegexOptions.IgnoreCase),
            ["Tax"] = new Regex(@"(?:tax|iva|impuesto)\s*:?\s*(?:\$|USD|EUR|MXN|ARS|CLP)?\s*(\d{1,3}(?:,\d{3})*(?:\.\d{2})?)", RegexOptions.IgnoreCase)
        };

        public InvoiceAnalysisService(IOcrService ocrService, ILogger<InvoiceAnalysisService> logger)
        {
            _ocrService = ocrService;
            _logger = logger;
        }

        public async Task<InvoiceAnalysisResultDto> AnalyzeInvoiceFromDocumentAsync(
            int documentId, 
            string? fileCabinetId = null, 
            string? language = null)
        {
            // Por defecto usar modo rápido (solo primera página)
            return await AnalyzeInvoiceFromDocumentAsync(documentId, fileCabinetId, language, fastMode: true);
        }

        public async Task<InvoiceAnalysisResultDto> AnalyzeInvoiceFromDocumentAsync(
            int documentId, 
            string? fileCabinetId = null, 
            string? language = null,
            bool fastMode = true)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var modeText = fastMode ? "MODO RÁPIDO (solo primera página)" : "MODO COMPLETO";
                _logger.LogInformation("Iniciando análisis de factura para documento {DocumentId} en {Mode}", documentId, modeText);

                // Realizar OCR primero
                var ocrResult = await _ocrService.ExtractTextFromDocumentAsync(documentId, fileCabinetId);

                if (!ocrResult.Success || string.IsNullOrEmpty(ocrResult.ExtractedText))
                {
                    return new InvoiceAnalysisResultDto
                    {
                        Success = false,
                        Message = $"Error en OCR: {ocrResult.Message}",
                        ProcessedAt = DateTime.UtcNow,
                        ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }

                // Analizar el texto extraído
                var analysis = AnalyzeInvoiceFromText(ocrResult.ExtractedText);
                
                // Agregar metadatos de OCR
                analysis.OcrMetadata = new OcrMetadataDto
                {
                    ExtractedText = ocrResult.ExtractedText,
                    OverallConfidence = ocrResult.Confidence,
                    Language = ocrResult.Language,
                    PageCount = 1 // Limitado a primera página
                };

                stopwatch.Stop();
                analysis.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                analysis.ProcessedAt = DateTime.UtcNow;

                // Actualizar mensaje con información del modo
                analysis.Message += fastMode ? " (Procesado en modo rápido - solo primera página)" : " (Procesado en modo completo)";

                _logger.LogInformation("Análisis de factura completado para documento {DocumentId} en {ElapsedMs}ms ({Mode})", 
                    documentId, stopwatch.ElapsedMilliseconds, modeText);

                return analysis;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error analizando factura del documento {DocumentId}", documentId);

                return new InvoiceAnalysisResultDto
                {
                    Success = false,
                    Message = $"Error en análisis de factura: {ex.Message}",
                    ProcessedAt = DateTime.UtcNow,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
        }

        public async Task<InvoiceAnalysisResultDto> AnalyzeInvoiceFromStreamAsync(Stream stream, string? language = null)
        {
            // Por defecto usar modo rápido (solo primera página)
            return await AnalyzeInvoiceFromStreamAsync(stream, language, fastMode: true);
        }

        public async Task<InvoiceAnalysisResultDto> AnalyzeInvoiceFromStreamAsync(Stream stream, string? language = null, bool fastMode = true)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var modeText = fastMode ? "MODO RÁPIDO (solo primera página)" : "MODO COMPLETO";
                _logger.LogInformation("Iniciando análisis de factura desde stream en {Mode}", modeText);

                // Realizar OCR primero
                var ocrResult = await _ocrService.ExtractTextFromStreamAsync(stream, language);

                if (!ocrResult.Success || string.IsNullOrEmpty(ocrResult.ExtractedText))
                {
                    return new InvoiceAnalysisResultDto
                    {
                        Success = false,
                        Message = $"Error en OCR: {ocrResult.Message}",
                        ProcessedAt = DateTime.UtcNow,
                        ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }

                // Analizar el texto extraído
                var analysis = AnalyzeInvoiceFromText(ocrResult.ExtractedText);
                
                // Agregar metadatos de OCR
                analysis.OcrMetadata = new OcrMetadataDto
                {
                    ExtractedText = ocrResult.ExtractedText,
                    OverallConfidence = ocrResult.Confidence,
                    Language = ocrResult.Language,
                    PageCount = 1 // Limitado a primera página
                };

                stopwatch.Stop();
                analysis.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                analysis.ProcessedAt = DateTime.UtcNow;

                // Actualizar mensaje con información del modo
                analysis.Message += fastMode ? " (Procesado en modo rápido - solo primera página)" : " (Procesado en modo completo)";

                _logger.LogInformation("Análisis de factura completado desde stream en {ElapsedMs}ms ({Mode})", 
                    stopwatch.ElapsedMilliseconds, modeText);

                return analysis;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error analizando factura desde stream: {Error}", ex.Message);

                return new InvoiceAnalysisResultDto
                {
                    Success = false,
                    Message = $"Error en análisis de factura: {ex.Message}",
                    ProcessedAt = DateTime.UtcNow,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
        }

        public InvoiceAnalysisResultDto AnalyzeInvoiceFromText(string extractedText)
        {
            try
            {
                _logger.LogInformation("Analizando texto como factura ({Length} caracteres)", extractedText.Length);

                var invoiceData = new InvoiceDataDto();
                var warnings = new List<string>();

                // *** DETECTAR TIPO DE FACTURA ARGENTINA PRIMERO ***
                var invoiceType = DetectArgentineInvoiceType(extractedText);
                if (invoiceType != null)
                {
                    invoiceData.Codes.Add(invoiceType);
                    _logger.LogInformation("Tipo de factura argentina detectado: {Type} (Código: {Code})", 
                        invoiceType.Value, invoiceType.Type == "InvoiceTypeA" ? "001" : "006");
                }

                // Extraer códigos generales
                invoiceData.Codes.AddRange(ExtractCodes(extractedText));

                // Extraer fechas
                invoiceData.Dates = ExtractDates(extractedText);

                // Extraer números
                invoiceData.Numbers = ExtractNumbers(extractedText);

                // Analizar estructura de factura
                AnalyzeInvoiceStructure(extractedText, invoiceData, warnings);

                // Calcular confianza general
                var overallConfidence = CalculateOverallConfidence(invoiceData);

                var typeMessage = invoiceType != null ? 
                    $" Tipo de factura: {invoiceType.Value} (Código {(invoiceType.Type == "InvoiceTypeA" ? "001" : "006")})." : 
                    " Tipo de factura no detectado.";

                var result = new InvoiceAnalysisResultDto
                {
                    Success = true,
                    Message = $"Factura analizada exitosamente. Encontrados: {invoiceData.Codes.Count} códigos, {invoiceData.Dates.Count} fechas, {invoiceData.Numbers.Count} números.{typeMessage}",
                    InvoiceData = invoiceData,
                    Warnings = warnings
                };

                _logger.LogInformation("Análisis de texto completado. Confianza: {Confidence:F2}%", overallConfidence);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analizando texto como factura");

                return new InvoiceAnalysisResultDto
                {
                    Success = false,
                    Message = $"Error en análisis de texto: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Detecta específicamente el tipo de factura argentina (A o B) con sus códigos
        /// </summary>
        private ExtractedCodeDto? DetectArgentineInvoiceType(string text)
        {
            _logger.LogInformation("Detectando tipo de factura argentina...");
            _logger.LogInformation("Texto a analizar (primeros 200 caracteres): {TextSample}", 
                text.Length > 200 ? text.Substring(0, 200) + "..." : text);

            // *** ESTRATEGIAS ESPECÍFICAS PARA EL FORMATO DETECTADO ***
            
            // Estrategia 1: Buscar "GRUPO A FACTURA" + "CODIGO N" 001"
            var matchA_Grupo = _patterns["InvoiceTypeAGrupo"].Match(text);
            if (matchA_Grupo.Success)
            {
                _logger.LogInformation("? Detectado tipo A con patrón GRUPO A: {Match}", matchA_Grupo.Value);
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeA",
                    Value = "A",
                    Pattern = "InvoiceTypeAGrupo",
                    Confidence = 0.98f
                };
            }

            var matchB_Grupo = _patterns["InvoiceTypeBGrupo"].Match(text);
            if (matchB_Grupo.Success)
            {
                _logger.LogInformation("? Detectado tipo B con patrón GRUPO B: {Match}", matchB_Grupo.Value);
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeB",
                    Value = "B",
                    Pattern = "InvoiceTypeBGrupo",
                    Confidence = 0.98f
                };
            }

            // Estrategia 2: Buscar "A FACTURA" + "CODIGO N" 001"
            var matchA_Factura = _patterns["InvoiceTypeAFactura"].Match(text);
            if (matchA_Factura.Success)
            {
                _logger.LogInformation("? Detectado tipo A con patrón A FACTURA: {Match}", matchA_Factura.Value);
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeA",
                    Value = "A",
                    Pattern = "InvoiceTypeAFactura",
                    Confidence = 0.96f
                };
            }

            var matchB_Factura = _patterns["InvoiceTypeBFactura"].Match(text);
            if (matchB_Factura.Success)
            {
                _logger.LogInformation("? Detectado tipo B con patrón B FACTURA: {Match}", matchB_Factura.Value);
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeB",
                    Value = "B",
                    Pattern = "InvoiceTypeBFactura",
                    Confidence = 0.96f
                };
            }

            // Estrategia 3: Patrones simplificados (solo A/B + código en un rango amplio)
            var matchA_Simple = _patterns["InvoiceTypeASimple"].Match(text);
            if (matchA_Simple.Success)
            {
                _logger.LogInformation("? Detectado tipo A con patrón simple: {Match}", matchA_Simple.Value);
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeA",
                    Value = "A",
                    Pattern = "InvoiceTypeASimple",
                    Confidence = 0.92f
                };
            }

            var matchB_Simple = _patterns["InvoiceTypeBSimple"].Match(text);
            if (matchB_Simple.Success)
            {
                _logger.LogInformation("? Detectado tipo B con patrón simple: {Match}", matchB_Simple.Value);
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeB",
                    Value = "B",
                    Pattern = "InvoiceTypeBSimple",
                    Confidence = 0.92f
                };
            }

            // Estrategia 4: Buscar patrones directos A + 001 o B + 006
            var matchA1 = _patterns["InvoiceTypeA"].Match(text);
            if (matchA1.Success)
            {
                _logger.LogInformation("? Detectado tipo A con patrón directo: {Match}", matchA1.Value);
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeA",
                    Value = "A",
                    Pattern = "InvoiceTypeA",
                    Confidence = 0.95f
                };
            }

            var matchB1 = _patterns["InvoiceTypeB"].Match(text);
            if (matchB1.Success)
            {
                _logger.LogInformation("? Detectado tipo B con patrón directo: {Match}", matchB1.Value);
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeB",
                    Value = "B",
                    Pattern = "InvoiceTypeB",
                    Confidence = 0.95f
                };
            }

            // Estrategia 5: Buscar A seguido de código 001 en líneas separadas
            var matchA2 = _patterns["InvoiceTypeALetter"].Match(text);
            if (matchA2.Success)
            {
                _logger.LogInformation("? Detectado tipo A con letra y código en líneas separadas: {Match}", matchA2.Value);
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeA",
                    Value = "A",
                    Pattern = "InvoiceTypeALetter",
                    Confidence = 0.9f
                };
            }

            // Estrategia 6: Buscar B seguido de código 006 en líneas separadas
            var matchB2 = _patterns["InvoiceTypeBLetter"].Match(text);
            if (matchB2.Success)
            {
                _logger.LogInformation("? Detectado tipo B con letra y código en líneas separadas: {Match}", matchB2.Value);
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeB",
                    Value = "B",
                    Pattern = "InvoiceTypeBLetter",
                    Confidence = 0.9f
                };
            }

            // Estrategia 7: Patrones flexibles con saltos de línea y espacios variables
            var matchA3 = _patterns["InvoiceTypeAFlexible"].Match(text);
            if (matchA3.Success)
            {
                _logger.LogInformation("? Detectado tipo A con patrón flexible: {Match}", matchA3.Value);
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeA",
                    Value = "A",
                    Pattern = "InvoiceTypeAFlexible",
                    Confidence = 0.88f
                };
            }

            var matchB3 = _patterns["InvoiceTypeBFlexible"].Match(text);
            if (matchB3.Success)
            {
                _logger.LogInformation("? Detectado tipo B con patrón flexible: {Match}", matchB3.Value);
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeB",
                    Value = "B",
                    Pattern = "InvoiceTypeBFlexible",
                    Confidence = 0.88f
                };
            }

            // Estrategia 8: Detección por proximidad (A cerca de 001, B cerca de 006)
            var matchA5 = _patterns["InvoiceTypeAProximity"].Match(text);
            if (matchA5.Success)
            {
                _logger.LogInformation("? Detectado tipo A por proximidad: {Match}", matchA5.Value);
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeA",
                    Value = "A",
                    Pattern = "InvoiceTypeAProximity",
                    Confidence = 0.82f
                };
            }

            var matchB5 = _patterns["InvoiceTypeBProximity"].Match(text);
            if (matchB5.Success)
            {
                _logger.LogInformation("? Detectado tipo B por proximidad: {Match}", matchB5.Value);
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeB",
                    Value = "B",
                    Pattern = "InvoiceTypeBProximity",
                    Confidence = 0.82f
                };
            }

            // Estrategia 9: Buscar correlación entre letras A/B y códigos 001/006 (fallback)
            var lettersA = _patterns["LetterAAlone"].Matches(text);
            var lettersB = _patterns["LetterBAlone"].Matches(text);
            var codes001 = _patterns["Code001"].Matches(text);
            var codes006 = _patterns["Code006"].Matches(text);

            _logger.LogInformation("?? Correlación encontrada - Letras A: {LettersA}, Letras B: {LettersB}, Códigos 001: {Codes001}, Códigos 006: {Codes006}", 
                lettersA.Count, lettersB.Count, codes001.Count, codes006.Count);

            if (lettersA.Count > 0 && codes001.Count > 0)
            {
                _logger.LogInformation("? Detectado tipo A por correlación: {LettersA} letras A, {Codes001} códigos 001", 
                    lettersA.Count, codes001.Count);
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeA",
                    Value = "A",
                    Pattern = "CorrelationA001",
                    Confidence = 0.75f
                };
            }

            if (lettersB.Count > 0 && codes006.Count > 0)
            {
                _logger.LogInformation("? Detectado tipo B por correlación: {LettersB} letras B, {Codes006} códigos 006", 
                    lettersB.Count, codes006.Count);
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeB",
                    Value = "B",
                    Pattern = "CorrelationB006",
                    Confidence = 0.75f
                };
            }

            _logger.LogWarning("? No se pudo detectar tipo de factura argentina específico");
            return null;
        }

        private List<ExtractedCodeDto> ExtractCodes(string text)
        {
            var codes = new List<ExtractedCodeDto>();

            // Buscar números de factura
            foreach (var match in _patterns["InvoiceNumber"].Matches(text).Cast<Match>())
            {
                codes.Add(new ExtractedCodeDto
                {
                    Type = "InvoiceNumber",
                    Value = match.Groups[1].Value.Trim(),
                    Pattern = "InvoiceNumber",
                    Confidence = 0.9f
                });
            }

            // Buscar CUIT (Argentina) - más relevante para facturas argentinas
            foreach (var match in _patterns["CUIT"].Matches(text).Cast<Match>())
            {
                codes.Add(new ExtractedCodeDto
                {
                    Type = "CUIT",
                    Value = match.Groups[1].Value,
                    Pattern = "CUIT",
                    Confidence = 0.95f
                });
            }

            // Buscar RFC (México)
            foreach (var match in _patterns["RFC"].Matches(text).Cast<Match>())
            {
                codes.Add(new ExtractedCodeDto
                {
                    Type = "RFC",
                    Value = match.Groups[1].Value.ToUpper(),
                    Pattern = "RFC",
                    Confidence = 0.95f
                });
            }

            // Buscar RUT (Chile)
            foreach (var match in _patterns["RUT"].Matches(text).Cast<Match>())
            {
                codes.Add(new ExtractedCodeDto
                {
                    Type = "RUT",
                    Value = match.Groups[1].Value,
                    Pattern = "RUT",
                    Confidence = 0.95f
                });
            }

            return codes;
        }

        private List<ExtractedDateDto> ExtractDates(string text)
        {
            var dates = new List<ExtractedDateDto>();

            // Buscar fechas DD/MM/YYYY
            foreach (var match in _patterns["DateDDMMYYYY"].Matches(text).Cast<Match>())
            {
                if (DateTime.TryParseExact(match.Groups[1].Value, new[] { "dd/MM/yyyy", "dd-MM-yyyy", "dd.MM.yyyy" }, 
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    dates.Add(new ExtractedDateDto
                    {
                        Type = "Date",
                        ParsedDate = date,
                        OriginalText = match.Groups[1].Value,
                        Format = "DD/MM/YYYY",
                        Confidence = 0.8f
                    });
                }
            }

            // Buscar fechas YYYY/MM/DD
            foreach (var match in _patterns["DateYYYYMMDD"].Matches(text).Cast<Match>())
            {
                if (DateTime.TryParseExact(match.Groups[1].Value, new[] { "yyyy/MM/dd", "yyyy-MM-dd", "yyyy.MM.dd" }, 
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    dates.Add(new ExtractedDateDto
                    {
                        Type = "Date",
                        ParsedDate = date,
                        OriginalText = match.Groups[1].Value,
                        Format = "YYYY/MM/DD",
                        Confidence = 0.8f
                    });
                }
            }

            return dates;
        }

        private List<ExtractedNumberDto> ExtractNumbers(string text)
        {
            var numbers = new List<ExtractedNumberDto>();

            // Buscar totales
            foreach (var match in _patterns["Total"].Matches(text).Cast<Match>())
            {
                if (decimal.TryParse(match.Groups[1].Value.Replace(",", ""), out var amount))
                {
                    numbers.Add(new ExtractedNumberDto
                    {
                        Type = "Total",
                        ParsedValue = amount,
                        OriginalText = match.Groups[1].Value,
                        Confidence = 0.85f
                    });
                }
            }

            // Buscar subtotales
            foreach (var match in _patterns["Subtotal"].Matches(text).Cast<Match>())
            {
                if (decimal.TryParse(match.Groups[1].Value.Replace(",", ""), out var amount))
                {
                    numbers.Add(new ExtractedNumberDto
                    {
                        Type = "Subtotal",
                        ParsedValue = amount,
                        OriginalText = match.Groups[1].Value,
                        Confidence = 0.85f
                    });
                }
            }

            // Buscar impuestos
            foreach (var match in _patterns["Tax"].Matches(text).Cast<Match>())
            {
                if (decimal.TryParse(match.Groups[1].Value.Replace(",", ""), out var amount))
                {
                    numbers.Add(new ExtractedNumberDto
                    {
                        Type = "Tax",
                        ParsedValue = amount,
                        OriginalText = match.Groups[1].Value,
                        Confidence = 0.85f
                    });
                }
            }

            // Buscar porcentajes
            foreach (var match in _patterns["Percentage"].Matches(text).Cast<Match>())
            {
                if (decimal.TryParse(match.Groups[1].Value, out var percentage))
                {
                    numbers.Add(new ExtractedNumberDto
                    {
                        Type = "Percentage",
                        ParsedValue = percentage,
                        OriginalText = match.Groups[1].Value + "%",
                        Confidence = 0.9f
                    });
                }
            }

            return numbers;
        }

        private void AnalyzeInvoiceStructure(string text, InvoiceDataDto invoiceData, List<string> warnings)
        {
            // Obtener tipo de factura argentina si se detectó
            var invoiceTypeA = invoiceData.Codes.FirstOrDefault(c => c.Type == "InvoiceTypeA");
            var invoiceTypeB = invoiceData.Codes.FirstOrDefault(c => c.Type == "InvoiceTypeB");
            var invoiceType = invoiceTypeA ?? invoiceTypeB;
            
            // Crear encabezado básico con la información encontrada
            invoiceData.Header = new InvoiceHeaderDto
            {
                InvoiceNumber = invoiceData.Codes.FirstOrDefault(c => c.Type == "InvoiceNumber")?.Value,
                InvoiceDate = invoiceData.Dates.FirstOrDefault()?.ParsedDate,
                Currency = "ARS", // Asumir pesos argentinos por defecto
                Confidence = 0.7f
            };

            // *** CONFIGURAR INFORMACIÓN ESPECÍFICA DE FACTURA ARGENTINA ***
            if (invoiceType != null)
            {
                invoiceData.Header.ArgentineInvoiceType = invoiceType.Value; // "A" o "B"
                invoiceData.Header.ArgentineInvoiceCode = invoiceType.Type == "InvoiceTypeA" ? "001" : "006";
                invoiceData.Header.Currency = "ARS";
                invoiceData.Header.Confidence = Math.Max(invoiceData.Header.Confidence, invoiceType.Confidence);
                invoiceData.Header.RequiresManualUpdate = false; // Se detectó correctamente
                
                _logger.LogInformation("? Tipo de factura argentina configurado: {Type} (Código: {Code})", 
                    invoiceData.Header.ArgentineInvoiceType, 
                    invoiceData.Header.ArgentineInvoiceCode);
            }
            else
            {
                invoiceData.Header.RequiresManualUpdate = true; // Requiere verificación manual
                _logger.LogWarning("? Tipo de factura argentina NO detectado - requiere actualización manual");
            }

            // Crear totales con los montos encontrados
            var total = invoiceData.Numbers.FirstOrDefault(n => n.Type == "Total");
            var subtotal = invoiceData.Numbers.FirstOrDefault(n => n.Type == "Subtotal");
            var tax = invoiceData.Numbers.FirstOrDefault(n => n.Type == "Tax");

            if (total != null || subtotal != null || tax != null)
            {
                invoiceData.Totals = new InvoiceTotalsDto
                {
                    Total = total?.ParsedValue,
                    Subtotal = subtotal?.ParsedValue,
                    TaxAmount = tax?.ParsedValue,
                    Currency = "ARS",
                    Confidence = 0.75f
                };
            }

            // Crear información básica del emisor si se encontró CUIT
            var cuit = invoiceData.Codes.FirstOrDefault(c => c.Type == "CUIT");
            if (cuit != null)
            {
                invoiceData.Issuer = new CompanyInfoDto
                {
                    TaxId = cuit.Value,
                    Confidence = cuit.Confidence
                };
            }

            // *** ADVERTENCIAS ESPECÍFICAS PARA FACTURAS ARGENTINAS ***
            if (invoiceType == null)
            {
                warnings.Add("?? CRÍTICO: No se detectó el tipo de factura argentina (A o B). REQUERE ACTUALIZACIÓN MANUAL en DocuWare.");
                warnings.Add("?? Buscar visualmente: letra grande 'A' o 'B' con código '001' o '006' respectivamente.");
            }

            if (invoiceData.Header?.InvoiceNumber == null)
                warnings.Add("No se pudo identificar el número de factura");

            if (invoiceData.Header?.InvoiceDate == null)
                warnings.Add("No se pudo identificar la fecha de la factura");

            if (invoiceData.Totals?.Total == null)
                warnings.Add("No se pudo identificar el monto total");

            if (cuit == null)
                warnings.Add("No se pudo identificar el CUIT del emisor");
        }

        private float CalculateOverallConfidence(InvoiceDataDto invoiceData)
        {
            var confidenceValues = new List<float>();

            if (invoiceData.Header != null)
                confidenceValues.Add(invoiceData.Header.Confidence);

            if (invoiceData.Totals != null)
                confidenceValues.Add(invoiceData.Totals.Confidence);

            if (invoiceData.Issuer != null)
                confidenceValues.Add(invoiceData.Issuer.Confidence);

            confidenceValues.AddRange(invoiceData.Codes.Select(c => c.Confidence));
            confidenceValues.AddRange(invoiceData.Dates.Select(d => d.Confidence));
            confidenceValues.AddRange(invoiceData.Numbers.Select(n => n.Confidence));

            return confidenceValues.Any() ? confidenceValues.Average() : 0f;
        }

        // *** NUEVOS MÉTODOS SIMPLIFICADOS ***

        public async Task<SimplifiedInvoiceResultDto> AnalyzeInvoiceSimplifiedAsync(
            int documentId,
            string? fileCabinetId = null,
            string? language = null)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogInformation("Iniciando análisis SIMPLIFICADO de factura para documento {DocumentId}", documentId);

                // Realizar OCR primero (solo primera página para velocidad)
                var ocrResult = await _ocrService.ExtractTextFromDocumentAsync(documentId, fileCabinetId);

                if (!ocrResult.Success || string.IsNullOrEmpty(ocrResult.ExtractedText))
                {
                    return new SimplifiedInvoiceResultDto
                    {
                        Success = false,
                        Message = $"Error en OCR: {ocrResult.Message}",
                        ProcessedAt = DateTime.UtcNow,
                        ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }

                // Analizar el texto extraído
                var analysis = AnalyzeInvoiceSimplifiedFromText(ocrResult.ExtractedText);

                stopwatch.Stop();
                analysis.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                analysis.ProcessedAt = DateTime.UtcNow;

                _logger.LogInformation("Análisis SIMPLIFICADO completado para documento {DocumentId} en {ElapsedMs}ms", 
                    documentId, stopwatch.ElapsedMilliseconds);

                return analysis;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error en análisis SIMPLIFICADO del documento {DocumentId}", documentId);

                return new SimplifiedInvoiceResultDto
                {
                    Success = false,
                    Message = $"Error en análisis simplificado: {ex.Message}",
                    ProcessedAt = DateTime.UtcNow,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
        }

        public async Task<SimplifiedInvoiceResultDto> AnalyzeInvoiceSimplifiedFromStreamAsync(
            Stream stream,
            string? language = null)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogInformation("Iniciando análisis SIMPLIFICADO desde stream");

                // Realizar OCR primero (solo primera página para velocidad)
                var ocrResult = await _ocrService.ExtractTextFromStreamAsync(stream, language);

                if (!ocrResult.Success || string.IsNullOrEmpty(ocrResult.ExtractedText))
                {
                    return new SimplifiedInvoiceResultDto
                    {
                        Success = false,
                        Message = $"Error en OCR: {ocrResult.Message}",
                        ProcessedAt = DateTime.UtcNow,
                        ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }

                // Analizar el texto extraído
                var analysis = AnalyzeInvoiceSimplifiedFromText(ocrResult.ExtractedText);

                stopwatch.Stop();
                analysis.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                analysis.ProcessedAt = DateTime.UtcNow;

                _logger.LogInformation("Análisis SIMPLIFICADO completado desde stream en {ElapsedMs}ms", 
                    stopwatch.ElapsedMilliseconds);

                return analysis;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error en análisis SIMPLIFICADO desde stream: {Error}", ex.Message);

                return new SimplifiedInvoiceResultDto
                {
                    Success = false,
                    Message = $"Error en análisis simplificado: {ex.Message}",
                    ProcessedAt = DateTime.UtcNow,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
        }

        public SimplifiedInvoiceResultDto AnalyzeInvoiceSimplifiedFromText(string extractedText)
        {
            try
            {
                _logger.LogInformation("Analizando texto SIMPLIFICADO ({Length} caracteres)", extractedText.Length);

                var data = new SimplifiedInvoiceDataDto();
                var warnings = new List<string>();
                var confidenceScores = new List<float>();

                // 1. DETECTAR TIPO DE FACTURA (A o B) Y CÓDIGO (001 o 006)
                var detectionResult = DetectArgentineInvoiceTypeAndCodeSimplified(extractedText);
                if (detectionResult.tipoDetectado != null || detectionResult.codigoDetectado != null)
                {
                    // Auto-completar si solo se detectó uno de los dos
                    if (detectionResult.tipoDetectado != null && detectionResult.codigoDetectado == null)
                    {
                        // Se detectó letra, inferir código
                        detectionResult.codigoDetectado = detectionResult.tipoDetectado == "A" ? "001" : "006";
                        _logger.LogInformation("? Auto-completado: Tipo {Tipo} ? Código {Codigo}", 
                            detectionResult.tipoDetectado, detectionResult.codigoDetectado);
                    }
                    else if (detectionResult.codigoDetectado != null && detectionResult.tipoDetectado == null)
                    {
                        // Se detectó código, inferir letra
                        detectionResult.tipoDetectado = detectionResult.codigoDetectado == "001" ? "A" : "B";
                        _logger.LogInformation("? Auto-completado: Código {Codigo} ? Tipo {Tipo}", 
                            detectionResult.codigoDetectado, detectionResult.tipoDetectado);
                    }

                    data.TipoFactura = detectionResult.tipoDetectado;
                    data.CodigoFactura = detectionResult.codigoDetectado;
                    confidenceScores.Add(detectionResult.confidence);
                    
                    _logger.LogInformation("? Tipo de factura: {Tipo}, Código: {Codigo}", 
                        data.TipoFactura, data.CodigoFactura);
                }
                else
                {
                    warnings.Add("No se pudo detectar el tipo de factura (A o B) ni el código (001 o 006)");
                    data.RequiereActualizacionManual = true;
                }

                // 2. DETECTAR NÚMERO DE FACTURA
                var numeroFactura = DetectInvoiceNumber(extractedText);
                if (numeroFactura != null)
                {
                    data.NroFactura = numeroFactura.Value;
                    confidenceScores.Add(numeroFactura.Confidence);
                    _logger.LogInformation("? Número de factura detectado: {Numero}", numeroFactura.Value);
                }
                else
                {
                    warnings.Add("No se pudo detectar el número de factura");
                }

                // 3. DETECTAR FECHA DE FACTURA
                var fechaFactura = DetectInvoiceDate(extractedText);
                if (fechaFactura != null)
                {
                    // Formatear fecha como DD/mm/yyyy
                    data.FechaFactura = fechaFactura.ParsedDate?.ToString("dd/MM/yyyy");
                    confidenceScores.Add(fechaFactura.Confidence);
                    _logger.LogInformation("? Fecha de factura detectada: {Fecha}", data.FechaFactura);
                }
                else
                {
                    warnings.Add("No se pudo detectar la fecha de factura");
                }

                // 4. DETECTAR CUIT DEL CLIENTE (SIEMPRE EL SEGUNDO)
                var cuitCliente = DetectClientCuitSecond(extractedText);
                if (cuitCliente != null)
                {
                    data.CuitCliente = cuitCliente.Value;
                    confidenceScores.Add(cuitCliente.Confidence);
                    _logger.LogInformation("? CUIT del cliente detectado (segundo): {Cuit}", cuitCliente.Value);
                }
                else
                {
                    warnings.Add("No se pudo detectar el CUIT del cliente (segundo CUIT del documento)");
                }

                // Calcular confianza general
                data.Confianza = confidenceScores.Any() ? confidenceScores.Average() : 0f;

                var fieldsDetected = new[] { 
                    data.TipoFactura, 
                    data.CodigoFactura,
                    data.NroFactura, 
                    data.FechaFactura, 
                    data.CuitCliente
                }.Count(f => !string.IsNullOrEmpty(f));

                return new SimplifiedInvoiceResultDto
                {
                    Success = true,
                    Message = $"Análisis simplificado completado. Campos detectados: {fieldsDetected}/5. Confianza: {data.Confianza:F2}",
                    Data = data,
                    Warnings = warnings
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en análisis simplificado de texto");

                return new SimplifiedInvoiceResultDto
                {
                    Success = false,
                    Message = $"Error en análisis simplificado: {ex.Message}"
                };
            }
        }

        // *** MÉTODOS DE DETECCIÓN ESPECÍFICOS Y SIMPLIFICADOS ***
        private ExtractedCodeDto? DetectArgentineInvoiceTypeSimplified(string text)
        {
            // Solo usar los patrones más efectivos para velocidad
            var strategies = new[]
            {
                ("InvoiceTypeAGrupo", _patterns["InvoiceTypeAGrupo"], 0.98f, "A", "001"),
                ("InvoiceTypeBGrupo", _patterns["InvoiceTypeBGrupo"], 0.98f, "B", "006"),
                ("InvoiceTypeAFactura", _patterns["InvoiceTypeAFactura"], 0.96f, "A", "001"),
                ("InvoiceTypeBFactura", _patterns["InvoiceTypeBFactura"], 0.96f, "B", "006"),
                ("InvoiceTypeASimple", _patterns["InvoiceTypeASimple"], 0.92f, "A", "001"),
                ("InvoiceTypeBSimple", _patterns["InvoiceTypeBSimple"], 0.92f, "B", "006")
            };

            foreach (var (patternName, pattern, confidence, tipo, codigo) in strategies)
            {
                var match = pattern.Match(text);
                if (match.Success)
                {
                    return new ExtractedCodeDto
                    {
                        Type = tipo == "A" ? "InvoiceTypeA" : "InvoiceTypeB",
                        Value = tipo,
                        Pattern = patternName,
                        Confidence = confidence
                    };
                }
            }

            // Fallback: correlación
            var lettersA = _patterns["LetterAAlone"].Matches(text);
            var lettersB = _patterns["LetterBAlone"].Matches(text);
            var codes001 = _patterns["Code001"].Matches(text);
            var codes006 = _patterns["Code006"].Matches(text);

            if (lettersA.Count > 0 && codes001.Count > 0)
            {
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeA",
                    Value = "A",
                    Pattern = "CorrelationA001",
                    Confidence = 0.75f
                };
            }

            if (lettersB.Count > 0 && codes006.Count > 0)
            {
                return new ExtractedCodeDto
                {
                    Type = "InvoiceTypeB",
                    Value = "B",
                    Pattern = "CorrelationB006",
                    Confidence = 0.75f
                };
            }

            return null;
        }

        private (string? tipoDetectado, string? codigoDetectado, float confidence) DetectArgentineInvoiceTypeAndCodeSimplified(string text)
        {
            _logger.LogInformation("Detectando tipo y código de factura argentina...");

            string? tipoDetectado = null;
            string? codigoDetectado = null;
            float maxConfidence = 0f;

            // Estrategia 1: Detectar tipo (A/B) con patrones existentes
            var strategies = new[]
            {
                ("InvoiceTypeAGrupo", _patterns["InvoiceTypeAGrupo"], 0.98f, "A", "001"),
                ("InvoiceTypeBGrupo", _patterns["InvoiceTypeBGrupo"], 0.98f, "B", "006"),
                ("InvoiceTypeAFactura", _patterns["InvoiceTypeAFactura"], 0.96f, "A", "001"),
                ("InvoiceTypeBFactura", _patterns["InvoiceTypeBFactura"], 0.96f, "B", "006"),
                ("InvoiceTypeASimple", _patterns["InvoiceTypeASimple"], 0.92f, "A", "001"),
                ("InvoiceTypeBSimple", _patterns["InvoiceTypeBSimple"], 0.92f, "B", "006")
            };

            foreach (var (patternName, pattern, confidence, tipo, codigo) in strategies)
            {
                var match = pattern.Match(text);
                if (match.Success)
                {
                    tipoDetectado = tipo;
                    codigoDetectado = codigo;
                    maxConfidence = confidence;
                    _logger.LogInformation("? Detectado con patrón {Pattern}: Tipo {Tipo}, Código {Codigo}", 
                        patternName, tipo, codigo);
                    return (tipoDetectado, codigoDetectado, maxConfidence);
                }
            }

            // Estrategia 2: Detectar solo códigos 001/006 si no se detectó el tipo
            var codes001 = _patterns["Code001"].Matches(text);
            var codes006 = _patterns["Code006"].Matches(text);

            if (codes001.Count > 0 && codes006.Count == 0)
            {
                codigoDetectado = "001";
                maxConfidence = 0.85f;
                _logger.LogInformation("? Detectado solo código 001 (sin letra A explícita)");
            }
            else if (codes006.Count > 0 && codes001.Count == 0)
            {
                codigoDetectado = "006";
                maxConfidence = 0.85f;
                _logger.LogInformation("? Detectado solo código 006 (sin letra B explícita)");
            }

            // Estrategia 3: Detectar solo letras A/B si no se detectó el código
            if (tipoDetectado == null && codigoDetectado == null)
            {
                var lettersA = _patterns["LetterAAlone"].Matches(text);
                var lettersB = _patterns["LetterBAlone"].Matches(text);

                if (lettersA.Count > 0 && lettersB.Count == 0)
                {
                    tipoDetectado = "A";
                    maxConfidence = 0.75f;
                    _logger.LogInformation("? Detectado solo letra A (sin código 001 explícito)");
                }
                else if (lettersB.Count > 0 && lettersA.Count == 0)
                {
                    tipoDetectado = "B";
                    maxConfidence = 0.75f;
                    _logger.LogInformation("? Detectado solo letra B (sin código 006 explícito)");
                }

                // Estrategia 4: Correlación entre letras y códigos
                if (tipoDetectado == null && lettersA.Count > 0 && codes001.Count > 0)
                {
                    tipoDetectado = "A";
                    codigoDetectado = "001";
                    maxConfidence = 0.8f;
                    _logger.LogInformation("? Detectado por correlación: A + 001");
                }
                else if (tipoDetectado == null && lettersB.Count > 0 && codes006.Count > 0)
                {
                    tipoDetectado = "B";
                    codigoDetectado = "006";
                    maxConfidence = 0.8f;
                    _logger.LogInformation("? Detectado por correlación: B + 006");
                }
            }

            if (tipoDetectado == null && codigoDetectado == null)
            {
                _logger.LogWarning("? No se pudo detectar tipo ni código de factura argentina");
            }

            return (tipoDetectado, codigoDetectado, maxConfidence);
        }

        private ExtractedCodeDto? DetectInvoiceNumber(string text)
        {
            // Patrón específico para números de factura como "N° 00704-00128327"
            var patterns = new[]
            {
                new Regex(@"N[°º""\*]?\s*(\d{5}-\d{8})", RegexOptions.IgnoreCase),
                new Regex(@"(?:factura|invoice|n[úu]mero|number|no\.?)\s*:?\s*([A-Z0-9\-]{8,20})", RegexOptions.IgnoreCase),
                new Regex(@"(\d{4,5}-\d{6,10})", RegexOptions.IgnoreCase)
            };

            foreach (var pattern in patterns)
            {
                var match = pattern.Match(text);
                if (match.Success)
                {
                    return new ExtractedCodeDto
                    {
                        Type = "InvoiceNumber",
                        Value = match.Groups[1].Value.Trim(),
                        Pattern = "InvoiceNumber",
                        Confidence = 0.9f
                    };
                }
            }

            return null;
        }

        private ExtractedDateDto? DetectInvoiceDate(string text)
        {
            // Patrón específico para fechas como "Fecha: 20/05/2025"
            var patterns = new[]
            {
                new Regex(@"(?:fecha|date):\s*(\d{1,2}\/\d{1,2}\/\d{4})", RegexOptions.IgnoreCase),
                new Regex(@"\b(\d{1,2}\/\d{1,2}\/\d{4})\b"),
                new Regex(@"\b(\d{1,2}-\d{1,2}-\d{4})\b"),
                new Regex(@"\b(\d{1,2}\.\d{1,2}\.\d{4})\b")
            };

            foreach (var pattern in patterns)
            {
                var match = pattern.Match(text);
                if (match.Success)
                {
                    if (DateTime.TryParseExact(match.Groups[1].Value, 
                        new[] { "dd/MM/yyyy", "dd-MM-yyyy", "dd.MM.yyyy" }, 
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    {
                        return new ExtractedDateDto
                        {
                            Type = "InvoiceDate",
                            ParsedDate = date,
                            OriginalText = match.Groups[1].Value,
                            Format = "DD/MM/YYYY",
                            Confidence = 0.9f
                        };
                    }
                }
            }

            return null;
        }

        private ExtractedCodeDto? DetectClientCuitSecond(string text)
        {
            _logger.LogInformation("?? Buscando CUIT del cliente (segundo CUIT en el documento)...");
            
            // Buscar todos los CUITs en el documento usando el patrón estándar
            var cuitPattern = new Regex(@"\b(\d{2}-\d{8}-\d{1})\b");
            var matches = cuitPattern.Matches(text);

            _logger.LogInformation("?? Total de CUITs encontrados en el documento: {Count}", matches.Count);
            
            // Loggear todos los CUITs encontrados para debugging
            for (int i = 0; i < matches.Count; i++)
            {
                _logger.LogInformation("CUIT #{Index}: {Cuit}", i + 1, matches[i].Groups[1].Value);
            }

            if (matches.Count >= 2)
            {
                // Tomar el segundo CUIT (índice 1)
                var secondCuit = matches[1].Groups[1].Value;
                _logger.LogInformation("? CUIT del cliente seleccionado (segundo): {Cuit}", secondCuit);
                
                return new ExtractedCodeDto
                {
                    Type = "ClientCUIT",
                    Value = secondCuit,
                    Pattern = "CUIT_Second",
                    Confidence = 0.95f
                };
            }
            else if (matches.Count == 1)
            {
                // Si solo hay un CUIT, advertir pero tomarlo
                var onlyCuit = matches[0].Groups[1].Value;
                _logger.LogWarning("??  Solo se encontró un CUIT en el documento. Se asume que es del cliente: {Cuit}", onlyCuit);
                
                return new ExtractedCodeDto
                {
                    Type = "ClientCUIT",
                    Value = onlyCuit,
                    Pattern = "CUIT_OnlyOne",
                    Confidence = 0.7f // Menor confianza porque no estamos seguros
                };
            }
            else
            {
                _logger.LogWarning("? No se encontraron CUITs en el documento");
                return null;
            }
        }
    }
}