using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Linq;
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
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public Settings Settings { get; set; }

        private ObservableCollection<SelectableItem>? _items;
        private ImageDirectoryWatcher? _watcher;
        private MiviaClient? _client;

        private TaskbarIcon _taskbarIcon;

        private List<RemoteJob> _jobs = new List<RemoteJob>();
        private Timer _jobsTimer = new Timer();

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

            SetTaskbarActivityIcon();

            var toRemove = new HashSet<RemoteJob>();
            foreach (var job in _jobs)
            {
                var modelName = job.Model.DisplayName.Replace(" ", "_");
                var path = Path.Join(Settings.InputDirectory, Path.GetFileNameWithoutExtension(job.Image.OrginalFilename) + "-" + modelName);
                try
                {
                    var completed = await _client.IsJobCompleted(job.Id.ToString());
                    if (!completed) continue;
                    await _client.SaveReport(job.Id.ToString(), path);
                }
                catch (Exception exception)
                {
                    _taskbarIcon.ShowError($"Error while calculating results for image {job.Image.OrginalFilename}");
                    ErrorLogger.Instance.LogError(exception.ToString());
                    _client.SaveError(path);
                }

                _taskbarIcon.ShowMessage($"Image {job.Image.OrginalFilename} has been processed");
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
            await LoadDataAsync();
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

        private async Task LoadDataAsync()
        {
            // Fetch models
            var models = await MiviaClient.GetModels(Settings.ServerUrl);
            if (models == null)
            {
                // Show message box with error
                MessageBox.Show("Error retrieving models. Check your Internet connection.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ErrorLogger.Instance.LogError("Error fetching models. Timeout or server error.");
                return;
            }

            foreach (var model in models)
            {
                Items.Add(new SelectableItem { Text = model.DisplayName, InternalName = model.Name, IsSelected = false, Id = model.Id });
            }

            // Select models based on Settings 
            if (Settings.SelectedModels == null) return;
            foreach (var item in Items)
            {
                if (Settings.SelectedModels.Contains(item.InternalName))
                {
                    item.IsSelected = true;
                }
            }
        }

        private void InitClient()
        {
            if (string.IsNullOrEmpty(Settings.AccessToken)) return;
            var baseUrl = Settings.ServerUrl;
            _client = new MiviaClient(Settings.AccessToken, baseUrl);
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
                    var job = await _client.RunModel(imageId, modelId);
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

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            // Convert _items to a comma-separated string
            // var itemsString = string.Join(", ", _items.Select(item => item.Text));

            // Get the configuration file
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var selectedModels = Items.Where(item => item.IsSelected).Select(item => item.InternalName).ToList();

            // Add or update the settings
            config.AppSettings.Settings.Remove("Models");
            // join selected models with comma
            config.AppSettings.Settings.Add("Models", string.Join(",", selectedModels));


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
            // Set the AccessToken variable to the password entered by the user
            Settings.AccessToken = pbPassword.Password;
        }

        private bool IsDirectoryExists(string path)
        {
            return Directory.Exists(path);
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
    }
}