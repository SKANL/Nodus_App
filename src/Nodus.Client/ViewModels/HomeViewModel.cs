using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodus.Client.Views;
using Nodus.Infrastructure.Services;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Common;
using Microsoft.Extensions.Logging;

namespace Nodus.Client.ViewModels;

/// <summary>
/// ViewModel for the Home/Dashboard page.
/// Implements traffic-light status, judge registration state, and optimistic sync.
/// </summary>
public partial class HomeViewModel : ObservableObject, IDisposable
{
    private readonly IDatabaseService _db;
    private readonly BleClientService _bleService;
    private readonly SwarmService _swarmService;
    private readonly Nodus.Client.Services.CloudProjectSyncService _cloudSync;
    private readonly ILogger<HomeViewModel> _logger;
    private readonly CancellationTokenSource _lifetimeCts = new();

    // ── Status Bar (Traffic Light) ─────────────────────────────────────────
    [ObservableProperty] private string _statusMessage = "Iniciando...";
    [ObservableProperty] private Color _statusColor = Colors.Gray;
    [ObservableProperty] private string _statusIcon = "⚪";

    // ── Judge Identity ─────────────────────────────────────────────────────
    [ObservableProperty] private string _judgeName = "";
    [ObservableProperty] private string _judgeInitial = "?";
    [ObservableProperty] private bool _isJudgeRegistered;
    [ObservableProperty] private string _eventName = "";

    // ── Stats Row ──────────────────────────────────────────────────────────
    [ObservableProperty] private int _pendingVoteCount;
    [ObservableProperty] private int _syncedVoteCount;
    [ObservableProperty] private bool _isSyncing;

    // ── BLE availability ──────────────────────────────────────────────────
    private bool _bleAvailable = true;

    public HomeViewModel(
        IDatabaseService db,
        BleClientService bleService,
        SwarmService swarmService,
        Nodus.Client.Services.CloudProjectSyncService cloudSync,
        ILogger<HomeViewModel> logger)
    {
        _db = db;
        _bleService = bleService;
        _swarmService = swarmService;
        _cloudSync = cloudSync;
        _logger = logger;

        // Guard BLE subscriptions — on platforms without Bluetooth (Windows desktop, simulator)
        // the underlying Shiny manager can throw. We degrade gracefully to offline-only mode.
        try
        {
            _bleService.ConnectionStatusChanged += OnConnectionStatusChanged;
        }
        catch (Exception ex)
        {
            _bleAvailable = false;
            _logger.LogWarning(ex, "BLE unavailable — running in offline-only mode");
        }

        try
        {
            _swarmService.PropertyChanged += OnSwarmServicePropertyChanged;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Swarm service subscription failed — relay features disabled");
        }

        _ = RefreshAsync();
    }

    /// <summary>
    /// Reloads judge identity and status. Called on page Appearing and after QR scan.
    /// </summary>
    public async Task RefreshAsync()
    {
        try
        {
            var name = await SecureStorage.Default.GetAsync(Nodus.Shared.NodusConstants.KEY_JUDGE_NAME);
            var eventId = await SecureStorage.Default.GetAsync(Nodus.Shared.NodusConstants.KEY_EVENT_ID);

            IsJudgeRegistered = !string.IsNullOrEmpty(name);

            if (IsJudgeRegistered)
            {
                JudgeName = name!;
                JudgeInitial = name![0].ToString().ToUpper();
            }
            else
            {
                JudgeName = "";
                JudgeInitial = "?";
                StatusMessage = "Escanea el QR del Evento para comenzar";
                StatusColor = Colors.Gray;
                StatusIcon = "⚪";
                return;
            }

            // Load event name from DB
            if (!string.IsNullOrEmpty(eventId))
            {
                // Trigger cloud sync if possible
                _ = Task.Run(async () =>
                {
                    await _cloudSync.SyncProjectsAsync(eventId, _lifetimeCts.Token);
                });

                var eventResult = await _db.GetEventAsync(eventId, _lifetimeCts.Token);
                EventName = eventResult.IsSuccess ? eventResult.Value.Name : "Active Event";
            }

            await UpdateStatusAsync(_lifetimeCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh failed");
            StatusMessage = "Error al cargar datos";
            StatusColor = Colors.Red;
        }
    }

    private void OnConnectionStatusChanged(object? sender, bool isConnected)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try { await UpdateStatusAsync(_lifetimeCts.Token); }
            catch (Exception ex) { _logger.LogError(ex, "Error updating status on connection change"); }
        });
    }

    private void OnSwarmServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SwarmService.IsMuleMode) or nameof(SwarmService.CurrentState))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try { await UpdateStatusAsync(_lifetimeCts.Token); }
                catch (Exception ex) { _logger.LogError(ex, "Error updating status on swarm change"); }
            });
        }
    }

    public async Task UpdateStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var pendingResult = await _db.GetPendingVotesAsync(ct);
            int pending = pendingResult.IsSuccess ? pendingResult.Value?.Count ?? 0 : 0;
            PendingVoteCount = pending;

            // Update synced count via SyncStats
            var statsResult = await _db.GetSyncStatsAsync(ct);
            SyncedVoteCount = statsResult.IsSuccess ? statsResult.Value.SyncedVotes : 0;

            // Traffic Light
            if (!_bleAvailable)
            {
                // BLE not available on this platform — degrade to offline-only UI
                StatusIcon = pending > 0 ? "🟡" : "🔵";
                StatusColor = pending > 0 ? Color.FromArgb("#F59E0B") : Color.FromArgb("#3B82F6");
                StatusMessage = pending > 0
                    ? $"{pending} voto{(pending == 1 ? "" : "s")} guardado{(pending == 1 ? "" : "s")} localmente"
                    : "Modo offline — Bluetooth no disponible en esta plataforma";
            }
            else if (_swarmService.IsMuleMode)
            {
                StatusIcon = "🟣";
                StatusColor = Colors.Purple;
                StatusMessage = "Modo Mula: Camina a la mesa de control para sincronizar";
            }
            else if (_bleService.IsConnected)
            {
                StatusIcon = "🟢";
                StatusColor = Color.FromArgb("#22C55E");
                StatusMessage = pending > 0 ? $"Conectado  —  Sincronizando {pending} votos..." : "Conectado  —  Todo sincronizado ✓";

                if (pending > 0 && !IsSyncing)
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(500, ct);
                        await SyncPendingVotesAsync(ct);
                    }, ct);

                // Trigger true offline Project sync (BLE via GATT stream)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var projs = await _db.GetProjectsAsync(this.EventName, ct); // just grab all
                        var allProjsResult = await _db.GetAllProjectsAsync(ct);
                        if (allProjsResult.IsSuccess && allProjsResult.Value.Count == 0)
                        {
                            var bleResult = await _bleService.GetProjectsFromServerAsync(ct);
                            if (bleResult.IsSuccess && bleResult.Value != null)
                            {
                                foreach (var p in bleResult.Value) await _db.SaveProjectAsync(p, ct);
                            }
                        }
                    }
                    catch (Exception ex) { _logger.LogError(ex, "BLE Project Sync Trigger failed"); }
                }, ct);
            }
            else if (pending > 0)
            {
                StatusIcon = "🟡";
                StatusColor = Color.FromArgb("#F59E0B");
                StatusMessage = $"{pending} voto{(pending == 1 ? "" : "s")} guardado{(pending == 1 ? "" : "s")} localmente — esperando conexión";
            }
            else
            {
                StatusIcon = "🔴";
                StatusColor = Color.FromArgb("#EF4444");
                StatusMessage = "Buscando Servidor Nodus...";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "Error updating status"); }
    }

    // ── Navigation Commands ────────────────────────────────────────────────

    /// <summary>Scan Event QR to register as judge (first time).</summary>
    [RelayCommand]
    private async Task NavigateToRegistrationAsync(CancellationToken ct)
    {
        try
        {
            // Camera permission MUST be granted before navigating to ScanPage.
            // On Android, CameraBarcodeReaderView throws SecurityException without it.
            if (!await EnsureCameraPermissionAsync()) return;
            await Shell.Current.GoToAsync($"{nameof(Views.ScanPage)}?Mode=JudgeRegistration");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to ScanPage for JudgeRegistration");
            await ShowNavErrorAsync("No se pudo abrir el escáner. Verifica que la app tenga permisos de cámara en Ajustes del sistema.");
        }
    }

    /// <summary>Scan Project QR to vote (after judge is registered).</summary>
    [RelayCommand]
    private async Task NavigateToScanAsync(CancellationToken ct)
    {
        try
        {
            if (!await EnsureCameraPermissionAsync()) return;
            await Shell.Current.GoToAsync($"{nameof(Views.ScanPage)}?Mode=ProjectScan");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to ScanPage for ProjectScan");
            await ShowNavErrorAsync("No se pudo abrir el escáner de proyectos. Verifica que la app tenga permisos de cámara.");
        }
    }

    /// <summary>
    /// Requests camera permission at runtime. Returns true if granted.
    /// Must be called from the UI thread (RelayCommand ensures this).
    /// </summary>
    private static async Task<bool> EnsureCameraPermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status == PermissionStatus.Granted) return true;

        // Rationale dialog before system prompt (Android only, ShouldShowRationale is a no-op elsewhere)
        if (Permissions.ShouldShowRationale<Permissions.Camera>())
            await ShowAlertOnCurrentPageAsync(
                "Permiso de Cámara",
                "Nodus necesita acceso a la cámara para escanear los códigos QR del evento.",
                "Entendido");

        status = await Permissions.RequestAsync<Permissions.Camera>();

        if (status != PermissionStatus.Granted)
        {
            await ShowAlertOnCurrentPageAsync(
                "Permiso Denegado",
                "Sin acceso a la cámara no es posible escanear QR. Habilítalo en Ajustes → Aplicaciones → Nodus → Permisos.",
                "OK");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Shows a DisplayAlert on the current page without dereference warnings.
    /// Uses safe window-first-or-default access pattern.
    /// </summary>
    private static async Task ShowAlertOnCurrentPageAsync(string title, string message, string button)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return;
        await page.DisplayAlertAsync(title, message, button);
    }

    private static async Task ShowNavErrorAsync(string message)
    {
        await ShowAlertOnCurrentPageAsync("Error de Navegación", message, "OK");
    }

    [RelayCommand]
    private async Task ShowConnectionMetricsAsync(CancellationToken ct)
    {
        var role = "Client";
#if ANDROID
        role = _swarmService.CurrentState == Nodus.Infrastructure.Services.SwarmState.Link ? "Router (Firefly)" : "Client";
#endif
        var signal = _bleService.LastRssi != 0 ? $"{_bleService.LastRssi} dBm" : "N/A";
        var peers = _swarmService.NeighborLinkCount;

        var message = $"Rol de Nodus: {role}\nSeñal del Servidor: {signal}\nNodos Cercanos: {peers}";

        var page = Application.Current?.Windows[0].Page;
        if (page != null)
        {
            await page.DisplayAlertAsync("Métricas de Conexión", message, "Cerrar");
        }
    }

    // ── Sync ───────────────────────────────────────────────────────────────

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SyncPendingVotesAsync(CancellationToken ct)
    {
        if (IsSyncing) return;
        IsSyncing = true;
        try
        {
            var pendingResult = await _db.GetPendingVotesAsync(ct);
            if (pendingResult.IsFailure || pendingResult.Value?.Count == 0) return;

            int successCount = 0;
            foreach (var vote in pendingResult.Value!)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var syncResult = await _bleService.SendVoteAsync(vote, ct);
                    if (syncResult.IsSuccess)
                    {
                        vote.Status = Nodus.Shared.Models.SyncStatus.Synced;
                        vote.SyncedAtUtc = DateTime.UtcNow;
                        await _db.SaveVoteAsync(vote, ct);
                        successCount++;
                    }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to sync vote {VoteId}", vote.Id); }
            }

            _logger.LogInformation("Sync completed: {Success}/{Total}", successCount, pendingResult.Value.Count);
            await UpdateStatusAsync(ct);
        }
        catch (OperationCanceledException) { StatusMessage = "Sincronización cancelada"; }
        catch (Exception ex) { _logger.LogError(ex, "Sync failed"); StatusMessage = "Sincronización fallida"; }
        finally { IsSyncing = false; }
    }

    public void Dispose()
    {
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
        try { _bleService.ConnectionStatusChanged -= OnConnectionStatusChanged; } catch { /* BLE may not be initialized */ }
        try { _swarmService.PropertyChanged -= OnSwarmServicePropertyChanged; } catch { /* Swarm may not be initialized */ }
    }
}
