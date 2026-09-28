// Folders of tests. A test is addressed by its path relative to the collection's tests folder
// ("orders/refunds/create.test.yaml"); its folder is everything before the last "/" ("" at the top level).

export function folderOf(fileName) {
  const path = String(fileName || '');
  const slash = path.lastIndexOf('/');
  return slash < 0 ? '' : path.slice(0, slash);
}

export function isInFolder(fileName, folder) {
  return !folder || fileName.startsWith(`${folder}/`);
}

// What a test's run status is remembered by: its stable id when it has one (so it survives renames and
// moves), otherwise its path.
export function statusKey(item) {
  return item.testId || item.fileName;
}

// Every folder that holds a test, directly or deeper, including the folders in between ("orders" for
// "orders/refunds/x.test.yaml"), sorted.
export function allFolders(items) {
  const folders = new Set();
  items.forEach((item) => {
    const parts = folderOf(item.fileName).split('/').filter(Boolean);
    parts.forEach((_, i) => folders.add(parts.slice(0, i + 1).join('/')));
  });
  return [...folders].sort(compareFolders);
}

// Segment by segment, so a folder is always directly followed by its subfolders ("orders",
// "orders/refunds", "orders-archive"), whatever characters the names contain.
function compareFolders(a, b) {
  const left = a.split('/');
  const right = b.split('/');
  for (let i = 0; i < Math.min(left.length, right.length); i++) {
    const order = left[i].localeCompare(right[i]);
    if (order !== 0) return order;
  }
  return left.length - right.length;
}

// Tests grouped by folder, in tree order: each folder (including folders that only hold subfolders)
// followed by its subfolders; tests at the top level come last, as in a file browser. Tests inside a
// folder are sorted by name.
export function groupByFolder(items) {
  const byFolder = new Map();
  allFolders(items).forEach((folder) => byFolder.set(folder, []));
  byFolder.set('', []);
  items.forEach((item) => byFolder.get(folderOf(item.fileName)).push(item));
  return [...byFolder.entries()]
    .map(([folder, groupItems]) => ({
      folder,
      depth: folder ? folder.split('/').length : 0,
      name: folder.split('/').pop(),
      items: [...groupItems].sort((a, b) => a.name.localeCompare(b.name)),
      total: items.filter((item) => folder && isInFolder(item.fileName, folder)).length,
    }))
    .filter((group) => group.folder || group.items.length);
}
