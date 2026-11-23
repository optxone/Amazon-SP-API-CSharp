using System;

namespace FikaAmazonAPI.AmazonSpApiSDK.Models.Exceptions
{
    // These derive from Exception / TimeoutException on purpose, not from AmazonException: they are not an SP-API error
    // response (no RestResponse), and callers that match AmazonException messages for "401" / "Unauthorized" must not
    // mistake a report id or an excerpt of Amazon's error document for an authorization failure.

    /// <summary>
    /// An on-demand report reached a terminal status without usable data: <c>FATAL</c>, or <c>DONE</c> without a report
    /// document id. Amazon attaches an error document to a FATAL report; it is never returned as the report's data. When it
    /// can be read, its start is in <see cref="ErrorDetail"/> (and the message).
    /// </summary>
    public class AmazonReportFatalException : Exception
    {
        /// <summary>Longest excerpt of Amazon's error document kept in <see cref="ErrorDetail"/>.</summary>
        public const int MaxErrorDetailLength = 500;

        public AmazonReportFatalException(string reportType, string reportId, string status, string? errorDetail)
            : base($"Amazon report {reportType} (reportId={reportId}) ended {status}" +
                   (string.IsNullOrWhiteSpace(errorDetail) ? "." : $": {errorDetail}"))
        {
            ReportType = reportType;
            ReportId = reportId;
            Status = status;
            ErrorDetail = errorDetail;
        }

        /// <summary>The report type, e.g. <c>GET_LEDGER_SUMMARY_VIEW_DATA</c>.</summary>
        public string ReportType { get; }

        public string ReportId { get; }

        /// <summary><c>FATAL</c>, or <c>DONE</c> when Amazon finished the report without a document.</summary>
        public string Status { get; }

        /// <summary>The start of Amazon's error document, when it could be read; otherwise null.</summary>
        public string? ErrorDetail { get; }
    }

    /// <summary>
    /// A report was still <c>IN_QUEUE</c> / <c>IN_PROGRESS</c> after <see cref="Services.ReportPollingOptions.MaxWait"/>.
    /// Usually transient: ask again later.
    /// </summary>
    public class AmazonReportTimeoutException : TimeoutException
    {
        public AmazonReportTimeoutException(string reportId, string lastStatus, TimeSpan waited)
            : base($"Amazon report {reportId} still {lastStatus} after {(int)waited.TotalMinutes} min")
        {
            ReportId = reportId;
            LastStatus = lastStatus;
            Waited = waited;
        }

        public string ReportId { get; }
        public string LastStatus { get; }

        /// <summary>Time spent waiting between polls.</summary>
        public TimeSpan Waited { get; }
    }

    /// <summary>
    /// The pre-signed URL of a report document answered a non-success HTTP status (an expired link, an S3 error). It is
    /// the storage link, not the seller's authorization: ask for a fresh document (or report) later.
    /// </summary>
    public class AmazonReportDocumentDownloadException : Exception
    {
        public AmazonReportDocumentDownloadException(string? reportDocumentId, int httpStatus)
            : base($"Report document {reportDocumentId} download answered HTTP {httpStatus}.")
        {
            ReportDocumentId = reportDocumentId;
            HttpStatus = httpStatus;
        }

        public string? ReportDocumentId { get; }
        public int HttpStatus { get; }
    }
}
