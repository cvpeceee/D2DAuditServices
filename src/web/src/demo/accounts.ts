import brand from "../../../seed/company.json";

/**
 * The public demo accepts only these example accounts.
 * The password is the documented local example password, not a customer secret.
 * Staff addresses follow brand.json so a renamed copy stays in step after rebuild.
 */
export const examplePassword = "Example-Only-1!";

export const exampleAccounts = [
  { email: `chandra.vell@${brand.domain}`, name: "Chandra Vell", role: "Admin" },
  { email: `priya.nair@${brand.domain}`, name: "Priya Nair", role: "ControlOwner" },
  { email: `lena.cho@${brand.domain}`, name: "Lena Cho", role: "ControlOwner" },
  { email: `marcus.lee@${brand.domain}`, name: "Marcus Lee", role: "ControlOwner" },
  { email: `dev.patel@${brand.domain}`, name: "Dev Patel", role: "Employee" },
  { email: "auditor@northgate.example", name: "Rachel Ortiz", role: "Auditor" },
] as const;
