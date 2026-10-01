import { describe, expect, it } from "vitest";
import { isFrameworkFrame, renderStackTrace, splitStackTrace } from "./stack-trace.js";

const TRACE = [
  "Microsoft.Data.SqlClient.SqlException (0x80131904): Execution Timeout Expired.",
  "   at Microsoft.Data.SqlClient.SqlCommand.ExecuteNonQueryAsync(CancellationToken cancellationToken)",
  "   at System.Threading.Tasks.Task.Wait()",
  "   at Client.Web.Forms.ContactRepository.SaveAsync(ContactSubmission submission)",
  "--- End of stack trace from previous location ---",
  "   at Umbraco.Cms.Core.Events.EventAggregator.PublishCore(...)",
  "   at Client.Web.Controllers.ContactSurfaceController.Submit(ContactFormModel model)",
].join("\r\n");

describe("isFrameworkFrame", () => {
  it.each([
    ["a System frame", "   at System.Threading.Tasks.Task.Wait()"],
    ["a Microsoft frame", "   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.Execute(...)"],
    ["an Umbraco.Cms frame", "   at Umbraco.Cms.Core.Events.EventAggregator.PublishCore(...)"],
    ["the async boundary marker", "--- End of stack trace from previous location ---"],
  ])("counts %s as framework", (_name, line) => {
    expect(isFrameworkFrame(line)).toBe(true);
  });

  it.each([
    ["a site frame", "   at Client.Web.Forms.ContactRepository.SaveAsync(ContactSubmission submission)"],
    ["a community package frame", "   at Umbraco.Community.LogExplorer.Files.Reader.Read()"],
    ["the exception header", "System.InvalidOperationException: Something failed."],
    ["an inner exception line", " ---> System.TimeoutException: The wait timed out."],
  ])("does not count %s as framework", (_name, line) => {
    expect(isFrameworkFrame(line)).toBe(false);
  });
});

describe("splitStackTrace", () => {
  it("groups consecutive lines into framework and other runs, in order", () => {
    const runs = splitStackTrace(TRACE);

    expect(runs.map((run) => [run.framework, run.lines.length])).toEqual([
      [false, 1],
      [true, 2],
      [false, 1],
      [true, 2],
      [false, 1],
    ]);
  });

  it("returns no runs for a missing trace", () => {
    expect(splitStackTrace(null)).toEqual([]);
  });
});

describe("renderStackTrace", () => {
  const placeholder = (count: number) => `… ${count} hidden`;

  it("replaces each framework run with one indented placeholder line", () => {
    expect(renderStackTrace(splitStackTrace(TRACE), false, placeholder).split("\n")).toEqual([
      "Microsoft.Data.SqlClient.SqlException (0x80131904): Execution Timeout Expired.",
      "   … 2 hidden",
      "   at Client.Web.Forms.ContactRepository.SaveAsync(ContactSubmission submission)",
      "   … 2 hidden",
      "   at Client.Web.Controllers.ContactSurfaceController.Submit(ContactFormModel model)",
    ]);
  });

  it("shows every line when framework frames are shown", () => {
    expect(renderStackTrace(splitStackTrace(TRACE), true, placeholder)).toBe(TRACE.replaceAll("\r\n", "\n"));
  });
});
