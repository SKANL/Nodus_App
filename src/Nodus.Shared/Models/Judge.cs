using MongoDB.Bson.Serialization.Attributes;

namespace Nodus.Shared.Models;

/// <summary>
/// Modelo de Juez — colección nueva en MongoDB.
/// 
/// CONTEXTO: En el sistema actual, el juez solo existía en SecureStorage del dispositivo
/// (nombre, clave AES, clave pública/privada). Este modelo centraliza su registro
/// en MongoDB para permitir consultas, auditoría y panel de administración.
///
/// SEGURIDAD: La clave PRIVADA NUNCA se almacena aquí — permanece solo en el dispositivo.
/// </summary>
public class Judge
{
    /// <summary>
    /// ID único del juez.
    /// Formato: "JUDGE-{NombreSinEspacios}-{UnixTimestamp}"
    /// Ejemplo: "JUDGE-OctavioGarcia-1739901600"
    /// </summary>
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Nombre del juez — equivalente a NodusConstants.KEY_JUDGE_NAME en SecureStorage.
    /// Se obtiene del QR al hacer el registro.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Clave pública RSA del juez — equivalente a NodusConstants.KEY_PUBLIC_KEY.
    /// Permite al servidor verificar firmas de los votos recibidos por BLE.
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// ID del evento al que pertenece este juez.
    /// Se obtiene del QR (parámetro "eid").
    /// Indexado en MongoDB para consultas por evento.
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// Si es false, el juez fue desactivado por el administrador y no puede votar.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Fecha/hora de registro del juez (UTC).
    /// Se establece en el momento del escaneo del QR.
    /// </summary>
    public DateTime RegisteredAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Último momento en que el juez tuvo actividad (voto o handshake BLE).
    /// Se actualiza automáticamente. Útil para detectar jueces inactivos.
    /// </summary>
    public DateTime? LastSeenAtUtc { get; set; }
}
