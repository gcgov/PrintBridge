using PrintBridge.Localization;
using PrintBridge.Printing;
using PrintBridge.Settings;

namespace PrintBridge.App;

/// <summary>
/// Add/edit dialog for one named queue: the name web apps use, and the Windows
/// printer it maps to. Built in code to match the rest of the UI.
/// </summary>
public sealed class QueueEditDialog : Form
{
    private readonly TextBox _nameInput = new() { Width = 260, PlaceholderText = "labels" };
    private readonly ComboBox _printerCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
    private readonly Label _printerStatusLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Text = string.Empty };

    public QueueEditDialog(string? queueName, string? printerName)
    {
        Text = string.IsNullOrEmpty(queueName) ? Strings.QueueDialogAddTitle : Strings.QueueDialogEditTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(12);

        QueueName = queueName ?? string.Empty;
        PrinterName = printerName ?? string.Empty;
        _nameInput.Text = QueueName;

        BuildLayout();
        LoadPrinters(printerName);
    }

    /// <summary>Normalized queue name, valid once the dialog returns <see cref="DialogResult.OK"/>.</summary>
    public string QueueName { get; private set; }

    public string PrinterName { get; private set; }

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top };
        layout.Controls.Add(new Label { Text = Strings.QueueDialogNameLabel, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_nameInput, 1, 0);
        layout.Controls.Add(new Label { Text = Strings.QueueDialogPrinterLabel, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(_printerCombo, 1, 1);
        layout.Controls.Add(_printerStatusLabel, 1, 2);
        var hint = new Label
        {
            Text = Strings.QueueDialogHint,
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Left,
        };
        layout.Controls.Add(hint, 1, 3);

        var okButton = new Button { Text = Strings.QueueDialogOk, AutoSize = true };
        var cancelButton = new Button { Text = Strings.SettingsCancel, AutoSize = true, DialogResult = DialogResult.Cancel };
        okButton.Click += (_, _) => Confirm();
        AcceptButton = okButton;
        CancelButton = cancelButton;

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(4),
        };
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        Controls.Add(buttonPanel);
        Controls.Add(layout);

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(460, 0);
    }

    private void LoadPrinters(string? selected)
    {
        List<string> printers;
        try
        {
            printers = PrintService.ListInstalledPrinters();
        }
        catch (Exception ex)
        {
            printers = [];
            _printerStatusLabel.Text = Strings.QueueDialogPrinterListFailed(ex.Message);
        }

        // A queue may point at a printer that has since been removed; keep it selectable
        // so editing the name does not silently repoint the queue.
        if (!string.IsNullOrEmpty(selected) &&
            !printers.Any(p => string.Equals(p, selected, StringComparison.OrdinalIgnoreCase)))
        {
            printers.Insert(0, selected);
            _printerStatusLabel.Text = Strings.QueueDialogPrinterMissing(selected);
        }

        _printerCombo.Items.AddRange([.. printers.Cast<object>()]);
        if (!string.IsNullOrEmpty(selected))
        {
            _printerCombo.SelectedItem = printers.FirstOrDefault(p =>
                string.Equals(p, selected, StringComparison.OrdinalIgnoreCase));
        }

        if (_printerCombo.SelectedIndex < 0 && _printerCombo.Items.Count > 0)
        {
            _printerCombo.SelectedIndex = 0;
        }

        if (printers.Count == 0 && string.IsNullOrEmpty(_printerStatusLabel.Text))
        {
            _printerStatusLabel.Text = Strings.QueueDialogNoPrinters;
        }
    }

    private void Confirm()
    {
        if (!AppSettings.TryNormalizeQueueName(_nameInput.Text, out var normalized))
        {
            MessageBox.Show(this,
                Strings.QueueDialogInvalidNameMessage,
                Strings.QueueDialogInvalidNameTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_printerCombo.SelectedItem is not string printer || string.IsNullOrWhiteSpace(printer))
        {
            MessageBox.Show(this, Strings.QueueDialogNoPrinterMessage,
                Strings.QueueDialogNoPrinterTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        QueueName = normalized;
        PrinterName = printer;
        DialogResult = DialogResult.OK;
        Close();
    }
}
