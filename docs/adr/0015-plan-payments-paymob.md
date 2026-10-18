# ADR-0015: Paid plans through Paymob

- Status: Accepted (2026-10-18, product owner: Paymob with Vodafone Cash or Visa; Fawry dropped the same day)

## Context
Plans and subscriptions exist since step 1.5 (one free default plan, `standard`), and the platform admin manages plans and assigns them (Sprint 5). Shop owners cannot see their plan or pay for a better one. In Egypt the owner pays by card, by mobile wallet (Vodafone Cash and others) or in cash at a kiosk.

## Decision
1. **One port, one provider now.** `IPaymentGateway` in Application, implemented in `Infrastructure/Payments/Paymob`. Another provider later is a new class (SOLID O); FawryPay was considered and dropped by the product owner. Provider HTTP clients come from `IHttpClientFactory` with the standard resilience handler; no SDK package.
2. **Methods.** `Card` (Visa, Mastercard, Meeza: Paymob), `Wallet` (Vodafone Cash and other wallets: Paymob), `Kiosk` (cash at Paymob kiosk partners: Paymob). The client chooses a method, never a provider.
3. **Hosted checkout only.** Cards and wallets are paid on the provider's hosted page (Paymob Unified Checkout). Stockly never sees or stores card numbers or wallet PINs (PCI scope stays with the provider). Kiosk returns a reference number and an expiry.
4. **Callbacks are the source of truth.** The mobile redirect never activates anything. A payment becomes `Paid` only from a provider callback whose Paymob HMAC verifies, whose amount and currency equal the stored payment, and whose provider order id matches. Callbacks are idempotent (unique provider transaction id); a repeat returns 200 and changes nothing. A reconcile job queries the provider for payments still `Pending` after their expiry.
5. **What a payment buys.** One month of the plan. On `Paid`: if the shop's active subscription is the same plan, `ends_on` is extended by one month from the later of today and the current `ends_on`; otherwise the active subscription ends today and a new one starts today for one month. The quota override is kept. A daily job moves shops whose paid subscription ended to the default free plan. No automatic renewal or saved cards in v1.
6. **Money.** `numeric(18,2)` EGP in Stockly; providers that take integer piastres get `amount × 100` computed with `decimal` (rule 6). The amount always comes from the plan price at checkout time, never from the client.
7. **Who can pay.** Owner only. Free plans (price 0) cannot be checked out.
8. **Secrets** (Paymob secret key, public key, HMAC secret, integration ids per method) live in `.env` only. Callback bodies are never logged; log payment id, provider, status and amounts.
9. **Test mode first.** Development uses each provider's test credentials and test integration ids. Tests use fake `HttpMessageHandler`s and signed fake callbacks; no test calls a real provider.

## Consequences
- New tables `payments` (and the callback de-duplication index), new contract section §12 (B1–B6), new admin read endpoints.
- The product owner must open merchant accounts (Paymob with Card, Wallet and Kiosk enabled) and put the keys in `.env` before real money moves.
- Refunds are manual in the provider dashboards in v1 (kiosk payments cannot be refunded at all).
