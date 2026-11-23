# Optxone fork

Fork of [abuzuhri/Amazon-SP-API-CSharp](https://github.com/abuzuhri/Amazon-SP-API-CSharp), built and
published by us so we can ship library fixes without waiting on upstream merges.

- **Package**: `Optxone.CSharpAmazonSpAPI` on the optxone GitHub Packages feed
  (`https://nuget.pkg.github.com/optxone/index.json`). Same `FikaAmazonAPI` assembly and namespaces as
  upstream's `CSharpAmazonSpAPI` — only the package id differs.
- **Versioning**: `<upstream version>.<our revision>`, e.g. `1.9.33.1`, `1.9.33.2`. Reset the revision
  to `.1` after taking a new upstream version.
- **Upstream's `Release` workflow (nuget.yml) is disabled** in this repo — it publishes to nuget.org.

## Getting upstream updates

```bash
git remote add upstream https://github.com/abuzuhri/Amazon-SP-API-CSharp.git   # once
git fetch upstream
git checkout main
git merge upstream/main        # resolve conflicts with our changes, if any
git push origin main
```

(Or use **Sync fork** on GitHub when there are no conflicts.)

## Releasing

```bash
git tag optx-v1.9.33.1
git push origin optx-v1.9.33.1
```

The `Optxone Package` workflow packs and pushes the package. Then bump the version in Crigora's
`Crigora.Integration.Amazon.csproj`.

## Our changes

Keep upstream-able fixes small and, where possible, also open a PR upstream so the fork does not drift.

## Version notes

### 1.9.33.2 (tag `optx-v1.9.33.2`)

- **Reports: decide on `processingStatus` first** (`ReportService`, `ReportLifecycle`). `CreateReportAndDownloadFile[Stream]`
  and `GetReportFile[Stream]ByReportIdAsync` keep their signatures, but now: DONE with a document → download; DONE
  without a document → `AmazonReportFatalException` (was: polled forever); CANCELLED → null (unchanged); FATAL →
  `AmazonReportFatalException` with `ReportType`, `ReportId`, `Status` and the start of Amazon's error document (was: the
  error document was downloaded and returned as the report's data, because the document id was checked first).
- **Polling backs off and has a limit**: 2 s doubling to 30 s, at most 2 h → `AmazonReportTimeoutException`
  (a `TimeoutException`). Configure with `ReportPollingOptions` (new overloads). The `int millisecondsDelay` of the
  existing overloads is now the first delay of the backoff (default 500 ms).
- **Download to a temp file**: `ReportDocumentFile` (gzip kept on disk, decompressed by `OpenRead()`, deleted on
  `Dispose()`), `ReportService.GetReportDocumentFileAsync`, `GetReportDocumentFileByReportIdAsync`,
  `CreateReportAndDownloadDocumentFileAsync` (by report type → file, or by `ParameterCreateReportSpecification` →
  `ReportFileResult` with report id, status, poll count). A non-success answer of the pre-signed URL throws
  `AmazonReportDocumentDownloadException` (`HttpStatus`). The new exceptions do not derive from `AmazonException`.
- `IReportOperations` (create / get / download-to-file; implemented by `ReportService`) lets callers fake the Reports
  API in tests and drive `ReportLifecycle` directly.
- **Orders v0 rate-limit defaults** corrected to Amazon's usage plans (ordersV0.json): `getOrder`, `getOrderBuyerInfo`,
  `getOrderAddress` 0.0167/20 → 0.5/30; `getOrderItems` burst 20 → 30; `getOrderRegulatedInfo` burst 15 → 30.
