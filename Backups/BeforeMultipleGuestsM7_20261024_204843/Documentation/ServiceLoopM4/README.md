# M4 — Multiplayer Motel Service Loop

Реализован один полный повторяемый цикл Room 101:
**Dirty → Make Bed + Remove Trash (2 объекта) → Ready → Check-in → Occupied → TEST Checkout → Dirty.**

Unity 6000.3.19f1 / URP 17.3.0 / NGO 2.13.3 / Unity Transport 2.6.0. Пакеты не изменялись.

## Как протестировать вдвоём

1. В Unity открыть `Assets/Game/Scenes/Motel_Blockout_V3.unity`, включить Play Mode и нажать **Start Host**.
2. Запустить `Builds/ServiceLoopM4/LaunchWindowed.command`: откроется актуальная сборка в окне 800×600. Нажать **Start Client**, указав IP хоста (на том же Mac — `127.0.0.1`, UDP port `7777`). Старые сборки других milestones не содержат M4.
3. Щёлкнуть вне меню для управления игроком. WASD / мышь / Left Shift; Esc освобождает курсор. Для переключения между двумя окнами нажимать Esc.
4. Оба игрока идут к Room 101 через существующий коридор и открывают дверь по E. Табличка возле входа показывает **Dirty**, `Bed: unmade`, `Trash: 0/2`.
5. Один игрок смотрит на матрас и нажимает **E — Make Bed**. Другой смотрит на два небольших коричневых объекта на полу возле изножья/шкафа и нажимает **E — Remove Trash** на каждом. Одеяло расправляется, мусор исчезает на обоих клиентах.
6. Только после обеих задач табличка показывает **Ready**. Вернуться к reception и навести центр камеры на основную стойку с надписью Reception. **E — Reception — room service** открывает окно.
7. Выбрать **Check in guest → Room 101**. Номер становится **Occupied**; placeholder гостя исчезает из зоны ожидания и появляется в номере. Это намеренно мгновенный переход, без AI-навигации.
8. В том же окне нажать **TEST: Check out Room 101**. Номер становится **Dirty**, кровать снова неубрана, оба объекта мусора возвращаются, гость снова ожидает на reception. Закрыть окно через Close/Esc; щёлкнуть в игровую область для возврата управления.
9. Выполнить цикл повторно, поменявшись задачами и игроком, выполняющим регистрацию/checkout.

Dirty и Occupied комнаты отображаются в окне, но кнопка заселения отключена. Если другой игрок уже выполнил действие, сервер отклонит повтор и покажет пояснение. Тестовый checkout намеренно подписан; таймеров, гостевых расписаний и игровой экономики нет.

## Структура реализации

- `ServiceRoom`: настройки номера, ссылки на кровать, массив мусора, таблички и визуал гостя. Одна server-write `NetworkVariable<RoomSnapshot>` хранит Status, BedMade, RemovedTrash и Cycle. Checkout сбрасывает весь snapshot одним присваиванием и увеличивает Cycle.
- `CleaningSpot`: существующий `IInteractable` на матрасе и двух объектах мусора. Индекс `-1` означает кровать, `0..N-1` — элементы массива мусора. Нет отдельного сетевого объекта на каждый кусок мусора.
- `ReceptionDesk`: список настроенных номеров, проверка регистрации/checkout и локальное окно IMGUI. В M4 обслуживается один гость; его положение выводится из Occupied-состояния номера, поэтому отдельное сетевое состояние гостя не может разойтись с room state.
- `ReceptionInteraction`: связывает существующую стойку с reception, не меняя её геометрию.
- `ServiceInteractionValidation`: сервер сам определяет игрока по RPC sender, проверяет существование player object, дистанцию 2.5 м и препятствия. Собственная капсула игрока исключена из проверки.
- `PlayerInteractor`: прежний механизм E/raycast/ownership. Добавлены подписи действий, сообщения результата и блокировка взаимодействий при открытом меню. Луч исключает свою капсулу, сохраняя блокировку стенами и прочими объектами.
- `WorldServiceText` и один URP shader: небольшие таблички с проверкой глубины, без просвечивания через стены. Каждый новый текст использует собственный временный material; существующие materials и font material не изменяются.

Номер комнаты и связи задаются в Inspector, runtime-код не выбирает объекты по имени Room101. Для другого номера нужно настроить другой `ServiceRoom`, кровать, массив мусора (1–31), guest visual и status signs, затем добавить room в `ReceptionDesk.rooms`. Копировать или менять runtime scripts не требуется. Editor installer создаёт только исходный Room 101; Room 102 в M4 не добавлялся и не тестировался.

Каждый запрос передаёт номер текущего service cycle. Сервер проверяет его и допустимость перехода. Повторные cleaning/check-in/checkout не складываются; запросы предыдущего цикла отклоняются. Последовательное выполнение серверных RPC исключает двойное заселение одного гостя.

## Проверки, которые действительно выполнены

- Предварительная C#-компиляция против установленных Unity/NGO assemblies.
- Native Unity import, compilation и повторное открытие сохранённой сцены: ссылки комнат/уборки/reception/guest/status signs проверены, missing scripts не обнаружены, существующая NetworkDoor сохранена.
- macOS Development build: **Succeeded, 0 errors, 0 warnings**.
- Два standalone-процесса через localhost NGO/UTP, затем замена клиента новым late-join клиентом: **PASS**. Итоговые результаты: `run-20261024-163310/{host,client,late}.txt`.
- Два полных цикла без перезапуска хоста, с обменом ролями уборки. Использовались реальные CharacterController, camera raycast, `IInteractable`, reception menu entry point и RPC. Bed + первый мусор оставляли Dirty; последний мусор переводил в Ready.
- Конкурирующие/повторные cleaning, check-in и checkout; отказ удалённому запросу, Dirty check-in, недопустимому индексу, уборке Occupied, повторному заселению и запросу старого цикла.
- Синхронизация occupancy/guest visuals; проверка made/unmade GameObject кровати, восстановление доступности всех cleaning interactions и trash colliders после обоих checkout.
- Late join в Occupied после первого checkout и второго заселения: правильные cycle, bed state, removed trash и guest location. Новый клиент выполнил второй checkout.
- Отдельная регрессия движения и cameras: `regression-20261024-162613/`, **PASS**. На обеих сторонах наблюдались около 3.2 м перемещения и 80° поворота удалённого игрока; ownership и disconnect проверены.
- Отдельная регрессия двери: `door-regression-20261024-162632/`, **PASS**. Host open, client close, closed collision, open passage, spam, distance denial, late join, disconnect.
- В успешных сетевых прогонах не зарегистрировано critical Error/Exception/Assert.
- Нативные рендеры сцены `preview-*.png` проверены визуально. На них видны новые bedding/labels; исправлены чрезмерный размер и просвечивание текста.
- `preservation-validation.json`: прежние материалы, свет, геометрия, FPC, player/door prefabs, SampleScene, Packages/ProjectSettings и уже импортированный пользователем PSX Mega Pack сохранены.

Ранние диагностические `run-*` с FAIL оставлены для прозрачности. Они выявили попадание луча в собственную капсулу (исправлено) и маршрут автотеста в угол санузла (исправлен только маршрут). Итоговый успешный M4 run указан выше.

## Ручная проверка и ограничения

Автоматические сетевые тесты выполнялись без графического окна (`-batchmode -nographics`). Они вызывают тот же raycast/interaction/RPC и действия меню, но не эмулируют клавишу E и клики мышью. Реальное управление курсором, кнопками IMGUI и субъективная читаемость подсказок требуют ручного Host/Client теста. Нативные screenshot-проверки относятся к world-space visuals, а не к игровому IMGUI.

В этом запуске не проверялись LAN между двумя компьютерами, packet loss/latency и 3–4 клиента. Сохранён owner-authoritative movement; distance validation опирается на серверную копию позиции игрока и не является защитой от злонамеренного телепорта.

Гость — переиспользуемый placeholder: без маршрута, расписания, диалога и сохранения состояния между сессиями. Начало новой сессии снова даёт Dirty. Сетевой хост должен оставаться запущен. Полная генерация сцены старым blockout builder не сохраняет добавленный gameplay; работать нужно с текущей сценой, а не регенерировать её.

Подтверждённых блокеров полного M4 loop после финальных тестов нет. Ручная проверка UI остаётся обязательной перед пользовательским playtest.

## Файлы и backup

Новые runtime-файлы в `Assets/Game/Scripts/Service/`:
`ServiceRoom.cs`, `CleaningSpot.cs`, `ServiceInteractionValidation.cs`, `ReceptionDesk.cs`, `ReceptionInteraction.cs`, `WorldServiceText.cs`.

Также добавлены:
- `Assets/Game/Shaders/WorldServiceText.shader`;
- `Assets/Game/Editor/ServiceLoopSetup.cs`;
- `Assets/Game/Scripts/Networking/Tests/ServiceLoopSmokeTest.cs`;
- соответствующие `.meta`, документация, отчёты и отдельная сборка M4.

Изменены:
- `Assets/Game/Scenes/Motel_Blockout_V3.unity`: новые service objects, два interaction components на существующих матрасе/стойке; Room101 scale-placeholder используется как зарегистрированный гость и изначально скрыт. Ни один старый объект не удалён, Transform геометрии и мебели не изменены.
- `Assets/Game/Scripts/Interactions/IInteractable.cs`: свойство Prompt.
- `Assets/Game/Scripts/Interactions/PlayerInteractor.cs`: подписи/feedback, menu guard, исключение собственной капсулы.
- `Assets/Game/Scripts/Interactions/NetworkDoor.cs`: только свойство Prompt; сетевой алгоритм двери прежний.
- `Assets/Game/Scripts/Networking/DevelopmentSession.cs`: при reception menu блокируется ввод и скрывается перекрывающая панель соединения.
- `Assets/Game/Editor/MotelBlockoutV3/MotelBlockoutV3Builder.cs`: service visuals исключены из проверки количества статических blockout-примитивов.

Backup перед изменениями: `Backups/BeforeServiceLoopM4_20261024_161040/`. Git-коммит не создавался. Неотслеживаемый PSX Mega Pack принадлежал проекту до M4 и не менялся.

Editor menu: **Tools → Motel → Validate Service Loop M4** и **Build Service Loop M4**. Сборка включает только Motel_Blockout_V3, поэтому не зависит от позиции SampleScene в Build Profiles. Editor tool также обрабатывает явные `.request` файлы в Library для автоматической проверки; без них ничего не собирает и не меняет. Перед установкой/изменением сцены проверяет hash и отсутствие несохранённых правок.

Повторить автоматические проверки на macOS можно через `python3 Documentation/ServiceLoopM4/Tests/run_service_test.py` после сборки. Он запускает Host/Client, затем late client, и сохраняет новый отдельный каталог результатов. Аналогично доступны `run_movement_regression.py` и `run_door_regression.py`. Тесты завершают только запущенные ими процессы.
