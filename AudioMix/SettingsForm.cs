namespace AudioMix;

/// <summary>
/// Einstellungsfenster. Wird nur bei Bedarf erzeugt und beim Schließen wieder freigegeben,
/// damit im Hintergrundbetrieb kein Fenster Speicher belegt.
/// Alle Änderungen werden sofort übernommen und gespeichert.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly VolumeBalancer _balancer;
    private readonly Action _onChanged;

    private readonly CheckBox _chkEnabled = new() { Text = "Volume-coupling active", AutoSize = true };
    private readonly ComboBox _cmbA = CreateCombo();
    private readonly ComboBox _cmbB = CreateCombo();
    private readonly CheckBox _chkMute = new() { Text = "Sync mute to device 2", AutoSize = true };
    private readonly CheckBox _chkAutostart = new() { Text = "Autostart on startup as background application", AutoSize = true };
    private readonly Label _lblStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private bool _loading;

    public SettingsForm(AppSettings settings, VolumeBalancer balancer, Action onChanged)
    {
        _settings = settings;
        _balancer = balancer;
        _onChanged = onChanged;

        // --- Fenster
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "AudioMix";
        Icon = SystemIcons.Application;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        // --- Layout
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Fill
        };
        AddSpanning(layout, _chkEnabled);
        AddLabeled(layout, "Device 1 (leading):", _cmbA);
        AddLabeled(layout, "Device 2 (opposing):", _cmbB);
        AddSpanning(layout, new Label
        {
            Text = "Volume device 2 = 100 % - volume device 1",
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        });
        AddSpanning(layout, _chkMute);
        AddSpanning(layout, _chkAutostart);
        AddSpanning(layout, _lblStatus);
        Controls.Add(layout);

        // --- Werte laden (noch ohne Ereignisse, sonst würde jede Zuweisung sofort speichern)
        _loading = true;
        PopulateDevices();
        _chkEnabled.Checked = settings.Enabled;
        _chkMute.Checked = settings.SyncMute;
        _chkAutostart.Checked = settings.StartWithWindows;
        UpdateStatus();
        _loading = false;

        _chkEnabled.CheckedChanged += OnUserChanged;
        _cmbA.SelectedIndexChanged += OnUserChanged;
        _cmbB.SelectedIndexChanged += OnUserChanged;
        _chkMute.CheckedChanged += OnUserChanged;
        _chkAutostart.CheckedChanged += OnUserChanged;

        _balancer.StatusChanged += OnStatusChanged;
        FormClosed += (_, _) => _balancer.StatusChanged -= OnStatusChanged;
    }

    /// <summary>Wird vom Tray-Menü aufgerufen, wenn dort "Aktiv" umgeschaltet wurde.</summary>
    public void ReloadEnabled()
    {
        _loading = true;
        _chkEnabled.Checked = _settings.Enabled;
        _loading = false;
        UpdateStatus();
    }

    // ---------------------------------------------------------------- Geräteliste

    private void PopulateDevices()
    {
        var devices = VolumeBalancer.GetPlaybackDevices();
        FillCombo(_cmbA, devices, _settings.DeviceAId, _settings.DeviceAName);
        FillCombo(_cmbB, devices, _settings.DeviceBId, _settings.DeviceBName);
    }

    private static void FillCombo(ComboBox combo, List<AudioDevice> devices, string? savedId, string? savedName)
    {
        combo.Items.Clear();
        foreach (var device in devices) combo.Items.Add(device);

        var selected = devices.FirstOrDefault(d => d.Id == savedId);

        // Gespeichertes Gerät ist gerade nicht verbunden: trotzdem anzeigen,
        // damit die Auswahl nicht versehentlich verloren geht.
        if (selected is null && !string.IsNullOrEmpty(savedId))
        {
            selected = new AudioDevice(savedId, savedName ?? "unknown device", Available: false);
            combo.Items.Add(selected);
        }

        combo.SelectedItem = selected;
    }

    // ---------------------------------------------------------------- Änderungen

    private void OnUserChanged(object? sender, EventArgs e)
    {
        if (_loading) return;

        _settings.Enabled = _chkEnabled.Checked;
        _settings.SyncMute = _chkMute.Checked;

        if (_cmbA.SelectedItem is AudioDevice a) { _settings.DeviceAId = a.Id; _settings.DeviceAName = a.Name; }
        if (_cmbB.SelectedItem is AudioDevice b) { _settings.DeviceBId = b.Id; _settings.DeviceBName = b.Name; }

        if (ReferenceEquals(sender, _chkAutostart))
        {
            if (AutostartManager.TrySet(_chkAutostart.Checked))
            {
                _settings.StartWithWindows = _chkAutostart.Checked;
            }
            else
            {
                MessageBox.Show(this, "Autostart setting could not be changed.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _loading = true;
                _chkAutostart.Checked = _settings.StartWithWindows;
                _loading = false;
            }
        }

        _onChanged();     // speichern, anwenden, Tray-Menü aktualisieren
        UpdateStatus();
    }

    // ---------------------------------------------------------------- Status

    private void OnStatusChanged()
    {
        // Kommt ggf. von einem Hintergrund-Thread -> auf den UI-Thread wechseln.
        try
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(new Action(UpdateStatus));
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { }
    }

    private void UpdateStatus() => _lblStatus.Text = "Status: " + _balancer.Status;

    // ---------------------------------------------------------------- Layout-Helfer

    private static ComboBox CreateCombo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };

    private static void AddSpanning(TableLayoutPanel table, Control control)
    {
        control.Margin = new Padding(3, 8, 3, 8);
        table.Controls.Add(control);
        table.SetColumnSpan(control, 2);
    }

    private static void AddLabeled(TableLayoutPanel table, string text, Control control)
    {
        table.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 12, 8)
        });
        control.Margin = new Padding(3, 8, 3, 8);
        table.Controls.Add(control);
    }
}