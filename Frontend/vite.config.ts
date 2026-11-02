import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
export default defineConfig({ plugins: [react()], resolve:{alias:{"@":new URL("./src",import.meta.url).pathname.replace(/^\/(\w:)/,"$1")}}, server: { strictPort: true, proxy: { '/api': { target: 'http://localhost:5180', changeOrigin: false } } } });
