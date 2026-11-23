using FikaAmazonAPI.AmazonSpApiSDK.Models.Exceptions;
using FikaAmazonAPI.AmazonSpApiSDK.Models.Reports;
using FikaAmazonAPI.Parameter.Report;
using FikaAmazonAPI.Services;
using FikaAmazonAPI.Utils;
using System.Text;
using Status = FikaAmazonAPI.AmazonSpApiSDK.Models.Reports.Report.ProcessingStatusEnum;
using ReportTypes = FikaAmazonAPI.Utils.Constants.ReportTypes;

namespace Tests
{
    /// <summary>
    /// Create → poll → download decides on the report's processingStatus first. A FATAL report carries an error document;
    /// reading the document id before the status downloaded that error document as if it were the report (0 rows, no error).
    /// </summary>
    [TestFixture]
    public class ReportLifecycleTests
    {
        private static ReportPollingOptions Fast(List<TimeSpan>? waits = null, TimeSpan? maxWait = null) => new ReportPollingOptions
        {
            FirstDelay = TimeSpan.FromSeconds(2),
            MaxDelay = TimeSpan.FromSeconds(30),
            MaxWait = maxWait ?? TimeSpan.FromHours(2),
            Delay = (d, _) => { waits?.Add(d); return Task.CompletedTask; },
        };

        private static ParameterCreateReportSpecification Spec() => new ParameterCreateReportSpecification
        {
            reportType = ReportTypes.GET_AMAZON_FULFILLED_SHIPMENTS_DATA_GENERAL,
            marketplaceIds = new MarketplaceIds { "ATVPDKIKX0DER" },
        };

        [Test]
        public async Task Done_DownloadsTheDocument_AndReportsIdStatusAndPolls()
        {
            var api = new FakeReportOperations(Status.INQUEUE, Status.INPROGRESS, Status.DONE) { DocumentText = "h1\th2\nv1\tv2\n" };

            var result = await ReportLifecycle.CreateAndDownloadAsync(api, Spec(), false, Fast());

            Assert.That(result.Status, Is.EqualTo(Status.DONE));
            Assert.That(result.ReportId, Is.EqualTo("r-1"));
            Assert.That(result.PollCount, Is.EqualTo(3));
            Assert.That(result.Waited, Is.EqualTo(TimeSpan.FromSeconds(6)));
            using (result.Document)
            using (var reader = new StreamReader(result.Document!.OpenRead(), Encoding.UTF8))
                Assert.That(await reader.ReadToEndAsync(), Is.EqualTo("h1\th2\nv1\tv2\n"));
            Assert.That(api.Created!.marketplaceIds, Is.EqualTo(new[] { "ATVPDKIKX0DER" }));
        }

        [Test]
        public void Fatal_WithAnErrorDocument_ThrowsTyped_QuotingTheErrorDocument_NeverReturningItAsData()
        {
            var api = new FakeReportOperations(Status.INPROGRESS, Status.FATAL) { DocumentText = "Report processing failed (injected)." };

            var ex = Assert.ThrowsAsync<AmazonReportFatalException>(() => ReportLifecycle.CreateAndDownloadAsync(api, Spec(), false, Fast()));

            Assert.That(ex!.Status, Is.EqualTo("FATAL"));
            Assert.That(ex.ReportId, Is.EqualTo("r-1"));
            Assert.That(ex.ReportType, Is.EqualTo(nameof(ReportTypes.GET_AMAZON_FULFILLED_SHIPMENTS_DATA_GENERAL)));
            Assert.That(ex.ErrorDetail, Is.EqualTo("Report processing failed (injected)."));
            Assert.That(ex.Message, Does.Contain("Report processing failed (injected)."));
            Assert.That(api.Downloads, Is.EqualTo(1), "only to read the error detail");
            Assert.That(api.OpenFiles.All(f => !File.Exists(f)), Is.True, "the error document's temp file is deleted");
        }

        [Test]
        public void Fatal_WithALongErrorDocument_KeepsOnlyItsStart()
        {
            var api = new FakeReportOperations(Status.FATAL) { DocumentText = new string('x', 2000) };

            var ex = Assert.ThrowsAsync<AmazonReportFatalException>(() => ReportLifecycle.CreateAndDownloadAsync(api, Spec(), false, Fast()));

            Assert.That(ex!.ErrorDetail, Is.EqualTo(new string('x', AmazonReportFatalException.MaxErrorDetailLength) + "…"));
        }

        [Test]
        public void Fatal_WhenTheErrorDocumentCannotBeRead_StillThrowsTheFatal()
        {
            var api = new FakeReportOperations(Status.FATAL) { FailDownloads = true };

            var ex = Assert.ThrowsAsync<AmazonReportFatalException>(() => ReportLifecycle.CreateAndDownloadAsync(api, Spec(), false, Fast()));

            Assert.That(ex!.ErrorDetail, Is.Null);
        }

        [Test]
        public void DoneWithoutADocument_Throws_InsteadOfPollingForever()
        {
            var api = new FakeReportOperations(Status.DONE) { DocumentOnDone = false };

            var ex = Assert.ThrowsAsync<AmazonReportFatalException>(() => ReportLifecycle.CreateAndDownloadAsync(api, Spec(), false, Fast()));

            Assert.That(ex!.Status, Is.EqualTo("DONE"));
            Assert.That(api.Downloads, Is.EqualTo(0));
        }

        [Test]
        public async Task Cancelled_IsAnEmptyWindow_NotAFailure()
        {
            var api = new FakeReportOperations(Status.INPROGRESS, Status.CANCELLED);

            var result = await ReportLifecycle.CreateAndDownloadAsync(api, Spec(), false, Fast());

            Assert.That(result.Document, Is.Null);
            Assert.That(result.Status, Is.EqualTo(Status.CANCELLED));
            Assert.That(result.ReportId, Is.EqualTo("r-1"));
            Assert.That(api.Downloads, Is.EqualTo(0));
        }

        [Test]
        public void StillInProgressAfterMaxWait_ThrowsTimeout_WithBackoff()
        {
            var waits = new List<TimeSpan>();
            var api = new FakeReportOperations(Status.INPROGRESS) { RepeatLast = true };

            var ex = Assert.ThrowsAsync<AmazonReportTimeoutException>(() =>
                ReportLifecycle.CreateAndDownloadAsync(api, Spec(), false, Fast(waits, TimeSpan.FromMinutes(5))));

            Assert.That(ex, Is.InstanceOf<TimeoutException>());
            Assert.That(ex!.ReportId, Is.EqualTo("r-1"));
            Assert.That(ex.LastStatus, Is.EqualTo("INPROGRESS"));
            Assert.That(waits.Take(5), Is.EqualTo(new[] { 2, 4, 8, 16, 30 }.Select(s => TimeSpan.FromSeconds(s))));
            Assert.That(waits.Skip(4), Is.All.EqualTo(TimeSpan.FromSeconds(30)), "capped at MaxDelay");
            Assert.That(waits.Aggregate(TimeSpan.Zero, (a, b) => a + b), Is.LessThanOrEqualTo(TimeSpan.FromMinutes(5)));
        }

        [Test]
        public void Cancellation_StopsPolling()
        {
            using var cts = new CancellationTokenSource();
            var api = new FakeReportOperations(Status.INPROGRESS) { RepeatLast = true };
            var polling = Fast();
            polling.Delay = (_, ct) => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };

            Assert.CatchAsync<OperationCanceledException>(() => ReportLifecycle.CreateAndDownloadAsync(api, Spec(), false, polling, cts.Token));
        }

        [Test]
        public async Task LegacyShapes_DecideOnStatusFirst_FatalNeverCallsTheDataDownload()
        {
            // GetReportFileByReportIdAsync / GetReportFileStreamByReportIdAsync use the same decision with their own download.
            var api = new FakeReportOperations(Status.FATAL);
            var report = await api.GetReportAsync("r-1", default);
            var dataDownloads = 0;

            Assert.ThrowsAsync<AmazonReportFatalException>(() => ReportLifecycle.DownloadTerminalAsync(api, report, null, false,
                _ => { dataDownloads++; return Task.FromResult(new MemoryStream()); }, default));
            Assert.That(dataDownloads, Is.EqualTo(0));

            var cancelled = new Report { ReportId = "r-2", ReportType = "X", ProcessingStatus = Status.CANCELLED };
            var none = await ReportLifecycle.DownloadTerminalAsync(api, cancelled, null, false,
                _ => { dataDownloads++; return Task.FromResult(new MemoryStream()); }, default);
            Assert.That(none, Is.Null);
            Assert.That(dataDownloads, Is.EqualTo(0));
        }

        [Test]
        public void LegacyMillisecondsDelay_IsTheFirstDelayOfTheBackoff()
        {
            var options = ReportPollingOptions.FromLegacyDelay(500);

            Assert.That(options.FirstDelay, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
            Assert.That(options.MaxDelay, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(options.MaxWait, Is.EqualTo(TimeSpan.FromHours(2)));
        }

        private sealed class FakeReportOperations : IReportOperations
        {
            private readonly Queue<Status> _statuses;
            private Status _last;

            public FakeReportOperations(params Status[] statuses) => _statuses = new Queue<Status>(statuses);

            public bool RepeatLast { get; init; }
            public bool DocumentOnDone { get; init; } = true;
            public bool FailDownloads { get; init; }
            public string DocumentText { get; init; } = "";
            public int Downloads { get; private set; }
            public List<string> OpenFiles { get; } = new();
            public ParameterCreateReportSpecification? Created { get; private set; }

            public Task<string> CreateReportAsync(ParameterCreateReportSpecification createReportSpecification, CancellationToken cancellationToken)
            {
                Created = createReportSpecification;
                return Task.FromResult("r-1");
            }

            public Task<Report> GetReportAsync(string reportId, CancellationToken cancellationToken)
            {
                if (_statuses.Count > 0) _last = _statuses.Dequeue();
                else if (!RepeatLast) throw new InvalidOperationException("polled after a terminal status");
                // Amazon attaches a document to FATAL reports too.
                var hasDocument = _last == Status.FATAL || (_last == Status.DONE && DocumentOnDone);
                return Task.FromResult(new Report
                {
                    ReportId = reportId, ReportType = "GET_AMAZON_FULFILLED_SHIPMENTS_DATA_GENERAL",
                    ProcessingStatus = _last, ReportDocumentId = hasDocument ? "doc-1" : null!,
                });
            }

            public async Task<ReportDocumentFile?> DownloadDocumentAsync(string reportDocumentId, bool isRestrictedReport, CancellationToken cancellationToken)
            {
                Downloads++;
                if (FailDownloads) throw new AmazonReportDocumentDownloadException(reportDocumentId, 403);
                using var content = new MemoryStream(Encoding.UTF8.GetBytes(DocumentText));
                var file = await ReportDocumentFile.FromStreamAsync(content, false, cancellationToken);
                OpenFiles.Add(file.Path);
                return file;
            }
        }
    }
}
