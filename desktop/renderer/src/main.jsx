import React from 'react';
import { createRoot } from 'react-dom/client';
import 'bootstrap/dist/css/bootstrap.min.css';
import './styles/base.css';
import './styles/builder.css';
import './styles/results.css';
import App from './App.jsx';
import { getTheme } from './i18n.js';

document.documentElement.setAttribute('data-theme', getTheme());

createRoot(document.getElementById('root')).render(<App />);
