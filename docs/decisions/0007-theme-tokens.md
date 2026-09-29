# 0007. One token source for Midnight and Rose

Status: Accepted (2026-09-28)

## Context
The app offers two palettes, Midnight (dark) and Rose (light), and the admin portal should match. Hard-coded colours drift, and they make switching palettes impossible.

## Decision
- `design/tokens/tokens.json` is the only place colours, radii, spacing and fonts are defined.
- `pnpm tokens` generates typed palettes for the app and CSS variables for the admin portal.
- Tokens are named by role (`text.primary`, `accent`, `interactive`), never by colour.
- Each palette records its appearance (dark or light). The app sets the phone's colour scheme to match, so the system tab bar and Liquid Glass render correctly.
- CI fails on stale generated files, on colour literals in app code, and on contrast below WCAG minimums.

## Consequences
- Rose's accent (#D2979F) is about 2:1 against its background. It works as a fill behind dark text, as on badges and the Give button. Thin marks in Rose that use only the accent colour, such as progress bars, use the `interactive` colour instead. The contrast test reports this as an advisory.
