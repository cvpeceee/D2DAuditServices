import { useEffect, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, api } from "../../shared/api/client";
import { Pill } from "../../shared/ui/ui";
import { isGroup, operators, type Condition, type ConditionLeaf } from "./conditions";

export type ResourceType = { provider: string; kind: string; count: number; fields: string[] };
export type CustomTest = {
  id: string;
  key: string;
  name: string;
  description: string;
  resourceKind: string;
  provider: string | null;
  filter: Condition | null;
  assertion: Condition;
  severity: string;
  controlCodes: string[];
  remediation: string;
  enabled: boolean;
  outcome: string;
  detail: string;
  createdByName: string;
  updatedAt: string;
};
type PreviewResource = { provider: string; kind: string; name: string; externalId: string };
type Preview = {
  outcome: string;
  detail: string;
  total: number;
  passing: PreviewResource[];
  failing: PreviewResource[];
};
type Row = { field: string; op: string; value: string };
type Block = { mode: "all" | "any"; rows: Row[] };

const severities = ["Low", "Medium", "High", "Critical"];
const noValue = new Set(["exists", "notExists"]);
const listOps = new Set(["in", "notIn"]);

function toBlock(condition: Condition | null): Block {
  if (!condition) return { mode: "all", rows: [] };
  const mode = isGroup(condition) && "any" in condition ? "any" : "all";
  const children: Condition[] = isGroup(condition)
    ? "any" in condition
      ? condition.any
      : condition.all
    : [condition];
  return {
    mode,
    rows: children
      .filter((child): child is ConditionLeaf => !isGroup(child))
      .map((leaf) => ({
        field: leaf.field,
        op: leaf.op,
        value: Array.isArray(leaf.value) ? leaf.value.join(", ") : String(leaf.value ?? ""),
      })),
  };
}

function toCondition(block: Block): Condition | null {
  const leaves = block.rows
    .filter((row) => row.field.trim())
    .map((row): ConditionLeaf => {
      if (noValue.has(row.op)) return { field: row.field.trim(), op: row.op };
      if (listOps.has(row.op))
        return {
          field: row.field.trim(),
          op: row.op,
          value: row.value
            .split(",")
            .map((item) => item.trim())
            .filter(Boolean),
        };
      return { field: row.field.trim(), op: row.op, value: row.value };
    });
  if (leaves.length === 0) return null;
  return block.mode === "any" ? { any: leaves } : { all: leaves };
}

function ConditionEditor({
  label,
  block,
  fields,
  onChange,
}: {
  label: string;
  block: Block;
  fields: string[];
  onChange: (next: Block) => void;
}) {
  const { t } = useTranslation();
  const update = (index: number, patch: Partial<Row>) =>
    onChange({
      ...block,
      rows: block.rows.map((row, at) => (at === index ? { ...row, ...patch } : row)),
    });
  return (
    <fieldset className="stack" style={{ border: "1px solid var(--line, #ddd)", padding: 10 }}>
      <legend>{label}</legend>
      <label className="field">
        <span>{t("assurance.tests.match")}</span>
        <select
          aria-label={`${label} ${t("assurance.tests.match")}`}
          value={block.mode}
          onChange={(event) => onChange({ ...block, mode: event.target.value as "all" | "any" })}
        >
          <option value="all">{t("assurance.tests.all")}</option>
          <option value="any">{t("assurance.tests.any")}</option>
        </select>
      </label>
      {block.rows.map((row, index) => (
        <div className="grid" key={index} style={{ gridTemplateColumns: "2fr 1.2fr 2fr auto" }}>
          <input
            aria-label={t("assurance.tests.field")}
            list="custom-test-fields"
            value={row.field}
            onChange={(event) => update(index, { field: event.target.value })}
          />
          <select
            aria-label={t("assurance.tests.operator")}
            value={row.op}
            onChange={(event) => update(index, { op: event.target.value })}
          >
            {operators.map((op) => (
              <option key={op} value={op}>
                {t(`assurance.ops.${op}`, { defaultValue: op })}
              </option>
            ))}
          </select>
          <input
            aria-label={t("assurance.tests.value")}
            value={row.value}
            disabled={noValue.has(row.op)}
            placeholder={listOps.has(row.op) ? "a, b, c" : ""}
            onChange={(event) => update(index, { value: event.target.value })}
          />
          <button
            className="btn"
            type="button"
            onClick={() => onChange({ ...block, rows: block.rows.filter((_, at) => at !== index) })}
          >
            {t("assurance.tests.remove")}
          </button>
        </div>
      ))}
      <div>
        <button
          className="btn"
          type="button"
          onClick={() =>
            onChange({
              ...block,
              rows: [...block.rows, { field: fields[0] ?? "name", op: "equals", value: "" }],
            })
          }
        >
          {t("assurance.tests.addCondition")}
        </button>
      </div>
    </fieldset>
  );
}

export function CustomTestBuilder({
  tenantId,
  canWrite,
  onChanged,
}: {
  tenantId: string | null;
  canWrite: boolean;
  onChanged: () => void;
}) {
  const { t } = useTranslation();
  const [types, setTypes] = useState<ResourceType[]>([]);
  const [tests, setTests] = useState<CustomTest[]>([]);
  const [editing, setEditing] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [typeKey, setTypeKey] = useState("");
  const [filter, setFilter] = useState<Block>({ mode: "all", rows: [] });
  const [assertion, setAssertion] = useState<Block>({ mode: "all", rows: [] });
  const [severity, setSeverity] = useState("Medium");
  const [controls, setControls] = useState("");
  const [remediation, setRemediation] = useState("");
  const [preview, setPreview] = useState<Preview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = () => {
    if (!tenantId) return;
    void api<CustomTest[]>("/api/v1/tests/custom", { tenantId })
      .then(setTests)
      .catch(() => setTests([]));
  };

  useEffect(() => {
    if (!tenantId) return;
    void api<ResourceType[]>("/api/v1/tests/resource-types", { tenantId })
      .then((value) => {
        setTypes(value);
        setTypeKey(
          (current) => current || (value[0] ? `${value[0].provider}|${value[0].kind}` : ""),
        );
      })
      .catch(() => setTypes([]));
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  const [provider, kind] = typeKey.split("|");
  const fields = types.find((type) => type.provider === provider && type.kind === kind)?.fields ?? [
    "name",
  ];

  const reset = () => {
    setEditing(null);
    setName("");
    setDescription("");
    setFilter({ mode: "all", rows: [] });
    setAssertion({ mode: "all", rows: [] });
    setSeverity("Medium");
    setControls("");
    setRemediation("");
    setPreview(null);
    setError(null);
  };

  const edit = (test: CustomTest) => {
    setOpen(true);
    setEditing(test.id);
    setName(test.name);
    setDescription(test.description);
    setTypeKey(`${test.provider ?? ""}|${test.resourceKind}`);
    setFilter(toBlock(test.filter));
    setAssertion(toBlock(test.assertion));
    setSeverity(test.severity);
    setControls(test.controlCodes.join(", "));
    setRemediation(test.remediation);
    setPreview(null);
    setError(null);
  };

  const body = () => ({
    name,
    description,
    resourceKind: kind ?? "",
    provider: provider || null,
    filter: toCondition(filter),
    assertion: toCondition(assertion),
    severity,
    controlCodes: controls
      .split(",")
      .map((item) => item.trim())
      .filter(Boolean),
    remediation,
  });

  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try {
      await action();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "save");
    } finally {
      setBusy(false);
    }
  };

  const runPreview = () =>
    run(async () => {
      const { resourceKind, provider: p, filter: f, assertion: a } = body();
      setPreview(
        await api<Preview>("/api/v1/tests/custom/preview", {
          method: "POST",
          tenantId,
          body: { resourceKind, provider: p, filter: f, assertion: a },
        }),
      );
    });

  const save = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => {
      await api<CustomTest>(editing ? `/api/v1/tests/custom/${editing}` : "/api/v1/tests/custom", {
        method: editing ? "PUT" : "POST",
        tenantId,
        body: body(),
      });
      reset();
      setOpen(false);
      load();
      onChanged();
    });
  };

  const remove = (test: CustomTest) =>
    void run(async () => {
      await api(`/api/v1/tests/custom/${test.id}`, { method: "DELETE", tenantId });
      if (editing === test.id) reset();
      load();
      onChanged();
    });

  return (
    <div className="card" style={{ marginBottom: 14 }}>
      <div className="head" style={{ marginBottom: 8 }}>
        <div>
          <h3>{t("assurance.tests.customTitle")}</h3>
          <div className="sub">{t("assurance.tests.customSub")}</div>
        </div>
        {canWrite && !open && (
          <div className="act">
            <button
              className="btn primary"
              type="button"
              onClick={() => {
                reset();
                setOpen(true);
              }}
            >
              {t("assurance.tests.new")}
            </button>
          </div>
        )}
      </div>
      {tests.length === 0 ? (
        <div className="empty">{t("assurance.tests.noCustom")}</div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t("testsPage.name")}</th>
                <th>{t("assurance.tests.resource")}</th>
                <th>{t("assurance.tests.severity")}</th>
                <th>{t("testsPage.control")}</th>
                <th>{t("testsPage.outcome")}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {tests.map((test) => (
                <tr key={test.id}>
                  <td>
                    <div className="t">{test.name}</div>
                    <div className="m">{test.detail}</div>
                  </td>
                  <td className="m">
                    {test.provider ? `${test.provider} · ` : ""}
                    {test.resourceKind}
                  </td>
                  <td>
                    <span className="tag">{test.severity}</span>
                  </td>
                  <td className="id">{test.controlCodes.join(", ")}</td>
                  <td>
                    <Pill status={test.outcome}>
                      {t(`status.${test.outcome}`, { defaultValue: test.outcome })}
                    </Pill>
                  </td>
                  <td>
                    {canWrite && (
                      <>
                        <button className="btn" type="button" onClick={() => edit(test)}>
                          {t("assurance.tests.edit")}
                        </button>{" "}
                        <button
                          className="btn"
                          type="button"
                          disabled={busy}
                          onClick={() => remove(test)}
                        >
                          {t("assurance.tests.delete")}
                        </button>
                      </>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {open && canWrite && (
        <form className="stack" onSubmit={save} style={{ marginTop: 14 }}>
          <h3>{editing ? t("assurance.tests.editTitle") : t("assurance.tests.new")}</h3>
          <datalist id="custom-test-fields">
            {fields.map((field) => (
              <option key={field} value={field} />
            ))}
          </datalist>
          <div className="grid" style={{ gridTemplateColumns: "1fr 1fr" }}>
            <label className="field">
              <span>{t("assurance.tests.name")}</span>
              <input required value={name} onChange={(event) => setName(event.target.value)} />
            </label>
            <label className="field">
              <span>{t("assurance.tests.resource")}</span>
              <select value={typeKey} onChange={(event) => setTypeKey(event.target.value)}>
                {types.map((type) => (
                  <option
                    key={`${type.provider}|${type.kind}`}
                    value={`${type.provider}|${type.kind}`}
                  >
                    {type.provider} · {type.kind} ({type.count})
                  </option>
                ))}
              </select>
            </label>
          </div>
          <label className="field">
            <span>{t("assurance.tests.description")}</span>
            <input value={description} onChange={(event) => setDescription(event.target.value)} />
          </label>
          <ConditionEditor
            label={t("assurance.tests.filter")}
            block={filter}
            fields={fields}
            onChange={setFilter}
          />
          <ConditionEditor
            label={t("assurance.tests.assertion")}
            block={assertion}
            fields={fields}
            onChange={setAssertion}
          />
          <div className="grid" style={{ gridTemplateColumns: "1fr 2fr" }}>
            <label className="field">
              <span>{t("assurance.tests.severity")}</span>
              <select value={severity} onChange={(event) => setSeverity(event.target.value)}>
                {severities.map((item) => (
                  <option key={item} value={item}>
                    {item}
                  </option>
                ))}
              </select>
            </label>
            <label className="field">
              <span>{t("assurance.tests.controls")}</span>
              <input
                value={controls}
                placeholder="CR-02, VM-05"
                onChange={(event) => setControls(event.target.value)}
              />
            </label>
          </div>
          <label className="field">
            <span>{t("assurance.tests.remediation")}</span>
            <textarea
              value={remediation}
              onChange={(event) => setRemediation(event.target.value)}
            />
          </label>
          {error && (
            <div className="error" role="alert">
              {t(`assurance.errors.${error}`, { defaultValue: error })}
            </div>
          )}
          <div className="act">
            <button className="btn" type="button" disabled={busy} onClick={() => void runPreview()}>
              {t("assurance.tests.preview")}
            </button>
            <button className="btn primary" type="submit" disabled={busy}>
              {t("assurance.tests.save")}
            </button>
            <button
              className="btn"
              type="button"
              onClick={() => {
                reset();
                setOpen(false);
              }}
            >
              {t("assurance.cancel")}
            </button>
          </div>
          {preview && (
            <div className="notice" aria-label={t("assurance.tests.previewResult")}>
              <div>
                <Pill status={preview.outcome}>
                  {t(`status.${preview.outcome}`, { defaultValue: preview.outcome })}
                </Pill>{" "}
                {preview.detail}
              </div>
              <div className="grid" style={{ gridTemplateColumns: "1fr 1fr", marginTop: 8 }}>
                <div>
                  <strong>{t("assurance.tests.passing", { count: preview.passing.length })}</strong>
                  <ul>
                    {preview.passing.map((item) => (
                      <li key={item.externalId}>{item.name}</li>
                    ))}
                  </ul>
                </div>
                <div>
                  <strong>{t("assurance.tests.failing", { count: preview.failing.length })}</strong>
                  <ul>
                    {preview.failing.map((item) => (
                      <li key={item.externalId}>{item.name}</li>
                    ))}
                  </ul>
                </div>
              </div>
            </div>
          )}
        </form>
      )}
    </div>
  );
}
