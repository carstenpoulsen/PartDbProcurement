# Changelog

## Unreleased

### Added

- **Notepad for things to remember when ordering**, e.g. consumables that are not in a schematic. Each item has a description, an optional supplier and order number, a quantity and remarks. Open it with **Notepad…**; the button shows how many items are waiting.
- After a pull, notepad items are listed at the bottom of the grid with status `REMEMBER`. On export the app lists them and asks whether to add them to the order list; they appear with the reference `REMEMBER`, sorted in with their supplier.
- **Picking and placement sheets.** The Excel export has one sheet per project (*Pick <project>*) with Name, MPN, Value, per build, Need, storage location, supplier, SPN, references and a Part-DB link. It is sorted by storage location, so the parts can be picked in one round; parts without a Part-DB ID are listed at the bottom.
- After such an export, the items get a *last ordered* date and can be removed from the notepad. Untick *On order list* to keep an item without ordering it.

## 1.1.0 (2026-10-22)

### Added

- **Combined purchase for several KiCad projects.** The dialog holds a list of projects, each with its own number of builds. One pull covers all of them; a Part-DB ID used by several projects becomes one line with the summed need, so it is ordered once.
- **Recent projects.** The last 5 `.kicad_pro` files are remembered together with their number of builds (**Recent ▾**).
- Projects can be added with **Add…** (several at once), by drag and drop, or as several paths on the command line.
- Excel and grid: one *per build* column per project, references prefixed with the project name, per-project details on the *Info* sheet. Parts without a Part-DB ID and excluded parts are listed per project.

### Changed

- The "already exported" warning is checked for each project in the list. A combined export is recorded for every project in it.
- Settings from 1.0 (one project, one number of builds) are migrated to the new project list automatically.

## 1.0.0 (2026-10-21)

First release: order list from a KiCad project and live Part-DB stock.
