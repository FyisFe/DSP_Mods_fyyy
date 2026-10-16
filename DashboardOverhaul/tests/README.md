# Dashboard checks

From the repository root, run:

```powershell
dotnet run --project DashboardOverhaul/tests/Checks.csproj -c Debug -- "C:/Program Files (x86)/Applications/Steam/steamapps/common/Dyson Sphere Program/DSPGAME_Data/Managed"
```

For a different game installation, also pass `-p:GameManagedDir="<managed directory>"` to `dotnet run`.
The checks run against original game binaries. The mod's publicized compile references must match the installed game; stripped reference assemblies cannot execute these checks.

Automated coverage: header and rename geometry; Harmony target and reverse-patch signatures from the plugin's patch list; page ordering, limits and recovery; transactional moves, independent copies including native locks, and impact counts. Style checks cover menu-order cycling and locked/unlocked border defaults. Native `ChartData.Export/Import` checks cover safe defaults without the mod, custom-style/layout recovery after a native resave and style-table growth, failed-write rollback, lock/border inheritance during preset changes, and one-time legacy-lock migration. Batch checks cover incompatible monitor domains and independent native callback targets. Production checks cover layout availability, rate periods, alarm thresholds, and the native number formatter. The fixtures do not create live Unity UI or establish in-game acceptance.

Save compatibility boundary: choosing the native default while the mod is absent is indistinguishable from retaining its fallback. Reinstalling may restore the earlier custom setting in that case; explicit non-default native styles remain authoritative.

## In-game acceptance

| Scenario | Expected result |
| --- | --- |
| 1–9 pages, long Chinese/English names, sidebar open/closed, UI scale and window changes | Tabs fit the available width, show hover/focus state and full-name tooltips; active rename follows its tab |
| Rename the first/last tab by double-click and menu; type a 64-character name; resize while editing | Input stays fully inside the header, original label is hidden, and the caret remains visible while editing the long name |
| Top-row charts; sidebar open/closed/in motion; first sidebar row's menu | Charts, grid and sidebar entries start below the header; the native arrow retains its position, appearance and animation; the sidebar overlays charts without shifting them |
| Sidebar open; open the page gear menu and its style submenus; right-click the first page tab and a visible chart near the right edge | Page menus and their first rows remain fully visible above the header and sidebar; chart submenus remain within the dashboard |
| Bright game scene behind the dashboard; sidebar closed/open/in motion; change window size and UI scale | The background, grid and filled left margin stay stationary throughout sidebar animation; no uncovered strip reveals the game scene; the native arrow remains visible and clickable |
| Open/close the sidebar repeatedly; close and reopen the dashboard; save/reload | Chart positions and sizes stay unchanged as the sidebar covers and reveals them, including the first visible frame |
| Drag a tab fully left; drag a chart above the viewport; click the header | The tab stays inside the tab bar; the header covers overflowing charts and does not pass clicks through to them |
| Maximum pages; only one page | Add/delete is disabled with an explanation |
| Delete a middle page, then add one; reorder; save/reload | New page appears last; order and viewed page are preserved |
| Delete the current populated page, then rename its statistic in the sidebar | No stale UI listener or exception; the statistic remains available |
| Rename from menu/title/sidebar; Enter, Esc, blur, page switch, dashboard close and chart recycling | Correct commit/cancel behavior; no stuck keyboard focus; shared titles and visible tooltips agree |
| Drag charts by their title and tabs by their label | Chart title double-click and normal dragging both work; tab dragging does not accidentally rename/switch |
| Move to an occupied/full page; navigate using the notice; reorder/delete destination while notice is visible | Find a non-overlapping position or leave source unchanged; navigation tracks the page object and highlights the moved chart |
| Remove one chart versus delete its statistic | Only the latter removes all associated charts; confirmation names the statistic and affected count |
| Empty current page while other pages contain charts; no statistics at all | Correct per-page empty state and sidebar/statistics-window action |
| Copy a populated page into an empty page, then edit chart display settings | Layout and styles are copied; display parameters are independent; statistic names are shared |
| Change saves with an editor or notice open | Old edit/notice state cannot act on the new save |
| Hover charts/header while a popup, dialog, text editor or overlay is active; press configured shortcuts and P | Only the intended unobstructed action runs; P opens statistics without leaking into the underlying screen |
| Hover the statistics button and tabs named after native labels, in Chinese and English | Tooltip labels remain literal, with no untranslated key tokens or substituted page names |
| Add charts with bottom-right placement disabled/enabled | Default configuration uses native positioning; opting in prefers the bottom-right free space |
| Cycle custom backgrounds and borders over dark/bright game scenes | Translucent teal-green, pale gold and soft rose fills reveal the native blue surface and grid; numbers and titles stay legible, with muted border accents |
| Shift-click a statistic entry with empty, occupied and full destinations | The shortcut adds the intended chart, respects placement bounds, and leaves existing charts intact when no space exists |
| Switch production presets and every supported layout, including rates/totals and StatsUITweaks enabled/disabled | Values, units, colors and tooltips match the selected layout; ordinary layouts recover their native appearance |
| Switch chart pages/presets repeatedly after changing titles and resource displays | Reused chart instances reset title layout and resource bars without retaining another chart's presentation |
| Batch-edit green charts with mixed production periods, Dyson monitored objects, local/interstellar logistics and storage kinds | Clearly labeled compatible groups receive only their valid options; each chart updates its own data and display |
| Lock through the native chart menu and page menu; change preset; unlock; save/reload; copy page | Native locks persist, copies are independent, and unlocking restores the previous border |
| Save custom backgrounds, borders and reference layouts; load/resave without the mod; reinstall and load | Vanilla dashboard opens with valid defaults; reinstall restores the saved custom settings; edits made after reinstall replace those restored settings |

Build the mod with `dotnet build DashboardOverhaul/DashboardOverhaul.csproj -c Debug --no-restore`. In-game validation uses only `DashboardOverhaul.dll`; never install the stripped game reference DLLs from build output.
