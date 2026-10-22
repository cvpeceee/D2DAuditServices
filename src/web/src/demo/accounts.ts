/**
 * The public demo accepts only these example accounts.
 * The password is the documented local example password, not a customer secret.
 */
export const examplePassword = "Example-Only-1!";

export const exampleAccounts = [
  { email: "chandra@acme.example", name: "Chandra V.", role: "Admin" },
  { email: "auditor@northgate.example", name: "Rachel Ortiz", role: "Auditor" },
] as const;
