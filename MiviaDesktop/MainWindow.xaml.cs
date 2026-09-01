using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using MiviaDesktop.Entities;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Timer = System.Timers.Timer;

namespace MiviaDesktop
{
    public class SelectableItem
    {
        public string Text { get; set; } = null!;
        public string InternalName { get; set; } = null!;
        public bool IsSelected { get; set; }
        public string Id { get; set; } = null!;
        public List<ModelCustomization> Customizations { get; set; } = new();
        public string? SelectedCustomizationId { get; set; }
        public bool HasCustomizations => Customizations.Count > 0;
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDisposable
    {
        public Settings Settings { get; set; }

        private ObservableCollection<SelectableItem>? _items;
        private ImageDirectoryWatcher? _watcher;
        private MiviaClient? _client;

        private TaskbarIcon _taskbarIcon;

        private List<RemoteJob> _jobs = new List<RemoteJob>();
        private Timer _jobsTimer = new Timer();
        private Timer _apiKeyDebounceTimer = new Timer();
        private bool _disposed = false;
        private bool _isInitializing = true;

        public ObservableCollection<SelectableItem> Items
        {
            get { return _items; }
            set
            {
                _items = value;
                OnPropertyChanged();
            }
        }


        public MainWindow()
        {
            InitializeComponent();

            Items = new ObservableCollection<SelectableItem>();

            // Initialize settings
            Settings = new Settings
            {
                AccessToken = "", // set default value
                InputDirectory = "", // set default value
            };

            this.DataContext = this;
            _taskbarIcon = new TaskbarIcon(this);
        }

        private async void JobsTimerOnTick(object? sender, EventArgs e)
        {
            if (_client == null) return;
            lock (_jobs) if (_jobs.Count == 0) return;

            // The timer keeps firing while this handler awaits. Report generation must stay
            // serialized: a second POST supersedes our own still-queued report server-side.
            _jobsTimer.Stop();
            try
            {
                await PollJobs();
            }
            catch (Exception ex)
            {
                // async void: an escaping exception would take the process down with the queue.
                ErrorLogger.Instance.LogError($"PollJobs crashed: {ex}");
            }
            finally
            {
                // The poll can outlive an explicit exit; restarting a disposed timer would throw
                // out of this async void and take the process with it.
                if (!_disposed) _jobsTimer.Start();
            }
        }

        private async Task PollJobs()
        {
            var log = ErrorLogger.Instance;

            // The key box can null or replace _client from the UI thread while we poll for minutes.
            var client = _client;
            if (client == null) return;
            log.LogDebug($"PollJobs: polling {_jobs.Count} job(s)");

            await Dispatcher.InvokeAsync(SetTaskbarActivityIcon);

            var toRemove = new HashSet<RemoteJob>();
            RemoteJob[] snapshot;
            lock (_jobs) snapshot = _jobs.ToArray();

            foreach (var job in snapshot)
            {
                var model = job.Model;
                var modelName = model?.DisplayName?.Replace(" ", "_") ?? "unknown";
                var imageName = job.Image?.OrginalFilename ?? "unknown";
                var path = Path.Join(Settings.InputDirectory, Path.GetFileNameWithoutExtension(imageName) + "-" + modelName);
                try
                {
                    var completed = await client.IsJobCompleted(job.Id.ToString());
                    if (!completed)
                    {
                        log.LogDebug($"PollJobs: job {job.Id} still pending");
                        continue;
                    }
                    log.LogInfo($"PollJobs: job {job.Id} completed, saving report to {path}");
                    await client.SaveReport(job.Id.ToString(), path);
                }
                catch (Exception transient) when (transient is MiviaTransientException
                                                  || transient is HttpRequestException
                                                  || transient is TaskCanceledException
                                                  || transient is ObjectDisposedException)
                {
                    // Keep the job queued and try again on the next tick, without spamming toasts.
                    log.LogInfo($"PollJobs: retrying job {job.Id} ({imageName}) later: {transient.Message}");
                    continue;
                }
                catch (Exception exception)
                {
                    await Dispatcher.InvokeAsync(() => _taskbarIcon.ShowError($"Error while calculating results for image {imageName}"));
                    log.LogError($"PollJobs error for job {job.Id} ({imageName}): {exception}");
                    toRemove.Add(job);
                    continue;
                }

                await Dispatcher.InvokeAsync(() => _taskbarIcon.ShowMessage($"Image {imageName} has been processed"));
                toRemove.Add(job);
            }

            lock (_jobs) _jobs.RemoveAll(toRemove.Contains);
        }

        private void SetTaskbarActivityIcon()
        {
            if (_jobs.Count == 0)
            {
                _taskbarIcon.SetInactiveIcon();
            }
            else
            {
                _taskbarIcon.SetActiveIcon();
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();

            if (ValidateApiKey())
            {
                await LoadModelsAsync();
            }
            else
            {
                ShowApiKeyRequiredState();
            }

            _isInitializing = false;
            InitApiKeyDebounceTimer();
            InitWatcher();
            InitClient();
            InitJobTimer();
            ErrorLogger.Instance.LogInfo($"Application started (LogLevel={Settings.LogLevel})");
        }

        private void InitJobTimer()
        {
            _jobsTimer.Interval = 5000;
            _jobsTimer.Elapsed += JobsTimerOnTick;
            _jobsTimer.AutoReset = true;
            _jobsTimer.Enabled = true;
        }

        private void InitApiKeyDebounceTimer()
        {
            _apiKeyDebounceTimer.Interval = 1000; // 1 second debounce
            _apiKeyDebounceTimer.Elapsed += async (sender, e) =>
            {
                _apiKeyDebounceTimer.Stop();
                await Dispatcher.InvokeAsync(async () => await LoadModelsAsync());
            };
            _apiKeyDebounceTimer.AutoReset = false;
        }

        private bool ValidateApiKey()
        {
            return !string.IsNullOrWhiteSpace(Settings.AccessToken);
        }

        private void ShowApiKeyRequiredState()
        {
            tbModelStatus.Text = "Enter your API key to load available models";
            tbModelStatus.Visibility = Visibility.Visible;
            lbItems.Visibility = Visibility.Collapsed;
        }

        private void ShowLoadingState()
        {
            tbModelStatus.Text = "Loading models...";
            tbModelStatus.Visibility = Visibility.Visible;
            lbItems.Visibility = Visibility.Collapsed;
        }

        private void ShowErrorState(string errorMessage)
        {
            tbModelStatus.Text = $"Error: {errorMessage}";
            tbModelStatus.Visibility = Visibility.Visible;
            lbItems.Visibility = Visibility.Collapsed;
        }

        private void ShowModelsLoadedState()
        {
            tbModelStatus.Visibility = Visibility.Collapsed;
            lbItems.Visibility = Visibility.Visible;
        }

        private void ClearModels()
        {
            Items.Clear();
            ShowApiKeyRequiredState();
        }

        private async Task LoadModelsAsync()
        {
            if (!ValidateApiKey())
            {
                ClearModels();
                return;
            }

            SetUIState(UIState.Loading);

            try
            {
                // Ensure we have a client instance with the current API key
                InitClient();
                if (_client == null)
                {
                    SetUIState(UIState.Error, "Failed to initialize API client.");
                    return;
                }

                var models = await _client.GetModels();
                if (models == null)
                {
                    SetUIState(UIState.Error, "Unable to retrieve models. Check your Internet connection.");
                    ErrorLogger.Instance.LogError("Error fetching models. Timeout or server error.");
                    return;
                }

                // Build items with customizations before adding to ObservableCollection
                // (WPF bindings evaluate on add — items must be fully populated first)
                var items = models.Select(model => new SelectableItem
                {
                    Text = model.DisplayName,
                    InternalName = model.Name,
                    IsSelected = false,
                    Id = model.Id
                }).ToList();

                // Load customizations for all models in parallel
                var customizationTasks = items.Select(async item =>
                {
                    var customizations = await _client.GetModelCustomizations(item.Id);
                    item.Customizations = customizations.ToList();
                }).ToArray();
                await Task.WhenAll(customizationTasks);

                // Restore selections from Settings
                if (Settings.SelectedModels != null)
                {
                    foreach (var item in items)
                    {
                        if (Settings.SelectedModels.Contains(item.InternalName))
                        {
                            item.IsSelected = true;
                        }
                    }
                }

                if (Settings.SelectedCustomizations != null)
                {
                    foreach (var item in items)
                    {
                        if (Settings.SelectedCustomizations.TryGetValue(item.InternalName, out var custId)
                            && item.Customizations.Any(c => c.Id == custId))
                        {
                            item.SelectedCustomizationId = custId;
                        }
                    }
                }

                // Add fully populated items to the UI collection
                Items.Clear();
                foreach (var item in items)
                {
                    Items.Add(item);
                }

                SetUIState(UIState.ModelsLoaded);
            }
            catch (Exception ex)
            {
                var errorMessage = GetUserFriendlyErrorMessage(ex);
                SetUIState(UIState.Error, errorMessage);
                ErrorLogger.Instance.LogError(ex.ToString());
            }
        }

        private void InitClient()
        {
            if (string.IsNullOrEmpty(Settings.AccessToken))
            {
                _client?.Dispose();
                _client = null;
                return;
            }
            
            // Only recreate client if necessary
            if (_client == null || _client.AccessToken != Settings.AccessToken || _client.BaseUrl != Settings.ServerUrl)
            {
                _client?.Dispose();
                var baseUrl = Settings.ServerUrl;
                _client = new MiviaClient(Settings.AccessToken, baseUrl);
            }
        }

        private void InitWatcher()
        {
            if (string.IsNullOrEmpty(Settings.InputDirectory)) return;
            _watcher = new ImageDirectoryWatcher(Settings.InputDirectory, OnImageCreated);
            _watcher.Start();
        }

        private async Task OnImageCreated(string filePath)
        {
            if (_client == null) return;

            var log = ErrorLogger.Instance;
            var fileName = Path.GetFileName(filePath);
            log.LogInfo($"OnImageCreated: processing {fileName}");

            _taskbarIcon.SetActiveIcon();
            try
            {
                var image = await _client.UploadFile(filePath);
                if (image == null)
                {
                    log.LogError($"OnImageCreated: upload returned null for {fileName}");
                    _taskbarIcon.ShowError($"Failed to upload image {fileName} — no response from server");
                    return;
                }

                var selectedModels = Items.Where(item => item.IsSelected).ToList();
                log.LogInfo($"OnImageCreated: {selectedModels.Count} model(s) selected for imageId={image.Id}");

                if (selectedModels.Count == 0)
                {
                    log.LogInfo("OnImageCreated: no models selected, skipping job creation");
                    return;
                }

                foreach (var model in selectedModels)
                {
                    var imageId = image.Id.ToString();
                    var modelId = model.Id;
                    log.LogDebug($"OnImageCreated: running model '{model.Text}' (id={modelId}), customization={model.SelectedCustomizationId ?? "(none)"}");

                    var submittedJob = await _client.RunModel(imageId, modelId, model.SelectedCustomizationId);
                    if (submittedJob == null)
                    {
                        log.LogInfo($"OnImageCreated: image {fileName} already calculated with model '{model.Text}', skipping");
                        _taskbarIcon.ShowMessage($"Image {fileName} already processed with {model.Text}, skipping");
                        continue;
                    }

                    var job = await _client.GetJob(submittedJob.Id.ToString());
                    if (job == null)
                    {
                        log.LogError($"OnImageCreated: GetJob returned null for jobId={submittedJob.Id}");
                        continue;
                    }

                    lock (_jobs) _jobs.Add(job);
                    log.LogInfo($"OnImageCreated: job {job.Id} created for {fileName} with model '{model.Text}'");
                    _taskbarIcon.ShowMessage($"Image {job.Image?.OrginalFilename ?? fileName} has been sent for processing");
                }
            }
            catch (Exception e)
            {
                if (e.Message.Contains("unauthorised") || e.Message.Contains("unauthorized"))
                {
                    _taskbarIcon.ShowError("Invalid access token. Please verify your access token and valid license.");
                    log.LogError("Invalid access token. Please verify your access token and valid license.");
                    return;
                }
                else
                {
                    _taskbarIcon.ShowError($"Error processing image {fileName}: {e.Message}");
                    log.LogError($"OnImageCreated error for {fileName}: {e}");
                }
            }
        }


        // INotifyPropertyChanged member
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        private bool m_isExplicitClose = false; // Indicate if it is an explicit form close request from the user.


        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            if (m_isExplicitClose == false) //NOT a user close request? ... then hide
            {
                e.Cancel = true;
                this.Hide();
            }
        }

        private void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            // Create FolderBrowserDialog 
            FolderBrowserDialog dlg = new FolderBrowserDialog();

            // Show FolderBrowserDialog
            DialogResult result = dlg.ShowDialog();

            // Get the selected directory and display it in the ComboBox
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                // Get directory
                string directory = dlg.SelectedPath;

                tbtInputDirectory.Text = directory;
                Settings.InputDirectory = directory;
            }
        }

        private async void btnSave_Click(object sender, RoutedEventArgs e)
        {
            // Ensure models are loaded before saving
            if (!ValidateApiKey())
            {
                MessageBox.Show("Please enter a valid API key before saving.", "API Key Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Items.Count == 0)
            {
                await LoadModelsAsync();
                if (Items.Count == 0)
                {
                    MessageBox.Show("Unable to load models. Please check your API key and internet connection.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // Get the configuration file
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var selectedModels = Items.Where(item => item.IsSelected).Select(item => item.InternalName).ToList();

            // Build customization selections (modelInternalName:customizationId pairs)
            var customizationEntries = Items
                .Where(item => !string.IsNullOrEmpty(item.SelectedCustomizationId))
                .Select(item => $"{item.InternalName}:{item.SelectedCustomizationId}")
                .ToList();

            // Add or update the settings
            config.AppSettings.Settings.Remove("Models");
            config.AppSettings.Settings.Add("Models", string.Join(",", selectedModels));

            config.AppSettings.Settings.Remove("Customizations");
            config.AppSettings.Settings.Add("Customizations", string.Join(",", customizationEntries));

            config.AppSettings.Settings.Remove("AccessToken");
            config.AppSettings.Settings.Add("AccessToken", Settings.AccessToken);

            config.AppSettings.Settings.Remove("InputDirectory");
            config.AppSettings.Settings.Add("InputDirectory", Settings.InputDirectory);

            // Save the configuration file
            config.Save(ConfigurationSaveMode.Modified);

            // Force a reload of the changed section, so the next time it's read, the updated values are retrieved
            ConfigurationManager.RefreshSection("appSettings");

            MessageBox.Show("Settings saved successfully!");
            InitWatcher();
            InitClient();
        }

        private void pbPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            Settings.AccessToken = pbPassword.Password;
            if (_isInitializing) return;

            _apiKeyDebounceTimer.Stop();

            if (string.IsNullOrWhiteSpace(Settings.AccessToken))
            {
                ClearModels();
            }
            else
            {
                _apiKeyDebounceTimer.Start();
            }
        }

        private bool IsDirectoryExists(string path)
        {
            return Directory.Exists(path);
        }

        private string GetUserFriendlyErrorMessage(Exception ex)
        {
            return ex switch
            {
                HttpRequestException httpEx when httpEx.Message.Contains("401") || httpEx.Message.Contains("403") ||
                                                httpEx.Message.Contains("unauthorised") || httpEx.Message.Contains("unauthorized") =>
                    "Invalid API key. Please check your access token.",
                HttpRequestException httpEx when httpEx.Message.Contains("404") =>
                    "Models endpoint not found. Please check server configuration.",
                HttpRequestException httpEx when httpEx.Message.Contains("500") =>
                    "Server error. Please try again later.",
                TaskCanceledException => "Request timed out. Please try again.",
                HttpRequestException => "Network error. Please check your internet connection.",
                _ => "Failed to load models. Please try again."
            };
        }

        private enum UIState
        {
            ApiKeyRequired,
            Loading,
            Error,
            ModelsLoaded
        }

        private void SetUIState(UIState state, string message = "")
        {
            switch (state)
            {
                case UIState.ApiKeyRequired:
                    tbModelStatus.Text = "Enter your API key to load available models";
                    tbModelStatus.Visibility = Visibility.Visible;
                    lbItems.Visibility = Visibility.Collapsed;
                    break;
                case UIState.Loading:
                    tbModelStatus.Text = "Loading models...";
                    tbModelStatus.Visibility = Visibility.Visible;
                    lbItems.Visibility = Visibility.Collapsed;
                    break;
                case UIState.Error:
                    tbModelStatus.Text = $"Error: {message}";
                    tbModelStatus.Visibility = Visibility.Visible;
                    lbItems.Visibility = Visibility.Collapsed;
                    break;
                case UIState.ModelsLoaded:
                    tbModelStatus.Visibility = Visibility.Collapsed;
                    lbItems.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void LoadSettings()
        {
            // Get the access token from the configuration file
            var accessToken = ConfigurationManager.AppSettings["AccessToken"];
            Settings.AccessToken = accessToken ?? string.Empty;
            pbPassword.Password = Settings.AccessToken;

            // Get the selected models from the configuration file
            var selectedModels = ConfigurationManager.AppSettings["Models"];
            if (!string.IsNullOrWhiteSpace(selectedModels))
            {
                Settings.SelectedModels = selectedModels.Split(',').ToList();
            }

            // Get the selected customizations from the configuration file
            var customizations = ConfigurationManager.AppSettings["Customizations"];
            if (!string.IsNullOrWhiteSpace(customizations))
            {
                Settings.SelectedCustomizations = new Dictionary<string, string>();
                foreach (var entry in customizations.Split(','))
                {
                    var parts = entry.Split(':', 2);
                    if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        Settings.SelectedCustomizations[parts[0]] = parts[1];
                    }
                }
            }

            var inputDirectory = ConfigurationManager.AppSettings["InputDirectory"];
            Settings.InputDirectory = inputDirectory ?? string.Empty;
            if (Settings.InputDirectory != "" && !IsDirectoryExists(Settings.InputDirectory))
            {
                MessageBox.Show("Input directory does not exist");
                Settings.InputDirectory = "";
            }

            Settings.ServerUrl = ConfigurationManager.AppSettings["ServerUrl"];

            // Load log level
            var logLevelStr = ConfigurationManager.AppSettings["LogLevel"];
            if (Enum.TryParse<LogLevel>(logLevelStr, true, out var logLevel))
            {
                Settings.LogLevel = logLevel;
            }
            ErrorLogger.Instance.Level = Settings.LogLevel;

            tbtInputDirectory.Text = Settings.InputDirectory;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _client?.Dispose();
                    _watcher?.Stop();
                    _jobsTimer?.Dispose();
                    _apiKeyDebounceTimer?.Dispose();
                    _taskbarIcon?.Dispose();
                }
                _disposed = true;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Dispose();
        }
    }
}