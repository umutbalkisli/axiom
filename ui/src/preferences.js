// The UI's preferences, kept by the axiom program (the UI gets a new address at every launch, so the browser's
// own storage would forget them). Loaded once before the UI starts; reads are synchronous after that.
let cache = {};

export async function loadPreferences() {
  try {
    const response = await fetch('/api/preferences');
    cache = response.ok ? await response.json() : {};
  } catch {
    cache = {};
  }
}

export function getPref(key, fallback) {
  return cache[key] ?? fallback;
}

// Saved in the background; a failure only means the preference is not remembered next time.
export function setPref(key, value) {
  cache = { ...cache, [key]: value };
  fetch('/api/preferences', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ [key]: value ?? null }),
  }).catch(() => {});
}
