# M15 — изменения и сохранность проекта

Сравнение выполнено с резервной копией непосредственно перед M15, а не с Git HEAD: изменения M14 ещё не были закоммичены. Backup: `Backups/BeforeBaggageM15_20261026_213724/` (Assets, Packages, ProjectSettings, Documentation, предыдущий стабильный Mac app).

## Изменённые существующие файлы

| Файл | Изменение |
|---|---|
| Assets/Game/Scenes/Motel_Blockout_V3.unity | Отдельный BaggageInspection_M15; станция, 14 ссылок на случаи, сумки пяти существующих NPC, четыре точки размещения; настройки темпа Mimic. Пребывание NPC 120 → 300 секунд. |
| Assets/Game/Scripts/Service/GuestLifecycle.cs | Начало/сброс багажа при посещении, короткая пауза при его возвращении. |
| Assets/Game/Scripts/Service/ReceptionDesk.cs | Проверка минимальной процедуры, атомарные допуск/отказ через существующее право работы за компьютером. |
| Assets/Game/Scripts/Service/ReceptionComputer.cs | Общая вкладка Baggage: ответы, результаты, допуск и отказ. |
| Assets/Game/Scripts/Service/NightShiftController.cs | Начало/завершение процедуры, итоги бизнеса и безопасности, подготовка debug-сценариев. |
| Assets/Game/Scripts/Horror/MimicHunt.cs | Физическое проникновение из багажа, исследование сети, выбор цели, настраиваемые интервалы; обход с приоритетом реже посещённых узлов. |
| Assets/Game/Scripts/Horror/MimicPresentation.cs | Сжатая поза при выходе из сумки, корректная начальная поза при включении. |
| Assets/Game/Scripts/Horror/VentNode.cs | Открытие решётки при входе существа. |
| Assets/Game/Scripts/Horror/HuntEquipment.cs | Исправлена Unity-null проверка вспышки при despawn thermal decoy. |
| Assets/Game/Scripts/Horror/MimicDebugMenu.cs | Три понятных F8-сценария, состояние/таймер/число гостей и жертв, полный reset, отдельные advanced diagnostics. |
| Assets/Game/Scripts/Networking/OnlineSessionController.cs | Идентификатор совместимости сборки `motel-m15-1`. |
| Assets/Game/Editor/PlaytestBuild.cs | Валидация M15 при существующей стабильной Mac-сборке. |

## Новые файлы

- `Assets/Game/Scripts/Inspection/`: BaggageCase, GuestBaggage, BaggageHandle, BaggageInspection, AnalyzerPanel, HuntScenario.
- `Assets/Game/Editor/BaggageInspectionSetup.cs`: установка, настройка, повторное открытие/валидация сцены, preview и запуск существующего build workflow.
- `Assets/Game/Scripts/Networking/Tests/BaggageInspectionSmokeTest.cs`: development-only интеграционные сценарии. Не запускается без явных тестовых аргументов.
- `Assets/Game/Inspection/Cases/`: 14 ScriptableObject; `Editor/Cases.json`: исходные авторские данные; 4 новых материала для прибора/сумок, 3 коротких процедурных звука. Все новые assets имеют стабильные `.meta`.
- `Documentation/BaggageInspectionM15/`: инструкции, контент, результаты проверок, screenshots и повторяемые Python runners в Tests/.

## Проверка границ изменений

`file-audit.txt` содержит точный список новых/изменённых Assets, Packages, ProjectSettings. `scene-record-audit.txt` сравнивает сериализованные объекты сцены с backup.

Среди старых записей сцены изменены только пять Guest prefab instances, компонент MimicHunt и список корней. Старые Transform, Renderer, Light, материалы, мебель, стены и парковка не менялись; удалённых старых объектов нет. SampleScene, Packages и ProjectSettings совпадают с backup. Навигационный mesh не перестраивался: новый прибор использует собственный carving obstacle.

Общие Editor-валидаторы повторно создают старые отчёты M7–M14. Их свежие результаты сохранены в `legacy-validation-output/`, исходные отчёты восстановлены из backup, чтобы M15 не переписывал историю предыдущих milestones.

Существующий app обновлён только в `Builds/Playtest/MotelGame.app`. LaunchWindowed.command и локальные launchers сохранены. Windows-сборки, git commit/push и публикация release не выполнялись.
