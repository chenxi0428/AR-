import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// 开发期由 VITE_API_BASE 指定 Go 后端地址（默认 http://127.0.0.1:8080）。
// 管理端直连后端，依赖后端 CORS（见 ar-inspection/docs/API.md）。
export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    port: 5173,
  },
});
