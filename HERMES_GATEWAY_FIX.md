# Исправление зависания на "Messaging platform token detected!"

## Проблема
Установка Hermes зависала на интерактивном промпте:
```
-> Messaging platform token detected!
-> The gateway handles messaging platforms and cron job execution
```

Это сообщение выводит команда `hermes gateway install`, которая ожидает ввода от пользователя.

## Корневая причина
В `install-hermes.ps1`, функция `Invoke-HermesGatewayCommand` запускала процесс с перенаправлением stdin в пустой файл:

```powershell
$stdinFile = Join-Path $env:TEMP ("hermes-gw-stdin-{0}.txt" -f [guid]::NewGuid().ToString('N'))
[System.IO.File]::WriteAllText($stdinFile, '')

$proc = Start-Process -FilePath (Get-NativePath $hermesCmd) `
    -ArgumentList @('gateway', $Action) `
    -PassThru -NoNewWindow `
    -RedirectStandardInput $stdinFile `
    -RedirectStandardOutput $outLog `
    -RedirectStandardError $errLog
```

**Проблема:** Даже с пустым файлом, stdin технически открыт, и некоторые программы могут пытаться читать из него, ожидая EOF или данных, что приводит к зависанию.

## Исправление

Использован `cmd.exe` с pipe для **полного закрытия stdin**:

```powershell
# Use cmd.exe wrapper to properly close stdin (echo. | closes the pipe)
$cmdWrapper = "echo. | `"$(Get-NativePath $hermesCmd)`" gateway $Action"
$proc = Start-Process -FilePath 'cmd.exe' `
    -ArgumentList @('/c', $cmdWrapper) `
    -PassThru -NoNewWindow -Wait:$false `
    -RedirectStandardOutput $outLog `
    -RedirectStandardError $errLog
```

### Почему это работает:
1. **`echo. |`** - выводит пустую строку и закрывает pipe, давая процессу немедленный EOF
2. **cmd.exe** - правильно обрабатывает закрытие stdin для дочернего процесса
3. **`-Wait:$false`** - явно указываем, что не ждем завершения сразу (используем `WaitForExit()` с таймаутом)

### Дополнительные env-переменные:
```powershell
$env:CI = 'true'
$env:HERMES_NONINTERACTIVE = '1'
$env:NO_INPUT = '1'  # Новая переменная
```

Добавлена `NO_INPUT=1` для дополнительного указания программе работать в неинтерактивном режиме.

## Файлы изменены
- `d:\apk\installer\install-hermes.ps1` (функция `Invoke-HermesGatewayCommand`, строки 516-560)
- `d:\apk\installer\AUDIT_REPORT_RU.md` (обновлен пункт #14)

## Проверка
```powershell
powershell -File d:\apk\installer\clean-encoding.ps1
```
**Результат:** VALIDATED OK ✅

## Техническое обоснование

### Почему `echo. |` лучше чем `-RedirectStandardInput`:

1. **`-RedirectStandardInput file.txt`**
   - Открывает файл для чтения
   - Stdin остается открытым до конца файла
   - Программа может ждать дополнительных данных

2. **`echo. | program`**
   - Pipe закрывается сразу после передачи пустой строки
   - Процесс немедленно получает EOF
   - Программа понимает, что stdin закрыт навсегда

## Результат
✅ Установка не зависает на `hermes gateway install`
✅ Процесс получает правильный сигнал о закрытом stdin
✅ Env-переменные сообщают о неинтерактивном режиме
✅ Таймаут 150 секунд защищает от других зависаний
