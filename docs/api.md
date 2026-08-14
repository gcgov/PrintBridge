# PrintBridge HTTP API

Base URL: `http://127.0.0.1:<port>` — default port **7227**. The listener binds to
loopback only. All response bodies are JSON (camelCase); the document to print is sent
as a raw PDF request body.

Browsers must be subject to the CORS allowlist: the calling page's origin has to be
listed under *Allowed website origins* in PrintBridge settings. Same-machine tools
(curl, desktop apps) are not subject to CORS.

Requests from browsers should pass `targetAddressSpace: 'loopback'` in the `fetch`
options to satisfy Chrome's Local Network Access rules (Chrome 142+).

## Queues, not printers

Web apps never name a Windows printer. An administrator configures **named queues** in
PrintBridge settings (for example `labels` → `ZDesigner GK420d`) and the web app asks
for the queue by name, so the printer behind a queue can be swapped without touching
the web app. One queue is marked as the default and is used when a request omits
`queue`.

## Error shape

Non-2xx responses (except 404s from unknown URLs) use:

```json
{ "error": { "code": "unknownQueue", "message": "There is no print queue named 'labels'." } }
```

| Code | Meaning |
|---|---|
| `unknownQueue` | The requested queue name is not configured. |
| `noQueueConfigured` | No `queue` was given and no usable default queue is configured. |
| `invalidDocument` | The body is not a PDF, or the PDF cannot be rendered. |
| `invalidCopies` | `copies` is outside 1–99. |
| `documentTooLarge` | The body is larger than 100 MB. |
| `printerUnavailable` | Windows does not have the printer behind the queue. |
| `canceled` | The job was canceled. |
| `printFailed` | Any other print failure; `message` has details. |
| `notFound` | Unknown job id. |

Messages name the queue, never the Windows printer.

## Endpoints

### `GET /api/v1/status`

Handshake / discovery. Use this to detect whether PrintBridge is installed and running.

```json
{ "app": "PrintBridge", "version": "1.0.0", "apiVersion": 1,
  "queuesConfigured": 2, "defaultQueue": "front-desk" }
```

### `GET /api/v1/queues`

Lists the configured queues so a page can offer a picker.

```json
[ { "name": "labels", "isDefault": false },
  { "name": "front-desk", "isDefault": true } ]
```

### `POST /api/v1/print-jobs?queue=labels&copies=2`

Submits a document. **The request body is the raw PDF** — not JSON, not multipart.
`Content-Type: application/pdf` is conventional but not enforced; PrintBridge looks for
the `%PDF-` marker in the first 1024 bytes instead.

| Query parameter | Default | Meaning |
|---|---|---|
| `queue` | the default queue | Name of the queue to print to. |
| `copies` | `1` | 1–99. |

Responses: `202 Accepted` with `{ "jobId": "…" }` (Location header points at the job),
`422` `unknownQueue` / `noQueueConfigured` / `invalidDocument` / `invalidCopies`,
`413` `documentTooLarge`.

Submissions are always accepted — there is no "busy" rejection. Jobs are printed one at
a time in the order they arrive, because rendering a PDF at printer resolution is
memory-hungry and serializing it bounds that cost.

### `GET /api/v1/print-jobs/{jobId}`

Polls a job.

```json
{ "jobId": "…", "status": "printing", "queue": "labels", "copies": 2,
  "pagesPrinted": 3, "error": null }
```

`status` is `queued | printing | completed | failed | canceled`. When `failed`/`canceled`,
`error` carries `{code, message}`.

`pagesPrinted` counts pages handed to the spooler, including copies (a 2-page document
printed twice reaches 4). `completed` means the Windows print spooler accepted the whole
document — what happens after that (an offline printer, a paper jam) is between Windows
and the printer, and PrintBridge cannot see it.

### `DELETE /api/v1/print-jobs/{jobId}`

Cancels a queued or printing job, or discards a finished one. Always `204`. Canceling a
job that is already spooling stops it at the next page boundary; pages Windows has
already accepted may still come out. Finished jobs are discarded automatically after
10 minutes.

## Typical client flow

1. `GET /status` — if unreachable, tell the user to install/start PrintBridge.
2. `POST /print-jobs?queue=labels` with the PDF as the body.
3. Poll `GET /print-jobs/{jobId}` every second until the status is terminal.
4. On `failed`, show `error.message`.

```js
const res = await fetch('http://127.0.0.1:7227/api/v1/print-jobs?queue=labels&copies=1', {
  method: 'POST',
  headers: { 'Content-Type': 'application/pdf' },
  body: pdfBlob,                              // a Blob / ArrayBuffer holding the PDF
  targetAddressSpace: 'loopback',             // Local Network Access hint (Chrome 142+)
});
const { jobId } = await res.json();

let job;
do {
  await new Promise(r => setTimeout(r, 1000));
  job = await (await fetch(`http://127.0.0.1:7227/api/v1/print-jobs/${jobId}`,
    { targetAddressSpace: 'loopback' })).json();
} while (!['completed', 'failed', 'canceled'].includes(job.status));
```
