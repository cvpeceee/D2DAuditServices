export type VendorRiskRow = {
  id: string;
  name: string;
  service: string;
  criticality: string;
  dataAccess: string;
  reviewState: string;
  decision: string;
  inherentScore: number;
  tier: string;
  ownerName: string;
  ownerEmail: string;
  contractRenewalOn: string | null;
  renewalState: string;
  dataCategories: string[];
  subprocessor: boolean;
  reviewCadenceMonths: number;
  lastReviewedOn: string | null;
  nextReviewOn: string;
  reviewStatus: string;
  contactName: string;
  contactEmail: string;
  latestAssessmentStatus: string;
  latestAssessmentScore: number | null;
};

export type VendorDiscovery = {
  id: string;
  name: string;
  source: string;
  detail: string;
  suggestedCriticality: string;
  dataCategories: string[];
  status: string;
  firstSeenAt: string;
  decidedAt: string | null;
  decidedBy: string;
  vendorId: string | null;
};

export type VendorDiscoveryScanResult = {
  found: number;
  added: number;
  sources: string[];
  items: VendorDiscovery[];
};

export type QuestionnaireQuestion = {
  code: string;
  domain: string;
  text: string;
  weight: number;
  critical: boolean;
};

export type QuestionnaireTemplate = {
  code: string;
  name: string;
  description: string;
  questions: QuestionnaireQuestion[];
};

export type VendorAnswer = { questionCode: string; answer: string; comment: string };

export type VendorAssessment = {
  id: string;
  vendorId: string;
  vendorName: string;
  templateCode: string;
  templateName: string;
  contactName: string;
  contactEmail: string;
  status: string;
  sentAt: string;
  sentBy: string;
  expiresAt: string;
  submittedAt: string | null;
  reviewedAt: string | null;
  reviewedBy: string;
  reviewNote: string;
  score: number | null;
  rating: string;
  answered: number;
  total: number;
  flagged: string[];
  deliveryReceipt: string;
  answers: VendorAnswer[];
};

export type SentVendorQuestionnaire = { assessment: VendorAssessment; portalPath: string };

export type VendorReviewDocument = {
  id: string;
  vendorId: string;
  vendorName: string;
  assessmentId: string | null;
  kind: string;
  fileName: string;
  sizeBytes: number;
  sha256: string;
  uploadedAt: string;
  uploadedBy: string;
  reportPeriod: string;
  opinion: string;
  exceptions: string[];
  cuecs: string[];
  summaryText: string;
  summaryMode: string;
  summarizedAt: string | null;
  signedOffBy: string;
  signedOffAt: string | null;
  signOffNote: string;
};

export type VendorReminder = {
  id: string;
  title: string;
  linkedCode: string;
  dueOn: string;
  status: string;
};

export type VendorReminderSyncResult = { created: number; reminders: VendorReminder[] };

export type VendorRiskDetail = {
  vendor: VendorRiskRow;
  assessments: VendorAssessment[];
  documents: VendorReviewDocument[];
  reminders: VendorReminder[];
};

export type VendorPortalView = {
  companyName: string;
  vendorName: string;
  contactName: string;
  status: string;
  expiresAt: string;
  template: QuestionnaireTemplate;
  answers: VendorAnswer[];
  documents: { kind: string; fileName: string; uploadedAt: string }[];
};

export const documentKinds = ["Soc2", "Iso27001", "PenTest", "Other"] as const;
export const answerValues = ["Yes", "Partial", "No", "NotApplicable"] as const;

export function riskTone(value: string) {
  if (["Tier1", "High", "Overdue", "Lapsed", "Expired", "Rejected", "Revoked"].includes(value))
    return "fail";
  if (["Tier2", "Moderate", "DueSoon", "Upcoming", "Submitted", "InProgress"].includes(value))
    return "warn";
  if (["Low", "Current", "Reviewed", "Accepted", "Approved"].includes(value)) return "pass";
  return "info";
}

export function readFileAsBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const result = String(reader.result ?? "");
      resolve(result.includes(",") ? result.slice(result.indexOf(",") + 1) : result);
    };
    reader.onerror = () => reject(reader.error ?? new Error("read"));
    reader.readAsDataURL(file);
  });
}

export const maxDocumentBytes = 700 * 1024;
