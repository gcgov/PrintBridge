using PrintBridge.Hosting;
using PrintBridge.Localization;
using PrintBridge.Settings;

namespace PrintBridge.App;

/// <summary>
/// The configuration window: the named print queues, the local server (port + allowed
/// browser origins), the run-at-login toggle and the UI language.
/// Built in code rather than with the WinForms designer to keep it reviewable.
/// Every text comes from <see cref="Strings"/> when the window opens, so a language
/// change applies the next time the window opens.
/// </summary>
public sealed class SettingsForm : Form
{
    private sealed record LanguageItem(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly SettingsStore _settingsStore;
    private readonly WebHostRunner _webHostRunner;
    private readonly SynchronizationContext _syncContext;

    private readonly List<PrintQueueDefinition> _queues = new();
    private string? _defaultQueue;

    private readonly ListView _queuesList = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        Width = 460,
        Height = 130,
    };

    private readonly Button _addQueueButton = new() { Text = Strings.SettingsAddQueue, AutoSize = true };
    private readonly Button _editQueueButton = new() { Text = Strings.SettingsEditQueue, AutoSize = true };
    private readonly Button _removeQueueButton = new() { Text = Strings.SettingsRemoveQueue, AutoSize = true };
    private readonly Button _defaultQueueButton = new() { Text = Strings.SettingsSetDefaultQueue, AutoSize = true };

    private readonly NumericUpDown _portInput = new() { Minimum = 1024, Maximum = 65535, Width = 100 };
    private readonly ListBox _originsList = new() { Width = 420, Height = 90 };
    private readonly TextBox _originInput = new() { Width = 300, PlaceholderText = "https://apps.example.gov" };
    private readonly Button _addOriginButton = new() { Text = Strings.SettingsAddOrigin, AutoSize = true };
    private readonly Button _removeOriginButton = new() { Text = Strings.SettingsRemoveOrigin, AutoSize = true };
    private readonly Label _listenerStatusLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Text = string.Empty };

    private readonly CheckBox _runAtLoginCheck = new() { Text = Strings.SettingsRunAtLogin, AutoSize = true };
    private readonly ComboBox _languageCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };

    public SettingsForm(SettingsStore settingsStore, WebHostRunner webHostRunner)
    {
        _settingsStore = settingsStore;
        _webHostRunner = webHostRunner;
        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        Text = Strings.SettingsTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(12);

        BuildLayout();
        LoadFromSettings(_settingsStore.Current);

        _addQueueButton.Click += (_, _) => AddQueue();
        _editQueueButton.Click += (_, _) => EditSelectedQueue();
        _queuesList.DoubleClick += (_, _) => EditSelectedQueue();
        _removeQueueButton.Click += (_, _) => RemoveSelectedQueue();
        _defaultQueueButton.Click += (_, _) => MakeSelectedQueueDefault();
        _queuesList.SelectedIndexChanged += (_, _) => UpdateQueueButtons();

        _addOriginButton.Click += (_, _) => AddOrigin();
        _originInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = e.SuppressKeyPress = true;
                AddOrigin();
            }
        };
        _removeOriginButton.Click += (_, _) =>
        {
            if (_originsList.SelectedItem is not null)
            {
                _originsList.Items.Remove(_originsList.SelectedItem);
            }
        };

        _webHostRunner.StatusChanged += OnListenerStatusChanged;
        FormClosed += (_, _) => _webHostRunner.StatusChanged -= OnListenerStatusChanged;
        UpdateListenerStatusLabel();
    }

    private void BuildLayout()
    {
        _queuesList.Columns.Add(Strings.SettingsQueueColumn, 150);
        _queuesList.Columns.Add(Strings.SettingsPrinterColumn, 230);
        _queuesList.Columns.Add(Strings.SettingsDefaultColumn, 80);

        var queuesGroup = new GroupBox { Text = Strings.SettingsQueuesGroup, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(10) };
        var queuesLayout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        var queuesHint = new Label
        {
            Text = Strings.SettingsQueuesHint,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };
        queuesLayout.Controls.Add(queuesHint, 0, 0);
        queuesLayout.Controls.Add(_queuesList, 0, 1);
        var queueButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        queueButtons.Controls.Add(_addQueueButton);
        queueButtons.Controls.Add(_editQueueButton);
        queueButtons.Controls.Add(_removeQueueButton);
        queueButtons.Controls.Add(_defaultQueueButton);
        queuesLayout.Controls.Add(queueButtons, 0, 2);
        queuesGroup.Controls.Add(queuesLayout);

        var serverGroup = new GroupBox { Text = Strings.SettingsServerGroup, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(10) };
        var serverLayout = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Fill };
        serverLayout.Controls.Add(new Label { Text = Strings.SettingsPortLabel, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        serverLayout.Controls.Add(_portInput, 1, 0);
        serverLayout.Controls.Add(_listenerStatusLabel, 2, 0);
        var originsLabel = new Label
        {
            Text = Strings.SettingsOriginsLabel,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };
        serverLayout.Controls.Add(originsLabel, 0, 1);
        serverLayout.SetColumnSpan(originsLabel, 3);
        serverLayout.Controls.Add(_originsList, 0, 2);
        serverLayout.SetColumnSpan(_originsList, 3);
        var originAddPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        originAddPanel.Controls.Add(_originInput);
        originAddPanel.Controls.Add(_addOriginButton);
        originAddPanel.Controls.Add(_removeOriginButton);
        serverLayout.Controls.Add(originAddPanel, 0, 3);
        serverLayout.SetColumnSpan(originAddPanel, 3);
        serverGroup.Controls.Add(serverLayout);

        var startupPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(4) };
        startupPanel.Controls.Add(_runAtLoginCheck);
        startupPanel.SetFlowBreak(_runAtLoginCheck, true);
        startupPanel.Controls.Add(new Label { Text = Strings.SettingsLanguageLabel, AutoSize = true, Margin = new Padding(3, 6, 0, 0) });
        startupPanel.Controls.Add(_languageCombo);

        var saveButton = new Button { Text = Strings.SettingsSave, AutoSize = true, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = Strings.SettingsCancel, AutoSize = true, DialogResult = DialogResult.Cancel };
        saveButton.Click += (_, _) => SaveAndClose();
        cancelButton.Click += (_, _) => Close();
        AcceptButton = saveButton;
        CancelButton = cancelButton;
        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(4),
        };
        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);

        // Docked top-to-bottom; add in reverse so the queues group ends up on top.
        Controls.Add(buttonPanel);
        Controls.Add(startupPanel);
        Controls.Add(serverGroup);
        Controls.Add(queuesGroup);

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(540, 0);
    }

    private void LoadFromSettings(AppSettings settings)
    {
        _queues.Clear();
        _queues.AddRange(settings.Queues.Select(q => q.Clone()));
        _defaultQueue = settings.DefaultQueue;
        RefreshQueueList();

        _portInput.Value = Math.Clamp(settings.Port, (int)_portInput.Minimum, (int)_portInput.Maximum);
        foreach (var origin in settings.AllowedOrigins)
        {
            _originsList.Items.Add(origin);
        }

        _runAtLoginCheck.Checked = settings.RunAtLogin;

        _languageCombo.Items.Add(new LanguageItem(UiLanguage.Automatic, Strings.SettingsLanguageAutomatic));
        foreach (var language in UiLanguage.Supported)
        {
            _languageCombo.Items.Add(new LanguageItem(language.Name, language.NativeName));
        }

        var configured = UiLanguage.FindSupported(settings.Language ?? string.Empty)?.Name ?? UiLanguage.Automatic;
        _languageCombo.SelectedItem = _languageCombo.Items.Cast<LanguageItem>().First(l => l.Value == configured);
    }

    private void RefreshQueueList()
    {
        var selectedName = SelectedQueue()?.Name;
        _queuesList.BeginUpdate();
        _queuesList.Items.Clear();
        foreach (var queue in _queues)
        {
            var isDefault = string.Equals(queue.Name, _defaultQueue, StringComparison.OrdinalIgnoreCase);
            var item = new ListViewItem([queue.Name, queue.PrinterName, isDefault ? Strings.SettingsDefaultYes : string.Empty])
            {
                Tag = queue,
                Selected = string.Equals(queue.Name, selectedName, StringComparison.OrdinalIgnoreCase),
            };
            _queuesList.Items.Add(item);
        }

        _queuesList.EndUpdate();
        UpdateQueueButtons();
    }

    private PrintQueueDefinition? SelectedQueue() =>
        _queuesList.SelectedItems.Count > 0 ? _queuesList.SelectedItems[0].Tag as PrintQueueDefinition : null;

    private void UpdateQueueButtons()
    {
        var hasSelection = _queuesList.SelectedItems.Count > 0;
        _editQueueButton.Enabled = hasSelection;
        _removeQueueButton.Enabled = hasSelection;
        _defaultQueueButton.Enabled = hasSelection;
    }

    private void AddQueue()
    {
        using var dialog = new QueueEditDialog(null, null);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (NameTaken(dialog.QueueName, existing: null))
        {
            return;
        }

        _queues.Add(new PrintQueueDefinition { Name = dialog.QueueName, PrinterName = dialog.PrinterName });

        // The first queue added is the obvious default.
        _defaultQueue ??= dialog.QueueName;
        RefreshQueueList();
    }

    private void EditSelectedQueue()
    {
        if (SelectedQueue() is not { } queue)
        {
            return;
        }

        using var dialog = new QueueEditDialog(queue.Name, queue.PrinterName);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (NameTaken(dialog.QueueName, existing: queue))
        {
            return;
        }

        if (string.Equals(_defaultQueue, queue.Name, StringComparison.OrdinalIgnoreCase))
        {
            _defaultQueue = dialog.QueueName;
        }

        queue.Name = dialog.QueueName;
        queue.PrinterName = dialog.PrinterName;
        RefreshQueueList();
    }

    private bool NameTaken(string name, PrintQueueDefinition? existing)
    {
        var clash = _queues.Any(q => !ReferenceEquals(q, existing) &&
            string.Equals(q.Name, name, StringComparison.OrdinalIgnoreCase));
        if (clash)
        {
            MessageBox.Show(this, Strings.SettingsDuplicateQueueMessage(name),
                Strings.SettingsDuplicateQueueTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        return clash;
    }

    private void RemoveSelectedQueue()
    {
        if (SelectedQueue() is not { } queue)
        {
            return;
        }

        _queues.Remove(queue);
        if (string.Equals(_defaultQueue, queue.Name, StringComparison.OrdinalIgnoreCase))
        {
            _defaultQueue = _queues.FirstOrDefault()?.Name;
        }

        RefreshQueueList();
    }

    private void MakeSelectedQueueDefault()
    {
        if (SelectedQueue() is { } queue)
        {
            _defaultQueue = queue.Name;
            RefreshQueueList();
        }
    }

    private void AddOrigin()
    {
        if (!AppSettings.TryNormalizeOrigin(_originInput.Text, out var normalized))
        {
            MessageBox.Show(this,
                Strings.SettingsInvalidOriginMessage,
                Strings.SettingsInvalidOriginTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!_originsList.Items.Contains(normalized))
        {
            _originsList.Items.Add(normalized);
        }

        _originInput.Clear();
    }

    private void SaveAndClose()
    {
        var settings = _settingsStore.Current.Clone();
        settings.Queues = _queues.Select(q => q.Clone()).ToList();
        settings.DefaultQueue = _defaultQueue;
        settings.Port = (int)_portInput.Value;
        settings.AllowedOrigins = _originsList.Items.Cast<string>().ToList();
        settings.RunAtLogin = _runAtLoginCheck.Checked;
        settings.Language = (_languageCombo.SelectedItem as LanguageItem)?.Value ?? UiLanguage.Automatic;

        _settingsStore.Save(settings);
        Close();
    }

    private void OnListenerStatusChanged()
    {
        _syncContext.Post(_ => UpdateListenerStatusLabel(), null);
    }

    private void UpdateListenerStatusLabel()
    {
        if (_webHostRunner.IsRunning)
        {
            _listenerStatusLabel.Text = Strings.SettingsListening(_settingsStore.Current.Port);
            _listenerStatusLabel.ForeColor = Color.DarkGreen;
        }
        else
        {
            _listenerStatusLabel.Text = _webHostRunner.LastError?.ToString() ?? Strings.NotListening;
            _listenerStatusLabel.ForeColor = Color.Firebrick;
        }
    }
}
