# 0018. Translations of pages and posts

Status: Accepted (2026-10-06)

## Context
Many members read more easily in isiZulu, Sesotho or another home language than in English. AI can draft translations (ADR 0017), but a machine translation of church teaching can be subtly wrong. A fluent speaker must check every translation before members see it.

## Decision
- **A translation is a copy of a page or post in another language**, linked to its English original (`translation_of_id`, `language`). It shares the original's address: `/blog/{slug}/zu`, `/{slug}/st`. Addresses are unique per language, and an original has at most one translation per language. When the original's address changes, its translations follow.
- **Checked before published.** A translation can only be published or scheduled after someone with `content.translations.review` marks it checked. The built-in **Translator** role holds this permission. Editing a translation's text clears the check and takes it off the site until it is checked again. If the English changes after a translation was checked, the admin portal flags the translation.
- **Translators can edit translations** (not originals) and check them. Publishing still needs `content.publish`.
- **AI drafts** come from the Assist module (`POST /api/admin/assist/drafts/translate`):
  - The languages offered are a setting (`Assist:Languages`, isiZulu and Sesotho first).
  - Email addresses, phone numbers and ID numbers are replaced with placeholders before sending and put back afterwards. Contact details stay in the translation but never reach the AI service.
- **Public reads:**
  - Lists show English originals.
  - Reading one item takes `?lang=`.
  - Every item says which languages it can be read in, so the app and the website can offer a switch.
  - The website builds a page per checked, published translation, with `lang` and `hreflang` set.
- `Shapers.SharedKernel.Languages` lists the languages the platform knows: English and South Africa's other official languages.

## Consequences
- Translators need a staff login (the Translator role sees no personal information, so two-step sign-in isn't required).
- The website's own wording (menus, buttons) stays in English for now; only page and post content is translated.
- Church lessons and announcements are not translated yet. Announcements would need each person's preferred language.
