import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The build (dist/) is embedded in the axiom executable, which serves it at the root of its local address.
export default defineConfig({
  base: '/',
  plugins: [react()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
});
