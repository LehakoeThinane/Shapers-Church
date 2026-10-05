import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';

import { API_URL, api, unwrap, type Schemas } from './api';

export type ChatRoom = Schemas['ChatRoomDto'];
export type ChatMessage = Schemas['ChatMessageDto'];
export type ChatRules = Schemas['ChatRulesDto'];

const MAX_MESSAGES = 200;
const NOTICE_KEY = 'shapers.chatNoticeSeen';

const roomKey = (livestreamId: string) => ['live-chat', livestreamId] as const;

function withMessage(room: ChatRoom | undefined, message: ChatMessage): ChatRoom | undefined {
  if (!room || room.messages.some((m) => m.id === message.id)) return room;
  return { ...room, messages: [...room.messages, message].slice(-MAX_MESSAGES) };
}

/**
 * The chat beside a livestream. New messages arrive over a live connection; if that can't be made (some
 * networks block it), the room is fetched every few seconds instead.
 */
export function useLiveChat(livestreamId: string) {
  const client = useQueryClient();
  const [connected, setConnected] = useState(false);

  const room = useQuery({
    queryKey: roomKey(livestreamId),
    queryFn: async () => unwrap(await api.GET('/api/media/live/{livestreamId}/chat', { params: { path: { livestreamId } } })),
    refetchInterval: connected ? false : 5_000,
  });

  useEffect(() => {
    let stopped = false;
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/live-chat`, { withCredentials: false })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.None)
      .build();

    const update = (change: (room: ChatRoom | undefined) => ChatRoom | undefined) =>
      client.setQueryData<ChatRoom>(roomKey(livestreamId), change);

    connection.on('message', (message: ChatMessage) => update((r) => withMessage(r, message)));
    connection.on('removed', (id: string) => update((r) => (r ? { ...r, messages: r.messages.filter((m) => m.id !== id) } : r)));
    connection.on('rules', (rules: ChatRules) => update((r) => (r ? { ...r, rules } : r)));
    connection.onreconnecting(() => setConnected(false));
    connection.onreconnected(async () => {
      await connection.invoke('Join', livestreamId).catch(() => undefined);
      setConnected(true);
      void client.invalidateQueries({ queryKey: roomKey(livestreamId) });
    });
    connection.onclose(() => setConnected(false));

    (async () => {
      try {
        await connection.start();
        if (stopped) return;
        await connection.invoke('Join', livestreamId);
        setConnected(true);
        // Anything said while we were connecting.
        void client.invalidateQueries({ queryKey: roomKey(livestreamId) });
      } catch {
        setConnected(false);
      }
    })();

    return () => {
      stopped = true;
      if (connection.state !== HubConnectionState.Disconnected) void connection.stop();
    };
  }, [client, livestreamId]);

  const send = useMutation({
    mutationFn: async (text: string) =>
      unwrap(await api.POST('/api/media/live/{livestreamId}/chat', { params: { path: { livestreamId } }, body: { text } })),
    onSuccess: (message) => {
      client.setQueryData<ChatRoom>(roomKey(livestreamId), (r) => {
        if (!r) return r;
        // The broadcast copy may have arrived first; ours knows it's "mine".
        const others = r.messages.filter((m) => m.id !== message.id);
        return { ...r, messages: [...others, message].slice(-MAX_MESSAGES) };
      });
    },
  });

  const report = useMutation({
    mutationFn: async (messageId: string) =>
      unwrap(await api.POST('/api/media/live/{livestreamId}/chat/{messageId}/report', { params: { path: { livestreamId, messageId } } })),
  });

  return { room, connected, send, report };
}

/** Whether this phone has already shown the "the chat is public" note. */
export function useChatNotice() {
  const [seen, setSeen] = useState<boolean | null>(null);
  useEffect(() => {
    AsyncStorage.getItem(NOTICE_KEY)
      .then((v) => setSeen(v === '1'))
      .catch(() => setSeen(false));
  }, []);
  const accept = () => {
    setSeen(true);
    void AsyncStorage.setItem(NOTICE_KEY, '1').catch(() => undefined);
  };
  return { seen, accept };
}

export function blockedMessage(blocked: Schemas['ChatBlock'] | null | undefined): string | null {
  switch (blocked) {
    case 'SignIn':
      return 'Sign in to join the chat.';
    case 'Closed':
      return 'The chat opens 15 minutes before a service.';
    case 'Under18':
      return "The live chat is for adults for now. You're welcome to read along.";
    case 'TimedOut':
      return 'A moderator has paused your messages for the rest of this service.';
    case 'Banned':
      return 'A moderator has stopped you from posting in the chat. Contact the church office if you think this is a mistake.';
    default:
      return null;
  }
}
