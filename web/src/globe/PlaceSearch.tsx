import { useId, useState } from 'react';
import type { KeyboardEvent } from 'react';
import { presetPlaces, searchPlaces } from './places';
import type { Place } from './places';

export interface PlaceSearchProps {
  /** A place was chosen: the globe goes there. */
  onGo: (place: Place) => void;
}

/**
 * A small field that takes the globe to a place by name: matches appear as you type, and Enter
 * or a tap goes to one. Before anything is typed it offers the four places the desktop app
 * keeps as buttons. The list is in the code, so nothing is asked of the network. The matches
 * hang below the field and take no room in the page.
 */
export function PlaceSearch({ onGo }: PlaceSearchProps) {
  const [text, setText] = useState('');
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const listId = useId();
  const typed = text.trim().length > 0;
  const matches = typed ? searchPlaces(text) : presetPlaces;
  const chosen = Math.min(active, Math.max(0, matches.length - 1));

  const go = (place: Place) => {
    onGo(place);
    setText(place.name);
    setOpen(false);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      setOpen(true);
      if (matches.length > 0) {
        setActive((chosen + (event.key === 'ArrowDown' ? 1 : matches.length - 1)) % matches.length);
      }
    } else if (event.key === 'Enter') {
      if (matches[chosen]) {
        event.preventDefault();
        go(matches[chosen]);
      }
    } else if (event.key === 'Escape') {
      setOpen(false);
    }
  };

  return (
    <div className="place-search">
      <input
        type="search"
        role="combobox"
        aria-label="Go to a place"
        aria-expanded={open}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={open && matches[chosen] ? `${listId}-${chosen}` : undefined}
        placeholder="Go to a place"
        autoComplete="off"
        spellCheck={false}
        value={text}
        onChange={(event) => {
          setText(event.target.value);
          setActive(0);
          setOpen(true);
        }}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
        onKeyDown={onKeyDown}
      />
      {open && (
        <ul className="place-search-list" id={listId} role="listbox" aria-label="Places">
          {matches.map((place, index) => (
            <li
              key={place.name}
              id={`${listId}-${index}`}
              role="option"
              aria-selected={index === chosen}
              className={index === chosen ? 'place-search-option place-search-active' : 'place-search-option'}
              // Pressing an option must not take the focus from the field first, or the list would close under the finger.
              onPointerDown={(event) => event.preventDefault()}
              onClick={() => go(place)}
            >
              <span className="place-search-name">{place.name}</span>
              <span className="place-search-note">{place.note}</span>
            </li>
          ))}
          {matches.length === 0 && <li className="place-search-none">No place by that name in the list.</li>}
        </ul>
      )}
    </div>
  );
}
