# Dark Ages v3.50 — window-open dispatch

How the client opens/toggles interface windows, recovered from `DarkAges.exe`. This complements the
geometry in [`ui-layout-350.md`](ui-layout-350.md) / [`ui-350.json`](ui-350.json): the layout says *where*
a window draws; this says *how* it gets opened.

## Window-toggle dispatcher — `sub_47607c`

A single "toggle window by index" function driven by a 7-entry jump table at `0x4761a5`
(`cmp idx,6 / ja default / jmp [idx*4 + 0x4761a5]`). Each window object is heap-allocated
(`operator new` via `sub_43cd30`) then its constructor/loader is called:

| index | action | VA |
|--:|---|---|
| 0 | close / toggle the current window | `0x476175` |
| 1 | (window, unidentified) | `0x439aa7` |
| 2 | none / default | `0x476188` |
| 3 | **Friend List** | `0x477e20` |
| 4 | **Macro Setup** | `0x476c3e` |
| 5 | **Game Setting** | `0x477221` |
| 6 | **server-request window** ("Waiting for response from server…") | `0x47851e` |

The dispatcher is invoked **through a vtable** (indirect call), not a direct `call` — which is why the
toolbar buttons show no literal cross-reference to it. Wiring an individual toolbar-icon rect to the index
it passes requires resolving the toolbar widget's vtable and per-button config (not yet done).

## A widget-local key handler — `sub_425332`

On a keydown message (message type `[msg+0xC] == 8`) this reads the key code `[msg+0x10]`, range-checks it to
`0x45..0x88`, and dispatches through a byte index table at `0x4254a5` into a **6-entry** jump table at
`0x42548d`. Decoding those tables shows it is **not** the global hotkey→window map — it is one widget's
`OnKey` (it has no direct callers; it's reached via a vtable, and `this` carries the widget's own state):

| case | target | effect |
|--:|---|---|
| 0,1,2 | `0x4253fc`/`0x4253ba`/`0x4253db` | set the widget's internal index `[this+0xE8]` (0/1/2) and refresh via `sub_42564c` |
| 3 | `0x42541a` | open **Macro Setup** |
| 4 | `0x42544a` | open **Game Setting** |
| 5 | `0x42547a` | no-op (default — most keys land here) |

The key codes indexing `0x4254a5` are the client's **own key enum**, not Windows virtual-key codes, so they
can't be labelled without recovering that enum. The **global** game hotkeys (the F-key panel shortcuts players
remember) are dispatched the same vtable way through the main window's `OnKey`; that map was not located as a
decodable table and remains open.

## Notes for a reimplementation

- Windows are singletons-by-pointer: the dispatcher stores the created object and index 0 closes whatever is
  open, so opening the same window again toggles it.
- Object sizes seen: Friend/Macro = `0x548` bytes, Game Setting = `0x554`, server-request = `0x54c`.
- The equipment/legend path goes through `sub_419200` ("User Equip").
