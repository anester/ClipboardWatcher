const editorRegistry = new Map();
let loaderPromise = null;

function configureMonacoEnvironment(basePath) {
  const baseUrl = new URL(`${basePath}/monaco/`, window.location.origin).toString();
  const workerMainUrl = new URL("vs/base/worker/workerMain.js", baseUrl).toString();

  if (window.MonacoEnvironment?.__configuredBasePath === basePath) {
    return;
  }

  window.MonacoEnvironment = {
    __configuredBasePath: basePath,
    getWorkerUrl: function getWorkerUrl(moduleId, label) {
      const workerBootstrap = `
        self.MonacoEnvironment = { baseUrl: '${baseUrl}' };
        importScripts('${workerMainUrl}');
      `;

      return `data:text/javascript;charset=utf-8,${encodeURIComponent(workerBootstrap)}`;
    }
  };
}

function loadScript(url) {
  return new Promise((resolve, reject) => {
    const existing = document.querySelector(`script[src="${url}"]`);
    if (existing) {
      existing.addEventListener("load", resolve);
      existing.addEventListener("error", reject);
      resolve();
      return;
    }

    const script = document.createElement("script");
    script.src = url;
    script.async = true;
    script.addEventListener("load", resolve);
    script.addEventListener("error", reject);
    document.head.appendChild(script);
  });
}

async function ensureMonaco(basePath) {
  if (window.monaco) {
    return;
  }

  if (!loaderPromise) {
    loaderPromise = (async () => {
      configureMonacoEnvironment(basePath);
      await loadScript(`${basePath}/monaco/vs/loader.js`);
      return new Promise((resolve, reject) => {
        window.require.config({ paths: { vs: `${basePath}/monaco/vs` } });
        window.require(["vs/editor/editor.main"], () => resolve(), reject);
      });
    })();
  }

  await loaderPromise;
}

export async function createEditor(element, options, dotNetRef, basePath) {
  await ensureMonaco(basePath);
  const editor = window.monaco.editor.create(element, options);
  const model = editor.getModel();

  if (model && options.language) {
    window.monaco.editor.setModelLanguage(model, options.language);
  }

  const subscription = editor.onDidChangeModelContent(() => {
    dotNetRef.invokeMethodAsync("NotifyChange", editor.getValue());
  });

  editorRegistry.set(editor, subscription);
  return editor;
}

export function setValue(editor, value) {
  if (!editor) {
    return;
  }

  const current = editor.getValue();
  if (current !== value) {
    editor.setValue(value);
  }
}

export function setLanguage(editor, language) {
  if (!editor) {
    return;
  }

  const model = editor.getModel();
  if (model && language) {
    window.monaco.editor.setModelLanguage(model, language);
  }
}

export function disposeEditor(editor) {
  const subscription = editorRegistry.get(editor);
  if (subscription) {
    subscription.dispose();
  }

  editorRegistry.delete(editor);
  editor?.dispose();
}

export function setReadOnly(editor, readOnly) {
  if (!editor) {
    return;
  }

  editor.updateOptions({ readOnly: !!readOnly });
}
