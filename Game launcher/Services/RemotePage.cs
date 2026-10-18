namespace Game_launcher
{
    /// <summary>Die Webseite der Handy-Fernbedienung. {{STRINGS}} wird durch die übersetzten Texte (JSON) ersetzt, {{ACCENT}} durch die Akzentfarbe.</summary>
    internal static class RemotePage
    {
        public const string Html = """
<!doctype html>
<html lang="de">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="theme-color" content="#0B0F17">
<meta name="robots" content="noindex">
<title>DFP Pro Launcher</title>
<style>
:root { --bg:#0B0F17; --card:#151B27; --line:#232B3B; --text:#F3F4F6; --sub:#9CA3AF; --accent:{{ACCENT}}; --red:#EF4444; --green:#34D399; }
* { box-sizing:border-box; -webkit-tap-highlight-color:transparent; }
body { margin:0; background:var(--bg); color:var(--text); font:15px/1.4 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif; padding:16px 16px calc(24px + env(safe-area-inset-bottom)); }
header { display:flex; align-items:center; gap:10px; margin-bottom:16px; }
header b { font-size:18px; letter-spacing:.3px; }
.dot { width:10px; height:10px; border-radius:50%; background:var(--sub); }
.dot.on { background:var(--green); box-shadow:0 0 8px var(--green); }
.card { background:var(--card); border:1px solid var(--line); border-radius:16px; padding:14px; margin-bottom:14px; }
.card h2 { margin:0 0 10px; font-size:13px; text-transform:uppercase; letter-spacing:1px; color:var(--sub); font-weight:600; }
button { font:inherit; color:var(--text); background:#222A3A; border:0; border-radius:12px; padding:12px 14px; min-height:46px; cursor:pointer; }
button:active { transform:scale(.97); }
button.accent { background:var(--accent); }
button.danger { background:var(--red); }
.row { display:flex; gap:10px; align-items:center; }
.row > * { flex:1; }
.playing { display:flex; align-items:center; gap:12px; margin-bottom:10px; }
.playing .name { font-weight:600; font-size:16px; flex:1; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
.empty { color:var(--sub); }
input[type=range] { width:100%; accent-color:var(--accent); height:32px; }
#vol { font-weight:600; min-width:64px; text-align:center; flex:0 0 auto; }
#search { width:100%; font:inherit; color:var(--text); background:#0F1420; border:1px solid var(--line); border-radius:12px; padding:12px 14px; margin-bottom:12px; }
.grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(100px,1fr)); gap:10px; }
.game { position:relative; aspect-ratio:2/3; border-radius:12px; overflow:hidden; background:#1E2533; display:flex; align-items:flex-end; padding:0; min-height:0; }
.game img { position:absolute; inset:0; width:100%; height:100%; object-fit:cover; }
.game span { position:relative; width:100%; padding:20px 8px 8px; font-size:12px; text-align:left; background:linear-gradient(transparent,rgba(0,0,0,.85)); }
.game.run::after { content:"▶"; position:absolute; top:6px; right:6px; background:var(--green); color:#000; border-radius:8px; padding:2px 6px; font-size:11px; }
#msg { position:fixed; left:16px; right:16px; bottom:calc(16px + env(safe-area-inset-bottom)); background:#111827; border:1px solid var(--line); border-radius:12px; padding:12px 14px; display:none; }
.hint { color:var(--sub); text-align:center; padding:40px 10px; }
</style>
</head>
<body>
<header><span class="dot" id="dot"></span><b>DFP Pro Launcher</b></header>
<div id="nokey" class="hint" style="display:none"></div>
<div id="app" style="display:none">
  <div class="card">
    <h2 id="tPlaying"></h2>
    <div id="running"></div>
  </div>
  <div class="card">
    <h2 id="tVolume"></h2>
    <div class="row">
      <button id="volDown" style="flex:0 0 56px">−</button>
      <div id="vol">–</div>
      <button id="volUp" style="flex:0 0 56px">+</button>
      <button id="mute"></button>
    </div>
    <input type="range" id="volSlider" min="0" max="100" step="1">
  </div>
  <div class="card">
    <h2 id="tPc"></h2>
    <div class="row" style="align-items:stretch">
      <button id="lock"></button>
      <button id="sleep"></button>
      <button id="shutdown" class="danger"></button>
    </div>
  </div>
  <div class="card">
    <h2 id="tLibrary"></h2>
    <input id="search" type="search" autocomplete="off">
    <div class="grid" id="games"></div>
  </div>
</div>
<div id="msg"></div>
<script>
const S = {{STRINGS}};
const T = k => S[k] || k;
const $ = id => document.getElementById(id);
let token = null;
try {
  const m = location.hash.match(/t=([A-Za-z0-9_-]+)/);
  if (m) { localStorage.setItem("dfpToken", m[1]); history.replaceState(null, "", location.pathname); }
  token = localStorage.getItem("dfpToken");
} catch (e) {
  const m = location.hash.match(/t=([A-Za-z0-9_-]+)/);
  if (m) token = m[1];
}

function msg(text) {
  const el = $("msg"); el.textContent = text; el.style.display = "block";
  clearTimeout(msg.timer); msg.timer = setTimeout(() => el.style.display = "none", 3000);
}

function noKey(text) {
  $("app").style.display = "none"; $("nokey").style.display = "block"; $("nokey").textContent = text;
  $("dot").className = "dot";
}

async function api(path, body) {
  const r = await fetch("/api/" + path, {
    method: body === undefined ? "GET" : "POST",
    headers: { "X-Remote-Token": token || "", "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
    cache: "no-store"
  });
  if (r.status === 401) {
    try { localStorage.removeItem("dfpToken"); } catch (e) {}
    token = null; noKey(T("Der Schlüssel ist nicht mehr gültig. Bitte scanne den QR-Code im Launcher neu."));
    throw new Error("401");
  }
  if (!r.ok) throw new Error(String(r.status));
  return r;
}

let state = { running: [], volume: 0, muted: false };
function renderState() {
  const box = $("running"); box.innerHTML = "";
  if (!state.running.length) {
    const p = document.createElement("div"); p.className = "empty"; p.textContent = T("Gerade läuft kein Spiel."); box.appendChild(p);
  }
  for (const g of state.running) {
    const row = document.createElement("div"); row.className = "playing";
    const n = document.createElement("div"); n.className = "name"; n.textContent = g.name;
    const b = document.createElement("button"); b.className = "danger"; b.textContent = T("Beenden");
    b.onclick = async () => {
      if (!confirm(T("Spiel beenden? Nicht gespeicherter Fortschritt geht verloren.") + "\n\n" + g.name)) return;
      try { await api("end", { id: g.id }); msg(T("Spiel wird beendet ...")); } catch (e) { fail(e); }
    };
    row.append(n, b); box.appendChild(row);
  }
  $("vol").textContent = state.muted ? "🔇" : state.volume + " %";
  if (!sliding) $("volSlider").value = state.volume;
  $("mute").textContent = state.muted ? T("Ton an") : T("Stumm");
  const running = new Set(state.running.map(g => g.id));
  document.querySelectorAll(".game").forEach(el => el.classList.toggle("run", running.has(el.dataset.id)));
}

function fail(e) { if (String(e.message) !== "401") msg(T("Der PC antwortet nicht.")); }

async function refresh() {
  if (!token || document.hidden) return;
  try {
    state = await (await api("state")).json();
    $("dot").className = "dot on";
    renderState();
  } catch (e) { if (String(e.message) !== "401") $("dot").className = "dot"; }
}

let games = [];
const covers = new Map();
const io = "IntersectionObserver" in window ? new IntersectionObserver(entries => {
  for (const en of entries) if (en.isIntersecting) { io.unobserve(en.target); loadCover(en.target); }
}, { rootMargin: "300px" }) : null;

async function loadCover(el) {
  const id = el.dataset.id;
  try {
    let url = covers.get(id);
    if (!url) { url = URL.createObjectURL(await (await api("cover?id=" + encodeURIComponent(id))).blob()); covers.set(id, url); }
    const img = document.createElement("img"); img.alt = ""; img.src = url; el.prepend(img);
  } catch (e) {}
}

function renderGames() {
  const q = $("search").value.trim().toLowerCase();
  const box = $("games"); box.innerHTML = "";
  const list = games.filter(g => !q || g.name.toLowerCase().includes(q));
  for (const g of list) {
    const b = document.createElement("button"); b.className = "game"; b.dataset.id = g.id;
    const s = document.createElement("span"); s.textContent = g.name; b.appendChild(s);
    b.onclick = async () => {
      if (!confirm(T("Dieses Spiel starten?") + "\n\n" + g.name)) return;
      try { await api("launch", { id: g.id }); msg(T("Spiel wird gestartet ...")); setTimeout(refresh, 4000); } catch (e) { fail(e); }
    };
    box.appendChild(b);
    if (g.cover) { if (io) io.observe(b); else loadCover(b); }
  }
  if (!list.length) { const p = document.createElement("div"); p.className = "empty"; p.textContent = T("Keine Treffer."); box.appendChild(p); }
  renderState();
}

let sliding = false, volTimer = null;
function setVolume(level) {
  state.volume = Math.max(0, Math.min(100, level)); state.muted = false; renderState();
  clearTimeout(volTimer); volTimer = setTimeout(() => api("volume", { level: state.volume }).catch(fail), 120);
}

async function start() {
  if (!token) { noKey(T("Scanne den QR-Code im Launcher: Einstellungen, Steuerung, Handy-Fernbedienung.")); return; }
  $("nokey").style.display = "none"; $("app").style.display = "block";
  $("tPlaying").textContent = T("Läuft gerade");
  $("tVolume").textContent = T("Lautstärke");
  $("tPc").textContent = T("PC");
  $("tLibrary").textContent = T("Bibliothek");
  $("search").placeholder = T("Spiel suchen...");
  $("lock").textContent = T("Sperren");
  $("sleep").textContent = T("Energie sparen");
  $("shutdown").textContent = T("Herunterfahren");

  $("volDown").onclick = () => setVolume(state.volume - 5);
  $("volUp").onclick = () => setVolume(state.volume + 5);
  $("volSlider").oninput = e => { sliding = true; setVolume(+e.target.value); };
  $("volSlider").onchange = () => sliding = false;
  $("mute").onclick = () => { state.muted = !state.muted; renderState(); api("volume", { mute: state.muted }).catch(fail); };
  $("lock").onclick = () => api("power", { kind: "lock" }).then(() => msg(T("PC wird gesperrt."))).catch(fail);
  $("sleep").onclick = () => { if (confirm(T("PC in den Energiesparmodus schicken?"))) api("power", { kind: "sleep" }).catch(fail); };
  $("shutdown").onclick = () => {
    const extra = state.running.length ? "\n\n" + T("Es läuft noch ein Spiel.") : "";
    if (confirm(T("PC jetzt herunterfahren?") + extra)) api("power", { kind: "shutdown" }).then(() => msg(T("PC fährt herunter ..."))).catch(fail);
  };
  $("search").oninput = renderGames;

  try { games = await (await api("games")).json(); renderGames(); } catch (e) { fail(e); }
  refresh();
  setInterval(refresh, 4000);
  document.addEventListener("visibilitychange", refresh);
}
start();
</script>
</body>
</html>
""";
    }
}
