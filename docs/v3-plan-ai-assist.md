# Plan: AI help for staff (sermons, cell lessons, translation, search)

Status: approved (2026-10-06). Built: steps 1–3 (including YouTube captions), step 4 for pages and posts (ADR 0018), and the writing help from step 5 (ADR 0017). Next: search by meaning; translating church lessons and announcements later.

## Rules (from the project brief)
- **AI only drafts.** Nothing it writes is published or sent until a named person reviews, edits and accepts it. Everything it produces is labelled *AI draft* until then.
- **No chatbot** answering questions about faith or the Bible, and **no scoring of individuals**.
- **Only public or church-authored content goes in**: sermons, lessons, posts, pages, announcements. Cell reports, prayer requests, pastoral notes, chat and people's details are never sent. The code enforces this: the AI module can only read content through the modules' public contracts for published or staff-authored text.

## Where it runs (checked with the Azure CLI, 2026-10-06)

| Job | Service | Where the data is processed |
|---|---|---|
| Writing (summaries, questions, translation) | Azure OpenAI, `gpt-5.4-mini` class model | Account in **South Africa North**; the models are offered only as *Global Standard*, so a request may be processed outside South Africa. Acceptable because the input is public content with no personal information. |
| Transcribing sermon audio | Azure AI Speech, fast transcription | **South Africa North** |
| Search by meaning | Azure OpenAI `text-embedding-3-large` (Standard) + pgvector | **South Africa North** |

- The service connects with a **managed identity**, so there are no keys in settings. The demo can use a key kept in its environment file.
- `Ai:Provider = Disabled | Azure | Fake`. It's **Disabled** by default, and the AI buttons simply don't appear. `Fake` returns canned text for tests and local development.
- **Monthly spend cap** (`Ai:MonthlyBudgetZar`). Every call is logged with tokens and estimated cost, and calls stop when the cap is reached. Expected cost is well under R300/month at one sermon a week.

## 1. Sermon text (Media module)
- Sermons gain a **transcript**: text, language, source (*captions*, *audio* or *pasted*), and status (*none*, *working*, *ready* or *failed*).
- Three ways to get it:
  1. **Uploaded audio** (already planned after each service) is transcribed automatically by a background job.
  2. **YouTube captions:** the channel owner connects the church's YouTube channel once (Google sign-in). After that, captions for imported sermons are fetched automatically. This also covers the ~40 sermons already on YouTube.
  3. **Paste** a transcript by hand.
- Transcripts are kept with the sermon but are **not published** by default; staff can choose to show them.

## 2. Sermon → cell lesson (first)
- On a sermon with a transcript, **Draft a cell lesson** produces: summary, key verses (references only, WEB text where quoted), 5–6 discussion questions, an application point and a prayer focus.
- It opens in the editor as an **AI draft**. A pastor edits it and publishes it as a **church lesson**.
- **Church lessons are new in Groups:** materials written by pastors for every cell, not for one cell. Leaders see them in My cell, can pick one when recording a meeting, and can copy one to adapt it for their cell.
- On the same sermon, **Draft show notes** fills the sermon's summary, notes and topics for the media team to review (feature 2 from the list).

## 3. Translation (second)
- Languages: **isiZulu** and **Sesotho** first (`Ai:Languages`, easy to extend).
- **Translate** on posts, pages, church lessons and announcements creates a linked copy in the target language, marked *AI draft — needs a speaker's review*.
- A new **Translator** role with permission `assist.translations.review`, given to volunteers who speak the language. Only a reviewer can mark a translation as checked, and only checked translations can be published.
- The app and website show a language switch where a translation exists, falling back to English.

## 4. Writing help and search (after 2 and 3)
- **Tidy up / shorter / longer** buttons in the post, page and announcement editors. Their output always lands in the editor for the person to accept.
- **Search by meaning** over published sermons (and later posts and lessons) in the app and on the website. It uses embeddings in pgvector, with ordinary text search as a fallback.

## Data and code
- **New `Assist` module** (the brief's AI/Automation module), with schema `assist`:
  - `drafts` table: kind, source, target, model, prompt version, output, status (*pending*, *accepted* or *discarded*), who asked, who reviewed;
  - `usage` table: tokens, cost and monthly totals.
- Prompts are versioned files in the repo, so a change to a prompt is reviewed like code.
- Platform abstractions (`ITextGenerator`, `ITranscriber`, `IEmbedder`) so the provider can change without touching modules.
- Permissions:
  - `assist.drafts.create`: Media team, Campus pastor, Content editors;
  - `assist.translations.review`: Translator.
- Tests use the Fake provider, and cover the budget cap, draft-only publishing, the reviewer rule and the content allow-list.

## Build order (each step usable on its own)
1. Assist module, providers (Azure + Fake), budget and usage log, drafts. Admin: an **AI usage** page under System tools.
2. Sermon transcripts: audio transcription job, paste, then YouTube caption import (after the channel is connected).
3. Church lessons in Groups, plus *Draft a cell lesson* and *Draft show notes*.
4. Translation: linked language copies, Translator role, language switch in the app and on the website.
5. Writing help, then search by meaning.
6. ADR 0017 (AI drafting, data boundaries, providers), docs, privacy notice wording (*"we use AI services to help draft public content; no personal information is sent"*).

## Needed from the church
- Azure OpenAI and Speech resources in the **church's** Azure subscription (I add them to `infra/azure/main.bicep`). For the demo I can create them in the current subscription.
- The **YouTube channel owner** connects the channel once (step 2).
- **Volunteers** who can review isiZulu and Sesotho (step 4).
