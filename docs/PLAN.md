# Pracownia Bajkowe Cuda — Project Plan

> Implementation specification for Claude Code. Read `CLAUDE.md` first for the working rules.
> Status: agreed with the owner on 2026-11-12. No domain yet: use `example.com` placeholders driven by configuration.

---

## 1. Goals

1. **Product:** a mobile-first one-page site for a custom toy-sewing service. Visitors see the About, Gallery, Pricing
   and Contact sections. Registered customers chat with the owner, and the owner turns conversations into orders.
   Customers follow their orders (reference picture, status, price, photos of the finished toy).
2. **Portfolio:** show production-grade **Docker**, **Azure** and **CI/CD** work, plus Clean Architecture, security
   and observability. The repository is public, so code, README and commit history are part of the deliverable.

### Out of scope (for now)
- Admin CMS for content: prices, gallery and texts are edited in code.
- Online payments: payment details are sent in the chat and the owner marks orders as paid manually.
- A settings page: the only user preference (email notifications) is a checkbox in the chat.
- Native mobile apps.

---

## 2. Key decisions

| Topic | Decision | Reason |
|---|---|---|
| Frontend | Angular (latest stable), standalone, signals, zoneless if stable | Owner's choice. Angular + .NET is a common job-market combination |
| Rendering | **Static prerendering (SSG)** of public routes per locale. Everything else is client-rendered | SEO without running a Node server in production. Content only changes in code anyway |
| Hosting | **One container**: ASP.NET Core serves API + SignalR + Angular static files | Same origin: no CORS, same-site cookies, one deployable |
| Auth model | **BFF with cookies.** ASP.NET Core Identity performs all logins (password + OAuth). Angular never sees tokens | Current recommendation for browser apps. Tokens are not exposed to XSS |
| Database | SQL Server (local Docker) / **Azure SQL** | Identity and the relational data (users → conversation → messages, orders) fit naturally. Familiar stack |
| Files | **Blob Storage** (Azurite locally), private containers, served through an authorized API endpoint | No binaries in the DB. Access is checked on every request |
| Gallery images | Static assets in the Angular app (optimized at build) | No CMS, so they live in code. Fastest possible delivery |
| Languages | en (source), de, fr, es, pl. Locale in the URL (`/de/...`) | SEO (`hreflang`, separate indexable URLs) |
| Currency | EUR everywhere | Owner's decision |
| Architecture | Clean Architecture: Domain / Application / Infrastructure / Api | Owner's choice. Good for portfolio |
| Email | Azure Communication Services Email (Azure-managed domain until a custom domain exists). Mailpit locally | Azure service on the CV. A free managed domain works without owning a domain |
| Background jobs | Transactional **outbox** table + `BackgroundService` dispatcher | Reliable email without a message broker |
| IaC | **Bicep** | Native Azure. Simple for one subscription |
| CI/CD | GitHub Actions, OIDC federated credentials to Azure (no stored cloud secrets) | Modern, secure |
| Environments | local (docker-compose), **staging**, **production** in Azure | Realistic promotion flow |

---

## 3. Functional specification

### 3.1 Public one-page site (mobile-first)
- **Sticky top bar** that always stays visible: logo/name, menu items *About · Gallery · Pricing · Contact*,
  language switcher, and a *Sign in* button (or *Chat* / *My orders* / account menu when signed in).
  On narrow screens the menu collapses into a hamburger panel.
- Tapping a menu item smooth-scrolls to its section (`scroll-margin-top` = bar height). The active item is highlighted
  with an `IntersectionObserver`. Section anchors have stable ids (`#about`, `#gallery`, `#pricing`, `#contact`) and are deep-linkable.
- **About:** text + photo of the maker.
- **Gallery:** swipeable carousel (CSS `scroll-snap` + small custom component, no heavy library). Prev/next buttons and dots,
  keyboard accessible, lazy-loaded responsive images (`NgOptimizedImage`, AVIF/WebP), tap to open a fullscreen viewer.
- **Pricing:** list of service packages/price ranges in EUR from a typed content file, plus a note that the final price is agreed in the chat.
- **Contact:** email, phone, social links, and a primary CTA *"Order a toy — write to us"*. It opens the chat, going through sign-in first if needed.
- **Footer:** links to Privacy policy, Terms, Cookie information, and the copyright line.
- Content lives in `src/frontend/src/app/content/` (typed TS objects with `$localize` strings, image lists, prices).
  Changing content = editing these files + redeploying.

### 3.2 Accounts
- Registration with email + password. Email confirmation is required before chatting.
- Sign in with **Google** and **Facebook** (phase 3). **Apple** comes later (needs a paid Apple Developer account and a custom domain).
  **No Instagram:** Meta no longer offers Instagram login for personal accounts.
- Each external provider is **enabled only if its configuration is present** (feature flag by config), so missing credentials never break the app.
- If an external provider returns no email (Facebook can do this) or an Apple private-relay email, show a *Complete profile* step that asks for an email and sends a confirmation.
- **Account linking:** link an external login to an existing local account automatically only when the provider asserts
  a verified email that equals an already *confirmed* local email. Otherwise the user must sign in to the existing account first and link from there (prevents account takeover).
- Forgot / reset password, resend confirmation email.
- Consent checkbox at registration (Terms + Privacy policy), stored with a timestamp and the policy version.
- **Account page** (`/account`, minimal): display name, preferred language, linked logins, **Export my data** (JSON download),
  **Delete account** (re-authentication required, see §8.6).
- Preferred language is stored on the user and used for emails.

### 3.3 Roles
- **Customer:** chat with the owner, email-notification checkbox, *My orders* (read-only), account page.
- **Admin** (owner, possibly an assistant): all conversations, creating and managing orders. **2FA (TOTP) is mandatory.**
  Admins are never created through public registration. The first admin is seeded from configuration (`Admin:Emails`) at startup:
  the role is assigned when an account with that email exists and is confirmed.

### 3.4 Chat
- Exactly **one conversation per customer**, between the customer and "the studio" (any admin can answer).
  Created lazily on the customer's first message.
- Text messages (max 2,000 chars) with optional image attachments (max 5 per message, JPEG/PNG/WebP, 10 MB each).
- **System messages** (non-editable, styled differently) are posted automatically for order events (§3.5).
- Real time through **SignalR**. History is loaded with cursor pagination (newest first, infinite scroll upward).
- Read state: per message `ReadByCustomerAt` / `ReadByAdminAt`. Unread badges in the top bar (customer) and in the conversation list (admin).
- **Email notifications** (§3.6): on the customer's *first* message a dialog asks *"Send replies to your email?"* (Yes / No).
  After that the chat composer always shows a checkbox **"Send replies to my email"** that can be toggled at any time.
- Admin chat UI: conversation list (search by name/email, unread first, last message preview) → conversation view with a side panel:
  customer info, the customer's orders, and **Create order** button.

### 3.5 Orders
Orders are created **only by an admin, from a conversation**. The customer can only view them.

**Lifecycle (domain state machine):**
```
Draft ──confirm──▶ AwaitingPayment ──markPaid──▶ InProgress ──markReady──▶ Ready ──markShipped──▶ Shipped ──complete──▶ Completed
  │                     │                            │                       │                       │
  └──────────────── cancel (with reason) from any non-final state ───────────┴───────────────────────┘──▶ Cancelled
```
- **Draft:** admin fills in title, description, **reference image** (chosen from the conversation's attachments or uploaded),
  price (EUR, > 0) and an internal note. **The customer cannot see drafts.**
- **confirm → AwaitingPayment:** the order becomes visible to the customer. A system message is posted in the chat with the
  order number, price and **payment instructions** generated from a per-language template in configuration
  (`PaymentInstructions:Templates:{lang}`, placeholders `{OrderNumber}`, `{Amount}`, `{Currency}`). No settings UI: the template lives in config / Key Vault.
- **markPaid → InProgress**, **markReady → Ready** (requires ≥ 1 photo of the finished toy), **markShipped → Shipped**, **complete → Completed**, **cancel → Cancelled** (reason required, shown to the customer).
- Every transition: domain validates it, writes an `OrderStatusChange` row, posts a localized system message in the chat,
  and pushes `OrderUpdated` through SignalR. Price and reference image can be edited only in `Draft` and `AwaitingPayment`.
- Optimistic concurrency (`RowVersion`) on orders.
- **My orders** tab (visible once the customer has ≥ 1 non-draft order): list with thumbnail, number, status badge, price → detail page with
  reference image, description, price, status timeline, finished-toy photos (gallery/fullscreen), cancellation reason if any.
- Human-readable order number: `BC-` + zero-padded sequence (`BC-0012`), from a SQL sequence. Public ids are GUID v7.

### 3.6 Email notifications
| Recipient | When | Condition |
|---|---|---|
| Admin(s) | new customer message(s) | always |
| Customer | new admin/system message(s) | only if `EmailNotificationsEnabled` |
| User | confirm email, reset password, email changed, account deleted | always (transactional) |

- **Debounce/batching:** when a message is posted and the recipient has no pending notification for that conversation,
  enqueue one with `SendAfter = now + 5 min` (configurable). At send time: if the recipient has already read everything, skip it.
  Otherwise send **one** email listing all unread messages (text excerpts, "N new photos", link to the chat). The same pending row collects further messages.
- Templates: localized (`.resx` + simple HTML layout, plain-text alternative), in the recipient's preferred language.
- Delivery: outbox table → `EmailDispatcher` `BackgroundService` (polling every 30 s, exponential retry, max 8 attempts, dead-letter state).
- Providers behind `IEmailSender`: `SmtpEmailSender` (Mailpit locally), `AcsEmailSender` (Azure Communication Services).

### 3.7 Legal / GDPR
- Pages: `/{lang}/privacy`, `/{lang}/terms`, `/{lang}/cookies`. Prerendered. Content is a **placeholder** with a checklist of what must be covered
  (data controller, purposes, legal bases, retention, processors: Microsoft Azure, Google, Meta; user rights; contact; withdrawal rules for made-to-order items).
  **Final texts must be reviewed by a lawyer before go-live.**
- **Only strictly necessary cookies** (auth session, antiforgery, language preference). No analytics or marketing cookies,
  no browser telemetry SDK. Therefore a consent banner is not needed, only a dismissible information notice linking to `/cookies`.
- Data export (JSON) and account deletion (§8.6). Facebook's *data deletion instructions URL* points to a page explaining how to delete the account.

---

## 4. Architecture

```
                      ┌──────────────────────── Docker image (one container) ────────────────────────┐
 Browser ── HTTPS ──▶ │ ASP.NET Core (Kestrel :8080, non-root, chiseled image)                        │
 (Angular SPA,        │  ├─ Static files: prerendered Angular per locale (/en, /de, /fr, /es, /pl)   │
  cookies only)       │  ├─ /api/*   REST (controllers), antiforgery, rate limiting                   │
                      │  ├─ /hubs/chat  SignalR                                                       │
                      │  └─ BackgroundService: EmailDispatcher (outbox)                                │
                      └───────┬──────────────────┬───────────────────┬──────────────────┬─────────────┘
                              │ EF Core          │ Blob SDK          │ ACS Email         │ OpenTelemetry
                        Azure SQL           Blob Storage      Communication Svc    Application Insights
                              (secrets from Key Vault via Managed Identity)
```

### 4.1 Repository layout
```
/
├─ CLAUDE.md, README.md, LICENSE, .editorconfig, .gitignore, .gitattributes, .env.example
├─ BajkoweCuda.sln
├─ Directory.Build.props, Directory.Packages.props, global.json
├─ Dockerfile, .dockerignore, docker-compose.yml, docker-compose.override.yml (dev)
├─ docs/  PLAN.md, DECISIONS.md, architecture diagrams (Mermaid)
├─ infra/ main.bicep, main.staging.bicepparam, main.prod.bicepparam, modules/*.bicep
├─ .github/ workflows/*.yml, dependabot.yml, CODEOWNERS, pull_request_template.md
├─ src/
│  ├─ backend/
│  │  ├─ BajkoweCuda.Domain/          entities, value objects, enums, order state machine, domain errors
│  │  ├─ BajkoweCuda.Application/     use-case handlers, DTOs, validators (FluentValidation), interfaces (ports)
│  │  ├─ BajkoweCuda.Infrastructure/  EF Core DbContext + configurations + migrations, Identity, Blob, Email, Outbox, image processing
│  │  └─ BajkoweCuda.Api/             Program.cs, controllers, SignalR hub, auth setup, security headers, static hosting, ProblemDetails
│  └─ frontend/                       Angular workspace
└─ tests/
   ├─ BajkoweCuda.Domain.UnitTests/
   ├─ BajkoweCuda.Application.UnitTests/
   ├─ BajkoweCuda.Api.IntegrationTests/   WebApplicationFactory + Testcontainers (SQL Server, Azurite)
   └─ e2e/                                Playwright (later phase)
```

### 4.2 Backend conventions
- Application layer: one handler class per use case (`CreateOrderHandler`, ...), returning `Result<T>` with typed errors.
  The Api layer maps them to RFC 7807 ProblemDetails with a stable `code` (e.g. `order.invalid_transition`). The frontend translates codes into messages.
- Validation with FluentValidation in Application. Domain invariants enforced in entities (private setters, factory methods).
- `ICurrentUser` abstraction (user id, roles, language) implemented in Api.
- Time through `TimeProvider` (testable).
- EF Core: Fluent configurations per entity, `decimal(10,2)` for money, SQL sequence for order numbers, `rowversion` on Order,
  connection resiliency (`EnableRetryOnFailure`), migrations in Infrastructure.
- Package suggestions: FluentValidation, Serilog (console sink, JSON), `Azure.Monitor.OpenTelemetry.AspNetCore`, `Azure.Storage.Blobs`,
  `Azure.Communication.Email`, `Azure.Identity`, `Azure.Extensions.AspNetCore.Configuration.Secrets`,
  `Microsoft.AspNetCore.Authentication.Google`, `Microsoft.AspNetCore.Authentication.Facebook`, `AspNet.Security.OAuth.Apple`,
  SixLabors.ImageSharp (check its Split License fits; Magick.NET or SkiaSharp are alternatives), Testcontainers, xUnit, FluentAssertions alternative
  (e.g. Shouldly, because FluentAssertions v8 is commercial), NSubstitute, Respawn.

### 4.3 Frontend conventions
- Standalone components, signals for state, `inject()`, typed reactive forms, OnPush / zoneless.
- Feature folders: `core/` (auth, http interceptors, guards, i18n helpers), `public/` (landing sections, legal pages),
  `auth/`, `chat/`, `orders/`, `account/`, `admin/` (lazy-loaded, `adminGuard`), `shared/` (UI components: carousel, image viewer, status badge, dialog).
- Styling: SCSS with design tokens (CSS variables), mobile-first breakpoints, no large UI framework required. Angular CDK (dialog, overlay, a11y) is fine.
- HTTP: relative URLs only (`/api/...`), `withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' })`, an error interceptor mapping ProblemDetails codes to localized messages, and 401 → redirect to sign in.
- SignalR client `@microsoft/signalr`, wrapped in a `ChatRealtimeService` exposing signals; auto-reconnect.
- Accessibility: WCAG 2.1 AA (contrast, focus states, alt texts, keyboard navigation of the carousel and dialogs).
- Tests: the default Angular unit test runner of the installed version, plus Playwright E2E later.

### 4.4 Routes
| Route | Render | Access |
|---|---|---|
| `/{lang}/` (landing with sections) | Prerender | public |
| `/{lang}/privacy`, `/terms`, `/cookies` | Prerender | public |
| `/{lang}/login`, `/register`, `/forgot-password`, `/reset-password`, `/confirm-email`, `/complete-profile`, `/login/2fa` | Client | anonymous |
| `/{lang}/chat` | Client | customer (confirmed email) |
| `/{lang}/orders`, `/{lang}/orders/:id` | Client | customer |
| `/{lang}/account` | Client | signed in |
| `/{lang}/admin/conversations`, `/admin/conversations/:id`, `/admin/orders`, `/admin/orders/:id`, `/admin/2fa-setup` | Client | admin (+2FA) |

ASP.NET Core hosting rules:
- `/` → 302 to `/{lang}/`, chosen from the `lang` cookie, then `Accept-Language`, then `en`.
- Static files for each locale folder. Unknown non-API paths under `/{lang}/` fall back to that locale's client-side shell
  (with `outputMode: 'static'` this is the generated CSR index file; verify its exact name, e.g. `index.csr.html`, in the build output).
- `/api/*` and `/hubs/*` never fall back to HTML (return 404 ProblemDetails).
- Cache headers: hashed assets `public, max-age=31536000, immutable`; HTML `no-cache`.

---

## 5. Domain model

```
ApplicationUser (IdentityUser<Guid>)
  DisplayName, PreferredLanguage (en|de|fr|es|pl), CreatedAt, TermsAcceptedAt, TermsVersion, IsDeleted, DeletedAt

Conversation
  Id (Guid v7), CustomerId (unique FK), CreatedAt, LastMessageAt,
  CustomerEmailNotificationsEnabled (bool), EmailPromptAnswered (bool)

Message
  Id, ConversationId, SenderId (null for system), Kind (Text | System), Body,
  SystemEventType? + SystemEventPayload (json, rendered localized on the client), OrderId?,
  CreatedAt, ReadByCustomerAt?, ReadByAdminAt?

Attachment
  Id, OwnerUserId, MessageId?, Container, BlobName, ThumbnailBlobName, ContentType, SizeBytes, Width, Height, CreatedAt
  (uploaded first, then bound to a message on send. Unbound attachments older than 24 h are cleaned up.)

Order
  Id, Number (int, sequence), CustomerId, ConversationId, Title, Description,
  ReferenceImage (Attachment ref), Price (Money: Amount decimal(10,2), Currency = EUR),
  Status, AdminNote, CancellationReason?, CreatedAt, ConfirmedAt?, PaidAt?, ReadyAt?, ShippedAt?, CompletedAt?, CancelledAt?, RowVersion

OrderPhoto            Id, OrderId, Attachment ref, SortOrder, CreatedAt
OrderStatusChange     Id, OrderId, FromStatus, ToStatus, ChangedById, ChangedAt, Comment?
EmailOutbox           Id, Kind (Transactional | ChatDigest), ToUserId?, ToEmail, Language, Template, PayloadJson,
                      ConversationId?, CreatedAt, SendAfter, Status (Pending|Sent|Skipped|Failed|Dead), Attempts, LastError?, SentAt?
AuditLog              Id, ActorId, Action, EntityType, EntityId, CreatedAt, DetailsJson   (admin actions, auth events)
```
Domain unit tests must cover every allowed and forbidden order transition and the edit rules per status.

---

## 6. API (REST, JSON, ProblemDetails errors)

All state-changing requests require the antiforgery header. All endpoints are authenticated unless marked *(anon)*.

**Auth** `/api/auth`
- `POST register` *(anon)* · `POST login` *(anon)* · `POST logout`
- `POST confirm-email` *(anon)* · `POST resend-confirmation` *(anon)* · `POST forgot-password` *(anon)* · `POST reset-password` *(anon)*
- `GET me` *(anon: returns `{ authenticated: false }` or the user summary with roles, language, emailConfirmed, hasOrders, unreadCount). Also issues the XSRF cookie. Must not hit the DB when anonymous.*
- `GET providers` *(anon: list of enabled external providers)*
- `GET external/{provider}?returnUrl=` *(anon: challenge, full-page navigation)* · `GET external/callback` *(anon)* · `POST complete-profile`
- `POST 2fa/login` · `GET 2fa/setup` (shared key + otpauth URI) · `POST 2fa/enable` · `POST 2fa/recovery-codes`
- Return URLs: only local relative paths are accepted (open-redirect protection).

**Account** `/api/account`
- `GET` · `PATCH` (displayName, preferredLanguage) · `GET export` · `POST delete` (password or fresh external re-auth) · `GET logins` · `DELETE logins/{provider}`

**Chat — customer** `/api/chat`
- `GET` (conversation summary + notification setting + emailPromptAnswered)
- `GET messages?before={cursor}&take=30`
- `POST attachments` (multipart, one file) → attachment id + thumbnail URL
- `POST messages` `{ body, attachmentIds[] }`
- `POST read` `{ upToMessageId }`
- `PUT email-notifications` `{ enabled }` (also sets `EmailPromptAnswered = true`)

**Files** `/api/files`
- `GET {attachmentId}` · `GET {attachmentId}/thumbnail`: streams the blob after checking access
  (owner of the conversation/order, or admin). `Cache-Control: private, max-age=3600`, `X-Content-Type-Options: nosniff`, correct `Content-Type`.

**Orders — customer** `/api/orders`
- `GET` (non-draft orders of the current user) · `GET {id}` (404 if not owned or draft)

**Admin** `/api/admin` (policy `Admin`, requires the 2FA `amr` claim)
- `GET conversations?search=&cursor=` · `GET conversations/{id}` · `GET conversations/{id}/messages?before=` · `POST conversations/{id}/messages` · `POST conversations/{id}/read`
- `POST orders` `{ conversationId, title, description, referenceAttachmentId, price }` → Draft
- `GET orders?status=&cursor=` · `GET orders/{id}` · `PUT orders/{id}` (editable fields per status)
- `POST orders/{id}/confirm` · `/mark-paid` · `/mark-ready` · `/mark-shipped` · `/complete` · `/cancel` `{ reason }`
- `POST orders/{id}/photos` (multipart) · `DELETE orders/{id}/photos/{photoId}` · `PUT orders/{id}/photos/order`
- `GET audit?cursor=`

**SignalR** `/hubs/chat` (cookie auth, same origin)
- Groups: `conversation:{id}` (the customer's connections), `admins`.
- Server → client: `MessageReceived(message)`, `MessagesRead(conversationId, upToMessageId, by)`, `OrderUpdated(orderSummary)`, `UnreadCountChanged(count)`.
- Client → server: none required (sending goes through REST so validation, rate limiting and antiforgery apply). Optional `Typing(conversationId)`.

**Health**: `GET /health/live` (no dependencies, used by Container Apps probes) and `GET /health/ready` (DB + Blob, used by the smoke test only, **not** by probes, so the serverless DB is not kept awake).

---

## 7. i18n & SEO

- `@angular/localize`, source locale `en`, translations `src/locale/messages.{de,fr,es,pl}.xlf`.
  Claude Code creates initial translations and marks them `state="needs-review"` (the owner will have them checked).
- Build with `localize: true`. Each locale is output to its own folder and served under `/{lang}/`.
- Language switcher → navigates to the same path in the other locale (full page load) and sets the `lang` cookie (1 year, strictly necessary).
- Prerendered per locale: landing + legal pages, each with:
  - `<html lang>`, localized `<title>` and meta description, canonical URL,
  - `<link rel="alternate" hreflang="xx">` for every locale + `x-default` → `/en/`,
  - Open Graph / Twitter card tags with a share image,
  - JSON-LD `LocalBusiness` + `Service` / `Offer` (prices in EUR) on the landing page.
- `sitemap.xml` (all locales × public pages, with hreflang alternates) and `robots.txt` generated at build time from `PublicBaseUrl`.
  `robots.txt` disallows `/api/` and private routes.
- Staging: `X-Robots-Tag: noindex, nofollow` header on every response + robots.txt `Disallow: /`.
- Performance budget: Lighthouse mobile ≥ 90 for Performance, Accessibility, Best Practices and SEO. Enforced in CI with Lighthouse CI against the container.
- Backend localization: `.resx` for email templates and system-message fallbacks. Supported cultures en, de, fr, es, pl.

---

## 8. Security (target: OWASP ASVS Level 2; an external pentest is planned after go-live)

### 8.1 Session & auth
- Identity application cookie: `__Host-` prefix, `HttpOnly`, `Secure`, `SameSite=Lax`, sliding 14 days, absolute 30 days, security-stamp validation every 30 min.
  (`Lax` rather than `Strict`, because after an OAuth redirect chain a `Strict` cookie is not sent on the first navigation. CSRF is covered by antiforgery.)
- External login correlation/nonce cookies: framework defaults (Apple uses `form_post`, which needs `SameSite=None` on those temporary cookies).
- Antiforgery: `XSRF-TOKEN` cookie (readable by JS) + `X-XSRF-TOKEN` header validated on all unsafe methods.
- Password policy: min 12 chars, no forced composition rules. Optional check against Have I Been Pwned (k-anonymity range API).
- Lockout: 5 failed attempts → 15 min. Generic error messages (no user enumeration on login, register, forgot-password).
- Admin: TOTP 2FA mandatory. Admin endpoints require the `amr=mfa` claim. Recovery codes.
- Re-authentication for account deletion and for changing email/password.
- Audit log for logins, failed logins, 2FA changes, admin actions.

### 8.2 HTTP hardening
- HSTS (prod), HTTPS only (TLS ends at Container Apps ingress, `ForwardedHeaders` configured for the proxy).
- Headers: strict `Content-Security-Policy` (`default-src 'self'`, no inline scripts: enable Angular `autoCsp` or nonce-based CSP; `connect-src 'self' wss:`; `img-src 'self' data: blob:`; `frame-ancestors 'none'`; `base-uri 'self'`; `form-action 'self'` + OAuth providers),
  `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` (camera, geolocation etc. disabled), `Cross-Origin-Opener-Policy: same-origin`.
  Verify zero CSP violations in the browser console in E2E tests.
- Remove the `Server` header. Request size limits. ProblemDetails without stack traces outside Development.

### 8.3 Rate limiting (ASP.NET Core `RateLimiter`)
- Auth endpoints: per IP, e.g. 10/min sliding window. Forgot-password/resend: 3 per hour per email + per IP.
- Chat messages: per user 20/min. Uploads: per user 30/hour. Admin endpoints: generous limits.
- 429 responses with `Retry-After`.
- Optional (phase 9): Cloudflare Turnstile on register / forgot-password.

### 8.4 Uploads
- Allow-list by **magic bytes** (JPEG, PNG, WebP), not by extension or the client's content type. Max 10 MB, max 40 megapixels.
- **Re-encode** every image server-side (strips EXIF/GPS and any embedded payload), generate a thumbnail (e.g. 480 px) and a display size (e.g. 1600 px).
- Random blob names (GUID). Never use the user's file name in paths. Store the original name only as metadata, sanitized.
- Private containers. Access only through `/api/files/{id}` with an authorization check (IDOR tests required).

### 8.5 Data & secrets
- Secrets: Key Vault, read through Managed Identity. App configuration via environment variables / Key Vault references. Nothing secret in Bicep parameters or the repo.
- Azure SQL: Entra ID authentication with Managed Identity (no SQL passwords in prod), TLS enforced, public network access restricted.
- Storage: shared-key access disabled in Azure, RBAC through Managed Identity.
- Logging: no PII (mask emails), no message bodies, no tokens.

### 8.6 Account deletion
- Deletes the Identity user's personal data: name, email, logins, 2FA, conversation messages and attachments (blobs included).
- Orders are **anonymized but kept** (customer link removed, a "deleted user" marker stays), because the business may have to keep transaction records. Confirm the retention period with the lawyer and make it configurable.
- Sends a confirmation email, writes an audit entry.

### 8.7 Supply chain & scanning (CI)
- GitHub CodeQL (C# + JS/TS), Dependabot (NuGet, npm, GitHub Actions, Docker), secret scanning + push protection,
  Trivy scan of the image (fail on HIGH/CRITICAL with a fix available), `npm audit --audit-level=high`, `dotnet list package --vulnerable`.
- OWASP ZAP baseline scan against staging after each deploy (report as an artifact, fail on high-risk alerts).
- Pin GitHub Actions to commit SHAs. Minimal `permissions:` per workflow.

---

## 9. Docker

- **Multi-stage `Dockerfile`:**
  1. `node:<lts>-alpine` → `npm ci` → `ng build` (localized, prerendered).
  2. `mcr.microsoft.com/dotnet/sdk:10.0` → restore (layer-cached by copying `*.csproj` + `Directory.*.props` first) → test (optional build arg) → `dotnet publish -c Release`.
  3. `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (or `-chiseled-extra` if globalization/ICU is needed for the cultures) → copy the API publish output + Angular `browser/` output into `wwwroot/` → run as non-root, port 8080, read-only root FS compatible.
- Build args: `APP_VERSION` (git SHA) shown in `/api/version` and logs. OCI labels.
- **`docker-compose.yml`** for local development:
  - `sqlserver` (`mcr.microsoft.com/mssql/server`, latest supported version) with a volume and a healthcheck,
  - `azurite` (blob + queue),
  - `mailpit` (SMTP :1025, UI :8025),
  - `app` (profile `app`): the built image, wired to the services above, `depends_on` with health conditions.
- Dev workflow: `docker compose up -d` (infra) + `dotnet watch` (API) + `ng serve` with `proxy.conf.json` (`/api`, `/hubs` with `ws: true`) → same-origin in dev as well.
- Seed data in Development: an admin user (2FA can be bypassed in Development only, behind an explicit flag), a test customer, a sample conversation and orders.

---

## 10. Azure infrastructure (Bicep, `infra/`)

One resource group per environment (`rg-bajkowecuda-staging`, `rg-bajkowecuda-prod`), region `westeurope` or `polandcentral`
(check that every service, including the free SQL offer, is available in the chosen region).

| Resource | Staging | Production |
|---|---|---|
| Log Analytics workspace + Application Insights | ✓ | ✓ |
| Container Registry | shared, **Basic** (one registry for both, e.g. in a `rg-bajkowecuda-shared` group) | |
| Container Apps environment (Consumption) + Container App | min replicas **0** | min replicas **1** (the chat worker and SignalR need a running instance, no cold start) |
| User-assigned Managed Identity | ✓ (AcrPull, Key Vault Secrets User, Storage Blob Data Contributor, SQL access) | ✓ |
| Azure SQL logical server (Entra admin) + database | **Free offer** serverless DB, auto-pause when the free limit is reached | **Basic (DTU)** tier: small fixed monthly price, always on, no auto-pause cold starts. Alternative: free offer with *continue billing* + budget alert. Owner decides (see §16) |
| Storage account (blob containers `attachments`, `order-photos`; shared key disabled) | ✓ | ✓ + soft delete |
| Key Vault (RBAC mode) | ✓ | ✓ + purge protection |
| Communication Services + Email Communication Service + **Azure-managed domain** (custom domain later) | ✓ | ✓ |
| Container Apps **Job** (manual trigger) for EF Core migrations | ✓ | ✓ |
| Budget with alerts (50 % / 80 % / 100 %) + Action Group (email) | ✓ | ✓ |
| Alerts: 5xx rate, availability test on `/health/live`, failed email dispatch | | ✓ |

Notes:
- Why no DB polling on staging when idle: the app scales to zero, so the outbox poller only runs while the app is awake.
  The serverless DB can pause. Probes use `/health/live`, which never touches the DB.
- SignalR runs in-process (one replica). If scaling beyond one replica is needed later, add **Azure SignalR Service** (Free tier) as the backplane (optional phase).
- Custom domain + free managed certificate on the Container App once the domain exists. Until then use the `*.azurecontainerapps.io` URL.
- Approximate monthly cost of production: Container Registry Basic + SQL Basic, each a few EUR, plus near-zero Container Apps/storage/email usage at this scale.
  Verify in the Azure Pricing Calculator. The budget alert guards against surprises.
- Bicep: modules per resource type, `@secure()` params, outputs (app URL, identity ids). `az deployment group what-if` runs in the PR pipeline for `infra/` changes.

---

## 11. CI/CD (GitHub Actions)

Branching: trunk-based. `main` is protected (PR + green checks + 1 review if a collaborator exists). Feature branches → PR.

| Workflow | Trigger | Steps |
|---|---|---|
| `ci.yml` | PR, push to `main` | backend: restore, build (warnings as errors), unit + integration tests (Testcontainers), coverage report · frontend: `npm ci`, lint, unit tests, `ng build` · Docker build (no push) + Trivy · upload test/coverage results |
| `codeql.yml` | PR, push, weekly | CodeQL C# + JS/TS |
| `infra-ci.yml` | PR touching `infra/` | `az bicep build`, lint, `what-if` against staging (OIDC) |
| `cd.yml` | push to `main` (after CI) | **build & push** image to ACR (tags: git SHA, plus `latest` for staging) → **deploy staging**: Bicep deploy (idempotent) → run migration Job (EF Core migration bundle in a small image or the app image with a `migrate` command) → update Container App revision → smoke tests (`/health/ready`, landing page per locale returns 200 + contains expected text) → ZAP baseline + Lighthouse CI → **production** (GitHub Environment `production` with **required reviewer**) → same deploy steps with the *same image digest* (build once, promote) |
| `cleanup.yml` | weekly | delete old ACR images (keep the last N), purge old Container App revisions |

- Azure login with **OIDC** (`azure/login` + federated credential on an Entra app / user-assigned identity). No client secrets in GitHub.
- GitHub Environments `staging` and `production` with environment-scoped variables. Concurrency groups prevent parallel deploys.
- Rollback: re-deploy a previous image digest (documented in README) or activate the previous Container Apps revision.
- Status badges in the README.

---

## 12. Observability & operations

- OpenTelemetry via `Azure.Monitor.OpenTelemetry.AspNetCore`: traces, metrics and logs to Application Insights (server-side only, no browser SDK).
- Serilog JSON console logs (Container Apps → Log Analytics), enriched with trace id, app version and environment. PII masking.
- Custom metrics/events: messages sent, orders by status transition, emails sent/failed, upload rejections.
- Alerts (prod): HTTP 5xx > threshold, availability test failure, outbox items in `Dead` state, budget thresholds.
- Backups: Azure SQL automated backups (verify point-in-time restore in a documented drill), Blob soft delete (7 days).
- Runbook in `docs/RUNBOOK.md`: deploy, rollback, rotate secrets, restore DB, add an admin, change payment-instructions text, update content/prices.

---

## 13. Testing strategy

- **Domain unit tests:** order state machine, money, edit rules, message rules.
- **Application unit tests:** handlers with fakes (notification batching logic, order confirmation posts a system message and enqueues email, etc.).
- **API integration tests** (`WebApplicationFactory` + Testcontainers SQL Server + Azurite, Respawn between tests):
  - auth flows (register → confirm → login → logout, lockout, reset password),
  - **authorization matrix**: anonymous / customer A / customer B / admin without 2FA / admin with 2FA × every endpoint,
  - IDOR: customer B cannot read A's messages, files, orders, drafts,
  - antiforgery enforced on unsafe methods, rate limits return 429,
  - upload validation (wrong magic bytes, oversized, EXIF stripped).
- **Frontend unit tests:** services, guards, interceptors, key components (carousel, chat composer, order timeline).
- **E2E (Playwright, phase 9):** landing in 2 locales, register + confirm via the Mailpit API, chat round-trip customer ↔ admin, admin creates → confirms order → customer sees it. Runs in CI against `docker compose --profile app`.
- Coverage target: Domain + Application ≥ 80 %.

---

## 14. Configuration keys (examples)

```
PublicBaseUrl                      https://example.com
ConnectionStrings:Sql
Storage:BlobServiceUri | Storage:ConnectionString (local Azurite)
Email:Provider (Smtp|Acs), Email:From, Email:Smtp:Host/Port, Email:Acs:Endpoint
Email:ChatDigestDelayMinutes       5
Admin:Emails                       ["owner@example.com"]
Auth:Google:ClientId/ClientSecret, Auth:Facebook:AppId/AppSecret, Auth:Apple:ClientId/TeamId/KeyId/PrivateKey
PaymentInstructions:Templates:en|de|fr|es|pl
Features:RequireAdminMfa           true (false allowed only in Development)
Seo:NoIndex                        true on staging
```
`.env.example` documents all local values. Secrets locally via `dotnet user-secrets`.

---

## 15. Implementation phases

Each phase ends with: green CI, updated README/DECISIONS, acceptance criteria demonstrated. Tick items as they are done.

### Phase 0 — Repository foundation
- [x] `git init`, `.gitignore` (VisualStudio + Node), `.gitattributes`, `.editorconfig`, LICENSE (MIT), README skeleton
- [x] Solution + 4 backend projects + 3 test projects, `Directory.Build.props` (net10.0, nullable, warnings as errors, analyzers), `Directory.Packages.props`, `global.json`
- [x] Angular workspace in `src/frontend` (standalone, SCSS, strict mode, ESLint, Prettier), `proxy.conf.json`
- [x] `docker-compose.yml` with SQL Server, Azurite, Mailpit + healthchecks. `.env.example`
- [x] `ci.yml` (build + test both sides), `codeql.yml`, `dependabot.yml`, PR template
- **AC:** `docker compose up -d` starts the dependencies. API returns `/health/live` 200. `ng serve` shows a placeholder page through the proxy. CI is green on the first PR.
- **AC status (2026-11-13):** files are in place; frontend format/lint/tests/build and the backend build pass locally.
  `docker compose up -d` verified: `sqlserver`, `azurite` and `mailpit` all report `healthy`.
  Still to demonstrate: running the API and the backend tests locally (Windows Smart App Control blocks freshly built binaries),
  and the first green CI run (needs the GitHub repository, §16).

### Phase 1 — Public site, i18n, SEO, container
- [ ] Sticky top bar, anchor navigation with active-section highlight, hamburger menu
- [ ] About, Gallery (carousel + fullscreen viewer), Pricing, Contact, Footer. Content in typed content files. Placeholder images and texts
- [ ] `@angular/localize` with 5 locales, language switcher, initial translations (`needs-review`)
- [ ] Static prerendering of landing + legal pages, meta/canonical/hreflang/OG/JSON-LD, `sitemap.xml`, `robots.txt`
- [ ] Legal placeholder pages + cookie information notice
- [ ] ASP.NET Core static hosting per locale, `/` language redirect, CSR fallback, cache headers, security headers (CSP etc.)
- [ ] Multi-stage `Dockerfile`, `app` profile in compose, Trivy in CI, Lighthouse CI
- **AC:** `docker compose --profile app up` serves `/pl/`, `/de/`… with content visible in *view-source*. Lighthouse mobile ≥ 90 in all 4 categories. No CSP violations.

### Phase 2 — Azure + CD (deploy early)
- [ ] Bicep modules for §10 (DB/Storage/ACS can be included now even though they are not used yet)
- [ ] OIDC federation, GitHub Environments, `infra-ci.yml`, `cd.yml` (build once → staging → approval → prod), smoke tests, `cleanup.yml`
- [ ] App Insights + Serilog wired up. `/api/version`. Staging noindex
- [ ] `docs/RUNBOOK.md` (first version: setup from zero, deploy, rollback)
- **AC:** a merge to `main` deploys the public site to staging automatically and to production after approval. Rollback documented and tested once. Budget alert exists.

### Phase 3 — Accounts & security baseline
- [ ] Domain/Infrastructure: `ApplicationUser`, EF Core DbContext, first migration, migration Job in CD
- [ ] BFF cookie auth, antiforgery, `/api/auth/*`, `/api/account/*`, ProblemDetails error codes
- [ ] Email outbox + dispatcher + SMTP/ACS senders + localized transactional templates
- [ ] Google + Facebook login (config-gated), account linking rules, complete-profile step
- [ ] Admin seeding, mandatory TOTP 2FA for admins, audit log
- [ ] Rate limiting, lockout, password policy, re-authentication, data export, account deletion
- [ ] Angular: auth state service (signals), guards, login/register/confirm/forgot/reset/2FA/account pages, top-bar user menu
- [ ] Integration tests: auth flows + authorization matrix skeleton
- **AC:** a user can register, confirm by email (Mailpit locally / ACS on staging), sign in with password and Google, export and delete the account. An admin cannot reach admin endpoints without 2FA.

### Phase 4 — Chat
- [ ] Domain: Conversation, Message, Attachment. Migrations
- [ ] Upload pipeline (magic-byte validation, re-encode, thumbnails, Blob), `/api/files` with access checks, orphan cleanup
- [ ] Customer chat API + SignalR hub, read receipts, unread counts
- [ ] Email digest notifications (debounced), first-message dialog, "send replies to my email" checkbox
- [ ] Angular customer chat (infinite scroll, attachments with preview, fullscreen viewer, reconnect handling)
- [ ] Angular admin: conversation list + conversation view with customer side panel
- [ ] Integration tests: IDOR on messages/files, rate limits, upload validation, notification batching
- **AC:** customer ↔ admin real-time conversation with photos works on mobile. Emails arrive batched according to the rules.

### Phase 5 — Orders
- [ ] Domain order state machine + unit tests (every transition)
- [ ] Admin order API (create from conversation, edit, transitions, photos), payment-instructions template from config
- [ ] System messages + `OrderUpdated` SignalR events + emails via the chat digest
- [ ] Customer *My orders* list + detail (timeline, photos). Tab shown only when orders exist
- [ ] Angular admin: create-order panel in the conversation (pick a reference image from attachments), orders list with status filter, order detail with transition buttons and photo management
- [ ] Integration tests: drafts invisible to customers, cross-customer access denied, invalid transitions → 409 with error code
- **AC:** full flow: chat → admin creates draft → confirm (customer gets payment info in chat + email) → paid → photos → ready → shipped → completed, visible to the customer at every step.

### Phase 6 — Security & quality hardening
- [ ] ZAP baseline scan in CD, fix findings. Review CSP, headers and cookies with securityheaders.com-style checks
- [ ] Review against an ASVS L2 checklist (`docs/SECURITY.md` with the status of each relevant item)
- [ ] Load/abuse sanity checks (rate limits, large uploads)
- [ ] Accessibility pass (axe in E2E or unit tests), Lighthouse ≥ 90 still holds
- **AC:** no high findings in ZAP/Trivy/CodeQL. `SECURITY.md` ready to hand to the external pentesters (scope, test accounts on staging, contact).

### Phase 7 — Operations polish
- [ ] Alerts and availability test in prod, outbox dead-letter alert, dashboards (App Insights workbook)
- [ ] DB restore drill documented, Blob soft delete verified
- [ ] README final: architecture diagram (Mermaid), screenshots, "how CI/CD works", tech decisions, badges
- **AC:** the README alone lets a reviewer understand and run the project. Runbook covers incidents.

### Phase 8 — When a domain exists (blocked until the owner buys one)
- [ ] Custom domain + managed certificate on the Container App, `PublicBaseUrl` updated, sitemap regenerated
- [ ] ACS custom email domain (SPF, DKIM, DMARC)
- [ ] OAuth redirect URIs updated, Google consent screen published, Facebook app review + data-deletion URL
- [ ] **Sign in with Apple** (Apple Developer account, Services ID, key in Key Vault), private-relay email handling (register the sending domain with Apple)
- [ ] Google Search Console + sitemap submission

### Phase 9 — Optional extras
- [ ] Playwright E2E suite in CI (if not done earlier)
- [ ] Cloudflare Turnstile on register / forgot-password
- [ ] Azure SignalR Service backplane + scaling to 2 replicas (demo of horizontal scaling)
- [ ] Have I Been Pwned password check
- [ ] Typing indicator in the chat

---

## 16. Open items — owner's tasks (Claude Code cannot do these)

- [ ] Choose a region and create the Azure subscription/budget. Decide **prod DB tier** (Basic DTU vs. free offer with billing).
- [ ] Create the GitHub repository (public), enable secret scanning/push protection, add environments and reviewers.
- [ ] Register OAuth apps: Google Cloud Console (OAuth client), Meta for Developers (Facebook Login). Later Apple Developer.
- [ ] Provide real content: texts for About/Pricing/Contact in all languages (or approve the machine translations), gallery photos, logo, colors.
- [ ] Provide payment instructions text (IBAN etc.) per language → stored in Key Vault.
- [ ] Legal texts reviewed by a lawyer (privacy policy, terms, cookies, withdrawal rules, retention period for order records).
- [ ] Buy a domain (unblocks phase 8).
