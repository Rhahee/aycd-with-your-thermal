const $ = (id) => document.getElementById(id);
const dayNames = ["Senin", "Selasa", "Rabu", "Kamis", "Jumat", "Sabtu", "Minggu"];
const pending = new Map();
let nextMessageId = 1;
let state = { lists: [], printer: { portName: "COM6", name: "RPP02N (80 mm)", paperWidthMm: 80, printableDots: 576, baudRate: 115200 } };
let view = "daily";
let selectedId = null;
let saveChain = Promise.resolve();
let previewTimer = null;
let previewGeneration = 0;
let toastTimer = null;
let editingPrinter = null;
let updateAvailable = false;
let updateBusy = false;

if (!window.chrome?.webview) {
  document.body.textContent = "Aplikasi ini perlu dibuka melalui AYCD Printer di Windows.";
} else {
  window.chrome.webview.addEventListener("message", ({ data }) => {
    if (data.type === "updateProgress") {
      showToast(data.message);
      return;
    }
    const request = pending.get(data.id);
    if (!request) return;
    pending.delete(data.id);
    data.ok ? request.resolve(data.data) : request.reject(new Error(data.error || "Operasi gagal."));
  });
  start().catch((error) => showToast(error.message, true));
}

function bridge(type, payload = {}) {
  return new Promise((resolve, reject) => {
    const id = nextMessageId++;
    pending.set(id, { resolve, reject });
    window.chrome.webview.postMessage({ id, type, payload });
  });
}

function isoDate(date) {
  const pad = (value) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function parseDate(value) { return new Date(`${value}T12:00:00`); }
function weekStart(value) {
  const date = parseDate(value);
  date.setDate(date.getDate() - (date.getDay() + 6) % 7);
  return isoDate(date);
}
function humanDate(value) {
  return parseDate(value).toLocaleDateString("id-ID", { weekday: "long", day: "numeric", month: "long", year: "numeric" });
}
function weekLabel(value) {
  const end = parseDate(value);
  end.setDate(end.getDate() + 6);
  const format = { day: "numeric", month: "long", year: "numeric" };
  return `${parseDate(value).toLocaleDateString("id-ID", format)} – ${end.toLocaleDateString("id-ID", format)}`;
}
function createId() { return crypto.randomUUID().replaceAll("-", ""); }
function currentList() { return state.lists.find((list) => list.id === selectedId) || null; }
function chosenDate() {
  const value = $("planDate").value || isoDate(new Date());
  return view === "weekly" ? weekStart(value) : value;
}

async function start() {
  state = await bridge("getState");
  $("planDate").value = isoDate(new Date());
  bindEvents();
  selectForDate();
  render();
}

function bindEvents() {
  document.querySelectorAll(".nav-parent").forEach((button) => button.addEventListener("click", () => {
    const group = button.closest(".nav-group");
    setNavGroup(group, button.getAttribute("aria-expanded") !== "true");
    if (button.getAttribute("aria-expanded") === "true") {
      document.querySelectorAll(".nav-group").forEach((other) => {
        if (other !== group) setNavGroup(other, false);
      });
    }
  }));
  document.querySelectorAll(".nav-button").forEach((button) => button.addEventListener("click", () => {
    view = button.dataset.view;
    document.querySelectorAll(".nav-group").forEach((group) =>
      setNavGroup(group, group.contains(button)));
    if (view === "daily") $("planDate").value = isoDate(new Date());
    if (view === "weekly") $("planDate").value = weekStart(isoDate(new Date()));
    if (view === "daily" || view === "weekly") selectForDate();
    render();
  }));
  $("planDate").addEventListener("change", () => {
    if (view === "weekly") $("planDate").value = weekStart($("planDate").value);
    selectForDate();
    render();
  });
  $("newListButton").addEventListener("click", createList);
  $("emptyCreateButton").addEventListener("click", createList);
  $("listTitle").addEventListener("input", () => {
    const list = currentList();
    if (!list) return;
    list.title = $("listTitle").value;
    persistList();
    schedulePreview();
  });
  $("refreshPreview").addEventListener("click", () => refreshPreview());
  $("printButton").addEventListener("click", printList);
  $("paperWidth").addEventListener("change", () => {
    $("printableDots").value = $("paperWidth").value === "80" ? "576" : "384";
  });
  $("reloadPorts").addEventListener("click", () => loadPorts($("portName").value));
  $("savePrinter").addEventListener("click", savePrinter);
  $("testPrint").addEventListener("click", testPrint);
  $("profileSelect").addEventListener("change", activateProfile);
  $("newProfile").addEventListener("click", newProfile);
  $("deleteProfile").addEventListener("click", deleteProfile);
  $("checkUpdate").addEventListener("click", checkUpdate);
  $("runUpdate").addEventListener("click", runUpdate);
}

function setNavGroup(group, expanded) {
  group.querySelector(".nav-parent").setAttribute("aria-expanded", String(expanded));
  group.querySelector(".nav-children").hidden = !expanded;
}

function selectForDate() {
  const kind = view === "weekly" ? "weekly" : "daily";
  const date = chosenDate();
  const matches = state.lists.filter((list) => list.kind === kind && list.date === date);
  matches.sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));
  selectedId = matches[0]?.id || null;
}

async function createList() {
  if (view !== "daily" && view !== "weekly") return;
  const now = new Date().toISOString();
  const list = { id: createId(), kind: view, date: chosenDate(), title: "", tasks: [], createdAt: now, updatedAt: now };
  try {
    const saved = await bridge("saveList", { list });
    state.lists.push(saved);
    selectedId = saved.id;
    render();
    $("listTitle").focus();
    showToast("Daftar baru dibuat.");
  } catch (error) { showToast(error.message, true); }
}

function persistList() {
  const list = currentList();
  if (!list) return Promise.resolve();
  const snapshot = structuredClone(list);
  saveChain = saveChain.catch(() => {}).then(async () => {
    const saved = await bridge("saveList", { list: snapshot });
    const existing = state.lists.find((item) => item.id === saved.id);
    if (existing) existing.updatedAt = saved.updatedAt;
  });
  saveChain.catch((error) => showToast("Gagal menyimpan: " + error.message, true));
  return saveChain;
}

function render() {
  document.querySelectorAll(".nav-button").forEach((button) =>
    button.classList.toggle("active", button.dataset.view === view));
  document.querySelectorAll(".nav-group").forEach((group) =>
    group.querySelector(".nav-parent").classList.toggle("current",
      !!group.querySelector(`.nav-button[data-view="${view}"]`)));
  const planner = view === "daily" || view === "weekly";
  $("plannerView").classList.toggle("hidden", !planner);
  $("historyView").classList.toggle("hidden", view !== "history");
  $("printerView").classList.toggle("hidden", view !== "printer");
  $("settingsView").classList.toggle("hidden", view !== "settings");
  $("newListButton").classList.toggle("hidden", !planner);
  $("pageTitle").textContent = ({ daily: "Daily", weekly: "Weekly", history: "History", printer: "Printer", settings: "Update" })[view];
  if (planner) renderPlanner();
  if (view === "history") renderHistory();
  if (view === "printer") renderPrinter();
  if (view === "settings") renderSettings();
}

function renderSettings() {
  $("checkUpdate").disabled = updateBusy;
  $("runUpdate").disabled = updateBusy || !updateAvailable;
}

async function checkUpdate() {
  if (updateBusy) return;
  updateBusy = true;
  updateAvailable = false;
  $("checkUpdate").textContent = "Memeriksa…";
  renderSettings();
  try {
    const result = await bridge("checkUpdate");
    updateAvailable = result.status === "available";
    showToast(updateAvailable ? "Pembaruan tersedia. Tekan Update untuk memasangnya." : "Aplikasi sudah versi terbaru.");
  } catch (error) {
    showToast("Gagal memeriksa update: " + error.message, true);
  } finally {
    updateBusy = false;
    $("checkUpdate").textContent = "Cek Update";
    renderSettings();
  }
}

async function runUpdate() {
  if (updateBusy || !updateAvailable) return;
  updateBusy = true;
  $("runUpdate").textContent = "Memperbarui…";
  renderSettings();
  try {
    await saveChain;
    await bridge("installUpdate");
  } catch (error) {
    showToast("Update gagal: " + error.message, true);
  } finally {
    updateBusy = false;
    $("runUpdate").textContent = "Update";
    renderSettings();
  }
}

function renderPlanner() {
  const weekly = view === "weekly";
  $("dateLabel").textContent = weekly ? "Pilih minggu" : "Pilih tanggal";
  $("dateHint").textContent = weekly ? `Senin–Minggu, ${weekLabel(chosenDate())}` : humanDate(chosenDate());
  $("paperBadge").textContent = `${state.printer.paperWidthMm} mm`;
  const list = currentList();
  $("emptyPlan").classList.toggle("hidden", !!list);
  $("editor").classList.toggle("hidden", !list);
  $("refreshPreview").disabled = !list;
  $("printButton").disabled = !list;
  if (list) {
    $("listTitle").value = list.title;
    $("listDateText").textContent = weekly ? weekLabel(list.date) : humanDate(list.date);
    renderTasks(list);
    schedulePreview(0);
  } else {
    previewGeneration++;
    $("previewImage").classList.add("hidden");
    $("previewPlaceholder").classList.remove("hidden");
  }
}

function renderTasks(list) {
  const container = $("taskGroups");
  container.replaceChildren();
  if (list.kind === "daily") addGroup(container, list, -1, "Tugas hari ini");
  else {
    addGroup(container, list, -1, "Tugas umum minggu ini");
    dayNames.forEach((name, index) => addGroup(container, list, index, name));
  }
  updateTaskCount();
}

function addGroup(container, list, day, heading) {
  const group = document.createElement("section");
  group.className = "task-group";
  const header = document.createElement("div");
  header.className = "group-heading";
  const title = document.createElement("h3");
  title.textContent = heading;
  header.append(title);
  group.append(header);
  const blocks = list.tasks.filter((task) => task.day === day);
  blocks.forEach((task, index) => group.append(blockRow(task, index, blocks.length)));
  const addMenu = document.createElement("details");
  addMenu.className = "block-add";
  const summary = document.createElement("summary");
  summary.textContent = "+ Tambah blok";
  addMenu.append(summary);
  const choices = document.createElement("div");
  choices.className = "block-choices";
  for (const [type, label] of [["checklist", "☐ Checklist"], ["heading", "Judul bagian"], ["note", "Catatan"]]) {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = label;
    button.addEventListener("click", () => {
      addBlock(list, day, type);
    });
    choices.append(button);
  }
  addMenu.append(choices);
  group.append(addMenu);
  container.append(group);
}

function addBlock(list, day, type) {
  if (list.tasks.length >= 200) { showToast("Maksimal 200 blok dalam satu daftar.", true); return; }
  const task = { id: createId(), type, text: "", day, completed: false };
  list.tasks.push(task);
  renderTasks(list);
  document.querySelector(`[data-block-id="${task.id}"] .block-text`)?.focus();
  persistList();
}

function blockRow(task, index, count) {
  const row = document.createElement("div");
  row.className = `block-row block-${task.type || "checklist"}`;
  row.dataset.blockId = task.id;
  row.classList.toggle("completed", task.completed);
  if ((task.type || "checklist") === "checklist") {
    const check = document.createElement("input");
    check.type = "checkbox";
    check.checked = task.completed;
    check.setAttribute("aria-label", "Tandai tugas selesai");
    check.addEventListener("change", () => {
      task.completed = check.checked;
      row.classList.toggle("completed", task.completed);
      persistList();
    });
    row.append(check);
  } else {
    const icon = document.createElement("span");
    icon.className = "block-icon";
    icon.textContent = task.type === "heading" ? "H" : "≡";
    icon.setAttribute("aria-hidden", "true");
    row.append(icon);
  }
  const input = document.createElement("textarea");
  input.className = "block-text";
  input.maxLength = 500;
  input.rows = 1;
  input.placeholder = task.type === "heading" ? "Judul bagian…" : task.type === "note" ? "Tulis catatan…" : "Tulis tugas…";
  input.value = task.text;
  requestAnimationFrame(() => resizeBlockText(input));
  input.addEventListener("input", () => {
    task.text = input.value;
    resizeBlockText(input);
    updateTaskCount();
    persistList();
    schedulePreview();
  });
  input.addEventListener("keydown", (event) => {
    if (event.key === "Enter" && task.type !== "note" && !event.shiftKey) {
      event.preventDefault();
      const list = currentList();
      if (list) addBlock(list, task.day, "checklist");
    }
  });
  row.append(input);
  for (const [direction, label, disabled] of [[-1, "Naikkan blok", index === 0], [1, "Turunkan blok", index === count - 1]]) {
    const move = document.createElement("button");
    move.type = "button";
    move.className = "move-block";
    move.textContent = direction < 0 ? "↑" : "↓";
    move.title = label;
    move.setAttribute("aria-label", label);
    move.disabled = disabled;
    move.addEventListener("click", () => moveBlock(task, direction));
    row.append(move);
  }
  const remove = document.createElement("button");
  remove.type = "button";
  remove.className = "delete-block";
  remove.textContent = "×";
  remove.title = "Hapus blok";
  remove.setAttribute("aria-label", "Hapus blok");
  remove.addEventListener("click", () => {
    const list = currentList();
    if (!list) return;
    list.tasks = list.tasks.filter((item) => item.id !== task.id);
    renderTasks(list);
    persistList();
    schedulePreview();
  });
  row.append(remove);
  return row;
}

function resizeBlockText(input) {
  input.style.height = "auto";
  input.style.height = `${Math.max(38, input.scrollHeight)}px`;
}

function moveBlock(task, direction) {
  const list = currentList();
  if (!list) return;
  const siblings = list.tasks.filter((item) => item.day === task.day);
  const index = siblings.findIndex((item) => item.id === task.id);
  const neighbor = siblings[index + direction];
  if (!neighbor) return;
  const from = list.tasks.findIndex((item) => item.id === task.id);
  const to = list.tasks.findIndex((item) => item.id === neighbor.id);
  [list.tasks[from], list.tasks[to]] = [list.tasks[to], list.tasks[from]];
  renderTasks(list);
  persistList();
  schedulePreview();
}

function updateTaskCount() {
  const count = currentList()?.tasks.filter((task) => task.text.trim()).length || 0;
  $("taskCount").textContent = `${count} blok`;
}

function schedulePreview(delay = 450) {
  clearTimeout(previewTimer);
  const generation = ++previewGeneration;
  previewTimer = setTimeout(() => refreshPreview(generation), delay);
}

async function refreshPreview(generation = ++previewGeneration) {
  const list = currentList();
  if (!list) return;
  try {
    const image = await bridge("preview", { list: structuredClone(list) });
    if (generation !== previewGeneration || currentList()?.id !== list.id) return;
    $("previewImage").src = image;
    $("previewImage").classList.remove("hidden");
    $("previewPlaceholder").classList.add("hidden");
  } catch (error) { showToast("Pratinjau gagal: " + error.message, true); }
}

async function printList() {
  const list = currentList();
  if (!list) return;
  const button = $("printButton");
  button.disabled = true;
  button.textContent = "Mengirim…";
  try {
    await saveChain;
    await bridge("printList", { id: list.id });
    showToast("Data cetak dikirim. Periksa hasil pada kertas.");
  } catch (error) { showToast("Gagal mencetak: " + error.message, true); }
  finally { button.disabled = false; button.textContent = "Cetak daftar"; }
}

function renderHistory() {
  const container = $("historyList");
  container.replaceChildren();
  $("historyCount").textContent = `${state.lists.length} daftar`;
  if (!state.lists.length) {
    const empty = document.createElement("div");
    empty.className = "history-empty";
    empty.textContent = "Belum ada daftar yang tersimpan.";
    container.append(empty);
    return;
  }
  const sorted = [...state.lists].sort((a, b) =>
    b.date.localeCompare(a.date) || b.updatedAt.localeCompare(a.updatedAt));
  for (const list of sorted) {
    const item = document.createElement("div");
    item.className = "history-item";
    const icon = document.createElement("div");
    icon.className = "history-kind";
    icon.textContent = list.kind === "daily" ? "◫" : "▦";
    const main = document.createElement("div");
    main.className = "history-main";
    const name = document.createElement("strong");
    name.textContent = list.title.trim() || (list.kind === "daily" ? "Daftar harian" : "Daftar mingguan");
    const detail = document.createElement("small");
    detail.textContent = `${list.kind === "daily" ? humanDate(list.date) : weekLabel(list.date)} · ${list.tasks.length} blok`;
    main.append(name, detail);
    const actions = document.createElement("div");
    actions.className = "history-actions";
    const open = document.createElement("button");
    open.type = "button";
    open.className = "button button-ghost";
    open.textContent = "Buka";
    open.addEventListener("click", () => {
      view = list.kind;
      document.querySelectorAll(".nav-group").forEach((group) =>
        setNavGroup(group, group.dataset.group === "todo"));
      $("planDate").value = list.date;
      selectedId = list.id;
      render();
    });
    const remove = document.createElement("button");
    remove.type = "button";
    remove.className = "button button-ghost button-danger";
    remove.textContent = "Hapus";
    remove.addEventListener("click", async () => {
      if (!confirm(`Hapus "${name.textContent}"?`)) return;
      try {
        await saveChain;
        await bridge("deleteList", { id: list.id });
        state.lists = state.lists.filter((item) => item.id !== list.id);
        if (selectedId === list.id) selectedId = null;
        renderHistory();
        showToast("Daftar dihapus.");
      } catch (error) { showToast(error.message, true); }
    });
    actions.append(open, remove);
    item.append(icon, main, actions);
    container.append(item);
  }
}

function renderPrinter() {
  editingPrinter = state.printer;
  const select = $("profileSelect");
  select.replaceChildren();
  for (const profile of state.printerProfiles) {
    const option = document.createElement("option");
    option.value = profile.id;
    option.textContent = `${profile.name || "Printer"} · ${profile.paperWidthMm} mm · ${profile.portName}`;
    select.append(option);
  }
  select.value = editingPrinter.id;
  $("deleteProfile").disabled = state.printerProfiles.length <= 1;
  populatePrinterForm(editingPrinter);
}

function populatePrinterForm(printer) {
  $("printerName").value = printer.name;
  $("paperWidth").value = String(printer.paperWidthMm);
  $("printableDots").value = String(printer.printableDots);
  $("baudRate").value = String(printer.baudRate);
  const selectedPort = document.createElement("option");
  selectedPort.value = printer.portName;
  selectedPort.textContent = printer.portName || "Pilih port printer";
  $("portName").replaceChildren(selectedPort);
  loadPorts(printer.portName);
}

async function loadPorts(desiredPort = editingPrinter?.portName || state.printer.portName) {
  try {
    const ports = await bridge("listPorts");
    const names = ports.map((port) => port.name);
    const options = [...new Set([desiredPort, ...names])].filter(Boolean);
    const select = $("portName");
    select.replaceChildren();
    const placeholder = document.createElement("option");
    placeholder.value = "";
    placeholder.textContent = "Pilih port printer";
    select.append(placeholder);
    for (const port of options) {
      const option = document.createElement("option");
      option.value = port;
      option.textContent = ports.find((item) => item.name === port)?.label || `${port} (tidak terdeteksi)`;
      select.append(option);
    }
    select.value = desiredPort;
  } catch (error) { showToast("Port tidak dapat dibaca: " + error.message, true); }
}

function printerFromForm() {
  return {
    id: editingPrinter.id,
    name: $("printerName").value.trim(),
    portName: $("portName").value,
    paperWidthMm: Number($("paperWidth").value),
    printableDots: Number($("printableDots").value),
    baudRate: Number($("baudRate").value)
  };
}

async function savePrinter() {
  try {
    await saveChain;
    state = await bridge("savePrinter", { printer: printerFromForm() });
    renderPrinter();
    showToast("Pengaturan printer disimpan.");
  } catch (error) { showToast(error.message, true); }
}

async function testPrint() {
  const button = $("testPrint");
  button.disabled = true;
  button.textContent = "Mengirim…";
  try {
    await saveChain;
    state = await bridge("savePrinter", { printer: printerFromForm() });
    renderPrinter();
    await bridge("testPrint");
    showToast("Data uji dikirim. Periksa hasil pada kertas.");
  } catch (error) { showToast("Uji cetak gagal: " + error.message, true); }
  finally { button.disabled = false; button.textContent = "Cetak halaman uji"; }
}

async function activateProfile() {
  const id = $("profileSelect").value;
  const profile = state.printerProfiles.find((item) => item.id === id);
  if (!profile) return;
  try {
    await saveChain;
    state = await bridge("activatePrinter", { id });
    renderPrinter();
  } catch (error) { showToast(error.message, true); }
}

function newProfile() {
  editingPrinter = {
    id: createId(), name: "Printer baru", portName: "",
    paperWidthMm: 80, printableDots: 576, baudRate: 115200
  };
  const option = document.createElement("option");
  option.value = editingPrinter.id;
  option.textContent = "Profil baru (belum disimpan)";
  $("profileSelect").append(option);
  $("profileSelect").value = editingPrinter.id;
  $("deleteProfile").disabled = false;
  populatePrinterForm(editingPrinter);
  $("printerName").focus();
}

async function deleteProfile() {
  if (!editingPrinter) return;
  const saved = state.printerProfiles.some((profile) => profile.id === editingPrinter.id);
  if (!saved) { renderPrinter(); return; }
  if (!confirm(`Hapus profil "${editingPrinter.name}"?`)) return;
  try {
    state = await bridge("deletePrinter", { id: editingPrinter.id });
    renderPrinter();
    showToast("Profil printer dihapus.");
  } catch (error) { showToast(error.message, true); }
}

function showToast(message, error = false) {
  const toast = $("toast");
  toast.textContent = message;
  toast.classList.toggle("error", error);
  toast.classList.add("show");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => toast.classList.remove("show"), 5000);
}
