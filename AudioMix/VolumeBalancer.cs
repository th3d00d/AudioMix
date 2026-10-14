using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioMix;

/// <summary>Ein Wiedergabegerät für die Auswahlfelder.</summary>
public sealed record AudioDevice(string Id, string Name, bool Available = true)
{
    public override string ToString() => Available ? Name : $"{Name} (not connected)";
}

/// <summary>
/// Hält die Lautstärke von Gerät 2 immer auf (100 % - Lautstärke von Gerät 1).
///
/// Komplett ereignisgesteuert: Windows meldet jede Lautstärke-/Stummschalt-Änderung per Callback.
/// Ohne Änderung läuft hier kein Code (kein Polling, kein Dauer-Timer).
/// </summary>
public sealed class VolumeBalancer : IDisposable
{
    private const int ThrottleMs = 15;          // fasst schnelle Änderungen (Slider ziehen) zusammen
    private const int ReconnectDelayMs = 500;   // wartet, bis sich ein Gerätewechsel "beruhigt" hat
    private const float Tolerance = 0.005f;     // Abweichung < 0,5 % gilt als gleich

    // Kennung der eigenen Änderungen an Gerät 2.
    // Windows reicht sie in der Benachrichtigung zurück, so erkennen wir das "Echo" und ignorieren es.
    private static readonly Guid OwnContext = new("8f3c1c52-6f0a-4a3b-9c35-5a0d2e7b6e11");

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDeviceNotificationClient _watcher;
    private readonly object _gate = new();      // schützt alle Felder unten
    private readonly System.Threading.Timer _syncTimer;
    private readonly System.Threading.Timer _reconnectTimer;
    private int _syncPending;

    private string? _idA, _idB;
    private bool _active, _syncMute, _disposed;
    private MMDevice? _devA, _devB;
    private AudioEndpointVolume? _volA, _volB;
    private bool _lastMuteA;
    private float _lastTarget = -1f, _lastReadBack = -1f;

    public string Status { get; private set; } = "Disabled";
    public event Action? StatusChanged;

    public VolumeBalancer()
    {
        _syncTimer = new System.Threading.Timer(_ => OnSyncTimer(), null, Timeout.Infinite, Timeout.Infinite);
        _reconnectTimer = new System.Threading.Timer(_ => OnReconnectTimer(), null, Timeout.Infinite, Timeout.Infinite);

        // Meldet, wenn Geräte ein-/ausgesteckt werden (z. B. Bluetooth-Kopfhörer, die erst nach dem Start verbinden).
        // false = Events direkt auf dem Windows-Thread statt über den UI-Thread. Unser Handler startet
        // ohnehin nur einen Timer, sollte unkritisch sein.
        _watcher = _enumerator.CreateNotificationClient(useSynchronizationContext: false);
        _watcher.DeviceStateChanged += (_, e) => OnDeviceEvent(e.DeviceId);
        _watcher.DeviceAdded += (_, e) => OnDeviceEvent(e.DeviceId);
        _watcher.DeviceRemoved += (_, e) => OnDeviceEvent(e.DeviceId);
    }

    /// <summary>Alle aktuell aktiven Wiedergabegeräte (für die Auswahlfelder).</summary>
    public static List<AudioDevice> GetPlaybackDevices()
    {
        var list = new List<AudioDevice>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            try { list.Add(new AudioDevice(device.ID, device.FriendlyName)); }
            finally { device.Dispose(); }
        }
        list.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase));
        return list;
    }

    /// <summary>Übernimmt die Einstellungen (verbindet bzw. trennt die Geräte).</summary>
    public void Apply(AppSettings settings)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _active = settings.Enabled;
            _idA = settings.DeviceAId;
            _idB = settings.DeviceBId;
            _syncMute = settings.SyncMute;
            Reconnect();
        }
    }

    // ---------------------------------------------------------------- Verbinden / Trennen

    private void Reconnect()   // nur unter lock aufrufen
    {
        Disconnect();

        if (!_active) { SetStatus("Disabled"); return; }
        if (string.IsNullOrEmpty(_idA) || string.IsNullOrEmpty(_idB) || _idA == _idB)
        {
            SetStatus("Please select two different devices");
            return;
        }

        try
        {
            _devA = _enumerator.GetDevice(_idA);
            _devB = _enumerator.GetDevice(_idB);

            if (_devA.State != DeviceState.Active || _devB.State != DeviceState.Active)
            {
                Disconnect();
                SetStatus("Waiting for devices…");
                return;
            }

            _volA = _devA.AudioEndpointVolume;
            _volB = _devB.AudioEndpointVolume;
            _volB.NotificationGuid = OwnContext;   // alle Änderungen an Gerät 2 tragen unsere Kennung

            _volA.OnVolumeNotification += HandleVolumeNotification;
            _volB.OnVolumeNotification += HandleVolumeNotification;

            _lastMuteA = _volA.Mute;
            if (_syncMute) _volB.Mute = _lastMuteA;   // Startzustand angleichen
            Sync();

            SetStatus("Active – device 2 is now following device 1");
        }
        catch (Exception ex) when (IsDeviceError(ex))
        {
            Disconnect();
            SetStatus("Waiting for devices…");
        }
    }

    private void Disconnect()   // nur unter lock aufrufen
    {
        if (_volA is not null) _volA.OnVolumeNotification -= HandleVolumeNotification;
        if (_volB is not null) _volB.OnVolumeNotification -= HandleVolumeNotification;
        _volA = _volB = null;

        // MMDevice.Dispose gibt auch das Endpoint-Volume-Objekt frei und meldet den Callback bei Windows ab.
        SafeDispose(_devA);
        SafeDispose(_devB);
        _devA = _devB = null;

        _lastTarget = _lastReadBack = -1f;
    }

    // ---------------------------------------------------------------- Lautstärke-Ereignisse

    // Läuft auf einem COM-Thread von Windows -> nur minimal arbeiten, nichts blockieren.
    private void HandleVolumeNotification(AudioVolumeNotificationData data)
    {
        if (data.EventContext == OwnContext) return;   // eigenes Echo ignorieren

        // Throttling: höchstens ein Abgleich pro ThrottleMs, egal wie viele Ereignisse kommen.
        if (Interlocked.Exchange(ref _syncPending, 1) == 0)
        {
            try { _syncTimer.Change(ThrottleMs, Timeout.Infinite); }
            catch (ObjectDisposedException) { }
        }
    }

    private void OnSyncTimer()
    {
        Interlocked.Exchange(ref _syncPending, 0);
        lock (_gate)
        {
            if (!_disposed) Sync();
        }
    }

    /// <summary>Gleicht Gerät 2 an Gerät 1 an. Nur unter lock aufrufen.</summary>
    private void Sync()
    {
        if (_volA is null || _volB is null) return;

        try
        {
            // Lautstärke: Gerät 2 = 100 % - Gerät 1.
            // Gilt auch, wenn Gerät 2 von außen verstellt wurde -> springt zurück.
            float target = Math.Clamp(1f - _volA.MasterVolumeLevelScalar, 0f, 1f);
            float current = _volB.MasterVolumeLevelScalar;

            // Manche Geräte haben grobe Lautstärke-Stufen und runden den gesetzten Wert.
            // wenn sich nichts geändert hat, nicht erneut setzen.
            bool isEcho = target == _lastTarget && current == _lastReadBack;

            if (!isEcho && Math.Abs(current - target) > Tolerance)
            {
                _volB.MasterVolumeLevelScalar = target;
                _lastTarget = target;
                _lastReadBack = _volB.MasterVolumeLevelScalar;
            }

            // Stummschaltung: nur reagieren, wenn sich der Zustand von Gerät 1 geändert hat.
            // (Wird Gerät 2 von außen stummgeschaltet, greifen wir bewusst nicht ein.)
            if (_syncMute)
            {
                bool muteA = _volA.Mute;
                if (muteA != _lastMuteA)
                {
                    _lastMuteA = muteA;
                    _volB.Mute = muteA;
                }
            }
        }
        catch (Exception ex) when (IsDeviceError(ex))
        {
            // Gerät ist gerade verschwunden – der Geräte-Callback löst gleich einen Reconnect aus.
        }
    }

    // ---------------------------------------------------------------- Geräte-Ereignisse

    private void OnDeviceEvent(string deviceId)
    {
        bool relevant = string.Equals(deviceId, _idA, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(deviceId, _idB, StringComparison.OrdinalIgnoreCase);
        if (!relevant) return;

        try { _reconnectTimer.Change(ReconnectDelayMs, Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }

    private void OnReconnectTimer()
    {
        lock (_gate)
        {
            if (!_disposed && _active) Reconnect();
        }
    }

    // ---------------------------------------------------------------- Hilfsfunktionen

    private void SetStatus(string status)
    {
        if (Status == status) return;
        Status = status;
        StatusChanged?.Invoke();
    }

    private static bool IsDeviceError(Exception ex) => ex is COMException or ArgumentException;

    private static void SafeDispose(IDisposable? disposable)
    {
        try { disposable?.Dispose(); }
        catch (Exception) { /* Aufräumen darf nie fehlschlagen (z. B. wenn das Gerät schon abgesteckt ist) */ }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
        }

        /*
        try { _enumerator.UnregisterEndpointNotificationCallback(_watcher); }
        catch (Exception) { }
        */

        (_watcher as IDisposable)?.Dispose();   // meldet den Callback bei Windows ab

        _syncTimer.Dispose();
        _reconnectTimer.Dispose();
        _enumerator.Dispose();
    }
}