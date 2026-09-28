# 0006. Admin portal as a React + Vite SPA

Status: Accepted (2026-09-28)

## Context
The admin portal sits behind a login, so it gains nothing from server rendering or search indexing. The team is four people and wants fewer moving parts.

## Decision
A React + Vite + TypeScript single-page app, served as static files, with React Query for server state. In development the dev server proxies `/api` to the backend, so the session cookie is same-origin. The public website is a separate decision for V1.

## Consequences
- There is no Node server to host for the admin portal.
- If the public website later uses a server-rendered framework, it will be a separate app.
