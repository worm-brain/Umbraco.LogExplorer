import { describe, expect, it } from "vitest";
import { tokeniseMessage, tokeniseTemplate } from "./message-tokens.js";

describe("tokeniseMessage", () => {
  it("marks each property value in the rendered body", () => {
    const tokens = tokeniseMessage(
      'HTTP "GET" "/healtz" responded 200 in 4.2 ms',
      "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed} ms",
    );

    expect(tokens).toEqual([
      { text: "HTTP " },
      { text: '"GET"', field: "RequestMethod" },
      { text: " " },
      { text: '"/healtz"', field: "RequestPath" },
      { text: " responded " },
      { text: "200", field: "StatusCode" },
      { text: " in " },
      { text: "4.2", field: "Elapsed" },
      { text: " ms" },
    ]);
  });

  it("strips operators, formats and alignment from property names", () => {
    const tokens = tokeniseMessage("Took 0012 ms for {cart}", "Took {Elapsed:0000} ms for {@Cart,10}");

    expect(tokens.filter((token) => token.field).map((token) => token.field)).toEqual(["Elapsed", "Cart"]);
  });

  it("keeps values containing braces and markup as plain text", () => {
    const tokens = tokeniseMessage('Rendered "<img src=x onerror=alert(1)>{x}" now', "Rendered {Html} now");

    expect(tokens[1]).toEqual({ text: '"<img src=x onerror=alert(1)>{x}"', field: "Html" });
  });

  it("treats doubled braces in the template as literal braces", () => {
    const tokens = tokeniseMessage("Set {literal} to 5", "Set {{literal}} to {Value}");

    expect(tokens).toEqual([{ text: "Set {literal} to " }, { text: "5", field: "Value" }]);
  });

  it("falls back to the plain body when it does not match the template", () => {
    expect(tokeniseMessage("Something else entirely", "Running job {JobName}")).toEqual([
      { text: "Something else entirely" },
    ]);
  });

  it("falls back to the plain body when two holes are adjacent", () => {
    expect(tokeniseMessage("ab", "{A}{B}")).toEqual([{ text: "ab" }]);
  });

  it("returns the body alone when there is no template", () => {
    expect(tokeniseMessage("Plain message", null)).toEqual([{ text: "Plain message" }]);
  });

  it("renders from the attributes when there is no body", () => {
    expect(tokeniseMessage(null, "Running job {JobName} ({Count})", { JobName: "Cleanup", Count: 3 })).toEqual([
      { text: "Running job " },
      { text: '"Cleanup"', field: "JobName" },
      { text: " (" },
      { text: "3", field: "Count" },
      { text: ")" },
    ]);
  });

  it("is empty without a body or a template", () => {
    expect(tokeniseMessage(null, null)).toEqual([]);
  });
});

describe("tokeniseTemplate", () => {
  it("marks each placeholder, keeping its operator and format", () => {
    expect(tokeniseTemplate("Took {Elapsed:0.00} ms for {@Cart}")).toEqual([
      { text: "Took " },
      { text: "{Elapsed:0.00}", field: "Elapsed" },
      { text: " ms for " },
      { text: "{@Cart}", field: "Cart" },
    ]);
  });

  it("keeps escaped braces and an unclosed brace as literal text", () => {
    expect(tokeniseTemplate("Set {{literal}} to {")).toEqual([{ text: "Set {literal} to {" }]);
  });
});
