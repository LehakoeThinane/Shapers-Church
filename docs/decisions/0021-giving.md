# 0021. Giving: card gifts through a provider's page, EFT and cash recorded by the finance team

Status: Proposed (2026-10-08)

## Context
The church takes tithes and offerings by card through a Yoco payment link, by bank transfer and in person. None of it was recorded in the platform, so there were no totals, no giving history for members and nothing to base a Section 18A certificate on. Card details must never pass through the church's systems. Who gives and how much is personal information, and church giving can reveal religious belief, so it needs the same care as other sensitive records. SARS requires the church to keep its financial records for five years.

## Decision
- **Giving module** (schema `giving`) with funds and gifts. Gifts are church-wide: the permissions must be held at the church's root.
- **Card gifts go through a payment provider's own page.** The API creates a checkout and sends the giver there; the gift is recorded as received only when the provider's **signed webhook** says so (signature and timestamp checked, amount must match, repeats change nothing). The provider sits behind `IPaymentProvider`; the first is **Yoco**, which the church already uses. Until its keys are set, card giving falls back to the existing Yoco payment link.
- **EFT and cash** are recorded by the finance team as they arrive; bank transfers aren't matched automatically.
- **Members** see their own giving in the app; **visitors** give with a name and an email for the receipt, without an account.
- **Permissions:** `giving.view` (totals, gifts, statements) and `giving.manage` (record gifts, funds). Both sensitive; every read is audited.
- **Retention:** gifts are kept five years after they were given, then deleted; card gifts that never went through are deleted after 30 days. Erasing a giver removes their name and email from their gifts but keeps the amounts for the financial records.

## Consequences
- No new Azure resource. The only cost is the provider's card fee per gift.
- Not yet: monthly recurring gifts (Yoco's checkout is once-off), automatic bank-statement matching, and issuing Section 18A certificates. The yearly statement gives the church the figures for them.
- Another provider (PayFast, Paystack) can be added behind the same interface if the church changes.
