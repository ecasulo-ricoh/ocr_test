# OCR Test API - Sistema de Actualizaci�n Masiva de Facturas

## ?? Descripci�n

API para an�lisis de facturas argentinas mediante OCR y actualizaci�n masiva de campos en DocuWare. El sistema detecta autom�ticamente el tipo de factura (A o B), c�digos, n�meros, fechas y CUIT del cliente.

## ?? Caracter�sticas Principales

- **An�lisis OCR**: Extracci�n autom�tica de campos de facturas
- **Actualizaci�n Masiva**: Procesamiento de m�ltiples documentos en lote
- **Validaciones Robustas**: Control de formatos y datos seg�n normas argentinas
- **Modo DRY-RUN**: Simulaci�n segura antes de actualizar
- **Logging Detallado**: Trazabilidad completa de operaciones

## ?? Endpoints Principales

### 1. An�lisis Simplificado de Factura Individual
```http
POST /api/invoices/analyze-simplified/{documentId}
```

### 2. Actualizaci�n Masiva de Documentos
```http
POST /api/docuware/bulk-update
```

### 3. Informaci�n de Mapeo de Campos
```http
GET /api/docuware/field-mapping
```

## ?? Actualizaci�n Masiva - Par�metros

### Request JSON
```json
{
  "documentCount": 10,           // Cantidad de documentos a procesar
  "dryRun": true,                // true = simulaci�n, false = actualizaci�n real
  "onlyUpdateEmptyFields": true  // true = solo campos vac�os, false = sobrescribir todos
}
```

### Par�metros Detallados

| Par�metro | Tipo | Default | Descripci�n |
|-----------|------|---------|-------------|
| `documentCount` | `int` | - | Cantidad de documentos a procesar (1-1000) |
| `dryRun` | `bool` | `true` | Modo simulaci�n (no modifica datos) |
| `onlyUpdateEmptyFields` | `bool` | `true` | Solo actualizar campos vac�os en DocuWare |

## ? Validaciones Implementadas

### 1. **N�mero de Factura (NDEG_FACTURA)**
- **Formato requerido**: `00000-00000000` (5 d�gitos + gui�n + 8 d�gitos)
- **Ejemplos v�lidos**: 
  - `12345-87654321` ?
  - `00704-00128327` ?
- **Ejemplos inv�lidos**:
  - `1234-87654321` ? (4 d�gitos)
  - `12345-8765432A` ? (contiene letra)
  - `12345.87654321` ? (punto en lugar de gui�n)

### 2. **Tipo de Factura (LETRA_DOCUMENTO)**
- **Valores permitidos**: Solo `A` o `B`
- **Ejemplos v�lidos**: `A` ?, `B` ?
- **Ejemplos inv�lidos**: `C` ?, `1` ?, `a` ?

### 3. **C�digo de Factura (CODIGO_DOCUMENTO)**
- **Valores permitidos**: Solo `001` (para tipo A) o `006` (para tipo B)
- **Ejemplos v�lidos**: `001` ?, `006` ?
- **Ejemplos inv�lidos**: `002` ?, `1` ?, `01` ?

### 4. **Fecha (DATE)**
- **Formato requerido**: `DD/MM/yyyy`
- **Ejemplos v�lidos**: `20/05/2025` ?, `01/12/2024` ?
- **Ejemplos inv�lidos**: `2025-05-20` ?, `20-05-2025` ?

### 5. **CUIT Cliente (CUIT_CLIENTE)**
- **Formato requerido**: `XX-XXXXXXXX-X` (2 d�gitos + gui�n + 8 d�gitos + gui�n + 1 d�gito)
- **Ejemplos v�lidos**: `30-58584975-1` ?, `20-12345678-9` ?
- **Ejemplos inv�lidos**: `30585849751` ?, `30-585849-75-1` ?

## ?? Estrategias de Actualizaci�n

### Modo: `onlyUpdateEmptyFields: true` (por defecto)
- ? Actualiza **solo campos vac�os** en DocuWare
- ? **Conserva datos existentes**
- ? **M�s seguro** para documentos ya procesados
- ? **Evita sobrescribir** informaci�n manual

### Modo: `onlyUpdateEmptyFields: false`
- ?? **Sobrescribe todos los campos** detectados por OCR
- ?? **Puede eliminar datos existentes**
- ?? Usar con precauci�n en documentos ya procesados

## ?? Ejemplos de Uso

### 1. Simulaci�n (Recomendado para pruebas)
```bash
curl -X POST "http://localhost:5000/api/docuware/bulk-update" \
-H "Content-Type: application/json" \
-d '{
  "documentCount": 5,
  "dryRun": true,
  "onlyUpdateEmptyFields": true
}'
```

### 2. Actualizaci�n Real - Solo Campos Vac�os
```bash
curl -X POST "http://localhost:5000/api/docuware/bulk-update" \
-H "Content-Type: application/json" \
-d '{
  "documentCount": 10,
  "dryRun": false,
  "onlyUpdateEmptyFields": true
}'
```

### 3. Actualizaci�n Real - Sobrescribir Todo
```bash
curl -X POST "http://localhost:5000/api/docuware/bulk-update" \
-H "Content-Type: application/json" \
-d '{
  "documentCount": 10,
  "dryRun": false,
  "onlyUpdateEmptyFields": false
}'
```

## ?? Mapeo de Campos

| Campo OCR | Campo DocuWare | Validaci�n | Descripci�n |
|-----------|----------------|------------|-------------|
| `tipoFactura` | `LETRA_DOCUMENTO` | A o B | Tipo de factura argentina |
| `codigoFactura` | `CODIGO_DOCUMENTO` | 001 o 006 | C�digo seg�n tipo de factura |
| `nroFactura` | `NDEG_FACTURA` | 00000-00000000 | N�mero de factura |
| `fechaFactura` | `DATE` | DD/MM/yyyy | Fecha de emisi�n |
| `cuitCliente` | `CUIT_CLIENTE` | XX-XXXXXXXX-X | **SIEMPRE el segundo CUIT del documento** |

**Notas importantes**:
- ? **CUIT del Cliente**: Se toma **SIEMPRE el segundo CUIT** que aparece en el documento (el primero es del vendedor)
- ? **Raz�n Social**: Ya **no se procesa ni actualiza** por requerimiento del usuario

## ?? Respuesta JSON

```json
{
  "success": true,
  "message": "Procesamiento completado en modo DRY-RUN...",
  "summary": {
    "totalProcessed": 10,
    "successfulUpdates": 8,
    "failedUpdates": 1,
    "skippedDocuments": 1,
    "successRate": 80.0
  },
  "metadata": {
    "mode": "DRY-RUN",
    "updateStrategy": "EMPTY_ONLY",
    "fileCabinetId": "e2cd4b35-bbef-4118-8229-3ba662b05e56",
    "language": "spa+eng",
    "performance": {
      "documentsPerSecond": 2.5,
      "averageOcrTimeMs": 850,
      "averageUpdateTimeMs": 120
    }
  },
  "details": [
    {
      "documentId": 1,
      "status": "Success",
      "message": "DRY-RUN: Actualizaci�n simulada exitosamente",
      "detectedFields": {
        "tipoFactura": "A",
        "codigoFactura": "001",
        "nroFactura": "00704-00128327",
        "fechaFactura": "20/05/2025",
        "cuitCliente": "30-58584975-1",
        "confianza": 0.92
      },
      "updatedFields": {
        "LETRA_DOCUMENTO": "A",
        "CODIGO_DOCUMENTO": "001",
        "NDEG_FACTURA": "00704-00128327",
        "DATE": "20/05/2025",
        "CUIT_CLIENTE": "30-58584975-1"
      },
      "skippedFields": [],
      "validationWarnings": []
    }
  ]
}
```

## ?? Manejo de Errores y Validaciones

### Tipos de Status por Documento
- **`Success`**: Documento procesado y actualizado exitosamente
- **`Failed`**: Error en OCR o actualizaci�n de DocuWare
- **`NoChanges`**: No hay campos v�lidos para actualizar
- **`Skipped`**: Documento omitido por validaciones

### Campos de Validaci�n en la Respuesta
- **`skippedFields`**: Lista de campos que no se actualizaron
- **`validationWarnings`**: Advertencias sobre formatos o valores inv�lidos
- **`errors`**: Errores t�cnicos durante el procesamiento

## ?? Logging Detallado

### Ejemplos de Logs
```
?? Iniciando actualizaci�n masiva en modo DRY-RUN con estrategia SOLO CAMPOS VAC�OS. Documentos a procesar: 5
?? Procesando documento 1/5: ID 1
? LETRA_DOCUMENTO validado: A para documento 1
? CODIGO_DOCUMENTO validado: 001 para documento 1
?? NDEG_FACTURA formato inv�lido para documento 1: 1234-87654321 - Revisar manualmente
? DATE validado: 20/05/2025 para documento 1
?? Progreso: 5/5 documentos procesados. Exitosos: 4, Fallidos: 0, Omitidos: 1
```

### Identificadores de Log
- ?? Inicio de proceso
- ?? Procesamiento de documento individual
- ? Validaci�n exitosa
- ?? Advertencia de validaci�n
- ? Error
- ?? Progreso/Estad�sticas
- ?? Campo omitido

## ?? Configuraci�n

### appsettings.json
```json
{
  "DWEnvVariables": {
    "User": "usuario_docuware",
    "Password": "password_docuware",
    "Uri": "https://servidor.docuware.cloud/DocuWare/Platform",
    "CabinetGUID": "id-del-filecabinet"
  },
  "Tesseract": {
    "DataPath": "./tessdata",
    "Language": "spa+eng"
  }
}
```

## ?? Mejores Pr�cticas

### 1. **Antes de Usar en Producci�n**
1. Ejecutar siempre en modo `dryRun: true` primero
2. Revisar los logs de validaci�n
3. Verificar los `validationWarnings`
4. Comprobar que el `successRate` sea aceptable

### 2. **Recomendaciones de Uso**
- Empezar con lotes peque�os (5-10 documentos)
- Usar `onlyUpdateEmptyFields: true` por defecto
- Revisar manualmente documentos con warnings
- Verificar formatos de n�meros de factura

### 3. **Monitoreo**
- Revisar logs de `NDEG_FACTURA formato inv�lido`
- Verificar documentos con `status: "Failed"`
- Monitorear `performance.documentsPerSecond`

## ?? Resoluci�n de Problemas

### N�mero de Factura Inv�lido
```
?? NDEG_FACTURA formato inv�lido para documento 123: 1234-87654321 - Revisar manualmente
```
**Soluci�n**: Verificar que el n�mero tenga exactamente 5 d�gitos, gui�n, y 8 d�gitos.

### Campo Ya Tiene Valor
```
?? LETRA_DOCUMENTO omitido para documento 123: campo ya tiene valor
```
**Soluci�n**: Normal con `onlyUpdateEmptyFields: true`. Usar `false` si quieres sobrescribir.

### Error de Conexi�n DocuWare
```
? Error actualizando documento 123 en DocuWare
```
**Soluci�n**: Verificar configuraci�n en `appsettings.json` y conectividad.

## ?? Endpoints de Utilidad

### Obtener Lista de Documentos
```http
GET /api/docuware/document-list?count=10
```

### Informaci�n de Mapeo de Campos
```http
GET /api/docuware/field-mapping
```

### An�lisis Individual
```http
POST /api/invoices/analyze-simplified/{documentId}
```

---

## ?? Flujo Recomendado de Trabajo

1. **Prueba Individual**: Analizar 1-2 documentos con `/analyze-simplified/`
2. **Simulaci�n**: Ejecutar `bulk-update` con `dryRun: true`
3. **Revisi�n**: Examinar logs y `validationWarnings`
4. **Producci�n**: Ejecutar con `dryRun: false` en lotes peque�os
5. **Monitoreo**: Revisar resultados y ajustar seg�n necesidades

Esta API est� dise�ada para procesar facturas argentinas de manera segura y eficiente, con validaciones robustas y logging detallado para facilitar el mantenimiento y resoluci�n de problemas.

---

## ?? Configuraci�n Original OCR

### Dependencias Instaladas
- `DocuWare.Platform.ServerClient` v7.10.0 - SDK oficial de DocuWare
- `Tesseract` v5.2.0 - Motor de OCR
- `Swashbuckle.AspNetCore` v6.6.2 - Documentaci�n Swagger

### Configuraci�n de Tesseract
1. Crear carpeta `tessdata` en la ra�z del proyecto
2. Descargar desde: https://github.com/tesseract-ocr/tessdata
   - `eng.traineddata` (ingl�s)
   - `spa.traineddata` (espa�ol)

### Endpoints OCR B�sicos
- `GET /api/documents/view/{documentId}` - Visualizar documento
- `POST /api/documents/{documentId}/ocr` - OCR desde DocuWare
- `POST /api/documents/ocr/upload` - OCR desde archivo subido