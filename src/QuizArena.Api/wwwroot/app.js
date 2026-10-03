// QuizArena client — vanilla JS talking to the SignalR hub via signalr-mini.js.
const $ = (id) => document.getElementById(id);
const conn = SignalRMini.createConnection("/quizhub");
let myCode = null;
let isHost = false;
let currentQuestion = null;
let timerHandle = null;

function show(id) {
  for (const v of ["lobbyView", "waitingView", "gameView", "finalView"]) $(v).classList.add("hidden");
  $(id).classList.remove("hidden");
}

function renderPlayers(players, highlightName) {
  const ul = $("playerList");
  ul.innerHTML = "";
  for (const p of players) {
    const li = document.createElement("li");
    li.textContent = p.name + (p.name === highlightName ? " (you)" : "");
    ul.appendChild(li);
  }
}

function renderBoard(el, entries) {
  el.innerHTML = "";
  entries.forEach((e, i) => {
    const li = document.createElement("li");
    const medal = i === 0 ? "🥇 " : i === 1 ? "🥈 " : i === 2 ? "🥉 " : "";
    li.textContent = medal + e.name + " — " + e.score + " pts";
    el.appendChild(li);
  });
}

function startTimer(endsAtUtc, totalSeconds) {
  clearInterval(timerHandle);
  const end = new Date(endsAtUtc).getTime();
  timerHandle = setInterval(() => {
    const remaining = Math.max(0, (end - Date.now()) / 1000);
    $("qTimer").textContent = remaining.toFixed(1) + "s";
    $("timerFill").style.width = (100 * remaining / totalSeconds) + "%";
    if (remaining <= 0) clearInterval(timerHandle);
  }, 100);
}

conn.on("LobbyUpdated", (room) => {
  $("waitTitle").textContent = room.name;
  renderPlayers(room.players, null);
});

conn.on("QuestionStarted", (q) => {
  currentQuestion = q;
  show("gameView");
  $("answerMsg").textContent = "";
  $("qCounter").textContent = "Question " + (q.index + 1) + " of " + q.total;
  $("qText").textContent = q.text;
  startTimer(q.endsAtUtc, q.timeLimitSeconds);
  const box = $("options");
  box.innerHTML = "";
  q.options.forEach((text, i) => {
    const btn = document.createElement("button");
    btn.textContent = String.fromCharCode(65 + i) + ". " + text;
    btn.addEventListener("click", async () => {
      box.querySelectorAll("button").forEach((b) => { b.disabled = true; });
      try {
        const res = await conn.invoke("SubmitAnswer", myCode, q.index, i);
        $("answerMsg").textContent = res.accepted
          ? (res.correct ? "Correct! +" + res.pointsAwarded + " pts" : "Wrong — " + res.reason)
          : "Not counted: " + res.reason;
        btn.classList.add(res.correct ? "correct" : "wrong");
      } catch (e) {
        $("answerMsg").textContent = "Error: " + e.message;
      }
    });
    box.appendChild(btn);
  });
});

conn.on("QuestionEnded", (r) => {
  clearInterval(timerHandle);
  const buttons = $("options").querySelectorAll("button");
  buttons.forEach((b, i) => {
    b.disabled = true;
    if (i === r.correctIndex) b.classList.add("correct");
  });
  $("answerMsg").textContent = "Correct answer: " + String.fromCharCode(65 + r.correctIndex);
  renderBoard($("board"), r.leaderboard);
});

conn.on("GameEnded", (room) => {
  clearInterval(timerHandle);
  show("finalView");
  renderBoard($("finalBoard"), room.players);
});

$("createForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  $("lobbyError").textContent = "";
  try {
    myCode = await conn.invoke("CreateRoom", $("roomName").value, $("hostName").value, $("setSelect").value);
    isHost = true;
    $("roomCodeBig").textContent = myCode;
    $("waitTitle").textContent = $("roomName").value;
    $("startBtn").classList.remove("hidden");
    show("waitingView");
  } catch (err) { $("lobbyError").textContent = err.message; }
});

$("joinForm").addEventListener("submit", async (e) => {
  e.preventDefault();
  $("lobbyError").textContent = "";
  try {
    myCode = $("joinCode").value.trim().toUpperCase();
    await conn.invoke("JoinRoom", myCode, $("joinName").value);
    isHost = false;
    $("roomCodeBig").textContent = myCode;
    $("startBtn").classList.add("hidden");
    show("waitingView");
  } catch (err) { $("lobbyError").textContent = err.message; }
});

$("startBtn").addEventListener("click", async () => {
  try { await conn.invoke("StartGame", myCode); }
  catch (err) { $("lobbyError").textContent = err.message; }
});

$("backBtn").addEventListener("click", () => { show("lobbyView"); });

(async function init() {
  try {
    const sets = await (await fetch("/api/questionsets")).json();
    for (const s of sets) {
      const opt = document.createElement("option");
      opt.value = s.id;
      opt.textContent = s.title + " (" + s.questionCount + " questions)";
      $("setSelect").appendChild(opt);
    }
    await conn.start();
    $("connStatus").textContent = "connected";
    $("connStatus").classList.add("ok");
  } catch (err) {
    $("connStatus").textContent = "connection failed: " + err.message;
  }
})();
