import React, { useEffect, useLayoutEffect, useRef, useState } from 'react';

/**
 * A right-click menu at (x, y). `items` are { id, label, enabled } or { separator: true }; `choose` gets
 * the chosen id, or null when the menu is dismissed (Escape, a click elsewhere, scrolling, resizing).
 * Arrow keys move between items, as in a system menu.
 */
export default function ContextMenu({ x, y, items, choose }) {
  const menu = useRef(null);
  const [position, setPosition] = useState({ left: x, top: y });

  // Keep the whole menu on screen.
  useLayoutEffect(() => {
    const rect = menu.current.getBoundingClientRect();
    setPosition({
      left: Math.max(4, Math.min(x, window.innerWidth - rect.width - 4)),
      top: Math.max(4, Math.min(y, window.innerHeight - rect.height - 4)),
    });
    menu.current.querySelector('button:not(:disabled)')?.focus();
  }, [x, y]);

  useEffect(() => {
    const dismiss = () => choose(null);
    window.addEventListener('resize', dismiss);
    window.addEventListener('blur', dismiss);
    document.addEventListener('scroll', dismiss, true);
    return () => {
      window.removeEventListener('resize', dismiss);
      window.removeEventListener('blur', dismiss);
      document.removeEventListener('scroll', dismiss, true);
    };
  }, [choose]);

  const move = (direction) => {
    const buttons = [...menu.current.querySelectorAll('button:not(:disabled)')];
    const current = buttons.indexOf(document.activeElement);
    buttons[(current + direction + buttons.length) % buttons.length]?.focus();
  };

  return (
    <div
      className="menu-backdrop"
      onMouseDown={(event) => event.target === event.currentTarget && choose(null)}
      onContextMenu={(event) => {
        event.preventDefault();
        choose(null);
      }}
    >
      <ul
        ref={menu}
        className="context-menu"
        role="menu"
        style={position}
        onKeyDown={(event) => {
          if (event.key === 'Escape') choose(null);
          else if (event.key === 'ArrowDown') move(1);
          else if (event.key === 'ArrowUp') move(-1);
          else return;
          event.preventDefault();
        }}
      >
        {items.map((item, index) =>
          item.separator ? (
            <li key={`separator-${index}`} className="menu-separator" role="separator" />
          ) : (
            <li key={item.id} role="none">
              <button
                type="button"
                role="menuitem"
                disabled={item.enabled === false}
                onClick={() => choose(item.id)}
              >
                {item.label}
              </button>
            </li>
          ),
        )}
      </ul>
    </div>
  );
}
