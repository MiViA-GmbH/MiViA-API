using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MiviaDesktop.Entities;

namespace MiviaDesktop
{
    public class MiviaClient : IDisposable
    {
        private readonly string _accessToken;
        private readonly HttpClient _client;
        private readonly string _baseUrl;
        private bool _disposed = false;

        private const string DefaultBaseUrl = "https://app.mivia.ai";
        private const string UploadUri = "/api/image";
        private const string ModelsUri = "/api/settings/available-models";
        private const string ModelUri = "/api/jobs";
        private const string ReportUri = "/api/reports/pdf2";

        public string AccessToken => _accessToken;
        public string BaseUrl => _baseUrl;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetLongPathName(string shortPath, StringBuilder longPath, int bufferSize);

        /// <summary>
        /// Converts Windows 8.3 short path to long path. Returns original path if conversion fails.
        /// </summary>
        private static string GetLongPath(string path)
        {
            var buffer = new StringBuilder(260);
            int result = GetLongPathName(path, buffer, buffer.Capacity);

            if (result > buffer.Capacity)
            {
                buffer.Capacity = result;
                result = GetLongPathName(path, buffer, buffer.Capacity);
            }

            return result > 0 ? buffer.ToString() : path;
        }

        public MiviaClient(string accessToken, string? baseUrl = null)
        {
            _baseUrl = baseUrl ?? DefaultBaseUrl;
            _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
            _client = new HttpClient { BaseAddress = new Uri(_baseUrl), Timeout = TimeSpan.FromSeconds(30) };
            _client.DefaultRequestHeaders.Add("authorization", _accessToken);
        }


        public async Task<ModelSettings[]?> GetModels()
        {
            try
            {
                HttpResponseMessage response = await _client.GetAsync(ModelsUri);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var modelSettings = JsonSerializer.Deserialize<ModelSettings[]>(jsonResponse, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return modelSettings;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"HTTP {response.StatusCode}: {error}");
                }
            }
            catch (Exception)
            {
                throw; // Re-throw to preserve original exception details
            }
        }

        public async Task<RemoteJob?> RunModel(string imageId, string modelId)
        {
            var jsonContent = JsonContent.Create(new
            {
                imageIds = new[] { imageId },
                modelId = modelId,
                source = "API"
            });
            var response = await _client.PostAsync(ModelUri, jsonContent);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception(error);
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var jobs = Serialization.Deserialize<RemoteJob[]>(jsonResponse);
            return jobs?.FirstOrDefault();
        }


        public async Task<RemoteImage?> UploadFile(string filePath)
        {
            // Read the file content as bytes
            byte[] data = await File.ReadAllBytesAsync(filePath);

            // Create ByteArrayContent with the file data
            var fileContent = new ByteArrayContent(data);

            // Convert short path to long path to preserve original filename
            var longPath = GetLongPath(filePath);
            var fileName = Path.GetFileName(longPath);

            // Set the content disposition headers
            fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "files",
                FileName = null,
                FileNameStar = fileName
            };

            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            // Create a MultipartFormDataContent object
            var content = new MultipartFormDataContent
            {
                { fileContent, "files", fileName } // Explicitly add the file name
            };

            // Add the forced field
            content.Add(new StringContent("false"), "forced");

            // Send the POST request
            var response = await _client.PostAsync(UploadUri, content);

            // Handle the response
            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var images = JsonSerializer.Deserialize<RemoteImage[]>(jsonResponse);
                return images?.FirstOrDefault();
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception(error);
            }
        }

        public async Task<bool> IsJobCompleted(string jobId)
        {
            var job = await GetJob(jobId);
            if (job.Status == JobStatus.FAILED) throw new Exception(job.Error);
            if (job.Status == JobStatus.PENDING) return false;
            return job?.ResultId != null;
        }

        public async Task SaveReport(string jobId, string reportPathWithoutExtension)
        {
            var offset = -DateTimeOffset.Now.Offset.TotalMinutes;

            var jsonContent = JsonContent.Create(new
            {
                jobsIds = new[] { jobId },
                tzOffset = offset
            });
            var response = await _client.PostAsync(ReportUri, jsonContent);
            using (var fs = new FileStream(reportPathWithoutExtension + ".pdf", FileMode.CreateNew))
            {
                await response.Content.CopyToAsync(fs);
            }
        }

        public void SaveError(string path)
        {
            try
            {
                using (var fs = new FileStream(path + ".txt", FileMode.CreateNew))
                {
                    using (var sw = new StreamWriter(fs))
                    {
                        sw.Write("Error occurred during processing the image.");
                    }
                }
            }
            catch (Exception)
            {
                // ignored
            }
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
                }
                _disposed = true;
            }
        }

        public async Task<RemoteJob> GetJob(string jobId)
        {
            var response = await _client.GetAsync($"{ModelUri}/{jobId}");
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception(error);
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            return Serialization.Deserialize<RemoteJob>(jsonResponse);
        }
    }
}