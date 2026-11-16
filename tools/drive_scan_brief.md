# Quét Google Drive (thư mục "VN 제작" của mọi game)

Bản hướng dẫn giao cho agent quét Drive, dùng mỗi sáng thứ 2 (lịch tự động) hoặc khi cần quét lại.
Kết quả là `drive_files.csv` + `drive_folders.csv`, rồi chạy:

```
python3 -I tools/drive_summary.py <thư mục>/drive_files.csv data
```

## Hướng dẫn cho agent

Goal: build a fresh raw inventory of every file in the "VN 제작" folders of all game folders in a Google Drive root, using the Google Drive MCP tools (load with ToolSearch "select:mcp__Google_Drive__search_files" if not loaded). Read-only: never modify, move, share or trash anything in Drive.

- Root folder id: `1HD_G0IuI2ZySmDJ5YgbfsqqblnxKAvyi` (shared drive `1udGILsHE0AIq6bLiyXQj_O6LhyPDUZYJe0Q`). Its direct child folders are games. List them with query `parentId = '1HD_G0IuI2ZySmDJ5YgbfsqqblnxKAvyi' and mimeType = 'application/vnd.google-apps.folder'`, pageSize 100, following every nextPageToken.
- search_files syntax: `parentId = '<id>'` (not `'<id>' in parents`, which errors). `title = '...'`, `title contains '...'`, `mimeType = ...`, `modifiedTime > '...'` work; several parentId clauses can be OR-ed in one query. orderBy only supports modifiedTime / viewedByMeTime. Always pass `excludeContentSnippets: true`.
- Large results are saved to a file under the session's tool-results folder — parse it with python (JSON `{files:[...], nextPageToken}`; each file has id, title, parentId, mimeType, createdTime, modifiedTime).
- The video folder is not always named exactly "VN 제작". Treat as a game's VN folder any child folder named "VN 제작", "VN제작", "VN영상" (also with a number prefix such as "3. VN영상") or "UA Creative_VN"; if none is a direct child, look one level deeper (Captain of Space, Match Flight, Rush Attack, Suzy's Restaurant and ThemePark Manager keep "VN 제작" inside another folder). New games appear as new child folders of the root (e.g. "The Miasma" → "VN영상", added 2026-11-16), so always re-list the root.
- Inside VN 제작 there are subfolders like `1.16x9`, `2.9x16`, `3.1x1`, `Video(영상)/1. 9x16`, `Social Media`, `TEST`. Recurse into every subfolder at any depth, except folders named `TEST` or `Please Delete`.
- Ignore files in other shared drives (e.g. `1Zw82yPbdXBk2tIrfgtDVyZHLwJe0ElVtyEw`) — duplicates.

Steps:
1. List all game folders under the root (all pages).
2. Find each game's VN 제작 folder (direct child of the game folder).
3. Recursively list every file in each VN 제작 (all pages, pageSize 100).
4. Write `drive_files.csv`: `Game,Subfolder,Path,File,CreatedTime,ModifiedTime,FileId` — one row per file, including non-matching names. Game = game folder name; Subfolder = first-level folder under VN 제작; Path = full relative path under VN 제작. Use the python csv module.
5. Write `drive_folders.csv`: `Game,GameFolderId,VNFolderId` (blank if no VN 제작).

Report back: number of game folders, number with VN 제작, total files, games whose file count changed vs the previous scan, and any folders that could not be listed.
