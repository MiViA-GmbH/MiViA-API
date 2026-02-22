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

            await Dispatcher.InvokeAsync(SetTaskbarActivityIcon);

            var toRemove = new HashSet<RemoteJob>();
            foreach (var job in _jobs)
            {
                var model = job.Model;
                var modelName = model.DisplayName.Replace(" ", "_");
                var path = Path.Join(Settings.InputDirectory, Path.GetFileNameWithoutExtension(job.Image.OrginalFilename) + "-" + modelName);
                try
                {
                    var completed = await _client.IsJobCompleted(job.Id.ToString());
                    if (!completed) continue;
                    await _client.SaveReport(job.Id.ToString(), path);
                }
                catch (Exception exception)
                {
                    await Dispatcher.InvokeAsync(() => _taskbarIcon.ShowError($"Error while calculating results for image {job.Image.OrginalFilename}"));
                    ErrorLogger.Instance.LogError(exception.ToString());
                    // _client.SaveError(path);
                }

                await Dispatcher.InvokeAsync(() => _taskbarIcon.ShowMessage($"Image {job.Image.OrginalFilename} has been processed"));
                toRemove.Add(job);
            }

            _jobs.RemoveAll(toRemove.Contains);
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
            InitApiKeyDebounceTimer();

            if (ValidateApiKey())
            {
                await LoadModelsAsync();
            }
            else
            {
                ShowApiKeyRequiredState();
            }

            InitWatcher();
            InitClient();
            InitJobTimer();
            ErrorLogger.Instance.LogError("Application started");
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

            _taskbarIcon.SetActiveIcon();
            try
            {
                var image = await _client.UploadFile(filePath);
                if (image == null) return;

                var selectedModels = Items.Where(item => item.IsSelected).ToList();
                foreach (var model in selectedModels)
                {
                    var imageId = image.Id.ToString();
                    var modelId = model.Id;
                    var submitedJob = await _client.RunModel(imageId, modelId, model.SelectedCustomizationId);
                    var job = await _client.GetJob(submitedJob.Id.ToString());
                    if (job == null) continue;
                    _jobs.Add(job);
                    _taskbarIcon.ShowMessage($"Image {job.Image.OrginalFilename} has been sent for processing");
                }
            }
            catch (Exception e)
            {
                // If e.Message contains unauthorized, then show message box with error
                if (e.Message.Contains("unauthorised"))
                {
                    // MessageBox.Show("Invalid access token. Please check your access token.", "MiViA Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    _taskbarIcon.ShowError("Invalid access token. Please verify your access token and valid license.");
                    ErrorLogger.Instance.LogError("Invalid access token. Please verify your access token and valid license.");
                    return;
                }
                else
                {
                    _taskbarIcon.ShowError("Error uploading image: " + e.Message);
                    ErrorLogger.Instance.LogError(e.ToString());
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