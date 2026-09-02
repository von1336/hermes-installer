## План продолжения

Проверка актуального кода завершена. Файлы пока не изменялись.

### Подтверждённые проблемы

1. __Неверный порядок аргументов `-NoPause`__\
   В `D:\apk\installer\launcher\Services\InstallerRunnerService.cs:181-216` параметры MemOS добавляются после финального `-NoPause`. Это может привести к некорректному PowerShell-вызову.

2. __Секреты попадают во временный PowerShell-файл__\
   `MemOSProviderKey` записывается в файл `%TEMP%\hermes-launcher-install-*.ps1`. Файл также не удаляется в `finally`.

3. __Небезопасная отмена и повторный запуск__

   - `InstallerRunnerService` хранит `_cts` и `_runningProcess` в общих полях.
   - `CancelInstall()` в `MainViewModel` немедленно устанавливает `IsInstalling = false`.
   - Фактическая старая операция может ещё продолжаться, пока уже разрешён retry.
   - События завершившейся старой операции могут изменить состояние нового запуска.

4. __Ложный успех установки__

   - `InstallerRunnerService` считает установку успешной только по exit code `0`.
   - `install-hermes.ps1` выводит предупреждения при провале health-check и всё равно доходит до `INSTALLATION COMPLETE`.
   - В `HermesConfigService.IsHermesInstalled()` наличие любого из двух `.env` считается полной установкой.

5. __Автозапуск регистрируется при `StartServices = false`__\
   В `install-hermes.ps1` функция `Register-NativeTasks` вызывается и в ветке пропуска запуска сервисов.

6. __Остановка процессов недостаточно ограничена__

   - `ServiceMonitor.KillProcessOnPort()` завершает любой процесс, владеющий портом.
   - `InstallerRunnerService.KillExistingHermesProcesses()` безусловно допускает процессы `hermes` и `pnpm`.
   - `uninstall-hermes.ps1` ориентируется в основном на имя процесса и может затронуть чужие процессы.

7. __Firewall__ `uninstall-hermes.ps1` не выполняет гарантированное повышение прав перед удалением правил.

8. __UI__ Интерфейс уже имеет sidebar, карточки и тёмную тему, но визуальная система неоднородна:

   - много плотных технических подписей;
   - не все состояния установки явно разделены;
   - секретные поля представлены обычными `TextBox`;
   - progress/log area можно сделать более заметной;
   - требуется единый современный spacing/radius/типографический набор.

Dribbble не предоставил содержимое из-за bot-check, поэтому дизайн будет основан на общих паттернах AI-приложений: спокойная тёмная палитра, яркий единичный accent, левый navigation rail, крупные status cards, выраженные loading/error/success states и progressive disclosure для расширенных настроек.

---

## План реализации в Act mode

### 1. Надёжная модель состояния установки

Файл:

- `D:\apk\installer\launcher\Services\InstallerRunnerService.cs`
- `D:\apk\installer\launcher\ViewModels\MainViewModel.cs`

Изменения:

- ввести operation-scoped объект состояния с уникальным `Guid`;

- использовать отдельные `CancellationTokenSource` и `Process` для каждой операции;

- не разрешать новый запуск, пока предыдущая операция полностью не завершила:

  - остановку процесса;
  - чтение stdout/stderr;
  - освобождение ресурсов;
  - отправку финального события;

- сделать отдельные состояния:

  - `Ready`;
  - `Running`;
  - `Cancelling`;
  - `Cancelled`;
  - `Failed`;
  - `Completed`;

- retry разрешать только после фактического завершения предыдущей операции;

- игнорировать устаревшие события по operation id;

- корректно различать отмену и ошибку.

### 2. Безопасный запуск PowerShell

Файл:

- `D:\apk\installer\launcher\Services\InstallerRunnerService.cs`

Изменения:

- собрать все аргументы до последнего `-NoPause`;

- добавить безопасное escaping PowerShell-строк;

- не записывать секреты в launch script:

  - передавать их через безопасный временный файл с гарантированной очисткой, либо;
  - использовать переменные окружения процесса;

- удалить временный launch script в `finally`;

- исключить секреты из логов и сообщений об ошибках;

- проверить exit code дочернего PowerShell-процесса.

### 3. Completion marker и строгий критерий установки

Файлы:

- `D:\apk\installer\install-hermes.ps1`
- `D:\apk\installer\launcher\Services\HermesConfigService.cs`
- `D:\apk\installer\launcher\ViewModels\MainViewModel.cs`

Изменения:

- создать marker, например:
  - `%LOCALAPPDATA%\hermes\install-complete.json`;

- записывать его только после успешного выполнения всех обязательных этапов;

- хранить в marker:

  - версию установщика;
  - install/workspace paths;
  - результаты обязательных компонентов;
  - результат health-check;
  - timestamp;

- удалять или инвалидировать marker при провале/отмене;

- `IsHermesInstalled()` должен проверять:

  - marker;
  - оба `.env`;
  - обязательные workspace-файлы;
  - установленный gateway;
  - валидность manifest;

- частичная установка должна открывать мастер восстановления, а не dashboard.

### 4. Обязательные health-check и exit code

Файл:

- `D:\apk\installer\install-hermes.ps1`

Изменения:

- определить обязательные проверки для gateway и workspace;

- если обязательный сервис не прошёл health-check:

  - записать `failed_hard` или отдельный failure result;
  - сформировать error report;
  - завершить скрипт ненулевым кодом;

- optional-компоненты вроде Ollama, Tailscale, MemOS и Obsidian оставить мягкими failures, если это соответствует выбранной конфигурации;

- `INSTALLATION COMPLETE` выводить только при действительно успешном результате.

### 5. Автозапуск

Файл:

- `D:\apk\installer\install-hermes.ps1`

Изменения:

- разделить:

  - немедленный запуск сервисов;
  - регистрацию автозапуска;

- при `StartServices = false` не создавать Scheduled Tasks и startup scripts, если пользователь отдельно не выбрал автозапуск;

- проверить также uninstall-логику удаления задач и startup-файлов.

При необходимости потребуется добавить отдельное поле в `InstallSettings`, например `EnableAutoStart`, и соответствующий binding в ViewModel/XAML.

### 6. Безопасное управление процессами

Файлы:

- `D:\apk\installer\launcher\Services\ServiceMonitor.cs`
- `D:\apk\installer\launcher\Services\InstallerRunnerService.cs`
- `D:\apk\installer\uninstall-hermes.ps1`

Изменения:

- перед остановкой проверять:

  - PID;
  - имя процесса;
  - полный путь executable;
  - командную строку;
  - рабочую директорию;
  - ожидаемый Hermes install/workspace path;

- для процессов, запущенных лаунчером, сохранять PID и metadata;

- `KillProcessOnPort()` должен завершать только подтверждённые Hermes-процессы;

- не завершать произвольные `node`, `pnpm`, `cmd`, `ollama` или `hermes`;

- аналогичные проверки использовать в uninstall flow;

- ошибки остановки не скрывать полностью: писать безопасное диагностическое сообщение.

### 7. Firewall и ярлыки

Файлы:

- `D:\apk\installer\install-hermes.ps1`
- `D:\apk\installer\uninstall-hermes.ps1`

Изменения:

- определить единый способ запуска операций Firewall с elevation;
- корректно обработать отказ пользователя от UAC;
- проверять существование правил перед удалением;
- синхронизировать создаваемые и удаляемые shortcuts;
- не оставлять Start Menu directory после удаления, если она пуста.

### 8. Маскирование секретов

Файлы:

- `D:\apk\installer\launcher\Views\MainWindow.xaml`
- `D:\apk\installer\launcher\Views\MainWindow.xaml.cs`
- `D:\apk\installer\launcher\ViewModels\MainViewModel.cs`

Изменения:

- заменить обычные `TextBox` для API keys/passwords на `PasswordBox` или контрол с toggle visibility;

- не отображать секреты в raw `.env` редакторе открытым текстом;

- добавить действия:

  - `Show`;
  - `Hide`;
  - `Copy`;
  - `Regenerate`;

- при копировании показывать нейтральный toast без раскрытия значения;

- проверить QR payload и явно показывать предупреждение о том, что pairing code содержит секреты.

### 9. Редизайн WPF-интерфейса

Файлы:

- `D:\apk\installer\launcher\Themes\HermesTheme.xaml`
- `D:\apk\installer\launcher\Views\MainWindow.xaml`
- при необходимости `D:\apk\installer\launcher\Views\MainWindow.xaml.cs`

Сохраню существующие event handlers и bindings, но приведу интерфейс к единой системе:

- палитра:

  - глубокий navy/charcoal background;
  - слегка контрастные surface cards;
  - один primary accent;
  - зелёный/жёлтый/красный только для semantic status;

- единые spacing tokens;

- единые радиусы карточек и полей;

- улучшенная типографическая иерархия;

- компактный левый navigation rail;

- status overview с:

  - общей готовностью системы;
  - количеством online services;
  - средней latency;
  - последней проверкой;

- карточки сервисов с явными состояниями:

  - Online;
  - Offline;
  - Checking;
  - Starting;
  - Failed;

- мастер установки:

  - Diagnostics;
  - Paths & Components;
  - Provider;
  - Deploy;

- заметный progress header;

- отдельные визуальные состояния:

  - empty;
  - loading;
  - success;
  - cancelled;
  - failure;

- улучшенная панель логов с фильтром и copy action;

- доступные hover/focus/disabled states;

- адаптивные `Grid`/`ScrollViewer` вместо чрезмерно фиксированных размеров.

### 10. Проверки

После реализации:

1. Проверка изменённых файлов чтением и поиском binding’ов.

2. `dotnet build` или `dotnet publish` для:

   - `D:\apk\installer\launcher\HermesLauncher.csproj`

3. PowerShell parser check для:

   - `D:\apk\installer\install-hermes.ps1`
   - `D:\apk\installer\uninstall-hermes.ps1`
   - `D:\apk\installer\lib\InstallComponents.ps1`

4. Статические проверки:

   - `-NoPause` находится последним параметром;
   - временный launch script удаляется;
   - нет открытого provider key в launch script;
   - marker создаётся только в успешной ветке;
   - `StartServices = false` не регистрирует автозапуск;
   - отсутствует безусловный `Stop-Process` для чужих процессов.

5. Если в проекте нет тестового проекта, не буду добавлять новый testing framework без подтверждённой зависимости. Вместо этого добавлю доступные локальные parser/static checks.

6. Обновлю:

   - `D:\apk\installer\AUDIT_REPORT_RU.md`

   Отчёт будет отражать только фактически исправленные проблемы и оставшиеся риски.
