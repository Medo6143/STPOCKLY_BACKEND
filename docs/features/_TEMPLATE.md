# Feature: <Name>

Status: Draft | In progress | Done · Sprint: <n> · Owner: <name>

## 1. Purpose
What problem this solves for the shop owner, in two or three sentences.

## 2. User flow
Numbered steps from the user's point of view. Include the Arabic screen titles.

## 3. Endpoints
| Method | Path | Auth / role | Request | Response | Status codes |
|---|---|---|---|---|---|

## 4. Data model changes
Tables, columns, constraints, **indexes and the query each one serves**. Link the migration name. Update `docs/erd.md`.

## 5. Integrations
For each (OpenRouter, Redis, Hangfire, MinIO...):

| | |
|---|---|
| Why | |
| Endpoint / operation | |
| Auth | |
| Timeout / retry | |
| Failure behaviour | |
| Cost | |

## 6. Cache keys
| Key | TTL | Invalidated by |
|---|---|---|

Update `docs/design/redis.md`.

## 7. Business rules
Bulleted, testable statements.

## 8. Error cases
| Case | Error code | HTTP | User message (ar / en) |
|---|---|---|---|

## 9. Configuration / env vars
| Name | Example | Secret? |
|---|---|---|

## 10. Mobile / Admin screens
Screen list with the 4 states (loading, empty, error, data). RTL notes.

## 11. Tests
List of test classes and the scenarios they cover.

## 12. Known limitations
What this version does not do, and the issue that tracks it.
