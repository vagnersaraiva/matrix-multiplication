# Matrix multiplication client

Console client for the Component Tech backend take-home. It initializes matrices A and B, downloads every row of A and every column of B, multiplies them locally, and submits the Base64 encoding of the MD5 of the product.

The timed run prints the same steps as the sample in the instructions and writes that text to `artifacts/timings.txt`. The successful n = 1000 run is captured in [artifacts/runtime-timings.png](artifacts/runtime-timings.png).

## Run

Reviewers need Docker. The image carries the .NET 8 runtime, so the host does not need an SDK.

```bash
docker build -t matrix-multiply .
docker run --rm -v "$PWD/artifacts:/app/artifacts" matrix-multiply --n 1000
docker run --rm matrix-multiply --calibrate
```

The timed run writes `artifacts/timings.txt` on the host through that volume. `--calibrate` only prints the winning hash layout.

To run the unit tests inside the SDK image:

```bash
docker build --target test -t matrix-multiply-tests .
```

With a local .NET 8 SDK, the same commands are:

```bash
dotnet test -c Release
dotnet run -c Release --project src/MatrixMultiply.Cli -- --calibrate
dotnet run -c Release --project src/MatrixMultiply.Cli -- --n 1000
```

`--calibrate` initializes a 3×3 pair and tries each hash layout until the validate endpoint accepts one. The timed path uses `ResultHasher.DefaultLayout`: the product written row-major as UTF-8 integers separated by commas, with no trailing comma. Pass `--layout` to force a different candidate.

Useful options:

| Option | Default | Purpose |
| --- | --- | --- |
| `--n` | 1000 | Matrix size, from 1 to 10000 |
| `--initial-concurrency` | 64 | Parallel GETs at the start of the fetch |
| `--max-concurrency` | 128 | Ceiling for the adaptive limit |
| `--base-url` | value in `appsettings.json` | API origin, without a port |
| `--layout` | `CommaSeparated` | Force the MD5 byte layout |

The base URL is `https://takehome-api-231097775515.us-central1.run.app`.

## Design

Fetching is the expensive step from a high-latency network. The client opens one HTTP/2 connection and loads rows of A and columns of B together. Those two streams are enough to build A×B: a column of B is stored contiguously, so each dot product reads two sequential spans. Columns of A and rows of B are not downloaded; they would double the traffic and do not change the product.

In-flight GETs are capped by an adaptive limit. Every 16 successes raise it by 4, up to the ceiling. A 429 cuts it in half, and only the failed call is retried, with a short jitter. A response that says the matrix is gone restarts from `init`. GET requests send `Accept` and no body. POST requests send `Content-Type: application/json` with no charset parameter.

Multiplication starts only after every fetch has completed. The product is a blocked parallel kernel. When AVX2 is available, each k-tile is accumulated with 8-wide int32 multiply-add. Arithmetic wraps in unchecked int32. The MD5 covers the UTF-8 text of that product (`5148,1460,...`) and is then Base64-encoded from the raw 16 hash bytes. A 2×2 run against the API confirmed this layout; the other candidates are still available for `--calibrate`.

`n = 1000` is the size used in the sample timing log. Larger sizes are supported up to the API maximum of 10000. Validate time includes whatever work the server does to check the hash, so the client cannot shrink that step.

## Layout

- `Dockerfile` — build and run without a local .NET SDK
- `src/MatrixMultiply.Core` — HTTP client, hash layouts, multiplication, and the run pipeline
- `src/MatrixMultiply.Cli` — command-line entry point
- `tests/MatrixMultiply.Tests` — multiplication, parsing, retry, and timing-format tests

## Submission checklist

- [x] Client, tests, and timing report
- [x] Screenshot of a successful timed run ([artifacts/runtime-timings.png](artifacts/runtime-timings.png))

