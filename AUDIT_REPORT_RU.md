# Bug Audit: Hermes Installer & Launcher

Дата: 2026-09-03  
Объём: все текстовые файлы репозитория (PowerShell, C#, XAML, Inno Setup, bat/sh, CI, документация).  
Метод: статический анализ. Пункты из раздела «Требует ручной проверки» не подтверждены запуском.

Приоритеты:

- **P0** - блокирует сборку/поставку.
- **P1** - ломает установку/удаление, создаёт ложное состояние или утечку.
- **P2** - безопасность, корректность, UX.
- **P3** - чистка и качество кода.

---

## P0. Блокирует сборку/поставку

| # | Проблема | Файлы | Исправление |
|---|---|---|---|
| 1 | `build-exe.ps1` ссылается на несуществующие `exe\HermesInstaller.csproj` и `exe\Program.cs`; каталог `exe/` в `.gitignore`. README рекламирует «Console EXE Wrapper», который собрать нельзя. | `build-exe.ps1`, `README.md`, `.gitignore` | Удалить скрипт и раздел README либо вернуть проект в репозиторий. |
| 2 | Хардкод путей разработчика: `process-logo.ps1` (`C:\Users\vona\.gemini\...`, `d:\apk\installer\...`), `build-brand-assets.ps1` (`C:\Users\vona\.cursor\...`), `install-hermes.bat` («Delete Telegram copies and use D:\apk\installer\»). | `process-logo.ps1`, `build-brand-assets.ps1`, `install-hermes.bat` | Удалить/параметризовать, убрать dev-сообщения из пользовательского bat. |
| 3 | Четыре источника версии: `install-hermes.ps1` = `2026-08-30-pro-v9`, `version.txt` = `2026.9.8`, `HermesLauncher.csproj` = `2026.8.31.1`, `hermes-setup.iss` = `2026.08.30.9`; `install-hermes.bat` проверяет строку через `findstr`. | все перечисленные | Один источник (`version.txt`), генерация остальных при сборке. |
| 4 | `clean-encoding.ps1` заменяет любой символ >=128 на `-` и уже испортил строки в `lib/InstallComponents.ps1` (`'not found --" skipping profile patch'`, `'--" continuing with degraded config'`, `'--" applying profile only'`, `'--" credentials may be invalid'`). | `clean-encoding.ps1`, `lib/InstallComponents.ps1` | Удалить скрипт, починить 4 строки, добавить `.gitattributes`/`.editorconfig`. |

---

## P1. Ломает установку/удаление или создаёт ложное состояние

### 6. StrictMode ломает отчёт об ошибке на ранних шагах
`Set-StrictMode -Version Latest` из `lib/InstallComponents.ps1` протекает в `install-hermes.ps1` через dot-source. `Protect-SecretText` читает `$apiKey` и `$hermesPassword`, которые не определены до шага «Generating secrets». Любая ошибка раньше (winget, git, Python, hermes-agent) приводит к исключению внутри `Write-InstallFailureReport`: `install-error.txt` не пишется, Notepad не открывается, лаунчер видит только exit 1.  
**Фикс:** инициализировать `$Script:ApiKey/$Script:HermesPassword = ''` в начале и использовать `$Script:`-переменные; либо не включать StrictMode в библиотеке.

### 7. Native stderr + `$ErrorActionPreference='Stop'` в PS 5.1 с перенаправленными потоками
Лаунчер запускает `powershell.exe` с `RedirectStandardError=true`. В этом режиме PS 5.1 превращает stderr нативных команд (`git clone` пишет «Cloning into...» в stderr, `winget`, `pnpm`, `corepack`, `node -v`) в `NativeCommandError`, а при `Stop` это терминирующая ошибка. Установка из лаунчера может падать на успешном `git clone`.  
**Фикс:** нативные вызовы в блоке с `$ErrorActionPreference='Continue'` + проверка `$LASTEXITCODE`, либо через `Start-Process`/`cmd /c`.

### 8. `uninstall-hermes.ps1` игнорирует пользовательский `InstallDir`
`$hermesHome` жёстко `%LOCALAPPDATA%\hermes`; из манифеста читается только `workspaceDir`. `WriteUninstallLaunchScript` в лаунчере не передаёт `InstallDir`. Установка в другой каталог не удаляется.

### 9. Полное удаление из лаунчера всегда оставляет мусор
`InstallerRunnerService` запускает PowerShell с `WorkingDirectory = %LOCALAPPDATA%\hermes\embedded-installer`; `Remove-Item $hermesHome -Recurse` не может удалить cwd работающего процесса, итог «WARNING: not fully removed». Затем лаунчер вызывает `ProcessOwnershipRegistry.Unregister`, а `Save()` заново создаёт `processes.json`.  
**Фикс:** cwd в `%TEMP%`, копировать скрипт удаления в `%TEMP%`, не писать registry после uninstall.

### 10. Uninstall не откатывает глобальные изменения
Пользовательские переменные `HERMES_HOME` и `OLLAMA_HOST` остаются навсегда; `Hermes-install-error.txt` на рабочем столе тоже.

### 11. `EnableAutoStart=false` не соблюдается для gateway
`Invoke-HermesGatewayCommand -Action install` всегда передаёт `--start-on-login` и `HERMES_GATEWAY_INSTALL_START_ON_LOGIN=1`. Также `Register-NativeTasks` использует `Get-Command hermes` вместо `Resolve-HermesCommand`: при устаревшем PATH задача `HermesDashboard` молча не создаётся.

### 12. Мастер в лаунчере собирает настройки, которые никуда не передаются
Шаг «Provider» биндится к `ProviderBaseUrl/ProviderApiKey/ProviderModelName` (gateway-провайдер для «Apply & Restart Gateway»), а в `InstallSettings` уходят `MemOSProviderUrl/Key/Model`, для которых в XAML нет контролов. `MemOSProviderKey` всегда пуст, путь «секрет через env» мёртвый. Нет UI для `StartServices`, `EnableAutoStart`, `InstallObsidianSkills`, `MemOSMode` (чекбокс «Obsidian & Skills Pack» включает только Obsidian).  
Файлы: `launcher/Views/MainWindow.xaml`, `launcher/ViewModels/MainViewModel.cs`.

### 13. Гонка в `StartInstallAsync`/`StartUninstallAsync`
`IsInstalling = true` выставляется до вызова runner; при `InvalidOperationException` (overlap) показывается toast, но `IsInstalling/IsCancelling/InstallationState` не сбрасываются, UI заблокирован навсегда.

### 14. Ложный успех при старте сервисов из лаунчера
`ServiceMonitor.RunProcessHidden` перенаправляет stderr и читает его только после `WaitForExit(10s)`: deadlock при >4 КБ вывода; по таймауту `StillRunning=true` трактуется как успех. `hermes gateway start` запускается без `CI/HERMES_NONINTERACTIVE` и с открытым stdin (промпт «Messaging platform token detected!»): зависает и репортится как «Started». `hermes` ищется только по PATH лаунчера, а не в `%LOCALAPPDATA%\hermes\bin`.

### 15. Regenerate/Expire QR не делают того, что обещают
`RegenerateConnectQr` генерирует секреты только если они пусты; кнопки 24h/7d/30d меняют лишь `exp` в payload, `API_SERVER_KEY`/`HERMES_PASSWORD` не ротируются, «Expire» только меняет таймер. README заявляет «1-click token regeneration».  
**Фикс:** реальная ротация с записью в оба `.env` и рестартом gateway, либо убрать обещания из UI.

### 16. Автоотправка отчёта об ошибке в GitHub без согласия
`HandleOperationFinished` вызывает `SendErrorReportToGitHub(auto: true)`: перезаписывает буфер обмена и открывает браузер с телом, содержащим `Machine`, `User`, Tailscale/LAN IP, хвосты логов. Санитайзер редактирует только `KEY=`-паттерны и MemOS-ключ.  
**Фикс:** сделать opt-in.

---

## P2. Безопасность, корректность, UX

17. **Inno Setup пишет MemOS API key открытым текстом** в `{tmp}\hermes-install-launch.ps1` (`WriteLaunchScript` в `hermes-setup.iss`). Фикс из аудита №5 покрыл только WPF-лаунчер.
18. **Секрет через `psi.Environment` наследуется всеми дочерними процессами**, включая долгоживущие `hermes gateway` и `pnpm dev`: ключ виден через WMI/Process Explorer. Передавать через stdin или файл с ACL, удалять переменную перед запуском сервисов.
19. **`connect.html` со секретами подгружает `qrcodejs` с jsdelivr CDN.** Встраивать QR как локальный PNG или инлайнить библиотеку.
20. **Правила брандмауэра только для `Private,Domain`.** На Wi-Fi с профилем «Public» телефон не подключится, установщик покажет OK.
21. **Supply chain:** `hermes-agent/main/scripts/install.ps1` и `MemOS/main/.../install.ps1` без пина (TOFU), кеш в HermesHome не обновляется; `UpdateService` подменяет exe без проверки `.sha256`; скрипт hermes-agent исполняется in-process (`& $HermesAgentScriptPath`), в отличие от MemOS.
22. **`Set-YamlBlockField` может править чужой блок.** Регулярка `(^${Block}:[\s\S]*?\s*${Field}:\s*).*$` лениво ищет `Field` до конца файла: если в `embedding:` нет `provider`, будет заменён `provider` в `llm:`. Значения (включая `apiKey`) пишутся без кавычек, `#` и `:` ломают YAML.
23. **Тройное создание ярлыков через Inno:** `[Icons]` (пропускается, `[Run]` ещё не выполнен), `CreateConnectShortcuts` в `ssPostInstall`, `install-hermes.ps1 -CreateShortcuts`. Папки Start Menu разные (`Hermes Workspace` vs `Programs\Hermes`).
24. **Производительность:** `Test-HermesOwnedProcess` делает CIM-запрос на каждый процесс системы; `ProcessOwnershipRegistry` порождает `powershell.exe` на каждый вопрос. Использовать один `Get-CimInstance` и `System.Management`/`IPGlobalProperties`.
25. **`ProcessOwnershipRegistry.Register` всегда получает `exePath = null`**: проверка «pid matches registry entry» не срабатывает никогда.
26. **`Stop` для Ollama ничего не делает**, но toast говорит «Stopped Ollama Local LLM».
27. **Windows Store-заглушка `python.exe`** проходит `Test-CommandExists 'python'` и даёт false positive в `SystemDiagnosticsService`.
28. **`tailscale up` без таймаута** (`Start-Process -Wait`) в скрытом процессе; нет общего таймаута установки в лаунчере.
29. **`Refresh-Path` пересобирает PATH из Machine+User** и теряет пути Tailscale/Ollama, добавленные в процессе.
30. **`Write-InstallLog -Secret`** редактирует всё сообщение целиком: в логе `****<4 символа текста>`.
31. **После Cancel сообщение «system left in previous state» ложно:** `.env`, задачи, firewall-правила уже могут быть созданы.
32. **Логи установки в UI:** `InstallerLogText += line` через синхронный `Dispatcher.Invoke` на каждую строку: O(n^2) и фризы при `pnpm install`. Батчить через `BeginInvoke` + `StringBuilder`.
33. **`pnpm dev` как продакшен-рантайм** (без сборки, с HMR, консольное окно при автозапуске через `cmd.exe /c`).
34. **`ApplyAndRestart` в `UpdateService`:** `find "1234"` совпадает с PID `11234` (бесконечное ожидание); автозагрузка обновления при каждом старте без согласия.
35. **`net6.0-windows` вне поддержки.** Перейти на net8+.
36. **`COOKIE_SECURE=0`, `HOST=0.0.0.0`, HTTP без TLS**: при LAN-fallback API-ключ и пароль ходят открытым текстом по Wi-Fi. Минимум предупреждать в UI/QR-странице.

---

## P3. Чистка и качество

37. Мёртвый код: `Invoke-Elevated` (с `Invoke-Expression` base64), `ShowSetupWizardChrome/ShowSetupConfigFields/ShowSetupComponents`, `ApiKeyDisplay/PasswordDisplay`, `ISecretCommands`, `PairingWarning`, событие `Finished`, `SecretFieldDto`, `DataState`, `-KeepUserData`, `setup-workspace.sh`, `launcher/Resources/*.png` и корневой `hermes-logo.png` (дубликаты `assets/`).
38. Стейл-документация: `AUDIT_REPORT_RU.md`, `plan.md`, `HERMES_GATEWAY_FIX.md` (описывает старую версию `Invoke-HermesGatewayCommand`), пути `D:\apk\installer`, README с несуществующим `installer\`.
39. `build-setup.ps1` всегда перегенерирует `assets/*.bmp|ico`: недетерминированные сборки и шум в git.
40. `Write-InstallManifest.choices` не содержит `enableAutoStart`, `createShortcuts`; результат компонента `workspace` записывается только при ошибке.
41. `ApplyHermesBrandingText` в `.iss`: ветки russian/english идентичны.
42. `SystemDiagnosticsService` и `lib/InstallComponents.ps1` расходятся в путях Obsidian и рекомендуемых пакетах (`OpenJS.NodeJS` vs `.LTS`, Python 3.11 vs 3.12).
43. Нет тестов, `.sln`, PSScriptAnalyzer в CI, `.gitattributes`.

---

## Рекомендуемый порядок работ

1. **Инфраструктура:** (`dotnet publish`, PSScriptAnalyzer, parser-check); единый `version.txt`; удалить `build-exe.ps1`, `process-logo.ps1`, `clean-encoding.ps1`; починить 4 испорченные строки; `.gitattributes`/`.editorconfig`; поправить README.
2. **Надёжность установщика:** п.6, 7, 11, 20, 22, 28, 29, 30.
3. **Uninstall:** п.8, 9, 10, 23; передавать `InstallDir`; cwd в `%TEMP%`; убрать `HERMES_HOME`/`OLLAMA_HOST`; единый список ярлыков.
4. **Лаунчер, корректность:** п.12, 13, 14, 15, 25, 26, 32.
5. **Безопасность:** п.16, 17, 18, 19, 21, 36.
6. **Производительность и модернизация:** п.24, 33, 34, 35.
7. **Чистка:** п.37-43.

---

## Требует ручной проверки на реальной машине

- Существуют ли флаги `hermes gateway install --start-now --start-on-login` и эндпоинты `/health`, `/api/healthcheck`, `/api/status`, порт MemOS 18800.
- Поведение `git clone`/`winget` под лаунчером (п.7) на PS 5.1.
- Полный цикл: установка в нестандартный каталог, Cancel, Retry, Clean Reinstall, Full Uninstall, с проверкой остатков (`%LOCALAPPDATA%\hermes`, env-переменные, задачи, firewall, ярлыки).
