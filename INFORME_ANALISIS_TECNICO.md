# 📊 INFORME TÉCNICO COMPLETO - NODUS FIREFLY SWARM EVALUATION SYSTEM

**Fecha:** 24 de Febrero 2026  
**Versión:** 1.0  
**Stack:** .NET 10, MAUI, Blazor WASM, BLE, MongoDB Atlas  
**Evaluación:** Análisis de Corrección Teórica Integral

---

## TABLA DE CONTENIDOS

1. [Resumen Ejecutivo](#resumen-ejecutivo)
2. [Arquitectura General](#arquitectura-general)
3. [Protocolo Firefly Swarm (Red BLE)](#protocolo-firefly-swarm)
4. [Análisis de las 3 Apps](#análisis-de-las-3-apps)
5. [Comunicación Inter-componentes](#comunicación-inter-componentes)
6. [Criptografía y Seguridad](#criptografía-y-seguridad)
7. [Persistencia y Sincronización](#persistencia-y-sincronización)
8. [⚠️ HALLAZGOS CRÍTICOS](#hallazgos-críticos)
9. [✅ FORTALEZAS](#fortalezas)
10. [❌ DEBILIDADES Y RIESGOS](#debilidades-y-riesgos)
11. [🔧 RECOMENDACIONES](#recomendaciones)

---

## RESUMEN EJECUTIVO

### Veredicto General: ⚠️ **FUNCIONARÍA CON LIMITACIONES Y RIESGOS**

El sistema **teóricamente es viable** pero tiene **implementación incompleta** y **varios riesgos críticos no mitigados**:

| Aspecto | Estado | Riesgo |
|---------|--------|--------|
| **Protocolo Firefly FSM** | ✅ Implementado | ⚠️ Medio (faltan pruebas) |
| **Comunicación BLE** | ✅ Parcial | ⚠️ Alto (timeouts, reconexión) |
| **Seguridad Criptográfica** | ✅ Diseño correcto | ⚠️ Medio (Ed25519 → ECDsa) |
| **Sincronización BLE→MongoDB** | 🟡 Incompleta | 🔴 **CRÍTICO** |
| **Manejo de Errores** | 🟡 Básico | 🔴 **CRÍTICO** |
| **Persistencia Offline-First** | ✅ SQLite | ✅ OK |
| **iOS/Android Constraints** | ✅ Considerado | ⚠️ Medio |

---

## ARQUITECTURA GENERAL

### 3 Aplicaciones Interconectadas

```
┌─────────────────────────────────────────────────────────────┐
│                    NODUS ECOSYSTEM                          │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────────────┐  ┌─────────────────┐  ┌────────────┐ │
│  │  Nodus.Client   │  │  Nodus.Server   │  │ Nodus.Web  │ │
│  │   (JUDGE)       │  │   (ADMIN)       │  │ (STUDENT)  │ │
│  │  MAUI Android   │  │  MAUI Windows   │  │  Blazor    │ │
│  │  Local SQLite   │  │  BLE GATT Srv   │  │  MongoDB   │ │
│  └────────┬────────┘  └────────┬────────┘  └────────┬───┘ │
│           │                    │                    │      │
│           │◄──────BLE Mesh──────►│                  │      │
│           │     (Firefly FSM)     │                  │      │
│           │                       │                  │      │
│           └───────────────────────┴──────────────────┘      │
│                                                             │
│                  ┌──────────────────────┐                  │
│                  │   MongoDB Atlas      │                  │
│                  │   (Cloud Sync)       │                  │
│                  └──────────────────────┘                  │
│                          ▲                                 │
│                          │                                 │
│                    HTTP/REST API                           │
│          (Nodus.Server ←→ Nodus.Web)                       │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Flujo de Datos Teórico

1. **Entrada (Judge App)**
   - Escanea QR del proyecto → Obtiene Project ID
   - Rellena rubric (ej. Design=8, Code=9)
   - Crea Vote con timestamp + firma Ed25519
   - Persiste en SQLite local

2. **Transmisión (BLE Firefly)**
   - SwarmService entra en máquina de estados SEEKER→CANDIDATE→LINK→COOLDOWN
   - Vote se serializa a JSON → Encriptado con AES-GCM
   - Se fragmenta en chunks de 180 bytes
   - Se envía por BLE con retry logic

3. **Recepción (Server App)**
   - Recibe chunks y reconstitución
   - Valida firma Ed25519
   - Desencripta payload
   - Persiste en SQLite local
   - Sincroniza a MongoDB Atlas

4. **Dashboard (Web App)**
   - Consume API REST del Servidor
   - MongoDB Atlas como datastore principal
   - Muestra resultados en tiempo real

---

## PROTOCOLO FIREFLY SWARM

### Máquina de Estados (FSM)

```
┌─────────────────────────────────────────────────────────────┐
│                    FIREFLY STATE MACHINE                    │
└─────────────────────────────────────────────────────────────┘

                         ┌──────────────┐
                         │   SEEKER     │  ← Default State
                         │ (Silent scan)│
                         └───────┬──────┘
                                 │
                    RSSI > -75dBm │ (Server detected)
                                 │
                         ┌───────▼──────┐
                         │  CANDIDATE   │
                         │ (Trickle wait)├─────────────────┐
                         └───────┬──────┘                  │
                                 │                         │
              Random(2s, 10s)    │                  LinkCount ≥ 2
              + No traffic       │                 OR Signal Lost
                                 │                         │
                         ┌───────▼──────┐          ┌──────▼──────┐
                         │    LINK      │          │   SEEKER    │
                         │  (Advertise) │          │  (Return)   │
                         └───────┬──────┘          └─────────────┘
                                 │
                    60s timeout   │
                    OR Battery<20%│
                                 │
                         ┌───────▼────────┐
                         │   COOLDOWN     │
                         │ (Rest 5 min)   │
                         └───────┬────────┘
                                 │
                               5min │
                                 │
                         ┌───────▼──────┐
                         │   SEEKER     │
                         └──────────────┘
```

### Algoritmo Trickle (Anti-Colisión)

```csharp
PROCEDIMIENTO CheckStateAsync() // Ejecutado cada 5s
├─ IF (Estado == COOLDOWN)
│  └─ IF (Now > CooldownExpiry)
│     └─ Estado ← SEEKER
│
├─ IF (Estado == LINK)
│  └─ IF (Duration > MAX_LINK_DURATION_SECONDS)
│     ├─ StopAdvertising()
│     ├─ Estado ← COOLDOWN
│     └─ CooldownExpiry ← Now + 5min
│
└─ IF (Estado == SEEKER)
   └─ IF (BleClient.IsConnected)  // RSSI > -75dBm
      ├─ Estado ← CANDIDATE
      ├─ Wait = Random(2000ms, 10000ms)
      ├─ Delay(Wait)
      │
      ├─ IF (NeighborLinkCount >= 2)  // Redundancy check
      │  └─ Estado ← SEEKER  // Abort, not needed
      │
      └─ ELSE
         ├─ Estado ← LINK
         └─ StartRelay()
```

### Evaluación de la FSM

✅ **Correcto teóricamente:**
- La transición de estados es lógica y previene bucles
- Constantes bien calibrados (60s LINK, 5min COOLDOWN)
- Random delay evita "thundering herd"
- Redundancy check (LinkCount) reduce interferencia

⚠️ **Problemas de implementación:**
- El check de RSSI es simplificado (`IsConnected` vs `LastRssi > -75dBm`)
- No hay manejo explícito si el servidor desaparece en estado LINK
- Mule Mode (10 min sin contacto) está considerado pero puede ser demasiado agresivo

---

## ANÁLISIS DE LAS 3 APPS

### 1️⃣ Nodus.Client (Judge App - MAUI Android/iOS)

**Responsabilidades:**
- Interfaz de votación offline
- Genera pares Ed25519 localmente
- Ejecuta FSM Firefly Swarm
- Fragmenta y envía votos por BLE

**Servicios Clave:**

| Servicio | Implementación | Estatus |
|----------|---|---|
| `BleClientService` | Cliente BLE con retry logic | ✅ Implementado |
| `SwarmService` | FSM Firefly 4-state | ✅ Implementado |
| `LocalDatabaseService` | SQLite local | ✅ Implementado |
| `RelayHostingService` | GATT Server (relay) | 🟡 Interface |
| `MediaSyncService` | Upload fotos a Supabase | 🟡 Stub |

**Flujo de Voto:**

```csharp
1. User UI
   └─ Rubric Form (Design: 8, Code: 9)
   
2. VoteAggregatorService
   ├─ Validar JSON payload
   ├─ Generar Vote { Id, ProjectId, PayloadJson }
   └─ Persiste en SQLite
   
3. BleClientService
   ├─ Serialize a NodusPacket (JSON)
   ├─ Encrypt con Event AES Key
   ├─ Split en chunks (MTU=180)
   └─ WriteCharacteristic con withResponse=false
   
4. SwarmService (Background)
   └─ Rotación automática de estado cada 5s
```

**Problemas Identificados:**

🔴 **CRÍTICO:**
- `RelayHostingService` es solo interfaz (no implementado)
  - Nodes **no pueden ser relays** actualmente
  - Todo el concepto de Firefly depende de esto
  
- No hay manejo de fragmentación de responses
  - Si Server envía datos >180 bytes, se pierden

⚠️ **MEDIO:**
- No hay reintentos en caso de pérdida de conexión BLE
- Timeout de 30s es muy largo (puede bloquear UI)
- Battery level check no está integrado

---

### 2️⃣ Nodus.Server (Admin App - MAUI Windows)

**Responsabilidades:**
- GATT Server central (anuncia el evento)
- Recibe votos de Judges
- Almacena en SQLite + MongoDB
- Dashboard de resultados en tiempo real

**Servicios Clave:**

| Servicio | Implementación | Estatus |
|----------|---|---|
| `BleServerService` | GATT Server hosting | 🟡 Hosting setup |
| `MongoDbService` | Sincronización nube | ✅ Implementado |
| `LocalDatabaseService` | SQLite local | ✅ Implementado |
| `VoteIngestionService` | Procesa packets JSON | ✅ Implementado |
| `CloudSyncService` | Bi-sync local↔cloud | 🟡 Partial |

**Flujo de Recepción:**

```csharp
1. BLE GATT Server
   └─ Escucha en NodosConstants.SERVICE_UUID
   
2. OnCharacteristicWrite()
   ├─ Recibe chunk (0-180 bytes)
   ├─ ChunkAssembler reconstitución
   └─ Completa → Evento PayloadCompleted
   
3. VoteIngestionService.ProcessPayloadAsync()
   ├─ Deserialize JSON
   ├─ Validate timestamp (±5 min skew)
   ├─ Verify Ed25519 signature ✅
   ├─ Decrypt con Event AES Key
   └─ VoteAggregatorService.ProcessVoteAsync()
       ├─ Persist to SQLite
       ├─ Trigger MongoDB sync
       └─ Emit WeakReferenceMessenger (UI update)
```

**Problemas Identificados:**

🔴 **CRÍTICO:**
- `CloudSyncService` está básicamente vacío
  - ¿Cómo se sincroniza MongoDB? ¿Con qué frecuencia?
  - ¿Manejo de conflictos si mismo voto llega 2 veces?
  
- No hay deduplicación de votos
  - Si Server recibe mismo vote 2 veces (por retry), se dublica
  - PacketTracker solo previene loops en relays, no en servidor

⚠️ **MEDIO:**
- BleServerService solo tienen "Setup" en MauiProgram
  - ¿Cuándo se llama `StartHostingAsync()`?
  - ¿Qué pasa si BLE falla? ¿Reintentos?

- No hay timeout para reconstitución de chunks
  - Si cliente envía Chunk 0 pero nunca Chunk 1, Queue crece infinito?

---

### 3️⃣ Nodus.Web (Student Portal - Blazor WASM)

**Responsabilidades:**
- Portal de registro de proyectos
- Generación de QR códigos
- Vista live de resultados
- Fullscreen display mode para proyectores

**Servicios Clave:**

| Servicio | Implementación | Estatus |
|----------|---|---|
| `MongoDataApiService` | API REST a MongoDB Atlas | ✅ Implementado |
| `QrGeneratorService` | Generación QR | ✅ Implementado |
| `EventService` | Gestión eventos | ✅ Implementado |
| `ProjectService` | CRUD Proyectos | ✅ Implementado |

**Flujo de Registro:**

```csharp
1. Student UI (RegisterProject.razor)
   └─ Form: ProjectName, Category, Authors, GitHub
   
2. ProjectService
   ├─ POST /api/projects
   ├─ Server-side validation
   └─ Persist to MongoDB
   
3. QrGeneratorService
   ├─ Genera QR con encoding: "PROJ-{ProjectId}"
   └─ Mostrar en pantalla
   
4. Display Mode (ProjectDisplay.razor)
   └─ Loop fullscreen con resultados live
       ├─ Cada 2s: Fetch scores desde MongoDB Data API
       ├─ Update ranking
       └─ Animar cambios
```

**Problemas Identificados:**

⚠️ **MEDIO:**
- Dependencia 100% en MongoDB Data API
  - Si API falla, todo el sitio se bloquea
  - No hay cache local ni fallback
  
- QR contiene solo ProjectId, no cifrado
  - Estudiantes pueden descubrir Project IDs sin QR
  - No hay validación de "belongsToEvent"

- Display mode: polling cada 2s es muy agresivo
  - Puede saturar MongoDB Data API
  - Mejor: WebSocket o Server-Sent Events

---

## COMUNICACIÓN INTER-COMPONENTES

### Canal 1: BLE Mesh (Judge ↔ Server)

**Protocolo:**

```
PACKET STRUCTURE:
┌──────────────────────────────────────────────────┐
│ [Tipo] [MessageId] [ChunkIndex] [Payload]        │
│  0x01     0-255      0-255       0-180 bytes    │
└──────────────────────────────────────────────────┘

Tipos:
  0x01 = JSON (Votos, Commands)
  0x02 = MEDIA (Fotos)
  0xA1 = ACK
```

**Fragmentación (BleChunker):**

```
Payload > 180 bytes
     ├─ Chunk 0 [Header]: MsgId, TotalChunks, PayloadLength
     ├─ Chunk 1-N: Datos (180 bytes c/u)
     └─ Reassemble: ChunkAssembler concatena
```

**Flujo de Voto Completo:**

```
JUDGE APP           BLE MESH         SERVER APP
───────────────────────────────────────────────
Vote created
  + Timestamp
  + Ed25519 sign
  + Encrypt AES-GCM
           │
           Send chunk 0
           ──────────→
                      │ Recibe Header
                      │ Espera chunks 1-N
           │
           Send chunk 1
           ──────────→
                      Chunk 1 received
           │
           Send chunk 2
           ──────────→
                      Chunk 2 received
                      │ ALL CHUNKS? YES
                      │ Reconstitución OK
                      │ Verify signature
                      │ Decrypt payload
                      │
                      Persist SQLite
                      │ Trigger MongoDB
                      │
           ←──────────
           ACK (Chunk 0xA1)
           │
   Mark Vote.Status = Synced
```

**Análisis de Confiabilidad:**

✅ **Puntos Fuertes:**
- Max TTL = 2 garantiza no hay loops
- PacketTracker (Bloom Filter) previene duplicados
- Ed25519 signing: Si alguien modifica payload en tránsito, falla verificación
- AES-GCM: Authenticated encryption (no solo confidencial, sino integro)

⚠️ **Debilidades:**
- **WriteWithoutResponse** = No hay ACK a nivel BLE
  - Chunks pueden perderse silenciosamente
  - No hay reintento automático en capa BLE
  
- Timeout no está claro
  - Si Server no recibe Chunk 1 en 30s, ¿qué sucede?
  - ChunkAssembler se bloquea esperando forever?
  
- If Judge disconnects mid-transfer
  - Chunks se pierden
  - No hay recovery automático
  - Vote queda con Status = Pending indefinidamente

---

### Canal 2: HTTP/REST (Server ↔ Web)

**Endpoints Implícitos:**

```
GET  /api/events                    → MongoDataApiService.GetEventsAsync()
GET  /api/events/{eventId}/projects → MongoDataApiService.GetProjectsAsync()
POST /api/projects                  → ProjectService.SaveProjectAsync()
```

**Comunicación:**

```
NODUS.SERVER          HTTP/REST       NODUS.WEB
─────────────────────────────────────────────────
SQLite <-> MongoDB    ←→ API Calls    ←→ Browser
Local Cache            Sync            Live Feed
(BLE receiver)                         (QR gen)
```

**Problemas:**

⚠️ **CRÍTICO:**
- **NO HAY AUTENTICACIÓN**
  - Cualquiera puede acceder a `/api/projects`
  - No hay API key ni JWT
  - Cualquier persona en la red puede leer/escribir proyectos

- MongoDB Data API Key está hardcoded
  - En `AppSecrets` (¿dónde se almacena?)
  - Si se filtra, alguien puede acceder a toda la BD

---

### Canal 3: Sincronización BLE → MongoDB

**Flujo:**

```
BLE Packet
    │
    ├─ Server App recibe
    │   └─ VoteIngestionService.ProcessPayloadAsync()
    │       └─ Vote persiste en SQLite
    │
    ├─ CloudSyncService (TODO: No está implementado)
    │   └─ ???
    │
    └─ MongoDB Atlas
        └─ Esperado: Queries en Web consulten resultados
```

**Problema:**

🔴 **CRÍTICO — ES AQUÍ DONDE EL SISTEMA FALLA:**

```csharp
// CloudSyncService.cs
public class CloudSyncService
{
    // ESTÁ BÁSICAMENTE VACÍO
    // ¿Cómo se sincroniza a MongoDB?
    // ¿Manejo de conflictos?
    // ¿Cuándo ejecutar sync?
}
```

**Lo que debería ocurrir:**

1. Vote llega a BLE
2. VoteIngestionService lo persiste en SQLite
3. **CloudSyncService detecta cambio**
4. **Sincroniza a MongoDB Atlas**
5. **Maneja conflictos (duplicate check)**
6. **Web está siempre actualizado**

**Lo que probablemente SUCEDE AHORA:**

1. Vote llega a BLE ✅
2. Se persiste en SQLite ✅
3. ??? (No automatizado)
4. MongoDB NO está actualizado
5. Web ve datos viejos
6. **El sistema no funciona como prometido**

---

## CRIPTOGRAFÍA Y SEGURIDAD

### Algoritmos Utilizados

| Operación | Algoritmo | Implementación | Eval |
|-----------|-----------|---|---|
| Encriptación | AES-256-GCM | `CryptoHelper.Encrypt()` | ✅ |
| Firma | Ed25519 | ❌ Fallback: ECDsa P-256 | ⚠️ |
| Key Derivation | PBKDF2-SHA256 | `DeriveKeyFromPassword()` | ✅ |
| Nonce Gen | CSPRNG | `RandomNumberGenerator.Fill()` | ✅ |
| Anti-replay | Bloom Filter | `PacketTracker` | ✅ |

### Análisis Detallado

#### 1. AES-GCM (Encriptación)

**Implementación:**

```csharp
public static byte[] Encrypt(byte[] plaintext, byte[] key)
{
    // 1. Genera nonce aleatorio (12 bytes - estándar GCM)
    var nonce = new byte[12];
    RandomNumberGenerator.Fill(nonce);
    
    // 2. Encripta y genera authentication tag (16 bytes)
    using var aes = new AesGcm(key, tag.Length);
    aes.Encrypt(nonce, plaintext, ciphertext, tag);
    
    // 3. Concatena [Nonce(12) + Ciphertext + Tag(16)]
    return Concat(nonce, ciphertext, tag);
}
```

✅ **Correcto:**
- Nonce de 12 bytes es estándar GCM
- Uses `RandomNumberGenerator` (CSPRNG)
- Concatenación correcta para deserialización
- Tag de 16 bytes es máximo disponible

**Potencial issue:**
- ¿Qué pasa si se usa el MISMO nonce dos veces?
  - AES-GCM con nonce repetido = Completa pérdida de seguridad
  - El código genera nonce aleatorio cada vez ✅
  - PERO: ¿Y si la app se reinicia y genera misma seed?
    - .NET RandomNumberGenerator es bueno pero...
    - En phones after reboot: posible regresar a misma seed?
    - Mitigación: Usar `TimeBasedNonceGenerator` o IV derivado

**Veredicto: ✅ Implementación segura**

---

#### 2. Ed25519 Signing (⚠️ PROBLEMA)

**Lo que debería ser:**

```csharp
public static (string PublicKeyBase64, string PrivateKeyBase64) GenerateSigningKeys()
{
    using var ed25519 = new Ed25519();  // ← .NET 8+ support?
    return ed25519.GenerateKeyPair();
}
```

**Lo que está implementado:**

```csharp
public static (string PublicKeyBase64, string PrivateKeyBase64) GenerateSigningKeys()
{
    using var dsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    // ↑ Usa ECDsa P-256 en lugar de Ed25519
}
```

🔴 **Problema:**
- El código **comentario dice Ed25519**
- La **implementación usa ECDsa P-256**
- Documentación dice "Derived from .NET 10"

**Análisis:**
- ECDsa P-256 es **más lento** que Ed25519 (verificación)
- ECDsa P-256 es **menos robusto** contra timing attacks (lado teórico)
- Pero: ECDsa P-256 es más testado y disponible

**Recomendación:**
```csharp
// Usar NSec.Cryptography NuGet para Ed25519
// O usar System.Security.Cryptography.Ed25519 si .NET 10 lo requiere
// Verificar: Cuál está disponible
```

**Impacto en funcionalidad:** ⚠️ Medio (funciona pero no óptimo)

---

#### 3. Anti-Replay & Anti-Cheat

**PacketTracker (Prevención de Loops):**

```csharp
public bool TryProcess(string packetId)
{
    // Si packet_id ya visto en últimos 10 min → DROP
    // Si no visto → Agregar a cache + PROCESS
    
    if (_seenPackets.TryGetValue(packetId, out var expiry))
    {
        if (expiry > now) 
            return false;  // LOOP DETECTED
    }
    
    _seenPackets[packetId] = now.Add(TimeSpan.FromMinutes(10));
    return true;
}
```

✅ **Correcto:**
- TTL de 10 minutos es razonable
- Hash map con double-check locking para limpieza
- Previene "ping-pong" entre dos relays

⚠️ **Limitaciones:**
- Solo previene loops geografía (A→B→A)
- No previene DoS: Someone sends 1000 unique packet_id
  - Cache crece sin límite
  - Memory leak posible
  
**Mitigation:**
```csharp
private const int MAX_CACHE_SIZE = 10_000;
if (_seenPackets.Count > MAX_CACHE_SIZE)
{
    // Limpiar los más antiguos (LRU)
    // ...
}
```

**Protección contra Cheat (Voting):**

```csharp
// En VoteAggregatorService:
var existing = list.FirstOrDefault(v => v.JudgeId == vote.JudgeId);
if (existing != null)
    list.Remove(existing);  // Reemplaza voto anterior
```

✅ **Correcto:**
- No permite 2 votos del mismo Judge para 1 proyecto
- Último voto gana (o debería ser promedio?)

⚠️ **Recomendación:**
- Implementar deduplicación en base de datos también
- Usar`INSERT OR REPLACE` en SQLite / Upsert en MongoDB
- No confiar solo en aplicación

---

## PERSISTENCIA Y SINCRONIZACIÓN

### Base de Datos Local (SQLite - Client)

**Tablas:**

```csharp
Events
├─ Id (PK)
├─ Name
├─ RubricJson
├─ GlobalSalt
├─ SharedAesKeyEncrypted
└─ IsActive

Projects
├─ Id (PK)
├─ EventId (FK)
├─ Name
├─ Category
├─ Description
└─ Authors

Votes
├─ Id (PK)
├─ EventId (FK)
├─ ProjectId (FK)
├─ JudgeId (FK)
├─ PayloadJson
├─ Status (Pending/Synced/SyncError)
├─ Timestamp
└─ LocalPhotoPath
```

**Índices Aplicados:**

✅ OK:
```csharp
events.EnsureIndex(x => x.IsActive)
votes.EnsureIndex(x => x.ProjectId)
votes.EnsureIndex(x => x.Status)
```

⚠️ **FALTA:**
```csharp
// Debería haber:
votes.EnsureIndex(x => x.JudgeId)  // Para auditoría
votes.EnsureIndex(x => x.EventId)  // Para sync
votes.EnsureIndex(new[] { x => x.Id, x => x.ProjectId })  // Compuesto
```

---

### Base de Datos en Nube (MongoDB Atlas - Server)

**Documentos:**

```
db.events
├─ _id
├─ name
├─ rubric (BSON document)
├─ globalSalt
├─ sharedAesKeyEncrypted
└─ isActive

db.projects
├─ _id
├─ eventId
├─ name
├─ category
├─ description
└─ authors

db.votes
├─ _id
├─ eventId
├─ projectId
├─ judgeId
├─ payload (BSON document) ← Permite queries: {payload.Design: {$gt: 7}}
├─ status (Pending/Synced)
├─ timestamp
└─ isMediaSynced
```

**Índices en MongoDB:**

✅ Creados:
```javascript
db.projects.createIndex({ eventId: 1 })
db.votes.createIndex({ eventId: 1 })
db.votes.createIndex({ projectId: 1 })
db.votes.createIndex({ judgeId: 1 })
db.votes.createIndex({ status: 1 })
db.judges.createIndex({ eventId: 1 })
```

✅ Ventaja: Permite queries como
```javascript
db.votes.find({ eventId, payload: { Design: { $gt: 7 } } })
```

---

### Sincronización BLE → MongoDB

**Arquitectura Implícita:**

```
BLE Reception          Local SQLite         MongoDB Cloud
──────────────────────────────────────────────────────────
VoteIngestionService
  └─ Recibe chunks
  └─ Reconstitución
  └─ Validación
       │
       ├─ Save to SQLite ✅
       │   Vote { Status: Pending }
       │
       └─ TODO: CloudSyncService ❌
           ├─ Detect change
           ├─ Attempt MongoDB insert
           ├─ Handle conflicts
           └─ Update Status: Synced
```

**PROBLEMA CRÍTICO:**

❌ **CloudSyncService está vacío**

No hay mecanismo automático para:
1. Detectar cambios en SQLite
2. Sincronizar a MongoDB
3. Manejar conflictos (duplicate votes)
4. Reintentos si falla sync

**Impacto:**
- **Server local tiene datos, Cloud NO**
- **Web consulta Cloud y ve datos viejos**
- **El sistema aparenta no estar funcionando**

**Solución Teórica (NO implementada):**

```csharp
public class CloudSyncService : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        _ = SyncLoopAsync(ct);  // Background task
    }
    
    private async Task SyncLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 1. Fetch pending votes from SQLite
                var pending = await _db.GetVotesAsync(status: SyncStatus.Pending);
                
                // 2. For each vote
                foreach (var vote in pending)
                {
                    try
                    {
                        // 3. Try MongoDB upsert
                        var result = await _mongo.UpsertVoteAsync(vote);
                        
                        // 4. If success, mark Synced
                        vote.Status = SyncStatus.Synced;
                        await _db.SaveVoteAsync(vote);
                    }
                    catch (Exception ex)
                    {
                        vote.Status = SyncStatus.SyncError;
                        vote.SyncError = ex.Message;
                        await _db.SaveVoteAsync(vote);
                    }
                }
                
                // 5. Wait before next sync
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync loop failed");
            }
        }
    }
}
```

**Without this → System breaks.**

---

### Sincronización Multimedia (Fotos)

**Implementación:**

```csharp
ProcessMediaPacketAsync(byte[] payload)
├─ Extract VoteId + ImageBytes
├─ Save to disk: /AppData/Media/{EventId}/{VoteId}.jpg ✅
├─ Update Vote.LocalPhotoPath = path
└─ Mark Vote.IsMediaSynced = true
```

✅ **OK:**
- Las fotos se guardaneyes localmente
- Metadata actualiza en SQLite
- No bloquea recepción de votos

⚠️ **Problemas:**
- Fotos se guardan en AppData (¿tamaño límite?)
- No hay compresión de imágenes
- MediaSyncService (upload a Supabase) está en Shared pero...
  - ¿Quién lo llama?
  - ¿Cuándo?
  - No hay loop de upload en background

---

## ⚠️ HALLAZGOS CRÍTICOS

### 🔴 1. CLOUDSYNCSERVICE ESTÁ VACÍO

**Severidad: CRÍTICA**  
**Impacto: Sistema no funciona end-to-end**

State:
```csharp
public class CloudSyncService
{
    // No hace nada
}
```

**Consecuencia:**
- Votes llegan a SQLite Server
- MongoDB Cloud **NUNCA se actualiza**
- Web ve datos viejos o no ve nada
- Dashboard de Server funciona, Web NO

**Fix Required:** Implementarbg loop de sincronización

---

### 🔴 2. RELAYHOSTING SERVICE NO IMPLEMENTADO

**Severidad: CRÍTICA**  
**Impacto: Nodes no pueden ser relays**

```csharp
public interface IRelayHostingService
{
    Task StartAdvertisingAsync();
    void StopAdvertising();
}

// En SwarmService:
private Task StartRelayAsync()
{
    return _relayService.StartAdvertisingAsync();  // ← ¿Qué pasa aquí?
}
```

**Problema:**
- El concepto completo de **Firefly es que Judges se conviertan en relays**
- Si esto no funciona, solo el Server puede alcanzar Judges cercanos
- **No hay mesh, solo punto-a-punto**

**Fix Required:** Implementar GATT Server en cada Judge

---

### 🔴 3. NO HAY AUTENTICACIÓN EN API REST

**Severidad: CRÍTICA**  
**Impacto: Security breach**

```csharp
GET /api/events                // Público
GET /api/projects              // Público
POST /api/projects             // Público (cualquiera registra proyectos)
```

**Problema:**
- Estudiantes pueden ver/modificar otros proyectos
- Atacantes pueden borrar datos o inyectar proyectos basura

**Fix Required:** Implementar JWT + API keys

---

### 🔴 4. DEDUPLICACIÓN INCOMPLETA

**Severidad: ALTA**  
**Impacto: Votos duplicados en MongoDB**

**Escenario:**
1. Judge envía Vote#1 por BLE
2. Server recibe y persiste en SQLite
3. Judge no recibe ACK (conectado BLE se perdió)
4. Judge reintenta...
5. Server recibe MISMO Vote#1 nuevamente
6. ¿Qué sucede?

**Actual:**
```csharp
// En VoteAggregatorService
_votesByProject.AddOrUpdate(vote.ProjectId, 
    new List<Vote> { vote }, 
    (key, list) => {
        // Reemplaza por judge (dedup OK)
        var existing = list.FirstOrDefault(v => v.JudgeId == vote.JudgeId);
        if (existing != null) list.Remove(existing);
        list.Add(vote);
    });
```

✅ **En memoria funciona:**
- Si mismo Vote.JudgeId llega 2 veces, reemplaza

❌ **Pero en MongoDB:**
```javascript
db.votes.insertOne({
    _id: vote.Id,  // ← Si es mismo vote, ¿upsert?
    // ...
})
```

**Sin índice único de](project_id, judge_id), puede haber duplicados**

**Fix Required:**
```javascript
db.votes.createIndex({ projectId: 1, judgeId: 1 }, { unique: true })
```

---

### ⚠️ 5. TIMEOUTS Y RECONEXIÓN NO CLAROS

**Severidad: MEDIA**  
**Impacto: App puede colgar**

**Escenarios:**
1. Judge conectado a Server
2. Server apaga mientras recibe voto
3. BLE desconecta
4. Judge intenta escribir characteristic
5. **¿Qué pasa? ¿Timeout? ¿Reintentos?**

**Código:**

```csharp
public async Task<Result> TransmitPacketAsync(NodusPacket packet, CancellationToken ct = default)
{
    // ...
    foreach (var chunk in chunks)
    {
        ct.ThrowIfCancellationRequested();
        
        if (target.ConnectionState != "Connected")
        {
            return Result.Failure("Connection lost");
        }
        
        await target.WriteCharacteristicAsync(
            NodusConstants.SERVICE_UUID, 
            NodusConstants.CHARACTERISTIC_UUID, 
            chunk, 
            withResponse: false  // ← NO ESPERA ACK
        );
        
        await Task.Delay(20, ct);  // 20ms Between chunks
    }
}
```

⚠️ **Problemas:**
- `withResponse: false` = Fire and forget
- Si chunk 0 llega, chunk 1 se pierde, no hay reintento
- No hay timeout en nivel BLE (firmware maneja después de 30s)
- Si ConnectionState se vuelve falso a mitad de chunks, parcial se mandó

**Mejor:**
```csharp
for (int attempt = 0; attempt < 3; attempt++)
{
    try
    {
        await target.WriteCharacteristicAsync(..., withResponse: true);
        break;  // Success
    }
    catch (TimeoutException)
    {
        if (attempt == 2) throw;
        await Task.Delay(500 * (attempt + 1), ct);  // Backoff
    }
}
```

---

### ⚠️ 6. iOS BACKGROUND EXECUTION

**Severidad: MEDIA**  
**Impacto: iOS app no funciona cuando está minimizado**

**Docs dicen:**

```csharp
// En 12.Network.Dynamic_Swarm_Logic.md:
// "If the user locks the phone:
//    - Android: Can continue (with Sticky Notification).
//    - iOS: IMMEDIATELY sends 'Goodbye' packet and stops Advertising."
```

**Problema:**
- En eventos real, judges probablemente minimizarán la app
- En iOS, se detiene FSM
- Relays se apagan
- Mesh colapsa

**Requiere:**
- VoIP push notifications (muy complejo)
- O al menos mantener scaneo (pero iOS mata background scan después 30s)

---

## ✅ FORTALEZAS

### 1. Algoritmo Firefly Swarm es Inteligente

✅ **Conceptualmente brillante:**
- Rotación de roles evita concentrar en un solo device
- Randomización previene colisiones
- Load balancing automático
- Battery-first design

**Comparación:**
- ❌ Mesh fijo: Relay muere → Todo falla
- ✅ Firefly: Relay agotado → Otro se promueve automáticamente

---

### 2. Criptografía Bien Diseñada

✅ **AES-GCM:**
- Authenticated encryption (no solo confidencial)
- Nonce generation es segura
- Tag validation detecta tampering

✅ **Ed25519 (aunque implementado como ECDsa):**
- Firma digital de votos
- Anti-forge, anti-tampering
- Timestamp binding

✅ **PBKDF2:**
- Key derivation con múltiples iteraciones
- Previene fuerza bruta

---

### 3. Offline-First Architecture

✅ **Resiliente a desconexiones:**
- SQLite local = funciona sin Internet
- Votos persisten localmente
- Sync asincrónico cuando hay conexión

✅ **Data model es limpio:**
- POCO models sin dependencias BD
- Fácil testeo
- Portable entre SQLite/MongoDB

---

### 4. QR-Based Identification

✅ **Ventajas:**
- Judges no tipean Project ID
- Rápido
- Menos errores

✅ **Security:**
- QR contiene suficiente info
- Validación server-side

---

### 5. Servicios Bien Abstractos (Interfaces)

✅ **Inyección de dependencias:**
```csharp
IBleClientService
IDatabaseService
ISecureStorageService
IFileService
```

- Facilita testing
- Mocking fácil
- Cambiar implementaciones sin modificar lógica

---

## ❌ DEBILIDADES Y RIESGOS

### 🔴 CRÍTICOS

| # | Problema | Severidad | Impacto |
|---|----------|-----------|--------|
| 1 | CloudSyncService vacío | 🔴 CRÍTICA | Sistema no sincroniza |
| 2 | RelayHostingService no impl. | 🔴 CRÍTICA | No hay relay, solo punto-a-punto |
| 3 | Sin autenticación API | 🔴 CRÍTICA | Security breach |
| 4 | Deduplicación incompleta | 🔴 CRÍTICA | Datos duplicados en Cloud |
| 5 | Timeouts no explícitos | 🔴 CRÍTICA | Posibles hangs |

### ⚠️ MEDIOS

| # | Problema | Severidad | Impacto |
|---|----------|-----------|--------|
| 6 | iOS background kill | ⚠️ MEDIA | App no funciona minimizada |
| 7 | MongoDB Data API key hardcoded | ⚠️ MEDIA | Exposure si se filtran secrets |
| 8 | Nonce reuse potential | ⚠️ MEDIA | Teórico, bajo riesgo actual |
| 9 | No hay observability/logging | ⚠️ MEDIA | Difícil debuggear en producción |

### 🟡 BAJOS

| # | Problema | Severidad | Impacto |
|---|----------|-----------|--------|
| 10 | QR no cifrado | 🟡 BAJA | Project IDs públicos |
| 11 | Polling en Web (2s) | 🟡 BAJA | Podría saturar API |
| 12 | Ed25519 → ECDsa fallback | 🟡 BAJA | Performance OK pero no óptimo |

---

## 🔧 RECOMENDACIONES

### Fase 1: CRÍTICA (Hack Now)

#### R1. Implementar CloudSyncService

**Prioridad: 🔴 BLOQUEADOR**

```csharp
public class CloudSyncService : IHostedService
{
    private readonly IDatabaseService _localDb;
    private readonly MongoDbService _cloud;
    private CancellationTokenSource? _cts;
    
    public async Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = SyncLoopAsync(_cts.Token);
    }
    
    private async Task SyncLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Fetch pending from SQLite
                var pending = await _localDb.GetVotesAsync(
                    filter: v => v.Status == SyncStatus.Pending,
                    limit: 100  // Batch sync
                );
                
                foreach (var vote in pending)
                {
                    try
                    {
                        // Upsert to MongoDB
                        await _cloud.SaveVoteAsync(vote);
                        
                        // Mark as synced
                        vote.Status = SyncStatus.Synced;
                        vote.SyncedAtUtc = DateTime.UtcNow;
                        await _localDb.SaveVoteAsync(vote);
                        
                        _logger.LogInformation("Synced vote {Id}", vote.Id);
                    }
                    catch (Exception ex)
                    {
                        vote.Status = SyncStatus.SyncError;
                        vote.SyncError = ex.Message;
                        await _localDb.SaveVoteAsync(vote);
                        _logger.LogWarning(ex, "Failed to sync vote {Id}", vote.Id);
                    }
                }
                
                // Sync every 30s
                await Task.Delay(30_000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync loop crashed");
                await Task.Delay(5_000, ct);
            }
        }
    }
    
    public async Task StopAsync(CancellationToken ct)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        await Task.CompletedTask;
    }
}

// En MauiProgram.cs
builder.Services.AddHostedService<CloudSyncService>();
```

**Estimado:** 4-6 horas

---

#### R2. Implementar RelayHostingService

**Prioridad: 🔴 BLOQUEADOR**

```csharp
public class RelayHostingService : IRelayHostingService
{
    private readonly IBleHostingManager _hostingManager;
    private IDisposable? _gattServer;
    private readonly ILogger<RelayHostingService> _logger;
    
    public async Task StartAdvertisingAsync()
    {
        try
        {
            // 1. Create GATT Server
            var service = GattServiceBuilder
                .CreatePrimaryService(Guid.Parse(NodusConstants.SERVICE_UUID))
                .AddCharacteristic(
                    Guid.Parse(NodusConstants.CHARACTERISTIC_UUID),
                    characteristicProperties: GattCharacteristicProperties.WriteWithoutResponse | GattCharacteristicProperties.Indicate
                );
            
            // 2. Register notification handler
            service.CharacteristicValueChanged += OnCharacteristicWrite;
            
            // 3. Start advertising
            _gattServer = await _hostingManager.AddServiceAsync(service);
            
            _logger.LogInformation("GATT Server started advertising");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start advertising");
            throw;
        }
    }
    
    private void OnCharacteristicWrite(GattCharacteristic characteristic, GattCharacteristicValueChangedEventArgs args)
    {
        // Relay received data
        // Note:ould forward to Server or other Judges
    }
    
    public void StopAdvertising()
    {
        _gattServer?.Dispose();
        _logger.LogInformation("GATT Server stopped");
    }
}
```

**Estimado:** 8-12 horas (requiere testing extensivo)

---

#### R3. Agregar Autenticación API

**Prioridad: 🔴 BLOQUEADOR**

```csharp
// En Nodus.Server
[ApiController]
[Route("api")]
public class ProjectsController : ControllerBase
{
    [Authorize]  // Requiere JWT
    [HttpGet("projects")]
    public async Task<ActionResult<List<ProjectDto>>> GetProjects()
    {
        var eventId = User.FindFirst("eventId")?.Value;
        if (string.IsNullOrEmpty(eventId))
            return Unauthorized();
        
        return await _projectService.GetProjectsAsync(eventId);
    }
    
    [Authorize]
    [HttpPost("projects")]
    public async Task<ActionResult> SaveProject(ProjectDto dto)
    {
        var judgeId = User.FindFirst("sub")?.Value;
        // Validate ownership, etc.
        return Ok();
    }
}

// TokenService
public class TokenService
{
    public string GenerateToken(string eventId, string judgeId)
    {
        var claims = new[]
        {
            new Claim("eventId", eventId),
            new Claim("sub", judgeId),
        };
        
        var token = new JwtSecurityToken(
            issuer: "Nodus",
            audience: "NodusClients",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecret)),
                SecurityAlgorithms.HmacSha256
            )
        );
        
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

**Estimado:** 6-8 horas

---

### Fase 2: IMPORTANTE (Week 1)

#### R4. Agregar Deduplicación en MongoDB

```csharp
// En MongoDbService Constructor
await _votes.Indexes.CreateOneAsync(
    new CreateIndexModel<VoteDocument>(
        Builders<VoteDocument>.IndexKeys
            .Ascending(v => v.ProjectId)
            .Ascending(v => v.JudgeId),
        new CreateIndexOptions { Unique = true, Sparse = true }
    )
);
```

**Estimado:** 2 horas

---

#### R5. Implementar Retry Logic con Backoff

```csharp
public static class RetryPolicy
{
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        int maxAttempts = 3)
    {
        TimeSpan delay = TimeSpan.FromMilliseconds(500);
        
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (i < maxAttempts - 1)
            {
                await Task.Delay(delay);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
            }
        }
        
        throw new TimeoutException("Max retries exceeded");
    }
}

// Uso:
await RetryPolicy.ExecuteWithRetryAsync(async () =>
{
    return await _bleClient.TransmitPacketAsync(packet, ct);
});
```

**Estimado:** 3-4 horas

---

### Fase 3: IMPORTANTE (Week 2-3)

#### R6. Logging y Observability

```csharp
// Structured logging
_logger.LogInformation(
    "Vote synced | VoteId={VoteId} | ProjectId={ProjectId} | Duration={Duration}ms",
    vote.Id, vote.ProjectId, stopwatch.ElapsedMilliseconds
);

// Application Insights / ELK
services.AddApplicationInsightsTelemetry();
```

**Estimado:** 4-6 horas

---

#### R7. Unit Testing

```csharp
[TestClass]
public class FireflySendStateTests
{
    [TestMethod]
    public async Task SeekerState_WithStrongSignal_PromotesTogather()
    {
        // Arrange
        var swarm = new SwarmService(...);
        swarm.CurrentState = SwarmState.Seeker;
        _bleClient.IsConnected = true;  // Strong signal
        
        // Act
        await swarm.CheckStateAsync();
        
        // Assert
        Assert.AreEqual(SwarmState.CANDIDATE, swarm.CurrentState);
    }
}
```

**Estimado:** 10-15 horas (cobertura 80%+)

---

### Fase 4: OPTIMIZACIÓN (Month 2)

#### R8. WebSocket para Live Updates (Web)

Replace polling HTTP con WebSocket:

```csharp
// SignalR Hub en Server
public class ResultsHub : Hub
{
    public async Task NotifyVoteReceived(Vote vote)
    {
        await Clients.Group(vote.EventId)
            .SendAsync("VoteReceived", vote);
    }
}

// Blazor Client
protected override async Task OnInitializedAsync()
{
    connection = new HubConnectionBuilder()
        .WithUrl(NavigationManager.ToAbsoluteUri("resultshub"))
        .Build();
        
    connection.On<Vote>("VoteReceived", HandleVoteReceived);
    await connection.StartAsync();
}
```

**Estimado:** 8-12 horas

---

#### R9. Ed25519 Support (Upgrade to real NSec or wait for .NET 11)

```csharp
// Usar NSec.Cryptography
// https://nsec.rocks/

using NSec.Cryptography;

var algorithm = SignatureAlgorithm.Ed25519;
var key = algorithm.GenerateKey();
var signature = algorithm.Sign(key, data);
var verified = algorithm.Verify(key.PublicKey, data, signature);
```

**Estimado:** 3-4 horas

---

## CONCLUSIÓN

### Veredicto Final

```
┌─────────────────────────────────────────────────────┐
│  EVALUACIÓN TEÓRICA: NODUS FIREFLY SWARM SYSTEM    │
├─────────────────────────────────────────────────────┤
│                                                     │
│  FUNCIONA TEÓRICAMENTE: ⚠️ CON LIMITACIONES        │
│                                                     │
│  ✅ QUÉ SÍ FUNCIONA:                                │
│     • Protocolo FSM Firefly (algoritmo correcto)   │
│     • Criptografía de juego (AES-GCM ok)           │
│     • Persistencia local (SQLite ok)               │
│     • QR scanning y votación (ok)                  │
│     • Fragmentación BLE y reunificación (ok)       │
│                                                     │
│  ❌ QUÉ NO FUNCIONA:                                │
│     • Sincronización BLE → MongoDB (VACÍO)         │
│     • Relay hosting (NO IMPLEMENTADO)              │
│     • Autenticación API (NO EXISTE)                │
│     • Deduplicación en Cloud (Incompleta)          │
│     • iOS Background (Limitado por SO)             │
│                                                     │
│  ⚠️  RESULTADO NETO:                                │
│     • En laboratorio: ~40% funcional               │
│     • Con fixes Fase 1: ~80% funcional             │
│     • Production ready: Fase 2-3 (2-4 semanas)     │
│                                                     │
└─────────────────────────────────────────────────────┘
```

### Roadmap de Correcciones

**Semana 1:** Implementar CloudSync + RelayHosting + Auth (CRITICAL)  
**Semana 2:** Testing exhaustivo, manejo de errores  
**Semana 3:** Optimizaciones, logging, coverage  
**Mes 2:** WebSocket, Ed25519, tuning final  

**Estimado Total:** 40-60 horas de desarrollo + 20-30 horas QA

---

### Recomendación Profesional

✅ **ADELANTE CON EL PROYECTO** pero con:

1. **Prioritario:** Fix los 5 problemas críticos (Fase 1)
2. **Antes de demo:** Tests exhaustivos de comunicación BLE
3. **Antes de producción:** Auditoría de seguridad externa
4. **Consideración:** Eventualmente migrar a WiFi Direct + BLE (más robusto)

**El concepto de "Firefly Swarm" es innovador y viable.  
La implementación actual es incompleta pero recuperable.**

---

**Fin del Informe**  
*24 de Febrero 2026*
