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
