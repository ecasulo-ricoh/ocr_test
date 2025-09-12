# OCR Test API - Gu�a de Configuraci�n y Uso

## ?? Resumen
Este proyecto replica los endpoints de visualizaci�n de documentos del proyecto DocuWare Utils y a�ade capacidades de OCR usando Tesseract.

## ??? Configuraci�n Inicial

### 1. Dependencias Instaladas
- `DocuWare.Platform.ServerClient` v7.10.0 - SDK oficial de DocuWare
- `Tesseract` v5.2.0 - Motor de OCR
- `Swashbuckle.AspNetCore` v6.6.2 - Documentaci�n Swagger

### 2. Configuraci�n en appsettings.json
```json
{
  "DWEnvVariables": {
    "User": "soporte",
    "Password": "Ricoh2021DW", 
    "Uri": "https://nat-ar.docuware.cloud/DocuWare/Platform",
    "CabinetGUID": "169bbeee-9fd5-4e7c-b9cf-2b82ea7de6a2"
  },
  "Tesseract": {
    "DataPath": "./tessdata",
    "Language": "eng+spa"
  }
}
```

### 3. Archivos de Idioma de Tesseract
Necesitas descargar los archivos de entrenamiento de Tesseract:

1. Crear carpeta `tessdata` en la ra�z del proyecto
2. Descargar desde: https://github.com/tesseract-ocr/tessdata
   - `eng.traineddata` (ingl�s)
   - `spa.traineddata` (espa�ol)

## ?? Endpoints Disponibles

### ?? Documentos de DocuWare

#### GET `/api/documents/{documentId}`
Obtiene informaci�n de un documento espec�fico.

**Par�metros:**
- `documentId` (int): ID del documento
- `fileCabinetId` (query, opcional): ID del FileCabinet

#### GET `/api/documents/view/{documentId}`
**?? ENDPOINT PRINCIPAL PARA VISUALIZACI�N**

Visualiza un documento en el navegador (equivalente al del proyecto original).

**Uso para obtener blob:**
```bash
GET /api/documents/view/12345
```

**Respuesta:** Stream del documento (PDF) listo para:
- Visualizaci�n en navegador
- Procesamiento con OCR
- Descarga

#### GET `/api/documents/{documentId}/download`
Descarga un documento como archivo.

### ?? OCR (Reconocimiento �ptico de Caracteres)

#### POST `/api/documents/{documentId}/ocr`
**?? ENDPOINT PRINCIPAL PARA OCR DE DOCUMENTOS DOCUWARE**

Extrae texto de un documento de DocuWare usando OCR.

**Par�metros:**
- `documentId` (int): ID del documento en DocuWare
- `fileCabinetId` (query, opcional): ID del FileCabinet
- `language` (query, opcional): Idioma para OCR (ej: "eng", "spa", "eng+spa")

**Ejemplo de uso:**
```bash
POST /api/documents/12345/ocr?language=eng+spa
```

#### POST `/api/documents/ocr/upload`
Extrae texto de un archivo subido.

**Par�metros:**
- `file` (form-data): Archivo de imagen o PDF
- `language` (query, opcional): Idioma para OCR

## ?? Flujo de Prueba Recomendado

### 1. Probar Conectividad con DocuWare
```bash
GET /api/documents/12345
```
Esto validar� que la conexi�n con DocuWare funciona correctamente.

### 2. Probar Visualizaci�n (Obtener Blob)
```bash
GET /api/documents/view/12345
```
Este es el endpoint equivalente al del proyecto original. Debe devolver el PDF como stream.

### 3. Probar OCR en Documento de DocuWare
```bash
POST /api/documents/12345/ocr?language=eng+spa
```
Esto:
1. Obtiene el documento desde DocuWare (usando el endpoint de visualizaci�n)
2. Aplica OCR con Tesseract
3. Devuelve el texto extra�do

### 4. Probar OCR con Archivo Local
```bash
POST /api/documents/ocr/upload
```
Con un archivo PDF o imagen en form-data.

## ?? Estructura del Proyecto

```
OCR_test/
??? Controllers/
?   ??? DocumentsController.cs          # Endpoints principales
??? Models/DTOs/DocuWare/
?   ??? DocumentDtos.cs                  # DTOs para DocuWare y OCR
??? Services/
?   ??? Interfaces/
?   ?   ??? IDocuWareServices.cs        # Interfaces para DocuWare
?   ?   ??? IDocumentAndOcrServices.cs  # Interfaces para documentos y OCR
?   ??? Implementations/
?       ??? DocuWareConfigurationService.cs
?       ??? DocuWareConnectionService.cs
?       ??? DocuWareDocumentService.cs  # ? Servicio principal para documentos
?       ??? OcrService.cs               # ? Servicio de OCR con Tesseract
??? appsettings.json                    # Configuraci�n
```

## ?? Servicios Implementados

### DocuWareDocumentService
- ? `GetDocumentAsync()` - Informaci�n del documento
- ? `ViewDocumentAsync()` - **Stream del documento (equivalente al original)**
- ? `DownloadDocumentAsync()` - Descarga del documento

### OcrService  
- ? `ExtractTextFromStreamAsync()` - OCR desde stream
- ? `ExtractTextFromDocumentAsync()` - OCR desde documento DocuWare

## ?? Pr�ximos Pasos para OCR

1. **Probar endpoint de visualizaci�n:** Verificar que obtienes el blob correctamente
2. **Configurar Tesseract:** Descargar archivos de idioma
3. **Probar OCR b�sico:** Con el endpoint de upload primero
4. **Probar OCR con DocuWare:** Usar el endpoint principal

## ?? Soluci�n de Problemas

### Error de Conexi�n DocuWare
- Verificar credenciales en `appsettings.json`
- Verificar conectividad a la URL de DocuWare

### Error de Tesseract
- Verificar que existe la carpeta `tessdata`
- Verificar que los archivos `.traineddata` est�n presentes
- Verificar permisos de lectura en la carpeta

### Error de FileCabinet
- Verificar que el `CabinetGUID` es correcto
- Verificar permisos del usuario en el FileCabinet

## ?? Notas Importantes

- El endpoint `/api/documents/view/{documentId}` es el equivalente exacto al del proyecto original
- La configuraci�n de DocuWare usa la misma estructura del proyecto original
- Los servicios est�n registrados con los mismos ciclos de vida
- Swagger est� configurado en la ra�z (`/`) para facilitar las pruebas