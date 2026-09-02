# Hermes Windows Modern Launcher & Installer

## 🚀 Hermes Modern Launcher (Recommended)

Single-file modern WPF desktop application (`HermesLauncher.exe`) featuring a dark theme matching Hermes Workspace:
- **📊 Real-time Dashboard:** Monitor and Start/Stop/Restart all services (Gateway `:8642`, Workspace `:3000`, Agent Dashboard `:9119`, Ollama `:11434`).
- **📲 Live Connect QR:** Native in-app QR code generation, deep link copy, 1-click token regeneration (24h, 7d, 30d, permanent), and expiry tracker.
- **⚡ Setup Wizard:** Integrated installer with component selection, custom paths, live progress bar, and real-time terminal output.
- **📜 Logs & Console:** Multi-file log viewer for troubleshooting.

### Build Launcher:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\build-launcher.ps1
```

Executable output:
```
installer\dist\HermesLauncher.exe
```

---

## 🛠️ Alternative Installers

1. **Inno Setup Wizard (Legacy GUI):**
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\build-setup.ps1
   # Output: installer\dist\HermesWorkspaceSetup.exe
   ```
2. **Direct CLI Script:**
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\install-hermes.ps1
   ```
3. **Console EXE Wrapper:**
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\build-exe.ps1
   # Output: installer\dist\HermesInstaller.exe
   ```

---

## 📦 What is Installed & Managed

- **Hermes Agent (Gateway on `:8642`):** Python OpenAI-compatible API inference server
- **Hermes Workspace (`:3000`):** Full-stack web dashboard, chat sessions, conductor & file manager
- **Agent Dashboard (`:9119`):** Channels & node management
- **Tailscale (100.x CGNAT IP):** Encrypted zero-trust phone-to-PC connection
- **Ollama (`:11434`):** Local model inference on host GPU/CPU
- **MemOS & Obsidian Skills (Optional):** Long-term memory and agent capabilities
