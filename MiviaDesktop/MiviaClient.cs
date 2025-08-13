using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using MiviaDesktop.Entities;

namespace MiviaDesktop
{
    public class MiviaClient : IDisposable
    {
        private string _accessToken;
        private HttpClient _client;

        private static string _baseUrl = "https://app.mivia.ai";
        private const string UploadUri = "/api/image";
        private const string ModelsUri = "/api/settings/models";
        private const string ModelUri = "/api/jobs";
        private const string ReportUri = "/api/reports/pdf2";

        public MiviaClient(string accessToken, string? baseUrl)
        {
            _baseUrl = baseUrl ?? _baseUrl;
            _accessToken = accessToken;
            _client = new HttpClient { BaseAddress = new Uri(_baseUrl) };
            _client.DefaultRequestHeaders.Add("authorization", accessToken);
        }


        public static async Task<ModelSettings[]?> GetModels(string? baseUrl = null)
        {
            try
            {
                var tmpClient = new HttpClient { BaseAddress = new Uri(baseUrl ?? _baseUrl), Timeout = new TimeSpan(0, 0, 3) };

                HttpResponseMessage response = await tmpClient.GetAsync(ModelsUri);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var modelSettings = JsonSerializer.Deserialize<ModelSettings[]>(jsonResponse, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return modelSettings;
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
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

            // Get the original file name
            var fileName = Path.GetFileName(filePath);

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
            _client?.Dispose();
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