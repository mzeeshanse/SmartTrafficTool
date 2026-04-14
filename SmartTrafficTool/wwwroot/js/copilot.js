(function () {
  const shell = document.getElementById("copilot-shell");
  if (!shell) return;

  const apiUrl = shell.dataset.apiUrl || "/api/Copilot/message";
  const fab = document.getElementById("copilot-fab");
  const panel = document.getElementById("copilot-panel");
  const backdrop = document.getElementById("copilot-backdrop");
  const closeBtn = panel && panel.querySelector(".copilot-close");
  const messagesEl = document.getElementById("copilot-messages");
  const input = document.getElementById("copilot-input");
  const sendBtn = document.getElementById("copilot-send");
  const micBtn = document.getElementById("copilot-mic");
  const statusEl = document.getElementById("copilot-status");

  const chartInstances = new Map();

  let voiceInputNext = false;
  let recognition = null;

  function escapeHtml(text) {
    const d = document.createElement("div");
    d.textContent = text;
    return d.innerHTML;
  }

  function formatReply(text) {
    if (!text) return "";
    const escaped = escapeHtml(text);
    return escaped.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
  }

  function stripMarkdownForSpeech(text) {
    return (text || "").replace(/\*\*([^*]+)\*\*/g, "$1");
  }

  function speak(text) {
    if (!text || !window.speechSynthesis) return;
    window.speechSynthesis.cancel();
    const u = new SpeechSynthesisUtterance(stripMarkdownForSpeech(text));
    u.rate = 1;
    window.speechSynthesis.speak(u);
  }

  function setOpen(open) {
    if (!panel || !fab) return;
    panel.hidden = !open;
    if (backdrop) backdrop.hidden = !open;
    fab.setAttribute("aria-expanded", open ? "true" : "false");
    if (open) {
      input && input.focus();
    } else {
      window.speechSynthesis && window.speechSynthesis.cancel();
    }
  }

  function destroyChartsIn(container) {
    if (!container) return;
    container.querySelectorAll("canvas[data-copilot-chart-id]").forEach(function (cv) {
      const id = cv.getAttribute("data-copilot-chart-id");
      if (id && chartInstances.has(id)) {
        chartInstances.get(id).destroy();
        chartInstances.delete(id);
      }
    });
  }

  function clearCopilotThread() {
    if (messagesEl) destroyChartsIn(messagesEl);
    if (messagesEl) messagesEl.innerHTML = "";
    if (window.speechSynthesis) window.speechSynthesis.cancel();
    setStatus("");
  }

  function applyQuickPickPhrase(phrase) {
    clearCopilotThread();
    if (input) {
      input.value = phrase;
      input.focus();
      try {
        input.setSelectionRange(phrase.length, phrase.length);
      } catch (_) {
        /* ignore */
      }
    }
  }

  function renderWidgets(container, widgets) {
    if (!widgets || !widgets.length) return;
    destroyChartsIn(container);

    const wrap = document.createElement("div");
    wrap.className = "copilot-widgets";

    widgets.forEach(function (w, idx) {
      const type = (w.type || "").toLowerCase();
      const block = document.createElement("div");
      block.className = "copilot-widget";

      if (w.title) {
        const t = document.createElement("div");
        t.className = "copilot-widget-title";
        t.textContent = w.title;
        block.appendChild(t);
      }

      if (type === "cards" && w.cards && w.cards.length) {
        const grid = document.createElement("div");
        grid.className = "copilot-cards";
        w.cards.forEach(function (c) {
          const card = document.createElement("div");
          card.className = "copilot-card";
          card.innerHTML =
            '<div class="copilot-card-label"></div><div class="copilot-card-value"></div>' +
            (c.hint ? '<div class="copilot-card-hint"></div>' : "");
          card.querySelector(".copilot-card-label").textContent = c.label || "";
          card.querySelector(".copilot-card-value").textContent = c.value || "";
          const hint = card.querySelector(".copilot-card-hint");
          if (hint) hint.textContent = c.hint || "";
          grid.appendChild(card);
        });
        block.appendChild(grid);
      } else if (type === "table" && w.table) {
        const tw = document.createElement("div");
        tw.className = "copilot-table-wrap";
        const table = document.createElement("table");
        table.className = "table table-sm table-dark copilot-table mb-0";
        const thead = document.createElement("thead");
        const trh = document.createElement("tr");
        (w.table.headers || []).forEach(function (h) {
          const th = document.createElement("th");
          th.scope = "col";
          th.textContent = h;
          trh.appendChild(th);
        });
        thead.appendChild(trh);
        table.appendChild(thead);
        const tbody = document.createElement("tbody");
        (w.table.rows || []).forEach(function (row) {
          const tr = document.createElement("tr");
          (row || []).forEach(function (cell) {
            const td = document.createElement("td");
            td.textContent = cell;
            tr.appendChild(td);
          });
          tbody.appendChild(tr);
        });
        table.appendChild(tbody);
        tw.appendChild(table);
        block.appendChild(tw);
      } else if (type === "chart" && w.chart && typeof Chart !== "undefined") {
        const labels = w.chart.labels || [];
        const data = w.chart.data || [];
        if (!labels.length || !data.length) {
          block.appendChild(document.createComment("empty chart"));
        } else {
          const cw = document.createElement("div");
          cw.className = "copilot-chart-wrap";
          const canvas = document.createElement("canvas");
          const chartId = "copilot-chart-" + Date.now() + "-" + idx;
          canvas.setAttribute("data-copilot-chart-id", chartId);
          cw.appendChild(canvas);
          block.appendChild(cw);

          const colors = w.chart.colors && w.chart.colors.length
            ? w.chart.colors
            : ["#56ccf2", "#f2c94c", "#bb6bd9", "#27ae60", "#eb5757", "#8fa1bd"];

          const kind = (w.chart.kind || "doughnut").toLowerCase();
          const cfg = {
            type: kind === "bar" ? "bar" : "doughnut",
            data: {
              labels: labels,
              datasets: [
                {
                  data: data,
                  backgroundColor: colors.slice(0, labels.length),
                  borderWidth: 0
                }
              ]
            },
            options: {
              responsive: true,
              maintainAspectRatio: false,
              plugins: {
                legend: {
                  position: "bottom",
                  labels: { color: "#c5d0e0", boxWidth: 10, font: { size: 10 } }
                }
              }
            }
          };
          if (cfg.type === "bar") {
            cfg.options.scales = {
              x: { ticks: { color: "#8fa1bd" }, grid: { color: "rgba(255,255,255,0.06)" } },
              y: { ticks: { color: "#8fa1bd" }, grid: { color: "rgba(255,255,255,0.06)" } }
            };
          }

          const chart = new Chart(canvas.getContext("2d"), cfg);
          chartInstances.set(chartId, chart);
        }
      } else if (type === "note" && w.note) {
        const n = document.createElement("div");
        n.className = "copilot-note";
        n.textContent = w.note;
        block.appendChild(n);
      }

      wrap.appendChild(block);
    });

    container.appendChild(wrap);
  }

  function appendUserMessage(text) {
    const div = document.createElement("div");
    div.className = "copilot-msg copilot-msg--user";
    div.textContent = text;
    messagesEl.appendChild(div);
    messagesEl.scrollTop = messagesEl.scrollHeight;
  }

  function appendAssistantMessage(reply, actions, widgets) {
    const div = document.createElement("div");
    div.className = "copilot-msg copilot-msg--assistant";

    const replyEl = document.createElement("div");
    replyEl.className = "copilot-reply";
    replyEl.innerHTML = formatReply(reply);
    div.appendChild(replyEl);

    if (actions && actions.length) {
      const act = document.createElement("div");
      act.className = "copilot-actions";
      actions.forEach(function (a) {
        if (!a.href) return;
        const btn = document.createElement("a");
        btn.className = "btn btn-sm " + (a.primary ? "btn-accent" : "btn-outline-light");
        btn.href = a.href;
        btn.textContent = a.label || "Open";
        btn.target = "_blank";
        btn.rel = "noopener noreferrer";
        btn.setAttribute("data-copilot-nav", "1");
        act.appendChild(btn);
      });
      div.appendChild(act);
    }

    renderWidgets(div, widgets);
    messagesEl.appendChild(div);
    messagesEl.scrollTop = messagesEl.scrollHeight;
  }

  function setStatus(msg) {
    if (statusEl) statusEl.textContent = msg || "";
  }

  async function sendMessage(overrideText) {
    const fromOverride =
      overrideText !== undefined && overrideText !== null && String(overrideText).trim() !== "";
    const text = fromOverride
      ? String(overrideText).trim()
      : (input && input.value ? input.value.trim() : "");
    if (!text) return;

    const useVoice = voiceInputNext;
    voiceInputNext = false;

    appendUserMessage(text);
    if (input) input.value = "";
    setStatus("Thinking…");
    if (sendBtn) sendBtn.disabled = true;

    try {
      const res = await fetch(apiUrl, {
        method: "POST",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        body: JSON.stringify({ message: text, voiceInput: useVoice })
      });

      if (!res.ok) {
        const err = await res.text();
        throw new Error(err || res.statusText);
      }

      const data = await res.json();
      appendAssistantMessage(data.reply || "", data.actions || [], data.widgets || []);

      if (data.speak && data.reply) {
        speak(data.reply);
      }
    } catch (e) {
      appendAssistantMessage(
        "I could not reach the Co-Pilot service. Check the network or try again.",
        [],
        []
      );
      console.error(e);
    } finally {
      setStatus("");
      if (sendBtn) sendBtn.disabled = false;
      if (input) input.focus();
    }
  }

  function initSpeechRecognition() {
    const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!SR || !micBtn) return;
    recognition = new SR();
    recognition.continuous = false;
    recognition.interimResults = false;
    recognition.lang = "en-US";

    recognition.onresult = function (ev) {
      const t = ev.results && ev.results[0] && ev.results[0][0] && ev.results[0][0].transcript;
      if (t && input) {
        input.value = (input.value ? input.value + " " : "") + t.trim();
        voiceInputNext = true;
      }
      micBtn.classList.remove("copilot-mic-listening");
    };

    recognition.onerror = function () {
      micBtn.classList.remove("copilot-mic-listening");
      setStatus("Voice input unavailable.");
    };

    recognition.onend = function () {
      micBtn.classList.remove("copilot-mic-listening");
    };

    micBtn.addEventListener("click", function () {
      try {
        micBtn.classList.add("copilot-mic-listening");
        setStatus("Listening…");
        recognition.start();
      } catch {
        micBtn.classList.remove("copilot-mic-listening");
        setStatus("Microphone busy — try again.");
      }
    });
  }

  fab.addEventListener("click", function () {
    setOpen(panel.hidden);
  });

  if (closeBtn) closeBtn.addEventListener("click", function () { setOpen(false); });
  if (backdrop) backdrop.addEventListener("click", function () { setOpen(false); });

  if (sendBtn) sendBtn.addEventListener("click", function () { sendMessage(); });
  if (shell) {
    shell.addEventListener("click", function (e) {
      const btn = e.target && e.target.closest && e.target.closest(".copilot-quick-pick[data-copilot-message]");
      if (!btn || !shell.contains(btn)) return;
      const msg = btn.getAttribute("data-copilot-message");
      if (msg) applyQuickPickPhrase(msg);
    });
  }
  if (input) {
    input.addEventListener("keydown", function (e) {
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        sendMessage();
      }
    });
  }

  document.addEventListener("keydown", function (e) {
    if (e.key === "Escape" && panel && !panel.hidden) {
      setOpen(false);
    }
  });

  initSpeechRecognition();

  const welcome =
    "**Co-Pilot ready.** **Quick** chips clear the thread, fill the box below — edit if you like, then **Send**. Or type: **insights**, **open all devices**, **summary for 7 days**, **summary of plate with text …** (or **search plate with text …** — same insight), **investigate plate number …**.";
  appendAssistantMessage(welcome, [], []);
})();
