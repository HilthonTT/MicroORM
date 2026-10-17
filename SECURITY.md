# Security policy

## Supported versions

Security fixes are made in the latest release. Before 1.0, only the most recent minor version is supported.

## Reporting a vulnerability

**Please do not open a public issue.** Report vulnerabilities privately through
[GitHub Security Advisories](https://github.com/HilthonTT/MicroORM/security/advisories/new).

Include the affected version, a description of the impact, and a minimal reproduction. You can
expect an acknowledgement within 72 hours and a fix or mitigation plan within 14 days for
confirmed issues. We'll credit you in the advisory unless you prefer otherwise.

## Scope

Of particular interest is any way to get **user-controlled data into SQL text** through the
public API without calling `Sql.Raw`. For example: through `Sql` interpolation, the expression
translator, `Sql.Identifier` quoting, or mapping. `Sql.Raw` is a documented trusted-input escape
hatch, so passing user input to it is a usage error, not a MicroORM vulnerability.
