import assert from "node:assert/strict";

export async function pauseRequest(page, match) {
  let resume;
  let cancelled = false;
  let released;
  const pending = new Set();
  const errors = [];
  const gate = new Promise((resolve) => {
    resume = resolve;
  });
  const handler = (route) => {
    const work = (async () => {
      await gate;
      try {
        await route.continue();
      } catch (error) {
        if (!cancelled) errors.push(error);
      }
    })();
    pending.add(work);
    void work.then(() => pending.delete(work));
    return work;
  };
  await page.route(match, handler);
  return (aborted = false) => {
    cancelled ||= aborted;
    released ??= (async () => {
      resume();
      while (pending.size) await Promise.all([...pending]);
      await page.unroute(match, handler);
      if (errors.length) throw errors[0];
    })();
    return released;
  };
}
export const collectionRequest = (url) =>
  url.pathname === "/test-drive/files" &&
  (url.searchParams.get("q") ?? "").includes("'folder-travel'");
export const metadataRequest = (url) =>
  url.pathname === "/test-drive/files/undated" &&
  url.searchParams.get("alt") === "media";

export async function thumbnailsFit(page, remaining) {
  await page.waitForFunction((remaining) => {
    const strip = document.querySelector(".thumbnail-strip");
    const buttons = [...strip.querySelectorAll(".thumbnail")];
    if (remaining === 0) return buttons.length === 0;
    if (buttons.length === 0 || buttons.length > remaining) return false;
    const bounds = strip.getBoundingClientRect();
    const first = buttons[0].getBoundingClientRect();
    const last = buttons.at(-1).getBoundingClientRect();
    const gap = parseFloat(getComputedStyle(strip).columnGap);
    return (
      buttons.every((button) => {
        const rect = button.getBoundingClientRect();
        return (
          Math.abs(rect.top - first.top) < 1 &&
          rect.left >= bounds.left - 1 &&
          rect.right <= bounds.right + 1
        );
      }) &&
      (buttons.length === remaining ||
        bounds.right - last.right < first.width + gap - 1)
    );
  }, remaining);
}
export async function mediaFillsViewer(page, fullscreen = false) {
  await page.waitForFunction((fullscreen) => {
    const viewer = document.querySelector(".viewer").getBoundingClientRect();
    const stage = document
      .querySelector(".media-stage")
      .getBoundingClientRect();
    const surface = document
      .querySelector(".media-surface")
      .getBoundingClientRect();
    const toolbar = getComputedStyle(document.querySelector(".viewer-toolbar"));
    return (
      Math.abs(viewer.height - stage.height) <= 2 &&
      Math.abs(stage.height - surface.height) <= 1 &&
      Math.abs(stage.width - surface.width) <= 1 &&
      toolbar.position === "absolute" &&
      toolbar.backgroundColor === "rgba(0, 0, 0, 0)" &&
      (!fullscreen ||
        (Math.abs(stage.height - innerHeight) <= 1 && stage.top === 0))
    );
  }, fullscreen);
}

export async function disconnectFocus(
  page,
  press = (key) => page.keyboard.press(key),
  back = "Escape",
  capture = async () => {},
) {
  const trigger = page.getByRole("button", {
    name: "Odłącz konto",
    exact: true,
  });
  await trigger.click();
  const dialog = page.getByRole("dialog");
  await dialog.waitFor();
  await page.waitForFunction(
    () => document.activeElement?.textContent === "Anuluj",
  );
  assert.equal(await dialog.evaluate((el) => el.matches(":modal")), true);
  await press("ArrowRight");
  await page.waitForFunction(
    () => document.activeElement?.textContent === "Odłącz wszędzie",
  );
  for (const key of ["ArrowRight", "ArrowDown", "ArrowUp"]) {
    await press(key);
    assert.equal(
      await page.evaluate(() => !!document.activeElement.closest("dialog")),
      true,
    );
  }
  await capture();
  await press("ArrowLeft");
  await page.waitForFunction(
    () => document.activeElement?.textContent === "Anuluj",
  );
  await press(back);
  await dialog.waitFor({ state: "hidden" });
  await page.waitForFunction(
    () => document.activeElement?.textContent === "Odłącz konto",
  );
}
