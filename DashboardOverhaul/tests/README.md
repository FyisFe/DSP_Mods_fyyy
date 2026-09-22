# Dashboard checks

From the repository root, run:

```powershell
dotnet run --project DashboardOverhaul/tests/Checks.csproj -c Debug -- "C:/Program Files (x86)/Applications/Steam/steamapps/common/Dyson Sphere Program/DSPGAME_Data/Managed"
```

For a different game installation, also pass `-p:GameManagedDir="<managed directory>"` to `dotnet run`.
The checks run against original game binaries. The mod's publicized compile references have stripped method bodies and cannot execute these checks.

Automated coverage: header clearance and tab width budgets for 1–9 pages at multiple window widths/UI scales, page rename input bounds at both viewport edges and in narrow windows, Harmony target signatures, sparse page append/order, rejected reorder, page limits, invalid/empty view recovery, failed move atomicity, edge-adjacent placement, complete independent chart copies, and shared-statistic impact counts including watch views. These checks do not instantiate Unity UI or establish in-game acceptance.

## In-game acceptance

| Scenario | Expected result |
| --- | --- |
| 1–9 pages, long Chinese/English names, sidebar open/closed, UI scale and window changes | Tabs fit the available width, show hover/focus state and full-name tooltips; active rename follows its tab |
| Rename the first/last tab by double-click and menu; type a 64-character name; resize while editing | Input stays fully inside the header, original label is hidden, and the caret remains visible while editing the long name |
| Top-row charts; sidebar open/closed/in motion; first sidebar row's menu | Charts, grid and sidebar entries start below the header; the native arrow retains its position, appearance and animation; charts pan clear of the handle |
| Bright game scene behind the dashboard; sidebar closed/open/in motion; change window size and UI scale | The background, grid and filled left margin stay stationary throughout sidebar animation; no uncovered strip reveals the game scene; the native arrow remains visible and clickable |
| Open/close the sidebar repeatedly; close and reopen the dashboard; save/reload | Chart grid positions and sizes stay unchanged; the chart origin follows the sidebar, including the first visible frame; rightmost charts return when the sidebar closes |
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

Build the mod with `dotnet build DashboardOverhaul/DashboardOverhaul.csproj -c Debug --no-restore`. In-game validation uses only `DashboardOverhaul.dll`; never install the stripped game reference DLLs from build output.
