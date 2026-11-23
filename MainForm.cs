using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;

namespace Mertviy;

internal sealed class ServiceState
{
    public string? StrategyName { get; set; }
    public DateTimeOffset? LastChecked { get; set; }
}

internal sealed class AppState
{
    public bool EnableQuic { get; set; }
    public Dictionary<string, ServiceState> Services { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class MainForm : Form
{
    private const string ResourcePrefix = "Mertviy.Engine.";
    private readonly ProfileCatalog _catalog;
    private readonly GitHubReleaseUpdater _updater;
    private readonly string _appVersion;
    private readonly string _engineVersion;
    private readonly CancellationTokenSource _formLifetime = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _log = new();
    private readonly Button _autoButton = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly CheckBox _quicCheck = new();
    private readonly Label _status = new();
    private readonly Label _updateStatus = new();
    private readonly Button _checkUpdateButton = new();
    private readonly Button _launchUpdateButton = new();
    private string? _downloadedUpdatePath;
    private bool _updateCheckRunning;
    private readonly string _appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mertviy");
    private readonly string _engineDir;
    private readonly string _statePath;
    private AppState _state = new();
    private Process? _engine;
    private bool _busy;

    public MainForm(ProfileCatalog catalog, GitHubReleaseUpdater updater, string appVersion, string engineVersion)
    {
        _catalog = catalog;
        _updater = updater;
        _appVersion = appVersion;
        _engineVersion = engineVersion;
        _engineDir = Path.Combine(_appDir, "engine");
        _statePath = Path.Combine(_appDir, "settings.json");
        Text = "Mertviy";
        MinimumSize = new Size(860, 620);
        Size = new Size(1050, 740);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(244, 246, 248);
        Font = new Font("Segoe UI", 9.5f);
        BuildUi();
        LoadState();
        RefreshGrid();
        FormClosing += (_, _) =>
        {
            _formLifetime.Cancel();
            StopEngine();
            _updater.Dispose();
            _formLifetime.Dispose();
        };
        Shown += async (_, _) => await OnShownAsync();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        Controls.Add(root);

        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Margin = Padding.Empty };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 54));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
        var title = new Label { Text = $"Mertviy  {_appVersion}", Font = new Font("Segoe UI Semibold", 19), ForeColor = Color.FromArgb(22, 34, 48), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
        heading.Controls.Add(title, 0, 0);
        var engineLabel = new Label { Text = $"Движок: {_engineVersion}", ForeColor = Color.FromArgb(91, 105, 120), AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0) };
        heading.Controls.Add(engineLabel, 1, 0);
        heading.SetColumnSpan(engineLabel, 3);
        _updateStatus.Text = "Проверка обновлений при запуске";
        _updateStatus.ForeColor = Color.FromArgb(91, 105, 120);
        _updateStatus.AutoEllipsis = true;
        _updateStatus.Dock = DockStyle.Fill;
        _updateStatus.TextAlign = ContentAlignment.MiddleLeft;
        _updateStatus.Margin = new Padding(0);
        heading.Controls.Add(_updateStatus, 0, 1);
        heading.SetColumnSpan(_updateStatus, 2);
        _checkUpdateButton.Text = "Проверить";
        StyleButton(_checkUpdateButton, false);
        _checkUpdateButton.Click += async (_, _) => await CheckForUpdatesAsync(true, _formLifetime.Token);
        heading.Controls.Add(_checkUpdateButton, 2, 1);
        _launchUpdateButton.Text = "Запустить обновление";
        StyleButton(_launchUpdateButton, false);
        _launchUpdateButton.Visible = false;
        _launchUpdateButton.Click += (_, _) => LaunchDownloadedUpdate();
        heading.Controls.Add(_launchUpdateButton, 3, 1);
        root.Controls.Add(heading, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.GridColor = Color.FromArgb(232, 236, 240);
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(235, 240, 244), ForeColor = Color.FromArgb(58, 73, 88), Font = new Font("Segoe UI Semibold", 9), Padding = new Padding(8) };
        _grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.White, ForeColor = Color.FromArgb(34, 47, 61), SelectionBackColor = Color.FromArgb(220, 236, 240), SelectionForeColor = Color.FromArgb(23, 47, 56), Padding = new Padding(8), WrapMode = DataGridViewTriState.True };
        _grid.RowTemplate.Height = 58;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "service", HeaderText = "Сервис", FillWeight = 20, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", HeaderText = "Состояние", FillWeight = 27, ReadOnly = true });
        var strategyColumn = new DataGridViewComboBoxColumn { Name = "strategy", HeaderText = "Профиль", FillWeight = 31, FlatStyle = FlatStyle.Flat };
        foreach (var strategy in _catalog.Strategies) strategyColumn.Items.Add(strategy.Name);
        _grid.Columns.Add(strategyColumn);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "checked", HeaderText = "Проверено", FillWeight = 22, ReadOnly = true });
        _grid.CellValueChanged += (_, e) => { if (e.RowIndex >= 0) SaveGridSelection(e.RowIndex); };
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.DataError += (_, e) => e.ThrowException = false;
        root.Controls.Add(_grid, 0, 1);

        var controls = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Padding = new Padding(0, 12, 0, 8) };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _autoButton.Text = "Автоподбор и запуск";
        StyleButton(_autoButton, true);
        _autoButton.Click += async (_, _) => await AutoSelectAndStart();
        controls.Controls.Add(_autoButton, 0, 0);
        _startButton.Text = "Запустить профиль";
        StyleButton(_startButton, false);
        _startButton.Click += (_, _) => StartSavedProfiles();
        controls.Controls.Add(_startButton, 1, 0);
        _stopButton.Text = "Остановить";
        StyleButton(_stopButton, false);
        _stopButton.Click += (_, _) => StopEngine();
        _stopButton.Enabled = false;
        controls.Controls.Add(_stopButton, 2, 0);
        _quicCheck.Text = "QUIC";
        _quicCheck.Dock = DockStyle.Fill;
        _quicCheck.TextAlign = ContentAlignment.MiddleCenter;
        _quicCheck.ForeColor = Color.FromArgb(53, 68, 83);
        _quicCheck.CheckedChanged += (_, _) => { _state.EnableQuic = _quicCheck.Checked; SaveState(); };
        controls.Controls.Add(_quicCheck, 3, 0);
        _status.Text = "Готов";
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleRight;
        _status.ForeColor = Color.FromArgb(77, 94, 108);
        controls.Controls.Add(_status, 4, 0);
        root.Controls.Add(controls, 0, 2);

        var note = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Проверка использует HTTPS-пробы; результат зависит от сети и может отличаться в браузере или приложении. QUIC выключен по умолчанию.",
            ForeColor = Color.FromArgb(91, 105, 120),
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(note, 0, 3);
        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Color.FromArgb(250, 251, 252);
        _log.ForeColor = Color.FromArgb(50, 67, 82);
        _log.Font = new Font("Consolas", 9.25f);
        root.Controls.Add(_log, 0, 4);
    }

    private static void StyleButton(Button button, bool primary)
    {
        button.Dock = DockStyle.Fill;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 221);
        button.BackColor = primary ? Color.FromArgb(25, 112, 116) : Color.White;
        button.ForeColor = primary ? Color.White : Color.FromArgb(39, 57, 72);
        button.Font = new Font("Segoe UI Semibold", 9.5f);
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(0, 0, 10, 0);
    }

    private void LoadState()
    {
        try
        {
            if (File.Exists(_statePath)) _state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(_statePath)) ?? new AppState();
        }
        catch { _state = new AppState(); }
        _quicCheck.Checked = _state.EnableQuic;
    }

    private void SaveState()
    {
        try
        {
            Directory.CreateDirectory(_appDir);
            File.WriteAllText(_statePath, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Append($"Не удалось сохранить настройки: {ex.Message}"); }
    }

    private void RefreshGrid()
    {
        _grid.Rows.Clear();
        foreach (var service in _catalog.Services)
        {
            _state.Services.TryGetValue(service.Name, out var saved);
            var status = saved?.StrategyName is null ? "Ожидает подбора" : "Профиль сохранён";
            var timestamp = saved?.LastChecked?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";
            var selected = _catalog.Strategies.Any(x => x.Name == saved?.StrategyName) ? saved!.StrategyName : _catalog.Strategies[0].Name;
            var index = _grid.Rows.Add(service.Name, status, selected, timestamp);
            _grid.Rows[index].Tag = service;
        }
    }

    private void SaveGridSelection(int rowIndex)
    {
        if (_grid.Rows[rowIndex].Tag is not ServiceTarget service) return;
        var selected = Convert.ToString(_grid.Rows[rowIndex].Cells["strategy"].Value);
        if (string.IsNullOrWhiteSpace(selected)) return;
        if (!_state.Services.TryGetValue(service.Name, out var state)) _state.Services[service.Name] = state = new ServiceState();
        state.StrategyName = selected;
        SaveState();
    }

    private async Task OnShownAsync()
    {
        await CheckForUpdatesAsync(false, _formLifetime.Token);
        if (!_formLifetime.IsCancellationRequested) await AutoSelectAndStart();
    }

    private async Task CheckForUpdatesAsync(bool userInitiated, CancellationToken cancellationToken)
    {
        if (_updateCheckRunning || IsDisposed) return;
        _updateCheckRunning = true;
        _checkUpdateButton.Enabled = false;
        _launchUpdateButton.Visible = false;
        _updateStatus.Text = "Проверяю стабильный релиз…";
        _updateStatus.ForeColor = Color.FromArgb(91, 105, 120);
        try
        {
            var result = await _updater.CheckLatestAsync(cancellationToken);
            _updateStatus.Text = result.StatusMessage;
            if (!result.IsUpdateAvailable)
            {
                _updateStatus.ForeColor = result.LatestVersion is null ? Color.FromArgb(176, 111, 31) : Color.FromArgb(38, 126, 92);
                return;
            }

            _updateStatus.ForeColor = Color.FromArgb(176, 111, 31);
            var sizeMb = result.AssetSize / 1024d / 1024d;
            var answer = MessageBox.Show(this,
                $"Доступен Mertviy {result.LatestVersion} ({sizeMb:N1} МБ).\n\nСкачать файл обновления и проверить SHA-256?",
                "Доступно обновление", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer != DialogResult.Yes) return;

            var updateDirectory = Path.Combine(_appDir, "updates");
            var progress = new Progress<long>(bytes =>
            {
                if (IsDisposed) return;
                var currentMb = bytes / 1024d / 1024d;
                _updateStatus.Text = $"Скачивание: {currentMb:N1} / {sizeMb:N1} МБ";
            });
            _updateStatus.Text = "Скачивание обновления…";
            var path = await _updater.DownloadUpdateAsync(result, updateDirectory, progress, cancellationToken);
            _downloadedUpdatePath = path;
            _updateStatus.Text = $"Файл проверен SHA-256 · {path}";
            _updateStatus.ForeColor = Color.FromArgb(38, 126, 92);
            _launchUpdateButton.Visible = true;
            Append($"Обновление {result.LatestVersion} скачано и проверено: {path}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!IsDisposed) _updateStatus.Text = "Проверка обновления отменена";
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                _updateStatus.Text = "Ошибка загрузки или проверки обновления";
                _updateStatus.ForeColor = Color.FromArgb(176, 67, 58);
            }
            Append($"Обновление: {ex.Message}");
            if (userInitiated && !IsDisposed)
                MessageBox.Show(this, ex.Message, "Mertviy: обновление", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _updateCheckRunning = false;
            if (!IsDisposed) _checkUpdateButton.Enabled = true;
        }
    }

    private void LaunchDownloadedUpdate()
    {
        if (string.IsNullOrWhiteSpace(_downloadedUpdatePath) || !File.Exists(_downloadedUpdatePath))
        {
            _launchUpdateButton.Visible = false;
            _updateStatus.Text = "Скачанный файл не найден";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_downloadedUpdatePath) { UseShellExecute = true });
            StopEngine();
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось запустить обновление", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task AutoSelectAndStart()
    {
        if (_busy || _engine is { HasExited: false }) return;
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            MessageBox.Show(this, "Mertviy нужен запуск от имени администратора для загрузки WinDivert.", "Нужны права администратора", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var existing = Process.GetProcessesByName("winws");
        if (existing.Length > 0)
        {
            foreach (var process in existing) process.Dispose();
            MessageBox.Show(this, "Обнаружен отдельный winws.exe. Остановите его перед подбором, чтобы драйверы перехвата не конфликтовали.", "Уже запущен winws", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _busy = true;
        SetBusy(true);
        try
        {
            ExtractEngine();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var service = (ServiceTarget)row.Tag!;
                row.Cells["status"].Value = "Проверка профилей…";
                _status.Text = $"Подбор: {service.Name}";
                var best = await FindBestStrategy(service);
                if (!_state.Services.TryGetValue(service.Name, out var state)) _state.Services[service.Name] = state = new ServiceState();
                state.StrategyName = best?.Name ?? _catalog.Strategies[0].Name;
                state.LastChecked = DateTimeOffset.UtcNow;
                row.Cells["strategy"].Value = state.StrategyName;
                row.Cells["status"].Value = best is null ? "Оставлен базовый профиль" : "Профиль подобран";
                row.Cells["checked"].Value = state.LastChecked.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
                Append(best is null ? $"{service.Name}: пробы не прошли, сохранён базовый профиль." : $"{service.Name}: выбран «{best.Name}».");
            }
            SaveState();
            StartEngine(BuildAllProfiles());
        }
        catch (Exception ex)
        {
            _status.Text = "Ошибка";
            Append($"Ошибка автоподбора: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Mertviy", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            SetBusy(false);
        }
    }

    private async Task<Strategy?> FindBestStrategy(ServiceTarget service)
    {
        Strategy? best = null;
        var bestLatency = double.MaxValue;
        foreach (var strategy in _catalog.Strategies)
        {
            Append($"{service.Name}: проба «{strategy.Name}»…");
            Process? candidate = null;
            try
            {
                candidate = StartEngine(BuildProfiles([(service, strategy)], _quicCheck.Checked), managed: false);
                await Task.Delay(1100);
                if (candidate.HasExited)
                {
                    Append($"{service.Name}: движок завершился с кодом {candidate.ExitCode}.");
                    continue;
                }
                var latency = await ProbeService(service);
                if (latency is null) continue;
                Append($"{service.Name}: ответ HTTPS за {latency.Value:F0} мс.");
                if (latency.Value < bestLatency)
                {
                    best = strategy;
                    bestLatency = latency.Value;
                }
            }
            finally
            {
                StopProcess(candidate);
            }
        }
        return best;
    }

    private async Task<double?> ProbeService(ServiceTarget service)
    {
        foreach (var url in service.ProbeUrls)
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = true };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.ConnectionClose = true;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if ((int)response.StatusCode is >= 200 and < 400) return stopwatch.Elapsed.TotalMilliseconds;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
        }
        return null;
    }

    private void StartSavedProfiles()
    {
        if (_busy || _engine is { HasExited: false }) return;
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            MessageBox.Show(this, "Mertviy нужен запуск от имени администратора для загрузки WinDivert.", "Нужны права администратора", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            ExtractEngine();
            StartEngine(BuildAllProfiles());
        }
        catch (Exception ex)
        {
            Append($"Ошибка запуска: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Mertviy", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private List<string> BuildAllProfiles()
    {
        var selected = new List<(ServiceTarget Service, Strategy Strategy)>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            var service = (ServiceTarget)row.Tag!;
            var name = Convert.ToString(row.Cells["strategy"].Value) ?? _catalog.Strategies[0].Name;
            selected.Add((service, _catalog.Strategies.FirstOrDefault(x => x.Name == name) ?? _catalog.Strategies[0]));
        }
        return BuildProfiles(selected, _quicCheck.Checked);
    }

    private List<string> BuildProfiles(IEnumerable<(ServiceTarget Service, Strategy Strategy)> profiles, bool enableQuic)
    {
        var args = new List<string>
        {
            "--wf-tcp=80,443,2053,2083,2087,2096,8443",
            enableQuic ? "--wf-udp=443,19294-19344,50000-50100" : "--wf-udp=19294-19344,50000-50100"
        };
        foreach (var (service, strategy) in profiles)
        {
            var domains = string.Join(",", service.Hosts);
            args.AddRange(["--filter-tcp=80", $"--hostlist-domains={domains}", "--dpi-desync=fake,fakedsplit", "--dpi-desync-repeats=6", "--dpi-desync-fakedsplit-pattern=0x00", "--new"]);
            args.AddRange(["--filter-tcp=443,2053,2083,2087,2096,8443", $"--hostlist-domains={domains}", $"--dpi-desync={strategy.Desync}"]);
            if (!string.IsNullOrWhiteSpace(strategy.SplitPosition)) args.Add($"--dpi-desync-split-pos={strategy.SplitPosition}");
            args.AddRange([$"--dpi-desync-repeats={strategy.Repeats}", "--dpi-desync-fooling=badseq", "--dpi-desync-fake-tls=!", "--dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com", "--new"]);
            if (enableQuic)
            {
                args.AddRange(["--filter-udp=443", "--filter-l7=quic", $"--hostlist-domains={domains}", "--dpi-desync=fake", "--dpi-desync-repeats=6", $"--dpi-desync-fake-quic={Path.Combine(_engineDir, "quic_initial_www_google_com.bin")}", "--new"]);
            }
            if (service.Name == "Discord")
            {
                args.AddRange(["--filter-udp=19294-19344,50000-50100", "--filter-l7=discord,stun", "--dpi-desync=fake", $"--dpi-desync-fake-discord={Path.Combine(_engineDir, "ACTIVE_DISCORD_UDP.bin")}", $"--dpi-desync-fake-stun={Path.Combine(_engineDir, "stun.bin")}", "--dpi-desync-repeats=6", "--new"]);
            }
        }
        if (args[^1] == "--new") args.RemoveAt(args.Count - 1);
        return args;
    }

    private void ExtractEngine()
    {
        Directory.CreateDirectory(_engineDir);
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)).ToArray();
        if (resources.Length == 0) throw new InvalidOperationException("Встроенные файлы сетевого движка не найдены.");
        foreach (var resource in resources)
        {
            var fileName = resource[ResourcePrefix.Length..];
            var target = Path.Combine(_engineDir, fileName);
            using var source = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException($"Не удалось прочитать встроенный файл {fileName}.");
            using var destination = File.Create(target);
            source.CopyTo(destination);
        }
        if (!File.Exists(Path.Combine(_engineDir, "winws.exe")) || !File.Exists(Path.Combine(_engineDir, "WinDivert64.sys")))
            throw new InvalidOperationException("Не удалось подготовить движок Mertviy.");
    }

    private Process StartEngine(List<string> args, bool managed = true)
    {
        var executable = Path.Combine(_engineDir, "winws.exe");
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = _engineDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        var process = Process.Start(info) ?? throw new InvalidOperationException("Не удалось запустить сетевой движок.");
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data) && !IsDisposed && IsHandleCreated) BeginInvoke(() => Append(e.Data)); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data) && !IsDisposed && IsHandleCreated) BeginInvoke(() => Append(e.Data)); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (managed)
        {
            _engine = process;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                if (!IsDisposed && IsHandleCreated) BeginInvoke(() =>
                {
                    if (!ReferenceEquals(_engine, process)) return;
                    _engine = null;
                    SetRunning(false);
                    Append("Сетевой движок завершился.");
                });
            };
            SetRunning(true);
        }
        return process;
    }

    private void StopEngine()
    {
        var process = _engine;
        _engine = null;
        StopProcess(process);
        SetRunning(false);
    }

    private static void StopProcess(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(true);
            process.WaitForExit(3000);
        }
        catch { }
        finally { process.Dispose(); }
    }

    private void SetBusy(bool busy)
    {
        _autoButton.Enabled = !busy;
        _startButton.Enabled = !busy && _engine is not { HasExited: false };
        _stopButton.Enabled = !busy && _engine is { HasExited: false };
        _grid.Enabled = !busy && _engine is not { HasExited: false };
        _quicCheck.Enabled = !busy && _engine is not { HasExited: false };
    }

    private void SetRunning(bool running)
    {
        if (IsDisposed) return;
        _startButton.Enabled = !running && !_busy;
        _stopButton.Enabled = running && !_busy;
        _grid.Enabled = !running && !_busy;
        _quicCheck.Enabled = !running && !_busy;
        _status.Text = running ? "Движок работает" : "Готов";
    }

    private void Append(string message)
    {
        if (IsDisposed) return;
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}
