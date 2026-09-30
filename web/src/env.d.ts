interface ImportMetaEnv {
  readonly API_URL?: string;
  readonly PUBLIC_API_URL?: string;
  readonly ALLOW_OFFLINE_BUILD?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
