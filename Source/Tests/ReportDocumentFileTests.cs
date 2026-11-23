using FikaAmazonAPI.AmazonSpApiSDK.Models.Exceptions;
using FikaAmazonAPI.AmazonSpApiSDK.Models.Reports;
using FikaAmazonAPI.Utils;
using System.IO.Compression;
using System.Net;
using System.Text;

namespace Tests
{
    /// <summary>A report document is streamed to a temp file (gzip kept on disk) and decompressed while it is read.</summary>
    [TestFixture]
    public class ReportDocumentFileTests
    {
        private const string Content = "Date\tSKU\tQuantity\n2026-11-01\tSKU-1\t3\n2026-11-02\tSKU-2\t5\n";

        [Test]
        public async Task GzipDocument_IsStoredCompressed_AndDecompressedOnRead_ThenDeletedOnDispose()
        {
            var gz = Gzip(Content);
            using var client = new HttpClient(new BytesHandler(gz));

            var doc = await ReportDocumentFile.DownloadAsync(new ReportDocument
            {
                ReportDocumentId = "D1", Url = "https://s3.example/D1", CompressionAlgorithm = ReportDocument.CompressionAlgorithmEnum.GZIP,
            }, client);

            Assert.That(doc.IsGzip, Is.True);
            Assert.That(doc.Length, Is.EqualTo(gz.Length), "kept as sent");
            using (var reader = new StreamReader(doc.OpenRead(), Encoding.UTF8))
                Assert.That(await reader.ReadToEndAsync(), Is.EqualTo(Content));

            var path = doc.Path;
            doc.Dispose();
            Assert.That(File.Exists(path), Is.False);
            doc.Dispose(); // idempotent
        }

        [Test]
        public async Task ContentEncodingGzip_IsDetected_WithoutCompressionAlgorithm()
        {
            using var client = new HttpClient(new BytesHandler(Gzip(Content), contentEncoding: "gzip"));

            using var doc = await ReportDocumentFile.DownloadAsync(new ReportDocument { ReportDocumentId = "D", Url = "https://s3.example/D" }, client);

            Assert.That(doc.IsGzip, Is.True);
            using var reader = new StreamReader(doc.OpenRead(), Encoding.UTF8);
            Assert.That(await reader.ReadToEndAsync(), Is.EqualTo(Content));
        }

        [Test]
        public async Task PlainDocument_IsStoredAsSent()
        {
            var bytes = Encoding.UTF8.GetBytes(Content);
            using var client = new HttpClient(new BytesHandler(bytes));

            using var doc = await ReportDocumentFile.DownloadAsync(new ReportDocument { ReportDocumentId = "D", Url = "https://s3.example/D" }, client);

            Assert.That(doc.IsGzip, Is.False);
            Assert.That(doc.Length, Is.EqualTo(bytes.Length));
            using var reader = new StreamReader(doc.OpenRead(), Encoding.UTF8);
            Assert.That(await reader.ReadToEndAsync(), Is.EqualTo(Content));
        }

        [Test]
        public void ExpiredUrl_ThrowsTypedDownloadException_AndLeavesNoFile()
        {
            using var client = new HttpClient(new BytesHandler(Array.Empty<byte>(), HttpStatusCode.Forbidden));
            var before = Directory.Exists(ReportDocumentFile.Directory) ? Directory.GetFiles(ReportDocumentFile.Directory).Length : 0;

            var ex = Assert.ThrowsAsync<AmazonReportDocumentDownloadException>(() =>
                ReportDocumentFile.DownloadAsync(new ReportDocument { ReportDocumentId = "D", Url = "https://s3.example/D" }, client));

            Assert.That(ex!.HttpStatus, Is.EqualTo(403));
            Assert.That(ex.ReportDocumentId, Is.EqualTo("D"));
            Assert.That(ex, Is.Not.InstanceOf<AmazonException>(), "not an SP-API (authorization) error");
            Assert.That(Directory.GetFiles(ReportDocumentFile.Directory).Length, Is.LessThanOrEqualTo(before));
        }

        [Test]
        public void EncryptedDocument_IsRefused_ByTheStreamingDownload()
        {
            Assert.ThrowsAsync<NotSupportedException>(() => ReportDocumentFile.DownloadAsync(new ReportDocument
            {
                ReportDocumentId = "D", Url = "https://s3.example/D",
                EncryptionDetails = new ReportDocumentEncryptionDetails(),
            }));
        }

        [Test]
        public async Task FromStream_WritesFromTheStart()
        {
            using var content = new MemoryStream(Encoding.UTF8.GetBytes(Content));
            content.Position = 10;

            using var doc = await ReportDocumentFile.FromStreamAsync(content);

            using var reader = new StreamReader(doc.OpenRead(), Encoding.UTF8);
            Assert.That(await reader.ReadToEndAsync(), Is.EqualTo(Content));
        }

        private static byte[] Gzip(string text)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                gz.Write(bytes, 0, bytes.Length);
            }
            return ms.ToArray();
        }

        private sealed class BytesHandler : HttpMessageHandler
        {
            private readonly byte[] _body;
            private readonly HttpStatusCode _status;
            private readonly string? _contentEncoding;

            public BytesHandler(byte[] body, HttpStatusCode status = HttpStatusCode.OK, string? contentEncoding = null)
            {
                _body = body;
                _status = status;
                _contentEncoding = contentEncoding;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(_status) { Content = new ByteArrayContent(_body) };
                if (_contentEncoding != null) response.Content.Headers.ContentEncoding.Add(_contentEncoding);
                return Task.FromResult(response);
            }
        }
    }
}
