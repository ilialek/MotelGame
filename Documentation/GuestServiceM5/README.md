# M5 — Multiplayer Guest Service & Phone Requests

> С M7 используется один актуальный build: `Builds/Playtest/LaunchWindowed.command`. Это историческое описание milestone; текущие многокомнатный сценарий и тесты находятся в `Documentation/MultipleGuestsM7/README.md`. Оба участника должны запускать текущую версию. Старые M4–M6 single-guest тесты сохранены как история и не проверяют текущий M7 protocol.
В существующую `Motel_Blockout_V3` добавлен повторяемый цикл:
**M4 уборка → Ready → Check-in → через 15 секунд звонок → E Answer Phone → Pending Delivery → Utility: E Take Clean Towel → у двери номера: E Deliver Towel → Completed → Checkout → Dirty / No Request.**

Unity 6000.3.19f1, URP 17.3.0, NGO 2.13.3, Unity Transport 2.6.0. Пакеты не менялись. Настройка сцены и ссылки выполнены; ручное добавление компонентов не требуется.

## Проверка вдвоём: Unity Editor + оконная сборка

1. В Unity открыть `Assets/Game/Scenes/Motel_Blockout_V3.unity`, включить Play Mode, нажать **Start Host**.
2. Запустить `Builds/Playtest/LaunchWindowed.command` через Finder. Откроется актуальная сборка в окне 800×600. Нажать **Start Client**, адрес `127.0.0.1`, UDP port `7777`. Для второго компьютера — LAN IP хоста; оба должны использовать одну актуальную сборку.
3. Расположить окно сборки рядом с Game view Unity. **Esc** освобождает курсор для переключения окон, щелчок вне меню возвращает FPS-управление. WASD, мышь, Left Shift сохранены.
4. Подготовить Room 101 через M4: открыть дверь по E, заправить кровать, убрать оба предмета мусора. Статус должен стать Ready на обеих сторонах.
5. Вернуться к Reception. Навести центр камеры на основную стойку, **E — Reception — room service**, выбрать **Check in guest → Room 101**. Закрыть окно, щёлкнуть вне UI. Номер Occupied, гость внутри.
6. Через **15 секунд** телефон на стойке между мониторами начнёт звонить; надпись PHONE RINGING и общий HUD укажут Reception. Навести центр камеры именно на небольшой телефон и нажать **E — Answer Phone**. Можно подходить с любой доступной стороны стойки.
7. На обоих экранах появится **Room 101 — Clean Towel — Pending Delivery** и текст просьбы. Звонок прекращается. Игрок B идёт в Utility через существующий проход за Reception. На средней секции стеллажа лежит стопка полотенец с надписью CLEAN TOWELS.
8. **E — Take Clean Towel** выдаёт одно полотенце. Владелец видит его снизу справа; напарник видит полотенце перед капсулой игрока. Второе полотенце тому же игроку не выдаётся.
9. Пройти к Room 101. Рядом с дверью, под табличкой номера, находится небольшая настенная полка. Навести на неё центр камеры и нажать **E — Deliver Towel**. Открывать дверь для доставки не требуется. Статус у обоих становится Complete; полотенце исчезает, номер остаётся Occupied.
10. На Reception выполнить **TEST: Check out Room 101**. Запрос исчезнет; M4 восстановит Dirty, неубранную кровать и мусор. Повторить цикл, поменяв отвечающего и доставляющего.

Дополнительные ручные сценарии:
- Ответить одновременно двумя игроками; доставить с быстрыми повторными E. Состояние должно измениться один раз.
- В Pending Delivery взять по полотенцу обоими игроками, затем выполнить checkout. Запрос и оба полотенца должны исчезнуть.
- Отключить Client после ответа/получения полотенца хостом, подключить Client вновь. Он должен увидеть Pending Delivery и полотенце хоста; после доставки — Complete.
- Сделать checkout в течение 15-секундной задержки: позднего звонка быть не должно.

Можно открыть два окна сборки, запустив launcher дважды: первое Start Host, второе Start Client. Не запускайте второй Host на том же порту. Сборка включает Motel_Blockout_V3 напрямую, независимо от положения SampleScene в Build Profiles.

## Реализация и интеграция с M4

- `GuestServiceRequests` — один scene NetworkObject, ссылка на `ServiceRoom`, телефон, towel supply, delivery point, AudioSource и phone label. `NetworkVariable<GuestRequestSnapshot>` хранит Phase, Cycle, CallAt; записывает только сервер. Фазы: NoRequest, Ringing, PendingDelivery, Completed. CallAt — серверный срок звонка; после ответа/завершения нового звонка в том же цикле не будет.
- `GuestServiceInteraction` — три обычных `IInteractable`: телефон, стопка, доставка. Все используют прежние E/raycast, owner-only `PlayerInteractor`, prompt и feedback. Нет второй системы взаимодействий.
- `TowelCarrier` на существующем player prefab — один server-write `NetworkVariable<bool>`. Без inventory и item NetworkObject. Для владельца включается визуал под Camera, для остальных — под корнем игрока. Начальная синхронизация NGO восстанавливает переносимый предмет у late join.
- `ServiceRoom.StateChanged` — единственное изменение M4 runtime. Уведомление вызывается после применения существующего room snapshot. M5 подписывается на него; checkout в той же серверной операции отменяет запрос и очищает переносимые полотенца. Повторная проверка в Update нужна для порядка spawn scene NetworkObjects.
- Answer/Take/Deliver RPC определяют игрока через NGO sender, проверяют текущий Occupied/Cycle, допустимую фазу, дистанцию и препятствия через **существующий `ServiceInteractionValidation`**. Доставка дополнительно проверяет NetworkObject комнаты и server-owned carry flag. Клиент не сообщает серверу успешный результат.
- Новый цикл наследует Cycle номера. RPC предыдущего цикла, повторные ответы и доставки не меняют актуальное состояние. Последовательная обработка серверных RPC исключает двойное завершение.
- Телефон использует созданный для проекта `PhoneRing.wav`: два коротких сигнала и пауза, loop, пространственный AudioSource. Его включение зависит от реплицируемой фазы; HUD/надпись дают визуальную подсказку независимо от звука.
- HUD использует существующий IMGUI-подход. При reception modal он скрывается, управление курсором/камерой M4 не меняется.

**Принятые ограничения:** одно обслуживаемое помещение и один тип запроса за раз. Полотенца выдаются только при Pending Delivery, по одному на игрока, запас неограничен. При завершении или checkout все оставшиеся полотенца возвращаются в запас, чтобы игроки не носили ненужный предмет. Disconnect уничтожает предмет вместе с player object; активный запрос остаётся и другой игрок может взять полотенце.

В runtime нет выбора по имени Room101. Для другого номера назначить другой `ServiceRoom` в `GuestServiceRequests.room`, соответствующий `GuestServiceInteraction.deliveryRoom` и collider/позицию точки доставки; обновить текст её таблички в Inspector. Копировать scripts не требуется. Несколько одновременно активных сервисов/запросов в M5 не поддерживаются: не дублировать `GuestServiceRequests` как очередь заданий.

Delay настраивается в **GuestService → GuestServiceRequests → Call Delay**. Основные переходы для чтения кода: `SynchronizeRoom`, `AnswerRpc`, `TakeTowelRpc`, `DeliverRpc`. Прежний M4 README остаётся описанием уборки и регистрации.

## Что действительно проверено

- Предварительная компиляция C# против установленных Unity/NGO assemblies.
- Native Unity import/compilation и сохранённая сцена повторно открыты: все M5/M4 references, аудиоклип, player carry visuals и NetworkDoor валидны; missing scripts отсутствуют.
- macOS Development build: **Succeeded, 0 errors, 0 warnings** (`build-validation.txt`).
- Реальные standalone Host/Client через NGO/UTP localhost с последующей заменой клиента late join: **PASS** в `run-20261024-172631/{host,client,late}.txt`.
- Два полных M4+M5 цикла без перезапуска host: сначала host отвечает / client доставляет; затем client отвечает / host доставляет. Перед второй доставкой прежний client заменён late client, который получает активный запрос и визуал полотенца хоста, наблюдает завершение и выполняет checkout.
- Использовались настоящий CharacterController, камера, raycast, IInteractable и production RPC. Маршруты прошли через lobby, коридор, комнату, проход за Reception и Utility.
- Idle/ringing pickup и delivery, пустая доставка, доставка неверному NetworkObject комнаты, удалённые pickup/delivery, повторные answer/pickup/delivery и RPC старого цикла отклонены.
- Конкурирующие ответы и получение по одному полотенцу двумя игроками; checkout в Pending удаляет оба carry flags/визуала. Следующее заселение снова звонит; checkout в Ringing отменяет звонок.
- Ранее написанный полный M4 regression на сборке M5: **PASS**, `m4-regression-20261024-172631/`. Два цикла, конкурентная уборка/регистрация, недопустимые действия, позднее подключение, восстановление визуалов и interactions.
- Движение/ownership/cameras/disconnect: **PASS**, `regression-20261024-172630/`. Оба наблюдали около 3.2 м движения и 80° поворота другого игрока; только одна активная локальная камера/listener.
- NetworkDoor regression: **PASS**, `door-regression-20261024-172336/`. Open/close, closed collision, open passage, spam, late join, disconnect.
- В успешных сетевых прогонах не зарегистрировано Error/Exception/Assert.
- Native Unity preview-рендеры телефона, utility, delivery, полотенца от первого и третьего лица просмотрены: `preview-*.png`. Это рендеры world-space presentation, не скриншоты игрового IMGUI.
- `preservation-validation.json`: сверены 225 прежних файлов. Прежние transforms, renderers, colliders и lighting сцены сохранены; дверь, movement, материалы, Packages, ProjectSettings, SampleScene и PSX-ассеты не изменены. Полка доставки выступает на 0.14 м и сохраняет **2.66 м** коридора; дверной проём и sweep двери свободны.

Ранние диагностические FAIL сохранены: отсутствовавший у декоративного телефона collider исправлен; новый тест ожидал M4 guest visual до следующего Update — добавлено ожидание; старый movement test выбирал первый Renderer и попадал в новый towel visual — теперь проверяет RemoteBody. Это отражено в ранних отчётах; актуальные успешные результаты указаны выше. Unity автоматически обновил Mobile_RPAsset при сборке — файл восстановлен из backup, проверено побайтово.

## Что остаётся проверить вручную

Автотесты работают с `-batchmode -nographics`, вызывают реальные interaction/RPC entry points, но не нажимают физическую E и кнопки мышью. Доступ к native UI Unity из этого окружения недоступен (`Sky Computer Use service startup request failed`). Поэтому вручную остаются:
- удобство наведения на маленький телефон/полку, клавиши E, кнопки IMGUI, cursor lock при переключении окон;
- реальная слышимость/громкость/пространственное затухание звонка (клип и AudioSource назначены; остановка состояния проверена, звук не прослушивался);
- читаемость HUD в выбранном разрешении и субъективное качество carry visual;
- checkout до истечения call delay, LAN на двух физических компьютерах, 3–4 игрока, packet loss/latency.

Сохраняется owner-authoritative movement: дистанция проверяется по серверной копии позиции, это не защита от намеренного телепорта модифицированным клиентом. Нет сохранений, host migration, request queues или NPC AI. Все состояния начинают новую сессию с Dirty/No Request.

## Изменённые файлы

Существующие:
- `Assets/Game/Scenes/Motel_Blockout_V3.unity` — новый корневой GuestService и связанные точки/labels/audio; у прежнего Telephone добавлены BoxCollider и GuestServiceInteraction. Старые объекты не удалены и не перемещены.
- `Assets/Game/Prefabs/NetworkPlayer.prefab` — TowelCarrier и два изначально скрытых visual children; прежние movement/camera/body settings сохранены.
- `Assets/Game/Scripts/Service/ServiceRoom.cs` — событие StateChanged, без изменения state machine M4.
- `Assets/Game/Scripts/Networking/Tests/LocalMultiplayerSmokeTest.cs` — проверка конкретного RemoteBody вместо первого попавшегося Renderer.

Новые:
- `Assets/Game/Scripts/Service/GuestServiceRequests.cs`
- `Assets/Game/Scripts/Service/GuestServiceInteraction.cs`
- `Assets/Game/Scripts/Service/TowelCarrier.cs`
- `Assets/Game/Scripts/Networking/Tests/GuestServiceSmokeTest.cs`
- `Assets/Game/Editor/GuestServiceSetup.cs`
- `Assets/Game/Audio/PhoneRing.wav` и соответствующие `.meta`;
- `Documentation/GuestServiceM5/` — инструкция, проверки, рендеры, повторяемые тестовые runner scripts;
- `Builds/Playtest/` — текущая сборка и постоянный оконный launcher с M7.

Backup: `Backups/BeforeGuestServiceM5_20261024_172034/`. Прежние незакоммиченные изменения M4 сохранены; Git-коммит не создавался.

Editor menu: **Tools → Motel → Validate Guest Service M5**, **Build Guest Service M5**. Installer запускается только по явному request с SHA256 сцены/prefab и отказывается работать с несохранённой сценой. Не запускайте полную регенерацию blockout builder: она не сохраняет M4/M5 gameplay.

Повторить тесты на macOS после сборки:

```
python3 Documentation/GuestServiceM5/Tests/run_guest_test.py
python3 Documentation/GuestServiceM5/Tests/run_m4_regression.py
python3 Documentation/GuestServiceM5/Tests/run_movement_regression.py
python3 Documentation/GuestServiceM5/Tests/run_door_regression.py
```

Runner создаёт отдельный каталог результатов и завершает только собственные процессы. Порты тестов: M5 17780, M4 17779, door 17778, movement 17777. Тестовые behaviours не запускаются в обычной игре, активируются только явными command-line flags в Editor/Development build.
