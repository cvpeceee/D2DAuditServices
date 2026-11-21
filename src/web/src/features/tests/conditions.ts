export type ConditionLeaf = {
  field: string;
  op: string;
  value?: string | number | boolean | string[];
};
export type ConditionGroup = { all: Condition[] } | { any: Condition[] };
export type Condition = ConditionLeaf | ConditionGroup;

export type InventoryResource = {
  provider: string;
  kind: string;
  externalId: string;
  name: string;
  attributes: Record<string, string>;
};

type Parsed =
  | { kind: "group"; all: boolean; children: Parsed[] }
  | { kind: "leaf"; field: string; op: string; value: string | null; values: string[] };

export const operators = [
  "equals",
  "notEquals",
  "contains",
  "notContains",
  "in",
  "notIn",
  "exists",
  "notExists",
  "gt",
  "lt",
  "gte",
  "lte",
  "before",
  "after",
] as const;

export const limits = { json: 8000, depth: 6, nodes: 50, value: 500, list: 50 };

const fieldPattern = /^(name|provider|kind|externalId|attributes\.[A-Za-z0-9_-]{1,64})$/;
const relative = /^now(?:([+-])(\d{1,4})d)?$/;
const numberPattern = /^-?\d+(\.\d+)?$/;

export class ConditionError extends Error {
  constructor(public code: string) {
    super(code);
  }
}

/** Mirrors the API evaluator: data only, closed operator set, fixed field allow-list. */
export function parseCondition(input: unknown, allowEmpty: boolean): Parsed | null {
  if (input === null || input === undefined || input === "") {
    if (allowEmpty) return null;
    throw new ConditionError("condition_required");
  }
  if (JSON.stringify(input).length > limits.json) throw new ConditionError("condition_too_large");
  const counter = { nodes: 0 };
  return parseNode(input, 1, counter);
}

function parseNode(element: unknown, depth: number, counter: { nodes: number }): Parsed {
  counter.nodes += 1;
  if (counter.nodes > limits.nodes) throw new ConditionError("condition_too_many_nodes");
  if (depth > limits.depth) throw new ConditionError("condition_too_deep");
  if (!element || typeof element !== "object" || Array.isArray(element)) {
    throw new ConditionError("condition_invalid");
  }
  const record = element as Record<string, unknown>;
  const keys = Object.keys(record);
  if ("all" in record || "any" in record) {
    if (keys.length !== 1) throw new ConditionError("condition_invalid");
    const list = record.all ?? record.any;
    if (!Array.isArray(list) || list.length === 0)
      throw new ConditionError("condition_empty_group");
    return {
      kind: "group",
      all: "all" in record,
      children: list.map((child) => parseNode(child, depth + 1, counter)),
    };
  }
  if (keys.some((key) => key !== "field" && key !== "op" && key !== "value")) {
    throw new ConditionError("condition_unknown_property");
  }
  if (typeof record.field !== "string" || typeof record.op !== "string") {
    throw new ConditionError("condition_invalid");
  }
  if (!fieldPattern.test(record.field)) throw new ConditionError("condition_invalid_field");
  const op = record.op;
  if (!(operators as readonly string[]).includes(op)) {
    throw new ConditionError("condition_invalid_operator");
  }
  if (op === "exists" || op === "notExists") {
    return { kind: "leaf", field: record.field, op, value: null, values: [] };
  }
  if (record.value === undefined || record.value === null) {
    throw new ConditionError("condition_value_required");
  }
  if (op === "in" || op === "notIn") {
    if (
      !Array.isArray(record.value) ||
      record.value.length === 0 ||
      record.value.length > limits.list
    ) {
      throw new ConditionError("condition_invalid_list");
    }
    return {
      kind: "leaf",
      field: record.field,
      op,
      value: null,
      values: record.value.map((item) => {
        const scalar = toScalar(item);
        if (scalar === null) throw new ConditionError("condition_invalid_value");
        return scalar;
      }),
    };
  }
  const value = toScalar(record.value);
  if (value === null) throw new ConditionError("condition_invalid_value");
  if ((op === "gt" || op === "lt" || op === "gte" || op === "lte") && !numberPattern.test(value)) {
    throw new ConditionError("condition_invalid_number");
  }
  if ((op === "before" || op === "after") && parseDate(value, new Date(0)) === null) {
    throw new ConditionError("condition_invalid_date");
  }
  return { kind: "leaf", field: record.field, op, value, values: [] };
}

function toScalar(value: unknown) {
  let text: string | null = null;
  if (typeof value === "string") text = value;
  else if (typeof value === "number" && Number.isFinite(value)) text = String(value);
  else if (typeof value === "boolean") text = value ? "true" : "false";
  return text !== null && text.length <= limits.value ? text : null;
}

export function parseDate(value: string | null | undefined, now: Date) {
  if (!value || !value.trim()) return null;
  const match = relative.exec(value.trim());
  if (match) {
    if (!match[1]) return now.getTime();
    const days = Number(match[2]);
    return now.getTime() + (match[1] === "-" ? -days : days) * 86_400_000;
  }
  if (!/^\d{4}-\d{2}-\d{2}/.test(value.trim())) return null;
  const parsed = Date.parse(value.trim());
  return Number.isNaN(parsed) ? null : parsed;
}

export function resolveField(field: string, resource: InventoryResource) {
  if (field.startsWith("attributes.")) {
    const key = field.slice("attributes.".length);
    return Object.prototype.hasOwnProperty.call(resource.attributes, key)
      ? resource.attributes[key]
      : null;
  }
  switch (field) {
    case "name":
      return resource.name;
    case "provider":
      return resource.provider;
    case "kind":
      return resource.kind;
    case "externalId":
      return resource.externalId;
    default:
      return null;
  }
}

export function matches(node: Parsed, resource: InventoryResource, now: Date): boolean {
  if (node.kind === "group") {
    return node.all
      ? node.children.every((child) => matches(child, resource, now))
      : node.children.some((child) => matches(child, resource, now));
  }
  return check(node, resolveField(node.field, resource), now);
}

function check(leaf: Extract<Parsed, { kind: "leaf" }>, actual: string | null, now: Date) {
  const present = Boolean(actual);
  const lower = (actual ?? "").toLowerCase();
  const expected = (leaf.value ?? "").toLowerCase();
  const inList = leaf.values.some((item) => item.toLowerCase() === lower);
  switch (leaf.op) {
    case "exists":
      return present;
    case "notExists":
      return !present;
    case "equals":
      return present && lower === expected;
    case "notEquals":
      return !present || lower !== expected;
    case "contains":
      return present && lower.includes(expected);
    case "notContains":
      return !present || !lower.includes(expected);
    case "in":
      return present && inList;
    case "notIn":
      return !present || !inList;
    case "gt":
    case "lt":
    case "gte":
    case "lte":
      return compare(actual, leaf.value, leaf.op);
    default:
      return compareDates(actual, leaf.value, leaf.op, now);
  }
}

function compare(actual: string | null, expected: string | null, op: string) {
  if (!actual || !numberPattern.test(actual) || !expected) return false;
  const left = Number(actual);
  const right = Number(expected);
  if (op === "gt") return left > right;
  if (op === "lt") return left < right;
  if (op === "gte") return left >= right;
  return left <= right;
}

function compareDates(actual: string | null, expected: string | null, op: string, now: Date) {
  const left = parseDate(actual, now);
  const right = parseDate(expected, now);
  if (left === null || right === null) return false;
  return op === "before" ? left < right : left > right;
}

export type TestRun = {
  outcome: "pass" | "fail" | "notRun";
  detail: string;
  passing: InventoryResource[];
  failing: InventoryResource[];
};

export function runTest(
  resourceKind: string,
  provider: string | null,
  filter: unknown,
  assertion: unknown,
  resources: readonly InventoryResource[],
  now: Date,
): TestRun {
  const filterNode = parseCondition(filter, true);
  const assertionNode = parseCondition(assertion, false);
  if (!assertionNode) throw new ConditionError("condition_required");
  const scope = resources.filter(
    (item) =>
      (resourceKind === "*" || item.kind.toLowerCase() === resourceKind.toLowerCase()) &&
      (!provider || item.provider.toLowerCase() === provider.toLowerCase()) &&
      (!filterNode || matches(filterNode, item, now)),
  );
  if (scope.length === 0) {
    return {
      outcome: "notRun",
      detail: "No matching resources in the inventory.",
      passing: [],
      failing: [],
    };
  }
  const passing = scope.filter((item) => matches(assertionNode, item, now));
  const failing = scope.filter((item) => !matches(assertionNode, item, now));
  const more = failing.length > 5 ? ", …" : "";
  const detail =
    failing.length === 0
      ? `${passing.length} of ${scope.length} resources pass.`
      : `${failing.length} of ${scope.length} resources fail: ${failing
          .slice(0, 5)
          .map((item) => item.name)
          .join(", ")}${more}`;
  return { outcome: failing.length === 0 ? "pass" : "fail", detail, passing, failing };
}

export function isGroup(condition: Condition): condition is ConditionGroup {
  return "all" in condition || "any" in condition;
}
