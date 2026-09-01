using System;
using System.Collections.Generic;
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
        // One handler for every presigned download: a per-download HttpClient leaks sockets.
        private static readonly HttpClient DownloadClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        // jobId -> reportId we are already waiting for. Posting again would supersede it.
        private readonly Dictionary<string, string> _pendingReports = new Dictionary<string, string>();

        private readonly string _accessToken;
        private readonly HttpClient _client;
        private readonly string _baseUrl;
        private bool _disposed = false;

        private const string DefaultBaseUrl = "https://app.mivia.ai";
        private const string UploadUri = "/api/image";
        private const string ModelsUri = "/api/settings/available-models";
        private const string ModelUri = "/api/jobs";
        private const string ReportsUri = "/api/reports";

        private const int ReportPollIntervalMs = 3000;
        private const int ReportTimeoutMs = 5 * 60 * 1000;

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
            var log = ErrorLogger.Instance;
            log.LogDebug("GetModels: fetching available models");
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
                    log.LogDebug($"GetModels: received {modelSettings?.Length ?? 0} model(s)");
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
                throw;
            }
        }

        /// <summary>
        /// Fetches available customization groups for a model assigned to current user.
        /// </summary>
        public async Task<ModelCustomization[]> GetModelCustomizations(string modelId)
        {
            try
            {
                var response = await _client.GetAsync($"/api/models/{modelId}/customizations");
                if (!response.IsSuccessStatusCode) return Array.Empty<ModelCustomization>();

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var items = JsonSerializer.Deserialize<ModelCustomizationResponse[]>(jsonResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return items?.Select(i => new ModelCustomization
                {
                    Id = i.Id,
                    Name = i.Name?.En ?? ""
                }).ToArray() ?? Array.Empty<ModelCustomization>();
            }
            catch
            {
                return Array.Empty<ModelCustomization>();
            }
        }

        public async Task<RemoteJob?> RunModel(string imageId, string modelId, string? customizationId = null)
        {
            var log = ErrorLogger.Instance;
            log.LogDebug($"RunModel: imageId={imageId}, modelId={modelId}, customizationId={customizationId ?? "(none)"}");

            object body = customizationId != null
                ? new { imageIds = new[] { imageId }, modelId, customizationId, source = "API" }
                : new { imageIds = new[] { imageId }, modelId, source = "API" };

            var jsonContent = JsonContent.Create(body);
            var response = await _client.PostAsync(ModelUri, jsonContent);

            var jsonResponse = await response.Content.ReadAsStringAsync();
            log.LogDebug($"RunModel response: HTTP {(int)response.StatusCode}, body={jsonResponse}");

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"RunModel failed with HTTP {(int)response.StatusCode}: {jsonResponse}");
            }

            var jobs = Serialization.Deserialize<RemoteJob[]>(jsonResponse);
            if (jobs == null || jobs.Length == 0)
            {
                log.LogInfo($"RunModel returned empty array for imageId={imageId}, modelId={modelId} — image already calculated");
                return null;
            }

            log.LogInfo($"RunModel created job {jobs[0].Id} for imageId={imageId}");
            return jobs[0];
        }


        public async Task<RemoteImage?> UploadFile(string filePath)
        {
            var log = ErrorLogger.Instance;
            log.LogInfo($"UploadFile: {filePath}");

            // Read the file content as bytes
            byte[] data = await File.ReadAllBytesAsync(filePath);
            log.LogDebug($"UploadFile: read {data.Length} bytes");

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
                log.LogDebug($"UploadFile response: {jsonResponse}");
                var images = JsonSerializer.Deserialize<RemoteImage[]>(jsonResponse);
                var image = images?.FirstOrDefault();
                if (image != null)
                {
                    log.LogInfo($"UploadFile: uploaded as imageId={image.Id}");
                }
                else
                {
                    log.LogError($"UploadFile: server returned success but no image data. Response: {jsonResponse}");
                }
                return image;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"UploadFile failed with HTTP {(int)response.StatusCode}: {error}");
            }
        }

        public async Task<bool> IsJobCompleted(string jobId)
        {
            var job = await GetJob(jobId);
            ErrorLogger.Instance.LogDebug($"IsJobCompleted: jobId={jobId}, status={job.Status}, resultId={job.ResultId}");
            if (job.Status == JobStatus.FAILED) throw new MiviaPermanentException(job.Error ?? "Job failed with no error message");
            if (job.Status == JobStatus.PENDING) return false;
            return job?.ResultId != null;
        }

        /// <summary>
        /// Reports are asynchronous: POST returns 202 with a report id, the row is polled until it
        /// is terminal, and the file itself comes from a presigned URL.
        /// Only one report per account may be generated at a time - the server cancels a caller's
        /// still-queued reports whenever that caller posts a new one.
        /// </summary>
        public async Task SaveReport(string jobId, string reportPathWithoutExtension)
        {
            var log = ErrorLogger.Instance;

            // A CANCELLED report means somebody (a second window, the web UI) superseded ours.
            // One retry is enough; a second cancellation means we are fighting for the slot.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                RemoteReport report;
                if (_pendingReports.TryGetValue(jobId, out var outstanding))
                {
                    // Resume. A fresh POST here would cancel the very report we are waiting for,
                    // so a report slower than our poll window must never be re-requested.
                    report = await WaitForReport(outstanding);
                }
                else
                {
                    var created = await CreateReport(jobId);
                    _pendingReports[jobId] = created.ReportId.ToString();
                    log.LogDebug($"SaveReport: report {created.ReportId} created with status {created.Status}");

                    report = created.Status == ReportStatus.PENDING
                        ? await WaitForReport(created.ReportId.ToString())
                        : new RemoteReport { Id = created.ReportId, Status = created.Status };
                }

                switch (report.Status)
                {
                    case ReportStatus.DONE:
                        await DownloadReport(report.Id.ToString(), reportPathWithoutExtension);
                        _pendingReports.Remove(jobId);
                        return;
                    case ReportStatus.FAILED:
                        _pendingReports.Remove(jobId);
                        throw new MiviaPermanentException(report.Error ?? "Report generation failed");
                    case ReportStatus.CANCELLED:
                        _pendingReports.Remove(jobId);
                        log.LogInfo($"SaveReport: report {report.Id} was superseded, retrying");
                        continue;
                    default:
                        // Keep the id: the next tick resumes this report rather than starting a new one.
                        throw new MiviaTransientException($"Report {report.Id} still pending after {ReportTimeoutMs / 1000}s");
                }
            }

            throw new MiviaTransientException("Report was superseded twice in a row");
        }

        private async Task<CreatedReport> CreateReport(string jobId)
        {
            object body = TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana)
                ? new { jobsIds = new[] { jobId }, timezone = iana }
                // tzOffset is deprecated server-side but still honoured, and it is all we have
                // when Windows has no ICU mapping for the local zone.
                : (object)new { jobsIds = new[] { jobId }, tzOffset = -DateTimeOffset.Now.Offset.TotalMinutes };

            var response = await _client.PostAsync($"{ReportsUri}/pdf", JsonContent.Create(body));
            var json = await response.Content.ReadAsStringAsync();
            ThrowForStatus(response, json, "CreateReport");
            return Serialization.Deserialize<CreatedReport>(json);
        }

        public async Task<RemoteReport> GetReport(string reportId)
        {
            var response = await _client.GetAsync($"{ReportsUri}/{reportId}");
            var json = await response.Content.ReadAsStringAsync();
            ThrowForStatus(response, json, "GetReport");
            return Serialization.Deserialize<RemoteReport>(json);
        }

        private async Task<RemoteReport> WaitForReport(string reportId)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ReportTimeoutMs);
            var report = await GetReport(reportId);
            while (report.Status == ReportStatus.PENDING && DateTime.UtcNow < deadline)
            {
                await Task.Delay(ReportPollIntervalMs);
                report = await GetReport(reportId);
            }

            return report;
        }

        private async Task DownloadReport(string reportId, string reportPathWithoutExtension)
        {
            var response = await _client.GetAsync($"{ReportsUri}/{reportId}/download");
            var json = await response.Content.ReadAsStringAsync();
            ThrowForStatus(response, json, "DownloadReport");

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("url", out var urlElement)
                || urlElement.ValueKind != JsonValueKind.String)
            {
                // { "status": "expired" } - the bucket lifecycle already removed the object.
                throw new MiviaPermanentException($"Report {reportId} is no longer available for download");
            }

            // Download beside the target and rename once complete, so an interrupted transfer
            // never leaves a truncated pdf that would block every later attempt.
            var target = reportPathWithoutExtension + ".pdf";
            var partial = target + ".part";
            try
            {
                // The presigned URL carries its own signature; our authorization header would break it.
                using (var file = await DownloadClient.GetAsync(urlElement.GetString(), HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!file.IsSuccessStatusCode)
                    {
                        throw new MiviaTransientException($"Report download failed with HTTP {(int)file.StatusCode}");
                    }

                    using var fs = new FileStream(partial, FileMode.Create);
                    await file.Content.CopyToAsync(fs);
                }

                // No overwrite, same as before: an existing report is left untouched.
                File.Move(partial, target);
            }
            catch
            {
                try { File.Delete(partial); } catch { /* best effort */ }
                throw;
            }
        }

        /// <summary>
        /// Splits API failures into "try again next tick" and "this job is done for".
        /// 429 is the per-account cap of 3 reports in flight, not a rate limit.
        /// </summary>
        private static void ThrowForStatus(HttpResponseMessage response, string body, string context)
        {
            if (response.IsSuccessStatusCode) return;

            var code = (int)response.StatusCode;
            var message = $"{context} failed with HTTP {code}: {body}";

            if (code == 429 || code >= 500)
            {
                throw new MiviaTransientException(message);
            }

            throw new MiviaPermanentException(message);
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
            var jsonResponse = await response.Content.ReadAsStringAsync();
            ThrowForStatus(response, jsonResponse, "GetJob");
            return Serialization.Deserialize<RemoteJob>(jsonResponse);
        }
    }
}