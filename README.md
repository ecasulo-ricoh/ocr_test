# OCR Test API - Sistema de Actualización Masiva de Facturas

## ?? Descripción

API para análisis de facturas argentinas mediante OCR y actualización masiva de campos en DocuWare. El sistema detecta automáticamente el tipo de factura (A o B), códigos, números, fechas y CUIT del cliente.

## ?? Características Principales

- **Análisis OCR**: Extracción automática de campos de facturas
- **Actualización Masiva**: Procesamiento de múltiples documentos en lote
- **Validaciones Robustas**: Control de formatos y datos según normas argentinas
- **Modo DRY-RUN**: Simulación segura antes de actualizar
- **Logging Detallado**: Trazabilidad completa de operaciones

## ?? Endpoints Principales

### 1. Análisis Simplificado de Factura Individual
```http
POST /api/invoices/analyze-simplified/{documentId}
```

### 2. Actualización Masiva de Documentos
```http
POST /api/docuware/bulk-update
```

### 3. Información de Mapeo de Campos
```http
GET /api/docuware/field-mapping
```

## ?? Actualización Masiva - Parámetros

### Request JSON
```json
{
  "documentCount": 10,           // Cantidad de documentos a procesar
  "dryRun": true,                // true = simulación, false = actualización real
  "onlyUpdateEmptyFields": true  // true = solo campos vacíos, false = sobrescribir todos
}
```

### Parámetros Detallados

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `documentCount` | `int` | - | Cantidad de documentos a procesar (1-1000) |
| `dryRun` | `bool` | `true` | Modo simulación (no modifica datos) |
| `onlyUpdateEmptyFields` | `bool` | `true` | Solo actualizar campos vacíos en DocuWare |

## ? Validaciones Implementadas

### 1. **Número de Factura (NDEG_FACTURA)**
- **Formato requerido**: `00000-00000000` (5 dígitos + guión + 8 dígitos)
- **Ejemplos válidos**: 
  - `12345-87654321` ?
  - `00704-00128327` ?
- **Ejemplos inválidos**:
  - `1234-87654321` ? (4 dígitos)
  - `12345-8765432A` ? (contiene letra)
  - `12345.87654321` ? (punto en lugar de guión)

### 2. **Tipo de Factura (LETRA_DOCUMENTO)**
- **Valores permitidos**: Solo `A` o `B`
- **Ejemplos válidos**: `A` ?, `B` ?
- **Ejemplos inválidos**: `C` ?, `1` ?, `a` ?

### 3. **Código de Factura (CODIGO_DOCUMENTO)**
- **Valores permitidos**: Solo `001` (para tipo A) o `006` (para tipo B)
- **Ejemplos válidos**: `001` ?, `006` ?
- **Ejemplos inválidos**: `002` ?, `1` ?, `01` ?

### 4. **Fecha (DATE)**
- **Formato requerido**: `DD/MM/yyyy`
- **Ejemplos válidos**: `20/05/2025` ?, `01/12/2024` ?
- **Ejemplos inválidos**: `2025-05-20` ?, `20-05-2025` ?

### 5. **CUIT Cliente (CUIT_CLIENTE)**
- **Formato requerido**: `XX-XXXXXXXX-X` (2 dígitos + guión + 8 dígitos + guión + 1 dígito)
- **Ejemplos válidos**: `30-58584975-1` ?, `20-12345678-9` ?
- **Ejemplos inválidos**: `30585849751` ?, `30-585849-75-1` ?

## ?? Estrategias de Actualización

### Modo: `onlyUpdateEmptyFields: true` (por defecto)
- ? Actualiza **solo campos vacíos** en DocuWare
- ? **Conserva datos existentes**
- ? **Más seguro** para documentos ya procesados
- ? **Evita sobrescribir** información manual

### Modo: `onlyUpdateEmptyFields: false`
- ?? **Sobrescribe todos los campos** detectados por OCR
- ?? **Puede eliminar datos existentes**
- ?? Usar con precaución en documentos ya procesados

## ?? Ejemplos de Uso

### 1. Simulación (Recomendado para pruebas)
```bash
curl -X POST "http://localhost:5000/api/docuware/bulk-update" \
-H "Content-Type: application/json" \
-d '{
  "documentCount": 5,
  "dryRun": true,
  "onlyUpdateEmptyFields": true
}'
```

### 2. Actualización Real - Solo Campos Vacíos
```bash
curl -X POST "http://localhost:5000/api/docuware/bulk-update" \
-H "Content-Type: application/json" \
-d '{
  "documentCount": 10,
  "dryRun": false,
  "onlyUpdateEmptyFields": true
}'
```

### 3. Actualización Real - Sobrescribir Todo
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

| Campo OCR | Campo DocuWare | Validación | Descripción |
|-----------|----------------|------------|-------------|
| `tipoFactura` | `LETRA_DOCUMENTO` | A o B | Tipo de factura argentina |
| `codigoFactura` | `CODIGO_DOCUMENTO` | 001 o 006 | Código según tipo de factura |
| `nroFactura` | `NDEG_FACTURA` | 00000-00000000 | Número de factura |
| `fechaFactura` | `DATE` | DD/MM/yyyy | Fecha de emisión |
| `cuitCliente` | `CUIT_CLIENTE` | XX-XXXXXXXX-X | **SIEMPRE el segundo CUIT del documento** |

**Notas importantes**:
- ? **CUIT del Cliente**: Se toma **SIEMPRE el segundo CUIT** que aparece en el documento (el primero es del vendedor)
- ? **Razón Social**: Ya **no se procesa ni actualiza** por requerimiento del usuario

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
      "message": "DRY-RUN: Actualización simulada exitosamente",
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
- **`Failed`**: Error en OCR o actualización de DocuWare
- **`NoChanges`**: No hay campos válidos para actualizar
- **`Skipped`**: Documento omitido por validaciones

### Campos de Validación en la Respuesta
- **`skippedFields`**: Lista de campos que no se actualizaron
- **`validationWarnings`**: Advertencias sobre formatos o valores inválidos
- **`errors`**: Errores técnicos durante el procesamiento

## ?? Logging Detallado

### Ejemplos de Logs
```
?? Iniciando actualización masiva en modo DRY-RUN con estrategia SOLO CAMPOS VACÍOS. Documentos a procesar: 5
?? Procesando documento 1/5: ID 1
? LETRA_DOCUMENTO validado: A para documento 1
? CODIGO_DOCUMENTO validado: 001 para documento 1
?? NDEG_FACTURA formato inválido para documento 1: 1234-87654321 - Revisar manualmente
? DATE validado: 20/05/2025 para documento 1
?? Progreso: 5/5 documentos procesados. Exitosos: 4, Fallidos: 0, Omitidos: 1
```

### Identificadores de Log
- ?? Inicio de proceso
- ?? Procesamiento de documento individual
- ? Validación exitosa
- ?? Advertencia de validación
- ? Error
- ?? Progreso/Estadísticas
- ?? Campo omitido

## ?? Configuración

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

## ?? Mejores Prácticas

### 1. **Antes de Usar en Producción**
1. Ejecutar siempre en modo `dryRun: true` primero
2. Revisar los logs de validación
3. Verificar los `validationWarnings`
4. Comprobar que el `successRate` sea aceptable

### 2. **Recomendaciones de Uso**
- Empezar con lotes pequeños (5-10 documentos)
- Usar `onlyUpdateEmptyFields: true` por defecto
- Revisar manualmente documentos con warnings
- Verificar formatos de números de factura

### 3. **Monitoreo**
- Revisar logs de `NDEG_FACTURA formato inválido`
- Verificar documentos con `status: "Failed"`
- Monitorear `performance.documentsPerSecond`

## ?? Resolución de Problemas

### Número de Factura Inválido
```
?? NDEG_FACTURA formato inválido para documento 123: 1234-87654321 - Revisar manualmente
```
**Solución**: Verificar que el número tenga exactamente 5 dígitos, guión, y 8 dígitos.

### Campo Ya Tiene Valor
```
?? LETRA_DOCUMENTO omitido para documento 123: campo ya tiene valor
```
**Solución**: Normal con `onlyUpdateEmptyFields: true`. Usar `false` si quieres sobrescribir.

### Error de Conexión DocuWare
```
? Error actualizando documento 123 en DocuWare
```
**Solución**: Verificar configuración en `appsettings.json` y conectividad.

## ?? Endpoints de Utilidad

### Obtener Lista de Documentos
```http
GET /api/docuware/document-list?count=10
```

### Información de Mapeo de Campos
```http
GET /api/docuware/field-mapping
```

### Análisis Individual
```http
POST /api/invoices/analyze-simplified/{documentId}
```

---

## ?? Flujo Recomendado de Trabajo

1. **Prueba Individual**: Analizar 1-2 documentos con `/analyze-simplified/`
2. **Simulación**: Ejecutar `bulk-update` con `dryRun: true`
3. **Revisión**: Examinar logs y `validationWarnings`
4. **Producción**: Ejecutar con `dryRun: false` en lotes pequeños
5. **Monitoreo**: Revisar resultados y ajustar según necesidades

Esta API está diseñada para procesar facturas argentinas de manera segura y eficiente, con validaciones robustas y logging detallado para facilitar el mantenimiento y resolución de problemas.

---

## ?? Configuración Original OCR

### Dependencias Instaladas
- `DocuWare.Platform.ServerClient` v7.10.0 - SDK oficial de DocuWare
- `Tesseract` v5.2.0 - Motor de OCR
- `Swashbuckle.AspNetCore` v6.6.2 - Documentación Swagger

### Configuración de Tesseract
1. Crear carpeta `tessdata` en la raíz del proyecto
2. Descargar desde: https://github.com/tesseract-ocr/tessdata
   - `eng.traineddata` (inglés)
   - `spa.traineddata` (español)

### Endpoints OCR Básicos
- `GET /api/documents/view/{documentId}` - Visualizar documento
- `POST /api/documents/{documentId}/ocr` - OCR desde DocuWare
- `POST /api/documents/ocr/upload` - OCR desde archivo subido