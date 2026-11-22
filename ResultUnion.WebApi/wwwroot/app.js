// A plain-JavaScript client for ResultUnion.WebApi. The interesting part is showProblem(): it reads the RFC 9457
// problem response (status, detail, code, arguments, errors, details) and puts each error next to its input.

"use strict";

let language = "en";

// ---- HTTP ------------------------------------------------------------------------------------------------------

async function send(method, url, body, headers = {}) {
  const init = {
    method,
    headers: { "Accept-Language": language, ...headers },
  };
  if (body !== undefined) {
    init.headers["Content-Type"] = "application/json";
    init.body = JSON.stringify(body);
  }

  const response = await fetch(url, init);
  const text = await response.text();
  const json = text ? JSON.parse(text) : null;

  showInInspector(method, url, response, json);
  return { ok: response.ok, status: response.status, json };
}

function showInInspector(method, url, response, json) {
  document.getElementById("last-request").textContent =
    `${method} ${url}  →  ${response.status} ${response.statusText}`;
  document.getElementById("last-response").textContent =
    json === null ? "(no body)" : JSON.stringify(json, null, 2);
}

// ---- Rendering ---------------------------------------------------------------------------------------------------

function clear(form) {
  form.querySelectorAll(".field-error").forEach(element => (element.textContent = ""));
  form.querySelectorAll(".invalid").forEach(element => element.classList.remove("invalid"));
  form.querySelector(".outcome").replaceChildren();
}

function element(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function showSuccess(form, title, lines = []) {
  const box = element("div", "box success");
  box.append(element("strong", null, title));
  for (const line of lines) box.append(element("div", null, line));
  form.querySelector(".outcome").replaceChildren(box);
}

// Puts each field error under its input; whatever has no matching input is listed in the summary box.
function showProblem(form, problem) {
  const unplaced = [];

  for (const [field, messages] of Object.entries(problem.errors ?? {})) {
    const target = findErrorTarget(form, field);
    if (target) {
      target.textContent = messages.join(" ");
      markInvalid(form, field);
    } else {
      unplaced.push(`${field || "(form)"}: ${messages.join(" ")}`);
    }
  }

  const box = element("div", "box problem");
  const heading = element("div", "problem-head");
  heading.append(element("span", "status", String(problem.status)), element("strong", null, problem.title ?? "Error"));
  box.append(heading);

  // A validation group's detail repeats the title; show it only when it says something more.
  if (problem.detail && problem.detail !== problem.title) box.append(element("div", null, problem.detail));

  const meta = element("div", "meta");
  meta.append(element("code", null, problem.code ?? "?"));
  for (const [name, value] of Object.entries(problem.arguments ?? {})) meta.append(element("span", "chip", `${name} = ${value}`));
  box.append(meta);

  for (const line of unplaced) box.append(element("div", "unplaced", line));

  form.querySelector(".outcome").replaceChildren(box);
  return box;
}

// "email" → the form's email error slot; "lines[1].quantity" → the quantity slot of the second line.
function findErrorTarget(form, field) {
  const line = /^lines\[(\d+)\]\.(\w+)$/.exec(field);
  if (line) {
    const row = form.querySelectorAll(".line")[Number(line[1])];
    return row?.querySelector(`[data-part-error="${line[2]}"]`) ?? null;
  }
  return form.querySelector(`[data-error-for="${CSS.escape(field)}"]`);
}

function markInvalid(form, field) {
  const line = /^lines\[(\d+)\]\.(\w+)$/.exec(field);
  const input = line
    ? form.querySelectorAll(".line")[Number(line[1])]?.querySelector(`[data-part="${line[2]}"]`)
    : form.querySelector(`[name="${CSS.escape(field)}"]`);
  input?.classList.add("invalid");
}

async function guarded(form, work) {
  clear(form);
  form.querySelectorAll("button").forEach(button => (button.disabled = true));
  try {
    await work();
  } catch (error) {
    form.querySelector(".outcome").replaceChildren(element("div", "box problem", `Network error: ${error.message}`));
  } finally {
    form.querySelectorAll("button:not([data-locked])").forEach(button => (button.disabled = false));
  }
}

// ---- Register ------------------------------------------------------------------------------------------------------

document.getElementById("register").addEventListener("submit", event => {
  event.preventDefault();
  const form = event.currentTarget;
  const data = Object.fromEntries(new FormData(form));

  void guarded(form, async () => {  // guarded catches and shows its own errors
    const { ok, json } = await send("POST", "/users", data);
    if (ok) showSuccess(form, "Registered", [`${json.email}, age ${json.age}`, `id ${json.id}`]);
    else showProblem(form, json);
  });
});

// ---- Find / deactivate / reset ------------------------------------------------------------------------------------

document.getElementById("find").addEventListener("submit", event => {
  event.preventDefault();
  const form = event.currentTarget;
  const action = event.submitter?.dataset.action ?? "find";
  const email = encodeURIComponent(form.elements.email.value.trim());

  void guarded(form, async () => {  // guarded catches and shows its own errors
    if (action === "find") {
      const { ok, json } = await send("GET", `/users/${email}`);
      if (ok) showSuccess(form, json.email, [`age ${json.age}`, json.isActive ? "active" : "inactive"]);
      else showProblem(form, json);
    } else if (action === "deactivate") {
      const { ok, json } = await send("DELETE", `/users/${email}`);
      if (ok) showSuccess(form, "Deactivated (204 No Content)");
      else showProblem(form, json);
    } else {
      const { ok, json } = await send("POST", `/users/${email}/password-reset`);
      if (ok) showSuccess(form, "Reset link sent (202 Accepted)");
      else {
        const box = showProblem(form, json);
        if (json.status === 429) startCountdown(form, box, Number(json.arguments?.retryAfterSeconds ?? 0));
      }
    }
  });
});

// A rate limit tells the client how long to wait (arguments.retryAfterSeconds): lock the button until then.
function startCountdown(form, box, seconds) {
  if (!seconds) return;
  const button = form.querySelector('[data-action="reset"]');
  const label = button.textContent;
  const note = element("div", "countdown");
  box.append(note);
  button.dataset.locked = "true";

  const tick = () => {
    if (seconds <= 0) {
      delete button.dataset.locked;
      button.disabled = false;
      button.textContent = label;
      note.textContent = "You can try again now.";
      return;
    }
    button.disabled = true;
    button.textContent = `${label} (${seconds}s)`;
    note.textContent = `Retry in ${seconds} s.`;
    seconds--;
    setTimeout(tick, 1000);
  };
  tick();
}

// ---- Orders --------------------------------------------------------------------------------------------------------

const lines = document.getElementById("lines");

function addLine(product = "", quantity = "") {
  const row = document.getElementById("line-template").content.firstElementChild.cloneNode(true);
  row.querySelector('[data-part="product"]').value = product;
  row.querySelector('[data-part="quantity"]').value = quantity;
  row.querySelector("[data-remove]").addEventListener("click", () => row.remove());
  lines.append(row);
}

document.getElementById("add-line").addEventListener("click", () => addLine());

document.getElementById("order").addEventListener("submit", event => {
  event.preventDefault();
  const form = event.currentTarget;
  const body = {
    customerEmail: form.elements.customerEmail.value,
    lines: [...lines.querySelectorAll(".line")].map(row => ({
      product: row.querySelector('[data-part="product"]').value,
      quantity: Number.parseInt(row.querySelector('[data-part="quantity"]').value, 10) || 0,
    })),
  };

  void guarded(form, async () => {  // guarded catches and shows its own errors
    const { ok, json } = await send("POST", "/orders", body);
    if (ok) showSuccess(form, `Order placed: ${json.totalQuantity} items`, json.lines.map(line => `${line.quantity} × ${line.product}`));
    else showProblem(form, json);
  });
});

// ---- Admin ---------------------------------------------------------------------------------------------------------

document.getElementById("admin").addEventListener("submit", event => {
  event.preventDefault();
  const form = event.currentTarget;
  const role = form.elements.role.value;

  void guarded(form, async () => {  // guarded catches and shows its own errors
    const { ok, json } = await send("GET", "/admin/users", undefined, role ? { "X-Role": role } : {});
    if (!ok) {
      showProblem(form, json);
      return;
    }

    const table = element("table", "users");
    const head = element("tr");
    for (const title of ["Email", "Age", "Status"]) head.append(element("th", null, title));
    table.append(head);
    for (const user of json) {
      const row = element("tr");
      row.append(element("td", null, user.email), element("td", null, String(user.age)), element("td", null, user.isActive ? "active" : "inactive"));
      table.append(row);
    }
    form.querySelector(".outcome").replaceChildren(table);
  });
});

// ---- Language ------------------------------------------------------------------------------------------------------

document.querySelectorAll("[data-lang]").forEach(button =>
  button.addEventListener("click", () => {
    language = button.dataset.lang;
    document.querySelectorAll("[data-lang]").forEach(other => other.setAttribute("aria-pressed", String(other === button)));
  }));

// Start with one valid and one invalid line, so "Place order" shows per-line errors right away.
addLine("apple", "2");
addLine("", "0");
