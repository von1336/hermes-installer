# Hermes Windows Launcher & Installer

Современный лаунчер и установщик компонентов рабочей среды Hermes для Windows на базе WPF (.NET).

## Современный графический лаунчер Hermes

Автономное десктопное приложение для Windows (`HermesLauncher.exe`) в тёмной теме, соответствующей стилю Hermes Workspace:
- **Панель мониторинга в реальном времени:** отслеживание статуса и управление службами (запуск, остановка, перезапуск) — Gateway (`:8642`), Workspace (`:3000`), Agent Dashboard (`:9119`), Ollama (`:11434`).
- **Генерация QR-кода для прямого подключения:** встроенная генерация QR-кода в приложении, копирование deep link, перевыпуск токена в один клик (24 часа, 7 дней, 30 дней, бессрочный) и отслеживание срока действия.
- **Мастер установки:** встроенный установщик с выбором компонентов, настройкой путей, прогресс-баром и терминальным выводом в реальном времени.
- **Журналы и консоль:** встроенный просмотрщик логов для диагностики и отладки.

### Сборка лаунчера:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\build-launcher.ps1
```

Исполняемый файл:
```
installer\dist\HermesLauncher.exe
```

---

## Альтернативные сценарии установки

1. **Мастер Inno Setup (классический графический установщик):**
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\build-setup.ps1
   # Результат: installer\dist\HermesWorkspaceSetup.exe
   ```
2. **Прямой сценарий командной строки PowerShell:**
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\install-hermes.ps1
   ```
3. **Консольный EXE-модуль:**
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\build-exe.ps1
   # Результат: installer\dist\HermesInstaller.exe
   ```

---

## Устанавливаемые и управляемые компоненты

- **Hermes Agent (Gateway на `:8642`):** инференс-сервер API на Python, совместимый с форматом OpenAI
- **Hermes Workspace (`:3000`):** полнофункциональная веб-панель, сессии чата, дирижёр задач и файловый менеджер
- **Agent Dashboard (`:9119`):** управление каналами и узлами
- **Tailscale (IP-адрес 100.x CGNAT):** защищённое соединение между смартфоном и ПК с нулевым доверием
- **Ollama (`:11434`):** локальный запуск языковых моделей на GPU/CPU рабочей станции
- **Навыки MemOS и Obsidian (опционально):** долговременная память и расширение возможностей агента