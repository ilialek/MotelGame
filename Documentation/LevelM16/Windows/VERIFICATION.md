# M16 Windows x64 distribution

Собрана текущая сцена `Motel_Layout_V4.unity` в Unity 6000.3.19f1: StandaloneWindows64, Mono, Development. Сборка успешна, 0 ошибок, 18 предупреждений о неподдерживаемой компиляции Ray Tracing Shader; сообщения сохранены в `windows-build-validation.txt`. Настройки URP и gameplay не изменялись для подавления предупреждений.

Проверены единственная сцена V4 в данных сборки, комнаты 101–106, валидация конфигурации сцены, зависимости NGO/Transport/Authentication/Multiplayer/URP/Input System, x64 PE-заголовки всех 10 нативных EXE/DLL и наличие файлов из BuildReport. Оба платформенных gameplay assembly содержат протокол `motel-m16-1`.

Архив: `/Users/ilalekomtsev/MotelGame/Builds/Playtest/Distributions/MotelGame-M16-Windows-x64.zip`. Размер: 75,909,837 байт. 350 файлов. CRC всех ZIP entries и SHA-256 каждого включённого файла проверены. Включены весь runtime, данные, Windows launchers и инструкция; исключены только помеченные Unity `DoNotShip` debug-данные. Контрольная сумма ZIP: `dda679fb869cb3ec3f430b9e7a17baa91f7011a83d2ef78cdc46645ed668e470`.

Windows EXE НЕ запускался. Windows rendering/input/audio, TLS/Relay runtime и совместная сессия Mac ↔ Windows остаются для проверки на компьютере друга. Предыдущие Mac multiplayer-тесты M16 не считаются Windows runtime-тестами.

Изменён только `Assets/Game/Editor/PlaytestBuild.cs`: существующая Windows-команда теперь выбирает V4 и сохраняет отчёт M16. Runtime scripts, сцены, ассеты, Packages, ProjectSettings, macOS app и все `.command` launchers совпадают с состоянием перед сборкой. Проверено 2864 ранее существовавших файлов. Commit/push не выполнялись. Предыдущая Windows-сборка сохранена в `/Users/ilalekomtsev/MotelGame/Backups/BeforeWindowsM16_20261027_175941`.

Для повторной сборки: `Tools → Motel → Online → Build Windows Playtest`. Выход: `Builds/Playtest/Windows/MotelGame.exe`. ZIP создаётся отдельно после проверки файлов.
