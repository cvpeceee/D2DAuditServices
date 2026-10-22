export type MeCard = {
  kind: string;
  title: string;
  status: string;
  dueOn: string | null;
  action: string;
  evidenceId: string | null;
  evidenceSha256: string | null;
};

export type TrainingModule = {
  id: string;
  code: string;
  title: string;
  type: string;
  assignedGroup: string;
  dueOn: string;
  assigned: number;
  completed: number;
  externalUrl: string | null;
};

export type TrainingDetail = Omit<TrainingModule, "completed"> & {
  slides: { title: string; body: string }[];
  questions: { prompt: string; choices: string[] }[];
  completed: boolean;
  score: number | null;
  evidenceId: string | null;
  evidenceSha256: string | null;
};

export type TrainingPeople = {
  items: {
    name: string;
    email: string;
    department: string;
    status: string;
    completedOn: string | null;
    dueOn: string | null;
  }[];
  total: number;
  page: number;
  pageSize: number;
};

export type AssetPage = {
  items: AssetItem[];
  total: number;
  page: number;
  pageSize: number;
};

export type AssetItem = {
  id: string;
  name: string;
  type: string;
  source: string;
  ownerName: string;
  ownerEmail: string;
  environment: string;
  classification: string;
  inScope: boolean;
  lastSeen: string;
};

export type WorkTask = {
  id: string;
  title: string;
  linkedKind: string;
  linkedCode: string;
  ownerName: string;
  ownerEmail: string;
  dueOn: string;
  status: string;
  recurrence: string;
  overdue: boolean;
};

export type Member = { id: string; name: string; email: string; role: string; active: boolean };

export type SsoStatus = { domain: string; connected: boolean; organizationId: string | null };

export type Grant = {
  id: string;
  email: string;
  name: string;
  auditName: string;
  firmName: string;
  periodStart: string;
  periodEnd: string;
  accessExpiresOn: string;
  frameworks: string[];
};
