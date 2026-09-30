namespace SmsIrCheck;

internal sealed class MainForm : Form
{
    private readonly SmsIrClient _client = new();
    private readonly AppState _state;
    private readonly TextBox _keyBox = new() { UseSystemPasswordChar = true, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly CheckBox _showKey = new() { Text = "Show", AutoSize = true };
    private readonly Label _creditLabel = new() { Text = "Credit: —", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ComboBox _lineBox = new() { DropDownStyle = ComboBoxStyle.DropDown, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly ComboBox _methodBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListView _recipients = new()
    {
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        GridLines = true,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
        Dock = DockStyle.Fill,
    };
    private readonly TextBox _nameBox = new();
    private readonly TextBox _mobileBox = new();
    private readonly Label _selectedLabel = new() { AutoSize = true, Text = "Selected: none" };
    private readonly TextBox _messageBox = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 160, AcceptsReturn = true };
    private readonly TextBox _templateBox = new();
    private readonly TextBox _paramNameBox = new() { Text = "CODE" };
    private readonly TextBox _paramValueBox = new() { Text = "12345" };
    private readonly TextBox _paramName2Box = new();
    private readonly TextBox _paramValue2Box = new();
        private readonly Panel _bulkPanel = new() { Height = 230 };
        private readonly Panel _verifyPanel = new() { Height = 230, Visible = false };
    private readonly TextBox _messageIdBox = new() { Width = 140 };
    private readonly RichTextBox _log = new()
    {
        ReadOnly = true,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9.5f),
        BackColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        DetectUrls = false,
        HideSelection = false,
    };
    private readonly Label _verdict = new()
    {
        Dock = DockStyle.Bottom,
        Height = 28,
        TextAlign = ContentAlignment.MiddleLeft,
        Text = "Ready",
        Padding = new Padding(4, 0, 0, 0),
    };
    private readonly List<Button> _actionButtons = [];
    private readonly bool _keyFromEnvironment;
    private readonly FlowLayoutPanel _rightFlow = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(8, 0, 0, 0),
    };
    private bool _busy;

    public MainForm()
    {
        Text = $"SMS.ir connectivity check — {Environment.MachineName}";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1040, 760);
        Size = new Size(1180, 820);
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Font;

        _state = AppStateStore.Load();
        var envKey = AppStateStore.KeyFromEnvironment();
        _keyFromEnvironment = _state.ApiKey.Length == 0 && envKey != null;
        if (_keyFromEnvironment)
            _state.ApiKey = envKey!;

        BuildLayout();
        BindState();
        FormClosing += (_, _) => SaveState(showConfirmation: false);

        Log("Check connectivity proves DNS, TLS, and your API key without sending.", CheckTone.Info);
        Log("Select a recipient and send an SMS to prove sms.ir accepts a real message from this PC.", CheckTone.Info);
        Log($"Settings file: {AppStateStore.FilePath}", CheckTone.Info);
        if (_state.KeyWasUnreadable)
            Log("A saved API key could not be unlocked for this Windows user. Enter it again and click Save.", CheckTone.Warn);
        else if (_keyFromEnvironment)
            Log("API key filled from SMSIR_API_KEY. Click Save to store it for this Windows user.", CheckTone.Info);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _client.Dispose();
        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        Controls.Add(root);

        var logPanel = new Panel { Dock = DockStyle.Bottom, Height = 280, Padding = new Padding(0, 8, 0, 0) };
        var logHeader = new Panel { Dock = DockStyle.Top, Height = 30 };
        logHeader.Controls.Add(new Label
        {
            Text = "Diagnostic log",
            Dock = DockStyle.Left,
            Width = 220,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold),
        });
        var clearButton = new Button { Text = "Clear", Dock = DockStyle.Right, Width = 72 };
        var copyButton = new Button { Text = "Copy", Dock = DockStyle.Right, Width = 72 };
        clearButton.Click += (_, _) => _log.Clear();
        copyButton.Click += (_, _) =>
        {
            if (_log.TextLength > 0)
                Clipboard.SetText(_log.Text);
        };
        logHeader.Controls.Add(copyButton);
        logHeader.Controls.Add(clearButton);
        logPanel.Controls.Add(_log);
        logPanel.Controls.Add(_verdict);
        logPanel.Controls.Add(logHeader);

        var top = BuildTop();
        top.Dock = DockStyle.Top;
        top.Height = 168;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
        };
        split.Panel1.Controls.Add(BuildRecipients());
        split.Panel2.Controls.Add(BuildComposer());
        split.Resize += (_, _) => FitFlowChildren();
        _rightFlow.Resize += (_, _) => FitFlowChildren();

        root.Controls.Add(split);
        root.Controls.Add(logPanel);
        root.Controls.Add(top);

        Load += (_, _) =>
        {
            try
            {
                split.SplitterDistance = Math.Max(280, (int)(split.Width * 0.42));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // The window is not wide enough yet. The default distance is fine.
            }

            FitFlowChildren();
        };
    }

    private Control BuildTop()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 4,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        for (var i = 0; i < 4; i++)
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 1 ? 44 : 36));

        grid.Controls.Add(new Label { Text = "API key", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
        grid.Controls.Add(_keyBox, 1, 0);
        _showKey.CheckedChanged += (_, _) => _keyBox.UseSystemPasswordChar = !_showKey.Checked;
        grid.Controls.Add(_showKey, 2, 0);
        var save = MakeButton("Save", OnSave);
        grid.Controls.Add(save, 3, 0);
        grid.Controls.Add(_creditLabel, 4, 0);

        var hint = new Label
        {
            Text = "Same host a production app uses. A sandbox key exercises the path without a real SMS. A production key sends a real message and spends credit. The key is stored encrypted for this Windows user.",
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        grid.Controls.Add(hint, 0, 1);
        grid.SetColumnSpan(hint, 5);

        var check = MakeButton("Check connectivity", OnCheckConnectivity);
        var refresh = MakeButton("Refresh lines", OnRefreshLines);
        check.Width = 160;
        refresh.Width = 120;
        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        buttonRow.Controls.Add(check);
        buttonRow.Controls.Add(refresh);
        grid.Controls.Add(buttonRow, 0, 2);
        grid.SetColumnSpan(buttonRow, 5);

        grid.Controls.Add(new Label { Text = "Sender line", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 3);
        grid.Controls.Add(_lineBox, 1, 3);
        var methodLabel = new Label { Text = "Method", TextAlign = ContentAlignment.MiddleRight, AutoSize = true, Padding = new Padding(12, 8, 8, 0) };
        grid.Controls.Add(methodLabel, 2, 3);
        _methodBox.Items.AddRange(["Bulk — text from your line", "Verify — template"]);
        _methodBox.SelectedIndex = 0;
        _methodBox.Width = 220;
        _methodBox.SelectedIndexChanged += (_, _) => ApplyMethod();
        grid.Controls.Add(_methodBox, 3, 3);
        grid.SetColumnSpan(_methodBox, 2);
        return grid;
    }

    private Control BuildRecipients()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        layout.Controls.Add(new Label
        {
            Text = "Recipients",
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);

        _recipients.Columns.Add("Name", 180);
        _recipients.Columns.Add("Mobile", 140);
        _recipients.SelectedIndexChanged += (_, _) => UpdateSelectedLabel();
        _recipients.Resize += (_, _) =>
        {
            if (_recipients.Columns.Count == 2)
                _recipients.Columns[0].Width = Math.Max(100, _recipients.ClientSize.Width - 150);
        };
        layout.Controls.Add(_recipients, 0, 1);
        _selectedLabel.ForeColor = Color.DimGray;
        layout.Controls.Add(_selectedLabel, 0, 2);

        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        editor.Controls.Add(new Label { Text = "Name", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
        editor.Controls.Add(_nameBox, 1, 0);
        _nameBox.Dock = DockStyle.Fill;
        editor.Controls.Add(new Label { Text = "Mobile", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
        editor.Controls.Add(_mobileBox, 1, 1);
        _mobileBox.Dock = DockStyle.Fill;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        actions.Controls.Add(MakeButton("Add", OnAddRecipient));
        actions.Controls.Add(MakeButton("Remove", OnRemoveRecipient));
        editor.Controls.Add(actions, 1, 2);
        layout.Controls.Add(editor, 0, 3);
        return layout;
    }

    private Control BuildComposer()
    {
        var methodHost = new Panel { Height = 8 };

        _bulkPanel.Padding = new Padding(0, 4, 0, 0);
        var bulkLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        bulkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bulkLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        bulkLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        bulkLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        var messageHeader = new Label { Text = "Message", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        bulkLayout.Controls.Add(messageHeader, 0, 0);
        _messageBox.Dock = DockStyle.Fill;
        bulkLayout.Controls.Add(_messageBox, 0, 1);
        var sample = MakeButton("Sample text", (_, _) => _messageBox.Text = SampleMessage());
        sample.Width = 110;
        bulkLayout.Controls.Add(sample, 0, 2);
        _bulkPanel.Controls.Add(bulkLayout);
        _bulkPanel.Height = 230;

        var verifyLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        verifyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        verifyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void AddVerifyRow(int row, string label, Control control)
        {
            verifyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            verifyLayout.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
            control.Dock = DockStyle.Fill;
            verifyLayout.Controls.Add(control, 1, row);
        }

        AddVerifyRow(0, "Template id", _templateBox);
        AddVerifyRow(1, "Parameter", _paramNameBox);
        AddVerifyRow(2, "Value", _paramValueBox);
        AddVerifyRow(3, "Parameter 2", _paramName2Box);
        AddVerifyRow(4, "Value 2", _paramValue2Box);
        verifyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        var verifyNote = new Label
        {
            Text = "Parameter names must match the template, without #. Sandbox's built-in template uses CODE. Each value can be at most 25 characters.",
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
        };
        verifyLayout.Controls.Add(verifyNote, 0, 5);
        verifyLayout.SetColumnSpan(verifyNote, 2);
        _verifyPanel.Controls.Add(verifyLayout);
        _verifyPanel.Height = 250;

        var sendRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0),
        };
        var send = MakeButton("Send SMS", OnSend);
        send.Width = 120;
        send.Font = new Font(Font, FontStyle.Bold);
        var delivery = MakeButton("Check delivery", OnDelivery);
        delivery.Width = 130;
        sendRow.Controls.Add(send);
        sendRow.Controls.Add(delivery);
        sendRow.Controls.Add(new Label { Text = "Message id", AutoSize = true, Padding = new Padding(8, 8, 4, 0) });
        sendRow.Controls.Add(_messageIdBox);

        _rightFlow.Controls.Add(methodHost);
        _rightFlow.Controls.Add(_bulkPanel);
        _rightFlow.Controls.Add(_verifyPanel);
        _rightFlow.Controls.Add(sendRow);
        return _rightFlow;
    }

    private Button MakeButton(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(0, 3, 8, 3) };
        button.Click += onClick;
        _actionButtons.Add(button);
        return button;
    }

    private void BindState()
    {
        _keyBox.Text = _state.ApiKey;
        _lineBox.Text = _state.LineNumber;
        _messageBox.Text = string.IsNullOrWhiteSpace(_state.MessageText) ? SampleMessage() : _state.MessageText;
        _templateBox.Text = _state.TemplateId;
        _paramNameBox.Text = _state.ParameterName;
        _paramValueBox.Text = _state.ParameterValue;
        _paramName2Box.Text = _state.ParameterName2;
        _paramValue2Box.Text = _state.ParameterValue2;
        _methodBox.SelectedIndex = _state.SendMethod == "verify" ? 1 : 0;
        ReloadRecipients(null);
        ApplyMethod();
    }

    private void ApplyMethod()
    {
        var verify = _methodBox.SelectedIndex == 1;
        _bulkPanel.Visible = !verify;
        _verifyPanel.Visible = verify;
        _lineBox.Enabled = !verify;
        _rightFlow.PerformLayout();
    }

    private void FitFlowChildren()
    {
        var width = Math.Max(200, _rightFlow.ClientSize.Width - _rightFlow.Padding.Horizontal - 4);
        foreach (Control child in _rightFlow.Controls)
            child.Width = width;
    }

    private void ReloadRecipients(string? selectMobile)
    {
        _recipients.Items.Clear();
        foreach (var recipient in _state.Recipients)
        {
            var item = new ListViewItem(recipient.Name) { Tag = recipient };
            item.SubItems.Add(recipient.Mobile);
            _recipients.Items.Add(item);
            if (selectMobile != null && recipient.Mobile == selectMobile)
                item.Selected = true;
        }

        UpdateSelectedLabel();
    }

    private void UpdateSelectedLabel()
    {
        if (_recipients.SelectedItems.Count == 1 && _recipients.SelectedItems[0].Tag is Recipient recipient)
            _selectedLabel.Text = $"Selected: {recipient.Name} ({recipient.Mobile})";
        else
            _selectedLabel.Text = "Selected: none — type a mobile to send a one-off";
    }

    private void OnSave(object? sender, EventArgs e)
    {
        SaveState(showConfirmation: true);
    }

    private void SaveState(bool showConfirmation)
    {
        _state.ApiKey = _keyBox.Text.Trim();
        _state.LineNumber = _lineBox.Text.Trim();
        _state.SendMethod = _methodBox.SelectedIndex == 1 ? "verify" : "bulk";
        _state.MessageText = _messageBox.Text;
        _state.TemplateId = _templateBox.Text.Trim();
        _state.ParameterName = _paramNameBox.Text.Trim();
        _state.ParameterValue = _paramValueBox.Text;
        _state.ParameterName2 = _paramName2Box.Text.Trim();
        _state.ParameterValue2 = _paramValue2Box.Text;
        try
        {
            AppStateStore.Save(_state);
            if (showConfirmation)
                Log("Saved for this Windows user.", CheckTone.Ok);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            Log("Could not save settings: " + ex.Message, CheckTone.Bad);
        }
    }

    private void OnAddRecipient(object? sender, EventArgs e)
    {
        var name = _nameBox.Text.Trim();
        var mobile = PhoneNumber.Compact(_mobileBox.Text);
        if (name.Length == 0 || mobile.Length == 0)
        {
            Log("Enter a name and a mobile number.", CheckTone.Warn);
            return;
        }

        var existing = _state.Recipients.FirstOrDefault(recipient => recipient.Mobile == mobile);
        if (existing == null)
            _state.Recipients.Add(new Recipient { Name = name, Mobile = mobile });
        else
            existing.Name = name;

        _nameBox.Clear();
        _mobileBox.Clear();
        ReloadRecipients(mobile);
        SaveState(showConfirmation: false);
        Log(existing == null ? $"Added {name} ({mobile})." : $"Updated {name} ({mobile}).", CheckTone.Ok);
    }

    private void OnRemoveRecipient(object? sender, EventArgs e)
    {
        if (_recipients.SelectedItems.Count != 1 || _recipients.SelectedItems[0].Tag is not Recipient recipient)
        {
            Log("Select a recipient to remove.", CheckTone.Warn);
            return;
        }

        _state.Recipients.Remove(recipient);
        ReloadRecipients(null);
        SaveState(showConfirmation: false);
        Log($"Removed {recipient.Name}.", CheckTone.Info);
    }

    private async void OnCheckConnectivity(object? sender, EventArgs e)
    {
        if (_busy)
            return;

        try
        {
            SetBusy(true);
            Log("", CheckTone.Info);
            var report = await _clientProbe();
            ShowReport(report);
            ApplyLines(report.SenderLines);
            if (report.Credit is double credit)
                SetCredit(credit);
        }
        catch (Exception ex)
        {
            Log("Unexpected error: " + ex.Message, CheckTone.Bad);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private Task<CheckReport> _clientProbe() =>
        ConnectivityCheck.RunAsync(_client, _keyBox.Text.Trim(), CancellationToken.None);

    private async void OnRefreshLines(object? sender, EventArgs e)
    {
        if (_busy)
            return;

        var key = _keyBox.Text.Trim();
        if (key.Length == 0)
        {
            Log("Enter the API key first.", CheckTone.Warn);
            return;
        }

        try
        {
            SetBusy(true);
            Log("", CheckTone.Info);
            Log("GET https://api.sms.ir/v1/line", CheckTone.Info);
            var result = await _client.GetLinesAsync(key, CancellationToken.None);
            var report = new CheckReport();
            ConnectivityCheck.AppendCall(report, result);
            ShowReport(report);
            if (result.Status == 1)
                ApplyLines(SmsJson.ReadLines(result.DataJson));
        }
        catch (Exception ex)
        {
            Log("Unexpected error: " + ex.Message, CheckTone.Bad);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnSend(object? sender, EventArgs e)
    {
        if (_busy)
            return;

        var key = _keyBox.Text.Trim();
        if (key.Length == 0)
        {
            Log("Enter the API key first.", CheckTone.Warn);
            return;
        }

        var target = ResolveTarget();
        if (target == null)
        {
            Log("Select a recipient, or type a mobile number.", CheckTone.Warn);
            return;
        }

        var mobile = PhoneNumber.Compact(target.Mobile);
        if (!PhoneNumber.LooksIranian(mobile))
        {
            var proceed = MessageBox.Show(
                this,
                $"{mobile} does not look like an Iranian mobile number. Send anyway?",
                "Mobile number",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (proceed != DialogResult.Yes)
                return;
        }

        var verify = _methodBox.SelectedIndex == 1;
        string requestLog;
        long lineNumber = 0;
        int templateId = 0;
        List<(string Name, string Value)>? parameters = null;
        var message = _messageBox.Text.Trim();

        if (!verify)
        {
            if (!long.TryParse(_lineBox.Text.Trim(), out lineNumber) || lineNumber <= 0)
            {
                Log("Choose or type a sender line. Use Refresh lines after the key is accepted.", CheckTone.Warn);
                return;
            }

            if (message.Length == 0)
            {
                Log("The message is empty.", CheckTone.Warn);
                return;
            }

            requestLog = SmsIrClient.DescribeRequest("POST", "/v1/send/bulk", SmsIrClient.BulkBody(lineNumber, message, mobile));
        }
        else
        {
            if (!int.TryParse(_templateBox.Text.Trim(), out templateId) || templateId <= 0)
            {
                Log("Template id must be a positive number.", CheckTone.Warn);
                return;
            }

            parameters = ReadParameters();
            if (parameters.Count == 0)
            {
                Log("Enter at least one parameter name and value.", CheckTone.Warn);
                return;
            }

            if (parameters.Any(parameter => parameter.Value.Length > 25))
            {
                Log("sms.ir rejects parameter values longer than 25 characters (status 114).", CheckTone.Warn);
                return;
            }

            requestLog = SmsIrClient.DescribeRequest("POST", "/v1/send/verify", SmsIrClient.VerifyBody(mobile, templateId, parameters));
        }

        var preview = verify
            ? $"Template {templateId}"
            : message;
        var confirm = MessageBox.Show(
            this,
            $"Send an SMS to {target.Name} ({mobile})?\n\n{preview}\n\nA production key sends a real message and spends credit.",
            "Send SMS",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes)
            return;

        try
        {
            SetBusy(true);
            SaveState(showConfirmation: false);
            Log("", CheckTone.Info);
            Log($"Sending to {target.Name} ({mobile}) from {Environment.MachineName}", CheckTone.Info);
            Log(requestLog, CheckTone.Body);

            var result = verify
                ? await _client.SendVerifyAsync(key, mobile, templateId, parameters!, CancellationToken.None)
                : await _client.SendBulkAsync(key, lineNumber, message, mobile, CancellationToken.None);
            var report = new CheckReport();
            ConnectivityCheck.AppendCall(report, result);
            ShowReport(report);

            var messageId = SmsJson.FirstMessageId(result.DataJson, bulkArray: !verify);
            if (messageId is long id)
                _messageIdBox.Text = id.ToString();

            var packId = SmsJson.TextProperty(result.DataJson, "packId");
            var cost = SmsJson.TextProperty(result.DataJson, "cost");
            if (packId != null)
                Log("Pack id  " + packId, CheckTone.Info);
            if (cost != null)
                Log("Cost  " + cost, CheckTone.Info);

            string verdict;
            if (!result.TransportSucceeded)
                verdict = "FAIL: the send never reached sms.ir.";
            else if (result.Status == 1 && messageId is > 0)
                verdict = "PASS: sms.ir accepted the message. This PC can use the API.";
            else if (result.Status == 1 && messageId == 0 && !verify)
                verdict = "PASS (API): the call was accepted. Message id 0 means this number is blacklisted, so nothing was queued.";
            else if (result.Status == 1 && messageId is null)
                verdict = "PASS (API): the call was accepted, but the number or text was rejected (message id is null).";
            else if (result.Status is int status)
                verdict = $"REACHED: sms.ir answered status {status} ({StatusText.Describe(status)}). The network path is open.";
            else
                verdict = $"REACHED: HTTP {result.HttpStatus}. See the body above.";

            Log(verdict, verdict.StartsWith("FAIL", StringComparison.Ordinal) ? CheckTone.Bad : verdict.StartsWith("PASS", StringComparison.Ordinal) ? CheckTone.Ok : CheckTone.Warn);
            SetVerdict(verdict, verdict.StartsWith("FAIL", StringComparison.Ordinal) ? CheckTone.Bad : CheckTone.Ok);

            var credit = await _client.GetCreditAsync(key, CancellationToken.None);
            if (credit.Status == 1
                && double.TryParse(credit.DataJson, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var amount))
            {
                SetCredit(amount);
                Log($"Credit after send  {amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}", CheckTone.Info);
            }

            if (messageId is > 0)
                await FetchDeliveryAsync(key, messageId.Value);
        }
        catch (Exception ex)
        {
            Log("Unexpected error: " + ex.Message, CheckTone.Bad);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnDelivery(object? sender, EventArgs e)
    {
        if (_busy)
            return;

        var key = _keyBox.Text.Trim();
        if (key.Length == 0)
        {
            Log("Enter the API key first.", CheckTone.Warn);
            return;
        }

        if (!long.TryParse(_messageIdBox.Text.Trim(), out var messageId) || messageId <= 0)
        {
            Log("Enter a message id greater than zero. Zero means the number was blacklisted and there is no delivery report.", CheckTone.Warn);
            return;
        }

        try
        {
            SetBusy(true);
            await FetchDeliveryAsync(key, messageId);
        }
        catch (Exception ex)
        {
            Log("Unexpected error: " + ex.Message, CheckTone.Bad);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task FetchDeliveryAsync(string key, long messageId)
    {
        Log("", CheckTone.Info);
        Log($"GET https://api.sms.ir/v1/send/{messageId}", CheckTone.Info);
        var result = await _client.GetMessageAsync(key, messageId, CancellationToken.None);
        var report = new CheckReport();
        ConnectivityCheck.AppendCall(report, result);
        ShowReport(report);
        if (result.Status == 1)
        {
            var state = SmsJson.IntProperty(result.DataJson, "deliveryState");
            var when = SmsJson.LongProperty(result.DataJson, "deliveryDateTime");
            var description = StatusText.DescribeDelivery(state);
            if (when is > 0)
            {
                var local = DateTimeOffset.FromUnixTimeSeconds(when.Value).ToLocalTime();
                description += $" at {local:yyyy-MM-dd HH:mm:ss}";
            }

            Log("Delivery  " + description, state == 1 ? CheckTone.Ok : CheckTone.Warn);
        }
    }

    private List<(string Name, string Value)> ReadParameters()
    {
        var parameters = new List<(string Name, string Value)>();
        AddParameter(parameters, _paramNameBox.Text, _paramValueBox.Text);
        AddParameter(parameters, _paramName2Box.Text, _paramValue2Box.Text);
        return parameters;
    }

    private static void AddParameter(List<(string Name, string Value)> parameters, string name, string value)
    {
        name = name.Trim();
        if (name.Length == 0)
            return;
        parameters.Add((name, value.Trim()));
    }

    private Recipient? ResolveTarget()
    {
        if (_recipients.SelectedItems.Count == 1 && _recipients.SelectedItems[0].Tag is Recipient selected)
            return selected;

        var mobile = _mobileBox.Text.Trim();
        if (mobile.Length == 0)
            return null;

        var name = _nameBox.Text.Trim();
        return new Recipient { Name = name.Length == 0 ? "One-off" : name, Mobile = mobile };
    }

    private void ApplyLines(IReadOnlyList<long> lines)
    {
        if (lines.Count == 0)
            return;

        var current = _lineBox.Text.Trim();
        _lineBox.Items.Clear();
        foreach (var line in lines)
            _lineBox.Items.Add(line.ToString());

        if (current.Length > 0 && lines.Any(line => line.ToString() == current))
            _lineBox.Text = current;
        else
            _lineBox.SelectedIndex = 0;

        _state.LineNumber = _lineBox.Text.Trim();
    }

    private void ShowReport(CheckReport report)
    {
        foreach (var line in report.Lines)
            Log(line.Text, line.Tone);
        if (report.Verdict.Length > 0)
            SetVerdict(report.Verdict, report.AuthOk || report.NetworkOk && report.ExitCode == 0 ? CheckTone.Ok : report.ExitCode == 2 ? CheckTone.Bad : CheckTone.Warn);
    }

    private void SetCredit(double credit)
    {
        _creditLabel.Text = "Credit: " + credit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _creditLabel.ForeColor = Color.FromArgb(20, 120, 60);
    }

    private void SetVerdict(string text, CheckTone tone)
    {
        _verdict.Text = text;
        _verdict.ForeColor = ToneColor(tone);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UseWaitCursor = busy;
        foreach (var button in _actionButtons)
            button.Enabled = !busy;
        _verdict.Text = busy ? "Working…" : _verdict.Text;
    }

    private void Log(string text, CheckTone tone)
    {
        if (IsDisposed)
            return;
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(text, tone));
            return;
        }

        _log.SelectionStart = _log.TextLength;
        _log.SelectionColor = ToneColor(tone);
        _log.AppendText(text + Environment.NewLine);
        _log.ScrollToCaret();
    }

    private static Color ToneColor(CheckTone tone) => tone switch
    {
        CheckTone.Ok => Color.FromArgb(20, 120, 60),
        CheckTone.Bad => Color.Firebrick,
        CheckTone.Warn => Color.DarkOrange,
        CheckTone.Body => Color.FromArgb(50, 50, 50),
        _ => Color.FromArgb(20, 70, 130),
    };

    private static string SampleMessage() =>
        $"SMS.ir connectivity check. Host: {Environment.MachineName}. Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
}
