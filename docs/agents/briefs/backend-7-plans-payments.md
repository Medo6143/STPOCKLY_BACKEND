Read AGENTS.md fully before starting.

# Brief: backend-7-plans-payments

Repository: `stockly-backend` (`D:\FLUTTER\stockly`), branch `feature/plans-payments` (already checked out, based on `main`) · Lane: BACKEND (+ Admin UI) · Contract version: **v1.7** (`docs/api/contract.md` §12)

You have not seen the conversation that produced this brief. Everything you need is here or in the files it links. If something you need is missing or contradicts AGENTS.md, ADR-0015 or the contract, **stop and report** instead of guessing.

## 1. Goal and why
The product owner wants shop owners to see their plan and pay for a better one with **Visa/cards, Vodafone Cash (and other wallets) or cash at a kiosk, through Paymob only** (Fawry was dropped). Build the plans/payments API (B1–B6), the Paymob gateway, the HMAC-verified callback, the jobs, the admin endpoints (AD21–AD22) and the admin dashboard pages for payments.

## 2. Scope
**You may change / create:** `src/**` (including `src/Stockly.Admin`), `tests/**`, `.env.example` (placeholders only), `docs/erd.md`, `docs/design/redis.md` (only if a key is added), `docs/features/billing.md` (new, from `_TEMPLATE.md`), `docs/features/admin.md` (payments part), `docs/api/openapi.json` (regenerated only).

**You must NOT touch:** `docs/api/contract.md`, `docs/adr/**`, `docs/agents/**`, `docs/sprints/**`, `AGENTS.md`, `CLAUDE.md`, `.env`, `Directory.Packages.props` (no new packages: Paymob is called over HTTP with `IHttpClientFactory` + `Microsoft.Extensions.Http.Resilience`; if you think a package is needed, stop and report).

**Git and machine safety:** never run git commands that change the working tree or history. Never kill processes by image name; stop only processes you started, by PID. Never delete Docker containers, images or volumes. Never change anything outside this repository. **Never call the real Paymob API** (no keys exist; all tests use fake handlers and signed fake callbacks). An API runs on port 5000 and the admin on 5080 for the product owner; leave them alone and do not change the local dev database.

## 3. Binding sources
- **ADR-0015** (read it fully): one `IPaymentGateway` port, Paymob implementation in `Infrastructure/Payments/Paymob`, hosted checkout only, callbacks as the source of truth, what a payment buys (one month; extend same plan, otherwise switch), downgrade job, money rules, owner only, secrets, test mode.
- **Contract §12** (B1–B6 schemas, codes, idempotency by `clientRequestId`, the callback rules in §12.3, AD21–AD22) and the new §9 codes.
- **Paymob specifics: use the official docs only** (https://developers.paymob.com/paymob-docs, its `llms.txt` index, the Intention API, Unified Checkout, Mobile Wallets [EGY], Kiosk [EGY], and the transaction callback / HMAC pages). Implement exactly what they document for: creating an intention (amount in piastres from `decimal`, currency EGP, the method's integration id, billing data from the shop/owner, `special_reference` = our payment id, `notification_url` = our callback, `redirection_url` from config), building the Unified Checkout URL from the public key and client secret, wallet and kiosk specifics (wallet phone, kiosk reference number and expiry if the API returns them), the transaction-processed callback body and its HMAC (exact field order and algorithm from the docs), and querying a transaction/intention for the reconcile job. If the docs are ambiguous on any of these, write down what you assumed in `docs/features/billing.md` under "Paymob assumptions to verify with test keys" and in your report; do not stop.
- Configuration (all from `.env`, validated at start-up only when payments are enabled): `Paymob__SecretKey`, `Paymob__PublicKey`, `Paymob__HmacSecret`, `Paymob__BaseUrl`, `Paymob__IntegrationIds__Card`, `__Wallet`, `__Kiosk` (a method without an id is "not configured" → `400 Billing.MethodUnavailable`), `Paymob__RedirectionUrl`, `Paymob__PendingExpiryMinutes` (card/wallet, default 30), kiosk expiry from Paymob's response or `Paymob__KioskExpiryHours` (default 48). Payments disabled when `Paymob__SecretKey` is empty: B1/B2/B4/B5 still work, B3 returns `Billing.MethodUnavailable`.

## 4. Data
- New `payments` table (tenant entity, global filter): id, shop_id, plan_id, client_request_id, amount `numeric(18,2)`, currency, method, provider, status, provider_order_id (intention id), provider_transaction_id, checkout_url, reference_number, expires_at, failure_reason, created_at, paid_at, updated_at. Unique `(shop_id, client_request_id)`; unique `provider_transaction_id` where not null (callback idempotency); index for "newest pending of a shop", for B5 paging, for AD21 filters and for the reconcile job. Document each in `docs/erd.md`.
- Subscription change on `Paid` in **one transaction** with the payment update (lock the payment row first, then the shop's active subscription), per ADR-0015 §5. Never charge or change anything from the redirect.
- Jobs (Hangfire, `maintenance` queue): reconcile `Pending` payments past `expires_at` (query Paymob; `Paid` → apply, else `Expired`); daily downgrade of shops whose paid subscription ended to the default free plan (system action, no AdminAuditLog).
- Admin reads (AD21, AD22) through admin ports under `Infrastructure/Persistence/Admin/` (rule 9).

## 5. Admin UI (`src/Stockly.Admin`)
- New **Payments** page: filters (shop, status), paged table (shop, plan, method, amount, status chip, created, paid, provider transaction id), link to the shop.
- Overview: a revenue card (paid amount and count, last 30 days) and a by-method breakdown (AD22).
- Shop detail: the shop's payments (AD21 with `shopId`).
- Plans page: show price prominently; free plans marked.
- bUnit tests for each in its 4 states.

## 6. Critical rules
1–6 money and transactions (rule 6: never `double`; piastres computed with `decimal`), 8–10 tenancy (another shop's payment → 404), 9 admin access, 11–12 secrets and logs (never log callback bodies, HMACs, keys, phone numbers in full; log payment id, provider, status, amounts), 20 idempotent writes, 26–33 API/DB, 40–42 hygiene, SOLID O (gateway port).

## 7. Tests (first)
- Domain: payment state machine (Pending → Paid/Failed/Expired; Paid is final; late success after cancel becomes Paid), subscription extension rules (same plan extends from the later of today and ends_on; different plan switches; override kept), piastre conversion.
- Gateway with a fake `HttpMessageHandler`: intention per method (request body shape per docs), checkout URL, wallet phone handling, kiosk reference, 4xx/5xx/timeout → `Billing.ProviderUnavailable` with nothing persisted as Pending.
- Callback: valid HMAC → Paid + subscription extended; invalid HMAC → 401 and nothing changes; amount/currency/order mismatch → not Paid (logged); repeat → 200, no second extension; unknown order → 200; **two concurrent identical callbacks** (Testcontainers) → one extension.
- Handlers: every B endpoint happy path, validation, not found, wrong tenant, Staff → 403, `PaymentPending`, `FreePlan`, `MethodUnavailable`, idempotent repeat of B3.
- Jobs: reconcile and downgrade with a fake clock.
- Admin API and bUnit pages.
- `ContractConformanceTests`: rows stay `planned` in the contract until the orchestrator flips them; report the expected failure.

## 8. Checks
```bash
dotnet build -warnaserror
dotnet test
```
Zero warnings. Never skip, delete or weaken a test.

## 9. Do not commit, push or merge.

## 10. Final report (exact format)
```
AGENTS.md read: yes
Rules applied: <numbers and one line each>
Files changed: <list by folder>
Check outputs: <tail of build and test, test counts per project>
Migration: <name and one-line SQL summary>
Paymob assumptions to verify with test keys: <list>
Decisions taken: <list, with reason>
Open questions: <list or "none">
Contract deviations: NONE   (anything else: stop before finishing and report it)
```
