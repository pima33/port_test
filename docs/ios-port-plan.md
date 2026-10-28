# План переноса MagerCam на iOS: аудит совместимости

Дата анализа и проверки внешних источников: **28 октября 2026 года**. Репозиторий: https://github.com/Magerko/MagerCam. Ветка: **main**. Исследованный commit: **f6ca87d896ebcd8bea3eebfcb203d110a15c2d64**, дата commit: 2026-10-26 20:13:57 UTC.

**Статус документа: только исследование и предложение к согласованию.** iOS-приложение, каталог ios/, тестовые стенды и workflows не создавались. Сборки, тесты, установка зависимостей, изменение клиентов и публикация не выполнялись.

## 1. Executive Summary

**Вывод:** создание нативного Swift/SwiftUI-источника для существующих Windows-клиента и OBS-плагина представляется реализуемым **по IPv4 Wi-Fi, с приложением на переднем плане**, без изменения их исходников. Это инженерный вывод из анализа контракта, а не результат испытания iPhone. Базовый путь: AVFoundation → VideoToolbox → преобразование NAL в Annex B → существующие MC-пакеты поверх HTTP/TCP; микрофон → PCM s16le, 48 kHz, mono → тот же поток с общей временной шкалой.

Совпадения названия H.264 недостаточно: нужны конфигурация SPS/PPS отдельно от кадров, правильные флаги, отсутствие B-frames, микросекундные PTS и восстановление с ключевого кадра. HEVC, 4K60, обработка фона и длительная работа — отдельные проверяемые этапы. Фактические запросы потребителей: [windows/client/main.c:540-702](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L540-L702), [obs-plugin/src/magercam.c:1038-1214](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L1038-L1214); упаковка Android: [android/app/src/main/java/com/magercam/app/StreamServer.kt:157-186](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L157-L186), [android/app/src/main/java/com/magercam/app/StreamServer.kt:430-439](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L430-L439).

Полная идентичность Android недостижима в нескольких важных местах:

- Android foreground service, wake lock и работа после ухода из Activity не имеют общего аналога для непрерывной камеры на заблокированном iPhone. Основной сценарий iOS — видимое приложение с отключением автоматической блокировки; затемнение внутри приложения не равно блокировке.
- USB-режим клиентов основан на ADB. iPhone не является Android ADB-устройством. Кабельный IP-канал, если его удастся подтвердить, был бы отдельным сценарием обычного сетевого подключения.
- OBS использует PTS аудио и видео; Windows отдаёт PCM в WASAPI без аудио-PTS, а виртуальная камера получает собственное время. Гарантию точной A/V-синхронизации Windows нельзя обеспечить только кодом iOS.
- Температура батареи в °C, Android AudioSource, antibanding, дистанция фокуса и стабилизация требуют семантических адаптаций. Нельзя выдавать недоступные измерения за реальные.
- UDP broadcast discovery требует отдельной проверки entitlement и Local Network privacy. Ввод IP и QR остаются резервными способами подключения.

**Рекомендация:** согласовать ограниченный MVP H.264 SDR 8-bit 1080p30, сначала видео, затем PCM, на физическом iPhone 17 Pro Max и неизменённых клиентах исследованной ревизии. Основная доставка — GitHub Actions/macOS → подписанный archive → TestFlight; резерв — Ad Hoc с зарегистрированным UDID и проверенной OTA-установкой. Локальный Mac для этой схемы не обязателен, но macOS/Xcode в облаке обязательны.

В следующих разделах используются четыре статуса доказательств:

| Статус | Значение |
|---|---|
| **Подтверждено кодом** | Найдена реализация в указанном commit; это не означает проверку на устройстве |
| **Подтверждено документацией** | Утверждение документации проекта или официального API; источник указан |
| **Требует проверки** | Результат зависит от устройства, окружения, runtime или ещё не проведённого эксперимента |
| **Предложение** | Будущая архитектура, политика, тест или критерий; сейчас не реализован |

## 2. Область анализа, commit SHA и методология

### 2.1. Доступ и воспроизводимость

В предоставленной рабочей папке первоначально находился только Задача.txt. Локального checkout MagerCam и каталога .git не было; при первоначальной проверке Git не находился в PATH и проверенных стандартных местах установки. На финальной проверке Git уже доступен, однако рабочая папка по-прежнему не является Git-репозиторием. Поэтому исходники прочитаны через GitHub API/raw **в памяти**, без клонирования и сохранения дополнительных файлов. Получены полное дерево commit (truncated=false) и 65 текстовых blob; их Git blob SHA-1 сверены с деревом. Для windows/install.ps1 отдельно учтён UTF-8 BOM при проверке исходных байтов.

Все ссылки на исходники ниже закреплены на исследованном SHA. Это исключает смешение анализа с дальнейшими изменениями main. Дата commit не заменяет дату фактической проверки Apple/GitHub.

Прочитаны манифест, Gradle, все 12 Kotlin-файлов, HTML/JS панели, docs/PROTOCOL.md, Windows C/C++ и заголовки, OBS-плагин, инструменты эмуляции/проверок и сборочные/установочные скрипты. В дереве нет AGENTS.md, готового ios-port-plan.md, .github/workflows и Android unit/instrumentation test suite. Отсутствие — результат проверки дерева этой ревизии, а не утверждение обо всех ветках.

Бинарные изображения, .ico, Gradle wrapper JAR и веса TFLite не декодировались и не исполнялись; интерфейс и применение модели установлены по коду. Для визуального сравнения экранов и проверки качества сегментации в будущем нужны скриншоты/запуск приложения, для модели — исследование её входов/выходов на устройстве. Исходный текст задания прочитан; недоступных текстовых исходников не выявлено.

### 2.2. Инвентаризация и цепочки

| Подсистема | Состав и назначение | Основные доказательства |
|---|---|---|
| Android UI | Одна Activity, программный View UI, диалоги, QR, системный выбор картинки | [android/app/src/main/java/com/magercam/app/MainActivity.kt:101-197](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/MainActivity.kt#L101-L197), [android/app/src/main/java/com/magercam/app/MainActivity.kt:271-406](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/MainActivity.kt#L271-L406), [android/app/src/main/java/com/magercam/app/MainActivity.kt:522-830](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/MainActivity.kt#L522-L830) |
| Android runtime | CameraService владеет Core, foreground notification, wake/Wi-Fi locks | [android/app/src/main/java/com/magercam/app/CameraService.kt:29-80](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraService.kt#L29-L80), [android/app/src/main/java/com/magercam/app/Core.kt:23-123](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L23-L123) |
| Камера/кодирование | Camera2, MediaCodec Surface, необязательный GL, JPEG, PCM, MP4 | [android/app/src/main/java/com/magercam/app/CameraEngine.kt:378-524](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L378-L524), [android/app/src/main/java/com/magercam/app/VideoEncoder.kt:27-138](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/VideoEncoder.kt#L27-L138), [android/app/src/main/java/com/magercam/app/AudioCapture.kt:31-85](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/AudioCapture.kt#L31-L85), [android/app/src/main/java/com/magercam/app/Recorder.kt:50-129](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Recorder.kt#L50-L129) |
| Сеть | Встроенный TCP/HTTP сервер, MC, UDP, исходящий QR TCP | [android/app/src/main/java/com/magercam/app/StreamServer.kt:34-186](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L34-L186), [android/app/src/main/java/com/magercam/app/Discovery.kt:29-70](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Discovery.kt#L29-L70), [android/app/src/main/java/com/magercam/app/Core.kt:179-196](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L179-L196) |
| Windows | Win32 UI, Winsock, Media Foundation decode, WASAPI, shared-memory virtual camera | [windows/client/main.c:540-775](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L540-L775), [windows/client/decoder.c:117-165](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/decoder.c#L117-L165), [windows/client/audio.c:59-120](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/audio.c#L59-L120), [windows/client/vcam.c:107-138](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/vcam.c#L107-L138) |
| OBS | Windows plugin, FFmpeg, D3D11 с fallback, async video/audio | [obs-plugin/src/magercam.c:679-896](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L679-L896), [obs-plugin/src/magercam.c:1839-1867](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L1839-L1867) |
| Веб-панель | Один HTML, MJPEG/JPEG, API, локальный детектор движения | [android/app/src/main/assets/index.html:45-129](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/assets/index.html#L45-L129), [android/app/src/main/assets/index.html:130-361](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/assets/index.html#L130-L361) |
| Проверки | fake-phone.js, obs-test.js, Windows vcamtest | [tools/fake-phone.js:1-196](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/tools/fake-phone.js#L1-L196), [tools/obs-test.js:1-111](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/tools/obs-test.js#L1-L111), [windows/tools/vcamtest.c:1-65](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/tools/vcamtest.c#L1-L65) |

Метод: проследить управление от UI/HTTP до Core, затем до CameraEngine/encoder/audio и сети; независимо проследить обратный путь у каждого клиента; сопоставить документацию с фактическими байтами и условиями. Наличие кода не трактуется как доказательство производительности, качества изображения, корректности драйвера или установки на iPhone.

### 2.3. Матрица главных доказательств и расхождений

| Вывод | Статус и основание | Следствие |
|---|---|---|
| В сети H.264/HEVC + PCM, не AAC | **Подтверждено кодом**: [android/app/src/main/java/com/magercam/app/VideoEncoder.kt:27-138](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/VideoEncoder.kt#L27-L138), [android/app/src/main/java/com/magercam/app/AudioCapture.kt:31-85](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/AudioCapture.kt#L31-L85) | AAC относится только к локальному MP4 |
| Annex B — контракт | **Подтверждено документацией** [docs/PROTOCOL.md:105-129](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/docs/PROTOCOL.md#L105-L129); код MediaCodec копирует bytes без конверсии [android/app/src/main/java/com/magercam/app/VideoEncoder.kt:105-138](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/VideoEncoder.kt#L105-L138) | Реальный bitstream Android требует capture; VideoToolbox нужно конвертировать явно |
| FPS API не строго 15..60 | **Подтверждено кодом** 5..60 [android/app/src/main/java/com/magercam/app/Core.kt:231-260](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L231-L260), против документа [docs/PROTOCOL.md:46-57](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/docs/PROTOCOL.md#L46-L57) | Не использовать таблицу документа как единственный validator |
| lock затрагивает фокус | **Подтверждено кодом** [android/app/src/main/java/com/magercam/app/CameraEngine.kt:215-229](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L215-L229), документация говорит AE/AWB [docs/PROTOCOL.md:60-61](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/docs/PROTOCOL.md#L60-L61) | iOS должен учитывать и фокус, либо явно согласовать отклонение |
| mirror не меняет сетевой H.26x | **Подтверждено кодом** preview/UI; JPEG создаётся отдельно [android/app/src/main/java/com/magercam/app/CameraEngine.kt:892-914](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L892-L914), [android/app/src/main/java/com/magercam/app/MainActivity.kt:198-240](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/MainActivity.kt#L198-L240) | Документированное mirror для MJPEG не подтверждается JPEG-путём |
| HEVC общий для всех MC-клиентов | **Подтверждено кодом** [android/app/src/main/java/com/magercam/app/Core.kt:104-123](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L104-L123) | H.264-клиент переводит общий encoder в H.264 |
| Точный Windows A/V sync отсутствует | **Подтверждено кодом** [windows/client/main.c:682-702](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L682-L702), [windows/client/audio.c:105-120](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/audio.c#L105-L120), [windows/vcam/vcam.cpp:1040-1054](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/vcam/vcam.cpp#L1040-L1054) | Требование нельзя объявлять выполненным по одному правильному PTS источника |
| Совместимость iOS без правок клиентов | **Требует проверки** на реальном bitstream | Первое условие перехода к полной реализации |
| Нет CI в репозитории | **Подтверждено деревом commit** | Раздел 15 — новый проект CI, не описание существующего |

## 3. Анализ Android-приложения

Android использует Kotlin, Camera2 и MediaCodec без Compose и AndroidX. Минимальный Android API 26, compile/target SDK 34, Java 17; приложение версии 1.1.4/code 5. Зависимости включают ZXing 3.5.3 и TensorFlow Lite 2.16.1. AGP 8.7.3, Kotlin plugin 2.0.21, Gradle wrapper 8.9. Это версии Android-сборки, не требования будущего iOS. [android/app/build.gradle.kts:8-60](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/build.gradle.kts#L8-L60), [android/build.gradle.kts:1-5](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/build.gradle.kts#L1-L5), [android/gradle/wrapper/gradle-wrapper.properties:1-8](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/gradle/wrapper/gradle-wrapper.properties#L1-L8).

Манифест объявляет одну MainActivity и CameraService с camera|microphone foreground type. Разрешения охватывают камеру, микрофон, сеть, multicast/Wi-Fi, wake lock, foreground service и уведомления. Intent filter magercam://pair ведёт в ту же Activity. [android/app/src/main/AndroidManifest.xml:4-46](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/AndroidManifest.xml#L4-L46).

Фактическая цепочка: MainActivity получает runtime permissions → запускает/bind CameraService → Core создаёт engine, audio, server, discovery → UI и HTTP изменяют Settings/Core → Core вызывает CameraEngine и уведомляет клиентов статусом. Жизнь Core не равна жизни Activity: Back может оставить службу работающей; START_NOT_STICKY не обещает автоматическое восстановление после убийства процесса. [android/app/src/main/java/com/magercam/app/MainActivity.kt:101-197](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/MainActivity.kt#L101-L197), [android/app/src/main/java/com/magercam/app/CameraService.kt:29-80](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraService.kt#L29-L80), [android/app/src/main/java/com/magercam/app/Core.kt:23-123](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L23-L123).

| Компонент | Реальная ответственность | Особенность переноса |
|---|---|---|
| MainActivity | Preview, UI, permissions, QR scan, ориентация, диалоги, dim | SwiftUI state + мост preview; capture не на MainActor |
| CameraService/Core | Владение ресурсами, команды, статус, ABR, thermal, pairing | Coordinator с явными состояниями, не попытка скопировать Android Service |
| Settings | SharedPreferences; большинство полей volatile | Typed settings + сериализованные обновления + UserDefaults/Keychain |
| CameraEngine | Выбор камер/форматов, capture requests, surfaces, demand, retries | Один serial executor AVFoundation; таблица capabilities |
| VideoEncoder | Encoder lifecycle, CSD, output, live bitrate, keyframe | VideoToolbox session + отдельный Annex B adapter |
| AudioCapture | AudioRecord, PCM chunks, clock mapping | AVFoundation audio output + conversion/resampling |
| StreamServer/Discovery | HTTP/MC/MJPEG/UDP, очереди, авторизация | Native network services и bounded queues |
| GlRenderer/BgSegmenter | OES/GL эффекты, TFLite маска, outputs | Metal/Core Image/Vision либо отдельно портируемая модель |
| Recorder | Второй H.264 + AAC encoder, MediaMuxer/MediaStore | AVAssetWriter, отдельная политика ресурсов и сохранения |

**Значения первого запуска:** 1920×1080, 30 fps, 8000 kbit/s, задняя камера через разрешение пустого cameraId, mic=true, audio_source=camcorder, TCP 4848, keep_screen_on=true, orientation_mode=auto, zoom=1, EV=0, continuous focus, WB/antibanding=auto, stabilization=false, torch/AE/AWB lock=false, mirror=false, auth=true, случайный шестизначный PIN, codec=auto, abr=true, heat_guard=true, framing=false, bg=off, bg_strength=0.6, auto_dim=false. Сохранённые preferences могут менять эти значения. [android/app/src/main/java/com/magercam/app/Settings.kt:12-45](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Settings.kt#L12-L45), [android/app/src/main/java/com/magercam/app/Settings.kt:73-75](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Settings.kt#L73-L75).

Профили — наборы настроек с последующим применением остальных параметров запроса: call — 720p30/3 Mbps/front/framing/voice; stream — 1080p60/12 Mbps/back/stab/camcorder; doc — 1080p30/8 Mbps/back/no stab/no background. Они не включают автоматически микрофон. Настройки поверх профиля могут сбрасывать имя profile. В локальном диалоге смена профиля приводит к раннему return, поэтому остальные изменения того же диалога не обязательно применяются. [android/app/src/main/java/com/magercam/app/Core.kt:218-315](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L218-L315), [android/app/src/main/java/com/magercam/app/MainActivity.kt:793-825](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/MainActivity.kt#L793-L825).

## 4. Экраны и пользовательские сценарии

### 4.1. Точное число экранов

Критерий: самостоятельная навигационная сущность приложения (Activity/Fragment/Compose route), а не любой overlay или диалог. **Нативный Android-экран один — MainActivity.** Fragment и Compose navigation в исследованном приложении нет. Настройки, помощь, выбор фокуса, ручной фокус, QR и затемнение — дочерние поверхности/состояния. Дополнительно есть **одна веб-страница** на телефоне для внешнего браузера; она не второй экран Android navigation. Системный image picker принадлежит ОС. Основание: манифест и построение UI [android/app/src/main/AndroidManifest.xml:26-41](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/AndroidManifest.xml#L26-L41), [android/app/src/main/java/com/magercam/app/MainActivity.kt:271-406](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/MainActivity.kt#L271-L406), [android/app/src/main/java/com/magercam/app/MainActivity.kt:522-830](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/MainActivity.kt#L522-L830).

| Поверхность | Вход и элементы | Состояния/сценарий | Android зависимость | Предложение SwiftUI / сложность |
|---|---|---|---|---|
| Основной экран | Launcher/deep link; preview, IP/PIN/status, camera, torch, mic, focus, framing, lock, rec, QR, dim, settings/help/exit, zoom/EV | Нет permissions; preview; streaming; recording; ошибка камеры/сети | SurfaceView, service binding, Camera2, OrientationEventListener | CameraScreen + status + toolbar / средняя |
| Настройки, диалог | Кнопка settings; формат, fps, bitrate, звук, WB, codec, orientation, toggles, auth/PIN, имя/порт | Просмотр/сохранение; restart некоторых ресурсов; профиль | AlertDialog, SharedPreferences | SettingsSheet, явное requested/applied / средняя |
| Focus/manual focus | Кнопка focus, выбор режима, slider | AF/continuous/infinity/macro/manual | Camera2 focus distance | Sheet + capability gates / высокая для семантики |
| QR overlay | Кнопка QR либо deep link | Scan задней камерой → hosts по очереди → success/error → восстановление камеры | ZXing, YUV frames, intent | AVCaptureMetadataOutput/QR sheet, URL handler / средняя |
| Затемнение | Кнопка/autoDim 60 s при клиентах | Чёрный overlay + низкая brightness, восстановление касанием | Window brightness, keep screen | Dim overlay + restore brightness, idle timer / низкая |
| Help и pairing result | Кнопка помощи/результат TCP | Инструкции или ошибка | AlertDialog | Sheet/alert / низкая |
| Background picker | Локальная кнопка изображения | Выбор изображения, decode/save | Системный picker, Bitmap | PhotosPicker/fileImporter, bounded decode / средняя |
| Веб-страница | HTTP / или /index.html | PIN, preview on/off, все API controls, browser motion detector | HTML/JS/Canvas, localStorage | Портировать asset и сохранить routes / средняя |

Источники элементов/поведения: [android/app/src/main/java/com/magercam/app/MainActivity.kt:309-406](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/MainActivity.kt#L309-L406), [android/app/src/main/java/com/magercam/app/MainActivity.kt:435-503](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/MainActivity.kt#L435-L503), [android/app/src/main/java/com/magercam/app/MainActivity.kt:522-667](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/MainActivity.kt#L522-L667), [android/app/src/main/java/com/magercam/app/MainActivity.kt:669-830](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/MainActivity.kt#L669-L830), [android/app/src/main/assets/index.html:45-129](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/assets/index.html#L45-L129).

~~~mermaid
flowchart TD
    Start["Launcher или magercam://pair"] --> Main["MainActivity — единственный экран"]
    Main --> Permission["Системные разрешения"]
    Permission --> Main
    Main --> Settings["Диалог настроек"]
    Main --> Focus["Диалоги фокуса"]
    Main --> Help["Помощь / результат сопряжения"]
    Main --> QR["QR overlay"]
    QR --> Pair["TCP к Windows"]
    Pair --> Main
    Main --> Picker["Системный выбор изображения"]
    Picker --> Main
    Main --> Dim["Затемнение внутри Activity"]
    Dim --> Main
    Browser["Внешний браузер"] --> Web["Одна страница HTTP /"]
    Web --> API["Core через API"]
    Main --> Core["Core / CameraService"]
~~~

Основные сценарии: первый запуск и permissions; подключение Windows вручную/по discovery/QR; подключение OBS по Wi-Fi; управление с ПК во время потока; несколько клиентов; просмотр в браузере; запуск/остановка локальной записи; потеря сети; смена камеры; восстановление после ошибки. Для iOS добавляются явные сценарии отказа Local Network, возврата из background, звонка и повторной активации audio session. Они описаны в разделе 13, а не скрываются за Android-моделью службы.

Детектор движения — **в браузере**, пока открыта вкладка: JPEG snapshots примерно раз в 700 ms, уменьшение до 64×36, сравнение яркости, cooldown 5 s, до 24 событий, звук/notifications. Это не фоновая аналитика Android. [android/app/src/main/assets/index.html:291-354](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/assets/index.html#L291-L354).

## 5. Камера, кодирование, аудио и синхронизация

### 5.1. Реальный Camera2 pipeline и capabilities

CameraEngine открывает камеру по потребности: MC-клиент, MJPEG, preview, recording или QR. Без потребителей capture освобождается. Поддерживаются front/back, логические и физические камеры с идентификаторами вида logical:physical; external camera исключены. Нельзя переносить Android ID буквально на iPhone: нужен короткий стабильный opaque ID (например back-wide), который клиенты берут из cameras. [android/app/src/main/java/com/magercam/app/CameraEngine.kt:276-399](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L276-L399).

Основной поток без эффектов использует Surface MediaCodec. GL-путь включается при background effect **или записи**, а не только при blur. MJPEG берётся отдельно через ImageReader YUV_420_888; latest image преобразуется в NV21/JPEG quality 75, с интервалом не менее 45 ms. Его картинка не обязана совпадать с GL-обработанным H.26x. [android/app/src/main/java/com/magercam/app/CameraEngine.kt:378-457](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L378-L457), [android/app/src/main/java/com/magercam/app/CameraEngine.kt:892-914](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L892-L914), [android/app/src/main/java/com/magercam/app/GlRenderer.kt:250-352](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/GlRenderer.kt#L250-L352).

| Параметр | Android фактически | iOS и ограничение |
|---|---|---|
| Размер | Пересечение размеров камеры с AVC capabilities; <=3840×2160, ширина >=320; выбор подходящего размера | Перебор device.formats + проверка encoder; не объявлять произвольное w/h поддержанным |
| FPS | Список верхних границ camera ranges 15..60; предпочитается fixed range; для >1080p дополнительно encoder cap | activeFormat и min/max frame duration в поддерживаемом диапазоне; список fps не доказывает каждый size×fps |
| Preview | Отдельная surface, ограничение до ~1080p, sensor orientation/mirror | AVCaptureVideoPreviewLayer через UIViewRepresentable; отдельно учитывать ориентацию preview |
| Focus | Continuous/auto/macro/infinity/manual; manual normalized diopters; tap AF/AE region | focusMode, focusPointOfInterest, lensPosition; режимы проверять isFocusModeSupported |
| Экспозиция | AUTO AE, EV bias, AE lock; **ручных ISO и shutter нет** | setExposureTargetBias, exposureMode lock; не добавлять ISO/shutter в обязательный parity |
| WB | Camera2 preset enums и AWB lock | Auto/locked gains; presets через согласованные temperature/tint approximation |
| Zoom | API30 ratio либо crop; physical lens ограничен; framing может переопределить crop | videoZoomFactor, min/max и switch-over virtual camera; диапазоны измерять для активного устройства |
| Stabilization | EIS по флагу; OIS включается отдельно всегда, если доступен | preferredVideoStabilizationMode с проверкой поддержки; false не идентичен Android «всё выключено» |
| Torch | Поддерживаемый torch, сброс при смене камеры | hasTorch/isTorchAvailable + lockForConfiguration |
| Orientation | Не поворот H.26x пикселей, а orient по часовой стрелке; auto/landscape/portrait | Сохранить sensor-native pixels + metadata либо документированно нормализовать, но не делать оба поворота |
| Ошибки | Retry ~3 s; при session configure failure убираются preview, MJPEG, recording по очереди | RuntimeError/interruption events + reconfigure; явная деградация, не бесконечный restart |

Подробные основания: [android/app/src/main/java/com/magercam/app/CameraEngine.kt:598-678](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L598-L678), [android/app/src/main/java/com/magercam/app/CameraEngine.kt:727-890](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L727-L890), [android/app/src/main/java/com/magercam/app/Core.kt:318-329](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L318-L329), [android/app/src/main/java/com/magercam/app/CameraEngine.kt:505-594](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L505-L594). Официальные API: [A01], [A02], [A03]. Значение lensPosition iOS 0 — ближний предел, 1 — дальний, причём 1 не гарантирует бесконечность. Android focus_dist имеет обратное направление; простое 1−x может быть UI-аппроксимацией, но не физически точным переносом. [A04], [A05].

### 5.2. Encoder, конфигурация и NAL

VideoEncoder пробует HEVC full/minimal, затем AVC full/minimal. HEVC заранее ищется среди hardware encoders; AVC создаётся createEncoderByType, что само по себе не доказывает hardware. В full format: Surface input, bitrate, fps, I-frame interval 2 s, priority=0, B-frames=0 где доступно, BT.709 limited SDR; CBR если encoder поддерживает; HEVC Main, AVC High с выбором level по размеру/частоте. Minimal fallback снимает часть требований: отсутствие B-frames в любом Android fallback не доказано. [android/app/src/main/java/com/magercam/app/VideoEncoder.kt:27-94](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/VideoEncoder.kt#L27-L94), [android/app/src/main/java/com/magercam/app/VideoEncoder.kt:177-203](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/VideoEncoder.kt#L177-L203).

CSD/BUFFER_FLAG_CODEC_CONFIG передаётся отдельным VIDEO_CONFIG; для AVC склеиваются csd-0/csd-1, HEVC обычно находится в csd-0. Конфигурация дедуплицируется внутри экземпляра encoder. Кадр — копия output buffer с presentationTimeUs и keyframe flag. Явной проверки/конвертации Annex B нет. Поэтому утверждение «Android на каждом устройстве уже выдаёт корректный Annex B» требует дампа, хотя именно Annex B заявлен протоколом и ожидается потребителями. [android/app/src/main/java/com/magercam/app/VideoEncoder.kt:97-150](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/VideoEncoder.kt#L97-L150), [docs/PROTOCOL.md:120-129](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/docs/PROTOCOL.md#L120-L129).

**Предложение VideoToolbox adapter:**

1. Создать VTCompressionSession для H.264, 8-bit SDR 4:2:0, RealTime=true, AllowFrameReordering=false. Предпочтительный input — NV12 pixel buffers; согласовать video/full range и BT.709 между capture attachments, encoder properties и SPS/VUI, чтобы исключить washed-out/crushed blacks. Проверять OSStatus каждого свойства и фактический профиль. Начальный профиль выбрать по эксперименту с текущим Windows MFT и FFmpeg; не считать High или Baseline обязательным wire-полем.
2. Установить GOP по времени 2 s, ожидаемую fps, bitrate; keyframe по подключению/смене конфигурации/восстановлению очереди. ExpectedFrameRate — hint, реальную cadence задаёт capture.
3. Из CMFormatDescription получить SPS/PPS, для HEVC VPS/SPS/PPS через CMVideoFormatDescriptionGetHEVCParameterSetAtIndex. В VIDEO_CONFIG положить каждый NAL с Annex B start code 00 00 00 01, не avcC/hvcC record.
4. Из compressed CMSampleBuffer получить length-prefixed NAL, длину префикса брать из format description, не безусловно считать равной 4. Проверить все границы; заменить каждый length-prefix start code, сохранив все NAL одного access unit в одном VIDEO_FRAME.
5. Sync/key определяется metadata sample и проверкой NAL в тестах; PTS переводится в общую шкалу microseconds. Без reorder PTS≈DTS; DTS отдельно протокол не несёт.
6. При смене кодека/размера увеличить внутреннее generation, запретить запоздалые callbacks старого encoder, послать новую config до первого IDR. Не смешивать старые и новые bytes.
7. Проверять поддержанные properties и реальный output; не обещать точное равенство битрейта и GOP только по успешному setProperty.

Источники Apple: [A06], [A07], [A08], [A09]. **Особая ловушка:** EnableLowLatencyRateControl документирует infinite GOP/all-P после первого IDR. Включать его без доказанной периодической принудительной выдачи IDR нельзя: восстановление MagerCam ждёт keyframe. Начальный вариант — обычный realtime encoder с отключённым reorder и контролируемым GOP. [A10], [A11].

HEVC разрешается только если **все** MC-клиенты запросили codec=hevc и пользователь/encoder допускают HEVC. MJPEG-клиенты в этом согласовании не участвуют. Открытие H.264-only клиента требует общей смены encoder и новой конфигурации для всех. [android/app/src/main/java/com/magercam/app/Core.kt:104-123](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L104-L123).

### 5.3. Backpressure и адаптация

Android контролирует возраст очереди и байты, а не только число кадров. При >500 ms или >6 MB накопленного video очищается очередь video, затем ожидается keyframe; повторный запрос keyframe ограничен примерно 2 s. Audio допускает более длинную очередь до ~1 s/12 MB. Config/status отдельно; полного строгого общего memory cap нет. TCP send buffer 256 KiB и BufferedOutputStream 64 KiB добавляют задержку вне прикладной очереди; write timeout не задан. [android/app/src/main/java/com/magercam/app/StreamServer.kt:157-165](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L157-L165), [android/app/src/main/java/com/magercam/app/StreamServer.kt:279-367](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L279-L367).

ABR раз в секунду снижает bitrate ×0.7 при drops/age>200 ms, с нижним ориентиром 800 kbit/s и ограничением target; при age<60 ms после спокойных тиков растит ×1.15+100. Это эвристика очередей, не измеритель доступной полосы TCP. Heat guard использует battery temp >=43.5 °C для ограничения 30 fps/6000 kbit/s и <=40.5 °C для снятия. [android/app/src/main/java/com/magercam/app/Core.kt:128-172](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L128-L172).

**Предложение:** сохранить wire-visible поведение, но реализовать bounded per-client queues и отслеживать незавершённые NWConnection sends; отделить медленного клиента от capture. Нельзя исправить устаревшие bytes, уже отправленные в TCP: при предельном backlog завершать соединение с диагностикой и давать клиенту переподключиться. Это политика нового iOS-сервера; изменения общих порогов/семантики вынесены в раздел 21.

### 5.4. Audio и общая шкала времени

Android: AudioRecord, PCM signed 16-bit little-endian, **48 000 Hz, mono, 960 frames / 1920 bytes / 20 ms**. Источник camcorder/mic/voice/unprocessed, при ошибке fallback MIC. Timestamp берётся из AudioTimestamp с framePosition; clock выбирается с учётом Camera2 timestampSource (REALTIME → boottime, иначе monotonic), fallback — now−20 ms. [android/app/src/main/java/com/magercam/app/AudioCapture.kt:31-85](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/AudioCapture.kt#L31-L85), [android/app/src/main/java/com/magercam/app/Core.kt:104-123](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L104-L123).

В network audio **нет AAC, ADTS, Opus и контейнера MP4**. AUDIO_CONFIG: BE32 sample rate + один byte channels. AAC-LC 128 kbit/s используется отдельным Recorder в локальном MP4. Запись использует собственный H.264 encoder и может иметь другой bitrate/GOP; MediaMuxer отбрасывает немонотонные timestamps. [android/app/src/main/java/com/magercam/app/Recorder.kt:50-86](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Recorder.kt#L50-L86), [android/app/src/main/java/com/magercam/app/Recorder.kt:118-177](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Recorder.kt#L118-L177).

**Предложение iOS:** сначала AVCaptureVideoDataOutput и AVCaptureAudioDataOutput в одной AVCaptureSession с её synchronizationClock; audio samples преобразовывать AVAudioConverter в interleaved s16le/mono/48 kHz. Предпочтительная частота AVAudioSession не гарантия actual format: проверять route/format после activation и resample. При новом route пересоздать converter и fractional sample accumulator. Не считать каждый callback равным 20 ms: собирать ровно 960 frames, PTS = время первого output sample с учётом задержки преобразования. synchronizationClock доступен с iOS 15.4, AVAudioConverter — с iOS 9. [A12], [A13], [A14].

Выбрать одну clock domain; host clock conversion делать через CoreMedia clock conversion, не Date/Unix wall clock и не независимые «now» в разных очередях. Не обнулять только audio или только video на route change. При разрыве временной непрерывности безопаснее закрыть stream, начать новую session timeline и отправить configs, чем оставить OBS с несовместимыми эпохами. Изменение системных часов не должно менять PTS.

OBS map_timestamp применяет общий offset для audio/video с начальным запасом 80 ms и коррекцией больших расхождений. Он не исправит постоянную ошибку sample rate на источнике. Windows audio_write PTS не принимает; latency audio определяется WASAPI/buffer, video — независимым decode/vcam. [obs-plugin/src/magercam.c:748-764](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L748-L764), [obs-plugin/src/magercam.c:1165-1200](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L1165-L1200), [windows/client/main.c:682-702](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L682-L702), [windows/client/audio.c:59-120](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/audio.c#L59-L120). Критерии drift необходимо разделить по этим потребителям.

### 5.5. Эффекты и локальная запись

GL сохраняет camera texture timestamp и передаёт его в encoder surface; сегментация асинхронная, не более одной задачи одновременно. TFLite читает размер и тип input/output модели: float input получает RGB 0..1, альтернативная ветка — RGB bytes; результат преобразуется в маску. [android/app/src/main/java/com/magercam/app/GlRenderer.kt:250-352](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/GlRenderer.kt#L250-L352), [android/app/src/main/java/com/magercam/app/BgSegmenter.kt:28-69](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/BgSegmenter.kt#L28-L69). Порт TFLite-модели не обязателен для wire compatibility: Vision person segmentation (iOS 15+) — кандидат, но маски, качество, доступная частота и лицензия модели требуют сравнения, а не заявления о полной эквивалентности. [A15].

AVAssetWriter для MP4, Photos add-only/export и обработка недостатка места — отдельный этап. Preview/MJPEG/network/recording должны явно описывать, какие эффекты входят в каждый output. Нельзя незаметно «улучшить» MJPEG до обработанного изображения и назвать это точным воспроизведением текущего Android.

## 6. Полный разбор сетевого протокола

### 6.1. Транспорт и HTTP

Телефон — сервер. TCP по умолчанию 4848, локально настраивается; UDP discovery всегда 4849. QR TCP listener находится **на ПК**, 4850..4859. Основной поток не RTSP/RTP/WebSocket: после HTTP headers идут собственные MC packets до закрытия TCP. TCP сохраняет порядок байтов, но не границы packets; клиенты используют recv_all. [android/app/src/main/java/com/magercam/app/StreamServer.kt:34-46](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L34-L46), [android/app/src/main/java/com/magercam/app/StreamServer.kt:80-186](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L80-L186), [windows/client/net.c:72-111](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/net.c#L72-L111).

Android request parser читает request line и headers строками (лимит одной строки 8192); socket read timeout 15 s до streaming. URI query декодируется URLDecoder UTF-8, повторные ключи сводятся к последнему. Из headers значимы X-MagerCam-Key и Content-Length без учёта регистра. **HTTP method не проверяется**: маршрутизация только по path. Это фактическая permissiveness, не рекомендация строить новый API без валидации. Нет обработки chunked request body, multipart upload, TLS или HTTP/2. [android/app/src/main/java/com/magercam/app/StreamServer.kt:80-137](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L80-L137), [android/app/src/main/java/com/magercam/app/StreamServer.kt:246-264](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L246-L264).

| Предназначенный запрос и aliases | Авторизация | Ответ и важные детали |
|---|---|---|
| GET /, /index.html | Нет | 200 HTTP/1.0, text/html; charset=utf-8, Content-Length, Cache-Control: no-cache; asset |
| GET /favicon.ico | Нет | 404, plain text |
| GET /api/status | Да | 200 JSON status |
| GET /api/set?… | Да | Применяет параметры, возвращает status; не transactional applied-ack |
| POST /api/background | Да | Raw bytes JPEG/PNG, Content-Length 1..12,000,000; 413 Bad size / 415 Not an image; Content-Type не валидируется |
| GET /video, /mjpeg, /mjpegfeed | Да | MJPEG multipart boundary=magercamframe, без audio |
| GET /shot.jpg, /jpeg | Да | JPEG snapshot; ожидание до ~4 s, затем 503 Camera not ready |
| GET /stream?… | Да | HTTP/1.1 application/x-magercam, затем бинарный поток без chunked framing |
| Иной path | Да, проверяется до 404 | 404 Not found после успешной авторизации; без PIN возможен 401 |

Основание всех routes: [android/app/src/main/java/com/magercam/app/StreamServer.kt:100-137](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L100-L137), [android/app/src/main/java/com/magercam/app/StreamServer.kt:188-243](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L188-L243).

JSON responses используют HTTP/1.0, application/json; charset=utf-8, Content-Length, Cache-Control: no-cache и Access-Control-Allow-Origin: *. MJPEG: HTTP/1.0, Connection: close, Cache-Control: no-cache, no-store, Pragma: no-cache и CORS *. Snapshot: JPEG с длиной и CORS *. Error body plain text: 401 Access code required, 429 Too many attempts, 404 Not found, 413 Bad size, 415 Not an image, 503 Camera not ready. 409 обрабатывается клиентами, но этот сервер его не генерирует. [android/app/src/main/java/com/magercam/app/StreamServer.kt:188-243](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L188-L243), [windows/client/main.c:600-622](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L600-L622), [obs-plugin/src/magercam.c:1070-1109](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L1070-L1109).

Пример запроса существующего клиента (адрес и PIN демонстрационные):

~~~http
GET /stream?w=1920&h=1080&fps=30&bitrate=8000&audio=1&camera=back HTTP/1.1
Host: 192.168.1.23:4848
X-MagerCam-Key: 123456
Connection: close

~~~

На wire строки завершаются CRLF; после последнего header — ещё CRLF. Host/Connection в примере допустимы, сервер их не использует. Нельзя оборачивать stream в Transfer-Encoding: chunked: существующие парсеры читают сразу magic MC. Реальный ответ:

~~~text
HTTP/1.1 200 OK\r\n
Content-Type: application/x-magercam\r\n
Cache-Control: no-cache\r\n
\r\n
<MC packets>
~~~

У HTTP коротких запросов один request на соединение, затем close. В /stream обратное чтение только выявляет EOF; in-band управляющих сообщений от ПК нет. Управление идёт отдельными HTTP-соединениями. [android/app/src/main/java/com/magercam/app/StreamServer.kt:157-186](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L157-L186).

### 6.2. Авторизация

PIN хранится как строка шести цифр, ведущие нули значимы. key в query приоритетнее X-MagerCam-Key. Из переданного значения Android удаляет нецифровые символы перед сравнением. При auth=false или loopback address проверка обходится. Wrong non-null key увеличивает счётчик по IP; отсутствие key не увеличивает его. После count>=10 следующие запросы получают 429, пока не истекли 60 s **от начала окна**, а не обязательно 60 s после десятой попытки; даже правильный PIN не проходит это раннее условие. Успешный PIN сбрасывает счётчик. [android/app/src/main/java/com/magercam/app/StreamServer.kt:144-154](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L144-L154), [android/app/src/main/java/com/magercam/app/Settings.kt:36-37](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Settings.kt#L36-L37), [android/app/src/main/java/com/magercam/app/Settings.kt:73-75](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Settings.kt#L73-L75).

Авторизация действует при открытии соединения. Ротация PIN не обрывает автоматически уже открытый stream. PIN и QR key передаются plaintext; query может попасть в history/log, браузер сохраняет PIN в localStorage. Это контроль доступа в доверенной LAN, не криптографическая защита канала. В проекте нет challenge-response, TLS, pairing certificate или session token. [android/app/src/main/assets/index.html:130-160](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/assets/index.html#L130-L160), [android/app/src/main/java/com/magercam/app/Core.kt:179-196](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L179-L196).

**Предложение:** PIN в Keychain, недопущение его в diagnostics, подтверждение пользователем намерения сопряжения в iOS UI, обычная совместимость X-MagerCam-Key/query. TLS-only или новый token нельзя включить вместо текущего транспорта без изменения клиентов; такие предложения только в разделе 21.

### 6.3. Все управляющие параметры

Общая логика applyParams: profile применяется первым, остальные значения поверх; неизвестные ключи игнорируются; Boolean true только строки 1/true/on, остальные значения при наличии ключа означают false. Числовой float допускает запятую через замену на точку. Неуспешный numeric parse обычно ничего не меняет. Ошибки unsupported enum/hardware не представлены HTTP 422. Сохранение и постановка задачи в camera handler могут опережать фактическое применение к устройству. [android/app/src/main/java/com/magercam/app/Core.kt:231-315](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L231-L315).

| Ключ | Реальная семантика/диапазон | Применение и ограничения |
|---|---|---|
| w, h | w 160..7680, h 120..4320; вне диапазона ignored | Restart; actual format может быть меньше |
| fps | Clamp 5..60 | Restart, затем camera/encoder cap |
| bitrate | Clamp 500..50000 kbit/s | Live bitrate target; ABR/heat могут снизить |
| camera | back/front/opaque ID | Текущий lens того же facing сохраняется; смена сбрасывает torch и перезапускает video/audio |
| audio | /stream: audio!="0" — подписка данного клиента; /api/set: alias mic, если mic не передан | Не путать локальное включение микрофона с желанием конкретного получателя |
| mic | Глобальное включение микрофона | Capture только при потребителе audio/record |
| audio_source | Строка; известные camcorder/mic/voice/unprocessed | Перезапуск audio; неизвестное значение сохраняется, capture выбирает fallback |
| torch | Boolean или toggle | Live, hardware capability |
| zoom | Clamp 0.1..100 в settings | Camera дополнительно clamp; status settings может не равняться физическому zoom |
| ev | Clamp −10..10 | Camera clamp в свой диапазон |
| focus | continuous/auto/infinity/macro/manual | Другие ignored; hardware mapping |
| focus_dist | Clamp 0..1, Android 0 infinity → 1 near | Live manual focus |
| af, x, y | af=trigger; координаты по умолчанию 0.5/0.5 | Sensor-normalized; камера clamp координат, учесть ориентацию preview |
| wb | Строка presets/auto | Неизвестная фактически fallback auto |
| antibanding | Строка auto/50/60/off | Hardware-dependent; неизвестная fallback auto |
| stab | Boolean | EIS; OIS отдельный |
| ae_lock, awb_lock | Boolean | По отдельности |
| lock | Boolean | CameraEngine меняет AE/AWB **и режим/позицию фокуса**; async |
| mirror | Boolean | Локальное preview, не H.26x rotate/mirror |
| orientation_mode | auto/landscape/portrait | Меняет orient metadata |
| name | Первые 60 символов | Статус/discovery |
| abr, heat_guard | Boolean | Адаптация/thermal guard |
| codec | /api/set: auto/h264; /stream: hevc означает способность клиента | В /stream удаляется из applyParams, затем участвует в общем выборе |
| framing | Boolean | Face crop; может переопределить zoom |
| bg | off/blur/image | Restart GL/encoder path |
| bg_strength | Clamp 0..1 | Blur/edge strength |
| rec | Boolean | Локальный recording start/stop, не toggle |
| profile | call/stream/doc | Пресет; неизвестный ignored |
| key | Auth | Удаляется до applyParams |

Порт, auth enabled, регенерация PIN, keepScreenOn, autoDim меняются локальным UI, не удалёнными ключами applyParams. [android/app/src/main/java/com/magercam/app/MainActivity.kt:793-825](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/MainActivity.kt#L793-L825). Запрос /stream тоже способен менять **общие** w/h/fps/bitrate/camera и прочие переданные controls: независимых capture-профилей по клиентам нет.

Примеры:

~~~http
GET /api/set?audio=0&torch=toggle&zoom=1.5&key=123456 HTTP/1.1
GET /api/set?profile=call&fps=24 HTTP/1.1
GET /api/set?af=trigger&x=0.25&y=0.75 HTTP/1.1
GET /api/set?codec=h264&orientation_mode=landscape HTTP/1.1
POST /api/background HTTP/1.1
Content-Length: 153600
Content-Type: application/octet-stream
X-MagerCam-Key: 123456

<153600 raw image bytes>
~~~

Первые четыре строки — отдельные запросы с завершающими headers/CRLF, сокращённые для иллюстрации. Upload не multipart/form-data и не base64.

### 6.4. MC header и payload

**Header ровно 16 bytes**, сериализация ByteBuffer big-endian. [android/app/src/main/java/com/magercam/app/StreamServer.kt:430-439](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L430-L439).

| Offset | Bytes | Тип | Значение |
|---|---:|---|---|
| 0 | 2 | ASCII | 0x4D 0x43 = MC |
| 2 | 1 | uint8 | packet type |
| 3 | 1 | bitfield | bit0 key; bit1 HEVC только VIDEO_CONFIG |
| 4 | 4 | uint32 BE | N, длина payload без header |
| 8 | 8 | int64 BE по документации | PTS microseconds, общая media clock domain |
| 16 | N | bytes | Payload |

Для совместимого iOS — неотрицательные PTS в пределах signed Int64, без переполнения при ×1000/×10 на клиентах. В header нет version, sequence number, DTS, CRC, channel ID, endian flag или ack. ver=2 живёт в JSON, не в magic.

| Type | Имя | Flags/PTS Android | Payload |
|---:|---|---|---|
| 1 | VIDEO_CONFIG | key bit=1; HEVC bit=0/1 → flags 1/3; PTS=0 | SPS+PPS AVC либо VPS+SPS+PPS HEVC, Annex B по контракту |
| 2 | VIDEO_FRAME | key bit=1 для key, иначе 0; PTS encoder | Один access unit, Annex B; HEVC определяется прошлой config |
| 3 | AUDIO_CONFIG | flags=0, PTS=0 | 00 00 BB 80 01 = BE32 48000 + mono |
| 4 | AUDIO | flags=0; PTS первого sample | PCM s16le mono; обычно 1920 bytes/20 ms |
| 5 | STATUS | flags=0, PTS=0 | UTF-8 JSON, как /api/status |
| 6 | PING | flags=0, PTS=0 | Пусто, N=0 |

Пример **полного валидного AUDIO_CONFIG** в hex:

~~~text
4D 43 03 00  00 00 00 05  00 00 00 00 00 00 00 00  00 00 BB 80 01
~~~

Пример header AUDIO: 1920 bytes, PTS=1,000,000 μs (после него ровно 1920 PCM bytes):

~~~text
4D 43 04 00  00 00 07 80  00 00 00 00 00 0F 42 40  <PCM>
~~~

Концептуальная VIDEO_CONFIG AVC (размер вычисляется по реальным NAL, это не тестовый bitstream):

~~~text
MC / type=1 / flags=1 / N / pts=0
00 00 00 01 <SPS NAL>
00 00 00 01 <PPS NAL>
~~~

Нельзя пересылать HEVC с flags=1 или выставлять HEVC bit только на VIDEO_FRAME. Нельзя считать один recv одним MC-пакетом. Предел приёма в обоих клиентах — 32 MiB payload; malformed magic/длина/EOF ведут к выходу и reconnect, не к поиску следующего MC внутри payload. [windows/client/main.c:626-680](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L626-L680), [obs-plugin/src/magercam.c:1110-1165](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L1110-L1165).

### 6.5. Начало, конфигурация и восстановление

При входе: применяются параметры → HTTP 200 → AUDIO_CONFIG (даже audio=0) → STATUS → cached VIDEO_CONFIG, если есть → регистрация клиента → пересчёт demand/codec → request keyframe. Из-за асинхронности status/cache могут отражать предыдущую конфигурацию; новая config позже исправляет её. iOS лучше привязать config cache к generation, не копируя риск stale cache. [android/app/src/main/java/com/magercam/app/StreamServer.kt:157-176](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L157-L176).

Клиенту не отправляются video frames до config и keyframe. Новая config сбрасывает waiting-key и очищает queued video. STATUS примерно раз в секунду и при изменениях. PING генерируется, когда writer не получил ничего за 1 s; при постоянном STATUS PING может не понадобиться. TCP idle без video не должен считаться здоровым capture: клиенты имеют отдельный ~8 s no-video watchdog. [android/app/src/main/java/com/magercam/app/Core.kt:66-72](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L66-L72), [android/app/src/main/java/com/magercam/app/StreamServer.kt:279-367](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L279-L367), [windows/client/main.c:626-650](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L626-L650).

### 6.6. JSON status: полный набор полей и типы

Источник — **код**, а не неполный пример документа: [android/app/src/main/java/com/magercam/app/Core.kt:353-400](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L353-L400).

| Поля | Тип/единицы | Значение и оговорки |
|---|---|---|
| name, ver | string, integer | Имя; protocol=2 |
| w, h, fps | integers | Active size/fps при открытой камере, иначе requested fallback |
| bitrate, bitrate_now | integers kbit/s | Requested верхняя граница и адаптированный target |
| camera, front | string, boolean | Resolved ID, facing |
| cameras | array объектов {id:string,front:boolean,label:string} | Клиенты используют opaque ID |
| sizes | array strings WxH | Поддержанные размеры; не objects |
| fps_list | array integers | Частоты; не size×fps capability matrix |
| orient, orientation_mode | integer degrees, string | Clockwise correction, auto/landscape/portrait |
| audio, audio_source | boolean, string | Глобальный mic enable, источник; не число audio subscribers |
| torch, has_flash | booleans | Requested torch и capability |
| zoom, zoom_min, zoom_max | numbers | Settings и пределы камеры |
| ev, ev_min, ev_max | numbers EV | Settings и пределы |
| focus, focus_dist | string, number 0..1 | Режим и Android-normalized distance |
| wb, antibanding | strings | Значения preferences, не гарантия hardware application |
| stab, ae_lock, awb_lock, mirror | booleans | Settings |
| battery, charging, temp | number %, boolean, number °C | Android battery telemetry |
| clients, viewers | integers | Число MC и MJPEG-подключений отдельно |
| camera_open, auth | booleans | Камера открыта, PIN включён |
| profile, codec, codec_pref | strings | Preset; active h264/hevc; auto/h264 preference |
| hevc, abr | booleans | Проверка encoder support; ABR enable |
| lock | boolean | AE && AWB, даже если focus lock сделан отдельно |
| framing, framing_available, faces | booleans, integer | Face crop enable/capability/число лиц |
| bg, bg_strength, bg_image | string, number, boolean | Режим, сила, сохранена ли картинка |
| rec | boolean | Recorder running |
| rec_ms | integer milliseconds, optional | Только при running; wall-clock duration в Android |
| hot, heat_guard | booleans | Thermal mode и настройка |
| ips, port | array strings, integer | IPv4 адреса, configured TCP port |
| error, bg_error, rec_error | strings, optional | Ошибки; serverError может заменить engine error |

**Предложение:** сохранять имена, JSON-типы и ordinary decimal numeric representation. Нельзя заменять boolean строкой или sizes массивом объектов. Новые поля допустимы только как дополнение после теста старых parsers, не как замена существующих.

iOS не предоставляет публичную Android-подобную battery temperature. Для temp нужен согласованный unknown policy: предпочтительно отсутствующее поле плюс capability/thermalState в будущем расширении; до согласования проверить default обоих parsers. Не писать выдуманные 31.5 °C. Для battery недоступное значение тоже не подменять измерением. camera_open, codec, rec должны отражать actual state; requested/applied различия показывать в iOS диагностике.

### 6.7. MJPEG и snapshot

Каждая MJPEG часть:

~~~text
--magercamframe\r\n
Content-Type: image/jpeg\r\n
Content-Length: <N>\r\n
\r\n
<N JPEG bytes>\r\n
~~~

HTTP Content-Type: multipart/x-mixed-replace; boundary=magercamframe. Финальный закрывающий boundary не нужен текущему бесконечному потоку: завершение через EOF. JPEG path независим от H.26x, не содержит audio и MC header. [android/app/src/main/java/com/magercam/app/StreamServer.kt:188-218](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L188-L218), [android/app/src/main/java/com/magercam/app/StreamServer.kt:374-390](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L374-L390). Веб поворачивает img по orient с CSS; H.26x клиенты поворачивают/зеркалят сами. [android/app/src/main/assets/index.html:212-231](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/assets/index.html#L212-L231).

## 7. Обнаружение устройств и QR-сопряжение

### 7.1. UDP discovery

PC отправляет на UDP 4849 datagram, начинающуюся с MAGERCAM_DISCOVER. Android trim + startsWith, то есть не требует точного совпадения всей строки; receive buffer 512 bytes. Ответ unicast на исходные IP/port:

~~~json
{"magercam":1,"name":"MagerCam iPhone","port":4848,"ver":2,"auth":true}
~~~

В ответе нет PIN и списка IP: клиент знает адрес по datagram sender. Android держит WifiManager.MulticastLock; исключение сокета вызывает повтор через 2 s. [android/app/src/main/java/com/magercam/app/Discovery.kt:17-70](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Discovery.kt#L17-L70).

Windows/OBS используют AF_INET, global/direct broadcast и дополнительно unicast по адресам своей /24. Это эвристика /24, не универсальный обход всех реальных subnet masks/VLAN. Клиенты принимают magercam>=1, dedup по IPv4, не выполняют строгого согласования ver. [windows/client/net.c:160-254](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/net.c#L160-L254), [obs-plugin/src/magercam.c:557-661](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L557-L661). Wi-Fi client isolation, guest network, firewall, VPN и несколько адаптеров могут ломать discovery при исправном приложении.

**Предложение iOS:** сохранить UDP 4849/request/reply через BSD socket + dispatch source либо проверенный Network.framework вариант. NWListener удобен для TCP; UDP broadcast semantics и ответ на source port отдельно доказать. Broadcast receive/send на iOS требует com.apple.developer.networking.multicast; entitlement предварительно разрешает Apple. [A42]. UDP unicast не требует этого entitlement, но ответ в LAN требует Local Network access. [A16]. Не заменять legacy UDP одним Bonjour: существующие клиенты Bonjour не используют.

Если entitlement не получен, проверить unicast discovery существующего /24 scan; это возможный обход, не гарантированная равноценная замена. Ввод IP работает как основной резерв. NSBonjourServices нужен только если позднее добавляется конкретный Bonjour service; не объявлять вымышленный service type ради legacy UDP.

### 7.2. QR: направление и точный обмен

Windows создаёт listener на первом доступном TCP 4850..4859, выбирает до четырёх адресов ПК, nonce из двух rand_s 32-bit значений в 16 hex digits и показывает:

~~~text
magercam://pair?h=192.168.1.10,10.0.0.2&p=4850&n=0123456789abcdef
~~~

iPhone должен **сам подключиться к ПК**, не ждать входящего pairing. По адресу h последовательно; порт p, nonce n; после подключения две LF-terminated строки:

~~~text
MCPAIR1 0123456789abcdef\n
{"name":"MagerCam iPhone","ips":["192.168.1.23"],"port":4848,"key":"123456","ver":2}\n
~~~

Ожидается OK\n. Android connect/read timeout 5 s; при выключенном auth key="". Windows read timeout 4 s и buffer 4096 bytes. Не добавлять HTTP headers, BOM или length-prefix к этому обмену. [android/app/src/main/java/com/magercam/app/Core.kt:179-196](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L179-L196), [android/app/src/main/java/com/magercam/app/MainActivity.kt:579-632](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/MainActivity.kt#L579-L632), [windows/client/pair.c:70-109](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/pair.c#L70-L109), [windows/client/pair.c:132-150](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/pair.c#L132-L150), [windows/client/pair.c:256-268](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/pair.c#L256-L268).

Windows берёт IP телефона из **peer address TCP**, игнорируя ips для выбора маршрута; после WM_APP_PAIRED закрывает окно pairing, сохраняет host/port/key, включает Wi-Fi и reconnect. [windows/client/main.c:1912-1936](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L1912-L1936). Поэтому QR не гарантирует работу через NAT/VPN: исходящий TCP к ПК и последующее входящее соединение к телефону должны оба маршрутизироваться.

Документ называет nonce одноразовым; handler сравнивает префикс строки через strncmp, без полного равенства, и не помечает nonce consumed сам. Закрытие окна после успешного UI event уменьшает окно повторов, но строгая replay protection не доказана. Нельзя считать QR криптографически защищённым pairing. Исправление Windows — отдельное согласование, не часть текущего отчёта. [docs/PROTOCOL.md:167-179](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/docs/PROTOCOL.md#L167-L179), [windows/client/pair.c:86-108](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/pair.c#L86-L108).

В OBS собственного QR listener/UI нет. Его подключают вручную/discovery и вводят PIN; Windows pairing не переносит ключ в OBS автоматически.

### 7.3. iOS permissions и UX

Нужны Camera usage description для scan/capture, Microphone usage description для audio, Local Network usage description для discovery/pairing. [A17], [A43]. Само listen/accept входящего TCP, по TN3179, **не требует** Local Network privilege; исходящее TCP и UDP response требуют. Поэтому нельзя диагностировать все отказы входящего /stream одной общей надписью «нет Local Network». Simulator не доказывает поведение этой privacy-системы. [A16], [A17].

Предложение UX: пользователь нажимает «Подключение» → видит назначение LAN-доступа → выполняется реальная нужная операция → обрабатываются waiting/denied и повтор после ответа. Отдельно проверяются Camera permission и QR URL. URL scheme зарегистрировать в будущем iOS target; разобрать h/p/n, ограничить длины/число адресов и показывать назначение передачи PIN. Не обращаться к адресам из QR незаметно для пользователя.

## 8. Таблица функций Android → iOS

Статусы ниже — **оценка переносимости**, не выполненные работы. «Полностью» означает наличие подходящего пути реализации без ожидаемого изменения wire contract; все строки ещё требуют реализации и испытания. P0 — MVP, P1 — базовое соответствие, P2 — расширенные возможности, P3 — исследование/отдельный продуктовый выбор.

| Функция | Исходный код | Текущее поведение | Эквивалент iOS | Статус | Ограничения | Обходной путь | Приоритет |
|---|---|---|---|---|---|---|---|
| Главный экран/preview | [android/app/src/main/java/com/magercam/app/MainActivity.kt:271-406](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/MainActivity.kt#L271-L406) | Один экран с surface/controls | SwiftUI + AVCaptureVideoPreviewLayer | Полностью | Capture не на UI thread | UIViewRepresentable | P0 |
| Задняя/передняя камера | [android/app/src/main/java/com/magercam/app/CameraEngine.kt:276-350](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L276-L350) | Camera2 facing/ID | AVCaptureDevice discovery/input | Полностью | Другая система ID | Короткие стабильные opaque IDs | P0 |
| Выбор отдельных объективов | [android/app/src/main/java/com/magercam/app/CameraEngine.kt:276-350](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L276-L350) | Logical/physical IDs | Physical/virtual AVCaptureDevice | Частично | Virtual camera может переключать constituent lens | Capability map, отдельные physical IDs | P1 |
| H.264 Wi-Fi | [android/app/src/main/java/com/magercam/app/VideoEncoder.kt:27-138](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/VideoEncoder.kt#L27-L138) | AVC + MC | VTCompressionSession + Annex B | Полностью | Нужна проверка MFT/FFmpeg | SDR8, no reorder, config/IDR | P0 |
| HEVC | [android/app/src/main/java/com/magercam/app/Core.kt:104-123](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L104-L123) | Все клиенты opt-in | VideoToolbox HEVC Main | Требует исследования | MFT availability, profile/level | Fallback H.264 | P2 |
| 720p30/1080p30 | [android/app/src/main/java/com/magercam/app/CameraEngine.kt:598-678](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L598-L678) | Выбор supported size/range | activeFormat + frame duration | Полностью | Actual mode подтверждать | При отказе явный меньший режим | P0 |
| 1080p60/4K60 | [android/app/src/main/java/com/magercam/app/CameraEngine.kt:415-434](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L415-L434) | Зависит от camera/encoder | Форматы AVFoundation + VT | Требует исследования | Thermal, Wi-Fi, decoder, concurrent effects | 4K30/1080p60/1080p30 | P2 |
| Live bitrate/ABR | [android/app/src/main/java/com/magercam/app/Core.kt:128-158](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L128-L158) | Очередь → bitrate | VT properties + queue metrics | Частично | Property support и реакция encoder | Verify output rate; reconnect при backlog | P1 |
| Keyframe/config/reconnect | [android/app/src/main/java/com/magercam/app/StreamServer.kt:279-367](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L279-L367) | Config then key, drops | Generation + ForceKeyFrame | Полностью | Старые callbacks и TCP backlog | Purge generation, закрыть зависший stream | P0 |
| PCM mono48k | [android/app/src/main/java/com/magercam/app/AudioCapture.kt:31-85](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/AudioCapture.kt#L31-L85) | s16le chunks20ms | CaptureAudio + AVAudioConverter | Полностью | Actual route format меняется | Resample и sample accumulator | P0 |
| Android AudioSource | [android/app/src/main/java/com/magercam/app/AudioCapture.kt:31-58](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/AudioCapture.kt#L31-L58) | camcorder/mic/voice/unprocessed | AVAudioSession category/mode/input | Частично | Нет точного соответствия четырём источникам | Согласованная таблица режимов, тест processing | P1 |
| Общие audio/video PTS | [android/app/src/main/java/com/magercam/app/AudioCapture.kt:59-85](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/AudioCapture.kt#L59-L85) | Clock/framePosition | synchronizationClock + CoreMedia | Полностью | Нужны clock conversion/latency correction | Один timeline, resampler accounting | P0 |
| Windows end-to-end A/V sync | [windows/client/main.c:682-702](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L682-L702) | Audio PTS игнорируются | Только правильная выдача PTS источником | Нужен обходной путь | Клиент/virtual camera retime | При строгом требовании OBS либо отдельная правка Windows | P1 |
| Tap AF/continuous AF | [android/app/src/main/java/com/magercam/app/CameraEngine.kt:231-261](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L231-L261) | AF/AE regions | focus/exposurePointOfInterest | Полностью | Camera capability, transforms | Координаты sensor space | P1 |
| Manual/infinity/macro focus | [android/app/src/main/java/com/magercam/app/CameraEngine.kt:787-839](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L787-L839) | Diopter normalized | lensPosition/focus modes | Частично | 0/1 обратны, infinity не гарантирована | Калибровка, UI approximation | P1 |
| EV/AE lock | [android/app/src/main/java/com/magercam/app/CameraEngine.kt:787-890](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L787-L890) | Bias + lock | exposureTargetBias/locked | Полностью | Hardware range | Clamp actual range | P1 |
| Ручные ISO/shutter | [android/app/src/main/java/com/magercam/app/CameraEngine.kt:787-890](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L787-L890) | Отсутствуют | AVFoundation может поддерживать | Требует исследования | Это новая функция, не parity | Не включать в MVP | P3 |
| WB presets/AWB lock | [android/app/src/main/java/com/magercam/app/CameraEngine.kt:787-890](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L787-L890) | Android enums | Temperature/tint→gains, lock | Частично | Разные цветовые характеристики | Нормированные preset approximations | P1 |
| Antibanding 50/60/off | [android/app/src/main/java/com/magercam/app/CameraEngine.kt:787-890](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L787-L890) | Camera2 control | Нет прямого общего эквивалента | Нужен обходной путь | Экспозиция 1/100/1/120 меняет AE | Согласовать no-op/error либо отдельный exposure mode | P2 |
| Zoom | [android/app/src/main/java/com/magercam/app/CameraEngine.kt:727-890](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L727-L890) | Ratio/crop | videoZoomFactor | Полностью | Доступный диапазон/lens jumps | Честные min/max | P1 |
| Stabilization | [android/app/src/main/java/com/magercam/app/CameraEngine.kt:880-890](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L880-L890) | EIS toggle + OIS отдельно | Video stabilization modes | Частично | Crop/latency и доступность по format | Поддержанный mode или off | P1 |
| Torch | [android/app/src/main/java/com/magercam/app/Core.kt:263-279](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L263-L279) | Toggle, сброс на смене камеры | torchMode/level | Полностью | Front/no torch/thermal unavailable | has_flash и actual state | P1 |
| Ориентация/зеркало | [android/app/src/main/java/com/magercam/app/Core.kt:318-329](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L318-L329) | Metadata orient, preview mirror | RotationCoordinator/preview transform | Полностью | Не допустить двойного поворота | Один documented pixel contract | P0 |
| HTTP API/PIN | [android/app/src/main/java/com/magercam/app/StreamServer.kt:80-154](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L80-L154) | Routes/key/header/rate limit | NWListener + HTTP parser | Полностью | Plaintext legacy | LAN UI + secret redaction | P0 |
| MJPEG/JPEG/web UI | [android/app/src/main/java/com/magercam/app/StreamServer.kt:188-243](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt#L188-L243) | Отдельный JPEG output | Pixel buffer → JPEG + multipart | Полностью | CPU/thermal cost, отличие effects | Throttle/latest-only | P1 |
| Discovery broadcast | [android/app/src/main/java/com/magercam/app/Discovery.kt:29-70](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Discovery.kt#L29-L70) | UDP4849 | BSD/Network UDP | Требует исследования | Multicast entitlement/privacy/AP | Unicast scan, ручной IP | P1 |
| QR pairing | [android/app/src/main/java/com/magercam/app/Core.kt:179-196](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L179-L196) | Phone→Windows TCP | Metadata QR + NWConnection | Полностью | Firewall и два направления TCP | Ручной IP/PIN | P1 |
| USB ADB | [windows/client/net.c:378-480](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/net.c#L378-L480) | adb forward/app start | Прямого аналога нет | Нельзя | iOS не ADB | Обычный IP по Wi-Fi; cable IP отдельно исследовать | P3 |
| Foreground/background service | [android/app/src/main/java/com/magercam/app/CameraService.kt:29-80](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraService.kt#L29-L80) | Capture вне Activity | Generic аналога нет | Нельзя | Camera background/lock restriction | Visible foreground mode | P0 |
| Dim/keep awake | [android/app/src/main/java/com/magercam/app/MainActivity.kt:435-503](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/MainActivity.kt#L435-L503) | Overlay/brightness/wake | isIdleTimerDisabled/brightness | Частично | Не блокирует ручной Lock, OS interruption | Восстановить brightness/idle timer при stop | P0 |
| Thermal guard | [android/app/src/main/java/com/magercam/app/Core.kt:160-172](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L160-L172) | Battery °C thresholds | thermalState/systemPressureState | Частично | Нет °C battery API | Согласованная pressure policy | P0 |
| Face framing | [android/app/src/main/java/com/magercam/app/CameraEngine.kt:727-783](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt#L727-L783) | Face crop/smoothing | Vision face detection/crop | Частично | Model latency, zoom conflict | Ограниченный FPS анализа | P2 |
| Background blur/image | [android/app/src/main/java/com/magercam/app/GlRenderer.kt:250-352](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/GlRenderer.kt#L250-L352) | GL + TFLite | Vision + Metal/Core Image | Частично | Другая маска/quality, нагрузка | Поздний этап, low-res segmentation | P2 |
| Upload background | [android/app/src/main/java/com/magercam/app/Core.kt:203-215](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L203-L215) | Raw image, downsample/save | ImageIO bounded decode/app storage | Полностью | Ограничить decompression memory | 12 MB wire limit + pixel limits | P1 |
| Локальный MP4 | [android/app/src/main/java/com/magercam/app/Recorder.kt:50-129](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Recorder.kt#L50-L129) | Отдельный H.264/AAC mux | AVAssetWriter | Частично | Второй encoder, storage, interruptions | Отдельный budget/Photos export | P2 |
| Profiles/preferences | [android/app/src/main/java/com/magercam/app/Core.kt:218-315](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt#L218-L315) | call/stream/doc, prefs | Typed settings/UserDefaults | Полностью | Запрос 60 fps может быть недоступен | Applied status и fallback | P1 |
| Browser motion detector | [android/app/src/main/assets/index.html:291-354](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/assets/index.html#L291-L354) | JS JPEG analysis | Сохранить web asset | Полностью | Только активная browser tab | Не обещать background phone detector | P2 |

## 9. Совместимость с Windows

### 9.1. Подключение и handshake

Исследован source APP_VERSION 1.1.3, тогда как Android version 1.1.4; protocol ver=2 общий. Привязка к commit важнее совпадения UI version. Клиент использует IPv4 Winsock, connect timeout ~3 s, receive timeout ~5 s. HTTP parser читает до CRLFCRLF в ограниченный буфер (~4 KiB), определяет status, не декодирует chunked и не проверяет MIME для MC. /stream query включает w/h/fps/bitrate/audio/camera, codec=hevc только при доступном synchronous/local HEVC MFT. [windows/client/main.c:20-93](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L20-L93), [windows/client/main.c:540-622](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L540-L622), [windows/client/net.c:24-135](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/net.c#L24-L135).

401/429 останавливают автоматические попытки до изменения конфигурации; 409 трактуется busy с повтором. Обычный reconnect ~1.5 s, но к **сохранённому адресу**: автоматическое rediscovery после смены IP не встроено. ~8 s без VIDEO_FRAME приводит к переподключению, даже если STATUS/PING приходят. [windows/client/main.c:626-650](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L626-L650), [windows/client/main.c:752-775](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L752-L775).

USB дополнительно вызывает adb devices/forward и может запускать com.magercam.app через adb. Имя пакета и PID Android специфичны. iOS-сервер не может заставить этот режим работать только совместимостью /stream. [windows/client/net.c:378-480](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/net.c#L378-L480).

### 9.2. Декодирование и динамическая config

VIDEO_CONFIG создаёт/пересоздаёт decoder по HEVC bit, затем bytes config подаются как sample. H.264 — Media Foundation decoder, HEVC — доступный MFT; NV12 output, MF_LOW_LATENCY, обновление output type при stream change. Входные bytes копируются непосредственно, PTS ×10 переводится в 100 ns MF sample time. Нет преобразования AVCC/HVCC в Annex B на клиенте. [windows/client/main.c:653-680](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L653-L680), [windows/client/decoder.c:19-44](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/decoder.c#L19-L44), [windows/client/decoder.c:117-165](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/decoder.c#L117-L165), [windows/client/decoder.c:198-263](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/decoder.c#L198-L263).

**Требует проверки:** VT profile/level, SPS/PPS, IDR и format transition декодируются данным MFT на конкретной Windows-машине. Наличие HEVC в iPhone не означает наличие HEVC decoder на ПК.

При decode error Windows уничтожает decoder и ждёт следующую VIDEO_CONFIG; при этом получение VIDEO_FRAME продолжает обновлять no-video watchdog. Есть риск «байты идут, изображение застыло» без автоматического восстановления. iOS должен гарантировать config при своих restart/reconnect, но не может обнаружить внутренний decode error клиента через этот протокол. Периодический повтор config ради такого исправления потребует отдельного анализа, поскольку config пересоздаёт decoder. [windows/client/main.c:653-680](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L653-L680).

Lag detection сравнивает локальный QPC и source PTS относительно накопленного baseline, при >400 ms начинает skip non-key до восстановления примерно <200 ms, keyframes пропускаются иначе. Это относительный контроль backlog, не измерение абсолютной glass-to-glass latency. [windows/client/main.c:458-481](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L458-L481).

### 9.3. Виртуальная камера, audio и управление

Decoded NV12 проходит local color/LUT, rotate/mirror/fill, затем shared memory. Четыре slot, верхний размер 3840×2160. В виртуальной камере есть фиксированные output media types 30/60 fps и собственный scheduler; source PTS в shared memory не передаётся, MF sample time берётся из MFGetSystemTime. Поэтому capture fps, decoder fps и advertised virtual camera fps — три разные величины. [windows/client/main.c:484-529](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L484-L529), [windows/common/mcv_shared.h:1-55](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/common/mcv_shared.h#L1-L55), [windows/client/vcam.c:107-138](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/vcam.c#L107-L138), [windows/vcam/vcam.cpp:744-806](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/vcam/vcam.cpp#L744-L806), [windows/vcam/vcam.cpp:919-980](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/vcam/vcam.cpp#L919-L980), [windows/vcam/vcam.cpp:1040-1054](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/vcam/vcam.cpp#L1040-L1054).

AudioConfig открывает PCM WASAPI endpoint; audio_write получает только bytes и frame count, не PTS. Shared-mode buffer около 100 ms, initial silence ~40 ms; при переполнении PCM обрезается/отбрасывается. Это вывод на audio device, не встроенный microphone driver; для передачи в приложение как mic может требоваться отдельно настроенный virtual cable. [windows/client/audio.c:10-11](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/audio.c#L10-L11), [windows/client/audio.c:59-120](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/audio.c#L59-L120), [windows/client/main.c:682-702](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L682-L702).

Remote controls используют GET /api/set, upload — POST raw bytes. Windows ждёт HTTP status, но не применяет JSON ответа как строгий ack; updated status поступает в stream. Размеры/fps/cameras читаются ограниченными массивами (примерно 32 sizes, 8 fps, 16 cameras); camera ID buffer 64 bytes. iOS не должен отдавать UUID с чрезмерной длиной или бесконечные capability lists. [windows/client/main.c:33-93](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L33-L93), [windows/client/main.c:335-422](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L335-L422), [windows/client/main.c:788-875](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c#L788-L875).

**Оценка:** без изменения клиента можно обеспечить H.264/PCM/manual IPv4, большую часть controls, QR/discovery при сетевых разрешениях; HEVC — условно. Нельзя объявить совместимыми ADB, гарантированный background и точный Windows A/V-sync. Обычный hosted windows runner не заменяет физическую Windows 11 с Media Foundation virtual camera/Frame Server, WASAPI, OBS и реальными consumer apps.

## 10. Совместимость с OBS

Исследованный plugin — Windows DLL на Winsock/D3D11, version 1.1.3. Наличие OBS на macOS/Linux само по себе не делает этот исходник/сборку кроссплатформенными. Зарегистрирован источник magercam_source с OBS_SOURCE_ASYNC_VIDEO | OBS_SOURCE_AUDIO | OBS_SOURCE_DO_NOT_DUPLICATE. [obs-plugin/src/magercam.c:1-29](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L1-L29), [obs-plugin/src/magercam.c:1839-1867](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L1839-L1867).

### 10.1. Сетевые ожидания

Есть IPv4 discovery и ADB path, manual host/port/key. OBS по умолчанию запрашивает HEVC, audio, hardware decode, low latency и remote control; format defaults 1080p30/8 Mbps. Query при control=true дополнительно несёт framing/lock/bg/bg_strength/zoom/EV/stab/WB/focus/antibanding. Подключение OBS способно перезаписать глобальные настройки телефона, даже если пользователь выставил их локально. [obs-plugin/src/magercam.c:984-1002](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L984-L1002), [obs-plugin/src/magercam.c:1318-1351](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L1318-L1351).

Timeouts/MC header/payload limit сходны с Windows: connect3 s, recv5 s, no-video8 s, maximum32 MiB, auth pause 401/429. Reconnect ~1 s, изменения settings debounce700 ms. deactivate_hidden/clear_on_disconnect управляют разрывом и сохранением последнего изображения; defaults false. [obs-plugin/src/magercam.c:1038-1256](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L1038-L1256), [obs-plugin/src/magercam.c:1395-1524](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L1395-L1524).

### 10.2. FFmpeg и audio

FFmpeg H.264/HEVC decoder принимает config и затем access units; optional D3D11 decoding с software fallback. Поддержанные output pixel formats — 8-bit YUV420P, YUVJ420P, NV12; **10-bit/HDR не является совместимым базовым потоком**. PTS передаётся как PTS и DTS, значит reorder/B-frames нельзя включать «по умолчанию VideoToolbox». Color matrix/range учитываются, rotate/mirror выполняются в plugin. [obs-plugin/src/magercam.c:679-711](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L679-L711), [obs-plugin/src/magercam.c:767-896](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L767-L896).

AUDIO_CONFIG задаёт rate/channels, AUDIO идёт в OBS как PCM s16 с timestamp, mapped общим механизмом с video. map_timestamp устанавливает общий offset и корректирует слишком старые/будущие значения; нормальная работа требует согласованной скорости audio clock и video clock. [obs-plugin/src/magercam.c:748-764](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L748-L764), [obs-plugin/src/magercam.c:1165-1200](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L1165-L1200).

Поддержка config change есть, но decoder recovery после аппаратного сбоя не равна доказанной автоматической replay конфигурации. Нужны сценарии reconfigure, H.264↔HEVC, hardware/software, повреждённого sample и нового IDR.

### 10.3. Controls и ограничения UI

OBS имеет controls профилей, эффектов, lock/framing, recording, torch/AF/camera, bitrate, audio; background image загружается через телефон/Windows/web, собственного POST upload UI в плагине нет. QR pairing UI тоже нет. [obs-plugin/src/magercam.c:1602-1659](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L1602-L1659), [obs-plugin/src/magercam.c:1811-1835](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L1811-L1835).

STATUS parser простой, не универсальный JSON model: сохранять имена, обычные числа, массив sizes строками, fps_list integers. error/bg_error/rec_error полноценно не отображаются в status updater, поэтому ошибка unsupported control может быть незаметна оператору OBS. Новый iOS UI должен показывать её локально; улучшение OBS capability/error UI — отдельное согласование. [obs-plugin/src/magercam.c:900-981](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c#L900-L981).

**Оценка:** OBS — наиболее подходящий неизменённый клиент для проверки общей audio/video timeline. Он всё ещё требует реального E2E-теста; успешный ffprobe локального H.264-файла не доказывает работу plugin, его clocks, scene lifecycle и rotation.

## 11. Общая матрица обратной совместимости

«Да» означает отсутствие очевидной необходимости менять код клиента, а не уже пройденный тест.

| Сценарий | Windows | OBS | Без изменений клиентов | Риск | Проверка |
|---|---|---|---|---|---|
| Manual IPv4 Wi-Fi + PIN + H.264 1080p30 | Поддержан parser/decode path | Поддержан | Да, ожидается | VT bitstream/profile | T04, T05 |
| PCM48k mono | WASAPI output | OBS source audio | Да, формат | Clock/route | T06 |
| Точная A/V синхронизация | PTS audio игнорируется | Общий timestamp mapper | Windows — не гарантируется; OBS — ожидается | Drift/independent clocks | T06, T07 |
| Смена resolution/fps/camera | Recreate decoder/config | Recreate FFmpeg | Да, условно | Старые callbacks/config | T08 |
| Одновременные Windows+OBS | Один общий encoder | Один общий encoder | Да | Controls last-writer/global codec | T09 |
| HEVC opt-in | Зависит от HEVC MFT | FFmpeg HW/SW | Да при available decoder | Profile/10-bit/format | T14 |
| H.264-only клиент во время HEVC | Должен принять new AVC config | Аналогично | Да, условно | Transition/IDR | T09, T14 |
| MJPEG/snapshot/browser | Внешний browser/consumer | Media source либо browser | Да | JPEG throughput/rotation | T11 |
| Controls базовой камеры | API | API | Да с адаптацией семантики | Focus/WB/stab | T08 |
| Unsupported iOS control | HTTP может выглядеть успешным | Error плохо виден | Только частично | Ложная parity | T08, E05 |
| Broadcast discovery | Existing UDP | Existing UDP | Да при entitlement/LAN | AP/permission | T10 |
| Unicast /24 discovery | Есть | Есть | Возможно без multicast entitlement | Сеть не /24, firewall | T10 |
| QR pairing | Есть TCP listener | Нет | Windows да; OBS неприменимо | NAT, firewall, nonce | T10 |
| Изменился IP телефона | Старый адрес сохранён | Старый адрес сохранён | Ручной rediscovery нужен | Автоматически не найдут новый IP | T12 |
| ADB USB | Android-only | Android-only | Нет | Нет adb на iOS | E07 |
| Кабельная IP-сеть | Manual IP теоретически | Manual IP теоретически | Требует исследования | Маршруты/tethering/iPhone listener | E07 |
| Home/Lock/background | Клиент переподключается | Клиент переподключается | Поток продолжать нельзя обещать | Ограничения камеры iOS | T12 |
| 4K60 + PCM | Decoder/virtual camera способны не всегда | CPU/GPU/FFmpeg зависимы | Требует исследования | Heat/bandwidth/drop | T14 |
| Background effects/MP4 | Controls существуют | Controls существуют | Да после реализации | Ресурсы, особенности output | T15 |

## 12. Ограничения iOS и обходные пути

| Ограничение | Подтверждение | Практическое решение |
|---|---|---|
| Generic background camera | Apple interruption reason videoDeviceNotAvailableInBackground [A18] | Foreground streaming; при уходе прекратить video и восстановить после active |
| Camera при Lock | Нет общего права непрерывного сетевого capture для обычного webcam app | Не обещать; visible dim mode, инструкция не нажимать Lock |
| Multitasking camera API | iOS16+, поддержка зависит от конфигурации/entitlement/режима [A19] | Проверять isMultitaskingCameraAccessSupported; не считать универсальным исключением |
| LockedCameraCapture | iOS18 extension, ограничения сети/контейнера [A20] | Не использовать как обход background streaming |
| Background audio | Audio session не даёт автоматически права на камеру; Review требует legitimate background use [A21] | Не добавлять VoIP/audio/PiP фиктивно ради удержания video |
| Local Network | Разные правила TCP accept/outgoing/UDP; simulator не моделирует privacy [A16] | Раздельная диагностика; physical permission tests |
| Thermal/pressure | ProcessInfo thermalState и camera systemPressureState [A22], [A23] | Снижать нагрузку, при critical остановить; никаких выдуманных °C |
| Camera/mic denial | Authorization status и usage descriptions [A17] | Preview/video-only при mic denial; понятный переход в Settings |
| Battery/idle | idle timer только автоматический sleep [A24] | Включать при активном сценарии, возвращать прежнее поведение при stop |
| Antibanding/WB/focus | API и шкалы отличаются [A03]–[A05] | Capability-aware mapping, фиксировать ограничения |
| Network address | Legacy consumers IPv4 | Показывать доступный IPv4, не обещать IPv6-only совместимость |
| Memory/thermal высоких режимов | Format support не гарантирует длительную комбинацию workloads | Admission control для эффекта/записи/4K60, измерение physical |

**Минимальная версия — предложение:** iOS 17.0, SwiftUI и Swift concurrency с изолированным capture executor. Она покрывает RotationCoordinator (17), synchronizationClock (15.4), Vision segmentation (15), NWListener/NWPathMonitor (12), перечисленные базовые VT/AV APIs. Это deployment target, не версия SDK сборки и не утверждение, что все поддержанные телефоны потянут 4K60. Для первого физического приёмочного стенда — только iPhone 17 Pro Max с зафиксированной установленной ОС; расширение устройства/ОС — после MVP.

Точный номер iOS на пользовательском iPhone и состояние Apple Developer Account в этом аудите не проверялись. Публичные спецификации камеры телефона не заменяют runtime перечисление AVCaptureDevice.formats и тест сочетания camera+encoder+audio+network.

## 13. Непрерывная передача видео и жизненный цикл приложения

### 13.1. Контракт непрерывной работы

**Предложение:** обещать непрерывную работу только при foreground, действующих permissions, исправной локальной сети и допустимом thermal state. Dim overlay и уменьшение яркости снижают нагрузку, но приложение остаётся видимым. Сохранить исходную brightness и восстановить при выходе; isIdleTimerDisabled включать на время пользовательского capture/stream режима, не навечно.

Inactive (например Control Center/system prompt) не всегда означает окончательный background. Решения принимать по сочетанию scenePhase и AVCaptureSession interruption events. При background — остановить capture/encoder/audio, безопасно завершить запись и закрыть media connections; короткий background task допустим только для завершения операций, не продолжения камеры. При foreground восстановить listener, capture resources, новый config/IDR и audio config. Автоматический resume — только если пользователь ранее оставил streaming intent и разрешения сохранились.

### 13.2. Состояния сессии

~~~mermaid
stateDiagram-v2
    [*] --> Idle
    Idle --> AwaitingPermission: Start
    AwaitingPermission --> Ready: allowed
    AwaitingPermission --> PermissionDenied: denied
    PermissionDenied --> AwaitingPermission: retry after Settings
    Ready --> Starting: preview/client/record demand
    Starting --> Streaming: capture + config + IDR
    Starting --> Recovering: setup error
    Streaming --> Reconfiguring: camera/format/codec
    Reconfiguring --> Streaming: new generation + config + IDR
    Streaming --> Interrupted: AV interruption / background
    Reconfiguring --> Interrupted: background
    Interrupted --> Recovering: foreground + available
    Streaming --> ThermalLimited: serious pressure
    ThermalLimited --> Streaming: stable recovery
    ThermalLimited --> Interrupted: critical
    Streaming --> Recovering: runtime reset/network loss
    Recovering --> Starting: resources ready
    Recovering --> Ready: no demand
    Streaming --> Ready: last consumer gone
    Ready --> Idle: Stop
    Streaming --> Idle: Stop
    Interrupted --> Idle: Stop
~~~

Состояния network listener и media session связаны, но не тождественны: HTTP status можно обслуживать при отсутствии camera demand, пока процесс работает; background process execution не гарантирован.

| Событие | Действие iOS (предложение) | Что видят старые клиенты | Проверка |
|---|---|---|---|
| Входящий звонок/audio interruption | Зафиксировать interruption, остановить нужные ресурсы, обработать ended/shouldResume | EOF/reconnect либо краткий gap | Нет старых PCM после resume; T12 |
| Смена mic route/Bluetooth/USB audio | Actual format заново, converter reset, единый timeline | PCM48k mono остаётся wire format | Нет pitch/rate change; T06 |
| Media services reset | Полное пересоздание AVAudioSession/capture/encoder | Reconnect, configs | Не повторять старые callbacks |
| Camera занята другим приложением | Состояние interrupted, bounded retry | Нет fake fresh frames | Restore после доступности |
| Screen lock/Home | Прекратить media, закрыть stream | Клиенты ожидают/повторяют; возможен frozen last frame в OBS | Не обещать background |
| Потеря Wi-Fi | NWPathMonitor + socket errors, отменить sends, очистить очереди | Timeout либо EOF | T12 с packet log |
| Новая сеть/IPv4 | Перезапустить binding/discovery, показать новый IP | Старый сохранённый IP не оживает | Ручной search/QR |
| Serious thermal/pressure | Сначала убрать дорогие effects, снизить bitrate/fps, затем size при необходимости | New status/config | Согласовать приоритет качества |
| Critical thermal/pressure | Остановить camera, сообщить причину локально | Disconnect | Без restart storm |
| Memory warning/storage full | Purge optional caches, прекратить recording/effects при необходимости | Stream сохраняется если возможно | T13/T15 |
| Явный Stop | Закрыть connections, stop sessions, release encoder, restore brightness/idle | EOF | Нет скрытого resume |

Audio interruption и route notifications могут приходить не на UI queue; обрабатывать через coordinator, не одновременно мутировать capture/audio state. [A25], [A26].

### 13.3. Восстановление соединения

~~~mermaid
sequenceDiagram
    participant PC as Windows / OBS
    participant Net as iOS TCP server
    participant C as SessionCoordinator
    participant V as VideoEncoder
    PC->>Net: GET /stream + PIN + capabilities
    Net->>C: authenticated subscription
    C->>V: select common codec / force IDR
    Net-->>PC: HTTP 200 + AUDIO_CONFIG + STATUS
    V-->>Net: current VIDEO_CONFIG + IDR
    Net-->>PC: CONFIG, IDR, subsequent packets
    Note over Net,PC: Wi-Fi loss or application background
    Net--xPC: close / timeout
    PC->>PC: retry saved address
    Note over C: foreground / network available
    C->>V: recreate if needed, new generation
    PC->>Net: fresh GET /stream
    Net-->>PC: configs + fresh IDR
    Note over PC,Net: If IP changed, manual discovery / QR needed
~~~

Восстановление должно быть идемпотентным: Stop отменяет pending retries; generation запрещает старому callback воскресить encoder; сетевой retry не запускает второй capture session. Ограниченный exponential backoff допустим внутри iOS для resource failure, но клиентские retry delays остаются существующими.

## 14. Предлагаемая архитектура iOS

Весь раздел — **предложение**, не описание созданного приложения. Будущий код расположен отдельно в ios/ только после явного разрешения. SwiftUI, AVFoundation, VideoToolbox, Network.framework/BSD, CoreMedia, AVAudioConverter, при необходимости Vision/Metal; внешняя server library на первом этапе не требуется. URLSession — клиентский API и не заменяет встроенный HTTP server.

### 14.1. Модули и границы

Интерфейсы ниже описаны концептуально, без исходного кода. Общие типы: RequestedSettings, AppliedState, CapabilitySnapshot, VideoSample, AudioChunk, EncodedAccessUnit, CodecConfiguration, MagerCamPacket, SessionEvent. Настройки отличаются от фактически применённых параметров. Каждая media unit несёт source PTS и generation.

| Модуль | Ответственность; вход → выход / интерфейс | Зависимости | Lifecycle/concurrency | Ошибки и тестируемость |
|---|---|---|---|---|
| AppUI/CameraViewModel | User intent, permissions/status → SwiftUI state; start/stop/set/scan | Coordinator, preview adapter | MainActor; подписка на immutable state | Fake coordinator, UI tests без камеры |
| SessionCoordinator | Demand/settings/events → ordered capture/audio/network actions; apply, subscribe, interrupt, resume | Все сервисы через interfaces | Один actor, конечный автомат; generation/cancellation | Typed errors и единый recovery; deterministic event tests |
| SettingsStore | Preferences/PIN → validated RequestedSettings | UserDefaults, Keychain | Actor; snapshot на startup; serialized writes | Memory store для unit tests, PIN redaction |
| CapabilityService | Devices/formats/VT supported properties → CapabilitySnapshot и legacy JSON projection | AVFoundation/VT adapters | Capture queue; rebuild при camera/format change | Synthetic capability matrices; не объявлять unsupported |
| CameraCaptureService | Applied capture plan → CMSampleBuffer + preview + device state | AVCaptureSession/device/output | Один serial executor; begin/commit configuration, start/stop off main | Inject sample source; interruption/runtime error events |
| AudioCaptureService | Capture audio + route → source audio samples/format/clock | AVCaptureSession, AVAudioSession | Serial audio handling; coordination при activate/reset | Route fixtures, permission denial, no realtime blocking |
| AudioFormatConverter | Source audio/timestamps → 960-frame PCM48k s16le chunks | AVAudioConverter, clock mapper | Audio worker; bounded ring buffer, reset на route | Known tone/impulse + sample-count tests |
| MediaClock | Session clock + samples → monotonic microsecond PTS | CoreMedia clocks | Один timeline owner; conversion без wall clock | Virtual clocks, drift/epoch/reset property tests |
| VideoEncoder | Pixel buffers/PTS/settings → encoded AU + config | VideoToolbox | Serial session control; callbacks возвращаются в ordered generation pipeline | Fake encoder; real VT fixture tests на macOS/iPhone раздельно |
| AnnexBAdapter/PacketCodec | CMSampleBuffer/config → Annex B → exact MC bytes | CoreMedia + pure byte logic | Stateless/serial ordered output | Golden vectors, truncated NAL fuzz, endian boundaries |
| HTTPControlServer | TCP bytes → bounded HTTP parse/routes/auth; responses | NWListener/NWConnection, command adapter | Network queue; один parser state на connection | Segmented input, malicious lengths, route/auth corpus |
| CompatibilityAdapter | Legacy query/status ↔ typed settings/state | Coordinator, auth policy | Actor command serialization; no hidden capture mutation | Все параметры раздела 6, JSON schema/type fixtures |
| StreamHub/ClientSession | Encoded units/PCM/status → per-client queues/TCP writes | PacketCodec, network transport | Bounded queues; один send in-flight per client; cancellation | Slow sink/partial write; config/IDR ordering; counters |
| DiscoveryService | UDP request → legacy JSON reply | BSD socket/dispatch, status snapshot | Start/stop with network availability | Datagram fixtures; physical entitlement test отдельно |
| PairingService | User-approved QR URL → TCP lines/OK result | QR reader, NWConnection | One cancellable attempt chain, timeouts | Fake PC, multi-host/failure/malformed QR |
| JPEG/WebService | Latest pixel buffer → JPEG/parts/snapshot; bundled HTML | ImageIO/CoreImage, StreamHub routes | Throttled worker, latest-only, no queue growth | JPEG validity/MIME/aliases/orientation |
| Effects/Recorder | Video+PCM → processed frames/MP4 | Vision/Metal/AVAssetWriter | Resource budget, optional workers, PTS preserved | Quality references, disk full, interruption/finalization |
| Diagnostics/ThermalPolicy | Counters/system events → redacted logs/policy | OSLog, ProcessInfo, pressure state | Low-rate telemetry; no per-frame main-thread logging | Inject thermal states; export без PIN/raw private media |

Interface ownership: CameraCaptureService не знает HTTP, PacketCodec не знает камеры, UI не вызывает set torch напрямую. Coordinator согласует глобальный codec/format для всех клиентов; StreamHub различает per-client wantsAudio/wantsHevc. Error propagation — typed event → AppliedState/error → local UI и legacy status. Существующие clients не умеют transactional acknowledgements, поэтому actual state обновляется после применения, а не только после получения команды.

### 14.2. Общая схема

~~~mermaid
flowchart LR
    UI["SwiftUI / Permissions"] --> C["SessionCoordinator actor"]
    Store["Settings / Keychain"] <--> C
    HTTP["HTTP + auth + query adapter"] --> C
    C --> Cap["Capabilities"]
    C --> Cam["CameraCaptureService"]
    C --> Aud["AudioCaptureService"]
    Cam --> Preview["Preview"]
    Cam --> FX["Optional effects"]
    FX --> VT["VideoToolbox"]
    Cam --> VT
    Aud --> AC["Audio converter"]
    Clock["MediaClock"] --> VT
    Clock --> AC
    VT --> AB["Annex B adapter"]
    AB --> Hub["StreamHub / bounded client queues"]
    AC --> Hub
    C --> Status["Legacy STATUS projection"]
    Status --> Hub
    Hub --> TCP["Existing Windows / OBS"]
    Cam --> JPEG["JPEG / MJPEG"]
    JPEG --> HTTP
    Status --> Disc["UDP discovery"]
    UI --> Pair["QR + pairing TCP"]
    C --> Rec["Optional Recorder"]
    FX --> Rec
    AC --> Rec
~~~

### 14.3. Media pipeline и правила памяти

~~~mermaid
flowchart TD
    V["Capture pixel buffer + source PTS"] --> Choice{"Effects enabled?"}
    Choice -->|No| E["VT encode"]
    Choice -->|Yes| FX["Bounded Metal/Vision path"]
    FX --> E
    E --> Desc["Format description / parameter sets"]
    E --> AU["Length-prefixed AU"]
    Desc --> CFG["Annex B VIDEO_CONFIG"]
    AU --> Annex["Validate NAL lengths → Annex B"]
    Annex --> VF["VIDEO_FRAME + key + PTS"]
    A["Audio source sample + PTS"] --> Res["Resample / mono / PCM16"]
    Res --> Chunks["960 frames, PTS first sample"]
    Chunks --> AP["AUDIO"]
    CFG --> Q["Per-client ordered bounded queue"]
    VF --> Q
    AP --> Q
    Q --> Send["TCP send completion"]
    Q --> Drop["Backlog: discard video → request IDR"]
    Drop --> E
~~~

CVPixelBuffer удерживается только пока нужен encoder/effect, не копируется каждый раз в Swift Data. Compressed bytes копируются/retain-ятся с контролируемым lifetime; одинаковый immutable packet можно разделять между clients. Отдельные лимиты: capture frames in-flight, conversion ring, compressed per-client bytes/time, HTTP header/body, image decode dimensions, recording buffer. Значения лимитов — измеряемое решение этапа 0/1, не обещание allocator behavior.

Предложенные начальные policy points: video backlog500 ms/6 MB как ориентир Android, audio1 s/12 MB, один новый IDR request не чаще2 s при congestion; дополнительно hard total cap и disconnect при stuck send. STATUS coalescing до последнего состояния допустим только с сохранением config order. Никогда не отбросить config и затем отправить зависимые P-frames.

### 14.4. A/V synchronization

~~~mermaid
flowchart LR
    VC["Video capture PTS"] --> Convert["CoreMedia clock conversion"]
    AC["Audio capture PTS + sample count"] --> Convert
    SC["AVCaptureSession synchronizationClock"] --> Convert
    Convert --> TL["One monotonic timeline, microseconds"]
    TL --> VP["VIDEO_FRAME PTS"]
    TL --> RP["Resampling delay / sample accounting"]
    RP --> AP["AUDIO PTS first output sample"]
    VP --> OBS["OBS common timestamp offset"]
    AP --> OBS
    VP --> WV["Windows decode / independently retimed vcam"]
    AP --> WA["Windows WASAPI: PTS unused"]
~~~

Предложение clock policy: один origin на session generation, порядок PTS каждого media track монотонный; sample duration вычисляется рационально (1/48000), накопление дробной части обязательно. Для 60 fps не округлять каждый frame interval до одного целого microsecond с накоплением ошибки. Mute не меняет epoch. После interruption продолжать clock только при доказанной непрерывности; иначе fresh TCP stream/config. Audio silence вставлять лишь по явно определённой gap policy, не маскировать пропавший микрофон бесконечно.

### 14.5. Организация будущего проекта

Предложение структуры: ios/App (SwiftUI), ios/Core (pure settings/protocol/state machine), ios/Media, ios/Network, ios/Resources (web asset), ios/Tests, ios/UITests, ios/Fixtures и декларация генерации проекта. Это **перечень будущих каталогов, сейчас их нет**.

Для Windows-редактирования удобен XcodeGen: декларативный project spec, генерация на macOS CI, pinned release **2.46.0** как кандидат, verified checksum и smoke test с выбранным Xcode. Версия подтверждена официальными releases; совместимость конкретного проекта ещё не проверена. Не обновлять генератор автоматически на каждом запуске. [G01]. Dependency graph сначала преимущественно Apple frameworks, без FFmpeg внутри iOS. Лицензии копируемых HTML/моделей/кода проверяются отдельно.

## 15. CI/CD через GitHub Actions

### 15.1. Реалистичная базовая среда

Это проект CI; workflows сейчас отсутствуют и **YAML не создаётся**. Windows пригодна для редактирования Swift/spec, review, protocol fixture scripts и управления GitHub. Сборка/линковка iOS SDK, simulator tests, archive и codesign выполняются на macOS/Xcode.

Проверенная официальная таблица Xcode на дату аудита содержит stable 27.1 и 27.2 RC, а также 26.6; это не означает наличие всех этих версий на конкретном Actions image. В проверенном pinned runner manifest images/macos/macos-26-Readme.md (commit 3b5e6b447abb7eadff4a19ac472e806b1e063f42, обновление 2026-08-28; image 20260824.0517.1) указан Xcode **26.6 / 17F113**, путь /Applications/Xcode_26.6.app, iOS SDK26.5 и соответствующие simulators. [A27], [G02].

**Предложение первоначального pin:** macos-26-intel и DEVELOPER_DIR на Xcode26.6, **только после проверки доступности этим job**. Label закрепляет семейство/архитектуру, но не immutable VM image. На старте выводить sw_vers, uname, xcodebuild -version/-showsdks и список runtimes; если pin отсутствует — fail с понятной причиной, а не незаметный переход на latest. Intel выбран для соответствия изученному manifest; arm64 — отдельная проверяемая оптимизация. GitHub публикует отдельные runner labels и лимиты. [G03].

С 28 апреля 2026 Apple требует для upload iOS/iPadOS SDK26 или новее; выбранный SDK26.5 этому условию соответствует. Deployment target iOS17 не противоречит SDK26.5. Перед первым upload повторно проверить актуальное требование и installed iPhone OS; дату/версию зафиксировать в build provenance. [A28].

### 15.2. Шесть концептуальных workflows

| Workflow | Trigger и runner | Проверки/шаги | Signing/доступ | Artifacts и gate |
|---|---|---|---|---|
| PR validation | Pull request; Linux для чистых scripts, macOS для Swift/iOS-specific compile | Schema/format/lint по выбранным правилам, pure unit tests, project generation validation, dependency lock | Без signing secrets, contents read | Test reports/logs/spec diff; merge gate |
| Simulator build/test | PR или main, macOS pinned Xcode/runtime | Generate project; simulator build; unit/UI tests с fake capture/network | CODE_SIGNING_ALLOWED=NO где применимо | Simulator .app, .xcresult, screenshots, build log; не IPA для телефона |
| Protocol compatibility | PR/main; macOS pure/VT fixtures + Windows parser/client harness где возможен | Golden packet/query/status tests, socket fragmentation, emulator regression, codec fixture decode | Без production secrets; fixtures sanitized | Packet traces, decode checksums/metadata, JUnit; real clients E2E отдельно |
| Unsigned device compile | PR/main, macOS generic iOS device | arm64 device target compile/link без signing | Нет сертификата; не installable | Build logs/unsigned .app; доказывает SDK compilation, не запуск |
| Signed archive/export | workflow_dispatch либо approved tag; protected environment, macOS | Re-run gates на exact SHA; import credentials; archive; export App Store Connect/Ad Hoc variant; inspect entitlements/profile | Distribution certificate/private key + matching profile | .xcarchive/.ipa/dSYM/provenance/checksum; restricted retention; ещё не публикация |
| TestFlight upload | Отдельный ручной approved dispatch/tag после signed gate | Upload exact approved build, проверить processing, metadata/compliance, назначить тестовую группу | ASC API key с нужной ролью; protected environment | Build number/ASC URL/upload status, redacted logs; только processed build считается доставленным |

Pure protocol suite не должна зависеть от физической камеры; macOS VideoToolbox encode не подменяет iPhone hardware encode. Windows hosted job может проверить parsing/build и часть FFmpeg, но не гарантирует WASAPI output/Frame Server/virtual camera/OBS GUI. Полный E2E выполняется на выделенном Windows11 стенде и физическом iPhone с теми же binaries; результат привязывается к IPA SHA/build number.

### 15.3. Подпись и секреты

Необходимые сущности различны:

| Сущность | Назначение | Хранение |
|---|---|---|
| Team ID / Bundle ID | Идентичность приложения | Обычно build config, не private key; владелец выбирает Bundle ID |
| Apple Distribution certificate + private key | Подпись executable | Password-protected P12, защищённый secret store |
| Provisioning profile | Разрешённые App ID/team/cert/entitlements, для Ad Hoc UDID | Protected CI secret/file; проверять expiration/capabilities |
| App Store Connect API private key .p8 + Key ID + Issuer ID | Авторизация upload/API | Protected secrets; key скачивается один раз, роли ограничены |
| Temporary keychain password | Доступ codesign в одном job | Случайный на job, не репозиторий |
| Apple Account password/2FA | Владение аккаунтом | Владелец вводит сам; не передавать в chat/log/artifact |

ASC API key **не заменяет** signing private key/profile. Автоматическая выдача сертификатов — отдельная операция с соответствующими permissions, не следствие наличия upload key. В ручном базовом варианте profiles/certificates подготовлены владельцем заранее. [A29], [A30].

На macOS job: temporary keychain в RUNNER_TEMP, import P12, ограниченный unlock, key partition list для codesign, install matching profile, archive/export, inspect signature/entitlements; cleanup keychain/profile/keyfiles в always/finally. Base64 — только transport encoding, не шифрование. Не печатать decoded content. Официальный GitHub процесс подтверждает такую модель. [G04].

PR из fork не получает secrets; не запускать untrusted source в privileged pull_request_target job. Signed job привязан к reviewed commit, protected environment и минимальным permissions; Actions pinned по commit, provenance включает SHA исходников, Xcode, SDK, generator, dependency locks, build number и SHA256 IPA. Caches не содержат credentials и не являются авторитетным build output.

### 15.4. Logs, artifacts, стоимость и отладка

Предложение retention: обычные logs/.xcresult 14 дней, signing diagnostics только redacted, release dSYM/archive с контролируемым доступом и сроком поддержки build, sanitised protocol fixtures дольше. Не загружать PIN/QR nonce, raw микрофон/камеру и P12 в обычные CI artifacts. Для физической отладки: встроенный diagnostic export, counters, reasoned state transitions; TestFlight feedback/crash data плюс dSYM. Пошаговый debugger/USB profiling потребуют отдельного Mac/device lab доступа, их нельзя обещать через один hosted runner.

GitHub standard runners для public repositories обычно бесплатны; private usage зависит от плана/quota, larger runners тарифицируются отдельно. На дату проверки standard macOS rate — $0.062/min сверх применимых включённых условий; это не гарантированный бюджет проекта. Пример оценки: 1000 оплачиваемых macOS минут ≈$62, без хранения/других услуг. [G05], [G06]. Предложение: отменять устаревшие PR runs, разделять cheap checks и macOS jobs, concurrency limits, timeouts, ручной archive, бюджетный лимит до включения CI. Не запускать дорогой nightly stress без выбранного physical lab.

## 16. Установка на iPhone без Mac

### 16.1. Основной путь: TestFlight

**Рекомендуется TestFlight**, потому что установка идёт непосредственно на iPhone через TestFlight, без UDID, кабеля к Mac и Developer Mode. Потребуется платная Apple Developer Program и ручная настройка владельцем; на дату проверки членство стоит 99 USD/год с региональной ценой. [A31], [A32], [A33].

Пошаговый процесс после согласования разработки:

1. Владелец проверяет Apple Account/2FA, вступает в Developer Program, принимает соглашения.
2. Регистрирует explicit App ID/Bundle ID, выбирает team, создаёт app record в App Store Connect. Не использовать предполагаемый Bundle ID без выбора владельца.
3. Включает нужные capabilities, при необходимости запрашивает multicast entitlement; profile должен содержать одобренные entitlement. Usage descriptions находятся в app plist, не заменяют entitlement.
4. Подготавливает distribution signing assets и provisioning profile, ограниченный ASC API key. Через веб-интерфейс GitHub вводит secrets в protected environment.
5. На Windows редактируется код; GitHub macOS job делает archive, codesign, export и upload утверждённого SHA. Для upload доступны Apple-supported Xcode/Transporter tooling/API; конкретный CLI и параметры фиксируются по выбранному Xcode на этапе настройки. [A34].
6. Дождаться обработки build в ASC, заполнить требуемые privacy/export compliance/beta metadata, выбрать test group.
7. Владелец iPhone устанавливает TestFlight из App Store, принимает приглашение и ставит build. На первом старте выдаёт Camera/Microphone/Local Network permissions по назначению.
8. В диагностике приложения сверяет build number/SHA, затем подключает Windows/OBS. Feedback/crash сохраняется вместе с версией клиента и условиями теста.

TestFlight build доступен 90 дней; до 100 internal testers — пользователи ASC с подходящими ролями, external — до 10 000. Первый внешний build требует beta review, последующие тоже могут попасть на review. Для владельца проекта internal testing удобнее, если роль/тип команды это позволяют; external testers не нужно делать администраторами. [A32]. Processing/upload успех не означает автоматическое одобрение review или доступность всем группам.

**Bootstrap без локального Mac:** App ID/profile/API key настраиваются в браузере; signing key/CSR можно подготовить локально подходящим OpenSSL-инструментом и получить certificate через Developer portal, затем собрать P12. Это предлагаемый инженерный путь, который ещё нужно проверить на выбранной Windows-среде и политике аккаунта; команды сейчас не выполняются. Если нужен именно документированный Apple Keychain Assistant workflow, использовать отдельно доступный Mac/cloud Mac для первоначальной подготовки credentials. Такой удалённый Mac решает создание сертификата, но сам по себе не даёт физического подключения домашнего iPhone для debugging. Apple описывает CSR через Keychain Assistant. [A35].

### 16.2. Резерв: Ad Hoc

Ad Hoc требует App ID, distribution certificate/private key, зарегистрированный UDID iPhone и соответствующий provisioning profile; лимит регистрации — до 100 устройств каждой product family за membership year, отключение устройства не возвращает quota немедленно. [A36], [A37].

Предложенный путь без локального Mac: владелец получает проверенный UDID устройства → регистрирует его в portal → генерирует Ad Hoc profile → CI экспортирует Ad Hoc IPA и OTA manifest → размещает их на контролируемом HTTPS hosting → на iPhone открывает install link/manifest в Safari. Apple описывает экспорт manifest для over-the-air installation. Простой download IPA из GitHub artifact и открытие его в Files **не является установкой**. Ссылки должны быть доступны системному installer, а не зависеть от cookie интерактивной GitHub-сессии; доступ/срок хранения ограничиваются. [A38].

**Важное ограничение резерва:** текущая Apple-инструкция для распространения на зарегистрированные устройства требует Developer Mode для запуска IPA-based app; инструкция Developer Mode отдельно описывает pairing и возможное отсутствие переключателя до его инициации. Поэтому нельзя обещать, что Ad Hoc на данном никогда не paired iPhone будет полностью настроен только Windows+Safari. На этапе 0 нужен эксперимент OTA install/launch/Developer Mode на конкретной ОС. Если он не проходит, сохранять TestFlight основным путём; доступ к физическому Mac/device lab для первичного pairing — отдельное решение, а не скрытое обязательство пользователя. [A39], [A33].

Profile/certificate могут истечь или быть отозваны; после обновления списка UDID profile пересоздаётся и IPA переэкспортируется. Ad Hoc не является бессрочным «установить один раз и забыть».

### 16.3. Сравнение вариантов

| Вариант | Mac где нужен | Account/signing | Установка/ограничения | Оценка |
|---|---|---|---|---|
| TestFlight | macOS CI для сборки; локальный Mac не нужен | Developer Program, distribution signing, ASC | iPhone TestFlight, 90 дней/build, processing/review | **Основной** |
| Ad Hoc OTA | macOS CI; device pairing может потребовать отдельного доступа | Paid team, cert/profile/UDID | HTTPS IPA+manifest, Developer Mode проверить, device quota/expiration | **Резерв после физического smoke test** |
| Development signed build | macOS CI и обычно Mac-connected device для полноценного install/debug | Development cert/profile/registered device | Developer Mode/pairing, debugging; не считать Windows click-install | Для lab, не основная доставка |
| Free Personal Team | Xcode/Mac workflow | Бесплатный account с ограничениями | Ограничения App IDs/devices/profiles; короткие сроки, переустановка | Не базировать устойчивый no-Mac CI на нём |
| Codemagic/другой macOS CI | Облачный Mac провайдера | Те же Apple требования + доверие сервису | Может автоматизировать assets/build/upload | Альтернатива Actions, не обход signing |
| App Store public/unlisted/custom | macOS CI, Apple review | Program/ASC/metadata | Подходящий канал после готовности продукта | Позже, не ускорение первого технического spike |
| Enterprise | macOS CI + допустимая организация | Отдельная программа/условия | Только подходящее внутреннее распространение | Не персональный обход |
| EU alternative marketplace/web distribution | macOS build и соответствующие Apple механизмы | Eligibility/notarization/региональные условия | Не универсальная sideload IPA-ссылка | Для этого проекта не рекомендовано |
| Сторонняя переподпись/«магазины сертификатов» | Зависит от инструмента | Доверие чужим ключам/account, revocation | Сроки, entitlement loss, security/reliability | Не основной и не резервный инженерный путь |

Основания: account/free provisioning [A40]; Codemagic собственная документация signing [C01]; Apple distribution [A38], [A39], [A41]. Возможность установки через сторонний сервис не доказывает совместимость multicast entitlement или стабильность подписи.

### 16.4. Ручные действия владельца

Нужны решения/действия, которые CI не может принять за владельца: тип и членство Apple team, Bundle ID, согласия/договоры, роли ASC, запрос entitlement, выдача и хранение credentials, включение protected environment, test group/review metadata, installation/permissions на физическом iPhone, при Ad Hoc — UDID и Developer Mode. Секреты не нужны в тексте согласования или в этом отчёте. Сейчас ни аккаунт, ни certificates/profiles, ни GitHub settings не изменялись.

## 17. Стратегия автотестов

**Ни один тест ниже в рамках аудита не запускался.** Это программа будущей верификации. Наличие зелёного CI не заменяет реальный iPhone/Windows/OBS. Каждый результат должен содержать source SHA, app build, OS, client/plugin binary hash, codec/format, сеть и профиль нагрузки.

### 17.1. Что уже есть и чего эти инструменты не доказывают

- tools/fake-phone.js запускает FFmpeg, генерирует H.264/HEVC и PCM, обслуживает часть API/discovery; полезен для client regression. Но encoder отдельный на connection, timestamps зависят от arrival/now, config PTS не как у Android, нет полного status schema, auth/rate limit/MJPEG/background semantics отличаются, любые /api/* упрощены. Поэтому **не golden oracle Android**. --stall моделирует трёхсекундную задержку примерно каждые 15 s, а не полный набор TCP failures. Есть зависимость от FFmpeg и Windows font path. [tools/fake-phone.js:1-196](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/tools/fake-phone.js#L1-L196).
- tools/obs-test.js подключается к OBS WebSocket, создаёт/меняет source/scene, получает screenshots, проверяет некоторые операции. Это изменяющий состояние smoke script, а не доказательство latency/drift/audio correctness. Default port 4466 требует согласования с настройкой OBS; глобальный WebSocket требует подходящего Node runtime. [tools/obs-test.js:1-111](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/tools/obs-test.js#L1-L111).
- windows/tools/vcamtest.c проверяет enumeration/direct/create/read/feed и может писать BMP/инжектировать кадры. Успешное получение кадров не доказывает корректные PTS, A/V sync или 4K60. Запуск требует подготовленного Windows стенда. [windows/tools/vcamtest.c:1-65](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/tools/vcamtest.c#L1-L65), [windows/tools/vcamtest.c:120-163](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/tools/vcamtest.c#L120-L163).
- Существующие build/setup scripts создают build/dist и ставят/скачивают зависимости; install.ps1 регистрирует COM в HKLM и меняет firewall. Они прочитаны, но не выполнялись. Фактическое QR firewall rule имеет Profile Any, несмотря на более узкие формулировки README; это важно для lab setup, не разрешение менять firewall сейчас. [windows/build.sh:8-27](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/build.sh#L8-L27), [obs-plugin/build.sh:9-18](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/build.sh#L9-L18), [windows/install.ps1:87-99](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/install.ps1#L87-L99).

### 17.2. План тестов

Все числовые критерии с пометкой П — **предлагаемые**, не существующие нормативы MagerCam. Детальные измерения в разделе 20.

| ID / цель | Входные данные | Среда | Автоматизация CI | Физическое устройство | Pass/fail и артефакты |
|---|---|---|---|---|---|
| T01 Packet codec | Header types1..6, known lengths/PTS, endian boundaries, malformed/truncated NAL | Pure Swift/core test | Да, каждый PR | Нет | Byte-exact golden output, reject overflow; fixtures + test report |
| T02 HTTP/auth/query | Все routes/aliases, valid/missing/wrong PIN, 10 failures/time window, duplicate keys, fragmented headers/body, 12MB boundary | Loopback fake transport + real listener | Да, deterministic clock | Нет | Exact status/body/parameter projection; request/response corpus без настоящего PIN |
| T03 State/config ordering | Concurrent clients, generation switches, stale callbacks, stop/interruption events | Fake media/clock/network | Да | Нет | Нет frame до config+key; no old-generation packets; event trace |
| T04 H.264 against Windows | Real iPhone 1080p30 SDR8, config/NAL capture, unchanged MagerCam.exe | Windows11 + iPhone LAN | Лишь parser/fixture часть | **Да** | Decoded dimensions/FPS/rotation, no fatal decode; pcap, decoder log, screenshot + binary hashes |
| T05 H.264 against OBS | Тот же signed build, unchanged plugin; FFmpeg SW/HW paths | Windows11 + OBS | Лишь harness/fixture часть | **Да** | Active source/rendered image correct; OBS log, local recording, counters |
| T06 PCM/route/audio | Known tone/impulse/counting fixture; mic mute/unmute; built-in/Bluetooth/доступный external input | Converter unit + iPhone + receivers | Conversion/clock tests да | **Да** для routes | 48k mono s16le, 960-frame normal chunks, no pitch/byte-order error, recovery; WAV/reference/counters |
| T07 A/V offset/drift | Одновременный видимый flash и acoustic click раз в заданный интервал | Calibrated rig + OBS recording + Windows consumer output | Analysis scripts да | **Да** | Пороги отдельно для OBS/Windows, раздел20; waveform/frame timestamps, drift plot, uncertainty |
| T08 Controls/reconfigure | Все параметры раздела6, rapid camera/fps/codec/orientation changes, out-of-range values | Synthetic API + physical camera | Parser/state tests да | **Да** hardware | Applied state честен, config before IDR, no crash/freeze; command trace + status snapshots |
| T09 Multi-client/backpressure | Windows+OBS, audio0/1, HEVC/H264 mix, throttled sink/slow TCP reader | Loopback/net impairment + LAN | Queue tests да | **Да** full path | Fast client bounded latency, common codec correct, no unbounded RAM; queue/drop/bitrate logs |
| T10 Discovery/QR/privacy | broadcast/unicast, multiple PC NICs, bad nonce, multiple h, port conflicts, Local Network allow/deny/revoke | Fake PC + real LAN/AP/firewall | Packet/parser tests да | **Да** для iOS privacy | Correct sender port/IP/key transfer, graceful failure/no PIN logs; redacted packet trace |
| T11 Web/MJPEG/JPEG | Все aliases, browser auth, snapshot timeout, multipart boundaries, uploaded valid/invalid images | Browser + synthetic JPEG + iPhone | Routes/image parsing да | **Да** quality/load | Valid JPEGs, correct lengths/no audio, expected orientation, upload errors; HTTP trace/screenshots |
| T12 Lifecycle/reconnect | Home/Lock/foreground, call, media reset, Wi-Fi off/on, IP change, crash/relaunch, Stop during retry | State tests + iPhone/Windows/OBS | Event tests да | **Да** реальное поведение | No hidden capture promise, no stale playback after resume, bounded reconnect sameIP; event timeline |
| T13 Long run/thermal | 1080p30 PCM, charging/unplugged, controlled room temp; thermal fault injection | Physical iPhone + Windows11/OBS | Policy injection да, endurance нет | **Да** | П 2h baseline, resource plateau/no critical failure; thermal/memory/FPS/queue series |
| T14 HEVC/high quality | Каждая camera×codec×size×fps combination, 4K30/4K60/1080p60, fallback | iPhone17 Pro Max + actual decoders | Bitstream decode fixtures да | **Да** | Mode proven separately, П60min/mode, downgrade correctness; SPS/VPS/profile/FPS/thermal report |
| T15 Effects/recording | Portrait/background edges, image upload, stream+MP4, disk-full/interruption | Synthetic images + physical camera | File/format/partial writer tests да | **Да** | Valid finalized MP4 audio/video, no streaming collapse; sample files/checksums + quality review |
| T16 Build/sign/delivery | Reviewed SHA, dev/distribution profiles, invalid/expired profile cases | macOS Actions + ASC + iPhone | Compile/sign validation да | **Да** installation | Exact IPA installs/launches, entitlements match, build visible in TF; provenance + redacted install result |
| T17 Resource/security robustness | Slowloris-like headers, body limits, invalid UTF8/JSON/lengths, connection churn | Isolated owned test server | Да, bounded tests | Желательно memory confirmation | No crash/OOM/leak; deterministic rejections, no secret logs; sanitizer/fuzz reports |
| T18 Android regression | Golden Android capture + unchanged clients + emulator | Android physical baseline + Windows/OBS | Golden decode/parse да | **Да** исходный Android для эталона | iOS additions не меняют Android-compatible path; differential report |

### 17.3. Fixtures и oracle

На этапе 0 после разрешения получить реальные Android wire captures: H.264 config/IDR/P-frame, HEVC config/IDR, PCM, STATUS, переход codec, reconnect. Личные видео/звук не сохранять: использовать тестовую таблицу и synthetic tone/controlled scene. Sanitise PIN/IP/name. Важно сохранять реальные raw bytes config: fixture, полученная только FFmpeg fake-phone, не отвечает на вопрос о MediaCodec framing.

Новый iOS encoder проверяется двумя способами: (1) structural NAL/PTS/protocol validation; (2) реальные неизменённые receivers. «Decode succeeds» недостаточно — проверить visual orientation/color, temporal cadence, audio content и отсутствие накопления задержки.

CI-compatible protocol harness можно разрабатывать как новый тестовый модуль будущей iOS-части; копирование/изменение tools/fake-phone.js, obs-test.js или production clients допускается только после отдельного согласования. Сейчас эти файлы не меняются.

### 17.4. Измеримость и ручная часть

Glass-to-glass latency — одновременная съёмка источника таймера/LED и экрана получателя внешней high-speed камерой либо калиброванный visual stimulus/recording метод. Разность часов iPhone и ПК без синхронизации не считается latency. Capture-to-send, queue age и decode-to-present полезны как раздельные внутренние метрики.

A/V drift — изменение offset flash/click между началом и концом длительного теста, плюс наклон offset по времени. Акустический путь и capture pipeline имеют постоянную задержку: измерить/учесть, указать погрешность. Для Windows проверять именно конечный consumer с выбранной виртуальной audio маршрутизацией; loopback WASAPI alone не доказывает синхронность видеозвонка.

Dropped frames: отдельно capture dropped, encoder dropped, server queue discarded, decoded и presented; denominator и причины фиксировать. В baseline без impairment — процент от захваченных пригодных кадров; intentional congestion drops учитывать отдельно и оценивать recovery, а не требовать нуля.

## 18. Реестр рисков

Вероятность/влияние — качественная предварительная оценка, не статистика эксплуатации.

| ID | Риск | Причина | Вероятность | Влияние | Снижение | Проверка | Этап |
|---|---|---|---|---|---|---|---|
| R01 | VT stream не декодируется | AVCC/AnnexB/profile/config mismatch | Средняя | Критическое | Explicit NAL adapter, SDR8, no reorder | T01/T04/T05 | 0–1 |
| R02 | Невозможно обещанное background/Lock | Ограничения iOS camera | Высокая, ограничение платформы | Критическое для такого требования | Согласовать foreground-only продукт | T12 | 0 |
| R03 | Windows A/V расходится | Audio PTS ignored, vcam retiming | Высокая для строгой гарантии | Высокое | OBS reference; согласовать scope либо client fix | T07 | 0–2 |
| R04 | Discovery не работает | Entitlement/privacy/AP/client isolation | Средняя | Высокое | Ранний entitlement check, unicast/IP/QR | T10 | 0/4 |
| R05 | IPA собрана, но не устанавливается | Team/profile/cert/UDID/Developer Mode | Средняя | Высокое | TF main, early delivery smoke | T16 | 0–1 |
| R06 | Старый encoder callback портит stream | Асинхронные reconfigure/generation | Средняя | Высокое | Generation ownership/config barrier | T03/T08 | 1 |
| R07 | Неограниченная задержка/RAM | Backpressure и kernel send backlog | Средняя | Высокое | Hard caps/send tracking/disconnect | T09/T17 | 1 |
| R08 | Аудио имеет неправильную скорость/drift | Actual route rate не48k, resampler accounting | Средняя | Высокое | Inspect formats/common clock/fractional counts | T06/T07 | 2 |
| R09 | 4K60 неустойчив | Heat, Wi-Fi, decoder, simultaneous work | Высокая | Высокое | Per-mode gate/fallback, no blanket marketing | T13/T14 | 5 |
| R10 | HEVC доступен не всем ПК | MFT/driver/environment | Средняя | Среднее/высокое | Exact opt-in, H.264 default fallback | T14 | 5 |
| R11 | Незаметный freeze decoder | Client waits config; watchdog смотрит packets | Средняя | Высокое | Config at restart, detect visually/log, optional client proposal | T04/T05/T08 | 1/5 |
| R12 | Неправильный focus/WB/antibanding | API semantics отличаются | Высокая | Среднее | Capability/adaptation matrix, honest state | T08/E05 | 3 |
| R13 | Thermal telemetry вводит в заблуждение | Нет battery °C API iOS | Высокая | Среднее | Unknown temp policy, thermalState локально | E06/T13 | 0/3 |
| R14 | Два клиента конфликтуют | Global settings/shared encoder | Высокая | Среднее | Сохранить и объяснить last writer semantics | T09 | 1/3 |
| R15 | QR/PIN разглашены | Plaintext, URL/log, weak nonce comparison в PC | Средняя | Высокое | Redaction/trusted LAN/explicit pair intent; fixes на согласование | T02/T10/T17 | 0/4 |
| R16 | Утечка signing secrets | CI logs/cache/artifacts/untrusted PR | Низкая при контролях | Критическое | Protected env, least privilege, temp keychain/cleanup | T16/security review | 0 |
| R17 | Изменился hosted runner/Xcode | Mutable image labels | Средняя | Среднее | Version assertions, explicit upgrade PR, provenance | Build preflight | 0 и далее |
| R18 | Эмулятор даёт ложное чувство parity | fake-phone не Android oracle | Высокая без real fixtures | Высокое | Android capture + differential corpus | T18 | 0 |
| R19 | Нет полноценной физической отладки без Mac | Hosted CI не имеет доступа к домашнему iPhone | Высокая | Среднее/высокое | Diagnostics/TF; бюджет device lab при необходимости | T12/T16 | 0 |
| R20 | Запись/эффекты рушат streaming budget | Второй encoder, сегментация, storage | Средняя | Высокое | Отдельный admission control/priority | T15 | 6 |
| R21 | Несовместимая лицензия/права на распространение | LICENSE GPLv2, сторонние model/assets | Не оценена | Высокое для публикации | Подтвердить права/лицензионную модель до переноса/дистрибуции | Owner/legal review | 0 |
| R22 | Необнаруженная регрессия Android | Правка общих clients ради iOS | Средняя | Высокое | Не менять без approval; Android regression baseline | T18 | Каждый изменяющий clients этап |

Основание R21 — наличие GPL version2 текста [LICENSE:1-12](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/LICENSE#L1-L12); здесь не даётся правовое заключение о допустимости App Store. Нужна проверка авторских прав, зависимостей и выбранного способа распространения, особенно перед копированием web asset/модели/существенного кода.

## 19. Пошаговый план переноса

**Ни один этап этим документом не запускается, включая этап 0.** Следующий шаг возможен только после явного подтверждения владельца. Этапы имеют выходные доказательства и stop/go gate; календарные сроки до первых экспериментов ненадёжны.

| Этап | Состав работ после разрешения | Выход | Зависимости/условие перехода |
|---|---|---|---|
| **0. Зафиксировать контракт и стенд** | Android real captures; packet/query/status golden corpus; проверить licences, foreground scope, iPhone OS, Apple team/entitlement, CI pin; proof of signing/delivery минимального разрешённого target; receiver binary hashes | Contract dossier, test fixtures, mode/permission inventory, delivery smoke result, утверждённые exceptions | Владелец разрешил реализацию; способ установки подтверждён; R02/R03 признаны |
| **1. Минимальное H.264 Wi-Fi** | SwiftUI start/status/preview, AV capture 1080p30, VT→AnnexB, TCP/HTTP auth/status/stream, config/IDR, common codec initially AVC, queues/reconnect, foreground lifecycle | Один signed build, видео в неизменённых Windows и OBS | T01–T05/T09/T12/T16; этап1 criteria ниже |
| **2. PCM и A/V** | Mic permission, audio session, converter48kmono, common clock, mute/route/interruption, OBS A/V and Windows limitation measurement | Audio-compatible build и drift report | Video stable; T06/T07/T12; не смешивать AAC recording с network audio |
| **3. Controls и status parity** | Camera/lens/format/fps/bitrate/AF/EV/torch/WB/zoom/stab/orient, profiles, ABR/thermal mapping, all JSON fields/unsupported policy; JPEG/web baseline | Полная базовая command matrix, honest applied state | T02/T08/T11/T13; решения focus/antibanding/temp утверждены |
| **4. Discovery и QR** | Legacy UDP broadcast/unicast, entitlement/profile, Local Network UX, QR URL+Windows TCP, multi-NIC/firewall checks | Подключение без ручного IP где разрешено; fallback documented | T10/T12; multicast entitlement либо согласован reduced mode |
| **5. HEVC и high-quality modes** | Common codec negotiation, H264↔HEVC,1080p60/4K30/4K60 per device, thermal/network fallback | Проверенный список поддерживаемых combinations с duration/receiver limits | T14/T09/T13; каждый режим имеет evidence, 4K60 не blanket promise |
| **6. Дополнительные функции Android** | Face framing, background blur/image, local MP4/audio, расширенный web motion UX, polish/accessibility/localization | Feature parity report и ограничения ресурсов | T15/T18/full regression; quality/MP4 acceptance и licensing |

Этап0 не должен незаметно превратиться в разработку всех UI. Его цель — устранить самые дорогие неизвестные: wire compatibility, установка, foreground expectation и реальный физический test loop. Если H.264 не проходит оба неизменённых receiver, переход к эффектам/4K60 запрещён до выяснения причины.

Функции реализуются вертикальными сценариями: например «подключить Windows → увидеть первый IDR → поменять камеру → восстановить после сети», а не только набором файлов с компиляцией. На каждом этапе повторяются contract tests; полный physical regression — после изменения capture/encoder/audio/queues.

## 20. Критерии успешности этапов

**Все численные целевые пороги в этом разделе — предлагаемые для согласования**, кроме формата протокола (16-byte header,48k PCM и т.п.) и явно приведённых существующих client timeouts. Это не якобы уже заданные требования MagerCam и не результаты измерений.

### 20.1. Условия измерения

Базовый стенд: iPhone17 Pro Max с зафиксированным iOS build, foreground, SDR, 1920×1080@30 H.264, audio48kmono; Windows11/OBS versions и binaries записаны; 5/6GHz Wi-Fi без client isolation, ПК предпочтительно Ethernet к тому же AP, RSSI/RTT/packet loss фиксируются. Room temperature и charging mode фиксируются; провести отдельные plugged/unplugged тесты. Эффекты/recording выключены для baseline. Часы/измерительный rig имеют документированную погрешность.

| Метрика | Предлагаемый порог | Метод и границы |
|---|---|---|
| Glass-to-glass video latency | median <=200 ms, p95<=300 ms на baseline | >=100 flash/timer observations/receiver; separate Windows preview/vcam consumer/OBS |
| First image after successful authenticated HTTP connection | <=3 s p95 | HTTP headers complete → первый действительно отображённый keyframe, 30 trials |
| Reconnect same IP | <=5 s p95 после доступности TCP/listener; <=15 s p95 от восстановления Wi-Fi для blackhole case | 30 disconnect cycles; учитывается текущий recv5s/no-video8s; не распространяется на новый IP/время ответа на permissions |
| Baseline delivered FPS | Среднее >=98% выбранной cadence за10 min; отсутствие необъяснённых gaps>500 ms | По decoded/presented frames, не UI timer |
| Resolution/orientation | 100% верные active dimensions и 0/90/180/270 orientation cases | SPS/decoder metadata + визуальная таблица; preview и receiver отдельно |
| Baseline drops | <=0.5% пригодных captured frames за10 min | С раздельными counters capture/encoder/queue/decoder; intentional impairment отдельно |
| Keyframe recovery | Новый клиент/config начинает decode с config+IDR; valid picture <=3 s p95 | 30 joins и30 format/codec changes |
| Queue bound under slow reader | Video age обычно <=500 ms до drop, hard cap не нарушен; fast client p95 latency рост<=100 ms | Искусственный reader stall10s; показать send-in-flight/TCP backlog и disconnect, не только deque age |
| OBS absolute A/V offset | <=80 ms magnitude после измерительной компенсации | Flash/click в итоговой OBS recording |
| OBS accumulated A/V drift | Изменение offset <=20 ms за30 min и <=40 ms за2h | Regression slope и start/end observations; clock discontinuities отдельными событиями |
| Windows A/V | Измерить абсолютный offset/drift тем же методом; целевой порог такой же **только при отдельном принятии зависимости от клиента** | Если не проходит из-за audio/vcam retiming, этап iOS audio не объявлять «точный Windows sync», оформить scope exception или правку клиента |
| Audio recovery | <=3 s после ended+shouldResume и доступного route | Не от начала звонка;30 mute/route/interruption trials |
| Baseline endurance | 2h без crash/deadlock/OOM/необъяснённого frozen output | Logged media continuity, thermal и state events |
| Memory stability | После warm-up10min footprint growth <=15% за оставшийся2h baseline, без устойчивого восходящего тренда | Абсолютный budget уточнить stage0/1; отдельно peak от bounded image upload |
| High-quality endurance | >=60min для каждого заявленного codec×size×fps; для release target2h | Если thermal fallback произошёл, это pass fallback policy, но **не** pass устойчивого исходного4K60 |
| Discovery | >=95% обнаружений за3s в поддержанной LAN,20 trials | Отдельно broadcast и unicast; не blanket обещание guest/VPN |
| QR | >=95% pairing за10s после выдачи permissions,20 trials | PC IP/ports available, затем реальный stream; never wrong peer |
| Camera controls | 100% documented commands корректно применены либо честно обозначены unsupported | Таблица request→actual state; no silent false success |
| Signed delivery | Установка/запуск exact approved build и permissions на физическом iPhone | TF build number+IPA SHA/provenance; не только archive success |

### 20.2. Gate каждого этапа

| Этап | Обязательный критерий выхода |
|---|---|
| 0 | Подтверждены SHA/binaries/Android fixtures, выбран account/channel, есть delivery smoke, согласованы foreground-only и Windows sync limitation; нет неизвестного блокера signing |
| 1 | Windows и OBS реально показывают H.264 без source changes; byte/headers/config/IDR tests зелёные; resolution/FPS/latency/reconnect thresholds пройдены; 2h video endurance |
| 2 | PCM wire format подтверждён, tone/voice без искажения, OBS drift thresholds пройдены; mic denial/route/interruption recovery; Windows measurement+явное решение по отклонению |
| 3 | Все базовые controls/status поля классифицированы и протестированы; unsupported не выдаются за работающие; JPEG/web и ABR/thermal transition работают; no regression этапов1–2 |
| 4 | Discovery/QR на real iPhone; allow/deny/revoke tested; fallback IP работает; secrets не в логах; не требуется модификация Windows/OBS |
| 5 | Для каждого заявленного high-quality mode отдельный60min evidence; HEVC fallback и mixed-client transitions; sustained4K60 объявляется только если не было снижения режима |
| 6 | Effects quality принята на фиксированном наборе сцен; MP4 воспроизводится с корректной длительностью/audio и завершается при interruption; no stream regression; итоговый2h выбранного release режима |

Если предложенные latency/drift thresholds не достигаются, сначала локализовать capture/encode/network/decode/display вклад и согласовать scope/порог на данных. Нельзя молча заменить измерение «стало быстрее на глаз» или объявить снижение fps успешным4K60.

## 21. Предлагаемые изменения существующего протокола/клиентов — только на согласование

**Ни одно изменение ниже не сделано и не входит автоматически в разрешение написать iOS MVP.** Базовый план сохраняет ver2, routes, MC и existing clients. Доработки предложены только там, где они дают конкретную пользу либо нужны для более сильного требования.

| ID | Предложение | Причина/минимальный объём | Совместимость Android и approval gate |
|---|---|---|---|
| C01 | Дополнительные capability/actual/unsupported поля JSON, без удаления старых | Нет точного antibanding/temp/focus parity; UI clients сейчас не умеет capability gating | Старые поля остаются; старые parsers проверить; изменение UI клиентов отдельно |
| C02 | Windows A/V clock scheduling | Использовать audio PTS, coherent video timing/output; сейчас клиент игнорирует часть timing | Это значимая Windows/vcam/audio работа, не «мелкая правка iOS»; Android regression обязателен |
| C03 | Decoder recovery и error visibility | На decode failure reconnect/replay config; watchdog должен учитывать decoded frames; OBS показывать error/bg_error/rec_error | Не меняет Android wire; тесты real failure; согласовать каждый client |
| C04 | Дополнительный Bonjour discovery | Дать iOS-friendly discovery при поддержке клиентов | **Дополнение**, не замена UDP; нужен согласованный service type/version и обновление клиентов |
| C05 | Более защищённое pairing/transport | Полное nonce equality/expiry/consume в Windows; позднее negotiated TLS/token | Nonce fix можно совместимо с Android; TLS-only сломает старых, нужен параллельный/versioned путь |
| C06 | USB/IP transport для iPhone | Исследовать допустимый tunnel/tethering, не ADB | Отдельный проект/driver/tooling review, не обязательство MVP |
| C07 | Строже HTTP parsing/resource limits | Ограничить total headers/connection/body, invalid methods, stuck writer | Новое iOS может быть строже на invalid inputs, valid existing clients сохраняются; если менять Android — отдельный approval |
| C08 | Обновить docs/PROTOCOL.md по коду | fps5..60, audio distinction, lock/focus, mirror/MJPEG, startup/config/limits | Только документация после отдельного согласования; сейчас этот файл не изменён |
| C09 | Улучшить existing tools/tests | Android fidelity fake-phone, explicit assertions/audio/drift, CI portability | Изменение tools/ только после разрешения; сохранять нынешние сценарии |
| C10 | Закрепить semantic policy temp/thermal | Unknown temperature без fake °C, optional thermalState/capabilities | Сначала тест default parsers; не менять ver бессистемно, не выдавать missing за0°C |

Не требуется менять протокол только потому, что VideoToolbox выдаёт AVCC/HVCC: правильное место конверсии — iOS adapter. Не требуется менять PCM на AAC ради iOS; converter решает соответствие существующему формату. HDR, stereo, новый transport и per-client независимые форматы — новые возможности, их нельзя подмешивать в compatibility MVP.

## 22. Непроверенные вопросы и технические эксперименты

| ID | Неизвестное | Эксперимент после разрешения | Решение по результату |
|---|---|---|---|
| E01 | Реальный VT H.264/profile/parameter sets совместимы с MFT/OBS? | iPhone capture encode→exact MC, run unchanged receivers; parse SPS/PPS/NAL | Выбрать verified profile/level/property set |
| E02 | Насколько надёжен GOP/ForceKeyFrame при выбранных VT properties? | Joins/congestion/reconfigure, измерить IDR interval/size | Не включать low-latency property без cadence proof |
| E03 | Реальный clock и converter delay на iPhone | Flash/click+route rates, long run, clock mapping | Настроить sample accounting/gap policy |
| E04 | UDP entitlement и unicast fallback | Signed entitlements inspection, allow/deny, broadcast/unicast AP tests | Full discovery либо согласованный reduced scope |
| E05 | Focus/WB/stab/antibanding на конкретных lenses | Enumerate support, calibrated scene, request→actual | Partial parity labels, UI/control policy |
| E06 | temp missing/unknown совместимы с parsers? | Inject missing/null/negative variants в test harness и real UI | Выбрать безопасное honest schema до релиза |
| E07 | Возможен ли кабельный IP к listener iPhone из Windows? | Только после выбора разрешённого способа: адреса/маршруты/TCP bidirectional | Отдельный поддерживаемый режим либо исключить |
| E08 | Список sustainable high-quality modes | Device formats + VT support + actual60min runs на charging/unplugged | Проверенный capability whitelist/fallback |
| E09 | Ad Hoc no-local-Mac installation/Developer Mode | Получить UDID/OTA install на exact iOS, проверить first launch | Backup подтверждён либо использовать TF/device lab |
| E10 | Current runner/Xcode pin доступен в аккаунте? | Preflight job после разрешения CI; exact image/toolchain log | Зафиксировать pin или отдельный upgrade |
| E11 | Неисправный decoder восстанавливается без client changes? | Fault injection только на test bench, new config/reconnect | iOS workaround если безопасен либо C03 |
| E12 | Effects/recording одновременно с high-quality | Budget CPU/GPU/memory/thermal, MP4 finalization | Ограничить combinations и priority |
| E13 | Какой формат Android реально присылает CSD на разных devices? | Sanitised capture с исходного Android, без нормализации ffmpeg | Подтвердить documented AnnexB и golden corpus |
| E14 | Права/лицензии/владение assets/model/code | Инвентаризация зависимостей и прав с владельцем | Выбор лицензии/замены/канала распространения |

Неизвестны: установленная iOS на iPhone, конкретный Windows GPU/HEVC MFT/OBS version, топология Wi-Fi, Apple membership/roles, наличие multicast entitlement, GitHub plan/budget, доступ к Android эталону. Эти сведения не предполагались полученными. На этом этапе задавать PIN, .p8, P12 или пароль Apple не требуется.

## 23. Итоговая рекомендация и условия начала реализации

Начинать с **foreground IPv4 Wi-Fi H.264 SDR1080p30 → неизменённые Windows+OBS**, затем PCM и OBS A/V sync. Сохранить MC16-byte framing, legacy HTTP/auth/query/status semantics, shared encoder negotiation и explicit config/IDR recovery. Native iOS архитектура подходит; прямой перенос Android lifecycle и raw MediaCodec byte handling не подходит.

Перед началом владелец должен явно согласовать:

1. Переход от аудита к реализации, включая этап0, будущий ios/ и тестовую/CI инфраструктуру; сейчас разрешён только этот документ.
2. MVP и исключения: foreground-only, отсутствие ADB USB, unsupported/approximate Android camera controls; iOS17 target как предложение, physical priority iPhone17 Pro Max.
3. Audio acceptance: строгая общая синхронизация проверяется в OBS; Windows ограничение принимается либо отдельным scope утверждается C02.
4. Канал доставки: Developer Program + TestFlight, доступный budget CI, team/Bundle ID и действия владельца; Ad Hoc — условный резерв до E09.
5. Объём initial parity: включать ли web/MJPEG в этап3, а effects/MP4/4K60 оставить отдельными gates; какие режимы считать release requirements.
6. Политику unavailable temperature/antibanding/focus и отдельные предложения C01–C10, **если** они понадобятся. Согласие на iOS не означает автоматическое согласие менять Android/Windows/OBS.
7. Лицензионные права на перенос/копирование ресурсов и выбранное распространение.

**Условие stop/go:** если нужно обязательно продолжать video на заблокированном iPhone или получить точный Windows A/V-sync без изменения клиента, исходную формулировку требований необходимо пересмотреть до разработки. Эти требования нельзя честно закрыть обещанием «AVFoundation умеет камеру».

### Контроль завершения данного аудита

| Пункт | Результат |
|---|---|
| Android исходники и число экранов | Все12 Kotlin + manifest/build/web исследованы;1 native screen по принятому критерию |
| Кодеки/аудио/протокол/endpoints | Разделы5–7; явные code/docs расхождения отмечены |
| Discovery/QR и оба receiver | Разделы7/9/10/11, привязка к SHA |
| VideoToolbox/iOS lifecycle | Разделы5/12–14, ограничения и эксперименты отделены |
| CI/signing/install без локального Mac | Разделы15–16, TestFlight основной, ручные owner actions |
| Тесты/риски/этапы/критерии | Разделы17–22; результаты тестов не выдуманы |
| Existing components | Не изменены; никаких builds/install/commits/PR |
| Файловый результат | Только docs/ios-port-plan.md; Задача.txt сверяется с исходным SHA256 |
| Git status/diff | Обе команды выполнены после записи документа и завершились ошибкой: папка не Git-репозиторий (.git отсутствует). Это ограничение проверки, не «чистый git diff» |

Первоначальный SHA256 Задача.txt: 7d6d62d8a9c80744a3f916e2af07998f2947a0a06c8352aea2b94cfbd9318652. Финальная проверка подтвердила тот же hash и ровно один новый файл — docs/ios-port-plan.md. Каталог ios/ отсутствует. Проверены24 обязательных раздела,29 Markdown-таблиц,6 Mermaid-диаграмм,210 ссылок с диапазонами строк и50 официальных внешних источников; незакрытых блоков и несогласованных колонок таблиц не обнаружено. Для обычной Git-проверки владелец позже может поместить документ в checkout исследованного commit и выполнить git status --short / git diff -- docs/ios-port-plan.md; в этом аудите checkout не создавался.

## 24. Приложение: ссылки на исходный код и документацию

### 24.1. Реестр исходников

Все ссылки в этом подразделе и в основном тексте ведут на один commit f6ca87d896ebcd8bea3eebfcb203d110a15c2d64. Интервал после двоеточия — реальные номера строк этой ревизии. В таблице ниже приведён inventory текстовых файлов, а узкие интервалы в разделах выше показывают доказательства конкретных выводов. Binary assets не включены в текстовый inventory.

| Текстовый файл | Строк в прочитанном представлении |
|---|---:|
| [.gitattributes](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/.gitattributes) | 8 |
| [.gitignore](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/.gitignore) | 25 |
| [LICENSE](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/LICENSE) | 341 |
| [README.md](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/README.md) | 199 |
| [android/app/build.gradle.kts](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/build.gradle.kts) | 62 |
| [android/app/src/main/AndroidManifest.xml](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/AndroidManifest.xml) | 49 |
| [android/app/src/main/assets/index.html](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/assets/index.html) | 361 |
| [android/app/src/main/java/com/magercam/app/AudioCapture.kt](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/AudioCapture.kt) | 97 |
| [android/app/src/main/java/com/magercam/app/BgSegmenter.kt](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/BgSegmenter.kt) | 79 |
| [android/app/src/main/java/com/magercam/app/CameraEngine.kt](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraEngine.kt) | 977 |
| [android/app/src/main/java/com/magercam/app/CameraService.kt](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/CameraService.kt) | 134 |
| [android/app/src/main/java/com/magercam/app/Core.kt](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Core.kt) | 437 |
| [android/app/src/main/java/com/magercam/app/Discovery.kt](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Discovery.kt) | 73 |
| [android/app/src/main/java/com/magercam/app/GlRenderer.kt](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/GlRenderer.kt) | 547 |
| [android/app/src/main/java/com/magercam/app/MainActivity.kt](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/MainActivity.kt) | 861 |
| [android/app/src/main/java/com/magercam/app/Recorder.kt](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Recorder.kt) | 227 |
| [android/app/src/main/java/com/magercam/app/Settings.kt](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/Settings.kt) | 78 |
| [android/app/src/main/java/com/magercam/app/StreamServer.kt](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/StreamServer.kt) | 444 |
| [android/app/src/main/java/com/magercam/app/VideoEncoder.kt](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/java/com/magercam/app/VideoEncoder.kt) | 206 |
| [android/app/src/main/res/drawable/ic_launcher_fg.xml](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/res/drawable/ic_launcher_fg.xml) | 9 |
| [android/app/src/main/res/drawable/ic_stat.xml](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/res/drawable/ic_stat.xml) | 6 |
| [android/app/src/main/res/mipmap-anydpi-v26/ic_launcher.xml](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/res/mipmap-anydpi-v26/ic_launcher.xml) | 6 |
| [android/app/src/main/res/values/colors.xml](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/app/src/main/res/values/colors.xml) | 5 |
| [android/build.gradle.kts](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/build.gradle.kts) | 5 |
| [android/gradle.properties](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/gradle.properties) | 6 |
| [android/gradle/wrapper/gradle-wrapper.properties](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/gradle/wrapper/gradle-wrapper.properties) | 8 |
| [android/gradlew](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/gradlew) | 253 |
| [android/gradlew.bat](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/gradlew.bat) | 95 |
| [android/settings.gradle.kts](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/android/settings.gradle.kts) | 17 |
| [docs/PROTOCOL.md](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/docs/PROTOCOL.md) | 179 |
| [docs/images/banner.svg](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/docs/images/banner.svg) | 30 |
| [install-apk.bat](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/install-apk.bat) | 27 |
| [install-obs-plugin.bat](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/install-obs-plugin.bat) | 32 |
| [install-windows.bat](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/install-windows.bat) | 24 |
| [obs-plugin/build.sh](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/build.sh) | 21 |
| [obs-plugin/setup-deps.sh](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/setup-deps.sh) | 29 |
| [obs-plugin/src/magercam.c](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/src/magercam.c) | 1879 |
| [tools/fake-phone.js](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/tools/fake-phone.js) | 196 |
| [tools/obs-test.js](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/tools/obs-test.js) | 111 |
| [uninstall-obs-plugin.bat](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/uninstall-obs-plugin.bat) | 5 |
| [uninstall-windows.bat](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/uninstall-windows.bat) | 13 |
| [windows/build.sh](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/build.sh) | 28 |
| [windows/client/app.manifest](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/app.manifest) | 23 |
| [windows/client/audio.c](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/audio.c) | 135 |
| [windows/client/audio.h](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/audio.h) | 18 |
| [windows/client/client.rc](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/client.rc) | 29 |
| [windows/client/color.c](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/color.c) | 236 |
| [windows/client/color.h](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/color.h) | 25 |
| [windows/client/decoder.c](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/decoder.c) | 263 |
| [windows/client/decoder.h](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/decoder.h) | 16 |
| [windows/client/main.c](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/main.c) | 2074 |
| [windows/client/net.c](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/net.c) | 696 |
| [windows/client/net.h](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/net.h) | 60 |
| [windows/client/pair.c](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/pair.c) | 290 |
| [windows/client/pair.h](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/pair.h) | 20 |
| [windows/client/vcam.c](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/vcam.c) | 139 |
| [windows/client/vcam.h](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/client/vcam.h) | 22 |
| [windows/common/mc_qr.h](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/common/mc_qr.h) | 340 |
| [windows/common/mcv_nv12.h](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/common/mcv_nv12.h) | 122 |
| [windows/common/mcv_shared.h](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/common/mcv_shared.h) | 55 |
| [windows/common/mcv_vcam_api.h](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/common/mcv_vcam_api.h) | 68 |
| [windows/install.ps1](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/install.ps1) | 116 |
| [windows/tools/vcamtest.c](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/tools/vcamtest.c) | 263 |
| [windows/vcam/vcam.cpp](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/vcam/vcam.cpp) | 1298 |
| [windows/vcam/vcam.def](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/windows/vcam/vcam.def) | 7 |

Сборочное окружение существующего OBS-плагина привязано в setup-deps.sh к headers libobs32.1.0 и FFmpeg7.1.1; import libraries берутся из установленного OBS (avcodec-61/avutil-59), default llvm-mingw tag20261020. Это зависимости сборочного скрипта, не доказательство установленной версии OBS у пользователя. [obs-plugin/setup-deps.sh:1-27](https://github.com/Magerko/MagerCam/blob/f6ca87d896ebcd8bea3eebfcb203d110a15c2d64/obs-plugin/setup-deps.sh#L1-L27).

### 24.2. Официальные внешние источники

**Все перечисленные источники проверены 28.10.2026.** Для Apple DocC страниц при необходимости прочитано JSON/Markdown представление официальной документации, поскольку HTML может содержать только JavaScript shell. Availability проверялась по metadata соответствующих API; отдельные claims о runtime явно оставлены экспериментами. Mutable pricing, SDK minima, runner inventory и distribution rules нужно повторно проверить перед настройкой CI/публикацией.

| ID | Источник | Что подтверждает |
|---|---|---|
| A01 | [AVCaptureDevice.Format][A01] | Форматы камеры; runtime capabilities |
| A02 | [activeVideoMinFrameDuration][A02] | Cadence и допустимые frame durations |
| A03 | [AVCaptureDevice][A03] | Focus/exposure/WB/zoom/torch capabilities |
| A04 | [lensPosition][A04] | Направление шкалы и отсутствие гарантии infinity |
| A05 | [setFocusModeLocked][A05] | Configuration lock и focus timestamp |
| A06 | [VTCompressionSession API][A06] | Создание/кодирование/lifecycle |
| A07 | [CMVideoFormatDescriptionGetH264ParameterSetAtIndex][A07] | Parameter sets и длина NAL prefix |
| A08 | [CMFormatDescription.nalUnitHeaderLength][A08] | Length-prefixed AVC/HEVC |
| A09 | [kVTCompressionPropertyKey_AllowFrameReordering][A09] | Необходимость явного запрета reorder |
| A10 | [EnableLowLatencyRateControl][A10] | Особый low-latency режим и infinite GOP |
| A11 | [Encoding video for low-latency conferencing][A11] | Properties, supported values, runtime checks |
| A12 | [AVCaptureSession.synchronizationClock][A12] | Общий capture clock, iOS15.4+ |
| A13 | [AVAudioConverter][A13] | PCM format/rate conversion, iOS9+ |
| A14 | [setPreferredSampleRate][A14] | Preferred vs actual sample rate |
| A15 | [VNGeneratePersonSegmentationRequest][A15] | Person segmentation, iOS15+ |
| A16 | [TN3179: Understanding local network privacy][A16] | TCP/UDP/Bonjour permission и multicast entitlement |
| A17 | [Requesting authorization to capture and save media][A17] | Camera/mic/Photos privacy |
| A18 | [videoDeviceNotAvailableInBackground][A18] | Ограничение background camera |
| A19 | [isMultitaskingCameraAccessSupported][A19] | Условная поддержка multitasking camera |
| A20 | [Creating a camera experience for the Lock Screen][A20] | Lock Screen extension не сетевой background server |
| A21 | [App Review Guidelines][A21] | Назначение background modes и согласие на capture |
| A22 | [Responding to power notifications][A22] | Low Power Mode/thermalState |
| A23 | [AVCaptureDevice.systemPressureState][A23] | Давление на camera resources, iOS11.1+ |
| A24 | [UIApplication.isIdleTimerDisabled][A24] | Управление автоматическим idle timer |
| A25 | [Handling audio interruptions][A25] | Interruption lifecycle/shouldResume |
| A26 | [AVAudioSession.routeChangeNotification][A26] | Смена route/формата |
| A27 | [Xcode system requirements][A27] | Xcode/macOS/SDK/Swift matrix |
| A28 | [Updated SDK minimum requirements, February 3 2026][A28] | С28.04.2026 iOS SDK26+ для upload |
| A29 | [App Store Connect API][A29] | Выдача ключей и роли |
| A30 | [Certificates overview][A30] | Development/Distribution identity |
| A31 | [Apple Developer Program enrollment][A31] | Вступление/2FA/стоимость |
| A32 | [TestFlight overview][A32] | 90дней, internal/external limits/review |
| A33 | [Enabling Developer Mode on a device][A33] | TestFlight исключение и pairing ограничения |
| A34 | [Upload builds][A34] | Способы upload и processing |
| A35 | [Create a certificate signing request][A35] | Официальный Keychain CSR workflow |
| A36 | [Create an Ad Hoc provisioning profile][A36] | App ID/cert/registered devices/profile |
| A37 | [Devices overview][A37] | Device quota и обновление списка |
| A38 | [Distributing your app for beta testing and releases][A38] | Export variants и OTA manifest |
| A39 | [Distributing your app to registered devices][A39] | Install IPA, signing и Developer Mode |
| A40 | [About your developer account][A40] | Free/paid capabilities и provisioning limits |
| A41 | [Distributing your app from your website][A41] | Alternative web distribution не generic IPA install |
| A42 | [com.apple.developer.networking.multicast][A42] | Entitlement для multicast/broadcast, требует разрешения Apple |
| A43 | [NSLocalNetworkUsageDescription][A43] | Объяснение пользователю LAN-доступа |
| G01 | [XcodeGen 2.46.0 releases][G01] | Кандидат pinned generator; не установлен |
| G02 | [Pinned macOS26 runner image manifest][G02] | Исторически проверенный inventory, не promise текущей VM |
| G03 | [GitHub Actions workflow syntax / hosted runners][G03] | Runner labels/architectures |
| G04 | [Signing Xcode applications][G04] | Secrets/P12/profiles/temporary keychain |
| G05 | [GitHub Actions billing and usage][G05] | Public/private usage и quotas |
| G06 | [Actions runner pricing][G06] | Тарифы, проверены28.10.2026 |
| C01 | [Codemagic: Signing iOS apps][C01] | Альтернативный macOS CI и signing assets |

Дополнительно проверена availability [AVCaptureDevice.RotationCoordinator](https://developer.apple.com/documentation/avfoundation/avcapturedevice/rotationcoordinator) — iOS17+, [NWListener](https://developer.apple.com/documentation/network/nwlistener) и [NWPathMonitor](https://developer.apple.com/documentation/network/nwpathmonitor) — iOS12+. Эти версии подтверждают выбранный minimum API baseline, но не доказывают работоспособность будущего приложения.

### 24.3. Ограничения доказательств

В отчёте нет результатов xcodebuild, unit/UI tests, реального encode на iPhone, packet capture с Android, запуска OBS/Windows, измерения latency/drift, регистрации Apple Account, подписания или установки IPA. Все эти действия определены как будущие эксперименты с явными pass/fail criteria. Не прочитанные binary assets отмечены в разделе2; текстовое исследование доступно полностью.

Аудит завершает задачу анализа. **Не начинать этап0, не создавать ios/ и не менять существующие компоненты до явного подтверждения владельца.**


[A01]: <https://developer.apple.com/documentation/avfoundation/avcapturedevice/format> "AVCaptureDevice.Format"
[A02]: <https://developer.apple.com/documentation/avfoundation/avcapturedevice/activevideominframeduration> "activeVideoMinFrameDuration"
[A03]: <https://developer.apple.com/documentation/avfoundation/avcapturedevice> "AVCaptureDevice"
[A04]: <https://developer.apple.com/documentation/avfoundation/avcapturedevice/lensposition> "lensPosition"
[A05]: <https://developer.apple.com/documentation/avfoundation/avcapturedevice/setfocusmodelocked(lensposition:completionhandler:)> "setFocusModeLocked"
[A06]: <https://developer.apple.com/documentation/videotoolbox/vtcompressionsession-api-collection> "VTCompressionSession API"
[A07]: <https://developer.apple.com/documentation/coremedia/cmvideoformatdescriptiongeth264parametersetatindex(_:parametersetindex:parametersetpointerout:parametersetsizeout:parametersetcountout:nalunitheaderlengthout:)> "CMVideoFormatDescriptionGetH264ParameterSetAtIndex"
[A08]: <https://developer.apple.com/documentation/coremedia/cmformatdescription/nalunitheaderlength> "CMFormatDescription.nalUnitHeaderLength"
[A09]: <https://developer.apple.com/documentation/videotoolbox/kvtcompressionpropertykey_allowframereordering> "kVTCompressionPropertyKey_AllowFrameReordering"
[A10]: <https://developer.apple.com/documentation/videotoolbox/kvtvideoencoderspecification_enablelowlatencyratecontrol> "EnableLowLatencyRateControl"
[A11]: <https://developer.apple.com/documentation/videotoolbox/encoding-video-for-low-latency-conferencing> "Encoding video for low-latency conferencing"
[A12]: <https://developer.apple.com/documentation/avfoundation/avcapturesession/synchronizationclock> "AVCaptureSession.synchronizationClock"
[A13]: <https://developer.apple.com/documentation/avfaudio/avaudioconverter> "AVAudioConverter"
[A14]: <https://developer.apple.com/documentation/avfaudio/avaudiosession/setpreferredsamplerate(_:)> "setPreferredSampleRate"
[A15]: <https://developer.apple.com/documentation/vision/vngeneratepersonsegmentationrequest> "VNGeneratePersonSegmentationRequest"
[A16]: <https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy> "TN3179: Understanding local network privacy"
[A17]: <https://developer.apple.com/documentation/avfoundation/requesting-authorization-to-capture-and-save-media> "Requesting authorization to capture and save media"
[A18]: <https://developer.apple.com/documentation/avfoundation/avcapturesession/interruptionreason/videodevicenotavailableinbackground> "videoDeviceNotAvailableInBackground"
[A19]: <https://developer.apple.com/documentation/avfoundation/avcapturesession/ismultitaskingcameraaccesssupported> "isMultitaskingCameraAccessSupported"
[A20]: <https://developer.apple.com/documentation/lockedcameracapture/creating-a-camera-experience-for-the-lock-screen> "Creating a camera experience for the Lock Screen"
[A21]: <https://developer.apple.com/app-store/review/guidelines/> "App Review Guidelines"
[A22]: <https://developer.apple.com/documentation/xcode/responding-to-power-notifications> "Responding to power notifications"
[A23]: <https://developer.apple.com/documentation/avfoundation/avcapturedevice/systempressurestate> "AVCaptureDevice.systemPressureState"
[A24]: <https://developer.apple.com/documentation/uikit/uiapplication/isidletimerdisabled> "UIApplication.isIdleTimerDisabled"
[A25]: <https://developer.apple.com/documentation/avfaudio/handling-audio-interruptions> "Handling audio interruptions"
[A26]: <https://developer.apple.com/documentation/avfaudio/avaudiosession/routechangenotification> "AVAudioSession.routeChangeNotification"
[A27]: <https://developer.apple.com/xcode/system-requirements/> "Xcode system requirements"
[A28]: <https://developer.apple.com/news/?id=ueeok6yw> "Updated SDK minimum requirements, February 3 2026"
[A29]: <https://developer.apple.com/help/app-store-connect/get-started/app-store-connect-api> "App Store Connect API"
[A30]: <https://developer.apple.com/help/account/certificates/certificates-overview> "Certificates overview"
[A31]: <https://developer.apple.com/programs/enroll/> "Apple Developer Program enrollment"
[A32]: <https://developer.apple.com/help/app-store-connect/test-a-beta-version/testflight-overview/> "TestFlight overview"
[A33]: <https://developer.apple.com/documentation/xcode/enabling-developer-mode-on-a-device> "Enabling Developer Mode on a device"
[A34]: <https://developer.apple.com/help/app-store-connect/manage-builds/upload-builds> "Upload builds"
[A35]: <https://developer.apple.com/help/account/certificates/create-a-certificate-signing-request/> "Create a certificate signing request"
[A36]: <https://developer.apple.com/help/account/provisioning-profiles/create-an-ad-hoc-provisioning-profile> "Create an Ad Hoc provisioning profile"
[A37]: <https://developer.apple.com/help/account/devices/devices-overview/> "Devices overview"
[A38]: <https://developer.apple.com/documentation/xcode/distributing-your-app-for-beta-testing-and-releases> "Distributing your app for beta testing and releases"
[A39]: <https://developer.apple.com/documentation/xcode/distributing-your-app-to-registered-devices> "Distributing your app to registered devices"
[A40]: <https://developer.apple.com/help/account/basics/about-your-developer-account> "About your developer account"
[A41]: <https://developer.apple.com/documentation/marketplacekit/distributing-your-app-from-your-website> "Distributing your app from your website"
[G01]: <https://github.com/yonaskolb/XcodeGen/releases/tag/2.46.0> "XcodeGen 2.46.0 releases"
[G02]: <https://github.com/actions/runner-images/blob/3b5e6b447abb7eadff4a19ac472e806b1e063f42/images/macos/macos-26-Readme.md> "Pinned macOS26 runner image manifest"
[G03]: <https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax> "GitHub Actions workflow syntax / hosted runners"
[G04]: <https://docs.github.com/en/actions/how-tos/deploy/deploy-to-third-party-platforms/sign-xcode-applications> "Signing Xcode applications"
[G05]: <https://docs.github.com/en/actions/concepts/billing-and-usage> "GitHub Actions billing and usage"
[G06]: <https://docs.github.com/en/billing/reference/actions-runner-pricing> "Actions runner pricing"
[C01]: <https://docs.codemagic.io/yaml-code-signing/signing-ios/> "Codemagic: Signing iOS apps"

[A42]: <https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.developer.networking.multicast> "com.apple.developer.networking.multicast"

[A43]: <https://developer.apple.com/documentation/bundleresources/information-property-list/nslocalnetworkusagedescription> "NSLocalNetworkUsageDescription"
