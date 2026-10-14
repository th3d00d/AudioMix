namespace AudioMix;

/// <summary>
/// Steuert die Anwendung ohne Hauptfenster: Tray-Symbol + Kontextmenü.
/// Das Einstellungsfenster wird nur geöffnet, wenn der Benutzer es möchte.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly VolumeBalancer _balancer = new();
    private readonly Control _ui = new();   // unsichtbar; dient nur dazu, Aufrufe auf den UI-Thread zu holen
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _enabledItem;
    private SettingsForm? _form;

    public TrayApplicationContext(bool startHidden)
    {
        _ = _ui.Handle;   // Handle jetzt im UI-Thread erzeugen, damit BeginInvoke später von jedem Thread funktioniert

        // Falls die EXE verschoben wurde: Pfad im Autostart-Eintrag aktualisieren (kostet nur einen Registry-Zugriff).
        if (_settings.StartWithWindows)
            AutostartManager.TrySet(true);

        _enabledItem = new ToolStripMenuItem("Enabled") { CheckOnClick = true, Checked = _settings.Enabled };
        _enabledItem.Click += (_, _) =>
        {
            _settings.Enabled = _enabledItem.Checked;
            OnSettingsChanged();
            _form?.ReloadEnabled();
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add(_enabledItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,   // später durch ein eigenes Symbol ersetzbar
            Text = "AudioMix",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowSettings();

        _balancer.Apply(_settings);

        // Beim Windows-Start (--background) bleibt alles unsichtbar; beim manuellen Start öffnet sich das Fenster.
        if (!startHidden)
            ShowSettings();
    }

    /// <summary>Wird aufgerufen, wenn eine zweite Programminstanz gestartet wurde.</summary>
    public void RequestShowSettings()
    {
        try
        {
            if (_ui.IsHandleCreated && !_ui.IsDisposed)
                _ui.BeginInvoke(new Action(ShowSettings));
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { }
    }

    private void ShowSettings()
    {
        if (_form is { IsDisposed: false })
        {
            if (_form.WindowState == FormWindowState.Minimized)
                _form.WindowState = FormWindowState.Normal;
            _form.Activate();
            return;
        }

        _form = new SettingsForm(_settings, _balancer, OnSettingsChanged);
        _form.FormClosed += (_, _) =>
        {
            _form = null;
            // Nach dem Schließen den Speicher des Fensters zeitnah freigeben.
            _ui.BeginInvoke(new Action(() => GC.Collect()));
        };
        _form.Show();
        _form.Activate();
    }

    /// <summary>Gemeinsamer Weg für Tray-Menü und Fenster: speichern, anwenden, Menü synchronisieren.</summary>
    private void OnSettingsChanged()
    {
        _settings.Save();
        _balancer.Apply(_settings);
        _enabledItem.Checked = _settings.Enabled;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Visible = false;   // Symbol sofort entfernen (sonst bleibt es bis zum Mouse-Over sichtbar)
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
            _ui.Dispose();
            _balancer.Dispose();
        }
        base.Dispose(disposing);
    }
}