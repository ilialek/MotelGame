# Milestone 1 — Local Multiplayer

> С M7 используется один актуальный build: `Builds/Playtest/LaunchWindowed.command`. Это историческое описание milestone; текущие многокомнатный сценарий и тесты находятся в `Documentation/MultipleGuestsM7/README.md`. Оба участника должны запускать текущую версию. Старые M4–M6 single-guest тесты сохранены как история и не проверяют текущий M7 protocol.
Unity 6000.3.19f1 / URP. Сцена: `Assets/Game/Scenes/Motel_Blockout_V3.unity`.

## Архитектура

- NGO **2.13.3**, Unity Transport **2.6.0**. Host–Client / Listen Server, стандартный NGO PlayerObject и ownership.
- `NetworkPlayer.prefab`: NetworkObject, штатный NetworkTransform с Authority Mode = Owner, CharacterController, существующий FirstPersonController, камера/AudioListener и Capsule RemoteBody.
- `NetworkPlayer` включает ввод, CharacterController, камеру и AudioListener только владельцу. На удалённых копиях они отключены; видна только модель, а transform получает обновления NGO.
- NetworkTransform синхронизирует позицию и yaw, с interpolation для удалённых копий. Camera pitch пока локальный; наклон головы другого игрока не отображается.
- Host через Connection Approval резервирует отдельные lobby spawn points и ограничивает сессию четырьмя участниками, включая host. Проверка рассчитана на два экземпляра; четыре игрока отдельно не протестированы.
- `LocalTransportSettings` содержит UnityTransport, IP и UDP port. `NetworkPlayer` и FirstPersonController не зависят от UTP. Для Steam в будущем потребуется отдельная интеграция transport/session startup; переписывать NetworkObject, ownership и gameplay RPC/NetworkVariable из-за смены транспорта не требуется.
- Авторитет клиента относится только к движению своего персонажа. Будущий общий gameplay state должен оставаться server-authoritative. Текущая реализация не добавляет сетевой gameplay state, Steam или сетевой framework.

## Что сохранено

Walk 4 м/с, Sprint 6 м/с, CharacterController 1.8 м высотой и 0.6 м диаметром, camera eye height 1.65 м, near plane 0.03 м. WASD, mouse look, Left Shift, Escape и gravity сохранены.

Геометрия, мебель, материалы, освещение и SampleScene не изменены. Все environment YAML documents сохранены семантически без изменений. Резервная копия сцены, FPS-контроллера и исходных package files: `BeforeMilestone1/`.

## Проверка Editor + standalone

1. Дождитесь установки пакетов и компиляции. Откройте заново Motel_Blockout_V3 с диска. Если Unity предлагает reload внешне изменённой сцены, примите reload после сохранения своих отдельных несохранённых изменений; не перезаписывайте новый файл старой открытой копией.
2. В Editor нажмите Play. В Game view нажмите **Start Host**. Стандартный адрес 127.0.0.1, UDP port 7777.
3. Откройте `Builds/Playtest/MotelGame.app`. В нём оставьте 127.0.0.1 и 7777, нажмите **Start Client**.
4. Должно быть два player объекта с разными OwnerClientId и отдельными spawn-точками (6,0.05,2) и (8,0.05,2). У каждого экземпляра активны одна камера/AudioListener и только собственный FPS-контроллер. Удалённый персонаж — синяя capsule.
5. В каждом окне отдельно пройдите по lobby и коридору, повернитесь. Второе окно должно видеть перемещение и поворот чужой capsule. Обе программы продолжают сетевые обновления в фоне (`Application.runInBackground`).
6. **Escape** открывает connection menu и освобождает курсор. Клик вне панели возвращает FPS-управление. Клик по панели не захватывает курсор. **Disconnect** останавливает локальную сессию. При уходе client его player исчезает у host; при уходе host сессия заканчивается у client.
7. Повторите подключение и проверьте Console/Player.log. Реальное управление, mouse sensitivity, cursor focus, визуальную плавность и обзор стен оцените вручную.

При необходимости пересобрать приложение: **Tools > Motel > Build Local Multiplayer Development Player**. Команда собирает только Motel_Blockout_V3, не меняя Build Settings других сцен. На другой платформе используйте обычный Unity Build Profile для этой сцены и установленный модуль целевой платформы.

## LAN

На host оставьте port 7777: transport слушает все IPv4 interfaces. На client введите LAN IPv4 компьютера-host, например `192.168.1.20`, и тот же port. Оба устройства должны находиться в доступной локальной сети; разрешите Unity/приложению локальный UDP-трафик, если ОС спросит. 127.0.0.1 работает только для двух экземпляров на одном компьютере. Это локальная сеть без Relay/NAT traversal и без Steam matchmaking.

## Файлы

Runtime:
- `Assets/Game/Scripts/FirstPersonController.cs` — сохранённое движение + флаг приостановки ввода для UI.
- `Assets/Game/Scripts/Networking/NetworkPlayer.cs` — ownership и локальные компоненты.
- `Assets/Game/Scripts/Networking/DevelopmentSession.cs` — start/stop, connection approval, spawn slots, development UI.
- `Assets/Game/Scripts/Networking/LocalTransportSettings.cs` — UTP endpoint.
- `Assets/Game/Prefabs/NetworkPlayer.prefab` — сетевой игрок.
- `Assets/Game/Scenes/Motel_Blockout_V3.unity` — NetworkSession и LobbyPreviewCamera.

Editor / validation:
- `Assets/Game/Editor/LocalMultiplayerSetup.cs` — одноразовая защищённая настройка, проверка конфигурации и development build.
- `Assets/Game/Scripts/Networking/Tests/LocalMultiplayerSmokeTest.cs` — только Editor/development build, неактивен без явного `-motel-smoke` argument. Проверяет два процесса через настоящий NGO/UTP; синтетически перемещает только локальную capsule и наблюдает удалённый transform через сеть.
- `Packages/manifest.json`, `Packages/packages-lock.json` — пакеты и разрешённые версии зависимостей.

## Ограничения

Remote capsule не имеет активного collider: игроки пока могут проходить друг сквозь друга. Это исключает неустойчивое взаимное выталкивание двух owner-authoritative контроллеров. Столкновения собственного CharacterController с motel environment сохранены.

Owner-authoritative движение не проверяется сервером на допустимую скорость/позицию. Нет prediction/reconciliation, player collision resolution, сохранения сессии, автоматического reconnect, host migration и синхронизации pitch. При уходе host сессия завершается. Development UI предназначен для локального теста.

## Источники совместимости

Официальный Unity Package Registry указывает minimum Unity 6000.0 для NGO 2.13.3 и зависимость Unity Transport 2.6.0. Версии зафиксированы в manifest/lock, установлены и скомпилированы в Unity 6000.3.19f1.

- https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.netcode.gameobjects.html
- https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.13/manual/index.html
- https://docs.unity3d.com/Packages/com.unity.transport@2.6/manual/index.html

## Выполненные проверки

- Пакеты установлены; C# компилируется против реальных Unity/NGO assemblies.
- Unity создал prefab/сцену и повторно открыл сцену; configuration validation пройдена.
- macOS development build: **Succeeded, 0 errors, 0 warnings**.
- Запущены **два отдельных standalone процесса**, Host + Client, localhost UDP **17777** (тестовый port; UI по умолчанию 7777). Оба завершились с exit code 0.
- Оба процесса получили два отдельных PlayerObject, проверили ownership и раздельные spawn positions. Удалённые FPS input, camera, listener и CharacterController отключены; remote body включён. Активны одна камера и один AudioListener в каждом процессе.
- Host наблюдал перемещение client на **3.20 м / 80.2° yaw**; client наблюдал движение host на **3.20 м / 79.5° yaw**. Данные прочитаны из удалённого NGO NetworkObject, не из общего файла.
- Client Disconnect удалил его объект у host. Оба локальных shutdown завершились, network player objects удалены. Критических error/exception/assert logs не было.
- Это автоматический headless-тест с синтетическим движением своей CharacterController capsule. Он не проверяет физические нажатия WASD/мыши, GUI-клики, качество рендера и субъективную плавность. LAN, четыре участника, reconnect и отключение host при ещё подключённом client отдельно не тестировались.

Результаты: `setup-validation.txt`, `build-validation.txt`, `smoke-host.txt`, `smoke-client.txt`, соответствующие `.log`. Полный список файлов и hash-проверка окружения — `preservation.json`.

Дополнительно адаптированы прежние Editor-команды FirstPersonControllerValidation и MotelBlockoutV3Builder к сетевому prefab/отдельному NetworkSession root. Prefab registry расположен в `Assets/Game/Prefabs/NetworkPrefabs.asset`, GUID сохранён. Автоматические изменения URP/Project Settings от сборки восстановлены до исходных байтов.
