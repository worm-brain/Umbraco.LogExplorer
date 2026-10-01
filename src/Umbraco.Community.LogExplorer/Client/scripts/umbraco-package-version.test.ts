import { describe, expect, it } from "vitest";
import { resolvePackageVersion, withVersion } from "./umbraco-package-version.js";

const csproj = `<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <Version>1.0.0-beta.1</Version>
  </PropertyGroup>
</Project>`;

describe("resolvePackageVersion", () => {
  it("uses the version dotnet pack passes in the environment", () => {
    expect(resolvePackageVersion("2.3.4", csproj)).toBe("2.3.4");
  });

  it("falls back to the csproj <Version> when the environment has none", () => {
    expect(resolvePackageVersion(undefined, csproj)).toBe("1.0.0-beta.1");
  });

  it("falls back to the csproj <Version> when the environment variable is empty", () => {
    expect(resolvePackageVersion("", csproj)).toBe("1.0.0-beta.1");
  });

  it("throws when neither source has a version", () => {
    expect(() => resolvePackageVersion(undefined, "<Project />")).toThrow(/No <Version>/);
  });
});

describe("withVersion", () => {
  it("replaces the version and keeps every other field", () => {
    const manifest = JSON.stringify({ id: "Umbraco.Community.LogExplorer", version: "0.0.0", allowTelemetry: true });

    const result = JSON.parse(withVersion(manifest, "1.0.0-beta.1"));

    expect(result).toEqual({ id: "Umbraco.Community.LogExplorer", version: "1.0.0-beta.1", allowTelemetry: true });
  });
});
