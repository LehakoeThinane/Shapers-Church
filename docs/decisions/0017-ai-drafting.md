# 0017. AI drafting help for staff

Status: Accepted (2026-10-06)

## Context
Staff spend hours each week turning Sunday's sermon into show notes and a lesson for the home cells, and tidying up announcements. AI can do a first draft of this work. The project brief limits AI to operational help: a person approves everything before it is published, there is no chatbot answering questions about faith, and individuals are not scored. Much of what the platform holds is special personal information under POPIA (faith, health), and it must not reach an AI service.

## Decision
- **New `Assist` module** (schema `assist`). It owns the AI providers, the drafts, the usage log and the monthly budget. Other modules never call an AI service themselves:
  - Media transcribes sermon audio through the `IAssistTranscriber` contract.
  - Assist reads sermons through Media's `ISermonSource` contract, which exposes a sermon's own words and nothing about listeners.
- **Drafts only.** AI output is stored as a *draft*, labelled as AI-written. A person copies it into the real editor, changes what they want and saves it there, then marks the draft accepted or discards it. Nothing is published from a draft. Drafts are deleted after 90 days.
- **What AI may see:**
  - sermon transcripts, titles, speakers and passages;
  - text staff ask to have rewritten.

  Cell reports, prayer requests, pastoral notes, chat and people's records are never sent. As a safety net:
  - rewrites that contain an email address, phone number or ID number are refused;
  - such details are removed from transcripts before sending.
- **Providers**, chosen in settings with `Assist:Provider`:
  - `Disabled` is the default.
  - `Azure` uses Azure OpenAI (chat completions with structured JSON output) and Azure AI Speech fast transcription, over REST with the API's managed identity. Keys are disabled on the accounts.
  - `Fake` gives canned answers for development and tests.
- **Where data is processed:** both Azure accounts are in South Africa North.
  - Speech, and embeddings when search is added, run in the region.
  - The writing models are offered there only as *Global Standard*, so a prompt may be processed outside South Africa. This is acceptable because only public church content is sent. POPIA's cross-border rules (section 72) concern personal information.
- **Budget:** every call is logged with its tokens or audio minutes and an estimated cost in rand, never with its content. Once the month's estimate (Johannesburg time) reaches `Assist:MonthlyBudgetZar` (default R300), calls are refused until the 1st. The usage log is kept for 13 months.
- **Prompts** are versioned files in the repository (`Prompts/*.v1.md`). Each draft records the prompt version and model, so a change in output can be traced.
- **Permissions:**
  - `assist.drafts.create`: Campus pastor, Campus administrator, Media team and Content team. It is checked at the scope of the source, such as the sermon's campus.
  - `assist.usage.view`: Church administrator.
- **Church lessons:** cell materials gain a church-wide form (`cell_id` null, scoped to the church or a campus, optionally linked to a sermon). Pastors publish lessons there, often from an AI draft. Leaders see them in My cell and can choose one when recording a meeting.
- **Transcripts** live on the sermon. They come from uploaded audio (queued automatically when AI help is on), YouTube captions (planned) or pasting. They are staff-only.

## Consequences
- AI help can be switched off entirely without code changes, and the rest of the platform does not depend on it.
- The church needs Azure OpenAI and Speech resources (`deploy.ps1 -EnableAi`). The demo keeps AI switched off unless a resource is created for it.
- Costs are estimates from configured prices; the Azure invoice is the real figure. Prices are settings so they can be updated.
- Translation (isiZulu, Sesotho) and search by meaning build on this module next. Translations will need a reviewer role, and only checked translations will be publishable.
