import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import type { Schemas } from '@shapers/api-client';
import { apiUrl, getJson } from './live';

type Room = Schemas['ChatRoomDto'];
type Message = Schemas['ChatMessageDto'];

const MAX_SHOWN = 100;

/**
 * Shows a livestream's chat, read-only (posting needs the app's sign-in). New messages arrive over a live
 * connection; if that can't be made, the room is fetched every ten seconds instead. Text is only ever set
 * with textContent, never as HTML. Returns a function that stops following.
 */
export function followChat(livestreamId: string, list: HTMLElement, onOpen: (open: boolean) => void): () => void {
  let stopped = false;
  let poll: ReturnType<typeof setInterval> | null = null;

  const add = (m: Message) => {
    if (m.pending || list.querySelector(`[data-id="${CSS.escape(m.id)}"]`)) return;
    const item = document.createElement('li');
    item.dataset.id = m.id;
    const author = document.createElement('strong');
    author.textContent = m.author;
    item.append(author);
    if (m.fromTeam) {
      const badge = document.createElement('span');
      badge.className = 'chat-badge';
      badge.textContent = 'Team';
      item.append(' ', badge);
    }
    const body = document.createElement('p');
    body.textContent = m.text;
    item.append(body);
    list.append(item);
    while (list.children.length > MAX_SHOWN) list.firstElementChild?.remove();
    list.scrollTop = list.scrollHeight;
  };

  const load = async () => {
    try {
      const room = await getJson<Room>(`/api/media/live/${livestreamId}/chat`);
      if (stopped) return;
      onOpen(room.rules.open);
      list.replaceChildren();
      room.messages.forEach(add);
    } catch {
      // Keep what's shown; the next refresh tries again.
    }
  };

  const connection = new HubConnectionBuilder()
    .withUrl(`${apiUrl}/hubs/live-chat`, { withCredentials: false })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.None)
    .build();
  connection.on('message', add);
  connection.on('removed', (id: string) => list.querySelector(`[data-id="${CSS.escape(id)}"]`)?.remove());
  connection.on('rules', (rules: Schemas['ChatRulesDto']) => onOpen(rules.open));
  connection.onreconnected(async () => {
    await connection.invoke('Join', livestreamId).catch(() => undefined);
    await load();
  });

  const startPolling = () => {
    poll ??= setInterval(() => {
      if (document.visibilityState === 'visible') void load();
    }, 10_000);
  };

  void (async () => {
    await load();
    try {
      await connection.start();
      if (stopped) return;
      await connection.invoke('Join', livestreamId);
      await load();
    } catch {
      startPolling();
    }
  })();
  connection.onclose(() => {
    if (!stopped) startPolling();
  });

  return () => {
    stopped = true;
    if (poll) clearInterval(poll);
    void connection.stop();
  };
}
