# 0022. Documents: shared files for staff and leaders, private by default

Status: Proposed (2026-10-22)

## Context
Staff and leaders share run sheets, rotas, policies, budgets and minutes by email and WhatsApp, so there's no single current copy and no control over who has what. Many of these files hold people's personal details (POPIA). The church doesn't pay for Microsoft 365.

## Decision
- **Documents module** (schema `documents`) in the admin portal: **libraries** (top-level shared folders), **folders**, and **documents** with **versions**. Uploading a file with the same name in the same folder adds a version; up to 25 are kept, and any one can be made current again.
- **Access per library.** A library belongs to the church, a campus or a ministry. `documents.view` opens it and `documents.edit` changes its files, at that scope. `documents.manage` creates libraries and can open all of them. A **private** library is only for the people listed, each with view or edit (and they must hold `documents.view` somewhere, i.e. be staff).
- **Files are private.** They're kept in their own storage container with no public access (`Media:Storage:PrivateContainer`, created on first use) and are only ever **streamed through the API**, which checks access on every request; there are no shareable links. Only safe types (PDF, images, plain text, audio, video) open in the browser, under a sandbox content-security policy; everything else downloads, so an uploaded web page can't run on the API's site.
- **Every opening and change is logged** in the audit log.
- **Recycle bin.** Deleting moves a file or folder to the bin; after 30 days a daily job removes it and its stored files for good.
- **Erasure** takes a person's name off the versions they uploaded; the church keeps the files.
- Up to **100 MB** per file.

## Consequences
- No new Azure resource: a container in the existing storage account. Storage costs cents a month.
- Not yet: editing Word documents in the browser. The plan is our own editor from open-source parts (TipTap/ProseMirror for editing, mammoth.js to read .docx, the docx library to write it back, later Yjs for co-editing), saving each edit as a new version.
- Not yet: sharing with members in the app, moving files between libraries.
