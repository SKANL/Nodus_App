# Informe de Cruce: Arquitectura Documentada vs Realidad del Código

> **Fecha:** Sesión activa  
> **Plataforma del Servidor confirmada:** Android  
> **Veredicto general:** ✅ SUFICIENTE para un MVP funcional — con 3 brechas críticas que deben cerrarse antes de producción

---

## 1. Resumen Ejecutivo

El protocolo **Firefly** está correctamente implementado en su núcleo (FSM, Trickle, seguridad AES-GCM, chunking BLE, anti-replay básico). El sistema puede funcionar en la práctica con las correcciones de la sesión actual ya aplicadas.

**Sin embargo, existen 3 brechas de protocolo que comprometen propiedades de seguridad y corrección enunciadas en los documentos:**

| Prioridad | Brecha | Impacto |
|-----------|--------|---------|
| 🔴 P1 | `NodusPacket` sin `hops[]` ni `ttl` — loops multi-hop posibles | Loops, duplicados, indefinición de red-mesh |
| 🔴 P2 | `VoteIngestionService` sin validación de antigüedad ni firma | Anti-replay incompleto, replay attacks posibles |
| 🟡 P3 | `SwarmService` usa `IsConnected` como proxy de RSSI | Cualquier conexión activa eleva a CANDIDATE sin medir señal real |

Las demás diferencias son desviaciones de diseño aceptables para MVP o inconsistencias de documentación ya previstas.

---

## 2. Análisis Capa por Capa

### 2.1 Capa de Red / Swarm (FSM Firefly)

#### ✅ Implementado correctamente

| Feature | Documentado en | Estado en código |
|---------|---------------|-----------------|
| Estados SEEKER / CANDIDATE / LINK / COOLDOWN | doc 12 §3A | `SwarmState` enum + FSM en `SwarmService.cs` ✅ |
| Heartbeat cada 5s | doc 12 `OnHeartbeat()` | `_heartbeat.Interval = TimeSpan.FromSeconds(5)` ✅ |
| Redundancy check `k=2` | doc 02 §Trickle, doc 11 §1B | `if (NeighborLinkCount >= 2) CurrentState = Seeker` ✅ |
| Max LINK duration 60s | doc 12 §3B | `MAX_LINK_DURATION_SECONDS = 60` ✅ |
| Cooldown 5 minutos | doc 12 §3B | `COOLDOWN_MINUTES = 5` ✅ |
| RSSI threshold -75dBm | doc 12 §3B1 | `const int RSSI_THRESHOLD = -75` (declarado) ✅ |
| Mule Mode (10min sin server) | doc 11 §3 | `MULE_MODE_THRESHOLD_MINUTES = 10`, flag `IsMuleMode` ✅ |
| Rotación de héroe distribuida | doc 12 §3B | Cooldown + timer garantizan rotación ✅ |

#### ❌ / ⚠️ Brechas

**Brecha 3 (🟡 P3) — RSSI no se lee realmente:**

```csharp
// SwarmService.cs línea 155
// Ideal: _bleClient.LastRssi > RSSI_THRESHOLD
// ACTUAL (simplificado):
if (_bleClient.IsConnected)  // ← cualquier conexión = CANDIDATE
```

El comentario en el propio código reconoce la simplificación. `_bleClient.LastRssi` existe pero no se usa en `CheckStateAsync()`. Consecuencia: un juez con señal débil (-85dBm) se eleva a CANDIDATE igual que uno a 1m de distancia.

**Desviación menor — T_wait range:**

- Doc 12: `T_wait = Random(5s, 30s)`  
- Código: `Random.Shared.Next(2000, 10000)` → 2–10s

La ventana de 2-10s es más agresiva (mayor probabilidad de colisión de candidatos). Funcional pero no alineado con la especificación final.

**Desviación menor — `await` dentro del heartbeat tick bloquea el timer:**

```csharp
// SwarmService.CheckStateAsync()
await _dateTime.Delay(TimeSpan.FromMilliseconds(randomWait)); // 2-10s
```

Durante ese delay, si el timer vuelve a llamar `CheckStateAsync()` (posible con dos ticks seguidos), hay race condition en `CurrentState`. No explosivo en MVP pero sí un bug latente.

---

### 2.2 Capa de Protocolo BLE (Packets y Chunking)

#### ✅ Implementado correctamente

| Feature | Documentado en | Estado en código |
|---------|---------------|-----------------|
| MTU = 180 bytes | doc 10 §2, `NodusConstants.MTU_SIZE = 180` | `ChunkerService.MaxMtu = 180` ✅ |
| Formato chunk: Header + Data packets | doc 10 §Chunking Protocol | `ChunkHeader(5 bytes) + DataPackets([msgId][idx][data])` ✅ |
| `ChunkAssembler` para reensamblado | doc 10 §Media Sync | Clase interna en `ChunkerService.cs` ✅ |
| `PacketType`: JSON(`0x01`), MEDIA(`0x02`), ACK(`0xA1`), PROJECTS(`0x03`) | `NodusConstants` | Definidos y usados en `VoteIngestionService` ✅ |
| ACK formato `[0xA1][voteId(16bytes)]` | doc 10 §ACK | `CreateAckPayload()` en `VoteIngestionService` ✅ |
| Separación métrica (BLE) vs media (Wi-Fi) | doc 03 §3A-B | Flujo `PayloadJson` por BLE, `LocalPhotoPath` separado ✅ |
| PacketTracker anti-loop (TTL 10min, max 10k) | doc 04 §Anti-replay | `ConcurrentDictionary` + limpieza periódica + OOM guard ✅ |

#### ❌ Brecha P1 (🔴 CRÍTICA) — `NodusPacket` sin `hops[]` ni `ttl`

Documentado en doc 02 y doc 12:
> "A packet includes [Trace: C, B, A]" — Max Hop Count = 2  
> `MAX_HOPS_TTL = 2` en `NodusConstants`

**Código actual de `NodusPacket.cs`:**

```csharp
public class NodusPacket {
    public string Id ...
    public MessageType Type ...
    public long Timestamp ...
    public string SenderId ...
    public byte[] Nonce ...
    public byte[] Signature ...
    public byte[] EncryptedPayload ...
    // ← NO hay hops[], NO hay ttl, NO hay trace/path
}
```

La constante `MAX_HOPS_TTL = 2` está definida en `NodusConstants` pero **ningún servicio la lee ni la aplica**.

**Consecuencia:** En un escenario multi-hop real (Node C → Relay B → Relay A → Server), el servidor no puede:
1. Detectar un loop (C→B→A→B→...)
2. Dropear paquetes que viajaron más de 2 saltos
3. El `PacketTracker` previene duplicados **dentro del mismo nodo** pero no coordina TTL entre nodos

**Fix requerido:**

```csharp
public class NodusPacket {
    // ... campos existentes ...
    public byte Ttl { get; set; } = NodusConstants.MAX_HOPS_TTL; // 2
    public List<string> Hops { get; set; } = new(); // Trace de nodos intermedios
}
```

Y en el relay (cuando reenvía): `packet.Ttl--; if (packet.Ttl == 0) return; packet.Hops.Add(myNodeId);`

---

### 2.3 Capa de Seguridad

#### ✅ Implementado correctamente

| Feature | Documentado en | Estado en código |
|---------|---------------|-----------------|
| AES-GCM (Nonce 12 + Tag 16) | doc 04 §Encryption | `CryptoHelper.Encrypt/Decrypt` — formato `[Nonce(12)][Ciphertext][Tag(16)]` ✅ |
| PBKDF2 / HMAC-SHA256 / 100k iteraciones | doc 04 §Key Derivation | `Rfc2898DeriveBytes.Pbkdf2(..., 100_000, SHA256, 32)` ✅ |
| Salt 16 bytes | `NodusConstants.SALT_SIZE = 16` | `DeriveKeyFromPassword` valida `salt.Length != 16` ✅ |
| Firma digital de votos (asimétrica) | doc 04 §Signing | `CryptoHelper.GenerateSigningKeys()` + `SignData()` + `VerifySignature()` ✅ |
| Anti-replay por ID de paquete | doc 04 §Anti-Replay | `PacketTracker.TryProcess(packetId)` ✅ |
| EdDSA / P-256 — ver nota abajo | doc 04 dice Ed25519 | Código usa ECDsa P-256 |

**Nota de implementación — ECDsa P-256 vs Ed25519:**

Doc 04 especifica Ed25519 "por performance" (`NSec.Cryptography`). El código usa `ECDsa.Create(ECCurve.NamedCurves.nistP256)` — **ambos son seguros**, P-256 tiene soporte nativo sin NuGet en .NET 10. El comentario en `CryptoHelper.cs` explica la decisión. Para producción: si se quiere Ed25519 puro, agregar `NSec.Cryptography` o esperar a que `System.Security.Cryptography.ECDiffieHellman`-based Ed25519 madure en .NET. No es una brecha de seguridad, es una desviación de tecnología documentada.

#### ❌ Brecha P2 (🔴 CRÍTICA) — `VoteIngestionService` sin validación de firma ni timestamp

**Doc 04 §Anti-Replay:**
> "Reject if timestamp older than 2 hours"  
> "Verify signature before processing"

**Código actual `ProcessJsonPacketAsync()`:**

```csharp
private async Task ProcessJsonPacketAsync(string json) {
    var packet = NodusPacket.FromJson(json);
    if (packet == null) return;

    // ← NO HAY: verificación de packet.Signature
    // ← NO HAY: validación de age (DateTimeOffset.UtcNow - packet.Timestamp > 2h)
    // ← NO HAY: verificación de PacketTracker.TryProcess(packet.Id) en este flujo

    if (packet.EncryptedPayload != null && _currentEventAesKey != null) {
        var decryptedBytes = CryptoHelper.Decrypt(packet.EncryptedPayload, ...);
        // → directo a guardar en DB sin validar firmante
    }
}
```

**Consecuencia:** Un atacante que captura un voto cifrado puede reenviarlo días después (si tiene la clave AES comprometida o si el dispositivo servidor se reinicia sin limpiar la caché del PacketTracker). También, si la clave AES la conocen múltiples jueces, un juez puede reenviar el voto de otro.

**Fix requerido (mínimo viable):**

```csharp
// En ProcessJsonPacketAsync:
// 1. Age check
var age = TimeSpan.FromSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - packet.Timestamp);
if (age > TimeSpan.FromHours(2)) {
    _logger.LogWarning("Packet {Id} rejected: too old ({Age})", packet.Id, age);
    return;
}

// 2. PacketTracker (anti-replay cross-process)
if (!_packetTracker.TryProcess(packet.Id)) {
    _logger.LogWarning("Packet {Id} rejected: already seen", packet.Id);
    return;
}
```

La verificación de firma requiere tener la `PublicKey` del juez en el servidor — esto depende del flujo de handshake que debe almacenar claves por `JudgeId`.

---

### 2.4 Capa de Datos (Offline-First)

#### ✅ Implementado correctamente

| Feature | Estado |
|---------|--------|
| Offline-first: local DB primero, sync después | `LocalDatabaseService` + background `CloudSyncService` ✅ |
| Modelos Vote con `SyncStatus{Pending, Synced, SyncError}` | Implementado ✅ |
| GUID único por voto (append-only efectivo) | Nuevo GUID en cada `Vote` creado ✅ |
| Media separada (paths locales, Wi-Fi sync) | `LocalPhotoPath`, `LocalAudioPath`, `IsMediaSynced` ✅ |
| Sync cada 5min a MongoDB Atlas | `CloudSyncService` ✅ |

#### ⚠️ Desviación — LiteDB vs SQLite

Doc 03 especifica `sqlite-net-pcl`. El código usa **LiteDB** (NoSQL embebido). No es un problema funcional — LiteDB es válido para MAUI cross-platform — pero implica que queries de tipo relacional (JOIN Events+Votes para resultados) requieren lógica en C# en vez de SQL. Si los reportes finales necesitan queries complejas, considerar migrar o envolver en un repositorio.

---

### 2.5 Capa de Plataforma / BLE Hardware

#### ✅ Implementado correctamente

| Feature | Estado |
|---------|--------|
| `BleServerService` solo en Android (`#if ANDROID`) | ✅ Correcto — BLE peripheral hosting es Android-only en Shiny |
| `BleClientService` filtro por `SERVICE_UUID` | ✅ Solo se conecta a periféricos Nodus |
| Stubs en Windows/iOS para `IBleServerService` | ✅ No crashea en otras plataformas |
| AndroidManifest Server con permisos BLE completos | ✅ Corregido esta sesión |
| AndroidManifest Client con permisos BLE | ✅ Ya estaba correcto |

#### ⚠️ Funcionalidades documentadas sin implementar

**ManufacturerData en RelayHostingService:**

Doc 02 §Manufacturer Data:
```
Byte 0: 0x02 (Relay indicator)
Byte 1: Battery% encoded
```

Código tiene la línea comentada con `// TODO: verify Shiny API`. Los Seekers no pueden preferir relays con más batería porque el dato nunca está en el advertisement. **Severidad baja para MVP** — la conectividad funciona sin él; la optimización de batería queda deshabilitada.

**iOS "Goodbye" packet:**

Doc 11-12: Si iOS entra a background → debe enviar paquete "Goodbye" y detener advertising. No implementado. **Impacto**: En iOS el BLE se detiene abruptamente, dejando al swarm con una entrada "zombie" hasta que expira el timeout de conexión. El Mule Mode compensa esto (10min sin servidor → modo local).

---

### 2.6 Seguridad por Plataforma (Admin → Android)

Confirmado en la sesión: el usuario pivotó a Android como plataforma del servidor (Admin node). Las implicaciones:

| Aspecto | Admin Windows (doc original) | Admin Android (decisión actual) |
|---------|-------------------------------|----------------------------------|
| BLE advertising | ❌ Requería código WinRT nativo | ✅ Nativo en Shiny |
| Número de conexiones simultáneas | ~7-8 (WinRT limit) | ~7-8 (Android GATT limit) |
| Proceso background | Win32 Service posible | Android Foreground Service (ya en manifiesto) |
| Pantalla durante evento | Opcional (PC laptop) | Requiere pantalla encendida o WakeLock |
| Robustez RF | Fija (posición del laptop) | Puede reposicionarse físicamente |
| Carga de procesamiento | Alta tolerancia | Limitada por batería Android |

**Recomendación**: El Admin Android debe correr con WakeLock + pantalla activa durante el evento. Agregar a la guía de operación.

---

## 3. Tabla Completa de Brechas

| # | Feature | Doc fuente | Estado | Severidad | Acción requerida |
|---|---------|-----------|--------|-----------|-----------------|
| 1 | `NodusPacket.Ttl` + `NodusPacket.Hops[]` | doc 02, 12 | ❌ Ausente | 🔴 ALTA | Agregar campos; relay decrementar TTL |
| 2 | Timestamp age check en VoteIngestion | doc 04 | ❌ Ausente | 🔴 ALTA | Rechazar paquetes > 2h |
| 3 | `PacketTracker.TryProcess()` en VoteIngestion | doc 04 | ❌ No conectado al flujo JSON | 🔴 ALTA | Inyectar y llamar en `ProcessJsonPacketAsync` |
| 4 | RSSI real en SwarmService CANDIDATE check | doc 12 | ⚠️ Proxy (`IsConnected`) | 🟡 MEDIA | Usar `_bleClient.LastRssi > RSSI_THRESHOLD` |
| 5 | Verificación de firma en VoteIngestion | doc 04 | ❌ Ausente | 🟡 MEDIA | Verificar `packet.Signature` con PublicKey del juez |
| 6 | T_wait Trickle (5-30s) | doc 12 | ⚠️ 2-10s en código | 🟢 BAJA | Ajustar constantes |
| 7 | `ManufacturerData` en RelayHostingService | doc 02 | ⚠️ Comentado | 🟢 BAJA | Verificar API Shiny y descomentar |
| 8 | iOS "Goodbye" packet en background | doc 11, 12 | ❌ Ausente | 🟢 BAJA | Implementar; Mule Mode compensa |
| 9 | Ed25519 → ECDsa P-256 | doc 04 | ⚠️ Desviación de tecnología | 🟢 BAJA | Aceptable; documentar decisión |
| 10 | SQLite (`sqlite-net-pcl`) → LiteDB | doc 03 | ⚠️ Desviación de tecnología | 🟢 BAJA | Aceptable para MVP |
| 11 | `await Delay` dentro del heartbeat tick | doc 12 | ⚠️ Race condition potencial | 🟡 MEDIA | Usar flag `_candidateInProgress` para debounce |
| 12 | `Nodus.Simulator` console app | doc 08 | ❌ No existe | 🟢 BAJA | Crear para testing de swarm |
| 13 | Admin = Android (pivot de Windows) | doc 01 | ✅ Decisión confirmada | — | Actualizar doc 01 con nueva plataforma |

---

## 4. Veredicto de Suficiencia Arquitectónica

### ¿Los algoritmos son suficientes para el caso de uso?

**SÍ**, con las siguientes aclaraciones:

| Dimensión | Veredicto | Evidencia |
|-----------|-----------|-----------|
| **Conectividad BLE básica** | ✅ SUFICIENTE | UUID scan, GATT R/W/Notify, chunking 180 bytes funcional |
| **Prevención de storm** | ✅ SUFICIENTE | Trickle k=2 + Cooldown implementados |
| **Anti-duplicados local** | ✅ SUFICIENTE | PacketTracker con TTL=10min en cada nodo |
| **Seguridad del QR/handshake** | ✅ SUFICIENTE | PBKDF2+AES-GCM, fix URL-decode aplicado |
| **Offline-first** | ✅ SUFICIENTE | LiteDB local + MongoDB sync |
| **Multi-hop (>1 salto)** | ⚠️ PARCIAL | Funciona solo si no hay loops; sin TTL no hay garantía |
| **Anti-replay completo** | ❌ INCOMPLETO | Falta timestamp check y conectar PacketTracker al flujo JSON |
| **Optimización por RSSI** | ⚠️ PARCIAL | Proxy de IsConnected — funcional pero no óptimo |
| **iOS background** | ❌ NO APLICA | Plataforma excluida para relay en background por diseño |

### Nota sobre el tamaño del evento

Para un hackathon de **30-100 estudiantes**, la arquitectura es apta. Los cálculos del Trickle (5-8 nodos activos en una sala de 50) cuadran con el diseño. El cuello de botella es el GATT server de Android (~7-8 conexiones simultáneas) — para 100+ jueces se necesitaría múltiples servidores o una red Wi-Fi.

---

## 5. Plan de Acción Priorizado

### Sprint 1 — Brechas críticas (necesitar cerrar antes del primer piloto)

**5.1 Agregar TTL/hops a NodusPacket**

Archivo: `src/Nodus.Shared/Protocol/NodusPacket.cs`
```csharp
public byte Ttl { get; set; } = NodusConstants.MAX_HOPS_TTL; // 2
public List<string> Hops { get; set; } = new();
```

Archivo: relay en `BleClientService` — al reenviar paquete:
```csharp
packet.Ttl--;
if (packet.Ttl <= 0 || packet.Hops.Contains(myNodeId)) return; // drop
packet.Hops.Add(myNodeId);
```

**5.2 Conectar PacketTracker + timestamp check a VoteIngestion**

Archivo: `src/Nodus.Shared/Services/VoteIngestionService.cs`

Inyectar `PacketTracker _tracker` y en `ProcessJsonPacketAsync`:
```csharp
var ageSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - packet.Timestamp;
if (ageSeconds > 7200) { _logger.LogWarning("Stale packet rejected"); return; }
if (!_tracker.TryProcess(packet.Id)) { _logger.LogWarning("Duplicate packet rejected"); return; }
```

**5.3 Debounce en SwarmService heartbeat**

Archivo: `src/Nodus.Infrastructure/SwarmService.cs`

```csharp
private bool _candidateInProgress = false;

// En CheckStateAsync, Seeker → Candidate block:
if (_bleClient.IsConnected && !_candidateInProgress) {
    _candidateInProgress = true;
    try {
        // ... lógica actual ...
    } finally {
        _candidateInProgress = false;
    }
}
```

### Sprint 2 — Mejoras de calidad (antes del evento real)

- Leer RSSI real: `if (_bleClient.LastRssi > RSSI_THRESHOLD)` en lugar del proxy
- Ajustar T_wait a 5000-30000ms para alinear con doc 12
- Verificar y descomentar `ManufacturerData` en `RelayHostingService`

### Sprint 3 — Nice-to-have

- Verificación de firma de votos (requiere almacenar PublicKey del juez en el Server)
- Implementar `Nodus.Simulator` para stress test del swarm
- Actualizar doc 01 con Admin = Android

---

## 6. Conclusión

El código **ya hace el trabajo central** del Firefly Protocol: FSM correcto, Trickle k=2, BLE chunking funcional, criptografía robusta, offline-first operativo. Los fixes de esta sesión (permisos Android, QR URL-decode, auto-connect sin nombre) resuelven los bloqueos de productividad.

Las 3 brechas críticas (TTL/hops, timestamp anti-replay, debounce heartbeat) no impiden un demo o piloto controlado pero **deben cerrarse antes de producción** porque afectan propiedades de correctitud del protocolo (loops, replay de votos). Son cambios pequeños (< 30 líneas de código en total).

**Veredicto para el pivote a Android:** La decisión es arquitectónicamente correcta y no introduce deuda técnica nueva — todo el código BLE ya asumía Android como plataforma real del servidor.
