# 0014. Moderated live chat during services

Status: Accepted (2026-09-30)

## Context
The Live tab shows the service, notes and scripture. Members asked to talk during the service, and the design includes a chat. A chat is a public space where adults and possibly teenagers write in real time. The brief requires safeguarding from day one: no private adult–minor messaging, reporting and moderation, and message retention. POPIA applies to what people write.

## Decision
- **One public room per livestream. There are no private messages.**
- **Who can post:**
  - Everyone can read.
  - Posting needs a signed-in member. Names show as first name and last initial.
  - Members under 18, by date of birth or as a child in a household, can read but not post in V1.
  - Moderators post with a Team badge.
- **When:** chat opens 15 minutes before the scheduled start and closes 30 minutes after the service ends. Members don't see a closed chat.
- **Moderation** (`media.chat.moderate`: the Chat moderator role, campus pastors and the media team):
  - Moderators can hide and restore messages, time someone out for the service, ban them from every service until lifted, and switch on "approve every message".
  - Slow mode defaults to one message every 5 seconds and can go up to 300.
  - Messages that match a church-wide word list wait for a moderator.
  - Every action is audited.
- **Reports:**
  - Any member can report a message once.
  - The first report alerts the moderators for that campus through the inbox and push. The alert carries no message text.
  - Three reports from different people hold the message until a moderator decides. After a moderator restores a message, reports don't hide it again.
- **Real time:**
  - Messages are saved first, then broadcast through a SignalR hub, which is receive-only.
  - Posting, reporting and moderation go through ordinary HTTP endpoints, so sign-in, permissions and limits all apply in one place.
  - Clients fall back to fetching the room every few seconds if a live connection can't be made.
  - Broadcasts carry no person IDs.
- **Retention:**
  - Ordinary messages are deleted after 90 days.
  - Hidden, held, reported or moderated messages, and timeouts and bans, are kept for a year, as evidence if there's a safeguarding concern.
  - Erasure deletes a person's messages, removes their reports and removes any timeouts or bans on them. Data export includes their messages.
- **Text:** messages are limited to 300 characters. Control and text-direction characters are removed, links are shown as plain text, and every client renders text only, never HTML.

## Consequences
- One API instance holds every live connection. Running more than one needs Azure SignalR Service (`AddAzureSignalR`); the application code doesn't change.
- The website's content security policy allows the API's WebSocket address as well as its HTTPS address.
- Youth-safe group chat, with leaders visible in the room, arrives with Groups in V2.
