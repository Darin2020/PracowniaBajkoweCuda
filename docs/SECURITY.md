# Security

Pracownia Bajkowe Cuda targets **OWASP ASVS 4.0.3 Level 2** (docs/PLAN.md §8). This document is the brief for
external penetration testers and the status of every ASVS requirement that applies to this application.

> **Open items for the owner** (marked _TBD_ below): the reporting contact, how test accounts are handed out, and
> whether production is in scope.

## 1. Reporting a vulnerability

- Contact: _TBD (owner): a dedicated address for security reports._
- Please do not open public GitHub issues for vulnerabilities. GitHub private vulnerability reporting
  (Security → Report a vulnerability) may be used as well.
- Include the URL, the request (without real customer data), what you expected and what happened.

## 2. Penetration test brief

### 2.1 What the application is
A one-page website for a custom toy-sewing service. Public pages are prerendered static HTML in five languages
(`/en/`, `/de/`, `/fr/`, `/es/`, `/pl/`). Signed-in customers chat with the studio (text and photos) and follow their
orders. The studio owner (Admin) answers chats and manages orders. There is no shop, no card payment and no
payment data in the system: customers pay by bank transfer to details shown on the order.

### 2.2 Architecture
- One container (ASP.NET Core, .NET 10) serves the API (`/api/*`), the SignalR hub (`/hubs/chat`), health endpoints
  (`/health/live`, `/health/ready`) and the static Angular pages, all from one origin.
- Authentication: ASP.NET Core Identity with a cookie session (BFF; no tokens in the browser), optional Google and
  Facebook sign-in, TOTP two-factor authentication (mandatory for Admin).
- Data: Azure SQL (Entra ID authentication only), Azure Blob Storage (private containers, no shared keys), Azure Key
  Vault for secrets, Azure Communication Services for email. All access from the app uses a managed identity.
- Hosting: Azure Container Apps (TLS terminates at the ingress; plain HTTP is refused).

### 2.3 Scope
| Target | In scope |
|---|---|
| Staging: `https://ca-bajkowecuda-staging.nicemeadow-7d5e38ee.polandcentral.azurecontainerapps.io` | Yes |
| Production | _TBD (owner)_ |
| Azure control plane, the subscription, GitHub, third-party identity providers, Azure-managed email domains | No |

Rules of engagement:
- No denial-of-service or volumetric testing. Rate limits exist (§3, V11) and are in scope to verify, at low
  volume (`scripts/abuse-check.sh` shows the intended limits).
- Do not send email to addresses you do not own. Registration and password reset send real email.
- Staging scales to zero: the first request after a quiet period can take ~30 s.
- Uploaded images are re-encoded; do not upload illegal content.

### 2.4 Test accounts (staging)
_TBD (owner)_: either a pre-created customer and an Admin with 2FA (credentials handed over outside the repository),
or self-registration for customer accounts with Admin access granted on request. Admin rights come only from the
`Admin:Emails` setting (an allow-list of addresses), so an Admin test account needs the owner to add its address.

### 2.5 Interesting areas
- Access control between customers: conversations, messages, attachments (`/api/files/{id}`), orders (IDOR).
- Admin endpoints (`/api/admin/*`) and the `admin` SignalR group: Admin role **and** a second factor in the session.
- Sign-in, 2FA, recovery codes, password reset, external sign-in linking and re-authentication.
- Image upload (`/api/chat/attachments`, `/api/admin/orders/{id}/photos`): allow-list by magic bytes and re-encoding.
- CSP with a per-response nonce, antiforgery header on every unsafe request.

## 3. ASVS 4.0.3 Level 2 checklist

Status: ✅ met · 🟡 partly met / accepted risk (see note) · ❌ not met (planned) · N/A not applicable.
Requirements of a chapter that do not apply are summarized in its "N/A" line. Evidence points to code or tests.

### V1 Architecture, design and threat modeling
| Item | Status | Evidence / note |
|---|---|---|
| 1.1 Secure SDLC, threat model, documented trust boundaries | 🟡 | docs/PLAN.md §8 and this document; no formal threat model (single developer project). |
| 1.2.x Least-privilege service accounts, authenticated communication between components | ✅ | One user-assigned managed identity per environment with only AcrPull, Key Vault Secrets User, Storage Blob Data Contributor and a SQL user (infra/modules/identity.bicep). |
| 1.4.x Access control enforced at one trusted server-side point, deny by default | ✅ | `MapControllers().RequireAuthorization()` and `MapHub(...).RequireAuthorization()` (Program.cs); `[AllowAnonymous]` only on auth endpoints. |
| 1.5–1.14 Input, crypto, logging, data, communications, configuration architecture | ✅ | Covered item by item in V5–V14 below. |

### V2 Authentication
| Item | Status | Evidence / note |
|---|---|---|
| 2.1.1–2.1.6 Passwords ≥ 12 characters, up to 128+, any characters, change needs the current password | ✅ | `RequiredLength = 12`, no composition rules (Auth/AuthSetup.cs); `AccountController` change-password re-checks the current password. |
| 2.1.7 Passwords checked against breached-password lists | ❌ | Optional HIBP range check (PLAN §8.1) not implemented yet. Planned. |
| 2.1.8–2.1.12 Strength meter, no rules, paste allowed, show-password | 🟡 | No composition or rotation rules, paste and password managers work. No strength meter and no show/hide toggle (planned with 2.1.7). |
| 2.2.1 Anti-automation (rate limit, lockout) | ✅ | Auth endpoints 10/min per IP (sliding window), emails 3/h per address + IP, lockout 5 attempts → 15 min (Security/RateLimiting.cs, AuthSetup.cs). |
| 2.2.3 Notifications after authentication changes | 🟡 | Emails after a password change, a password reset and account deletion (Infrastructure/Emails). No email when 2FA or an external login changes (they are in the audit log). |
| 2.3.1 Generated initial secrets | N/A | Users choose their own passwords. |
| 2.4.1–2.4.5 Password storage | ✅ | ASP.NET Core Identity v3 hasher (PBKDF2-HMAC-SHA512, 100 000 iterations, per-user salt). |
| 2.5.1–2.5.7 Recovery: no secret questions, reset by an emailed one-time link, no hints, 2FA not bypassed by reset | ✅ | Identity data-protection tokens, single use (security stamp changes); a reset signs out other sessions; 2FA stays on. Token lifetime is the Identity default (1 day). |
| 2.7.x Out-of-band verifiers (email links) | ✅ | Confirmation and reset links are single-use, bound to the user and expire. |
| 2.8.1–2.8.6 TOTP | 🟡 | Identity authenticator (RFC 6238), 30 s steps, shared key generated by Identity; 2FA sign-in is rate limited and counts towards lockout. Identity does not block reuse of a code within its validity window (2.8.4). |
| 2.9, 2.10 Cryptographic verifiers, service authentication | ✅ | No passwords between services: managed identity to SQL, Blob, Key Vault, ACS. |
| 2.6.1–2.6.3 Look-up secrets (recovery codes) | 🟡 | 10 random codes, each usable once, regenerable (Auth/TwoFactorController.cs). Identity stores them unhashed in `AspNetUserTokens` (2.6.2 asks for hashing); the database is Entra-only and encrypted at rest. |

### V3 Session management
| Item | Status | Evidence / note |
|---|---|---|
| 3.1.1 No session tokens in URLs | ✅ | Cookie only. SignalR uses the same cookie (no access-token query string). |
| 3.2.1–3.2.3 New session on sign-in, ≥ 64-bit entropy, stored only in a cookie | ✅ | Identity issues a new protected ticket at every sign-in; data-protection keys in Blob Storage wrapped by a Key Vault key. |
| 3.3.1 Logout invalidates the session | ✅ | `POST /api/auth/logout` deletes the cookie; server-side invalidation via security stamp on password change/reset and deletion. |
| 3.3.2 Re-authentication / idle and absolute timeouts | ✅ | Sliding 14 days, absolute 30 days, security stamp checked every 30 min (AuthSetup.cs `ValidatePrincipalAsync`). |
| 3.3.3 Option to end other sessions after a password change | ✅ | Password change and reset update the security stamp; other sessions end within 30 min. |
| 3.3.4 Users can see and end active sessions | 🟡 | No session list; changing the password ends all other sessions. Accepted for this application. |
| 3.4.1–3.4.5 Cookie attributes: Secure, HttpOnly, SameSite, `__Host-` prefix, Path=/ | ✅ | `__Host-bc.session` (HttpOnly, Secure, Lax), `__Host-bc.2fa`, `__Host-bc.af` (Strict); the XSRF request token cookie is readable by JS by design. |
| 3.5.x Token-based sessions | N/A | No bearer tokens or JWTs are issued. |
| 3.7.1 Re-authentication before sensitive actions | ✅ | Password (or a fresh external re-authentication) needed to change the password and to delete the account (AccountController, ExternalAuthController re-auth mode). There is no email change. |

### V4 Access control
| Item | Status | Evidence / note |
|---|---|---|
| 4.1.1–4.1.5 Enforced on the server, deny by default, fail securely | ✅ | Global `RequireAuthorization()`; Admin policy = Admin role + `amr=mfa` claim (AuthSetup.cs). |
| 4.2.1 IDOR protection | ✅ | Customer queries always filter by the current user id; integration tests for other customers' orders, files and conversations return 404 (OrderApiTests, Chat tests). |
| 4.2.2 CSRF protection | ✅ | `AntiforgeryFilter` validates `X-XSRF-TOKEN` on every unsafe request; SameSite cookies. |
| 4.3.1 Admin interfaces use MFA | ✅ | Admin policy requires the second factor; `Features:RequireAdminMfa=false` throws outside Development. Test: `Admin_order_endpoints_refuse_customers_and_admins_without_2fa`. |
| 4.3.2 Directory browsing off, no metadata exposed | ✅ | Static files only from the Angular build output; no directory listing; no `.git`/source maps in the image. |
| 4.3.3 Step-up for sensitive admin operations | 🟡 | Admin sessions always carry 2FA; no extra step-up per operation. Accepted. |

### V5 Validation, sanitization and encoding
| Item | Status | Evidence / note |
|---|---|---|
| 5.1.1–5.1.5 Input validation, allow-lists, redirects | ✅ | Request DTO validation returns ProblemDetails; `returnUrl` for external sign-in accepts local paths only. |
| 5.2.x Sanitization, no HTML from users rendered as HTML | ✅ | Chat messages rendered as text by Angular (no `innerHTML`); emails use encoded templates. |
| 5.3.1–5.3.10 Output encoding, SQL injection, no eval | ✅ | EF Core parameterized queries only (no raw SQL with input); Angular template escaping; CSP blocks inline script. |
| 5.4.x Memory safety | ✅ | Managed code; image decoding through SkiaSharp with size and pixel limits before decode. |
| 5.5.x Deserialization | ✅ | System.Text.Json without polymorphic type handling. |

### V6 Stored cryptography
| Item | Status | Evidence / note |
|---|---|---|
| 6.2.x Approved algorithms, no custom crypto | ✅ | Only framework crypto (Data Protection AES-256-CBC + HMACSHA256, PBKDF2, CSPRNG). |
| 6.3.x Random values | ✅ | `RandomNumberGenerator` for CSP nonces; GUID v4 for blob names; Identity tokens. |
| 6.4.x Secret management | ✅ | Key Vault (RBAC mode, purge protection in production); data-protection key ring wrapped by a Key Vault key. |
| 6.1.x Regulated data encrypted at rest | ✅ | Azure SQL TDE and Storage encryption (platform default). No payment card data is stored. |

### V7 Error handling and logging
| Item | Status | Evidence / note |
|---|---|---|
| 7.1.1–7.1.4 No credentials, tokens, message bodies or full emails in logs | ✅ | Email addresses masked (`EmailAddresses.Mask`); Serilog request logging records method, path (no query string) and status, never bodies. |
| 7.2.1 / 7.2.2 Authentication and access-control events logged | ✅ | Audit log: sign-in success/failure, 2FA, password changes, re-authentication, external logins, export, deletion, admin role changes, every order action (Application/Audit/AuditActions.cs, OrderService). |
| 7.3.x Log protection, time sync | ✅ | Application Insights / Log Analytics (no write access from outside); UTC timestamps. |
| 7.4.1 Generic error messages | ✅ | ProblemDetails with an error code, no stack traces outside Development; generic messages for login, register and forgot-password (no user enumeration). |

### V8 Data protection
| Item | Status | Evidence / note |
|---|---|---|
| 8.1.1 Caching of sensitive data | ✅ | Every `/api` response is `no-store` unless the endpoint sets its own policy (Security/SecurityHeadersMiddleware.cs, test `Api_responses_are_not_stored_by_default`); images through `/api/files` are `private`; HTML `no-cache`. |
| 8.2.x Client-side data | ✅ | No tokens or personal data in `localStorage`; only UI preferences. |
| 8.3.1 Sensitive data not in URLs | ✅ | Data sent in bodies; email links carry only one-time tokens. |
| 8.3.2 Export and deletion of personal data | ✅ | `GET /api/account/export`; deletion erases name, email, logins, 2FA, chat and images, keeps orders anonymized (PLAN §8.6). Tests: `Deleting_erases_the_personal_data...`, `Deleting_the_customer_account_keeps_the_orders_anonymized`. |
| 8.3.4 Inventory of sensitive data | ✅ | Privacy policy pages (`/{lang}/privacy`) and PLAN §8.6. Order retention period still to be confirmed with a lawyer (PLAN §16). |

### V9 Communications
| Item | Status | Evidence / note |
|---|---|---|
| 9.1.1–9.1.3 TLS everywhere, TLS 1.2+ | ✅ | Ingress `allowInsecure: false`; HSTS outside Development; SQL and Storage minimum TLS 1.2. |
| 9.2.x Server-side connections over TLS, authenticated | ✅ | SQL, Blob, Key Vault, ACS over TLS with managed identity. |
| HSTS max-age | 🟡 | Framework default (30 days, no preload) until the custom domain exists (Phase 8). |

### V10 Malicious code
| Item | Status | Evidence / note |
|---|---|---|
| 10.3.x Application integrity, dependencies | ✅ | GitHub Actions pinned to commit SHAs; base images pinned; Dependabot for NuGet, npm, Actions, Docker; `npm audit` with a reviewed allow-list. |
| 10.2.x No back doors, time bombs, phone-home | ✅ | Code review via PRs, CodeQL (C#, JS/TS, Actions). |

### V11 Business logic
| Item | Status | Evidence / note |
|---|---|---|
| 11.1.1–11.1.5 Sequential steps, limits, anti-automation | ✅ | Order state machine in the domain (Domain/Orders/Order.cs) with optimistic concurrency (`RowVersion`); rate limits: chat 20/min, uploads 30/h per user, admin limits per user. |
| 11.1.6–11.1.8 Race conditions, alerts on abuse | 🟡 | Concurrency handled by row versions. Alerts (5xx rate, availability) come with Phase 7; no alert on abuse patterns. |

### V12 Files and resources
| Item | Status | Evidence / note |
|---|---|---|
| 12.1.1–12.1.3 Size and decompression limits | ✅ | 10 MB per image, 40 megapixels (checked from the header before decoding), 1 MB default body limit with 413 (Security/RequestSizeGuardMiddleware.cs). |
| 12.2.1 Type checked by content | ✅ | Magic bytes for JPEG, PNG, WebP; the decoder must agree (Infrastructure/Imaging/ImageProcessor.cs). |
| 12.3.x User file names never used in paths | ✅ | Blob names are random GUIDs; images re-encoded to WebP (EXIF/GPS removed). |
| 12.4.x Files stored outside the web root, not executable | ✅ | Private Blob containers; served only through `/api/files/{id}` with an authorization check, `Content-Type` fixed, `nosniff`. |
| 12.5.x, 12.6.1 File download, SSRF | ✅ | The server never fetches user-supplied URLs. |

### V13 API and web services
| Item | Status | Evidence / note |
|---|---|---|
| 13.1.x Same encoding, admin and user APIs separated | ✅ | JSON only; `/api/admin/*` behind the Admin policy. |
| 13.2.1–13.2.6 RESTful: methods, JSON schema validation, CSRF, content type | ✅ | Unsafe methods need the antiforgery header; `[ApiController]` model validation; JSON content type required. |
| 13.3.x SOAP / 13.4.x GraphQL | N/A | |

### V14 Configuration
| Item | Status | Evidence / note |
|---|---|---|
| 14.1.x Build and deploy pipeline, reproducible, no manual deploy | ✅ | GitHub Actions CI → staging → production with approval; Azure login via OIDC; Bicep (`infra/`). |
| 14.2.x Dependencies up to date, unused removed | ✅ | Dependabot, Trivy (fails on fixed HIGH/CRITICAL), `npm audit`, `dotnet list package --vulnerable`. |
| 14.3.x No debug, no version disclosure | ✅ | No `Server` header; no stack traces outside Development; Development-only switches throw elsewhere; `/api/version` shows only the git SHA and the environment name. |
| 14.4.1–14.4.7 Security headers | ✅ | CSP with nonce (`default-src 'self'`, no inline script, `frame-ancestors 'none'`), `nosniff`, `Referrer-Policy`, `Permissions-Policy`, COOP/COEP/CORP (Security/SecurityHeadersMiddleware.cs). ZAP baseline scan after every staging deploy. |
| 14.5.x Request validation: methods, Origin, CORS | ✅ | No CORS policy, so browsers allow only same-origin API calls; unsafe methods need the antiforgery header. |
| Container hardening | ✅ | Chiseled non-root runtime image; no shell. |
| Network exposure of data services | 🟡 | Azure SQL, Storage and Key Vault accept public network traffic (SQL: "Allow Azure services"), protected by Entra ID / RBAC with no keys or passwords. Private endpoints need a VNet-integrated environment and cost more than the whole budget; accepted risk (docs/DECISIONS.md, 2026-11-25). |

## 4. Scanning in CI/CD
| Scanner | When | Gate |
|---|---|---|
| CodeQL (C#, JavaScript/TypeScript, Actions) | every PR and push to `main`, weekly | alerts reviewed in GitHub Security |
| Trivy (image) | every PR and build | fails on HIGH/CRITICAL with a fix available |
| `npm audit` | every PR | fails on high, except reviewed entries in `src/frontend/audit-allowlist.json` |
| `dotnet list package --vulnerable` | every PR | fails on any vulnerable package |
| OWASP ZAP baseline | after every staging deploy | fails on high-risk alerts; ignored rules with reasons in `.zap/rules.tsv` |
| Lighthouse (accessibility, best practices) | every PR | thresholds in `.lighthouserc.json` |
| GitHub secret scanning + push protection | always | blocks pushes with secrets |

## 5. Known gaps and planned work
- Breached-password check (ASVS 2.1.7): Have I Been Pwned range API on register, reset and change.
- HSTS: raise `max-age` to one year and consider preload once the custom domain is live (Phase 8).
- Password reset token lifetime: consider shortening from the default 1 day to 1–2 hours.
- Order retention period after account deletion: confirm with a lawyer and make it configurable (PLAN §8.6).
- Recovery codes stored unhashed and TOTP codes reusable within their window (Identity defaults): a custom user store
  could hash the codes and remember the last used time step. Low risk with Entra-only SQL; revisit after the pentest.
- No notification email when 2FA or an external login changes.
