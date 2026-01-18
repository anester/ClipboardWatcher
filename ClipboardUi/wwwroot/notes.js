export function getScrollFraction(element) {
  if (!element) {
    return 0;
  }

  const maxScroll = Math.max(1, element.scrollHeight - element.clientHeight);
  return element.scrollTop / maxScroll;
}

export function registerSaveShortcut(dotNetRef) {
  const handler = (event) => {
    const key = event.key?.toLowerCase();
    if ((event.ctrlKey || event.metaKey) && key === "s") {
      event.preventDefault();
      dotNetRef.invokeMethodAsync("OnSaveShortcut");
    }
  };

  window.addEventListener("keydown", handler);
  return {
    dispose: () => window.removeEventListener("keydown", handler)
  };
}
