# 🍃 Plan de Migración: SQLite → MongoDB
## Proyecto Nodus App — Rama: `octavio-cambios`

---

## ¿Qué es este directorio?

Contiene todos los archivos necesarios para migrar la capa de datos del proyecto Nodus de
**SQLite** (`sqlite-net-pcl`) a **MongoDB** (`MongoDB.Driver`).

La migración **no rompe** el código existente porque respeta el contrato de `IDatabaseService`.

---

## 📁 Estructura de archivos

```
migracion-NoSql/
├── Models/
│   ├── Judge.cs             → Modelo NUEVO (juez → MongoDB)
│   └── NodusDocuments.cs    → Documentos Mongo para Event, Project, Vote
├── MongoDbService.cs        → Implementación de IDatabaseService con MongoDB
└── README-migracion.md      → Este archivo
```

---

## 🗄️ Colecciones en MongoDB

| Colección   | Origen SQLite | Nueva |
|-------------|---------------|-------|
| `events`    | Tabla `Event` | ❌    |
| `projects`  | Tabla `Project` | ❌  |
| `votes`     | Tabla `Vote`  | ❌    |
| `judges`    | SecureStorage | ✅ **Nueva** |

---

## 🔑 Índices definidos

```
projects.eventId              → Ascending
votes.eventId                 → Ascending
votes.projectId               → Ascending
votes.judgeId                 → Ascending
votes.status                  → Ascending
votes.(localPhotoPath, isMediaSynced)  → Partial (solo donde hay foto pendiente)
judges.eventId                → Ascending
```

---

## 📋 Pasos de migración

### Paso 1 — Instalar el driver de MongoDB
```bash
dotnet add Nodus.Shared package MongoDB.Driver
```

### Paso 2 — Copiar archivos al proyecto Shared
```
migracion-NoSql/Models/Judge.cs         → Nodus.Shared/Models/Judge.cs
migracion-NoSql/Models/NodusDocuments.cs → Nodus.Shared/Models/NodusDocuments.cs
migracion-NoSql/MongoDbService.cs        → Nodus.Shared/Services/MongoDbService.cs
```

### Paso 3 — Actualizar IDatabaseService
Agregar las nuevas operaciones de juez al contrato de la interfaz:

```csharp
// En IDatabaseService.cs, agregar:
Task<Result<List<Judge>>> GetJudgesAsync(string eventId, CancellationToken ct = default);
Task<Result<Judge>> GetJudgeAsync(string id, CancellationToken ct = default);
Task<Result> SaveJudgeAsync(Judge judge, CancellationToken ct = default);
```

### Paso 4 — Actualizar Program.cs / MauiProgram.cs
Reemplazar el registro de SQLite con MongoDB en cada proyecto:

**Nodus.Web/Program.cs**
```csharp
// ANTES (SQLite):
builder.Services.AddSingleton<IDatabaseService>(sp => {
    var logger = ...;
    return new DatabaseService("nodus_web.db", logger);
});

// DESPUÉS (MongoDB):
builder.Services.AddSingleton<IDatabaseService>(sp => {
    var logger = sp.GetRequiredService<ILogger<MongoDbService>>();
    return new MongoDbService("mongodb://localhost:27017", "nodus_db", logger);
});
```

**Nodus.Client/MauiProgram.cs** y **Nodus.Server/MauiProgram.cs** → mismo patrón.

### Paso 5 — Registrar Judge en JudgeRegistrationViewModel
Después del registro por QR, además de guardar en SecureStorage, guardar en MongoDB:

```csharp
// Al final de PerformRegistrationAsync(), agregar:
var judge = new Judge
{
    Id = $"JUDGE-{name}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
    Name = name,
    PublicKey = keys.PublicKey,
    EventId = eventId,
    RegisteredAtUtc = DateTime.UtcNow,
    IsActive = true
};
await _db.SaveJudgeAsync(judge, ct);
```

### Paso 6 — Configurar MongoDB Local (Desarrollo)
```bash
# Opción A: MongoDB sin Replica Set (sin transacciones)
mongod --dbpath C:/data/db

# Opción B: Con Replica Set (permite transacciones)
mongod --replSet rs0 --dbpath C:/data/db
# Luego en mongosh:
rs.initiate()
```

### Paso 7 — MongoDB Atlas (Producción)
Reemplazar connection string con URI de Atlas:
```
mongodb+srv://usuario:password@cluster.mongodb.net/nodus_db
```

---

## ⚠️ Puntos importantes

### Transacciones
`ExecuteInTransactionAsync` en `MongoDbService` retorna error indicando que se requiere
Replica Set. Las operaciones `upsert` en MongoDB son atómicas a nivel de documento,
lo cual es suficiente para el 99% de los casos del proyecto.

### PayloadJson → Payload (BsonDocument)
En SQLite: `PayloadJson = "{\"Design\": 8, \"Functionality\": 9}"` (string)
En MongoDB: `Payload = BsonDocument` (objeto nativo)

Esto permite queries directamente sobre los scores sin parsear en C#:
```js
db.votes.find({ "payload.Design": { $gt: 7 } })
```

### Offline-First
El patrón `SyncStatus (Pending/Synced/SyncError)` se mantiene igual.
MongoDB funciona mejor como **servidor central** mientras SQLite sigue siendo la
base local del kiosco/cliente. Se puede mantener la arquitectura híbrida.

---

## 🏗️ Arquitectura recomendada final

```
[Nodus.Client (juez)]  ──BLE──▶  [Nodus.Server (coordinador)]
       SQLite local                      SQLite local
                                              │
                                         Sync ▼
                                    [MongoDB Central]
                                   (events, projects,
                                    votes, judges)
                                              │
                                         Query ▼
                                    [Nodus.Web (kiosco)]
                                    Blazor WASM + Mongo
```
