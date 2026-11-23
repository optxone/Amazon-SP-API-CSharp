using FikaAmazonAPI.AmazonSpApiSDK.Models.Exceptions;
using FikaAmazonAPI.AmazonSpApiSDK.Models.Reports;
using FikaAmazonAPI.AmazonSpApiSDK.Models.Token;
using FikaAmazonAPI.Parameter;
using FikaAmazonAPI.Parameter.Report;
using FikaAmazonAPI.Utils;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static FikaAmazonAPI.Utils.Constants;

namespace FikaAmazonAPI.Services
{
    public class ReportService : RequestService, IReportOperations
    {
        public ReportService(AmazonCredential amazonCredential, ILoggerFactory? loggerFactory) : base(amazonCredential, loggerFactory)
        {
        }
        #region GetReport
        public IList<Report> GetReports(ParameterReportList parameterReportList) =>
            Task.Run(() => GetReportsAsync(parameterReportList)).ConfigureAwait(false).GetAwaiter().GetResult();
        public async Task<IList<Report>> GetReportsAsync(ParameterReportList parameterReportList, CancellationToken cancellationToken = default)
        {
            if (parameterReportList.marketplaceIds == null || parameterReportList.marketplaceIds.Count == 0)
            {
                parameterReportList.marketplaceIds = new List<string>();
                parameterReportList.marketplaceIds.Add(AmazonCredential.MarketPlace.ID);
            }
            if (parameterReportList.createdSince.HasValue)
            {
                var totalDays = (parameterReportList.createdSince.Value - DateTime.UtcNow).TotalDays;
                if (totalDays > 90)
                {
                    throw new AmazonInvalidInputException("Amazon api not accepting createdSince more than 90 days ," +
                        "The earliest report creation date and time for reports to include in the response, in ISO 8601 date time format. The default is 90 days ago. Reports are retained for a maximum of 90 days. https://github.com/amzn/selling-partner-api-docs/blob/main/references/reports-api/reports_2021-06-30.md#parameters");
                }
            }
            var parameters = parameterReportList.getParameters();

            await CreateAuthorizedRequestAsync(ReportApiUrls.GetReports, RestSharp.Method.Get, parameters, cancellationToken: cancellationToken);
            var response = await ExecuteRequestAsync<GetReportsResponseV00>(RateLimitType.Report_GetReports, cancellationToken);
            parameterReportList.nextToken = response.NextToken;
            var list = response.Reports;

            while (!string.IsNullOrEmpty(parameterReportList.nextToken))
            {
                var nextTokenResponse = await GetReportsByNextTokenAsync(parameterReportList, cancellationToken);
                list.AddRange(nextTokenResponse.Reports);
                parameterReportList.nextToken = nextTokenResponse.NextToken;
            }
            return list;
        }

        public Report GetReport(string reportId) =>
            Task.Run(() => GetReportAsync(reportId)).ConfigureAwait(false).GetAwaiter().GetResult();
        public async Task<Report> GetReportAsync(string reportId, CancellationToken cancellationToken = default)
        {

            await CreateAuthorizedRequestAsync(ReportApiUrls.GetReport(reportId), RestSharp.Method.Get, cancellationToken: cancellationToken);
            var response = await ExecuteRequestAsync<Report>(RateLimitType.Report_GetReport, cancellationToken);
            if (response != null)
                return response;
            return null;
        }

        public bool CancelReport(string reportId) =>
            Task.Run(() => CancelReportAsync(reportId)).ConfigureAwait(false).GetAwaiter().GetResult();
        public async Task<bool> CancelReportAsync(string reportId, CancellationToken cancellationToken = default)
        {

            await CreateAuthorizedRequestAsync(ReportApiUrls.CancelReport(reportId), RestSharp.Method.Delete, cancellationToken: cancellationToken);
            var response = await ExecuteRequestAsync<CancelReportResponse>(RateLimitType.Report_CancelReport, cancellationToken);
            if (response != null && response.Errors != null)
                return false;
            return true;
        }

        public ReportScheduleList GetReportSchedules(ParameterReportSchedules parametersSchedules) =>
            Task.Run(() => GetReportSchedulesAsync(parametersSchedules)).ConfigureAwait(false).GetAwaiter().GetResult();
        public async Task<ReportScheduleList> GetReportSchedulesAsync(ParameterReportSchedules parametersSchedules, CancellationToken cancellationToken = default)
        {
            var parameters = parametersSchedules.getParameters();
            await CreateAuthorizedRequestAsync(ReportApiUrls.GetReportSchedules, RestSharp.Method.Get, parameters, cancellationToken: cancellationToken);
            var response = await ExecuteRequestAsync<GetReportSchedulesResponseV00>(RateLimitType.Report_GetReportSchedules, cancellationToken);
            if (response != null && response.ReportSchedules != null)
                return response.ReportSchedules;
            return null;
        }

        public GetReportsResponseV00 GetReportsByNextToken(ParameterReportList parameterReportList) =>
            Task.Run(() => GetReportsByNextTokenAsync(parameterReportList)).ConfigureAwait(false).GetAwaiter().GetResult();
        public async Task<GetReportsResponseV00> GetReportsByNextTokenAsync(ParameterReportList parameterReportList, CancellationToken cancellationToken = default)
        {
            List<KeyValuePair<string, string>> parameters = null;
            if (string.IsNullOrEmpty(parameterReportList.nextToken))
                parameters = parameterReportList.getParameters();
            else
            {
                var parameterReportListNew = new ParameterReportList();
                parameterReportListNew.nextToken = parameterReportList.nextToken;
                parameters = parameterReportListNew.getParameters();
            }

            await CreateAuthorizedRequestAsync(ReportApiUrls.GetReports, RestSharp.Method.Get, parameters, cancellationToken: cancellationToken);
            var response = await ExecuteRequestAsync<GetReportsResponseV00>(RateLimitType.Report_GetReports, cancellationToken);
            return response;
        }

        public string CreateReport(ParameterCreateReportSpecification createReportSpecification) =>
            Task.Run(() => CreateReportAsync(createReportSpecification)).ConfigureAwait(false).GetAwaiter().GetResult();
        public async Task<string> CreateReportAsync(ParameterCreateReportSpecification createReportSpecification, CancellationToken cancellationToken = default)
        {
            if (createReportSpecification.marketplaceIds == null || createReportSpecification.marketplaceIds.Count == 0)
            {
                createReportSpecification.marketplaceIds = new MarketplaceIds();
                createReportSpecification.marketplaceIds.Add(AmazonCredential.MarketPlace.ID);
            }
            await CreateAuthorizedRequestAsync(ReportApiUrls.CreateReport, RestSharp.Method.Post, null, createReportSpecification, cancellationToken: cancellationToken);
            var response = await ExecuteRequestAsync<AmazonSpApiSDK.Models.Reports.CreateReportResult>(RateLimitType.Report_CreateReport, cancellationToken);

            if (response == null)
                return null;


            return response.ReportId;
        }

        public string CreateReportSchedule(ParameterCreateReportScheduleSpecification createReportScheduleSpecification) =>
            Task.Run(() => CreateReportScheduleAsync(createReportScheduleSpecification)).ConfigureAwait(false).GetAwaiter().GetResult();
        public async Task<string> CreateReportScheduleAsync(ParameterCreateReportScheduleSpecification createReportScheduleSpecification, CancellationToken cancellationToken = default)
        {
            await CreateAuthorizedRequestAsync(ReportApiUrls.CreateReportSchedule, RestSharp.Method.Post, null, createReportScheduleSpecification, cancellationToken: cancellationToken);
            var response = await ExecuteRequestAsync<CreateReportScheduleResult>(RateLimitType.Report_CreateReportSchedule, cancellationToken);

            if (response == null)
                return null;


            return response.ReportScheduleId;
        }

        public ReportSchedule GetReportSchedule(string reportScheduleId) =>
            Task.Run(() => GetReportScheduleAsync(reportScheduleId)).ConfigureAwait(false).GetAwaiter().GetResult();
        public async Task<ReportSchedule> GetReportScheduleAsync(string reportScheduleId, CancellationToken cancellationToken = default)
        {
            await CreateAuthorizedRequestAsync(ReportApiUrls.GetReportSchedule(reportScheduleId), RestSharp.Method.Get, cancellationToken: cancellationToken);
            var response = await ExecuteRequestAsync<ReportSchedule>(RateLimitType.Report_GetReportSchedules, cancellationToken);
            if (response != null)
                return response;
            return null;
        }

        public ReportDocument GetReportDocument(string reportDocumentId, bool isRestrictedReport = false) =>
            Task.Run(() => GetReportDocumentAsync(reportDocumentId, isRestrictedReport)).ConfigureAwait(false).GetAwaiter().GetResult();
        public async Task<ReportDocument> GetReportDocumentAsync(string reportDocumentId, bool isRestrictedReport = false, CancellationToken cancellationToken = default, bool enableContentEncodingUrlHeader = false)
        {
            ParameterBasedPII parameterBasedPII = null;

            if (isRestrictedReport)
            {
                parameterBasedPII = new ParameterBasedPII()
                {
                    IsNeedRestrictedDataToken = true,
                    RestrictedDataTokenRequest = new CreateRestrictedDataTokenRequest
                    {
                        restrictedResources = new List<RestrictedResource>
                        {
                            new RestrictedResource
                            {
                                method = Method.GET.ToString(),
                                path = ReportApiUrls.GetReportDocument(reportDocumentId)
                            }
                        }
                    }
                };
            }

            List<KeyValuePair<string, string>> queryParameters = null;
            if (enableContentEncodingUrlHeader)
            {
                queryParameters = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("enableContentEncodingUrlHeader", "true")
                };
            }

            await CreateAuthorizedRequestAsync(ReportApiUrls.GetReportDocument(reportDocumentId), RestSharp.Method.Get, queryParameters, parameter: parameterBasedPII, cancellationToken: cancellationToken);
            var response = await ExecuteRequestAsync<ReportDocument>(RateLimitType.Report_GetReportDocument, cancellationToken);
            if (response != null)
                return response;
            return null;
        }
        public string GetReportFile(string reportDocumentId, bool isRestrictedReport = false) =>
            Task.Run(() => GetReportFileAsync(reportDocumentId, isRestrictedReport)).ConfigureAwait(false).GetAwaiter().GetResult();
        public async Task<string> GetReportFileAsync(string reportDocumentId, bool isRestrictedReport = false, CancellationToken cancellationToken = default)
        {
            var reportDocument = await GetReportDocumentAsync(reportDocumentId, isRestrictedReport, cancellationToken);

            return await GetFileAsync(reportDocument, cancellationToken);
        }

        public MemoryStream GetReportFileStream(string reportDocumentId, bool isRestrictedReport = false) =>
            Task.Run(() => GetReportFileStreamAsync(reportDocumentId, isRestrictedReport)).ConfigureAwait(false).GetAwaiter().GetResult();

        public async Task<MemoryStream> GetReportFileStreamAsync(string reportDocumentId, bool isRestrictedReport = false, CancellationToken cancellationToken = default)
        {
            var reportDocument = await GetReportDocumentAsync(reportDocumentId, isRestrictedReport, cancellationToken);

            return await GetFileStreamAsync(reportDocument, cancellationToken);
        }

        /// <summary>
        /// Downloads a report document to a temp file, never whole in memory (gzip is kept on disk and undone by
        /// <see cref="ReportDocumentFile.OpenRead"/>). Dispose the result to delete the file. Null when Amazon returns no
        /// document metadata.
        /// </summary>
        /// <exception cref="AmazonReportDocumentDownloadException">The pre-signed URL answered a non-success status.</exception>
        public ReportDocumentFile GetReportDocumentFile(string reportDocumentId, bool isRestrictedReport = false) =>
            Task.Run(() => GetReportDocumentFileAsync(reportDocumentId, isRestrictedReport)).ConfigureAwait(false).GetAwaiter().GetResult();

        public async Task<ReportDocumentFile> GetReportDocumentFileAsync(string reportDocumentId, bool isRestrictedReport = false, CancellationToken cancellationToken = default)
        {
            var reportDocument = await GetReportDocumentAsync(reportDocumentId, isRestrictedReport, cancellationToken);
            if (reportDocument == null)
                return null;
            if (reportDocument.EncryptionDetails == null)
                return await ReportDocumentFile.DownloadAsync(reportDocument, null, cancellationToken);

            // Encrypted documents (the retired 2020-09-04 Reports API; 2021-06-30 never encrypts) are decrypted in memory.
            using (var decrypted = await GetFileStreamAsync(reportDocument, cancellationToken))
            {
                return await ReportDocumentFile.FromStreamAsync(decrypted, false, cancellationToken);
            }
        }

        Task<ReportDocumentFile> IReportOperations.DownloadDocumentAsync(string reportDocumentId, bool isRestrictedReport, CancellationToken cancellationToken)
            => GetReportDocumentFileAsync(reportDocumentId, isRestrictedReport, cancellationToken);

        private string GetFile(ReportDocument reportDocument) =>
            Task.Run(() => GetFileAsync(reportDocument)).ConfigureAwait(false).GetAwaiter().GetResult();
        private async Task<string> GetFileAsync(ReportDocument reportDocument, CancellationToken cancellationToken = default)
        {
            bool isCompressionFile = false;
            bool isEncryptedFile = reportDocument.EncryptionDetails != null;

            if (reportDocument.CompressionAlgorithm is ReportDocument.CompressionAlgorithmEnum.GZIP)
                isCompressionFile = true;

            var client = new System.Net.WebClient();
            string fileName = Guid.NewGuid().ToString();

            if (isCompressionFile)
            {
                client.Headers[System.Net.HttpRequestHeader.AcceptEncoding] = "gzip";
                fileName += ".gz";
            }
            else fileName += ".txt";

            string tempFilePath = Path.Combine(Path.GetTempPath() + fileName);

            try
            {
                if (isEncryptedFile)
                {
                    //Later will check
                    byte[] rawData = client.DownloadData(reportDocument.Url);
                    byte[] key = Convert.FromBase64String(reportDocument.EncryptionDetails.Key);
                    byte[] iv = Convert.FromBase64String(reportDocument.EncryptionDetails.InitializationVector);
                    var reportData = FileTransform.DecryptString(key, iv, rawData);
                    File.WriteAllText(tempFilePath, reportData);
                }
                else
                {
                    var stream = await client.OpenReadTaskAsync(new Uri(reportDocument.Url));
                    using (Stream s = File.Create(tempFilePath))
                    {
                        stream?.CopyTo(s);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (isCompressionFile)
                {
                    var compressionFile = tempFilePath;
                    tempFilePath = FileTransform.Decompress(tempFilePath);
                    File.Delete(compressionFile);
                }

                cancellationToken.ThrowIfCancellationRequested();

                return tempFilePath;
            }
            catch (OperationCanceledException)
            {
                File.Delete(tempFilePath);
                throw;
            }
        }

        private MemoryStream GetFileStream(ReportDocument reportDocument) =>
            Task.Run(() => GetFileStreamAsync(reportDocument)).ConfigureAwait(false).GetAwaiter().GetResult();

        private async Task<MemoryStream> GetFileStreamAsync(ReportDocument reportDocument, CancellationToken cancellationToken = default)
        {
            bool isCompressionFile = false;
            bool isEncryptedFile = reportDocument.EncryptionDetails != null;

            if (reportDocument.CompressionAlgorithm is ReportDocument.CompressionAlgorithmEnum.GZIP)
                isCompressionFile = true;

            var client = new System.Net.WebClient();

            if (isCompressionFile)
                client.Headers[System.Net.HttpRequestHeader.AcceptEncoding] = "gzip";

            try
            {
                Stream stream;
                if (isEncryptedFile)
                {
                    byte[] rawData = client.DownloadData(reportDocument.Url);
                    byte[] key = Convert.FromBase64String(reportDocument.EncryptionDetails.Key);
                    byte[] iv = Convert.FromBase64String(reportDocument.EncryptionDetails.InitializationVector);
                    var reportData = FileTransform.DecryptString(key, iv, rawData);
                    stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(reportData));
                }
                else
                {
                    stream = await client.OpenReadTaskAsync(new Uri(reportDocument.Url));
                }

                if (isCompressionFile)
                    stream = new GZipStream(stream, CompressionMode.Decompress);

                var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream, 81920, cancellationToken);
                memoryStream.Position = 0;

                return memoryStream;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }

        public async Task SaveStreamToFileAsync(string fileFullPath, Stream stream, CancellationToken cancellationToken = default)
        {
            if (stream.Length == 0) return;

            // Create a FileStream object to write a stream to a file
            using (FileStream fileStream = System.IO.File.Create(fileFullPath, (int)stream.Length))
            {
                // Fill the bytes[] array with the stream data
                byte[] bytesInStream = new byte[stream.Length];
                await stream.ReadAsync(bytesInStream, 0, (int)bytesInStream.Length, cancellationToken);

                // Use FileStream object to write to the specified file
                fileStream.Write(bytesInStream, 0, bytesInStream.Length);
            }
        }
        public bool CancelReportSchedule(string reportScheduleId) =>
            Task.Run(() => CancelReportScheduleAsync(reportScheduleId)).ConfigureAwait(false).GetAwaiter().GetResult();
        public async Task<bool> CancelReportScheduleAsync(string reportScheduleId, CancellationToken cancellationToken = default)
        {
            await CreateAuthorizedRequestAsync(ReportApiUrls.CancelReportSchedule(reportScheduleId), RestSharp.Method.Delete, cancellationToken: cancellationToken);
            var response = await ExecuteRequestAsync<CancelReportScheduleResponse>(RateLimitType.Report_CancelReportSchedule, cancellationToken);
            if (response != null && response.Errors != null)
                return false;
            return true;
        }
        #endregion


        // Create → poll → download. Every overload decides on the report's processingStatus first (ReportLifecycle):
        // DONE with a document → download; DONE without one → AmazonReportFatalException; CANCELLED → null (Amazon had no
        // data for the window); FATAL → AmazonReportFatalException (the error document is never returned as data).
        // Polling backs off up to 30 s and gives up after ReportPollingOptions.MaxWait (default 2 h) with
        // AmazonReportTimeoutException. The int millisecondsDelay of the older overloads is the first delay of that backoff.

        public string CreateReportAndDownloadFile(ReportTypes reportType, DateTime? dataStartTime = null, DateTime? dataEndTime = null, ReportOptions? reportOptions = null, bool isRestrictedReport = false, List<MarketPlace>? marketplaces = null, int millisecondsDelay = 500) =>
            Task.Run(() => CreateReportAndDownloadFileAsync(reportType, dataStartTime, dataEndTime, reportOptions, isRestrictedReport, marketplaces, millisecondsDelay)).ConfigureAwait(false).GetAwaiter().GetResult();
        public Task<string> CreateReportAndDownloadFileAsync(ReportTypes reportType, DateTime? dataStartTime = null, DateTime? dataEndTime = null, ReportOptions? reportOptions = null, bool isRestrictedReport = false, List<MarketPlace>? marketplaces = null, int millisecondsDelay = 500, CancellationToken cancellationToken = default)
            => CreateReportAndDownloadFileAsync(reportType, dataStartTime, dataEndTime, reportOptions, isRestrictedReport, marketplaces, ReportPollingOptions.FromLegacyDelay(millisecondsDelay), cancellationToken);

        public string CreateReportAndDownloadFile(ReportTypes reportType, DateTime? dataStartTime, DateTime? dataEndTime, ReportOptions? reportOptions, bool isRestrictedReport, List<MarketPlace>? marketplaces, ReportPollingOptions polling) =>
            Task.Run(() => CreateReportAndDownloadFileAsync(reportType, dataStartTime, dataEndTime, reportOptions, isRestrictedReport, marketplaces, polling)).ConfigureAwait(false).GetAwaiter().GetResult();
        public async Task<string> CreateReportAndDownloadFileAsync(ReportTypes reportType, DateTime? dataStartTime, DateTime? dataEndTime, ReportOptions? reportOptions, bool isRestrictedReport, List<MarketPlace>? marketplaces, ReportPollingOptions polling, CancellationToken cancellationToken = default)
        {
            var parameters = BuildCreateReportParameters(reportType, dataStartTime, dataEndTime, reportOptions, marketplaces);
            isRestrictedReport = isRestrictedReport || IsRestrictedReportType(reportType);
            var reportId = await CreateReportAsync(parameters, cancellationToken);
            return await GetReportFileByReportIdAsync(reportId, isRestrictedReport, polling, cancellationToken);
        }

        public MemoryStream CreateReportAndDownloadFileStream(ReportTypes reportType, DateTime? dataStartTime = null, DateTime? dataEndTime = null, ReportOptions? reportOptions = null, bool isRestrictedReport = false, List<MarketPlace>? marketplaces = null, int millisecondsDelay = 500) =>
            Task.Run(() => CreateReportAndDownloadFileStreamAsync(reportType, dataStartTime, dataEndTime, reportOptions, isRestrictedReport, marketplaces, millisecondsDelay)).ConfigureAwait(false).GetAwaiter().GetResult();

        public Task<MemoryStream> CreateReportAndDownloadFileStreamAsync(ReportTypes reportType, DateTime? dataStartTime = null, DateTime? dataEndTime = null, ReportOptions? reportOptions = null, bool isRestrictedReport = false, List<MarketPlace>? marketplaces = null, int millisecondsDelay = 500, CancellationToken cancellationToken = default)
            => CreateReportAndDownloadFileStreamAsync(reportType, dataStartTime, dataEndTime, reportOptions, isRestrictedReport, marketplaces, ReportPollingOptions.FromLegacyDelay(millisecondsDelay), cancellationToken);

        public MemoryStream CreateReportAndDownloadFileStream(ReportTypes reportType, DateTime? dataStartTime, DateTime? dataEndTime, ReportOptions? reportOptions, bool isRestrictedReport, List<MarketPlace>? marketplaces, ReportPollingOptions polling) =>
            Task.Run(() => CreateReportAndDownloadFileStreamAsync(reportType, dataStartTime, dataEndTime, reportOptions, isRestrictedReport, marketplaces, polling)).ConfigureAwait(false).GetAwaiter().GetResult();

        /// <summary>Buffers the whole decompressed document in memory; prefer CreateReportAndDownloadDocumentFileAsync for large reports.</summary>
        public async Task<MemoryStream> CreateReportAndDownloadFileStreamAsync(ReportTypes reportType, DateTime? dataStartTime, DateTime? dataEndTime, ReportOptions? reportOptions, bool isRestrictedReport, List<MarketPlace>? marketplaces, ReportPollingOptions polling, CancellationToken cancellationToken = default)
        {
            var parameters = BuildCreateReportParameters(reportType, dataStartTime, dataEndTime, reportOptions, marketplaces);
            isRestrictedReport = isRestrictedReport || IsRestrictedReportType(reportType);
            var reportId = await CreateReportAsync(parameters, cancellationToken);
            return await GetReportFileStreamByReportIdAsync(reportId, isRestrictedReport, polling, cancellationToken);
        }

        /// <summary>
        /// Creates a report, waits for it and downloads its document to a temp file (never whole in memory). Null when
        /// Amazon CANCELLED the report (no data for the window). Dispose the result to delete the file.
        /// </summary>
        /// <exception cref="AmazonReportFatalException">The report ended FATAL, or DONE without a document.</exception>
        /// <exception cref="AmazonReportTimeoutException">Not finished within <see cref="ReportPollingOptions.MaxWait"/>.</exception>
        public ReportDocumentFile CreateReportAndDownloadDocumentFile(ReportTypes reportType, DateTime? dataStartTime = null, DateTime? dataEndTime = null, ReportOptions? reportOptions = null, bool isRestrictedReport = false, List<MarketPlace>? marketplaces = null, ReportPollingOptions? polling = null) =>
            Task.Run(() => CreateReportAndDownloadDocumentFileAsync(reportType, dataStartTime, dataEndTime, reportOptions, isRestrictedReport, marketplaces, polling)).ConfigureAwait(false).GetAwaiter().GetResult();

        public async Task<ReportDocumentFile> CreateReportAndDownloadDocumentFileAsync(ReportTypes reportType, DateTime? dataStartTime = null, DateTime? dataEndTime = null, ReportOptions? reportOptions = null, bool isRestrictedReport = false, List<MarketPlace>? marketplaces = null, ReportPollingOptions? polling = null, CancellationToken cancellationToken = default)
        {
            var result = await CreateReportAndDownloadDocumentFileAsync(
                BuildCreateReportParameters(reportType, dataStartTime, dataEndTime, reportOptions, marketplaces),
                isRestrictedReport, polling, cancellationToken);
            return result.Document;
        }

        /// <summary>
        /// Creates a report from a ready specification, waits for it and downloads its document to a temp file; returns
        /// the report id, final status (DONE / CANCELLED) and polling figures with the document (null when CANCELLED).
        /// When <c>marketplaceIds</c> is empty, the connection's marketplace is used. Restricted report types are detected.
        /// </summary>
        /// <exception cref="AmazonReportFatalException">The report ended FATAL, or DONE without a document.</exception>
        /// <exception cref="AmazonReportTimeoutException">Not finished within <see cref="ReportPollingOptions.MaxWait"/>.</exception>
        public Task<ReportFileResult> CreateReportAndDownloadDocumentFileAsync(ParameterCreateReportSpecification parameters, bool isRestrictedReport, ReportPollingOptions? polling = null, CancellationToken cancellationToken = default)
            => ReportLifecycle.CreateAndDownloadAsync(this, parameters, isRestrictedReport || IsRestrictedReportType(parameters.reportType), polling, cancellationToken);

        public Task<string> GetReportFileByReportIdAsync(string reportId, bool isRestrictedReport, int millisecondsDelay = 500, CancellationToken cancellationToken = default)
            => GetReportFileByReportIdAsync(reportId, isRestrictedReport, ReportPollingOptions.FromLegacyDelay(millisecondsDelay), cancellationToken);

        /// <summary>Waits for an existing report and downloads it to a file path. Null when CANCELLED.</summary>
        public async Task<string> GetReportFileByReportIdAsync(string reportId, bool isRestrictedReport, ReportPollingOptions polling, CancellationToken cancellationToken = default)
        {
            var poll = await ReportLifecycle.WaitForTerminalStatusAsync(this, reportId, polling, cancellationToken);
            return await ReportLifecycle.DownloadTerminalAsync(this, poll.Report, null, isRestrictedReport,
                documentId => GetReportFileAsync(documentId, isRestrictedReport, cancellationToken), cancellationToken);
        }

        public Task<MemoryStream> GetReportFileStreamByReportIdAsync(string reportId, bool isRestrictedReport, int millisecondsDelay = 500, CancellationToken cancellationToken = default)
            => GetReportFileStreamByReportIdAsync(reportId, isRestrictedReport, ReportPollingOptions.FromLegacyDelay(millisecondsDelay), cancellationToken);

        /// <summary>Waits for an existing report and downloads it into memory. Null when CANCELLED.</summary>
        public async Task<MemoryStream> GetReportFileStreamByReportIdAsync(string reportId, bool isRestrictedReport, ReportPollingOptions polling, CancellationToken cancellationToken = default)
        {
            var poll = await ReportLifecycle.WaitForTerminalStatusAsync(this, reportId, polling, cancellationToken);
            return await ReportLifecycle.DownloadTerminalAsync(this, poll.Report, null, isRestrictedReport,
                documentId => GetReportFileStreamAsync(documentId, isRestrictedReport, cancellationToken), cancellationToken);
        }

        /// <summary>Waits for an existing report and downloads it to a temp file (never whole in memory). Null when CANCELLED.</summary>
        public async Task<ReportDocumentFile> GetReportDocumentFileByReportIdAsync(string reportId, bool isRestrictedReport, ReportPollingOptions? polling = null, CancellationToken cancellationToken = default)
        {
            var poll = await ReportLifecycle.WaitForTerminalStatusAsync(this, reportId, polling, cancellationToken);
            return await ReportLifecycle.DownloadTerminalAsync(this, poll.Report, null, isRestrictedReport,
                documentId => GetReportDocumentFileAsync(documentId, isRestrictedReport, cancellationToken), cancellationToken);
        }

        private static bool IsRestrictedReportType(ReportTypes? reportType)
            => reportType.HasValue && Enum.TryParse<RestrictedReportTypes>(reportType.Value.ToString(), out _);

        private ParameterCreateReportSpecification BuildCreateReportParameters(ReportTypes reportType, DateTime? dataStartTime, DateTime? dataEndTime, ReportOptions? reportOptions, List<MarketPlace>? marketplaces)
        {
            var parameters = new ParameterCreateReportSpecification();
            parameters.reportType = reportType;

            parameters.marketplaceIds = new MarketplaceIds();

            if (marketplaces == null || !marketplaces.Any())
            {
                parameters.marketplaceIds.Add(AmazonCredential.MarketPlace.ID);
            }
            else
            {
                parameters.marketplaceIds.AddRange(marketplaces.Select(x => x.ID).ToList());
            }

            if (reportOptions != null)
                parameters.reportOptions = reportOptions;

            if (dataStartTime.HasValue)
                parameters.dataStartTime = dataStartTime;
            if (dataEndTime.HasValue)
                parameters.dataEndTime = dataEndTime;
            return parameters;
        }

        public IList<string> DownloadExistingReportAndDownloadFile(ReportTypes reportTypes, DateTime? createdSince = null, DateTime? createdUntil = null) =>
            Task.Run(() => DownloadExistingReportAndDownloadFileAsync(reportTypes, createdSince, createdUntil)).ConfigureAwait(false).GetAwaiter().GetResult();
        public async Task<IList<string>> DownloadExistingReportAndDownloadFileAsync(ReportTypes reportTypes, DateTime? createdSince = null, DateTime? createdUntil = null, CancellationToken cancellationToken = default)
        {
            var parameters = new ParameterReportList();
            parameters.reportTypes = new List<ReportTypes>();
            parameters.reportTypes.Add(reportTypes);

            parameters.marketplaceIds = new MarketplaceIds();
            parameters.marketplaceIds.Add(AmazonCredential.MarketPlace.ID);


            if (createdSince.HasValue)
                parameters.createdSince = createdSince;
            if (createdUntil.HasValue)
                parameters.createdUntil = createdUntil;

            var reports = await GetReportsAsync(parameters, cancellationToken);

            var reportsPath = new List<string>();

            if (reports != null)
            {
                foreach (var reportData in reports)
                {
                    if (!string.IsNullOrEmpty(reportData.ReportDocumentId))
                    {
                        var filePath = await GetReportFileAsync(reportData.ReportDocumentId, cancellationToken: cancellationToken);
                        reportsPath.Add(filePath);

                    }
                }
            }

            return reportsPath;
        }

        public IList<MemoryStream> DownloadExistingReportAndDownloadFileStream(ReportTypes reportTypes, DateTime? createdSince = null, DateTime? createdUntil = null) =>
            Task.Run(() => DownloadExistingReportAndDownloadFileStreamAsync(reportTypes, createdSince, createdUntil)).ConfigureAwait(false).GetAwaiter().GetResult();

        public async Task<IList<MemoryStream>> DownloadExistingReportAndDownloadFileStreamAsync(ReportTypes reportTypes, DateTime? createdSince = null, DateTime? createdUntil = null, CancellationToken cancellationToken = default)
        {
            var parameters = new ParameterReportList();
            parameters.reportTypes = new List<ReportTypes>();
            parameters.reportTypes.Add(reportTypes);

            parameters.marketplaceIds = new MarketplaceIds();
            parameters.marketplaceIds.Add(AmazonCredential.MarketPlace.ID);

            if (createdSince.HasValue)
                parameters.createdSince = createdSince;
            if (createdUntil.HasValue)
                parameters.createdUntil = createdUntil;

            var reports = await GetReportsAsync(parameters, cancellationToken);

            var reportsStreams = new List<MemoryStream>();

            if (reports != null)
            {
                foreach (var reportData in reports)
                {
                    if (!string.IsNullOrEmpty(reportData.ReportDocumentId))
                    {
                        var stream = await GetReportFileStreamAsync(reportData.ReportDocumentId, cancellationToken: cancellationToken);
                        reportsStreams.Add(stream);

                    }
                }
            }

            return reportsStreams;
        }
    }
}
