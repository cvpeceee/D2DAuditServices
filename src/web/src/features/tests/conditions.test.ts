import { describe, expect, it } from "vitest";
import { ConditionError, parseCondition, runTest, type InventoryResource } from "./conditions";

const now = new Date("2025-06-01T00:00:00Z");
const resources: InventoryResource[] = [
  {
    provider: "aws",
    kind: "bucket",
    externalId: "a",
    name: "logs-prod",
    attributes: { versioning: "true", size: "10", created: "2024-01-01" },
  },
  {
    provider: "aws",
    kind: "bucket",
    externalId: "b",
    name: "stages-prod",
    attributes: { versioning: "false", size: "200" },
  },
  { provider: "aws", kind: "bucket", externalId: "c", name: "scratch-dev", attributes: {} },
];

function code(input: unknown) {
  try {
    parseCondition(input, false);
    return "ok";
  } catch (error) {
    return error instanceof ConditionError ? error.code : "unexpected";
  }
}

describe("condition evaluator", () => {
  it("evaluates scope, assertions and groups", () => {
    const run = runTest(
      "bucket",
      "aws",
      { field: "name", op: "contains", value: "PROD" },
      {
        any: [
          { field: "attributes.versioning", op: "equals", value: true },
          { field: "attributes.size", op: "lt", value: 50 },
        ],
      },
      resources,
      now,
    );
    expect(run.outcome).toBe("fail");
    expect(run.failing.map((item) => item.name)).toEqual(["stages-prod"]);
    expect(run.detail).toBe("1 of 2 resources fail: stages-prod");
  });

  it("treats missing fields as passing negative operators", () => {
    const run = runTest(
      "bucket",
      null,
      null,
      { field: "attributes.public", op: "notEquals", value: "true" },
      resources,
      now,
    );
    expect(run.outcome).toBe("pass");
  });

  it("supports relative dates", () => {
    const run = runTest(
      "bucket",
      null,
      { field: "attributes.created", op: "exists" },
      { field: "attributes.created", op: "after", value: "now-365d" },
      resources,
      now,
    );
    expect(run.outcome).toBe("fail");
  });

  it("returns notRun when the scope is empty", () => {
    expect(
      runTest("repository", null, null, { field: "name", op: "exists" }, resources, now).outcome,
    ).toBe("notRun");
  });

  it("rejects malicious and malformed input", () => {
    expect(code({ field: "__proto__", op: "equals", value: "x" })).toBe("condition_invalid_field");
    expect(code({ field: "attributes.__proto__.polluted", op: "equals", value: "x" })).toBe(
      "condition_invalid_field",
    );
    expect(code({ field: "name", op: "regex", value: ".*" })).toBe("condition_invalid_operator");
    expect(code({ field: "name", op: "equals", value: { $gt: "" } })).toBe(
      "condition_invalid_value",
    );
    expect(code({ field: "name", op: "equals" })).toBe("condition_value_required");
    expect(code({ field: "name", op: "in", value: [] })).toBe("condition_invalid_list");
    expect(code({ field: "name", op: "before", value: "tomorrow" })).toBe("condition_invalid_date");
    expect(code({ all: [], any: [] })).toBe("condition_invalid");
    expect(code("name == 'x'")).toBe("condition_invalid");
    expect(code(null)).toBe("condition_required");
    expect(code({ field: "name", op: "equals", value: "x".repeat(501) })).toBe(
      "condition_invalid_value",
    );
    expect(code({ all: Array.from({ length: 60 }, () => ({ field: "name", op: "exists" })) })).toBe(
      "condition_too_many_nodes",
    );
    expect(code({ field: "name", op: "equals", value: "y".repeat(9000) })).toBe(
      "condition_too_large",
    );
  });

  it("does not read inherited properties from attributes", () => {
    const run = runTest(
      "bucket",
      null,
      null,
      { field: "attributes.toString", op: "exists" },
      resources,
      now,
    );
    expect(run.passing).toHaveLength(0);
  });
});
