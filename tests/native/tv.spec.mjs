import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync, writeFileSync } from "node:fs";
import { X509Certificate, createHash } from "node:crypto";
import { _android, chromium } from "playwright";

const app = "pl.wiq1999.digitalsorter.androidtv";
const certificate = new X509Certificate(
  readFileSync(process.env.TEST_CERTIFICATE_PEM),
);
const pin = createHash("sha256")
  .update(certificate.publicKey.export({ type: "spki", format: "der" }))
  .digest("base64");

test(
  "Android TV: native startup, phone approval, SecureStorage after restart and remote navigation",
  { timeout: 300000 },
  async () => {
    const [device] = await _android.devices();
    assert.ok(device, "A booted Android TV device is required");
    device.setDefaultTimeout(60000);
    const browser = await chromium.launch({
      channel: "chrome",
      args: ["--no-sandbox", "--ignore-certificate-errors-spki-list=" + pin],
    });
    let page;
    const errors = [];
    async function attach() {
      const webview = await device.webView({ pkg: app }, { timeout: 90000 });
      const result = await webview.page();
      result.setDefaultTimeout(60000);
      result.on("pageerror", (error) => errors.push(error.message));
      return result;
    }
    async function launch() {
      await device.shell(
        `monkey -p ${app} -c android.intent.category.LEANBACK_LAUNCHER 1`,
      );
    }
    async function next() {
      const counter = await page.getByTestId("counter").innerText();
      await page
        .getByRole("button", { name: "Następny →", exact: true })
        .click();
      await page.waitForFunction(
        (previous) =>
          document.querySelector('[data-testid="counter"]').innerText !==
          previous,
        counter,
      );
    }
    try {
      await device.shell(`pm clear ${app}`);
      await launch();
      await device.wait({ desc: "broker-origin" });
      await device.fill({ desc: "broker-origin" }, "https://10.0.2.2:7443");
      await device.shell("input keyevent KEYCODE_ESCAPE");
      await device.tap({ desc: "open-broker" });
      page = await attach();
      await page.getByRole("button", { name: "Połącz przez telefon" }).click();
      const code = (await page.locator(".pair-code").innerText()).trim();
      assert.match(code, /^[A-Z2-9]{4}-[A-Z2-9]{4}$/);
      const phone = await browser.newPage();
      await phone.goto(process.env.TEST_ORIGIN + "/pair?code=" + code);
      await phone.getByRole("link", { name: "Połącz konto Google" }).click();
      await phone
        .getByRole("button", { name: "Zatwierdź dostęp testowy" })
        .click();
      await phone.getByRole("button", { name: "Zatwierdź telewizor" }).click();
      await page.getByRole("heading", { name: "Twoje źródła" }).waitFor();
      assert.equal(
        await page.evaluate(() => document.body.classList.contains("tv")),
        true,
      );
      assert.equal(
        await page.evaluate(() => localStorage.length + sessionStorage.length),
        0,
      );
      await device.shell(`am force-stop ${app}`);
      await launch();
      page = await attach();
      await page.getByRole("heading", { name: "Twoje źródła" }).waitFor();
      await page.getByRole("checkbox").first().check();
      await page.getByRole("button", { name: "Otwórz kolekcję" }).click();
      await page.waitForFunction(
        () => document.querySelector(".media-surface img")?.naturalWidth > 0,
      );
      assert.match(await page.getByTestId("counter").innerText(), /1\s*\/\s*9/);
      await page.getByTestId("media-stage").focus();
      await device.shell("input keyevent KEYCODE_DPAD_RIGHT");
      await page.waitForFunction(() =>
        document
          .querySelector('[data-testid="counter"]')
          .innerText.trim()
          .startsWith("2"),
      );
      for (let i = 0; i < 3; i++) {
        await next();
        await page.waitForFunction(
          () => document.querySelector(".media-surface img")?.naturalWidth > 0,
        );
      }
      for (const name of [
        "Zachód słońca.mp4",
        "Chwila nad wodą.webm",
        "Spacer.mov",
      ]) {
        await next();
        await page.waitForSelector(".media-surface video");
        assert.equal(
          await page.locator(".media-caption strong").innerText(),
          name,
        );
        await page.getByTestId("media-stage").focus();
        await device.shell("input keyevent KEYCODE_MEDIA_PLAY_PAUSE");
        await page.waitForFunction(
          () => document.querySelector("video")?.currentTime > 0.2,
        );
      }
      await page
        .getByRole("button", { name: "Pełny ekran", exact: true })
        .click();
      await page.waitForSelector(".app-shell.fullscreen");
      await device.shell("input keyevent KEYCODE_BACK");
      await page.waitForSelector(".app-shell:not(.fullscreen)");
      await device.screenshot({ path: "test-results/native-tv-gallery.png" });
      await page.getByRole("button", { name: "Wyloguj", exact: true }).click();
      await page
        .getByRole("button", { name: "Połącz przez telefon" })
        .waitFor();
      await device.shell(`am force-stop ${app}`);
      await launch();
      page = await attach();
      await page
        .getByRole("button", { name: "Połącz przez telefon" })
        .waitFor();
      assert.deepEqual(errors, []);
      writeFileSync(
        "test-results/native-tv-result.json",
        JSON.stringify({ passed: true, device: device.model() }),
      );
    } finally {
      await device
        .screenshot({ path: "test-results/native-tv-final.png" })
        .catch(() => {});
      await browser.close();
      await device.close();
    }
  },
);
