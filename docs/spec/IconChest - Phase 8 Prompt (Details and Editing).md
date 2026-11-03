# IconChest Phase 8 Prompt: Details and Editing

Paste the Master Project Brief, then this prompt, into a fresh AI coding session that has the Phase 1 to 7 code. Those phases must be complete and their tests passing.

## Goal

Make the details panel editable and finish tag management. The user can edit one image or many at once (title, description, tags, rating, taken date, hidden), create tags inline, rename, recolour and delete tags, copy a file's path, and open it in the system viewer. All edits live in the database, and **no file on disk is ever changed**. There is no filter UI yet (Phase 9).

## Before you write any code

1. Read the existing solution, especially the Phase 7 details panel and image browser, `selection-model.js`, the Phase 4 bulk-edit endpoint (`PATCH /api/images`, presence versus null semantics) and the tag endpoints, plus `docs/FRONTEND.md`. Follow their conventions.
2. Re-read brief sections 12.6 and 14.
3. Verify which of `<input type="date">` and `<input type="color">` behave acceptably in the oldest WebKitGTK you support. Record the result in `docs/FRONTEND.md` and choose the fallbacks described below accordingly.
4. Reply with a short plan and assumptions, then build.

## Scope

### 1. API additions (keep them small, test them all)

- **`POST /api/images/summary`** with `{ ids }` (1 to 10,000, otherwise 400; unknown ids 404). Returns aggregated edit state in one call, because the multi-edit panel cannot fetch 10,000 records: `count`, `totalSizeBytes`, `formats` (counts), width and height ranges, and for each editable field whether all images share one value and what it is (`title`, `description`, `rating`, `takenOverrideDate`), counts of `isHidden` and `isFavourite`, and `tags` as `[{ tagId, count }]`. Pass the ids to SQLite as one JSON array parameter with `json_each` (not thousands of parameters). 10,000 ids must answer in well under 200 ms on the seeded dataset.
- **`takenOverrideDate`** (`yyyy-mm-dd` or `null`) accepted by `PATCH /api/images` as an alternative to `takenOverrideUtc` (sending both is 400). The server converts the date to **local noon in the server's time zone**, stored as UTC, so the calendar day in the query language and the Years tree always matches what the user typed even across DST. Details and summary responses return `takenOverrideDate` too, plus `effectiveDateSource` (`override`, `exif`, `created` or `modified`).
- **`GET /api/images/{id}/duplicates`**: the other images in the same duplicate set (id, path, folder, size), excluding hidden and missing. Empty if none.
- **`POST /api/native/open-file`** with `{ imageId }`: opens the file in the operating system's default application. **Loopback requests only** (otherwise 403), the image must exist, not be flagged missing, and its file must exist (otherwise 404). Never accept a path. Implement `INativeLauncher` in Core (Windows: shell-execute; Linux: `xdg-open` started with an argument list and no shell), so tests use a fake. A launch failure becomes a clear problem-details error. `GET /api/system/info` already reports `isLocal`.

### 2. Edit model (pure logic)

Put the editing rules in small data-agnostic modules and test them exhaustively:

- `lib/edit-model.js`: build state from a summary; track which fields the user changed (dirty); produce the exact `PATCH /api/images` body (**only dirty fields**, an emptied description or a cleared date as an explicit `null`, an emptied title as the empty string meaning "reset to default", tag changes as `addTagIds` and `removeTagIds` computed from the tri-state result); validate (rating 0 to 5, date `yyyy-mm-dd` and real, selection size at most 10,000); revert; "Multiple values" placeholder logic (a field with differing values shows a placeholder, is not dirty until the user types, and typing applies the same value to every selected image).
- `lib/tag-combobox-model.js`: filtering and ranking (prefix matches first, then substring; case-insensitive and Unicode-aware, same folding as the query language), the "Create 'name'" option rules (shown only when no tag matches case-insensitively and the name is valid; an exact match selects the existing tag), name validation (trimmed, 1 to 50 characters, no control characters).
- `lib/contrast.js`: WCAG contrast ratio, hex parsing and validation, a palette of 16 preset foreground and background pairs that all meet 4.5:1.
- `lib/tags-store.js`: cached `/api/tags` list by id, subscribe, refresh on `library.changed`, optimistic helpers.

### 3. The editable details panel

Replace the Phase 7 content, keeping the container and its open, close, auto-open and resize behaviour. The panel has three parts: an editable form, a read-only information area, and a sticky footer with **Save** and **Revert**.

**Editable fields**

- **Title:** single-line text. Empty means "reset to the default from the file name" (show the default as a hint and offer a **Reset to file name** button).
- **Description:** multi-line text.
- **Tags:** a combobox (ARIA 1.2 pattern) listing matching existing tags as you type, with arrow keys, Enter to add and Backspace to remove the last chip, plus a **Create "name"** option for new names. **Creating a tag is immediate** (it calls `POST /api/tags` with the default colours, shows a toast, and updates the tags store) while *assigning* it is part of the pending edit. If a create returns 409 because the name exists, reuse the existing tag. For several images, a tag carried by all shows a normal chip with an **x**, a tag carried by some shows a dashed chip with a count such as `3/5`, with a **+** to apply it to all and an **x** to remove it from all. Chips use each tag's own foreground and background colours.
- **Rating:** five stars as a radio group (mouse, and arrow keys), click the current star or press a **Clear** button for 0. With several images that differ it shows a mixed state.
- **Taken date:** `yyyy-mm-dd`, with a **Clear** button and quick-fill buttons **Use EXIF date** and **Use created date** where those dates exist. Use `<input type="date">` only if your verified WebKitGTK handles it well, otherwise a text field with strict validation and a small calendar-free hint. Show which date currently wins (`effectiveDateSource`).
- **Hidden:** a checkbox with a mixed state when the selection is mixed (`aria-checked="mixed"`).
- **Favourite:** the heart in the panel header acts **immediately**, exactly like the grid heart (it is not part of Save).

**Read-only information** (never editable, and the controls must not exist for these): full path (click to copy, with an "Copied" confirmation) with an **Open** button beside it, file name, dimensions, format, file size, hash and algorithm, created, modified, added, EXIF date, the effective date with its source, transparency, status, EXIF fields as a collapsible key and value list, and **Copies** (a collapsible list of the other files in its duplicate set, using the new endpoint). For several images show shared values and a "Multiple" marker where they differ, the count, and the total size. The **Open** button appears only when `isLocal` is true, and is disabled with a tooltip unless exactly one image is selected.

**Behaviour**

- **Save** sends one `PATCH /api/images` with only the dirty fields, then refetches the summary, shows a toast ("Saved 3 images"), and soft-refreshes the grid so new titles, sorts and tags appear. Save and Revert are disabled when nothing is dirty. `Ctrl+S` (or `Cmd+S`) saves while focus is inside the panel. Plain Enter in a field does not save. Errors from the server (problem details with an `errors` map) appear beside the field, and the edits stay in place.
- **Hiding:** saving `isHidden = true` refreshes the view and **clears the selection**, because the hidden images leave the view. The success toast has an **Undo** button that sets them visible again. There is no keyboard shortcut for hiding.
- **Unsaved changes guard:** any action that would change the selection, change the tree node, switch view, or close the panel while edits are pending first shows a three-button dialog (**Save**, **Discard**, **Cancel**). **Cancel must leave the selection and view exactly as they were.** To support this, add an asynchronous veto hook to the image browser's selection and navigation changes (for example `beforeChange()` returning a promise), used by the panel. Also register a `beforeunload` warning as a best effort.
- **Limits:** if more than 10,000 images are selected, show a clear message in place of the form ("Select 10,000 images or fewer to edit") and keep the read-only counts.
- **Concurrency:** a `library.changed` event or a scan while editing must not clobber dirty fields. Non-dirty fields update from the refetched data. If an image disappears (404 on save), show a toast, drop it from the selection, and keep the remaining edits.
- Accessibility: labelled fields, a `role="group"` per field set, live announcements for save results and errors, visible focus, and everything operable by keyboard.

### 4. Tag management

In the **Tags** tree (Panel A) and via a chip menu in the details panel:

- A reusable accessible **context menu** component (`role="menu"`, arrow keys, type-ahead, Escape, focus return) opened by right-click, the Menu key, or `Shift+F10`.
- **Rename** (`F2`): inline edit or small dialog. Names are unique case-insensitively, a conflict shows the server's 409 message inline.
- **Change colours:** a dialog with a live chip preview, foreground and background pickers (`<input type="color">` only if verified, otherwise the preset swatches plus hex fields with validation), the 16 preset pairs, and a non-blocking warning when the contrast ratio is below 4.5:1.
- **Delete** (`Delete` key or the menu): a confirm dialog that says the tag is used by *N* images (from `GET /api/tags/{id}`), that it will be removed from all of them, and that no images are deleted. It honours the `confirmBeforeDelete` setting (when off, delete immediately with an Undo-less toast).
- After any change update the tags store, the Tags tree (order and colours), open chips in the details panel, and refresh the grid if the current view is filtered by that tag. If the deleted tag is the selected tree node, fall back to **Show all**.
- Add `F2`, `Delete`, `Ctrl+S`, `Menu` and `Shift+F10` to `shortcuts.js` with their contexts.

## Out of scope

Tag import and export (Phase 10), the filter bar and query box (Phase 9), the Duplicates, Missing and Help pages, a context menu for images, undo history, tag merging, batch title patterns, rating shortcut keys, "reveal in file manager", editing anything about files on disk (never), thumbnails changes.

## Required tests

**Pure logic** (import the modules inside the page, as in Phases 6 and 7; large data-driven tables):

- **Edit model:** dirty tracking for every field; a PATCH body that contains only changed fields; explicit `null` for cleared description and date; the empty-title rule; tri-state tag diffs (all, some, none, with adds and removes in every combination, adding a tag that some images already have); "Multiple values" behaviour and typing over it; validation (rating bounds, impossible dates such as 2026-02-30, 10,000 limit); revert; no body at all when nothing changed.
- **Tag combobox model:** ranking, Unicode and case folding, create-option rules (existing tag with different case, whitespace, 51-character names, control characters), keyboard index maths.
- **Contrast:** known WCAG values, hex parsing (3 and 6 digits, bad input), every preset pair meets 4.5:1.
- **Tags store:** caching, refresh, subscriptions, optimistic update and rollback.

**API tests** (`WebApplicationFactory`, real SQLite):

- **Summary:** correct aggregates for every field including nulls, mixed values and tag coverage; 1, 2 and 10,000 ids; more than 10,000 gives 400; an unknown id gives 404; authentication required; performance on the seeded dataset (slow-marked, print timings).
- **takenOverrideDate:** set, clear, round trip in several time zones including DST days (Europe/London on 2026-03-29 and 2026-10-25) and a zone far from UTC; the stored value falls on the right local day for the Years tree and for `taken == date` queries; sending both date forms is 400; invalid dates are 400; `effectiveDateSource` for each case.
- **Duplicates endpoint:** excludes self, hidden and missing; empty when none; 404 for an unknown id.
- **open-file:** loopback only (simulate a remote address), 404 for unknown, missing or flagged-missing, the fake launcher receives exactly the recorded path, file names with spaces, quotes, semicolons and Unicode are passed as one argument, launch failure maps to a clear error, authentication required.
- Existing Phase 4 bulk-edit tests still pass; hidden changes still recompute duplicates.

**End-to-end** (Playwright for .NET, Chromium and WebKit, against a standalone API with a small real library of about 60 generated images plus a seeded set for the 10,000-image limit):

- **Single edit:** change every editable field, Save, reload the page and restart the API, and confirm persistence through the UI and through `GET /api/images/{id}`. Title reset, description clear and date clear work. **The original files are byte-identical** (hash them before and after).
- **Multi-edit:** select several images with differing values, confirm the "Multiple values" placeholders, change one field only, Save, and confirm through the API that **only that field changed** on every image and all other fields kept their individual values. Test tags with all, some and none coverage, applying to all, removing from all, and mixed adds and removes.
- **Tags inline:** create via the combobox (new name, existing name with different case, invalid name, server 409), keyboard-only use, chips coloured by tag, creation stays even if the edit is reverted.
- **Rating and date:** star mouse and keyboard use, Clear, mixed state, quick-fill buttons, invalid date message.
- **Hidden:** tri-state checkbox, Save hides the images, the selection is cleared and the grid refreshes, the toast Undo restores them, and no keyboard shortcut hides anything (press `H` and assert nothing happens).
- **Unsaved-changes guard:** the Save, Discard and Cancel dialog for selection change by click and keyboard, tree navigation, view switch and closing the panel; **Cancel keeps selection and view unchanged**; Revert; `Ctrl+S`; a failed save (route the request to fail) keeps the edits and shows the error; 404 during save; more than 10,000 selected shows the message; a `library.changed` event during editing keeps dirty fields.
- **Read-only and utilities:** read-only values match the API, no input exists for them, the path copies to the clipboard, **Open** is visible only when `isLocal` is true (override the info response) and only for one image, clicking it calls the endpoint with the id, the Copies list and the EXIF section render, the panel heart acts immediately and matches the grid heart.
- **Tag management:** rename (updates the tree order, chips in the panel, and grid views filtered by that tag), a case-only rename, a conflicting rename, recolour with the swatches and with hex, the contrast warning, delete with the confirm dialog showing the right count, delete removing the tag from images (check via the API), deletion with `confirmBeforeDelete` off, deleting the selected tree node falls back to Show all, context menu by mouse and keyboard, `F2` and `Delete`, menu accessibility (roles, focus return, Escape).
- No console errors or unexpected failed requests in any test.

**Manual checks** (listed below) for the operating-system integration that tests cannot cover.

## Working rules

- Follow the master brief and earlier conventions. Where silent, choose the simplest option and say so. If this prompt conflicts with the brief, follow this prompt and tell me.
- The front end never builds HTML from data by concatenation, and never talks to anything except the API. Keep editing logic in the pure modules, with the DOM code thin.
- Add any dependency to `THIRD-PARTY-NOTICES.md` after checking its licence. Prefer none.

## Definition of done

- `dotnet build` and `dotnet test` pass on Linux, and the E2E tests pass in both browsers when run.
- All required tests exist and pass, or you list exactly which could not be written and why.
- `docs/FRONTEND.md` records the date and colour input findings. `shortcuts.js` is complete.
- Your final reply includes: new files, assumptions, deviations, decisions to confirm, known issues, and the manual checklist with exact commands.

## How I will test Phase 8

1. Select one image and edit its title, description, rating, taken date and tags. Save, restart the app, and confirm everything stuck. Confirm the file on disk is unchanged.
2. Select five images with different titles and ratings. Check the "Multiple values" placeholders, change only the rating, save, and confirm the titles were not touched.
3. Add a tag to some of five images, then select all five and see the `3/5` chip. Apply it to all, then remove it from all.
4. Create a tag from the combobox, try the same name in a different case, and try an invalid name.
5. Make an edit and then click another image. Try Cancel, then Discard, then Save in the dialog. Repeat while changing folder.
6. Tick Hidden on three images and Save. Confirm they leave the view, the selection clears, and Undo brings them back.
7. Rename a tag, recolour it with the swatches and a custom hex (try a low-contrast pair), then delete it and check the warning count.
8. Click the path to copy it and paste it somewhere. Click Open and confirm the image opens in your default viewer, on both Windows and Linux. Try an SVG.
9. Open an image that has copies and check the Copies list. Check an image with EXIF.
10. Start a scan and edit at the same time, and confirm nothing you typed is lost.
11. Try the date and colour controls in the Linux desktop window, not just a browser.
12. Run `dotnet test`, the E2E tests in both browsers, and the slow performance test.
