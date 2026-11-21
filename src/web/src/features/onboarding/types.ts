export type SignupOptions = {
  identityMode: string;
  passwordRequired: boolean;
  regions: string[];
  minimumPasswordLength: number;
};

export type SignupAccepted = {
  signupId: string;
  status: string;
  email: string;
  expiresAt: string;
  devVerificationUrl: string | null;
};

export type SignupStatus = {
  signupId: string;
  status: string;
  email: string;
  tenantId: string | null;
  companyName: string;
  percent: number;
  currentStep: string;
  error: string | null;
  nextAction: string;
};

export type OnboardingOptions = {
  sizes: string[];
  industries: string[];
  cloudProviders: string[];
  dataTypes: string[];
  frameworks: string[];
};

export type OnboardingState = {
  companySize: string;
  industry: string;
  cloudProviders: string[];
  dataTypes: string[];
  frameworks: string[];
  scopedControls: string[];
  steps: string[];
  completedSteps: string[];
  auditTargetDate: string | null;
  dismissed: boolean;
  complete: boolean;
  percent: number;
  options: OnboardingOptions;
};

export type Recommendation = {
  frameworks: { frameworkKey: string; reason: string }[];
  scopedControls: string[];
};

export type GateDecision = {
  feature: string;
  allowed: boolean;
  used: number;
  limit: number | null;
  overLimit: boolean;
  upgradeTo: string | null;
};

export type ReadinessPlan = {
  targetDate: string;
  milestones: {
    key: string;
    title: string;
    dueOn: string;
    tasks: { title: string; dueOn: string }[];
  }[];
  tasksCreated: number;
  frameworks: GateDecision;
};

export type PlanEntitlements = {
  plan: string;
  frameworks: number | null;
  seats: number | null;
  integrations: number | null;
  trustCenter: boolean;
  apiAccess: boolean;
  monthlyPriceUsd: number | null;
};

export type Invoice = {
  id: string;
  number: string;
  amountUsd: number;
  status: string;
  issuedOn: string;
  url: string | null;
};

export type BillingOverview = {
  plan: string;
  status: string;
  provider: string;
  providerConfigured: boolean;
  entitlements: PlanEntitlements;
  usage: GateDecision[];
  plans: PlanEntitlements[];
  invoices: Invoice[];
  currentPeriodEnd: string | null;
};

export type ChangePlanResult = { plan: string; status: string; redirectUrl: string | null };

export type Invite = {
  id: string;
  email: string;
  role: string;
  state: string;
  invitedBy: string;
  createdAt: string;
  expiresAt: string;
};

export type CreatedInvite = { invite: Invite; devAcceptUrl: string | null; seats: GateDecision };

export type InvitePreview = {
  tenantName: string;
  email: string;
  role: string;
  state: string;
  passwordRequired: boolean;
  expiresAt: string;
};

export type AcceptInviteResult = { tenantId: string; email: string; nextAction: string };

export type SecuritySettings = {
  ssoEnforced: boolean;
  mode: string;
  domain: string;
  connected: boolean;
  scimTokenPrefix: string | null;
  scimTokenCreatedAt: string | null;
  scimEndpoint: string;
};

export type ScimToken = { token: string; prefix: string; createdAt: string };
