# 0004. Self-hosted identity: phone codes for members, 2FA for staff

Status: Accepted (2026-09-28)

## Context
Most members use phones, and many don't use email regularly. Staff handle special personal information. Managed identity providers charge per active user and add another processor of personal data.

## Decision
- ASP.NET Core Identity, self-hosted. A login (User) always links to one church record (Person).
- **Members** sign in with a one-time code sent to their phone: SMS now, WhatsApp later.
  - Codes are HMAC-hashed with a server key, expire after 5 minutes and allow 5 tries.
  - Code requests are rate-limited per number and per IP.
  - The app gets a 15-minute access token and a refresh token that rotates on every use. Reusing a retired refresh token ends every session from that sign-in.
- **New members** consent to the church keeping a record before an account is created. A new login is linked to an existing record only when exactly one adult record has that phone number and no login. Otherwise a new record is created and flagged as a possible duplicate.
- **Staff** sign in with email, a password (12+ characters, locked after 5 failures) and an authenticator app.
  - The admin portal uses an HttpOnly, SameSite=Strict session cookie plus a required CSRF header, so JavaScript can't read any token.
  - Sensitive permissions work only in sessions signed in with a second factor. This is configurable and relaxed in local development.

## Consequences
- We own the cost of sending codes and the protection against abuse.
- An SMS provider must be chosen before production. The log-only sender is refused outside Development.
