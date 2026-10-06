(() => {
  "use strict";

  const session = window.CvTailorSession;
  const APP_URL = "../app.html#vault";
  const MIN_PASSWORD = 8;

  function showMessage(text, type="success") { const box=document.querySelector("#ct-auth-message"); if(!box)return; box.textContent=text; box.className=`ct-auth-message is-visible ${type === "success" ? "is-success" : "is-error"}`; }

  // Hata olursa butonu eski yazısına döndürebilmek için orijinal metni sakla.
  function setLoading(form, text="Açılıyor...") { const button=form?.querySelector("button[type=submit]"); if(!button)return; button.dataset.ctLabel ??= button.textContent; button.disabled=true; button.textContent=text; }
  function resetLoading(form) { const button=form?.querySelector("button[type=submit]"); if(!button)return; button.disabled=false; if(button.dataset.ctLabel)button.textContent=button.dataset.ctLabel; }
  function navigate(form, href, message) { if(message)showMessage(message,"success"); setLoading(form); setTimeout(()=>window.location.href=href,420); }
  const valueOf = selector => document.querySelector(selector)?.value.trim() || "";
  const params = new URLSearchParams(location.search);

  // Önceki sayfadan bırakılan mesaj (örn. şifre güncellendi).
  const flash = session?.takeFlash();
  if (flash?.message) showMessage(flash.message, flash.type);

  // Zaten girişliyse giriş/kayıt sayfasında durmasın.
  if (session?.isSignedIn() && document.querySelector("#ct-login-form, #ct-register-form")) window.location.replace(APP_URL);

  // Doğrulama ve şifre onayı sayfaları oturum istiyor.
  if (document.querySelector("#ct-verify-form, #ct-confirm-form") && !session?.isSignedIn()) {
    session?.setFlash("Devam etmek için giriş yap.", "error");
    window.location.replace("sign-in.html");
  }

  document.querySelectorAll("[data-ct-password-toggle]").forEach(button => button.addEventListener("click", () => { const input=document.querySelector(`#${button.dataset.ctPasswordToggle}`); if(!input)return; input.type=input.type==="password"?"text":"password"; button.setAttribute("aria-label", input.type==="password"?"Şifreyi göster":"Şifreyi gizle"); }));

  // Kod kutuları: yazınca ileri, silince geri, yapıştırınca hepsini doldur.
  const otpInputs=[...document.querySelectorAll("[data-ct-otp]")];
  otpInputs.forEach((input,index)=>{
    input.addEventListener("input",()=>{input.value=input.value.replace(/\D/g,"").slice(0,1);if(input.value&&otpInputs[index+1])otpInputs[index+1].focus();});
    input.addEventListener("keydown",event=>{if(event.key==="Backspace"&&!input.value&&otpInputs[index-1])otpInputs[index-1].focus();});
    input.addEventListener("paste",event=>{const digits=(event.clipboardData?.getData("text")||"").replace(/\D/g,"").slice(0,6);if(!digits)return;event.preventDefault();digits.split("").forEach((d,i)=>{if(otpInputs[i])otpInputs[i].value=d;});otpInputs[Math.min(digits.length,6)-1]?.focus();});
  });
  const otpCode = () => otpInputs.map(v=>v.value).join("");
  otpInputs[0]?.focus();

  // Giriş
  document.querySelector("#ct-login-form")?.addEventListener("submit", async event => {
    event.preventDefault();
    const form = event.currentTarget;
    const email = valueOf("#ct-login-user");
    const password = document.querySelector("#ct-login-password")?.value || "";
    if (!email || !password) { showMessage("E-posta ve şifre gerekli.", "error"); return; }
    const remember = document.querySelector("#ct-login-remember")?.checked ?? true;

    setLoading(form, "Kontrol ediliyor...");
    const { ok, data } = await session.api.post("/api/auth/login", { email, password, rememberMe: remember });
    if (!ok) { showMessage(data?.message || "Giriş yapılamadı.", "error"); resetLoading(form); return; }

    session.saveSession(data.user, data.expiresAt, remember);
    // Mail doğrulanmamışsa önce doğrulama ekranı, istenirse "Şimdilik atla".
    if (data.user && !data.user.emailConfirmed) {
      navigate(form, "email-verification.html", "Giriş başarılı. Önce e-posta adresini doğrulayalım.");
      return;
    }
    navigate(form, APP_URL, "Giriş başarılı.");
  });

  // Kayıt → mail doğrulama ekranı
  document.querySelector("#ct-register-form")?.addEventListener("submit", async event => {
    event.preventDefault();
    const form = event.currentTarget;
    const displayName = valueOf("#ct-register-name");
    const email = valueOf("#ct-register-email");
    const password = document.querySelector("#ct-register-password")?.value || "";
    if (!email || !password) { showMessage("E-posta ve şifre gerekli.", "error"); return; }
    if (password.length < MIN_PASSWORD) { showMessage(`Şifre en az ${MIN_PASSWORD} karakter olmalı.`, "error"); return; }

    setLoading(form, "Hesap oluşturuluyor...");
    const { ok, data } = await session.api.post("/api/auth/register", { email, password, displayName });
    if (!ok) { showMessage(data?.message || "Hesap oluşturulamadı.", "error"); resetLoading(form); return; }
    session.saveSession(data.user, data.expiresAt, true);
    navigate(form, "email-verification.html", "Hesabın oluşturuldu. E-postana doğrulama kodu gönderdik.");
  });

  // Mail doğrulama
  const verifyForm = document.querySelector("#ct-verify-form");
  verifyForm?.addEventListener("submit", async event => {
    event.preventDefault();
    const code = otpCode();
    if (code.length !== 6) { showMessage("Lütfen 6 haneli kodu gir.", "error"); return; }
    setLoading(verifyForm, "Doğrulanıyor...");
    const { ok, data } = await session.api.post("/api/account/verify-email", { code });
    if (!ok) { showMessage(data?.message || "Kod doğrulanamadı.", "error"); resetLoading(verifyForm); return; }
    const user = session.getUser();
    if (user) session.updateUser({ ...user, emailConfirmed: true });
    navigate(verifyForm, APP_URL, "E-posta adresin doğrulandı.");
  });
  document.querySelector("#ct-verify-resend")?.addEventListener("click", async event => {
    event.preventDefault();
    const { ok, data } = await session.api.post("/api/account/resend-verification");
    showMessage(ok ? (data?.message || "Yeni kod gönderildi.") : (data?.message || "Kod gönderilemedi."), ok ? "success" : "error");
  });

  // Şifremi unuttum
  document.querySelector("#ct-forgot-form")?.addEventListener("submit", async event => {
    event.preventDefault();
    const form = event.currentTarget;
    const email = valueOf("#ct-forgot-email");
    if (!email) { showMessage("E-posta adresini gir.", "error"); return; }
    setLoading(form, "Gönderiliyor...");
    const { ok, data } = await session.api.post("/api/auth/forgot-password", { email });
    resetLoading(form);
    showMessage(ok ? `${data.message} Bağlantı 30 dakika geçerlidir; gelen kutunu ve spam klasörünü kontrol et.` : (data?.message || "İstek gönderilemedi."), ok ? "success" : "error");
  });

  // Yeni şifre (maildeki linkten geliniyor)
  const recoverForm = document.querySelector("#ct-recover-form");
  if (recoverForm && (!params.get("email") || !params.get("token"))) {
    showMessage("Bu sayfa e-postadaki şifre sıfırlama bağlantısıyla açılmalı. Yeni bağlantı isteyebilirsin.", "error");
    recoverForm.querySelector("button[type=submit]").disabled = true;
  }
  recoverForm?.addEventListener("submit", async event => {
    event.preventDefault();
    const p1 = document.querySelector("#ct-recover-password")?.value || "";
    const p2 = document.querySelector("#ct-recover-confirm")?.value || "";
    if (p1.length < MIN_PASSWORD) { showMessage(`Şifre en az ${MIN_PASSWORD} karakter olmalı.`, "error"); return; }
    if (p1 !== p2) { showMessage("Şifre alanları eşleşmiyor.", "error"); return; }
    setLoading(recoverForm, "Güncelleniyor...");
    const { ok, data } = await session.api.post("/api/auth/reset-password", { email: params.get("email"), token: params.get("token"), newPassword: p1 });
    if (!ok) { showMessage(data?.message || "Şifre güncellenemedi.", "error"); resetLoading(recoverForm); return; }
    await session.signOut();
    session.setFlash(data.message);
    navigate(recoverForm, "sign-in.html", data.message);
  });

  // Hesap silme onayı: Hesap ekranından ?action=delete-account ile geliniyor.
  const confirmForm = document.querySelector("#ct-confirm-form");
  const deleting = params.get("action") === "delete-account";
  if (confirmForm) {
    if (!deleting) {
      showMessage("Onaylanacak bir işlem bulunamadı.", "error");
      confirmForm.querySelector("button[type=submit]").disabled = true;
    } else {
      document.querySelector(".ct-auth-title").textContent = "Hesabını kalıcı olarak sil";
      document.querySelector(".ct-auth-subtitle").textContent = "Hesabın, kariyer kasan, ilanların ve ürettiğin bütün CV'ler kalıcı olarak silinecek. Bu işlem geri alınamaz.";
      confirmForm.querySelector("button[type=submit]").textContent = "Doğrula ve hesabı sil";
    }
  }
  confirmForm?.addEventListener("submit", async event => {
    event.preventDefault();
    const password = document.querySelector("#ct-confirm-password")?.value || "";
    if (!password) { showMessage("Şifreni gir.", "error"); return; }
    setLoading(confirmForm, "Siliniyor...");
    const { ok, data } = await session.api.post("/api/account/delete", { password });
    if (!ok) { showMessage(data?.message || "İşlem tamamlanamadı.", "error"); resetLoading(confirmForm); return; }
    session.clearSession();
    session.setFlash(data.message);
    navigate(confirmForm, "sign-in.html", data.message);
  });
})();
