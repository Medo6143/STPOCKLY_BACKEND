# Feature: Plans and payments (Paymob)

Status: Done (backend and admin; contract rows still `planned` until the orchestrator flips them) · Sprint: billing (backend 7) · Owner: backend lane

## 1. Purpose
Shop owners see their plan and usage and pay for a better plan with a card (Visa, Mastercard, Meeza), a mobile wallet (Vodafone Cash and others) or cash at a kiosk, through Paymob only (ADR-0015). A payment buys one month of a plan; only a verified Paymob callback (or the reconcile job) activates it. The platform admin sees every payment and the revenue.

## 2. User flow
1. Owner opens "الباقات" (Plans, B1) and "اشتراكي" (My subscription, B2): current plan, period, scans today, members, any pending payment.
2. Owner picks a paid plan and a method and taps "ادفع" (Pay): the app sends B3 with a `clientRequestId` created once (reused on retries).
3. Card / wallet: the app opens `checkoutUrl` in the system browser (Paymob hosted page). Kiosk: the app shows `referenceNumber` and `expiresAt`; the owner pays cash at a kiosk partner.
4. The app polls B4 until the status is `Paid`, `Failed` or `Expired`. The redirect back never activates anything.
5. Paymob posts the transaction callback to the API; on a verified, matching success the payment becomes `Paid` and the subscription gets its month.
6. To switch method while a payment is pending, the owner cancels it (B6) and starts again.

## 3. Endpoints
| Method | Path | Auth / role | Request | Response | Status codes |
|---|---|---|---|---|---|
| GET | /api/billing/plans (B1) | user | — | `BillingPlan[]` (active, free first, then price) | 200, 401, 403 |
| GET | /api/billing/subscription (B2) | user | — | `SubscriptionSummary` | 200, 401, 403 |
| POST | /api/billing/checkout (B3) | owner | `{ clientRequestId, planId, method, walletPhone? }` | `PaymentDto` (201 new + `Location`, 200 repeat) | 200, 201, 400, 401, 403, 404, 409, 502 |
| GET | /api/billing/payments/{id} (B4) | user | — | `PaymentDto` | 200, 401, 403, 404 |
| GET | /api/billing/payments (B5) | user | `page`, `pageSize` | paged `PaymentDto`, newest first | 200, 400, 401, 403 |
| POST | /api/billing/payments/{id}/cancel (B6) | owner | — | `PaymentDto` (status `Expired`) | 200, 401, 403, 404, 409 |
| POST | /api/payments/paymob/callback | anonymous, HMAC | Paymob transaction processed callback, `?hmac=` | empty | 200, 401 (operations, not in the index or OpenAPI) |
| GET | /api/admin/payments (AD21) | admin | `shopId`, `status`, paging | paged `AdminPayment` | 200, 400, 401, 403 |
| GET | /api/admin/payments/summary (AD22) | admin | `from`, `to` (Cairo days, ≤ 92 apart, default 30 days) | `PaymentSummary` | 200, 400, 401, 403 |

## 4. Data model changes
Migration `PlanPayments`: table `payments` with its indexes and checks, all listed with the query each serves in `docs/erd.md` §3.8. No change to `shop_subscriptions` (the existing `ix_shop_subscriptions_ends_on_active` serves the downgrade job).

## 5. Integrations

**Paymob** (`Infrastructure/Payments/Paymob`, `IPaymentGateway` port in Application; SOLID O: another provider is a new class)

| | |
|---|---|
| Why | Hosted card/wallet checkout and kiosk references; Stockly never sees card numbers or wallet PINs |
| Endpoint / operation | `POST v1/intention/` (Create Intention: `amount` in piastres from `decimal`, `currency` EGP, `payment_methods` = the method's integration id, one item, `billing_data` from the owner, `special_reference` = our payment id, `notification_url`, `redirection_url` for card/wallet, `expiration`); card → `https://eg.checkout.paymob.com/?publicKey=…&clientSecret=…`; wallet → `POST api/acceptance/payments/pay` with `source {identifier: walletPhone, subtype: WALLET}` and the intention's payment key → `redirect_url`; kiosk → the same pay request with `source {identifier: AGGREGATOR, subtype: AGGREGATOR}` → `data.bill_reference`; reconcile → `POST api/auth/tokens` (`api_key`) then `POST api/ecommerce/orders/transaction_inquiry` (`auth_token`, `order_id`) |
| Auth | `Authorization: Token <secret key>` (intention); payment key (pay request); auth token from the API key (inquiry); callbacks verified with HMAC-SHA512 of the documented 20 fields (`PaymobHmac`) and the HMAC secret, constant-time compare |
| Timeout / retry | `IHttpClientFactory` typed client + standard resilience handler: attempt and total timeout `Paymob__TimeoutSeconds` (30 s), circuit breaker, retries disabled for unsafe methods (every Paymob call is a POST; a repeated pay request could create a second kiosk reference) |
| Failure behaviour | Checkout: any 4xx/5xx/timeout/unreadable answer, or JSON of an unexpected shape, → `502 Billing.ProviderUnavailable`, nothing stored, retry with the same `clientRequestId`. Reconcile: a provider error, or any unexpected exception for one payment, skips that payment until the next run and the rest of the batch still runs. Logs carry payment id, method, order id, HTTP status only (never keys, bodies, HMACs or phone numbers) |
| Cost | Paymob fees per transaction (merchant contract); no cost per API call |

**Hangfire** (queue `maintenance`, no tenant): `ReconcilePaymentsJob` every 15 minutes (`*/15 * * * *` UTC, `payment-reconcile`); `DowngradeLapsedSubscriptionsJob` daily at 22:10 UTC (`subscription-downgrade`), just after the Cairo day changed.

## 6. Cache keys
None. Billing reads go to PostgreSQL (small, owner-only screens); `docs/design/redis.md` is unchanged.

## 7. Business rules
- The amount is the plan's `monthlyPrice` at checkout; the client never sends an amount (ADR-0015 §6). Piastres = `amount × 100` with `decimal` (`Piastres.FromPounds`); more than 2 decimals is refused.
- Owner only for B3 and B6 (Staff → `403 Auth.Forbidden`). A free plan (price 0) is `400 Billing.FreePlan`; an inactive plan `400 Plan.Inactive`; a method without an integration id (or payments disabled) `400 Billing.MethodUnavailable`.
- One open payment per shop: a `Pending` payment whose `expires_at` is in the future blocks B3 (`409 Billing.PaymentPending`). Checkouts of a shop are serialized by locking its active subscription row; the open check is repeated inside that transaction.
- B3 is idempotent by `clientRequestId` (ADR-0006): a repeat returns the existing payment (200) and never calls Paymob again; a concurrent repeat that loses the unique insert gets the winner.
- Paymob is called before any transaction. The payment row is inserted only after Paymob accepted the checkout.
- State machine: `Pending → Paid | Failed | Expired`; `Paid` is final. A success after a cancel (`Expired`) or a failed attempt still becomes `Paid` (the money was taken). A failure only fails a `Pending` payment.
- Callback: invalid or missing HMAC, or not a `TRANSACTION` callback → `401` with no body, nothing changes. Verified callbacks always answer `200`: unknown order (logged by id), order/amount/currency mismatch (never `Paid`), still pending (kiosk not paid yet), repeats (no change). Lookup is by the signed `order.id`.
- Paid buys one month (ADR-0015 §5), in the same transaction as the payment update (payment locked first, then the active subscription): same plan → `ends_on = max(today, ends_on) + 1 month` (open-ended stays open-ended); other plan → current ends today, new one starts today and ends today + 1 month; the quota override is carried over.
- Reconcile: pending payments past `expires_at` (100 per run, oldest expiry first) are checked with the transaction inquiry; a matching success → `Paid` (with its month), a matching failure → `Failed`, anything else (no transaction, still pending, mismatch) → `Expired`.
- Downgrade: an active subscription not on the default plan whose `ends_on` is before today lapses (keeps its last day) and the default free plan starts today, open-ended, keeping the override (500 shops per run). System action: no `AdminAuditLog`.
- Kiosk expiry: `Paymob__KioskExpiryHours` (48 h; Paymob returns no expiry). Card/wallet: `Paymob__PendingExpiryMinutes` (30 min). The same lifetime is sent as the intention's `expiration`.

## 8. Error cases
| Case | Error code | HTTP | User message (ar / en) |
|---|---|---|---|
| Free plan | `Billing.FreePlan` | 400 | الباقة المجانية لا تحتاج إلى دفع. / A free plan does not need a payment. |
| Method not configured / payments off | `Billing.MethodUnavailable` | 400 | طريقة الدفع هذه غير متاحة حالياً… / This payment method is not available right now… |
| Inactive plan | `Plan.Inactive` | 400 | (existing) |
| Unknown plan | `Plan.NotFound` | 404 | (existing) |
| Pending payment open | `Billing.PaymentPending` | 409 | لدى المتجر عملية دفع قيد الانتظار… / The shop already has a pending payment… |
| Provider failed | `Billing.ProviderUnavailable` | 502 | لم يستجب مزود الدفع. لم يتم خصم أي مبلغ… / The payment provider did not answer. Nothing was charged… |
| Unknown or another shop's payment | `Payment.NotFound` | 404 | لم يتم العثور على عملية الدفع. / The payment was not found. |
| Cancel a non-pending payment | `Billing.NotCancelable` | 409 | يمكن إلغاء عملية الدفع قيد الانتظار فقط. / Only a pending payment can be cancelled. |
| Wallet without a valid phone | `Validation.Failed` (`errors.walletPhone`) | 400 | الدفع بالمحفظة يحتاج رقم موبايل مصري… / A wallet payment needs an Egyptian mobile number… |
| Staff on B3/B6 | `Auth.Forbidden` | 403 | (existing) |

`502` maps from the new `ErrorType.BadGateway` (Domain; appended, never reordered).

## 9. Configuration / env vars
| Name | Example | Secret? |
|---|---|---|
| `Paymob__SecretKey` | `sk_test_…` (empty disables payments) | yes |
| `Paymob__PublicKey` | `egy_pk_test_…` | yes (kept in `.env`) |
| `Paymob__HmacSecret` | — | yes |
| `Paymob__ApiKey` | — (inquiry auth token; see decisions) | yes |
| `Paymob__IntegrationIds__Card` / `__Wallet` / `__Kiosk` | `4101` | yes (kept in `.env`) |
| `Paymob__NotificationUrl` | `https://api.example.com/api/payments/paymob/callback` | no |
| `Paymob__RedirectionUrl` | `https://example.com/payment-done` | no |
| `Paymob__BaseUrl` | `https://accept.paymob.com/` (default) | no |
| `Paymob__CheckoutBaseUrl` | `https://eg.checkout.paymob.com/` (default) | no |
| `Paymob__PendingExpiryMinutes` | `30` (default) | no |
| `Paymob__KioskExpiryHours` | `48` (default) | no |
| `Paymob__TimeoutSeconds` | `30` (default) | no |

Validated at startup (`PaymobOptions`, messages name the variable, never the value); public key, HMAC secret and API key are required only when the secret key is set.

## 10. Mobile / Admin screens
Mobile screens are in `stockly-mobile`. Admin (`Stockly.Admin`, `docs/features/admin.md` §13–§14): Payments page (filters, paged table, link to the shop), overview revenue card with the by-method split, the shop's payments on the shop page, plan prices shown prominently with free plans marked. Each in loading, empty, error and data states.

## 11. Tests
- Domain: `PaymentTests` (state machine, late success after cancel, Paid final, matching, attach once), `PiastresTests`, `ShopSubscriptionPaidMonthTests` (extend from the later of today and `ends_on`, switch, lapse).
- Application: `BillingHandlerTests` (B1–B6 happy paths, validation, not found / wrong tenant, `PaymentPending` before and inside the transaction, `FreePlan`, `MethodUnavailable`, `ProviderUnavailable` with nothing stored, idempotent and concurrent repeat), `AdminPaymentHandlerTests` (callback paid/switch/extend/repeat/mismatch/declined/pending/late success, invalid signature, reconcile and downgrade with a fake clock, AD21/AD22).
- Infrastructure: `PaymobPaymentGatewayTests` (fake `HttpMessageHandler`: intention body per method, checkout URL, wallet phone, kiosk reference, 4xx/5xx/timeout/unreadable → `ProviderUnavailable` without retry, HMAC field order and value, tampered/missing HMAC, inquiry, options validation), `PaymentPersistenceTests` (Testcontainers: tenant isolation, unique client request id, open-payment expiry, callback in one transaction, **four concurrent identical callbacks extend once**, reconcile and downgrade jobs, admin list/summary, newest-first indexes), `MigrationTests` (table, indexes, partial predicates, checks).
- Api: `BillingEndpointTests` (B1–B6 codes incl. Staff 403 and wrong tenant 404, idempotent checkout, 502 with nothing stored, disabled payments, callback 200/401 empty body/repeat/mismatch/unknown order, AD21/AD22, shop token on admin 403), `ErrorTextTests` (ar/en text for the new codes, 502 mapping).
- Admin: `PaymentsPageTests`, `PlansPageTests.Render_FreeAndPaidPlans_*`.

## 12. Known limitations
- No automatic renewal or saved cards (ADR-0015 §5). Refunds are manual in the Paymob dashboard (kiosk cannot be refunded). A refund or void callback does not undo a `Paid` payment.
- A callback that arrives before the payment row is committed (Paymob answering faster than our insert) is answered `200` as an unknown order; the reconcile job settles the payment after its expiry.
- Callback types other than `TRANSACTION` (e.g. card `TOKEN`) are answered `401` (we never save cards).
- Paymob keys do not exist yet: every Paymob behaviour is tested against fakes. Verify the assumptions below with test keys before real money moves.

### Paymob assumptions to verify with test keys
1. `provider_order_id` stores the intention's `intention_order_id` (Paymob order id), because the callback signs `order.id`, not the intention id (`pi_…`) or `special_reference`. The intention id is not stored.
2. `expiration` in the Create Intention body is in seconds (the docs give only the example `3600`).
3. Wallet and kiosk with the Intention API use the documented-elsewhere Accept pay request `POST api/acceptance/payments/pay` with the intention's `payment_keys[0].key` as `payment_token`; wallet `source = {identifier: <phone>, subtype: "WALLET"}` answers `redirect_url` (fallback `iframe_redirection_url`); kiosk `source = {identifier: "AGGREGATOR", subtype: "AGGREGATOR"}` answers `data.bill_reference`. The current docs pages for wallets and kiosk do not describe the API call.
4. Kiosk returns no expiry: `Paymob__KioskExpiryHours` (48 h) is used and sent as the intention's `expiration`.
5. The HMAC value arrives as the `hmac` query parameter of the processed (POST) callback; values are concatenated as sent (numbers raw, booleans `true`/`false`, a missing or `null` field as an empty string); hex is compared case-insensitively.
6. `billing_data.phone_number` is required in practice: the wallet phone, else the owner's phone, else `NA`; a one-word name gets `last_name = NA`.
7. The transaction inquiry by order id needs an auth token from `POST api/auth/tokens` with the merchant API key (`Paymob__ApiKey`, a key the brief's list did not name), passed as `auth_token` in the body; an order without transactions answers `404` (treated as "no transaction").
8. A transaction is a success when `success = true`, `pending = false`, `is_voided = false`, `is_refunded = false`; `pending = true` is still waiting; anything else is a failure with `data.message` as the reason.
9. `notification_url` per intention overrides the integration's dashboard callback; when `Paymob__NotificationUrl` is empty the dashboard setting is used.
10. Unified Checkout for Egypt is `https://eg.checkout.paymob.com/?publicKey=…&clientSecret=…` (configurable as `Paymob__CheckoutBaseUrl`).
