/**
 * Builds what the e2e sites serve before Playwright starts them: the client bundle (the sites serve
 * the RCL's wwwroot, which `bun run build` writes) and then each selected site.
 *
 * Running this before the sites start also covers the fresh-clone gotcha: a site discovers its
 * static web asset folders at start-up, so it must start after the first client build.
 *
 * `E2E_SKIP_BUILD=1` skips all of it: use it when a site is already running on its e2e port
 * (Playwright then reuses it), because building would fail on the site's locked DLLs.
 */
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { repoRoot, sites } from "./support/sites.js";

function run(command: string, args: Array<string>, cwd: string): void {
  console.log(`> ${command} ${args.join(" ")}`);
  const result = spawnSync(command, args, { cwd, stdio: "inherit", shell: process.platform === "win32" });
  if (result.status !== 0) process.exit(result.status ?? 1);
}

if (process.env.E2E_SKIP_BUILD === "1") {
  console.log("E2E_SKIP_BUILD=1: using the existing client and site builds.");
} else {
  run("bun", ["run", "build"], fileURLToPath(new URL("..", import.meta.url)));
  for (const site of sites) {
    run("dotnet", ["build", site.projectDir, `-p:UmbracoMajor=${site.major}`], repoRoot);
  }
}
