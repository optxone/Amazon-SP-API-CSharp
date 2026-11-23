using FikaAmazonAPI.AmazonSpApiSDK.Models.Exceptions;
using FikaAmazonAPI.AmazonSpApiSDK.Models.Reports;
using FikaAmazonAPI.Parameter.Report;
using FikaAmazonAPI.Utils;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FikaAmazonAPI.Services
{
    /// <summary>Polling cadence and limit for an on-demand report (create → poll → download).</summary>
    public sealed class ReportPollingOptions
    {
        /// <summary>2 s first delay, doubling to 30 s, at most 2 h of waiting.</summary>
        public static ReportPollingOptions Default => new ReportPollingOptions();

        /// <summary>Delay before the second poll; doubled after every poll up to <see cref="MaxDelay"/>.</summary>
        public TimeSpan FirstDelay { get; set; } = TimeSpan.FromSeconds(2);

        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// A report still <c>IN_QUEUE</c> / <c>IN_PROGRESS</c> after this much waiting fails with
        /// <see cref="AmazonReportTimeoutException"/> (a <see cref="TimeoutException"/>) instead of polling forever.
        /// </summary>
        public TimeSpan MaxWait { get; set; } = TimeSpan.FromHours(2);

        /// <summary>How a delay is waited (tests replace it). Default <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
        public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (delay, ct) => Task.Delay(delay, ct);

        /// <summary>The options the pre-1.9.33.2 overloads map their <c>millisecondsDelay</c> to: it is the first delay of the backoff.</summary>
        internal static ReportPollingOptions FromLegacyDelay(int millisecondsDelay) => new ReportPollingOptions
        {
            FirstDelay = TimeSpan.FromMilliseconds(Math.Max(1, millisecondsDelay)),
        };
    }

    /// <summary>
    /// The three Reports calls an on-demand report needs. <see cref="ReportService"/> implements it; tests (here and in
    /// consumers) fake it to drive <see cref="ReportLifecycle"/> without HTTP.
    /// </summary>
    public interface IReportOperations
    {
        Task<string> CreateReportAsync(ParameterCreateReportSpecification createReportSpecification, CancellationToken cancellationToken);

        Task<Report> GetReportAsync(string reportId, CancellationToken cancellationToken);

        /// <summary>Downloads the report document to a temp file (never whole in memory). Dispose the result to delete it.</summary>
        Task<ReportDocumentFile?> DownloadDocumentAsync(string reportDocumentId, bool isRestrictedReport, CancellationToken cancellationToken);
    }

    /// <summary>A report that reached a terminal status (see <see cref="ReportLifecycle.WaitForTerminalStatusAsync"/>).</summary>
    public sealed class ReportPollResult
    {
        public ReportPollResult(Report report, int pollCount, TimeSpan waited)
        {
            Report = report;
            PollCount = pollCount;
            Waited = waited;
        }

        /// <summary>The last <c>getReport</c> answer: DONE, CANCELLED or FATAL.</summary>
        public Report Report { get; }

        public int PollCount { get; }

        /// <summary>Time spent in delays between polls.</summary>
        public TimeSpan Waited { get; }
    }

    /// <summary>The outcome of <see cref="ReportLifecycle.CreateAndDownloadAsync"/>.</summary>
    public sealed class ReportFileResult
    {
        public ReportFileResult(string reportId, Report.ProcessingStatusEnum status, ReportDocumentFile? document, int pollCount, TimeSpan waited)
        {
            ReportId = reportId;
            Status = status;
            Document = document;
            PollCount = pollCount;
            Waited = waited;
        }

        public string ReportId { get; }

        /// <summary>DONE (with <see cref="Document"/>) or CANCELLED (no document).</summary>
        public Report.ProcessingStatusEnum Status { get; }

        /// <summary>The downloaded document; null when the report was CANCELLED. The caller disposes it.</summary>
        public ReportDocumentFile? Document { get; }

        public int PollCount { get; }
        public TimeSpan Waited { get; }
    }

    /// <summary>
    /// Create → poll → download for an on-demand report, deciding on the report's <c>processingStatus</c> first:
    /// <list type="bullet">
    ///   <item><description><c>DONE</c> with a document id: the document is downloaded.</description></item>
    ///   <item><description><c>DONE</c> without a document id: <see cref="AmazonReportFatalException"/> (Status DONE).</description></item>
    ///   <item><description><c>CANCELLED</c>: no document (null). Amazon cancels a report when there is no data to return
    ///   (or for a repeat request inside its regeneration period): an empty window, not a failure.</description></item>
    ///   <item><description><c>FATAL</c>: <see cref="AmazonReportFatalException"/> with the start of Amazon's error document
    ///   when readable. The error document (FATAL reports carry one) is never returned as the report's data.</description></item>
    /// </list>
    /// Polling backs off (<see cref="ReportPollingOptions"/>) and gives up after <see cref="ReportPollingOptions.MaxWait"/>
    /// with <see cref="AmazonReportTimeoutException"/>.
    /// </summary>
    public static class ReportLifecycle
    {
        /// <summary>Creates the report, waits for a terminal status and downloads its document to a temp file.</summary>
        public static async Task<ReportFileResult> CreateAndDownloadAsync(IReportOperations api,
            ParameterCreateReportSpecification parameters, bool isRestrictedReport, ReportPollingOptions? polling = null,
            CancellationToken cancellationToken = default)
        {
            if (api == null) throw new ArgumentNullException(nameof(api));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));

            var reportId = await api.CreateReportAsync(parameters, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(reportId))
                throw new InvalidOperationException($"createReport for {parameters.reportType} returned no report id.");

            var poll = await WaitForTerminalStatusAsync(api, reportId, polling, cancellationToken).ConfigureAwait(false);
            var document = await DownloadTerminalAsync(api, poll.Report, parameters.reportType.ToString(), isRestrictedReport,
                id => api.DownloadDocumentAsync(id, isRestrictedReport, cancellationToken), cancellationToken).ConfigureAwait(false);
            return new ReportFileResult(reportId, poll.Report.ProcessingStatus, document, poll.PollCount, poll.Waited);
        }

        /// <summary>
        /// Polls <c>getReport</c> until DONE / CANCELLED / FATAL, backing off from <see cref="ReportPollingOptions.FirstDelay"/>
        /// to <see cref="ReportPollingOptions.MaxDelay"/>.
        /// </summary>
        /// <exception cref="AmazonReportTimeoutException">Not terminal within <see cref="ReportPollingOptions.MaxWait"/>.</exception>
        public static async Task<ReportPollResult> WaitForTerminalStatusAsync(IReportOperations api, string reportId,
            ReportPollingOptions? polling = null, CancellationToken cancellationToken = default)
        {
            if (api == null) throw new ArgumentNullException(nameof(api));
            polling = polling ?? ReportPollingOptions.Default;

            var waited = TimeSpan.Zero;
            var delay = polling.FirstDelay;
            var polls = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var report = await api.GetReportAsync(reportId, cancellationToken).ConfigureAwait(false);
                polls++;
                if (report == null)
                    throw new InvalidOperationException($"getReport for report {reportId} returned no report.");
                if (IsTerminal(report.ProcessingStatus))
                    return new ReportPollResult(report, polls, waited);

                if (waited + delay > polling.MaxWait)
                    throw new AmazonReportTimeoutException(reportId, report.ProcessingStatus.ToString(), waited);

                await polling.Delay(delay, cancellationToken).ConfigureAwait(false);
                waited += delay;
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, Math.Max(polling.MaxDelay.Ticks, 1)));
            }
        }

        internal static bool IsTerminal(Report.ProcessingStatusEnum status)
            => status == Report.ProcessingStatusEnum.DONE
               || status == Report.ProcessingStatusEnum.CANCELLED
               || status == Report.ProcessingStatusEnum.FATAL;

        /// <summary>
        /// The decision on a terminal report: download (DONE with a document), null (CANCELLED) or
        /// <see cref="AmazonReportFatalException"/> (FATAL; DONE without a document). Shared by every download shape
        /// (temp file, file path, MemoryStream).
        /// </summary>
        internal static async Task<T?> DownloadTerminalAsync<T>(IReportOperations api, Report report, string? reportType,
            bool isRestrictedReport, Func<string, Task<T>> download, CancellationToken cancellationToken) where T : class
        {
            var type = string.IsNullOrEmpty(reportType) ? report.ReportType : reportType!;
            switch (report.ProcessingStatus)
            {
                case Report.ProcessingStatusEnum.DONE when !string.IsNullOrEmpty(report.ReportDocumentId):
                    return await download(report.ReportDocumentId).ConfigureAwait(false);
                case Report.ProcessingStatusEnum.DONE:
                    throw new AmazonReportFatalException(type, report.ReportId, "DONE", "no report document id");
                case Report.ProcessingStatusEnum.CANCELLED:
                    return null;
                case Report.ProcessingStatusEnum.FATAL:
                    var detail = await TryReadErrorDocumentAsync(api, report.ReportDocumentId, isRestrictedReport, cancellationToken).ConfigureAwait(false);
                    throw new AmazonReportFatalException(type, report.ReportId, "FATAL", detail);
                default:
                    throw new InvalidOperationException($"Report {report.ReportId} is not terminal ({report.ProcessingStatus}).");
            }
        }

        /// <summary>The start of a FATAL report's error document, best effort (null when there is none or it cannot be read).</summary>
        private static async Task<string?> TryReadErrorDocumentAsync(IReportOperations api, string? reportDocumentId,
            bool isRestrictedReport, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(reportDocumentId)) return null;
            try
            {
                using (var document = await api.DownloadDocumentAsync(reportDocumentId!, isRestrictedReport, cancellationToken).ConfigureAwait(false))
                {
                    if (document == null) return null;
                    using (var reader = new StreamReader(document.OpenRead(), Encoding.UTF8))
                    {
                        // An error document is small; read only what the message keeps.
                        var max = AmazonReportFatalException.MaxErrorDetailLength;
                        var buffer = new char[max + 1];
                        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                        var text = new string(buffer, 0, read).Trim();
                        return read > max ? text.Substring(0, Math.Min(text.Length, max)) + "…" : text;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return null; // the FATAL status is what matters; the detail is best effort
            }
        }
    }
}
