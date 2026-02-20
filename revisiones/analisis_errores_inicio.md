# Análisis de Fallos y Soluciones - Nodus Server

A continuación se detallan las razones por las cuales el servidor de Nodus fallaba al iniciar y las soluciones aplicadas para estabilizarlo.

### Razones de los Errores (Por qué fallaba)
*   **Servicios no registrados:** Faltaba registrar la interfaz `IFileSaverService` en el contenedor de dependencias (`MauiProgram.cs`), bloqueando la creación de la página principal.
*   **Directorio de base de datos inexistente:** La aplicación intentaba abrir la base de datos LiteDB antes de asegurar que la carpeta de destino existiera en el sistema.
*   **Conflicto de inyección de dependencias:** El servicio `ExportService` estaba programado para usar una clase específica (`DatabaseService`) en lugar de la interfaz genérica, lo que impedía que el sistema pudiera iniciarlo correctamente.
*   **Interfaz incompleta:** La nueva interfaz de base de datos no contaba con el método `GetAllVotesAsync` que los reportes de exportación requerían para funcionar.
*   **Cierre silencioso:** No existía un sistema de captura de errores durante el arranque, lo que hacía que cualquier excepción terminara el proceso sin dar avisos al usuario o desarrollador.

### Soluciones Implementadas
*   **Vinculación de dependencias:** Se registraron todos los servicios necesarios en `MauiProgram.cs`, incluyendo los servicios de exportación y gestión de archivos.
*   **Aseguramiento de rutas:** Se añadió lógica en `LocalDatabaseService.cs` para verificar y crear automáticamente la carpeta de datos del programa en `LocalApplicationData`.
*   **Uso de abstracciones:** Se refactorizó `ExportService` para que ahora use la interfaz `IDatabaseService`, permitiéndole funcionar independientemente de si se usa LiteDB, MongoDB o SQLite.
*   **Estandarización de interfaz:** Se actualizó `IDatabaseService` y todos sus implementadores (`MongoDbService`, `LocalDatabaseService`) para incluir y dar soporte a la obtención completa de votos.
*   **Sistema de diagnóstico activo:** Se integró un logger temporal que imprime errores en la terminal y guarda un registro detallado en la carpeta de **Documentos** del usuario (`Nodus_Debug.log`) para facilitar la resolución de problemas futuros.
