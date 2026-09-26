import React from 'react';
import { createRoot } from 'react-dom/client';
import 'bootstrap/dist/css/bootstrap.min.css';
import './app.css';
import App from './App.jsx';
import { getTheme } from './i18n.js';

document.documentElement.setAttribute('data-theme', getTheme());

createRoot(document.getElementById('root')).render(<App />);
