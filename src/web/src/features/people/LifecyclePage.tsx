import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { Pill } from "../../shared/ui/ui";
import { ChecklistCard } from "./LifecycleParts";
import {
  slaTone,
  type EmployeeGroup,
  type LifecycleOverview,
  type LifecycleSettings,
  type LifecycleSyncResult,
} from "./lifecycleTypes";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

const splitList = (value: string) =>
  value
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean);

export function LifecyclePage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [data, setData] = useState<LifecycleOverview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [filter, setFilter] = useState<"open" | "all">("open");
  const canWrite = Boolean(workspace?.canWrite);

  const load = () => {
    if (!tenantId) return;
    void api<LifecycleOverview>("/api/v1/lifecycle/overview", { tenantId })
      .then(setData)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function sync() {
    if (!tenantId) return;
    setError(null);
    try {
      const result = await api<LifecycleSyncResult>("/api/v1/lifecycle/sync", {
        method: "POST",
        tenantId,
      });
      setNotice(
        t("lifecycle.synced", {
          onboarding: result.onboardingStarted,
          offboarding: result.offboardingStarted,
        }),
      );
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  async function refreshTests() {
    if (!tenantId) return;
    setError(null);
    try {
      await api("/api/v1/lifecycle/tests/refresh", { method: "POST", tenantId });
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  const checklists = (data?.checklists ?? []).filter(
    (item) => filter === "all" || item.state === "Open",
  );

  return (
    <>
      <div className="head">
        <div>
          <Link to="/people" className="m">
            ← {t("nav.people")}
          </Link>
          <h1>{t("lifecycle.title")}</h1>
          <div className="sub">{t("lifecycle.sub")}</div>
        </div>
        {canWrite && (
          <button className="btn primary" type="button" onClick={() => void sync()}>
            {t("lifecycle.sync")}
          </button>
        )}
      </div>
      {error && (
        <div className="error" role="alert">
          {error === "forbidden" ? t("errors.forbidden") : `${t("errors.load")} (${error})`}
        </div>
      )}
      {notice && (
        <div className="notice" role="status">
          {notice}
        </div>
      )}
      {data && tenantId && (
        <>
          <div className="card">
            <h3>
              {t("lifecycle.tests")}
              {canWrite && (
                <span className="r">
                  <button className="btn" type="button" onClick={() => void refreshTests()}>
                    {t("lifecycle.refreshTests")}
                  </button>
                </span>
              )}
            </h3>
            <ul>
              {data.tests.map((test) => (
                <li key={test.name}>
                  <Pill status={slaTone(test.outcome)}>
                    {t(`lifecycle.outcomes.${test.outcome}`, { defaultValue: test.outcome })}
                  </Pill>{" "}
                  <b>{test.name}</b> <span className="id">{test.controlCode}</span>
                  <div className="m">{test.detail}</div>
                </li>
              ))}
            </ul>
            <div className="id">{t("lifecycle.revokerMode", { mode: data.revokerMode })}</div>
          </div>
          {canWrite && (
            <SettingsForm
              key={JSON.stringify(data.settings)}
              settings={data.settings}
              tenantId={tenantId}
              onSaved={() => {
                setNotice(t("lifecycle.settingsSaved"));
                load();
              }}
              onError={setError}
            />
          )}
          <div className="card" style={{ marginTop: 14 }}>
            <h3>
              {t("lifecycle.checklists")}
              <span className="r">
                <select
                  aria-label={t("lifecycle.filter")}
                  value={filter}
                  onChange={(event) => setFilter(event.target.value as "open" | "all")}
                >
                  <option value="open">{t("lifecycle.filterOpen")}</option>
                  <option value="all">{t("lifecycle.filterAll")}</option>
                </select>
              </span>
            </h3>
            {checklists.length === 0 ? (
              <div className="empty">{t("lifecycle.noChecklists")}</div>
            ) : (
              checklists.map((checklist) => (
                <ChecklistCard
                  key={checklist.id}
                  checklist={checklist}
                  tenantId={tenantId}
                  canWrite={canWrite}
                  onChanged={load}
                  onError={setError}
                />
              ))
            )}
          </div>
          <div className="card" style={{ marginTop: 14 }}>
            <h3>{t("lifecycle.groupsTitle")}</h3>
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>{t("lifecycle.group")}</th>
                    <th>{t("lifecycle.match")}</th>
                    <th>{t("lifecycle.requiredPolicies")}</th>
                    <th>{t("lifecycle.requiredTraining")}</th>
                    <th>{t("lifecycle.members")}</th>
                  </tr>
                </thead>
                <tbody>
                  {data.groups.map((group) => (
                    <tr key={group.code}>
                      <td>
                        <b>{group.name}</b>
                        <div className="id">{group.code}</div>
                        <div className="m">{group.description}</div>
                      </td>
                      <td className="m">
                        {[...group.departments, ...group.roleKeywords].join(", ") || "—"}
                      </td>
                      <td>{group.policyCodes.join(", ")}</td>
                      <td>{group.trainingCodes.join(", ")}</td>
                      <td>{group.memberCount}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {canWrite && <GroupForm tenantId={tenantId} onSaved={load} onError={setError} />}
          </div>
          <div className="card" style={{ marginTop: 14 }}>
            <h3>{t("lifecycle.templates")}</h3>
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>{t("lifecycle.step")}</th>
                    <th>{t("lifecycle.kind")}</th>
                    <th>{t("lifecycle.slaHours")}</th>
                    <th>{t("lifecycle.group")}</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {data.templates.map((step) => (
                    <tr key={step.id}>
                      <td>
                        <b>{step.title}</b>
                        <div className="id">
                          {step.code} ·{" "}
                          {t(`lifecycle.categories.${step.category}`, {
                            defaultValue: step.category,
                          })}
                          {step.revokesAccess ? ` · ${t("lifecycle.revokesAccess")}` : ""}
                          {step.perAccount ? ` · ${t("lifecycle.perAccount")}` : ""}
                        </div>
                      </td>
                      <td>{t(`lifecycle.kinds.${step.kind}`, { defaultValue: step.kind })}</td>
                      <td>
                        {step.revokesAccess
                          ? t("lifecycle.accessSlaShort", {
                              hours: data.settings.offboardingAccessSlaHours,
                            })
                          : step.slaHours}
                      </td>
                      <td>{step.groupCode || t("lifecycle.allGroups")}</td>
                      <td>
                        {canWrite && (
                          <button
                            className="linkish"
                            type="button"
                            onClick={() => {
                              void api(`/api/v1/lifecycle/templates/${step.id}`, {
                                method: "DELETE",
                                tenantId,
                              })
                                .then(load)
                                .catch((caught: unknown) =>
                                  setError(caught instanceof ApiError ? caught.code : "load"),
                                );
                            }}
                          >
                            {t("lifecycle.remove")}
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {canWrite && (
              <TemplateForm
                tenantId={tenantId}
                groups={data.groups}
                onSaved={load}
                onError={setError}
              />
            )}
          </div>
        </>
      )}
    </>
  );
}

function SettingsForm({
  settings,
  tenantId,
  onSaved,
  onError,
}: {
  settings: LifecycleSettings;
  tenantId: string;
  onSaved: () => void;
  onError: (value: string | null) => void;
}) {
  const { t } = useTranslation();
  const [value, setValue] = useState(settings);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    onError(null);
    try {
      await api("/api/v1/lifecycle/settings", { method: "POST", tenantId, body: value });
      onSaved();
    } catch (caught: unknown) {
      onError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  return (
    <form className="card" style={{ marginTop: 14 }} onSubmit={(event) => void save(event)}>
      <h3>{t("lifecycle.settings")}</h3>
      <div className="split-fields">
        <label className="field">
          <span>{t("lifecycle.accessSla")}</span>
          <select
            value={value.offboardingAccessSlaHours}
            onChange={(event) =>
              setValue({ ...value, offboardingAccessSlaHours: Number(event.target.value) })
            }
          >
            {[4, 8, 24, 48, 72].map((hours) => (
              <option key={hours} value={hours}>
                {t("lifecycle.hours", { count: hours })}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>{t("lifecycle.completeSla")}</span>
          <input
            type="number"
            min={1}
            max={1440}
            value={value.offboardingCompleteSlaHours}
            onChange={(event) =>
              setValue({ ...value, offboardingCompleteSlaHours: Number(event.target.value) })
            }
          />
        </label>
        <label className="field">
          <span>{t("lifecycle.onboardingDays")}</span>
          <input
            type="number"
            min={1}
            max={120}
            value={value.onboardingDays}
            onChange={(event) => setValue({ ...value, onboardingDays: Number(event.target.value) })}
          />
        </label>
      </div>
      <button className="btn primary" type="submit">
        {t("lifecycle.save")}
      </button>
    </form>
  );
}

function GroupForm({
  tenantId,
  onSaved,
  onError,
}: {
  tenantId: string;
  onSaved: () => void;
  onError: (value: string | null) => void;
}) {
  const { t } = useTranslation();
  const empty = {
    code: "",
    name: "",
    departments: "",
    roleKeywords: "",
    policies: "",
    training: "",
  };
  const [value, setValue] = useState(empty);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    onError(null);
    try {
      await api<EmployeeGroup>("/api/v1/lifecycle/groups", {
        method: "POST",
        tenantId,
        body: {
          code: value.code,
          name: value.name,
          departments: splitList(value.departments),
          roleKeywords: splitList(value.roleKeywords),
          policyCodes: splitList(value.policies),
          trainingCodes: splitList(value.training),
        },
      });
      setValue(empty);
      onSaved();
    } catch (caught: unknown) {
      onError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  const field = (key: keyof typeof empty, label: string) => (
    <label className="field">
      <span>{label}</span>
      <input
        required={key === "code" || key === "name"}
        value={value[key]}
        onChange={(event) => setValue({ ...value, [key]: event.target.value })}
      />
    </label>
  );

  return (
    <form onSubmit={(event) => void save(event)} style={{ marginTop: 10 }}>
      <b>{t("lifecycle.saveGroup")}</b>
      <div className="split-fields">
        {field("code", t("lifecycle.code"))}
        {field("name", t("lifecycle.name"))}
        {field("departments", t("lifecycle.departments"))}
      </div>
      <div className="split-fields">
        {field("roleKeywords", t("lifecycle.roleKeywords"))}
        {field("policies", t("lifecycle.requiredPolicies"))}
        {field("training", t("lifecycle.requiredTraining"))}
      </div>
      <button className="btn" type="submit">
        {t("lifecycle.saveGroup")}
      </button>
    </form>
  );
}

function TemplateForm({
  tenantId,
  groups,
  onSaved,
  onError,
}: {
  tenantId: string;
  groups: EmployeeGroup[];
  onSaved: () => void;
  onError: (value: string | null) => void;
}) {
  const { t } = useTranslation();
  const empty = {
    kind: "Onboarding",
    code: "",
    title: "",
    category: "Policy",
    slaHours: 72,
    groupCode: "",
    revokesAccess: false,
    perAccount: false,
  };
  const [value, setValue] = useState(empty);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    onError(null);
    try {
      await api("/api/v1/lifecycle/templates", { method: "POST", tenantId, body: value });
      setValue(empty);
      onSaved();
    } catch (caught: unknown) {
      onError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  return (
    <form onSubmit={(event) => void save(event)} style={{ marginTop: 10 }}>
      <b>{t("lifecycle.addStep")}</b>
      <div className="split-fields">
        <label className="field">
          <span>{t("lifecycle.kind")}</span>
          <select
            value={value.kind}
            onChange={(event) => setValue({ ...value, kind: event.target.value })}
          >
            <option value="Onboarding">{t("lifecycle.kinds.Onboarding")}</option>
            <option value="Offboarding">{t("lifecycle.kinds.Offboarding")}</option>
          </select>
        </label>
        <label className="field">
          <span>{t("lifecycle.code")}</span>
          <input
            required
            value={value.code}
            onChange={(event) => setValue({ ...value, code: event.target.value })}
          />
        </label>
        <label className="field">
          <span>{t("lifecycle.name")}</span>
          <input
            required
            value={value.title}
            onChange={(event) => setValue({ ...value, title: event.target.value })}
          />
        </label>
      </div>
      <div className="split-fields">
        <label className="field">
          <span>{t("lifecycle.category")}</span>
          <select
            value={value.category}
            onChange={(event) => setValue({ ...value, category: event.target.value })}
          >
            {["Policy", "Training", "Device", "Identity", "Screening", "Access", "Ownership"].map(
              (item) => (
                <option key={item} value={item}>
                  {t(`lifecycle.categories.${item}`)}
                </option>
              ),
            )}
          </select>
        </label>
        <label className="field">
          <span>{t("lifecycle.slaHours")}</span>
          <input
            type="number"
            min={0}
            max={2880}
            value={value.slaHours}
            onChange={(event) => setValue({ ...value, slaHours: Number(event.target.value) })}
          />
        </label>
        <label className="field">
          <span>{t("lifecycle.group")}</span>
          <select
            value={value.groupCode}
            onChange={(event) => setValue({ ...value, groupCode: event.target.value })}
          >
            <option value="">{t("lifecycle.allGroups")}</option>
            {groups.map((group) => (
              <option key={group.code} value={group.code}>
                {group.name}
              </option>
            ))}
          </select>
        </label>
      </div>
      {value.kind === "Offboarding" && (
        <div className="split-fields">
          <label className="field">
            <span>
              <input
                type="checkbox"
                checked={value.revokesAccess}
                onChange={(event) => setValue({ ...value, revokesAccess: event.target.checked })}
              />{" "}
              {t("lifecycle.revokesAccess")}
            </span>
          </label>
          <label className="field">
            <span>
              <input
                type="checkbox"
                checked={value.perAccount}
                onChange={(event) => setValue({ ...value, perAccount: event.target.checked })}
              />{" "}
              {t("lifecycle.perAccount")}
            </span>
          </label>
        </div>
      )}
      <button className="btn" type="submit">
        {t("lifecycle.addStep")}
      </button>
    </form>
  );
}
