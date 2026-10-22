# Changelog

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
