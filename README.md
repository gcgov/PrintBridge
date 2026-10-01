# PrintBridge

A small Windows tray application that lets **web applications print to desktop
printers**. A page in the browser POSTs a PDF to PrintBridge's local HTTP API;
PrintBridge renders it and hands it to the Windows print spooler — silently, with no
print dialog and no user interaction.

PrintBridge is generic — it knows nothing about any particular web app. Any site whose
origin an administrator adds to the allowlist can print to the queues an administrator
has configured.

```
┌──────────────┐  POST http://127.0.0.1:7227   ┌─────────────┐   Windows spooler   ┌─────────┐
│ Your web app │ ────────── PDF ─────────────► │ PrintBridge │ ──────────────────► │ Printer │
│  (browser)   │ ◄──────── job id ──────────── │ (tray app)  │                     │         │
└──────────────┘                               └─────────────┘                     └─────────┘
```

It is the sibling of [ScanBridge](https://github.com/gcgov/scanbridge), which does the
same thing for document scanners.

## Named queues

Web apps never name a Windows printer. An administrator configures **queues** — a name
plus the printer it maps to — and web apps ask for the queue:

| Queue | Printer |
|---|---|
| `labels` | ZDesigner GK420d |
| `front-desk` | HP LaserJet M404 |

Swapping the printer behind `labels` is a settings change; no web app has to be
touched, and no web app learns anything about the machine's printers.

## Install

Grab the latest release from the [Releases](../../releases) page:

- **`PrintBridge-<version>-setup.exe`** — per-user installer (no admin rights needed).
  Installs to `%LocalAppData%\Programs\PrintBridge` and starts at login by default.
- **`PrintBridge-<version>-win-x64-portable.zip`** — portable build; unzip anywhere and
  run `PrintBridge.exe`.

Both are self-contained: no .NET runtime install is required.

Intune-managed computers get each new release automatically. See
[docs/intune.md](docs/intune.md) for the setup.

On first run the settings window opens. Add at least one queue, and — important — add
the website origin(s) that are allowed to print.

## Configuration

Right-click the tray icon → **Settings…**

| Setting | Meaning |
|---|---|
| Print queues | Named queues web apps can print to, each mapped to an installed Windows printer. One is the default, used when a request omits `queue`. |
| Port | The local HTTP port (default **7227**). PrintBridge listens on `127.0.0.1` only — it is never reachable from the network. |
| Allowed website origins | Exact origins (e.g. `https://apps.example.gov`) allowed to call the API from a browser. Empty list = no site may use it. |
| Start when I sign in | Per-user autostart (registry `Run` key). |

Settings live in `%AppData%\PrintBridge\settings.json`; logs in
`%LocalAppData%\PrintBridge\logs`.

```json
{
  "port": 7227,
  "allowedOrigins": ["https://apps.example.gov"],
  "queues": [
    { "name": "labels", "printerName": "ZDesigner GK420d" },
    { "name": "front-desk", "printerName": "HP LaserJet M404" }
  ],
  "defaultQueue": "front-desk",
  "runAtLogin": true
}
```

## Using it from a web page

See [docs/api.md](docs/api.md) for the full HTTP API. The short version:

```js
// 1. submit the PDF (raw body, not JSON)
const res = await fetch('http://127.0.0.1:7227/api/v1/print-jobs?queue=labels&copies=1', {
  method: 'POST',
  headers: { 'Content-Type': 'application/pdf' },
  body: pdfBlob,
  targetAddressSpace: 'loopback',           // Local Network Access hint (Chrome 142+)
});
const { jobId } = await res.json();

// 2. poll until it is done
let job;
do {
  await new Promise(r => setTimeout(r, 1000));
  job = await (await fetch(`http://127.0.0.1:7227/api/v1/print-jobs/${jobId}`,
    { targetAddressSpace: 'loopback' })).json();
} while (!['completed', 'failed', 'canceled'].includes(job.status));

if (job.status !== 'completed') console.error(job.error.code, job.error.message);
```

Only PDF is accepted. `completed` means the Windows spooler took the document; what the
printer does afterwards is not visible to PrintBridge.

### Browser requirements (Local Network Access)

Calling `http://127.0.0.1` from an HTTPS page is allowed by Chrome, Edge and Firefox
(loopback is a "potentially trustworthy" origin; Safari currently blocks it). Since
Chrome 142, the **Local Network Access** feature additionally shows a one-time
permission prompt the first time a site talks to the local machine; the user must click
**Allow**. The decision is remembered per site.

For managed fleets, administrators can skip the prompt entirely by adding the web app's
origin to the `LocalNetworkAccessAllowedForUrls` enterprise policy (Chrome/Edge via
GPO or Intune; Firefox has an equivalent `LocalNetworkAccess` policy).

### Security model

- The listener binds to `127.0.0.1` only — nothing on the network can reach it.
- Browsers enforce CORS: only origins on the allowlist get responses.
- Callers can only reach printers an administrator mapped to a queue, and never learn
  the printer names.
- Requests carry no credentials and PrintBridge stores no secrets; the worst a
  malicious allowed page could do is waste paper.

## Building from source

Requires the .NET 10 SDK on Windows.

```
dotnet build PrintBridge.sln
dotnet test PrintBridge.sln
dotnet publish src/PrintBridge -c Release -r win-x64 --self-contained true
```

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php) from
`installer/PrintBridge.iss`.

### Manual smoke test

Needs a Windows machine with a real printer.

1. Run `PrintBridge.exe`. In Settings, add a queue (say `labels`) pointing at a real
   printer, make it the default, and add `https://localhost:8097` (or your dev origin)
   to the allowed origins.
2. `curl http://127.0.0.1:7227/api/v1/status` → JSON status with `queuesConfigured: 1`.
3. Tray menu → **Print test page** → a page comes out of the printer.
4. Submit a PDF the way a web app would, and poll it to `completed`:

   ```
   curl -i -X POST "http://127.0.0.1:7227/api/v1/print-jobs?queue=labels&copies=1" ^
     -H "Content-Type: application/pdf" --data-binary "@sample.pdf"
   curl http://127.0.0.1:7227/api/v1/print-jobs/<jobId>
   ```

5. `?queue=nope` must return `422 unknownQueue`, and posting a non-PDF body must return
   `422 invalidDocument`.
6. From a disallowed origin, a browser `fetch` must fail CORS.

## Licensing

PrintBridge is MIT-licensed (see [LICENSE](LICENSE)). Its dependencies are MIT and
BSD-3-Clause — see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
