import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// The dev server proxies /api to the local backend, so the session cookie is same-origin.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
      // Development file storage and the podcast feed are served by the API too.
      '/media-files': 'http://localhost:5080',
      '/podcast.xml': 'http://localhost:5080',
    },
  },
});
