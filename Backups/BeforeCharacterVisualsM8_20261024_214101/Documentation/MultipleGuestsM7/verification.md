# M7 — выполненные проверки, 24 октября 2026

## Результат

Unity development build **Succeeded, 0 errors, 0 warnings**. Постоянный выход: `Builds/Playtest/MotelGame.app`; launcher: `Builds/Playtest/LaunchWindowed.command`. macOS standalone исполнялся в отдельных процессах через localhost / Unity Transport.

| Проверка | Фактический результат |
|---|---|
| Сохранение/повторное открытие сцены, отсутствующие scripts, Inspector references, NGO scene IDs | PASS — 4 комнаты, 5 server-owned NPC, один Reception и один phone service |
| NavMesh | PASS — 25 полных путей, включая вход/выход каждой комнаты и парковку |
| Геометрия дверей | PASS — все четыре sweep 0–90° с шагом 2° без проникновения; CharacterController проходит открытую дверь и блокируется закрытой |
| Три одновременных гостя | PASS — фактически четыре гостя в 101–104, пятый ждёт при заполненном мотеле |
| Независимость уборки | PASS — подготовка 102 не меняет состояние/жильца 101 |
| Независимость checkout | PASS — checkout102 возвращает только102 в Dirty; гости101/103 остаются InRoom; физический уход102 |
| Повторное использование | PASS — другая NPC identity в102, два полных occupancy/request/delivery/checkout цикла без перезапуска; затем третий check-in с checkout в пути |
| Телефон/полотенце | PASS — запросы из всех четырёх комнат; доставка102 в101 отвергнута; правильная102 завершена; запрос103 отменён его checkout; checkout104 не отменил чужой102 и не очистил полотенца |
| Конкурентные действия | PASS — повторные check-in/checkout/delivery с Host и Client, одна корректная транзакция; устаревший request ID не меняет следующий запрос |
| Late join | PASS — третий standalone увидел четыре Occupied комнаты, пять NPC, правильные assignments/позиции/уборку, PendingDelivery102 и два carry visuals; client NavMeshAgents выключены |
| Встречные NPC | PASS — прибывающий в104 и уходящий из103 разошлись и завершили маршруты; 547.4 м суммарного физического движения, sampled проверки геометрии/взаимных пересечений и непрерывности |
| Player regression | PASS — отдельные player objects/spawns, owner-only input/camera/listener/CC, удалённые позиции и yaw, отключение/удаление player object |
| Door regression | PASS — owner raycast, host/client open/close, закрытый коллайдер, проход открытого, отвергнутый дальний запрос, 20 RPC → один переход, late join получил open pose |
| Disconnect | PASS — observer уходит без изменения состояния комнаты/гостей; основной Client отключается, Host проверяет состояние, затем shutdown |
| Критические runtime logs | Не обнаружены в успешных Host/Client/observer прогонах |

Доказательства:
- `run-20261024-210951/{host,client,observer}.txt` — полный M7.
- `regression-20261024-211005/{host,client}.txt` — игроки.
- `door-regression-20261024-211003/{host,client,late}.txt` — дверь.
- `setup-validation.txt`, `navigation-validation.txt`, `build-validation.txt`, `preservation-validation.txt` — native Unity и сравнение assets.

В тестах встречаются предупреждения `Guest waiting for an obstruction to clear`: NPC временно останавливался, пока тестовые игроки стояли на пути. Все проверенные маршруты затем завершились. Это не runtime exception.

## Исправления в ходе проверки

Первичная настройка дверей103/104 предполагала простое смещение101/102 на5.7м. Native path validation выявила фактическое отличие проёмов; новые двери/точки/таблички/подносы настроены по существующим стенам. Стены и мебель не перемещались. `SendMessage(OnValidate)` вызывал Editor assertion на соседних компонентах; точечный вызов NGO validation устранил причину, последующие build/validation успешны.

Старый door regression выбирал первый найденный NetworkDoor; при четырёх дверях он проверял проход101 после открытия102. Тест теперь явно выбирает Room101Door и прошёл повторно. Первый M7-прогон остановился на допуске позиции уходящего NPC на клиенте: сетевой snapshot скрытия приходил раньше завершения интерполяции отображаемого transform. Строгий допуск0.3м оставлен на Host, на Client —0.8м; полный сценарий после корректировки прошёл. Gameplay-код при этих двух исправлениях тестов не менялся. Неуспешные диагностические логи сохранены.

## Сохранность проекта и очистка

Backup до M7: `Backups/BeforeMultipleGuestsM7_20261024_204843/`.

Сравнение исходной сцены с backup: существующие transform positions/rotations/scales, Collider, Renderer и Light компоненты не изменены. Изменения прежних GameObjects: добавление CleaningSpot к трём матрасам и деактивация трёх старых дверных заглушек. Дополнены списки дочерних объектов и service references. Player scripts/prefab, Packages, ProjectSettings и исходные материалы совпадают с исходными файлами побайтно. Все проверки относятся к состоянию перед M7, а не к старому Git HEAD.

После успешного финального standalone теста удалены только проверенные старые `.app`, пустые generated Burst debug directories и launchers в ServiceLoopM4, GuestServiceM5, GuestLifecycleM6, LocalMultiplayer, NetworkedInteractions. Список — `build-cleanup.json`; логический объём удалённых файлов: 1,665,776,107 bytes. Источники, сцены, тесты, документация и backups сохранены.

Автоматическая approval review отклонила включение `.DS_Store` в очистку. Выполнен безопасный вариант: эти файлы оставлены на месте, поэтому старые каталоги содержат только метаданные Finder, без сборок/launchers. Единственная актуальная standalone сборка — Playtest.

## Что ещё проверить вручную

- Native UI automation недоступна: `Sky Computer Use service startup request failed`. Реальные процессы запускались с `-batchmode -nographics`; пиксели интерфейса и звук не оценивались.
- Посмотреть имена/цвета гостей, таблички всех комнат, прокрутку Reception при800×600, HUD источника, carry visuals в графическом режиме.
- Оценить мышь, отзывчивость движения, визуальную плавность NetworkTransform, звук звонка, ручную расстановку окон. Автотест проверял ownership камеры и её interaction raycasts, но не физический ввод мыши.
- LAN между отдельными компьютерами, latency/loss, Windows build и длительный soak не запускались.
- NPC остаются placeholder без walking animation. Пять NPC переиспользуются, очередь у Reception ограничена одним гостем. Всего один глобальный активный телефонный запрос. Сохранение смены и host migration отсутствуют по scope.
