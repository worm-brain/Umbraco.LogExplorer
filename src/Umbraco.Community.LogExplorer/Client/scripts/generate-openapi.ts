/**
 * Generates the typed Management API client in `src/api` from the package's Swagger document.
 *
 * Usage: `bun scripts/generate-openapi.ts <swagger-url>` (see the `generate` script in
 * package.json). A sample site (samples/LogExplorer.Site17) must be running, because the Swagger document is only served
 * by a live Umbraco instance.
 */
import { createClient } from "@hey-api/openapi-ts";

const swaggerUrl = process.argv[2];
if (!swaggerUrl) {
  console.error("Missing URL to the OpenAPI spec, e.g.");
  console.error("  bun scripts/generate-openapi.ts https://localhost:44370/umbraco/swagger/log-explorer/swagger.json");
  process.exit(1);
}

// The sample site runs on a self-signed localhost certificate.
process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";

// Check that the site is up first, so a stopped site gives a clear message rather than a generator stack trace.
console.log(`Fetching OpenAPI definition from ${swaggerUrl}`);
const response = await fetch(swaggerUrl).catch((error: Error) => {
  console.error(`Failed to reach the OpenAPI spec: ${error.message}`);
  console.error("Is the sample site running?");
  process.exit(1);
});
if (!response.ok) {
  console.error(
    `OpenAPI spec returned ${response.status} ${response.statusText}. Is the sample site running, and is the URL right?`,
  );
  process.exit(1);
}

await createClient({
  input: swaggerUrl,
  output: "src/api",
  plugins: [
    "@hey-api/client-fetch",
    "@hey-api/typescript",
    { name: "@hey-api/sdk", operations: { strategy: "byTags", containerName: "{{name}}Service" } },
  ],
});
