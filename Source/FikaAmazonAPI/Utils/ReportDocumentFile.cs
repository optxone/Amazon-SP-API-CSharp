using FikaAmazonAPI.AmazonSpApiSDK.Models.Exceptions;
using FikaAmazonAPI.AmazonSpApiSDK.Models.Reports;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FikaAmazonAPI.Utils
{
    /// <summary>
    /// A downloaded report document kept in a temp file, never whole in memory. The document is streamed from its
    /// pre-signed URL to disk as Amazon sends it (still gzipped when it is), and <see cref="OpenRead"/> decompresses it
    /// while it is read: memory is a few buffers, whatever the document size (a large seller's 30-day ledger summary is
    /// ~90 MB). <see cref="Dispose"/> deletes the file.
    /// </summary>
    public sealed class ReportDocumentFile : IDisposable
    {
        /// <summary>Folder for downloaded documents (default: a subfolder of the OS temp folder). Set once at startup.</summary>
        public static string Directory { get; set; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amazon-sp-api-report-documents");

        /// <summary>A document file older than this is an orphan of a crashed process; the first download of a process deletes it.</summary>
        public static readonly TimeSpan OrphanAge = TimeSpan.FromHours(12);

        private const int CopyBufferSize = 81920;
        private static int _swept;

        private static readonly Lazy<HttpClient> SharedClient = new Lazy<HttpClient>(() => new HttpClient(new StandardSocketsHttpHandler
        {
            // The document bytes are kept as sent; gzip is undone by OpenRead, never by the handler.
            AutomaticDecompression = DecompressionMethods.None,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = TimeSpan.FromMinutes(30),
        });

        private int _disposed;

        private ReportDocumentFile(string path, bool gzip)
        {
            Path = path;
            IsGzip = gzip;
            Length = new FileInfo(path).Length;
        }

        /// <summary>The temp file.</summary>
        public string Path { get; }

        /// <summary>The file holds the gzipped document; <see cref="OpenRead"/> decompresses it.</summary>
        public bool IsGzip { get; }

        /// <summary>Bytes on disk (compressed when <see cref="IsGzip"/>). 0 = empty document.</summary>
        public long Length { get; }

        /// <summary>A forward-only stream of the document's content (decompressed). Dispose it before this file.</summary>
        public Stream OpenRead()
        {
            var file = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return IsGzip ? (Stream)new GZipStream(file, CompressionMode.Decompress, leaveOpen: false) : file;
        }

        /// <summary>
        /// Streams the document at <paramref name="document"/>'s pre-signed URL to a temp file. Gzip is decided by the
        /// document's <c>compressionAlgorithm</c> (or a <c>Content-Encoding: gzip</c> answer). Encrypted documents (the
        /// retired 2020-09-04 Reports API) are not handled here; <c>ReportService.GetReportDocumentFileAsync</c> decrypts them.
        /// </summary>
        /// <param name="client">The HttpClient to download with (tests); null = a shared client that never decompresses.</param>
        /// <exception cref="AmazonReportDocumentDownloadException">The URL answered a non-success status.</exception>
        public static async Task<ReportDocumentFile> DownloadAsync(ReportDocument document, HttpClient? client = null, CancellationToken cancellationToken = default)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (string.IsNullOrEmpty(document.Url))
                throw new InvalidOperationException($"Report document {document.ReportDocumentId} has no download url.");
            if (document.EncryptionDetails != null)
                throw new NotSupportedException($"Report document {document.ReportDocumentId} is encrypted; use ReportService.GetReportDocumentFileAsync.");

            var path = NewPath();
            try
            {
                using (var response = await (client ?? SharedClient.Value)
                    .GetAsync(document.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new AmazonReportDocumentDownloadException(document.ReportDocumentId, (int)response.StatusCode);

                    var gzip = document.CompressionAlgorithm == ReportDocument.CompressionAlgorithmEnum.GZIP
                               || response.Content.Headers.ContentEncoding.Any(e => e.Equals("gzip", StringComparison.OrdinalIgnoreCase));

                    using (var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                               CopyBufferSize, FileOptions.Asynchronous))
                    {
                        await body.CopyToAsync(file, CopyBufferSize, cancellationToken).ConfigureAwait(false);
                    }
                    return new ReportDocumentFile(path, gzip);
                }
            }
            catch
            {
                TryDelete(path);
                throw;
            }
        }

        /// <summary>Writes an already-materialised document (a decrypted document, tests) to a temp file.</summary>
        public static async Task<ReportDocumentFile> FromStreamAsync(Stream content, bool gzip = false, CancellationToken cancellationToken = default)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            var path = NewPath();
            try
            {
                using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                           CopyBufferSize, FileOptions.Asynchronous))
                {
                    if (content.CanSeek) content.Position = 0;
                    await content.CopyToAsync(file, CopyBufferSize, cancellationToken).ConfigureAwait(false);
                }
                return new ReportDocumentFile(path, gzip);
            }
            catch
            {
                TryDelete(path);
                throw;
            }
        }

        private static string NewPath()
        {
            System.IO.Directory.CreateDirectory(Directory);
            if (Interlocked.Exchange(ref _swept, 1) == 0) SweepOrphans();
            return System.IO.Path.Combine(Directory, $"{Guid.NewGuid():N}.doc");
        }

        /// <summary>Deletes document files a crashed process left behind (once per process).</summary>
        private static void SweepOrphans()
        {
            try
            {
                var cutoff = DateTime.UtcNow - OrphanAge;
                foreach (var f in new DirectoryInfo(Directory).EnumerateFiles("*.doc"))
                    if (f.LastWriteTimeUtc < cutoff) TryDelete(f.FullName);
            }
            catch (Exception)
            {
                // best effort
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // best effort: an orphan is swept by the next process
            }
        }

        /// <summary>Deletes the temp file.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) TryDelete(Path);
        }
    }
}
