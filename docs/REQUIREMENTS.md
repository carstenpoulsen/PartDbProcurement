# Requirements: Part-DB Procurement v1.2 (booking)

Status: **DRAFT, for approval**. Date: 2026-10-21. Section 6 was released in **v1.1.0** (2026-10-22); the booking functions are planned for v1.2.

Origin: **S** = stated by the owner · **A** = proposed by the developer and approved by the owner · **A?** = proposed, not yet approved

Verification (planned): **T** = automated test · **M** = manual test on Windows · **I** = inspection of code or output

---

## 1. Scope

v1.2 adds two booking functions to the existing, read-only order-list tool:

1. **Goods receipt:** book a DigiKey packing list into Part-DB stock.
2. **Build:** withdraw the parts used by *N* builds of a KiCad project from Part-DB stock.

Both functions change data in Part-DB. Everything else stays read-only.

**Multi-project purchases and a recent-projects list** (section 6, read-only) were released in v1.1.0.

## 2. Common requirements

| ID | Requirement | Origin | Verif. |
|---|---|---|---|
| R1 | Whether something has already been booked is decided **only** from the app's own booking log, never from Part-DB data or history. Changes made outside the app are not detected; that is the user's responsibility. | S | I |
| R2 | The booking log is append-only: entries are never changed or deleted by the app. It is one CSV file (UTF-8, readable in Excel). | A | T |
| R3 | The location of the booking log can be set in the app. Default: `%AppData%\PartDbProcurement\bookings.csv`. A shared folder may be chosen when several PCs are used. | A | M |
| R4 | Each line is written to the log **immediately after** it has been booked in Part-DB, so an interrupted run can be resumed without booking a line twice. When a key (R12, R21) has lines that are already booked, the app offers to book **only the remaining lines**. | A | T |
| R5 | Before any change is made in Part-DB, a preview lists every change (part, lot, quantity before → after, new parts and purchase information to be created). Nothing is changed until the user confirms. | A | M |
| R6 | Booking requires a Part-DB API token with **write** access. It is stored separately from the read-only token used for pulls, and encrypted in the same way (DPAPI, current Windows user). | A | I |
| R7 | **Lot rule:** if the part has exactly one lot, that lot is used, whether or not it has a named storage location. If the part has no lot, one is created without a storage location (goods receipt only). If the part has several lots, the line is shown as "choose lot" in the preview and is not booked until the user chooses one. | S | T |
| R8 | Every booking in Part-DB carries a change comment that identifies it: `Goods receipt DigiKey invoice <no>` or `Build <project> ×<n>`. | A | M |
| R9 | Log columns: timestamp, type (Receipt/Build), key, reference, line no., Part-DB ID, MPN, SPN, quantity, stock before, stock after, lot code, date code, result (Booked / Created / Skipped / Failed), Windows user, PC name. | A | I |

## 3. Goods receipt from a DigiKey packing list

| ID | Requirement | Origin | Verif. |
|---|---|---|---|
| R10 | The app reads a DigiKey packing list PDF (as `DK_PACKLIST_<invoice>.pdf`, Danish/English layout). | S | T |
| R11 | Per shipment the log records the date, invoice number, sales order number and, per line: DigiKey part number (PART), MPN (MFG#), manufacturer, quantity. | S | T |
| R12 | The key of a shipment is the **invoice number**. If the invoice is already in the log, the app **warns** with the date of the earlier booking. The user may continue; the default answer is **No**. | S | M |
| R12a | The packing list number (e.g. `1`) is recorded in the log together with the invoice number. | A | T |
| R13 | The quantity booked is the **Shipped** quantity. A line delivered as several lots is booked as the sum of the lots. | A | T |
| R14 | A line is matched to a Part-DB part by **MPN** (exact, ignoring case and leading/trailing spaces). | S | T |
| R14a | If the matched part has no DigiKey purchase information with this DigiKey part number, a new purchase information entry (supplier DigiKey, supplier part number = PART) is **added**. Existing purchase information is not changed. | S | T |
| R14b | If more than one Part-DB part has the same MPN, the line is shown as "choose part" in the preview and is not booked until the user chooses one. | A | T |
| R15 | If no part matches, a new part is created: name = MFG#, MPN = MFG#, description = DESC, manufacturer per R15c, purchase information: supplier DigiKey, supplier part number = PART, and one lot without a storage location holding the shipped quantity. | S | T |
| R15a | New parts are marked **Needs review** in Part-DB. | A | M |
| R15b | New parts are placed in the category **"New from DigiKey"**, which the app creates if it does not exist. | A | M |
| R15c | The manufacturer is looked up by name, ignoring a trailing DigiKey suffix in parentheses (`YAGEO (VA)` → `YAGEO`). A manufacturer that does not exist is created. | S | T |
| R16 | Lot code and date code from the packing list are recorded in the log. | A | T |
| R17 | Lines that cannot be read with certainty (missing PART, MFG# or Shipped quantity) are shown in the preview as errors and are not booked. | A? | T |

## 4. Build: withdrawing usage from stock

| ID | Requirement | Origin | Verif. |
|---|---|---|---|
| R20 | The app withdraws the usage of a build from Part-DB: quantity per build (as counted for the order list) × number of builds, per Part-DB ID. | S | T |
| R21 | The log records the project, date, number of builds and the quantity withdrawn per part. The key is the project file plus the time of booking. | S | T |
| R22 | If the project has been booked before, the app **warns** with the date(s) and number of builds, and lets the user continue, because building another batch is legitimate. | A | M |
| R23 | If the stock of a part is lower than its usage, that part is not withdrawn (stock never goes negative). It is listed, and the user decides whether to continue without it. Skipped parts are logged as Skipped. | A | T |
| R24 | Symbols without a Part-DB ID cannot be withdrawn; they are listed in the preview and logged as Skipped. | A? | T |

## 5. User interface and documentation

| ID | Requirement | Origin | Verif. |
|---|---|---|---|
| R30 | The main dialog gets two buttons: **Book delivery…** (choose PDF) and **Book build…** (uses the current project and number of builds). | A? | M |
| R31 | The booking log can be opened from the app (it opens in Excel). | A? | M |
| R32 | The README is updated: the tool is read-only **unless** the booking functions are used, which need a separate write token. | A | I |
| R33 | PDF text is extracted with the NuGet package **PdfPig** (Apache 2.0). This is the only external dependency. | A | I |

## 6. Multi-project purchase and recent projects (released in v1.1.0)

| ID | Requirement | Origin | Verif. |
|---|---|---|---|
| R40 | The dialog holds a **list of projects** (one or more `.kicad_pro`), each with its own number of builds. Projects are added with **Add…** (multi-select), from the recent list, by drag and drop, or on the command line, and removed with **Remove** / Del. A project cannot be in the list twice. | S | M |
| R41 | One pull reads all projects and fetches each Part-DB ID once. A Part-DB ID used by several projects is **one line**; its need is the sum of per build × builds over the projects. | S | T |
| R42 | Parts without a Part-DB ID, excluded parts and warnings are listed **per project**, since lines without an ID cannot be matched across projects. | A? | T |
| R43 | The Excel output and the grid show one *per build* column per project, references prefixed with the project name, and per-project details on the Info sheet. A single project gives the same output as v1.0. | A? | T |
| R44 | A **recent list** keeps the last **5** projects, newest first, each with its **number of builds**. A project moves to the top when it is added, pulled or exported; changing its builds updates the remembered number. Missing files are shown as *not found* and cannot be added. | S | T |
| R45 | The project list is restored at start-up. v1.0 settings (one project, one number of builds) are migrated to it. | A? | T |
| R46 | The "already exported" warning (B11) is checked for **each** project; an export of a combined list is recorded for every project in it, together with the projects it was combined with. | A? | M |
| R47 | Changing builds recalculates without a new pull. Adding or removing a project disables export until the next pull. | A? | M |
| R48 | **Book build…** (R30) with several projects in the list withdraws the usage of all of them, as one booking per project (key per R21). | A? | M |

## 7. Out of scope (v1.2)

- Updating prices from the invoice (possible later; the invoice number is already the key, so it cannot double-book a shipment).
- Suppliers other than DigiKey.
- Choosing a lot by rule (FIFO etc.) instead of R7.
- KiCad 10 design variants (known limitation from v1.0).

## 8. Baseline: v1.0 as built

For traceability, the behaviour of v1.0 that was not specified up front:

| ID | Behaviour |
|---|---|
| B1 | API token stored DPAPI-encrypted in `%AppData%\PartDbProcurement\settings.json`. |
| B2 | Read-only access to Part-DB. |
| B3 | Supplier on the order list = first non-obsolete supplier on the part. |
| B4 | Order quantity rounded up to the smallest price break. |
| B5 | DNP excluded by default; *Exclude from BOM* and `#` power symbols always excluded; both also inherited from sheets (G1, fixed). |
| B6 | Stock = Part-DB *total in stock* (expired lots and lots with unknown amount count as 0). |
| B7 | A Part-DB ID that is not found → the full need is ordered and marked red. |
| B8 | Symbols without an ID are grouped by Value + Footprint + MPN and listed separately. |
| B9 | No currency conversion; totals per currency. |
| B10 | Old-data warning when the pull is more than 30 minutes old. |
| B11 | "Already exported" history stored locally per PC. |
| B12 | Plain HTTP to Part-DB accepted. |
| B13 | A file open in Excel gives a Retry/Cancel message; unexpected errors give a plain message and are logged to `error.log`. |
| G2 | KiCad 10 design variants are ignored (known limitation). |
