import React from 'react';
import { createRoot } from 'react-dom/client';
import 'bootstrap/dist/css/bootstrap.min.css';
import './styles/base.css';
import './styles/builder.css';
import './styles/results.css';
import App from './App.jsx';
import { getTheme } from './i18n.js';
import { loadPreferences } from './preferences.js';
import { keepSessionAlive } from './session.js';

keepSessionAlive();
// Preferences (language, theme, recent collections) come from the axiom program before the first render.
await loadPreferences();
const theme = getTheme();
const dark =
  theme === 'dark' ||
  (theme === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');

createRoot(document.getElementById('root')).render(<App />);
