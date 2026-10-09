(() => {
  "use strict";

  const session = window.CvTailorSession;
  const $ = selector => document.querySelector(selector);
  const views = [...document.querySelectorAll(".ct-view")];
  const links = [...document.querySelectorAll(".ct-nav a")];

  let toastTimer;
  function toast(message, type = "success") {
    const box = $("#ct-toast");
    box.textContent = message;
    box.className = `ct-toast is-visible ${type === "error" ? "is-error" : ""}`;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => box.classList.remove("is-visible"), 2800);
  }

  // Ekranlar hash ile değişiyor (#vault, #new...); sayfa yenilenince aynı ekranda kalınsın.
  // Parametreli ekranlar "#review:<id>" biçiminde; menüde ait olduğu bölüm (CV'lerim) seçili görünüyor.
  const PARENT = { review: "cvs" };
  function showView() {
    const name = location.hash.slice(1).split(":")[0];
    const target = views.some(v => v.dataset.view === name) ? name : "vault";
    views.forEach(v => { v.hidden = v.dataset.view !== target; });
    links.forEach(a => a.classList.toggle("is-active", a.dataset.view === (PARENT[target] || target)));
  }
  window.addEventListener("hashchange", showView);
  showView();

  function renderMe(me) {
    const { user, usage, ai } = me;
    $("#ct-account-email").textContent = user.email;
    $("#ct-account-verified").textContent = user.emailConfirmed ? "E-posta doğrulandı" : "E-posta doğrulanmadı";
    $("#ct-account-plan").textContent = `${usage.plan === "pro" ? "Pro" : "Ücretsiz"} · ayda ${usage.limit} AI işlemi`;
    $("#ct-account-ai").textContent = ai.enabled ? `${ai.provider} (${ai.model})` : "Tanımlı değil, yerel mod";
    $("#ct-name").value = user.displayName || "";
    $("#ct-verify-banner").hidden = user.emailConfirmed;

    $("#ct-usage").hidden = false;
    $("#ct-usage-text").textContent = `${usage.used} / ${usage.limit}`;
    $("#ct-usage-bar").style.width = `${Math.min(100, Math.round(usage.used / Math.max(usage.limit, 1) * 100))}%`;
  }

  async function loadMe() {
    const { ok, data } = await session.api.get("/api/account/me");
    if (!ok || !data) return;
    session.updateUser(data.user);
    renderMe(data);
  }

  $("#ct-name-form").addEventListener("submit", async event => {
    event.preventDefault();
    const { ok, data } = await session.api.put("/api/account/profile", { displayName: $("#ct-name").value.trim() });
    if (!ok) { toast(data?.message || "Ad kaydedilemedi.", "error"); return; }
    session.updateUser(data);
    toast("Adın kaydedildi.");
  });

  $("#ct-password-form").addEventListener("submit", async event => {
    event.preventDefault();
    const form = event.currentTarget;
    const { ok, data } = await session.api.post("/api/account/change-password", {
      currentPassword: $("#ct-current-password").value,
      newPassword: $("#ct-new-password").value
    });
    toast(data?.message || (ok ? "Şifren güncellendi." : "Şifre değiştirilemedi."), ok ? "success" : "error");
    if (ok) form.reset();
  });

  $("#ct-logout").addEventListener("click", async () => {
    await session.signOut();
    window.location.replace("auth/sign-in.html");
  });

  // Ekran dosyaları (vault.js...) bildirim göstermek ve AI işleminden sonra kotayı tazelemek için kullanıyor.
  window.CvTailorUi = { toast, refreshUsage: loadMe };

  loadMe();
})();
