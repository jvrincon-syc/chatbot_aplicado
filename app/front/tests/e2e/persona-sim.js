async function(page) {
  const TIMEOUT_MS = 120000;

  async function simulate(name, role, question) {
    // Reload to reset state between personas
    await page.goto("http://localhost:5173/");
    await page.waitForTimeout(1500);
    // Ensure auth
    await page.evaluate(() => {
      if (!localStorage.getItem("sst_chatbot_user")) {
        localStorage.setItem("sst_chatbot_user", JSON.stringify({name:"Tester",email:"test@test.com"}));
      }
    });

    const input = page.locator("#chat-input");
    await input.fill(question);
    await page.waitForTimeout(300);

    const t0 = Date.now();
    await page.locator('button[aria-label="Enviar"]').click();

    const typing = page.locator(".bubble--typing");
    try { await typing.waitFor({ state: "visible", timeout: 8000 }); } catch {}

    try {
      await typing.waitFor({ state: "hidden", timeout: TIMEOUT_MS });
    } catch {
      return { name, role, ms: Date.now() - t0, ok: false, answer: "TIMEOUT" };
    }

    const ms = Date.now() - t0;
    const bubbles = page.locator(".bubble--assistant .bubble__text");
    const count = await bubbles.count();
    const answer = count > 0 ? (await bubbles.last().textContent()) || "" : "";
    return { name, role, ms, ok: answer.trim().length > 10, answer: answer.trim().slice(0, 200) };
  }

  const personas = [
    { name: "Carlos Mendoza", role: "Ingeniero de Seguridad", q: "¿Qué es el Anexo Técnico de Seguridad y por qué es obligatorio?" },
    { name: "Maria F. Lopez", role: "Operaria de manufactura", q: "¿Cuáles son los EPP obligatorios en una planta industrial?" },
    { name: "Andres F. Ramirez", role: "Gerente de RRHH", q: "¿Cada cuánto se debe realizar la capacitación de SST?" },
    { name: "Diana C. Munoz", role: "Supervisora de planta", q: "¿Qué protocolo de emergencia se sigue ante un incendio?" },
    { name: "Juan E. Gutierrez", role: "Aprendiz nuevo", q: "¿Qué es la inducción de SST y qué temas debe cubrir?" },
    { name: "Patricia V. Solano", role: "Presidenta COPASST", q: "¿Cuáles son las funciones principales del COPASST?" },
  ];

  const results = [];
  for (const p of personas) {
    const r = await simulate(p.name, p.role, p.q);
    results.push(r);
    console.log(`[${r.ok ? "PASS" : "FAIL"}] ${r.name} (${r.role}) — ${r.ms}ms`);
    console.log(`  Answer: ${r.answer.slice(0, 120)}`);
  }

  console.log("\n=== SUMMARY ===");
  for (const r of results) {
    console.log(`${r.ok ? "✓" : "✗"} ${r.name.padEnd(26)} ${r.role.padEnd(26)} ${String(r.ms).padStart(7)}ms`);
  }
  const passed = results.filter((r) => r.ok).length;
  console.log(`Passed: ${passed}/${results.length}`);
}
