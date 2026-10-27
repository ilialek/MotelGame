# Набор случаев M15 — авторская таблица (спойлеры)

Эта таблица предназначена для настройки контента. В игровом интерфейсе скрытая модель, признак носителя и авторское объяснение не показываются.

| Asset | Истина | Заявление | Основание для решения |
|---|---|---|---|
| 01_ClothesWeekend | Обычный гость | Clothes for the weekend and a wash bag. No equipment. | Low metal, 5.4 kg and fixed balance support all statements. Straightforward innocent. |
| 02_MechanicTools | Обычный гость | Workshop tools and padding. I am repairing a truck tomorrow. | Heavy and metal-rich is legitimate. Tilt shifts downhill; leveling preserves tray displacement. No active recovery. |
| 03_BooksAndCamera | Обычный гость | Several books, a camera and a spare shirt. | 9.2 kg and modest metal response fit a metal camera among heavier books; fixed balanced load. |
| 04_WaterAndLaundry | Обычный гость | Laundry and a sealed water jug for my camping trip. | Liquid response is innocent: downhill offset under tilt, level balance recovers, long decay. Claims acknowledge liquid. |
| 05_ForgottenTripod | Обычный гость | Mainly clothes. I packed in a hurry. | Initial mass estimate is wrong but follow-up explains metal and stable right-heavy balance. An honest omission, not a universal lie detector. |
| 06_PrivateWeights | Обычный гость | Only some clothes and personal things. | Deliberately misleading innocent. Follow-ups predict high mass/metal and passive irreversible sliding. False accusation remains possible. |
| 07_WoundMusicMechanism | Обычный гость | A spring-driven music mechanism for restoration. No batteries. | Counter-motion and delayed response have a legitimate disclosed clockwork cause, supported by metal-rich load. Same response vocabulary as some Mimics. |
| 08_PoweredCameraRig | Обычный гость | A camera stabilizer with a battery fitted, wrapped in pads. | Innocent powered counterbalance. Mass and metal agree; the guest admits power and unlocked mechanism. Counter-motion alone is not conclusive. |
| 09_LightClothesCarrier | Носитель | A few light clothes and a towel. Nothing else. | 13 kg contradicts the confident light-load claim. Counterbalance/delayed pulse contradict both fixed packing and passive cloth. At least two independent clues. |
| 10_FalseToolboxCarrier | Носитель | A full case of steel spanners and sockets. | Mass is plausible but metal response around 9 percent is incompatible with mostly steel. Elastic recovery also contradicts bolted trays. No need for universal counter-motion cue. |
| 11_UnsecuredRadioCarrier | Носитель | Books and an old radio. I left the radio unsecured. | Initial mass/metal are plausible. Left and right tilts show motion against the slope, followed by delayed pulse recovery. An unpowered loose radio cannot explain the combination. |
| 12_LockedGimbalCarrier | Носитель | Camera equipment and padding. It includes a stabilizer. | Plausible technically true cover story. Packing follow-up asserts a locked stage; opposing tilt response and delayed sizable movement contradict that. Compare innocent powered rig, whose explanation differs. |
| 13_StaticSamplesCarrier | Носитель | Nonmetallic sample weights and foam. They are packed in a centered rigid block. | A quiet, braced Mimic has no signature sound or counter-motion. Stable left-heavy center of mass around -4.2 cm contradicts the explicit centered symmetrical packing. Weight and metal alone would miss it. |
| 14_PackedBatteryCarrier | Носитель | A small battery and hand tools, all fitted in solid foam. | Mass/metal plausible. Large tilt displacement, level recovery and delayed long pulse decay contradict both solid clamps and no liquid/moving mechanism. Different combination from low-metal toolbox or quiet off-center case. |

## Правила модели

Масса — сумма частей плюс масса оболочки: suitcase 2.4 kg, duffel 0.8 kg, backpack 0.7 kg, hard case 1.8 kg. Metal — массово-взвешенный относительный индекс, включая фурнитуру оболочки. Balance — массово-взвешенное смещение частей. Отрицательное значение означает левую сторону платформы.

| Response | Поведение |
|---|---|
| Fixed | Сохраняет заданное смещение. |
| Sliding | Перемещается по последнему уклону; после возврата level остаётся сдвинутым. Импульс на ранее горизонтальном грузе даёт фиксированный сдвиг вправо. |
| Liquid | Смещается вниз по текущему уклону и возвращается при level; характерны колебания/затухание. |
| Elastic | Под нагрузкой отклоняется, после импульса частично возвращается. |
| Counterbalanced | Противодействует наклону и импульсу; доступно как обычным заведённым/работающим механизмам, так и существу. Само по себе не определяет носителя. |

Задержка реакции, время затухания и амплитуда задаются физическими частями и объединяются общими формулами. Повторный одинаковый тест после установления состояния повторяет показания. При возврате level сохраняется последний сдвиг незакреплённого груза; это объясняет гистерезис и не является случайной ошибкой.

## Настройка нового случая

Создайте BaggageCase через Assets → Create → Motel → Baggage Case. Укажите оболочку, физические части, пять ответов и authorReasoning. Ответ не должен превышать 480 UTF-8 bytes. Значения массы/времён должны быть конечными и неотрицательными; масса каждой части положительна.

Добавьте asset в Cases компонента BaggageInspection. Сценарии используют отдельные Inspector-массивы ссылок: Inspection Practice Cases, Complete Scenario Cases, Hunt Only Cases. Первый содержит все учебные случаи; полный сценарий — четыре случая, ровно один носитель; Hunt Only — три обычных случая (защищённый носитель задаётся самим отладочным сценарием). Порядок библиотеки не является номером поведения. Точки багажа сопоставляются с Reception.Rooms; новой логики специально для Room 101 нет.

Обычная смена перемешивает невредные случаи и вставляет одного случайно выбранного носителя в одну из первых четырёх позиций. После исчерпания набора повторяются только обычные случаи. Debug B намеренно имеет фиксированную последовательность для повторяемых проверок.

Проверяйте новый случай по всем каналам: не должно существовать обычного случая с теми же ответами и абсолютно теми же наблюдениями, который делает носителя неотличимым. Не используйте отдельный звук, цвет или оболочку как универсальную метку угрозы. authorReasoning — авторская гипотеза о читаемости улик; её понятность требует теста с людьми.
