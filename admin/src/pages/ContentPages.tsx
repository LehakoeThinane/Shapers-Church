import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import Markdown from 'react-markdown';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { Badge, Button, Card, Empty, ErrorNote, Field, Loading, PageHeader, Select, TextInput } from '../components/ui';
import { api, formatDate, formatDateTime, unwrap, type Schemas } from '../lib/api';
import { can, Permissions, useAccess } from '../lib/access';
import { RewriteHelp } from '../components/Assist';
import { useAssistStatus, useCanDraft, useReviewDraft } from '../lib/assist';

type Status = Schemas['ContentStatus'];
type Kind = Schemas['PostKind'];
type PageAdmin = Schemas['PageAdminDto'];
type PostAdmin = Schemas['PostAdminDto'];

const statusTone = { Draft: 'neutral', Scheduled: 'accent', Published: 'success', Archived: 'neutral' } as const;
const tabs = [
  { key: 'pages', label: 'Pages' },
  { key: 'News', label: 'News' },
  { key: 'Blog', label: 'Blog' },
] as const;

const toLocalInput = (iso: string | null | undefined) => {
  if (!iso) return '';
  const d = new Date(iso);
  return new Date(d.getTime() - d.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
};

/** Website and app content: standing pages, church news and blog articles. */
export function ContentPage() {
  const { data: access } = useAccess();
  const [params, setParams] = useSearchParams();
  const tab = (params.get('tab') ?? 'pages') as (typeof tabs)[number]['key'];

  return (
    <>
      <PageHeader
        title="Content"
        subtitle="Pages and posts for the website and the app. Write in Markdown; nothing is public until it's published."
        actions={
          can(access, Permissions.contentEdit) && (
            <Link className="btn btn-primary" to={tab === 'pages' ? '/content/pages/new' : `/content/posts/new?kind=${tab}`}>
              {tab === 'pages' ? 'New page' : tab === 'News' ? 'New news item' : 'New article'}
            </Link>
          )
        }
      />
      <div className="row">
        {tabs.map((t) => (
          <Button key={t.key} variant={tab === t.key ? 'primary' : 'ghost'} onClick={() => setParams({ tab: t.key })}>
            {t.label}
          </Button>
        ))}
      </div>
      {tab === 'pages' && can(access, Permissions.contentPublish) && <ImportCard />}
      {tab === 'pages' ? <PagesList /> : <PostsList kind={tab} />}
    </>
  );
}

/** One-off: bring the old WordPress site's articles and pages across. Safe to run again. */
function ImportCard() {
  const queryClient = useQueryClient();
  const run = useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/admin/content/import-wordpress')),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['content'] }),
  });
  const result = run.data;
  return (
    <Card title="Bring content across from the old website">
      <div className="stack">
        <p className="small muted">
          Copies the blog articles (published, with their original dates) and the pages (as drafts, so you can check details like the address
          before publishing). Old addresses redirect to the new ones. Running it again only adds anything new.
        </p>
        <ErrorNote error={run.error} />
        {result && (
          <p className="small">
            Imported {result.postsImported} articles and {result.pagesImported} pages
            {result.alreadyImported > 0 && `; ${result.alreadyImported} were already here`}. Review the new drafts below.
          </p>
        )}
        <div>
          <Button busy={run.isPending} onClick={() => run.mutate()}>
            Import from shaperschurch.com
          </Button>
        </div>
      </div>
    </Card>
  );
}

function PagesList() {
  const pages = useQuery({ queryKey: ['content', 'pages'], queryFn: async () => unwrap(await api.GET('/api/admin/content/pages')) });
  return (
    <Card>
      <ErrorNote error={pages.error} />
      {pages.isPending ? (
        <Loading />
      ) : pages.data?.length === 0 ? (
        <Empty>No pages yet.</Empty>
      ) : (
        <table className="table">
          <thead>
            <tr>
              <th>Page</th>
              <th>Address</th>
              <th>In menu</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {pages.data?.map((p) => (
              <tr key={p.page.id}>
                <td>
                  <Link to={`/content/pages/${p.page.id}`}>{p.page.title}</Link>
                </td>
                <td className="small muted">/{p.page.slug}</td>
                <td>{p.page.menuOrder ?? '—'}</td>
                <td>
                  <Badge tone={statusTone[p.status]}>{p.status}</Badge>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Card>
  );
}

function PostsList({ kind }: { kind: Kind }) {
  const posts = useQuery({
    queryKey: ['content', 'posts', kind],
    queryFn: async () => unwrap(await api.GET('/api/admin/content/posts', { params: { query: { kind } } })),
  });
  return (
    <Card>
      <ErrorNote error={posts.error} />
      {posts.isPending ? (
        <Loading />
      ) : posts.data?.length === 0 ? (
        <Empty>{kind === 'News' ? 'No news yet.' : 'No articles yet.'}</Empty>
      ) : (
        <table className="table">
          <thead>
            <tr>
              <th>Title</th>
              <th>Published</th>
              {kind === 'News' && <th>Shown until</th>}
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {posts.data?.map((p) => (
              <tr key={p.id}>
                <td>
                  <Link to={`/content/posts/${p.id}`}>{p.title}</Link>
                </td>
                <td>{p.publishedAt ? formatDate(p.publishedAt) : p.publishAt ? `From ${formatDateTime(p.publishAt)}` : '—'}</td>
                {kind === 'News' && <td>{p.showUntil ? formatDate(p.showUntil) : 'No end'}</td>}
                <td>
                  <Badge tone={statusTone[p.status]}>{p.status}</Badge>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Card>
  );
}

export function PageEditorPage() {
  const { id } = useParams();
  const page = useQuery({
    queryKey: ['content', 'page', id],
    enabled: !!id,
    queryFn: async () => unwrap(await api.GET('/api/admin/content/pages/{id}', { params: { path: { id: id! } } })),
  });
  if (!id) return <PageEditor key="new" />;
  if (page.isPending) return <Loading />;
  if (page.error || !page.data) return <ErrorNote error={page.error ?? new Error('Not found')} />;
  return <PageEditor key={`${page.data.page.id}-${page.data.page.updatedAt}-${page.data.status}`} existing={page.data} />;
}

function PageEditor({ existing }: { existing?: PageAdmin }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { data: access } = useAccess();
  const translation = existing?.translation ?? null;
  const original = useOriginal('pages', translation?.originalId);
  const writable = canWrite(access, !!translation);
  const [form, setForm] = useState({
    title: existing?.page.title ?? '',
    slug: existing?.page.slug ?? '',
    summary: existing?.page.summary ?? '',
    body: existing?.page.body ?? '',
    menuOrder: existing?.page.menuOrder?.toString() ?? '',
  });
  const set = (patch: Partial<typeof form>) => setForm((f) => ({ ...f, ...patch }));
  const save = useMutation({
    mutationFn: async () => {
      const body = {
        title: form.title,
        slug: form.slug || null,
        summary: form.summary || null,
        body: form.body,
        menuOrder: form.menuOrder ? Number(form.menuOrder) : null,
      };
      return existing
        ? unwrap(await api.PUT('/api/admin/content/pages/{id}', { params: { path: { id: existing.page.id } }, body }))
        : unwrap(await api.POST('/api/admin/content/pages', { body }));
    },
    onSuccess: (p) => {
      queryClient.setQueryData(['content', 'page', p.page.id], p);
      void queryClient.invalidateQueries({ queryKey: ['content', 'pages'] });
      if (!existing) navigate(`/content/pages/${p.page.id}`, { replace: true });
    },
  });

  return (
    <>
      <PageHeader
        title={existing ? existing.page.title : 'New page'}
        subtitle={<Link to="/content?tab=pages">All content</Link>}
        actions={existing && <Badge tone={statusTone[existing.status]}>{existing.status}</Badge>}
      />
      <div className="grid-2">
        <Card title="Page">
          <form
            className="stack"
            onSubmit={(e: FormEvent) => {
              e.preventDefault();
              save.mutate();
            }}>
            <fieldset className="stack" disabled={!writable}>
              <Field label="Title">
                <TextInput required maxLength={150} value={form.title} onChange={(e) => set({ title: e.target.value })} />
              </Field>
              {!translation && (
                <Field label="Address" hint="Leave empty to make one from the title, e.g. about-us.">
                  <TextInput maxLength={80} pattern="[a-z0-9]+(-[a-z0-9]+)*" value={form.slug} onChange={(e) => set({ slug: e.target.value })} />
                </Field>
              )}
              <Field label="Summary" hint="One or two sentences for search engines and link previews.">
                <textarea className="input" rows={2} maxLength={300} value={form.summary} onChange={(e) => set({ summary: e.target.value })} />
              </Field>
              <Field label="Content" hint="Markdown: ## for headings, - for lists, **bold**, [link](https://…).">
                <textarea className="input" rows={14} required maxLength={50000} value={form.body} onChange={(e) => set({ body: e.target.value })} />
              </Field>
              {!translation && <RewriteHelp text={form.body} onApply={(body) => set({ body })} />}
              <Field label="Position in the website menu" hint="Empty keeps it out of the menu.">
                <TextInput type="number" min={1} max={20} value={form.menuOrder} onChange={(e) => set({ menuOrder: e.target.value })} />
              </Field>
            </fieldset>
            <ErrorNote error={save.error} />
            {writable && (
              <Button variant="primary" type="submit" busy={save.isPending}>
                {existing ? 'Save' : 'Create draft'}
              </Button>
            )}
          </form>
        </Card>
        <div className="stack">
          {existing && translation && <TranslationCheckCard kind="pages" id={existing.page.id} info={translation} original={original.data} />}
          {existing && (
            <PublishCard
              kind="pages"
              id={existing.page.id}
              status={existing.status}
              publishAt={existing.publishAt}
              legacyPath={existing.legacyPath}
              blocked={!!translation && !translation.checked}
            />
          )}
          {existing && !translation && <TranslationsCard kind="pages" id={existing.page.id} translations={existing.translations} />}
          <Preview title={form.title} body={form.body} />
        </div>
      </div>
    </>
  );
}

export function PostEditorPage() {
  const { id } = useParams();
  const post = useQuery({
    queryKey: ['content', 'post', id],
    enabled: !!id,
    queryFn: async () => unwrap(await api.GET('/api/admin/content/posts/{id}', { params: { path: { id: id! } } })),
  });
  if (!id) return <PostEditor key="new" />;
  if (post.isPending) return <Loading />;
  if (post.error || !post.data) return <ErrorNote error={post.error ?? new Error('Not found')} />;
  return <PostEditor key={`${post.data.id}-${post.data.updatedAt}-${post.data.status}`} existing={post.data} />;
}

function PostEditor({ existing }: { existing?: PostAdmin }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { data: access } = useAccess();
  const [params] = useSearchParams();
  const translation = existing?.translation ?? null;
  const original = useOriginal('posts', translation?.originalId);
  const writable = canWrite(access, !!translation);
  const [form, setForm] = useState({
    kind: (existing?.kind ?? (params.get('kind') === 'News' ? 'News' : 'Blog')) as Kind,
    title: existing?.title ?? '',
    slug: existing?.slug ?? '',
    summary: existing?.summary ?? '',
    body: existing?.body ?? '',
    author: existing?.author ?? '',
    coverImageUrl: existing?.coverImageUrl ?? '',
    showUntil: existing?.showUntil ?? '',
  });
  const set = (patch: Partial<typeof form>) => setForm((f) => ({ ...f, ...patch }));
  const save = useMutation({
    mutationFn: async () => {
      const body = {
        kind: form.kind,
        title: form.title,
        slug: form.slug || null,
        summary: form.summary || null,
        body: form.body,
        author: form.author || null,
        coverImageUrl: form.coverImageUrl || null,
        showUntil: form.kind === 'News' && form.showUntil ? form.showUntil : null,
      };
      return existing
        ? unwrap(await api.PUT('/api/admin/content/posts/{id}', { params: { path: { id: existing.id } }, body }))
        : unwrap(await api.POST('/api/admin/content/posts', { body }));
    },
    onSuccess: (p) => {
      queryClient.setQueryData(['content', 'post', p.id], p);
      void queryClient.invalidateQueries({ queryKey: ['content', 'posts'] });
      if (!existing) navigate(`/content/posts/${p.id}`, { replace: true });
    },
  });

  return (
    <>
      <PageHeader
        title={existing ? existing.title : form.kind === 'News' ? 'New news item' : 'New article'}
        subtitle={<Link to={`/content?tab=${form.kind}`}>All content</Link>}
        actions={existing && <Badge tone={statusTone[existing.status]}>{existing.status}</Badge>}
      />
      <div className="grid-2">
        <Card title={form.kind === 'News' ? 'News' : 'Article'}>
          <form
            className="stack"
            onSubmit={(e: FormEvent) => {
              e.preventDefault();
              save.mutate();
            }}>
            <fieldset className="stack" disabled={!writable}>
              <Field label="Type">
                <Select value={form.kind} onChange={(e) => set({ kind: e.target.value as Kind })}>
                  <option value="News">News: short, shown on Home and the front page</option>
                  <option value="Blog">Blog article</option>
                </Select>
              </Field>
              <Field label="Title">
                <TextInput required maxLength={150} value={form.title} onChange={(e) => set({ title: e.target.value })} />
              </Field>
              {!translation && (
                <Field label="Address" hint="Leave empty to make one from the title.">
                  <TextInput maxLength={80} pattern="[a-z0-9]+(-[a-z0-9]+)*" value={form.slug} onChange={(e) => set({ slug: e.target.value })} />
                </Field>
              )}
              <Field label="Summary" hint="Shown in lists and link previews.">
                <textarea className="input" rows={2} maxLength={300} value={form.summary} onChange={(e) => set({ summary: e.target.value })} />
              </Field>
              <Field label="Content" hint="Markdown.">
                <textarea className="input" rows={14} required maxLength={50000} value={form.body} onChange={(e) => set({ body: e.target.value })} />
              </Field>
              {!translation && <RewriteHelp text={form.body} onApply={(body) => set({ body })} />}
              <div className="form-grid">
                <Field label="Author (optional)">
                  <TextInput maxLength={100} value={form.author} onChange={(e) => set({ author: e.target.value })} />
                </Field>
                {form.kind === 'News' && (
                  <Field label="Show until" hint="Drops off Home after this day.">
                    <TextInput type="date" value={form.showUntil} onChange={(e) => set({ showUntil: e.target.value })} />
                  </Field>
                )}
              </div>
              <Field label="Cover image link (optional)" hint="A wide image, starting with https://.">
                <TextInput type="url" maxLength={500} value={form.coverImageUrl} onChange={(e) => set({ coverImageUrl: e.target.value })} />
              </Field>
            </fieldset>
            <ErrorNote error={save.error} />
            {writable && (
              <Button variant="primary" type="submit" busy={save.isPending}>
                {existing ? 'Save' : 'Create draft'}
              </Button>
            )}
          </form>
        </Card>
        <div className="stack">
          {existing && translation && <TranslationCheckCard kind="posts" id={existing.id} info={translation} original={original.data} />}
          {existing && (
            <PublishCard
              kind="posts"
              id={existing.id}
              status={existing.status}
              publishAt={existing.publishAt}
              legacyPath={existing.legacyPath}
              blocked={!!translation && !translation.checked}
            />
          )}
          {existing && !translation && <TranslationsCard kind="posts" id={existing.id} translations={existing.translations} />}
          <Preview title={form.title} body={form.body} />
        </div>
      </div>
    </>
  );
}

function PublishCard({
  kind,
  id,
  status,
  publishAt,
  legacyPath,
  blocked = false,
}: {
  kind: 'pages' | 'posts';
  id: string;
  status: Status;
  publishAt: string | null;
  legacyPath: string | null;
  /** An unchecked translation: it can't go live yet. */
  blocked?: boolean;
}) {
  const queryClient = useQueryClient();
  const { data: access } = useAccess();
  const [when, setWhen] = useState(toLocalInput(publishAt));
  const change = useMutation({
    mutationFn: async (action: 'publish' | 'schedule' | 'unpublish' | 'archive') => {
      const path = { params: { path: { id } } };
      if (kind === 'pages') {
        if (action === 'publish') return unwrap(await api.POST('/api/admin/content/pages/{id}/publish', path));
        if (action === 'schedule') return unwrap(await api.POST('/api/admin/content/pages/{id}/schedule', { ...path, body: { publishAt: new Date(when).toISOString() } }));
        if (action === 'unpublish') return unwrap(await api.POST('/api/admin/content/pages/{id}/unpublish', path));
        return unwrap(await api.POST('/api/admin/content/pages/{id}/archive', path));
      }
      if (action === 'publish') return unwrap(await api.POST('/api/admin/content/posts/{id}/publish', path));
      if (action === 'schedule') return unwrap(await api.POST('/api/admin/content/posts/{id}/schedule', { ...path, body: { publishAt: new Date(when).toISOString() } }));
      if (action === 'unpublish') return unwrap(await api.POST('/api/admin/content/posts/{id}/unpublish', path));
      return unwrap(await api.POST('/api/admin/content/posts/{id}/archive', path));
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['content'] }),
  });

  if (!can(access, Permissions.contentPublish)) {
    return (
      <Card title="Publishing">
        <p className="small muted">Someone with publishing rights will put this live.</p>
      </Card>
    );
  }

  return (
    <Card title="Publishing">
      <div className="stack">
        {status === 'Scheduled' && <p className="small">Goes live {formatDateTime(publishAt)}.</p>}
        {legacyPath && <p className="small muted">Imported from the old website ({legacyPath}); that address redirects here.</p>}
        <ErrorNote error={change.error} />
        {blocked && <p className="small muted">Publishing opens once a translator has checked it.</p>}
        {status !== 'Published' && !blocked && (
          <Button variant="primary" busy={change.isPending} onClick={() => change.mutate('publish')}>
            Publish now
          </Button>
        )}
        {status !== 'Published' && !blocked && (
          <div className="row">
            <TextInput type="datetime-local" value={when} onChange={(e) => setWhen(e.target.value)} aria-label="Publish at" />
            <Button busy={change.isPending} disabled={!when} onClick={() => change.mutate('schedule')}>
              Schedule
            </Button>
          </div>
        )}
        {(status === 'Published' || status === 'Scheduled') && (
          <Button busy={change.isPending} onClick={() => change.mutate('unpublish')}>
            Back to draft
          </Button>
        )}
        {status !== 'Archived' && (
          <Button variant="ghost" busy={change.isPending} onClick={() => confirm('Archive this? It comes off the website and app.') && change.mutate('archive')}>
            Archive
          </Button>
        )}
      </div>
    </Card>
  );
}

type Kinds = 'pages' | 'posts';

/** Translations need checking by a speaker before they can be published; editing one makes it need checking again. */
function canWrite(access: Parameters<typeof can>[0], translation: boolean) {
  return can(access, Permissions.contentEdit) || (translation && can(access, Permissions.contentTranslationsReview));
}

/** On an English original: its translations, and AI drafts of new ones. */
function TranslationsCard({ kind, id, translations }: { kind: Kinds; id: string; translations: Schemas['TranslationSummaryDto'][] }) {
  const navigate = useNavigate();
  const { data: access } = useAccess();
  const { data: status } = useAssistStatus();
  const canDraft = useCanDraft();
  const review = useReviewDraft();
  const draft = useMutation({
    mutationFn: async (language: string) => {
      const d = unwrap(await api.POST('/api/admin/assist/drafts/translate', { body: { sourceType: kind === 'pages' ? 'Page' : 'Post', sourceId: id, language } }));
      const t = d.translation!;
      const body = { language, title: t.title, summary: t.summary || null, body: t.body };
      const created =
        kind === 'pages'
          ? (await api.POST('/api/admin/content/pages/{id}/translations', { params: { path: { id } }, body }).then(unwrap)).page.id
          : (await api.POST('/api/admin/content/posts/{id}/translations', { params: { path: { id } }, body }).then(unwrap)).id;
      review.mutate({ id: d.id, action: 'accept' });
      return created;
    },
    onSuccess: (created) => navigate(`/content/${kind}/${created}`),
  });

  const missing = (status?.languages ?? []).filter((l) => !translations.some((t) => t.language === l.code));
  if (translations.length === 0 && (!canDraft || missing.length === 0)) return null;
  return (
    <Card title="Translations">
      <div className="stack">
        {translations.length > 0 && (
          <ul className="list">
            {translations.map((t) => (
              <li key={t.id} className="list-row">
                <Link to={`/content/${kind}/${t.id}`}>{t.languageName}</Link>
                <span>
                  {t.checked ? <Badge tone="success">Checked</Badge> : <Badge tone="accent">Needs checking</Badge>}
                  {t.originalChangedSince && <Badge tone="danger">English changed since</Badge>} <Badge tone={statusTone[t.status]}>{t.status}</Badge>
                </span>
              </li>
            ))}
          </ul>
        )}
        {canDraft && can(access, Permissions.contentEdit) && missing.length > 0 && (
          <div className="stack-tight">
            <div className="row">
              {missing.map((l) => (
                <Button key={l.code} busy={draft.isPending && draft.variables === l.code} disabled={draft.isPending} onClick={() => draft.mutate(l.code)}>
                  Translate into {l.name}
                </Button>
              ))}
            </div>
            {draft.isPending && <p className="small muted">Translating… this can take a minute for a long text.</p>}
            <p className="small muted">AI writes a first draft. A translator who speaks the language checks it before it can be published.</p>
          </div>
        )}
        <ErrorNote error={draft.error} />
      </div>
    </Card>
  );
}

/** On a translation: what it translates, whether it's been checked, and the original to compare against. */
function TranslationCheckCard({ kind, id, info, original }: { kind: Kinds; id: string; info: Schemas['TranslationInfoDto']; original: { title: string; body: string } | undefined }) {
  const queryClient = useQueryClient();
  const { data: access } = useAccess();
  const [showOriginal, setShowOriginal] = useState(false);
  const check = useMutation({
    mutationFn: async () =>
      kind === 'pages'
        ? unwrap(await api.POST('/api/admin/content/pages/{id}/check', { params: { path: { id } } }))
        : unwrap(await api.POST('/api/admin/content/posts/{id}/check', { params: { path: { id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['content'] }),
  });

  return (
    <Card title={`${info.languageName} translation`}>
      <div className="stack">
        <p className="small">
          Of <Link to={`/content/${kind}/${info.originalId}`}>{info.originalTitle}</Link>. Its address is the same as the English one.
        </p>
        {info.checked ? (
          <p className="note small">Checked {formatDateTime(info.checkedAt)}. Saving changes makes it need checking again.</p>
        ) : (
          <p className="note note-accent small">Needs checking by someone who speaks {info.languageName}. It can't be published until then.</p>
        )}
        {info.originalChangedSince && <p className="note note-danger small">The English has changed since this was translated or checked. Compare them and update this.</p>}
        {!info.checked && can(access, Permissions.contentTranslationsReview) && (
          <Button variant="primary" busy={check.isPending} onClick={() => check.mutate()}>
            I've checked it: it says what the English says
          </Button>
        )}
        <ErrorNote error={check.error} />
        {original && (
          <>
            <button type="button" className="link-button" onClick={() => setShowOriginal(!showOriginal)}>
              {showOriginal ? 'Hide the English' : 'Show the English to compare'}
            </button>
            {showOriginal && (
              <div className="markdown">
                <h2>{original.title}</h2>
                <Markdown>{original.body}</Markdown>
              </div>
            )}
          </>
        )}
      </div>
    </Card>
  );
}

/** Translators: every translation, unchecked first. */
export function TranslationsPage() {
  const queue = useQuery({ queryKey: ['content', 'translations'], queryFn: async () => unwrap(await api.GET('/api/admin/content/translations')) });
  return (
    <>
      <PageHeader
        title="Translations"
        subtitle="Pages and posts in other languages. A speaker of the language checks each one before it's published, and again whenever it changes."
      />
      <Card>
        <ErrorNote error={queue.error} />
        {queue.isPending ? (
          <Loading />
        ) : queue.data?.length === 0 ? (
          <Empty>No translations yet. Open a page or post and choose “Translate into …”.</Empty>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Translation</th>
                <th>Of</th>
                <th>Language</th>
                <th>Checked</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {queue.data?.map((t) => (
                <tr key={t.id}>
                  <td>
                    <Link to={`/content/${t.type === 'Page' ? 'pages' : 'posts'}/${t.id}`}>{t.title}</Link>
                  </td>
                  <td className="small">{t.originalTitle}</td>
                  <td>{t.languageName}</td>
                  <td>
                    {t.checked ? <Badge tone="success">Checked</Badge> : <Badge tone="accent">Needs checking</Badge>}
                    {t.originalChangedSince && <Badge tone="danger">English changed</Badge>}
                  </td>
                  <td>
                    <Badge tone={statusTone[t.status]}>{t.status}</Badge>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
    </>
  );
}

/** The English original, for translators to compare against. */
function useOriginal(kind: Kinds, originalId: string | undefined) {
  return useQuery({
    queryKey: ['content', 'original', kind, originalId],
    enabled: !!originalId,
    queryFn: async () => {
      if (kind === 'pages') {
        const p = unwrap(await api.GET('/api/admin/content/pages/{id}', { params: { path: { id: originalId! } } }));
        return { title: p.page.title, body: p.page.body };
      }
      const p = unwrap(await api.GET('/api/admin/content/posts/{id}', { params: { path: { id: originalId! } } }));
      return { title: p.title, body: p.body };
    },
  });
}

function Preview({ title, body }: { title: string; body: string }) {
  return (
    <Card title="Preview">
      <div className="markdown">
        {title && <h2>{title}</h2>}
        {body ? <Markdown>{body}</Markdown> : <p className="muted">Start writing to see a preview.</p>}
      </div>
    </Card>
  );
}
