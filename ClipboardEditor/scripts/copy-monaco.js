const fs = require("fs");
const path = require("path");

const source = path.join(__dirname, "..", "node_modules", "monaco-editor", "min", "vs");
const destination = path.join(__dirname, "..", "wwwroot", "monaco", "vs");

function copyRecursive(src, dest) {
  if (!fs.existsSync(src)) {
    throw new Error(`Source not found: ${src}`);
  }

  if (!fs.existsSync(dest)) {
    fs.mkdirSync(dest, { recursive: true });
  }

  for (const entry of fs.readdirSync(src, { withFileTypes: true })) {
    const srcPath = path.join(src, entry.name);
    const destPath = path.join(dest, entry.name);

    if (entry.isDirectory()) {
      copyRecursive(srcPath, destPath);
    } else if (entry.isFile()) {
      fs.copyFileSync(srcPath, destPath);
    }
  }
}

copyRecursive(source, destination);
console.log(`Copied Monaco to ${destination}`);
