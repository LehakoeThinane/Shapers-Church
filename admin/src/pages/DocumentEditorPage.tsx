import { useQuery, useQueryClient } from '@tanstack/react-query';
import Image from '@tiptap/extension-image';
import { TableKit } from '@tiptap/extension-table';
import TextAlign from '@tiptap/extension-text-align';
import { EditorContent, useEditor, type Editor } from '@tiptap/react';
import StarterKit from '@tiptap/starter-kit';
import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { Button, ErrorNote, Loading, PageHeader, Select, TextInput } from '../components/ui';
import { askConfirm, askText } from '../lib/dialogs';
import { api, formatDateTime, unwrap } from '../lib/api';
import { DOCX_TYPE, fileUrl, sendDocument } from '../lib/documents';
import { htmlToWord, wordToHtml } from '../lib/wordDocuments';

/**
 * Edit a Word document in the browser: the .docx opens as editable text (headings, lists, tables, pictures, links),
 * and Save writes a real .docx back as the document's next version, so nothing is ever overwritten.
 */
export default function DocumentEditorPage() {
  const { id } = useParams();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const libraryId = params.get('library');
  const folderId = params.get('folder');

  const detail = useQuery({
    queryKey: ['documents', 'detail', id],
    enabled: !!id,
    queryFn: async () => unwrap(await api.GET('/api/admin/documents/files/{id}', { params: { path: { id: id! } } })),
  });
  const content = useQuery({
    queryKey: ['documents', 'word', id, detail.data?.document.version],
    enabled: !!detail.data,
    staleTime: Infinity,
    queryFn: async () => {
      const response = await fetch(fileUrl(id!, { download: true }), { credentials: 'include' });
      if (!response.ok) throw new Error(`Couldn't open the document (${response.status}).`);
      return wordToHtml(await response.arrayBuffer());
    },
  });

  const editable = !id || (detail.data ? detail.data.access !== 'View' : false);
  const [name, setName] = useState('Untitled document.docx');
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);

  const editor = useEditor({
    extensions: [
      StarterKit.configure({ link: { openOnClick: false, autolink: true, defaultProtocol: 'https' } }),
      TableKit.configure({ table: { resizable: false } }),
      Image.configure({ allowBase64: true }),
      TextAlign.configure({ types: ['heading', 'paragraph'] }),
    ],
    content: '',
    shouldRerenderOnTransaction: true,
    editorProps: { attributes: { class: 'word-page', 'aria-label': 'Document text' } },
    onUpdate: () => setDirty(true),
  });

  // Put the opened document in the editor once it has loaded (not an edit, so it doesn't count as a change).
  useEffect(() => {
    if (editor && content.data) editor.commands.setContent(content.data.html, { emitUpdate: false });
  }, [editor, content.data]);

  useEffect(() => {
    editor?.setEditable(editable);
  }, [editor, editable]);

  // Leaving with unsaved changes asks first.
  useEffect(() => {
    if (!dirty) return;
    const warn = (e: BeforeUnloadEvent) => e.preventDefault();
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [dirty]);

  const save = useCallback(async () => {
    if (!editor || saving || !editable) return;
    setSaving(true);
    setError(null);
    try {
      const blob = await htmlToWord(editor.getHTML());
      if (id && detail.data) {
        const doc = await sendDocument({ documentId: id }, new File([blob], detail.data.document.name, { type: DOCX_TYPE }), detail.data.document.name);
        setSaved(`Saved as version ${doc.version}.`);
      } else if (libraryId) {
        const fileName = name.trim().toLowerCase().endsWith('.docx') ? name.trim() : `${name.trim() || 'Untitled document'}.docx`;
        const doc = await sendDocument({ libraryId, folderId }, new File([blob], fileName, { type: DOCX_TYPE }), fileName);
        setDirty(false);
        void queryClient.invalidateQueries({ queryKey: ['documents'] });
        navigate(`/documents/edit/${doc.id}`, { replace: true });
        return;
      }
      setDirty(false);
      void queryClient.invalidateQueries({ queryKey: ['documents'] });
    } catch (err) {
      setError(err);
    } finally {
      setSaving(false);
    }
  }, [editor, saving, editable, id, detail.data, libraryId, folderId, name, navigate, queryClient]);

  // Ctrl+S / Cmd+S saves, like Word.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
        e.preventDefault();
        void save();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [save]);

  const back = detail.data
    ? `/documents/${detail.data.libraryId}${detail.data.document.folderId ? `?folder=${detail.data.document.folderId}` : ''}`
    : libraryId
      ? `/documents/${libraryId}${folderId ? `?folder=${folderId}` : ''}`
      : '/documents';

  if (id && (detail.isPending || content.isPending)) return <Loading />;
  if (id && (detail.error || content.error)) return <ErrorNote error={detail.error ?? content.error} />;
  if (!id && !libraryId) return <ErrorNote error={new Error('Open the library to create the document in first.')} />;

  return (
    <>
      <PageHeader
        title={detail.data ? detail.data.document.name : 'New document'}
        subtitle={
          <>
            <Link
              to={back}
              onClick={async (e) => {
                if (!dirty) return;
                // Stay put while asking; leave only if they say so.
                e.preventDefault();
                if (await askConfirm("Your changes haven't been saved. Leave without saving them?", { title: 'Unsaved changes', confirmLabel: 'Leave without saving', cancelLabel: 'Stay', danger: true })) navigate(back);
              }}>
              {detail.data?.libraryName ?? 'Back to the library'}
            </Link>
            {detail.data && ` · version ${detail.data.document.version}, ${formatDateTime(detail.data.document.updatedAt)} by ${detail.data.document.updatedBy}`}
          </>
        }
        actions={
          editable ? (
            <div className="row">
              {!id && <TextInput aria-label="File name" value={name} onChange={(e) => setName(e.target.value)} />}
              <span className="small muted" aria-live="polite">
                {saving ? 'Saving…' : dirty ? 'Unsaved changes' : saved}
              </span>
              <Button variant="primary" busy={saving} disabled={!dirty && !!id} onClick={() => void save()}>
                Save
              </Button>
            </div>
          ) : (
            <span className="small muted">Read only: ask a library manager for edit access to change it.</span>
          )
        }
      />
      <ErrorNote error={error} />
      {content.data && content.data.notes.length > 0 && (
        <p className="small muted word-notes">Some Word formatting can't be shown here (for example text boxes or page layout); the text is all here. Saving keeps the old version in Versions.</p>
      )}
      <div className="word-editor glass">
        {editor && editable && <Toolbar editor={editor} />}
        <EditorContent editor={editor} />
      </div>
    </>
  );
}

function Toolbar({ editor }: { editor: Editor }) {
  const style = editor.isActive('heading', { level: 1 }) ? 'h1' : editor.isActive('heading', { level: 2 }) ? 'h2' : editor.isActive('heading', { level: 3 }) ? 'h3' : 'p';
  const chain = () => editor.chain().focus();
  const inTable = editor.isActive('table');

  const addLink = async () => {
    const current = editor.getAttributes('link').href as string | undefined;
    const href = await askText(current ? 'Change the link' : 'Add a link', {
      label: 'Link address (leave empty to remove the link)',
      value: current ?? 'https://',
      confirmLabel: 'Save link',
    });
    if (href === null) return;
    if (!href.trim() || href.trim() === 'https://') chain().extendMarkRange('link').unsetLink().run();
    else chain().extendMarkRange('link').setLink({ href: href.trim() }).run();
  };
  const addPicture = (file: File | undefined) => {
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => chain().setImage({ src: String(reader.result), alt: file.name }).run();
    reader.readAsDataURL(file);
  };

  return (
    <div className="word-toolbar" role="toolbar" aria-label="Formatting">
      <Tool label="Undo" onClick={() => chain().undo().run()} disabled={!editor.can().undo()}>
        ↶
      </Tool>
      <Tool label="Redo" onClick={() => chain().redo().run()} disabled={!editor.can().redo()}>
        ↷
      </Tool>
      <span className="word-sep" />
      <Select
        aria-label="Text style"
        className="input word-style"
        value={style}
        onChange={(e) => {
          const v = e.target.value;
          if (v === 'p') chain().setParagraph().run();
          else chain().toggleHeading({ level: Number(v[1]) as 1 | 2 | 3 }).run();
        }}>
        <option value="p">Normal text</option>
        <option value="h1">Heading 1</option>
        <option value="h2">Heading 2</option>
        <option value="h3">Heading 3</option>
      </Select>
      <span className="word-sep" />
      <Tool label="Bold" active={editor.isActive('bold')} onClick={() => chain().toggleBold().run()}>
        <strong>B</strong>
      </Tool>
      <Tool label="Italic" active={editor.isActive('italic')} onClick={() => chain().toggleItalic().run()}>
        <em>I</em>
      </Tool>
      <Tool label="Underline" active={editor.isActive('underline')} onClick={() => chain().toggleUnderline().run()}>
        <u>U</u>
      </Tool>
      <Tool label="Strikethrough" active={editor.isActive('strike')} onClick={() => chain().toggleStrike().run()}>
        <s>S</s>
      </Tool>
      <span className="word-sep" />
      <Tool label="Bulleted list" active={editor.isActive('bulletList')} onClick={() => chain().toggleBulletList().run()}>
        • ≡
      </Tool>
      <Tool label="Numbered list" active={editor.isActive('orderedList')} onClick={() => chain().toggleOrderedList().run()}>
        1. ≡
      </Tool>
      <Tool label="Quote" active={editor.isActive('blockquote')} onClick={() => chain().toggleBlockquote().run()}>
        ❝
      </Tool>
      <span className="word-sep" />
      {(['left', 'center', 'right', 'justify'] as const).map((a) => (
        <Tool key={a} label={`Align ${a}`} active={editor.isActive({ textAlign: a })} onClick={() => chain().setTextAlign(a).run()}>
          {{ left: '⇤', center: '↔', right: '⇥', justify: '☰' }[a]}
        </Tool>
      ))}
      <span className="word-sep" />
      <Tool label="Link" active={editor.isActive('link')} onClick={() => void addLink()}>
        🔗
      </Tool>
      <label className="word-tool" title="Insert a picture" aria-label="Insert a picture">
        🖼
        <input type="file" hidden accept="image/png,image/jpeg,image/gif,image/webp" onChange={(e) => addPicture(e.target.files?.[0])} />
      </label>
      <Tool label="Table" onClick={() => chain().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run()}>
        ⊞
      </Tool>
      <Tool label="Line" onClick={() => chain().setHorizontalRule().run()}>
        ―
      </Tool>
      {inTable && (
        <>
          <span className="word-sep" />
          <Tool label="Add row" onClick={() => chain().addRowAfter().run()}>
            +row
          </Tool>
          <Tool label="Add column" onClick={() => chain().addColumnAfter().run()}>
            +col
          </Tool>
          <Tool label="Delete row" onClick={() => chain().deleteRow().run()}>
            −row
          </Tool>
          <Tool label="Delete column" onClick={() => chain().deleteColumn().run()}>
            −col
          </Tool>
          <Tool label="Delete table" onClick={() => chain().deleteTable().run()}>
            ✕⊞
          </Tool>
        </>
      )}
    </div>
  );
}

function Tool({ label, active, disabled, onClick, children }: { label: string; active?: boolean; disabled?: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      className={`word-tool${active ? ' word-tool-on' : ''}`}
      title={label}
      aria-label={label}
      aria-pressed={active}
      disabled={disabled}
      onMouseDown={(e) => e.preventDefault()}
      onClick={onClick}>
      {children}
    </button>
  );
}
