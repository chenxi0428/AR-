/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Go 后端地址，缺省 http://127.0.0.1:8080 */
  readonly VITE_API_BASE?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
