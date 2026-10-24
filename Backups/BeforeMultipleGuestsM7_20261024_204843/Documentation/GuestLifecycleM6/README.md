# M6 — Networked Guest NPC Lifecycle

В `Motel_Blockout_V3` настроен один физический гость:
**Parking → Reception → ожидание регистрации → M4 Check-in → проход через дверь Room 101 → InRoom → M5 звонок через 15 секунд → полотенце → TEST Checkout → выход на парковку → следующий визит.**

Все компоненты, prefab, ссылки и сохранённый NavMesh уже находятся в сцене. Стены, мебель, Reception, парковка, материалы, освещение и настройки игроков сохранены.

Unity 6000.3.19f1; URP 17.3.0; NGO 2.13.3; Unity Transport 2.6.0; AI Navigation 2.0.13. Пакеты не добавлялись и не обновлялись: AI Navigation уже был установлен.

## Ручная проверка: два видимых окна

1. Открыть `Assets/Game/Scenes/Motel_Blockout_V3.unity` в Unity, дождаться компиляции. Play → **Start Host**.
2. В Finder запустить `Builds/GuestLifecycleM6/LaunchWindowed.command`. Откроется актуальная сборка M6 в окне 800×600. **Start Client**, адрес `127.0.0.1`, порт `7777`. Расположить рядом с Game view Unity. Альтернатива: запустить launcher дважды, Host в первом окне и Client во втором.
3. **Esc** освобождает курсор; щелчок вне UI возвращает управление. WASD, мышь, Left Shift сохранены. Использовать M6 на обеих сторонах: прежние M4/M5 builds не совместимы с новым составом сцены и форматом DoorState.
4. Через 3 секунды гость появляется на парковке и идёт к Reception (приблизительно 10 секунд). Он останавливается перед стойкой, лицом к сотрудникам. Ничего не регистрируется автоматически. Не стойте на его пути.
5. Пока Room 101 Dirty, регистрация недоступна. Открыть дверь по E, заправить кровать, убрать оба предмета мусора. На обоих экранах должен появиться Ready.
6. На Reception: **E → Check in guest → Room 101**. В первый раз регистрацию выполняет Client. Оба игрока должны видеть Occupied и одного гостя, идущего в гостевое крыло. Убедиться, что старая капсула-гость не появляется в комнате.
7. Оставить дверь Room 101 закрытой и пройти за гостем, не перекрывая узкий дверной проём. Он сам откроет дверь, войдёт, закроет её и остановится внутри. Пока он проходит, prompt двери — **Guest using door**. Дверь не закрывается на NPC или игроке, стоящем в зоне её движения.
8. Только после прихода в комнату отсчитываются 15 секунд до звонка. Один игрок отвечает на телефон по E, другой берёт полотенце в Utility и доставляет на существующую полку возле Room 101. После Complete гость остаётся внутри, номер — Occupied.
9. На Reception выполнить **TEST: Check out Room 101**. Сразу восстановятся Dirty, кровать и мусор; M5 и переносимые полотенца сбросятся. Гость выходит через дверь и идёт к парковке. Он скрывается только у точки выхода; через 6 секунд начинается следующий визит.
10. Повторить полный цикл, поменяв обязанности. NPC не должны накапливаться.

Дополнительные проверки:
- Открыть Reception до прихода гостя: информация о комнате доступна, Check-in отсутствующего гостя недоступен.
- Одновременно нажать Check-in на Host и Client: должно произойти одно заселение.
- Выполнить checkout, пока гость ещё идёт по лобби, и отдельно во время прохода через дверь. Он должен развернуться к выходу, без последующего звонка.
- Ответить на звонок, взять полотенца обоими игроками, выполнить checkout до доставки: просьба и оба полотенца исчезают.
- Встать на пути NPC, затем отойти: он ждёт и продолжает движение. Это не симуляция толпы; не окружайте гостя несколькими игроками.
- Переподключить Client в WaitingForCheckIn, WalkingToRoom, InRoom и Leaving: один NPC, правильные поза, состояние и назначенная комната.

Для LAN указать IP хоста вместо localhost и разрешить UDP 7777 локальным firewall. LAN на двух физических компьютерах здесь не проверялся.

## Реализация

`GuestLifecycle` — один scene NetworkObject с server-write `NetworkVariable<GuestSnapshot>`. Snapshot хранит Phase, Visit, RoomCycle, Assigned, Blocked и `NetworkObjectReference` комнаты. Состояния: Absent, Arriving, WaitingForCheckIn, WalkingToRoom, InRoom, Leaving. Решения и NavMeshAgent работают только на сервере; стандартный server-authoritative `NetworkTransform` передаёт позицию/поворот клиентам и late join.

Объект переиспользуется между визитами. В Absent визуал и collider отключены. Перенос к arrival point возможен только перед новым визитом после завершённого выхода; активные маршруты проходят физически, без Warp между этапами. Prefab содержит простого humanoid ростом 1.8 м, CapsuleCollider radius 0.28 м и NavMeshAgent radius 0.3 м, speed 1.6 м/с.

`ReceptionDesk` использует прежние UI, E/raycast, server RPC и проверки досягаемости. Регистрация дополнительно требует настоящего ожидающего NPC, соответствующего ServiceRoom, Ready и актуального Cycle. `ServiceRoom` остаётся владельцем Dirty/Ready/Occupied и уборки. Старые waiting/in-room placeholders сохранены, но отключены при подключённом физическом госте. Дополнительный статичный blockout-маркер QueueGuest_1.8m также отключён, чтобы не изображать второго посетителя.

`GuestServiceRequests` использует прежнюю машину состояний M5. Изменён только момент назначения CallAt: Room Occupied **и** `guest.IsInRoomFor(room)`. Checkout по прежнему событию комнаты сразу отменяет запрос и carry flags; GuestLifecycle не дублирует телефонный сценарий.

`NetworkDoor` использует прежние DoorState, серверное открытие и анимацию. Добавлена короткая серверная резервация `GuestPassage` на время прохода. NPC ждёт полного открытия, проходит к точке по другую сторону, освобождает дверь; закрытие ждёт освобождения зоны движения игроками и гостем. Попытки игрока переключить дверь во время резервации отклоняются. Нового независимого механизма двери нет.

При checkout маршрут выбирается по уже пройденному этапу: из лобби — прямо к выходу, из комнаты — через дверь, посреди прохода — обратно наружу с сохранением резервации. Геометрическая проверка стороны бесконечной плоскости двери здесь непригодна: лобби лежит в той же полуплоскости, что и номер.

Навигация рассчитывает полный NavMesh path. Capsule sweep проверяет реальное перемещение относительно геометрии и двери; позиции remote players проверяются отдельно, поскольку их CharacterController на хосте отключён. При блокировке NPC останавливается и пытается продолжить; UI Reception показывает блокировку. Ошибка пути не переводит его в InRoom. Нет teleport recovery через стены.

## Настройка и поддержка

В сцене:
- `NetworkGuest` — экземпляр `Assets/Game/Prefabs/NetworkGuest.prefab`.
- `GuestLifecycleNavigation` — NavMeshSurface и дочерние `Destinations`: ParkingArrival, ReceptionWaiting, RoomDoorOutside, RoomDoorInside, RoomIdle, ParkingDeparture.
- `ReceptionDesk.guest`, `ServiceRoom.physicalGuest`, `GuestServiceRequests.guest` ссылаются на этот NPC.

Gameplay-код не выбирает Room 101 по имени и не содержит координат сцены. Чтобы использовать другой номер, назначить его ServiceRoom, NetworkDoor и точки outside/inside/idle, а также согласовать конфигурацию существующих Reception/M5. Несколько одновременных гостей/занятых комнат не входят в M6.

Сохранённый `MotelGuestNavMesh.asset` построен по фактическим colliders с локальным профилем radius 0.3, height 1.8, climb 0.15, voxel 0.04. Подвижная дверь исключена из bake; её прохождение контролируется в runtime. Глобальные agent settings проекта не менялись. **Не заменять этот bake обычной кнопкой NavMeshSurface Bake без переноса этих параметров и исключения подвижной двери**: глобальный default agent имеет другой radius.

Фактические пути: Parking→Reception 16.04 м; Reception→outside двери 16.53 м; crossing 3.10 м; inside→idle 1.02 м; outside→ParkingDeparture 35.69 м. Arrival/idle positions и задержки редактируются в Inspector. Маршруты учитывают существующий поворот коридора.

Editor menu: **Tools → Motel → Validate Guest Lifecycle M6**, **Rebake Guest Navigation M6**, **Build Guest Lifecycle M6**. Перед Editor-командами выйти из Play Mode. Для изменения статической геометрии или destination points использовать Rebake Guest Navigation M6: он сохраняет профиль и GUID navigation asset. Validator повторно открывает сохранённую сцену, проверяет ссылки и полные пути, создаёт preview-рендеры. Builder собирает отдельное macOS Development приложение. Installer требует явного request и совпадающего SHA256 исходной сцены, отказывается перезаписывать уже установленный M6 или несохранённые изменения. Повторная генерация всего blockout builder не сохраняет интеграцию milestone gameplay — для M6 она не нужна.

## Реально выполненные проверки

- Native Unity import/компиляция и macOS Development build: **Succeeded, 0 errors, 0 warnings** (`build-validation.txt`).
- Сохранённая сцена повторно открыта: один NPC, правильная server authority, все ссылки M4/M5/навигации, missing scripts отсутствуют (`setup-validation.txt`). Все восемь направлений маршрутов имеют полный NavMesh path (`navigation-validation.txt`).
- Основной Host/Client прогон **PASS**: `run-20261024-183335/`. Два полных M4+M5+NPC цикла; Client выполняет первое заселение; конкурирующие повторные регистрации/checkout; на втором цикле игроки меняются обязанностями. NPC прошёл **207.7 м**, все шесть состояний; проверялись непрерывность движения и проникновение в solid colliders. Активные маршруты не использовали teleport.
- Late join **PASS** во всех четырёх состояниях — WaitingForCheckIn, WalkingToRoom, InRoom, Leaving. На входе в InRoom наблюдатель также получил PendingDelivery и переносимое полотенце. Во всех случаях один server-owned NPC, client NavMeshAgent выключен.
- Client checkout во время движения из Reception **PASS**: NPC идёт к парковке, M4 сброшен, отложенный звонок не появляется; следующий посетитель прибывает без перезапуска.
- Дополнительный Host/Client прогон **PASS**: `edge-20261024-183334/`. Реальный игрок перекрывает путь NPC, затем освобождает его; NPC ждёт и продолжает движение. Client выполняет checkout посреди зарезервированного дверного проёма: гость безопасно разворачивается, выходит, дверь закрывается, следующий посетитель прибывает. Checkout в PendingDelivery очищает просьбу и оба полотенца/визуала, отклоняет RPC старого цикла. Пройдено ещё **163.9 м**; solid collisions и continuity также проверялись.
- Движение, ownership, камеры, AudioListener, remote movement/rotation и disconnect **PASS**: `regression-20261024-181028/`.
- Регрессия существующей двери **PASS**: `door-regression-20261024-181028/` — Host/Client open/close, server distance denial, collision закрытой двери, проход CharacterController при открытой, повторные RPC, late join и disconnect.
- В перечисленных успешных сетевых прогонах нет Error/Exception/Assert. Короткое ожидание перед игроком может сопровождаться сообщением Guest navigation; это диагностическое предупреждение о блокировке, а не ошибка состояния.
- Сверка 237 прежних файлов и исходных YAML-компонентов сцены **PASS** (`preservation-validation.json`). Прежние transforms/colliders/renderers/lights побайтово сохранены.

Диагностические FAIL сохранены отдельно. Первый прогон ожидал конечную позу клиента раньше, чем завершилась NetworkTransform interpolation; ожидание исправлено. Проверка progress NPC при очень высоком FPS учитывала слишком маленькое перемещение за кадр; теперь учитывается накопленное расстояние. Второй прогон обнаружил настоящую ошибку checkout из лобби: положение относительно плоскости двери ошибочно выбирало маршрут через закрытую дверь. Маршрут теперь выбирается по завершённому этапу; полный повторный прогон после исправления указан выше. Дополнительная проверка препятствия обнаружила пропуск sweep при субмиллиметровом перемещении на очень высоком FPS; порог убран. Сценарий препятствия затем уточнён, чтобы тестовый Client не блокировал NPC раньше выбранной точки у двери; клиентская проверка остановки ждёт завершения интерполяции.

Все сетевые тесты запускаются отдельными настоящими standalone процессами через NGO/Unity Transport localhost с `-batchmode -nographics`. Игроки перемещаются CharacterController; E-сценарии вызывают реальные PlayerInteractor/raycast/IInteractable и production RPC. Кнопки Reception вызываются через тот же публичный обработчик, что у IMGUI. NPC проходит маршруты своим production NavMeshAgent; тест не переносит его между состояниями. Барьеры файлов синхронизируют действия тестов, не игровые состояния.

Повторить на macOS после сборки:

```
python3 Documentation/GuestLifecycleM6/Tests/run_lifecycle_test.py
python3 Documentation/GuestLifecycleM6/Tests/run_edge_test.py
python3 Documentation/GuestLifecycleM6/Tests/run_movement_regression.py
python3 Documentation/GuestLifecycleM6/Tests/run_door_regression.py
```

Development-only test behaviour активируется только явным аргументом командной строки. Обычная игра не запускает его. Порты: lifecycle 17781, edge 17782, door 17778, movement 17777. Runner сохраняет отдельный каталог результатов и завершает только свои процессы. Старые M4/M5 service runner scripts остаются историческими проверками прежних milestones: они предполагают мгновенное появление placeholder после Check-in. В M6 их сценарии уборки/телефона/доставки проверяет новый интеграционный тест с ожиданием физического прихода гостя.

## Ограничения и ручная проверка

- Подходящей модели с walking animation в проекте нет. Использован временный humanoid со статичными конечностями: ноги скользят при ходьбе. Направление движения задаёт NavMeshAgent, ожидание использует заданный yaw. Финальная анимация не реализована.
- Управление native UI Unity из окружения недоступно (`Sky Computer Use service startup request failed`). Графический Play Mode с физическими клавишами/мышью не проверялся. Проверены native preview-рендеры персонажа в parking/reception/room; это не скриншоты реального gameplay.
- Вручную оценить плавность поворотов/интерполяции на экране, визуальную заметность прибытия, удобство прохода рядом с NPC, кнопки UI/cursor lock, слышимость звонка. Звук M5 не менялся и здесь не прослушивался.
- Один гость и одна настроенная комната; нет толпы, динамического перестроения motel NavMesh, save/load, Steam, host migration. Если игрок перекрывает путь, он должен освободить его; сложный обход групп игроков не реализован.
- LAN на разных компьютерах, искусственная задержка/потери пакетов и четыре одновременных игрока не проверены. В late join тесте использовались Host, основной Client и временный третий наблюдатель.
- Сохраняется owner-authoritative player movement; это не защита от намеренного перемещения модифицированным клиентом. NPC и service state принадлежат серверу.

## Изменённые файлы и backup

Изменены существующие:
- `Assets/Game/Scenes/Motel_Blockout_V3.unity` — новые NPC/navigation roots, ссылки M4/M5, отключение прежних waiting/queue visuals. Ни один прежний объект не удалён/не перемещён.
- `Assets/Game/Scripts/Service/ReceptionDesk.cs` — check-in физического гостя и его статус в прежнем UI.
- `Assets/Game/Scripts/Service/ServiceRoom.cs` — отключение старого in-room placeholder при физическом госте.
- `Assets/Game/Scripts/Service/GuestServiceRequests.cs` — старт задержки после InRoom.
- `Assets/Game/Scripts/Interactions/NetworkDoor.cs` — резервация прохода NPC и безопасное закрытие.
- `Assets/DefaultNetworkPrefabs.asset` — NGO зарегистрировал новый NetworkGuest prefab.

Добавлены, вместе с `.meta`:
- `Assets/Game/Scripts/Service/GuestLifecycle.cs`
- `Assets/Game/Scripts/Networking/Tests/GuestLifecycleSmokeTest.cs`
- `Assets/Game/Editor/GuestLifecycleSetup.cs`
- `Assets/Game/Prefabs/NetworkGuest.prefab`
- `Assets/Game/Navigation/MotelGuestNavMesh.asset`
- `Documentation/GuestLifecycleM6/` — инструкция, результаты, previews и повторяемые runners.
- `Builds/GuestLifecycleM6/` — отдельная сборка M6 и оконный launcher.

Backup: `Backups/BeforeGuestLifecycleM6_20261024_175913/`. Сохранены исходная сцена, scripts, prefabs и settings. Сверка 237 прежних файлов — `preservation-validation.json`. Player prefab/контроллер, материалы, свет, мебель, геометрия, Packages, ProjectSettings, SampleScene и PSX assets не изменены. Прежняя незакоммиченная работа сохранена; коммит не создавался.
